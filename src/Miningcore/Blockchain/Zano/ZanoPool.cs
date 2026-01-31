using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.IO;
using Miningcore.Blockchain.Zano.StratumRequests;
using Miningcore.Blockchain.Zano.StratumResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Notifications.Messages;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Zano;

[CoinFamily(CoinFamily.Zano)]
public class ZanoPool : PoolBase
{
    public ZanoPool(IComponentContext ctx,
        JsonSerializerSettings serializerSettings,
        IConnectionFactory cf,
        IStatsRepository statsRepo,
        IMapper mapper,
        IMasterClock clock,
        IMessageBus messageBus,
        RecyclableMemoryStreamManager rmsm,
        NicehashService nicehashService) :
        base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
    {
    }

    private long currentJobId;

    private ZanoJobManager manager;
    private string minerAlgo;
    private ZanoCoinTemplate coin;

    #region // Protocol V2 handlers - https://github.com/nicehash/Specifications/blob/master/EthereumStratum_NiceHash_v1.0.0.txt

    private async Task OnSubscribeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.Other, "missing request id");

        var requestParams = request.ParamsAs<string[]>();

        if(requestParams == null || requestParams.Length < 2 || requestParams.Any(string.IsNullOrEmpty))
            throw new StratumException(StratumError.MinusOne, "invalid request");

        context.UserAgent = requestParams.FirstOrDefault()?.Trim();

        var data = new object[]
        {
            new object[]
            {
                ZanoStratumMethods.MiningNotify,
                connection.ConnectionId,
                requestParams[1]
            },
            "0000000000000000"
        }
        .ToArray();

        // Nicehash's stupid validator insists on "error" property present
        // in successful responses which is a violation of the JSON-RPC spec
        var response = new JsonRpcResponse<object[]>(data, request.Id);

        if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
        {
            response.Extra = new Dictionary<string, object>();
            response.Extra["error"] = null;
        }

        await connection.RespondAsync(response);

        // setup worker context
        context.IsSubscribed = true;
    }

    private async Task OnAuthorizeAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var requestParams = request.ParamsAs<string[]>();
        var workerValue = requestParams?.Length > 0 ? requestParams[0] : "0";
        var password = requestParams?.Length > 1 ? requestParams[1] : null;
        var passParts = password?.Split(PasswordControlVarsSeparator);

        // extract worker/miner
        var workerParts = workerValue?.Split('.');
        context.Miner = workerParts?.Length > 0 ? workerParts[0].Trim() : null;
        context.Worker = workerParts?.Length > 1 ? workerParts[1].Trim() : "0";

        var addressToValidate = context.Miner;

        // extract paymentid
        var index = context.Miner.IndexOf('#');
        if(index != -1)
        {
            var paymentId = context.Miner[(index + 1)..].Trim();

            // validate
            if(!string.IsNullOrEmpty(paymentId) && paymentId.Length != ZanoConstants.PaymentIdHexLength)
                throw new StratumException(StratumError.MinusOne, "invalid payment id");

            // re-append to address
            addressToValidate = context.Miner[..index].Trim();
            context.Miner = addressToValidate + PayoutConstants.PayoutInfoSeperator + paymentId;
        }

        // validate login
        var result = manager.ValidateAddress(addressToValidate);

        context.IsAuthorized = result;

        // Nicehash's stupid validator insists on "error" property present
        // in successful responses which is a violation of the JSON-RPC spec
        // [We miss you Oliver <3 We miss you so much <3 Respect the goddamn standards Nicehash :(]
        var response = new JsonRpcResponse<object>(context.IsAuthorized, request.Id);

        if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
        {
            response.Extra = new Dictionary<string, object>();
            response.Extra["error"] = null;
        }

        // respond
        await connection.RespondAsync(response);

        if(context.IsAuthorized)
        {
            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Nicehash support
            var nicehashDiff = await GetNicehashStaticMinDiff(context, coin.Name, coin.GetAlgorithmName());

            if(nicehashDiff.HasValue)
            {
                if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                    staticDiff = nicehashDiff;
                }

                else
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Setting static difficulty of {staticDiff.Value}");
            }

            var job = CreateWorkerJob(connection);

            await connection.NotifyAsync(ZanoStratumMethods.SetDifficulty, new object[] { context.Difficulty });
            await connection.NotifyAsync(ZanoStratumMethods.MiningNotify, job);

            // log association
            if(!string.IsNullOrEmpty(context.Worker))
                logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {context.Worker}@{context.Miner}");
            else
                logger.Info(() => $"[{connection.ConnectionId}] Authorized miner {context.Miner}");
        }

        else
        {
            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {context.Miner} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    private async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        try
        {
            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            // validate worker
            if(!context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "unauthorized");

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

            if(requestAge > maxShareAge)
            {
                logger.Warn(() => $"[{connection.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            if(requestParams == null || requestParams.Length < 3 || requestParams.Any(string.IsNullOrEmpty))
                throw new StratumException(StratumError.MinusOne, "invalid request");

            // recognize activity
            context.LastActivity = clock.Now;

            ZanoWorkerJob job;

            lock(context)
            {
                var jobId = requestParams[1];

                if((job = context.GetJob(jobId)) == null)
                    throw new StratumException(StratumError.MinusOne, "invalid jobid");
            }

            var submitRequest = new ZanoSubmitShareRequest
            {
                JobId = requestParams[1].StripHexPrefix(),
                Nonce = requestParams[2].StripHexPrefix(),
                Hash = requestParams[4].StripHexPrefix()
            };

            // dupe check
            if(!job.Submissions.TryAdd(submitRequest.Nonce, true))
                throw new StratumException(StratumError.MinusOne, "duplicate share");

            // submit
            var share = await manager.SubmitShareAsync(connection, submitRequest, job, ct);

            // Nicehash's stupid validator insists on "error" property present
            // in successful responses which is a violation of the JSON-RPC spec
            // [Respect the goddamn standards Nicehack :(]
            var response = new JsonRpcResponse<object>(new ZanoResponseBase(), request.Id);

            if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
            {
                response.Extra = new Dictionary<string, object>();
                response.Extra["error"] = null;
            }

            await connection.RespondAsync(response);

            // publish
            messageBus.SendMessage(share);

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * coin.ShareMultiplier, 3)}");

            // update pool stats
            if(share.IsBlockCandidate)
                poolStats.LastPoolBlockTime = clock.Now;

            // update client stats
            context.Stats.ValidShares++;

            await UpdateVarDiffAsync(connection, false, ct);
        }

        catch(StratumException ex)
        {
            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

            // update client stats
            context.Stats.InvalidShares++;
            logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message} [{context.UserAgent}]");

            // banning
            ConsiderBan(connection, context, poolConfig.Banning);

            throw;
        }
    }

    #endregion // Protocol V2 handlers

    #region // Protocol V1 handlers - https://github.com/sammy007/open-ethereum-pool/blob/master/docs/STRATUM.md

    private async Task OnLoginAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var loginRequest = request.ParamsAs<ZanoLoginRequest>();

        if(string.IsNullOrEmpty(loginRequest?.Login))
            throw new StratumException(StratumError.MinusOne, "missing login");

        // extract worker/miner/paymentid
        var split = loginRequest.Login.Split('.');
        context.Miner = split[0].Trim();
        context.Worker = split.Length > 1 ? split[1].Trim() : null;
        context.UserAgent = loginRequest.UserAgent?.Trim();

        var addressToValidate = context.Miner;

        // extract paymentid
        var index = context.Miner.IndexOf('#');
        if(index != -1)
        {
            var paymentId = context.Miner[(index + 1)..].Trim();

            // validate
            if(!string.IsNullOrEmpty(paymentId) && paymentId.Length != ZanoConstants.PaymentIdHexLength)
                throw new StratumException(StratumError.MinusOne, "invalid payment id");

            // re-append to address
            addressToValidate = context.Miner[..index].Trim();
            context.Miner = addressToValidate + PayoutConstants.PayoutInfoSeperator + paymentId;
        }

        // validate login
        var result = manager.ValidateAddress(addressToValidate);

        context.IsSubscribed = result;
        context.IsAuthorized = result;

        if(context.IsAuthorized)
        {
            // extract control vars from password
            var passParts = loginRequest.Password?.Split(PasswordControlVarsSeparator);
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Nicehash support
            var nicehashDiff = await GetNicehashStaticMinDiff(context, manager.Coin.Name, manager.Coin.GetAlgorithmName());

            if(nicehashDiff.HasValue)
            {
                if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                    staticDiff = nicehashDiff;
                }

                else
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Static difficulty set to {staticDiff.Value}");
            }

            // Nicehash's stupid validator insists on "error" property present
            // in successful responses which is a violation of the JSON-RPC spec
            // [Respect the goddamn standards Nicehack :(]
            var response = new JsonRpcResponse<object>(true, request.Id);

            if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
            {
                response.Extra = new Dictionary<string, object>();
                response.Extra["error"] = null;
            }

            await connection.RespondAsync(response);

            // log association
            if(!string.IsNullOrEmpty(context.Worker))
                logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {context.Worker}@{context.Miner}");
            else
                logger.Info(() => $"[{connection.ConnectionId}] Authorized miner {context.Miner}");
        }

        else
        {
            await connection.RespondErrorAsync(StratumError.MinusOne, "invalid login", request.Id);

            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {context.Miner} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    private async Task OnSubmitLoginAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var requestParams = request.ParamsAs<string[]>();

        if(requestParams == null || requestParams.Length < 2 || requestParams.Any(string.IsNullOrEmpty))
            throw new StratumException(StratumError.MinusOne, "invalid request");

        // extract worker/miner/paymentid
        var workerValue = requestParams?.Length > 0 ? requestParams[0] : null;
        
        if(string.IsNullOrEmpty(workerValue))
            throw new StratumException(StratumError.MinusOne, "missing login");

        var split = workerValue.Split('.');
        context.Miner = split[0].Trim();
        context.Worker = split.Length > 1 ? split[1].Trim() : null;
        var password = requestParams?.Length > 1 ? requestParams[1] : null;
        context.UserAgent = requestParams?.Length > 2 ? requestParams[2].Trim() : null;

        var addressToValidate = context.Miner;

        // extract paymentid
        var index = context.Miner.IndexOf('#');
        if(index != -1)
        {
            var paymentId = context.Miner[(index + 1)..].Trim();

            // validate
            if(!string.IsNullOrEmpty(paymentId) && paymentId.Length != ZanoConstants.PaymentIdHexLength)
                throw new StratumException(StratumError.MinusOne, "invalid payment id");

            // re-append to address
            addressToValidate = context.Miner[..index].Trim();
            context.Miner = addressToValidate + PayoutConstants.PayoutInfoSeperator + paymentId;
        }

        // validate login
        var result = manager.ValidateAddress(addressToValidate);

        context.IsSubscribed = result;
        context.IsAuthorized = result;

        if(context.IsAuthorized)
        {
            // extract control vars from password
            var passParts = password?.Split(PasswordControlVarsSeparator);
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Nicehash support
            var nicehashDiff = await GetNicehashStaticMinDiff(context, manager.Coin.Name, manager.Coin.GetAlgorithmName());

            if(nicehashDiff.HasValue)
            {
                if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                    staticDiff = nicehashDiff;
                }

                else
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Static difficulty set to {staticDiff.Value}");
            }

            // Nicehash's stupid validator insists on "error" property present
            // in successful responses which is a violation of the JSON-RPC spec
            // [Respect the goddamn standards Nicehack :(]
            var response = new JsonRpcResponse<object>(true, request.Id);

            if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
            {
                response.Extra = new Dictionary<string, object>();
                response.Extra["error"] = null;
            }

            await connection.RespondAsync(response);

            // log association
            if(!string.IsNullOrEmpty(context.Worker))
                logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {context.Worker}@{context.Miner}");
            else
                logger.Info(() => $"[{connection.ConnectionId}] Authorized miner {context.Miner}");
        }

        else
        {
            await connection.RespondErrorAsync(StratumError.MinusOne, "invalid login", request.Id);

            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {context.Miner} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    private async Task OnGetJobAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        // authorized worker
        if(!context.IsAuthorized)
            throw new StratumException(StratumError.MinusOne, "unauthorized");

        var job = CreateWorkerJob(connection);

        // Nicehash's stupid validator insists on "error" property present
        // in successful responses which is a violation of the JSON-RPC spec
        // [Respect the goddamn standards Nicehack :(]
        var response = new JsonRpcResponse<object[]>(job, request.Id);

        if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
        {
            response.Extra = new Dictionary<string, object>();
            response.Extra["error"] = null;
        }

        // respond
        await connection.RespondAsync(response);
    }

    private async Task OnSubmitWorkAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        try
        {
            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            // validate worker
            if(!context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "unauthorized");

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

            if(requestAge > maxShareAge)
            {
                logger.Warn(() => $"[{connection.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            if(requestParams == null || requestParams.Length < 3 || requestParams.Any(string.IsNullOrEmpty))
                throw new StratumException(StratumError.MinusOne, "invalid request");

            // recognize activity
            context.LastActivity = clock.Now;

            ZanoWorkerJob job;

            lock(context)
            {
                var jobId = requestParams[1];

                if((job = context.GetJob(jobId)) == null)
                    throw new StratumException(StratumError.MinusOne, "invalid jobid");
            }

            var submitRequest = new ZanoSubmitShareRequest
            {
                JobId = requestParams[1].StripHexPrefix(),
                Nonce = requestParams[0].StripHexPrefix(),
                Hash = requestParams[2].StripHexPrefix()
            };

            // dupe check
            if(!job.Submissions.TryAdd(submitRequest.Nonce, true))
                throw new StratumException(StratumError.MinusOne, "duplicate share");

            // submit
            var share = await manager.SubmitShareAsync(connection, submitRequest, job, ct);

            // Nicehash's stupid validator insists on "error" property present
            // in successful responses which is a violation of the JSON-RPC spec
            // [Respect the goddamn standards Nicehack :(]
            var response = new JsonRpcResponse<object>(new ZanoResponseBase(), request.Id);

            if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
            {
                response.Extra = new Dictionary<string, object>();
                response.Extra["error"] = null;
            }

            await connection.RespondAsync(response);

            // publish
            messageBus.SendMessage(share);

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty * coin.ShareMultiplier, 3)}");

            // update pool stats
            if(share.IsBlockCandidate)
                poolStats.LastPoolBlockTime = clock.Now;

            // update client stats
            context.Stats.ValidShares++;

            await UpdateVarDiffAsync(connection, false, ct);
        }

        catch(StratumException ex)
        {
            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

            // update client stats
            context.Stats.InvalidShares++;
            logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message} [{context.UserAgent}]");

            // banning
            ConsiderBan(connection, context, poolConfig.Banning);

            throw;
        }
    }

    #endregion // Protocol V1 handlers

    private object[] CreateWorkerJob(StratumConnection connection)
    {
        var context = connection.ContextAs<ZanoWorkerContext>();
        var job = manager.PrepareWorkerJob(context.Difficulty);

        logger.Debug(() => $"JobId: {job.Id} - Target: {job.Target} - Height: {job.Height} - SeedHash: {job.SeedHash}");

        // should never happen
        if(string.IsNullOrEmpty(job.Id) || string.IsNullOrEmpty(job.Target))
            return null;

        var result = new object[]
        {
            job.Id,
            job.SeedHash,
            job.Target,
            job.Height
        };

        // update context
        lock(context)
        {
            context.AddJob(job, 4);
        }

        return result;
    }

    private string NextJobId()
    {
        return Interlocked.Increment(ref currentJobId).ToString(CultureInfo.InvariantCulture);
    }

    private async Task OnNewJobAsync()
    {
        logger.Info(() => "Broadcasting jobs");

        await Guard(() => ForEachMinerAsync(async (connection, ct) =>
        {
            var context = connection.ContextAs<ZanoWorkerContext>();

            var job = CreateWorkerJob(connection);

            switch(context.ProtocolVersion)
            {
                case 1:
                    // Nicehash's stupid validator insists on "error" property present
                    // in successful responses which is a violation of the JSON-RPC spec
                    // [Respect the goddamn standards Nicehack :(]
                    var response = new JsonRpcResponse<object[]>(job);

                    if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
                    {
                        response.Extra = new Dictionary<string, object>();
                        response.Extra["error"] = null;
                    }

                    // notify
                    await connection.RespondAsync(response);
                    break;

                case 2:
                    // varDiff: if the client has a pending difficulty change, apply it now
                    if(context.ApplyPendingDifficulty())
                        await connection.NotifyAsync(ZanoStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    // notify
                    await connection.NotifyAsync(ZanoStratumMethods.MiningNotify, job);
                    break;
            }
        }));
    }

    #region Overrides

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<ZanoCoinTemplate>();

        base.Configure(pc, cc);
    }

    protected override async Task SetupJobManager(CancellationToken ct)
    {
        manager = ctx.Resolve<ZanoJobManager>();
        manager.Configure(poolConfig, clusterConfig);

        await manager.StartAsync(ct);

        if(poolConfig.EnableInternalStratum == true)
        {
            minerAlgo = GetMinerAlgo();

            disposables.Add(manager.Blocks
                .Select(_ => Observable.FromAsync(() =>
                    Guard(OnNewJobAsync,
                        ex=> logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                .Concat()
                .Subscribe(_ => { }, ex =>
                {
                    logger.Debug(ex, nameof(OnNewJobAsync));
                }));

            // start with initial blocktemplate
            await manager.Blocks.Take(1).ToTask(ct);
        }

        else
        {
            // keep updating NetworkStats
            disposables.Add(manager.Blocks.Subscribe());
        }
    }

    private string GetMinerAlgo()
    {
        switch(manager.Coin.Hash)
        {
            case CryptonightHashType.ProgPowZ:
                return "cn/wow"; // wownero specific change to include algo in job to miner
        }

        return null;
    }

    protected override async Task InitStatsAsync(CancellationToken ct)
    {
        await base.InitStatsAsync(ct);

        blockchainStats = manager.BlockchainStats;
    }

    protected override WorkerContextBase CreateWorkerContext()
    {
        return new ZanoWorkerContext();
    }

    protected void EnsureProtocolVersion(ZanoWorkerContext context, int version)
    {
        if(context.ProtocolVersion != version)
            throw new StratumException(StratumError.MinusOne, $"protocol mismatch");
    }

    protected override async Task OnRequestAsync(StratumConnection connection,
        Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<ZanoWorkerContext>();

        try
        {
            switch(request.Method)
            {
                // V2/Nicehash Stratum Methods
                case ZanoStratumMethods.Subscribe:
                    context.ProtocolVersion = 2;    // lock in protocol version

                    await OnSubscribeAsync(connection, tsRequest);
                    break;

                case ZanoStratumMethods.Authorize:
                    EnsureProtocolVersion(context, 2);

                    await OnAuthorizeAsync(connection, tsRequest);
                    break;

                case ZanoStratumMethods.SubmitShare:
                    EnsureProtocolVersion(context, 2);

                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;

                case ZanoStratumMethods.ExtraNonceSubscribe:
                    EnsureProtocolVersion(context, 2);

                    // Pretend to support it even though we actually do not. Some miners drop the connection upon receiving an error from this

                    // Nicehash's stupid validator insists on "error" property present
                    // in successful responses which is a violation of the JSON-RPC spec
                    // [We miss you Oliver <3 We miss you so much <3 Respect the goddamn standards Nicehash :(]
                    var responseSubscribe = new JsonRpcResponse<object>(true, request.Id);

                    if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
                    {
                        responseSubscribe.Extra = new Dictionary<string, object>();
                        responseSubscribe.Extra["error"] = null;
                    }

                    await connection.RespondAsync(responseSubscribe);
                    break;

                case ZanoStratumMethods.Login:
                    context.ProtocolVersion = 1;    // lock in protocol version

                    await OnLoginAsync(connection, tsRequest);
                    break;

                case ZanoStratumMethods.SubmitLogin:
                    context.ProtocolVersion = 1;    // lock in protocol version

                    await OnSubmitLoginAsync(connection, tsRequest);
                    break;

                case ZanoStratumMethods.GetWork:
                case ZanoStratumMethods.GetJob:
                    EnsureProtocolVersion(context, 1);

                    await OnGetJobAsync(connection, tsRequest);
                    break;

                case ZanoStratumMethods.Submit:
                    EnsureProtocolVersion(context, 2);

                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;

                case ZanoStratumMethods.SubmitWork:
                    EnsureProtocolVersion(context, 1);

                    await OnSubmitWorkAsync(connection, tsRequest, ct);
                    break;

                case ZanoStratumMethods.KeepAlive:
                    // recognize activity
                    context.LastActivity = clock.Now;
                    
                    // For some reasons, we would never send a reply here :/
                    // But the official XMRig documentation is explicit, we should reply: https://xmrig.com/docs/extensions/keepalive
                    // XMRig is such a gift, i wish more mining pool operators were like them and valued open-source, the same way the XMRig devs do

                    // Nicehash's stupid validator insists on "error" property present
                    // in successful responses which is a violation of the JSON-RPC spec
                    // [Respect the goddamn standards Nicehack :(]
                    var responseKeepAlive = new JsonRpcResponse<object>(new ZanoKeepAliveResponse(), request.Id);

                    if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
                    {
                        responseKeepAlive.Extra = new Dictionary<string, object>();
                        responseKeepAlive.Extra["error"] = null;
                    }

                    await connection.RespondAsync(responseKeepAlive);
                    break;

                case ZanoStratumMethods.SubmitHashrate:
                    // Nicehash's stupid validator insists on "error" property present
                    // in successful responses which is a violation of the JSON-RPC spec
                    // [We miss you Oliver <3 We miss you so much <3 Respect the goddamn standards Nicehash :(]
                    var responseSubmitHashrate = new JsonRpcResponse<object>(true, request.Id);

                    if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
                    {
                        responseSubmitHashrate.Extra = new Dictionary<string, object>();
                        responseSubmitHashrate.Extra["error"] = null;
                    }

                    await connection.RespondAsync(responseSubmitHashrate);
                    break;

                default:
                    logger.Debug(() => $"[{connection.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    await connection.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        catch(StratumException ex)
        {
            await connection.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
        }
    }

    public override double HashrateFromShares(double shares, double interval)
    {
        var result = shares / interval;

        if(coin.HashrateMultiplier.HasValue)
            result *= coin.HashrateMultiplier.Value;

        return result;
    }

    public override double ShareMultiplier => coin.ShareMultiplier;

    protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff, CancellationToken ct)
    {
        await base.OnVarDiffUpdateAsync(connection, newDiff, ct);

        if(connection.Context.ApplyPendingDifficulty())
        {
            var context = connection.ContextAs<ZanoWorkerContext>();

            var job = CreateWorkerJob(connection);

            switch(context.ProtocolVersion)
            {
                case 1:
                    // Nicehash's stupid validator insists on "error" property present
                    // in successful responses which is a violation of the JSON-RPC spec
                    // [Respect the goddamn standards Nicehack :(]
                    var response = new JsonRpcResponse<object[]>(job);

                    if(context.IsNicehash || poolConfig.EnableAsicBoost == true)
                    {
                        response.Extra = new Dictionary<string, object>();
                        response.Extra["error"] = null;
                    }

                    // re-send job
                    await connection.RespondAsync(response);
                    break;

                case 2:
                    await connection.NotifyAsync(ZanoStratumMethods.SetDifficulty, new object[] { context.Difficulty });

                    // re-send job
                    await connection.NotifyAsync(ZanoStratumMethods.MiningNotify, job);
                    break;
            }
        }
    }

    #endregion // Overrides
}
