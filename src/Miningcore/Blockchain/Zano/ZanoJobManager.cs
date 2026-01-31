using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using Autofac;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Zano.Configuration;
using Miningcore.Blockchain.Zano.DaemonRequests;
using Miningcore.Blockchain.Zano.DaemonResponses;
using Miningcore.Blockchain.Zano.StratumRequests;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Zano;

public class ZanoJobManager : JobManagerBase<ZanoJob>
{
    public ZanoJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(ctx, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);

        this.clock = clock;
    }

    private byte[] instanceId;
    private DaemonEndpointConfig[] daemonEndpoints;
    private RpcClient rpc;
    private RpcClient walletRpc;
    private readonly IMasterClock clock;
    private ZanoNetworkType networkType;
    private ZanoPoolConfigExtra extraPoolConfig;
    private ulong poolAddressBase58Prefix;
    private DaemonEndpointConfig[] walletDaemonEndpoints;
    private ZanoCoinTemplate coin;

    protected async Task<bool> UpdateJob(CancellationToken ct, string via = null, string json = null)
    {
        try
        {
            var response = string.IsNullOrEmpty(json) ? await GetBlockTemplateAsync(ct) : GetBlockTemplateFromJson(json);

            // may happen if daemon is currently not connected to peers
            if(response.Error != null)
            {
                logger.Warn(() => $"Unable to update job. Daemon responded with: {response.Error.Message} Code {response.Error.Code}");
                return false;
            }

            var blockTemplate = response.Response;
            var job = currentJob;
            var newHash = blockTemplate.Blob.HexToByteArray().AsSpan().Slice(ZanoConstants.BlobPrevHashOffset, 32).ToHexString();

            var isNew = job == null ||
                (job.BlockTemplate?.Height < blockTemplate.Height && newHash != job.PrevHash);

            if(isNew)
            {
                messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                if(via != null)
                    logger.Info(() => $"Detected new block {blockTemplate.Height} [{via}]");
                else
                    logger.Info(() => $"Detected new block {blockTemplate.Height}");

                var progpowHasher = await coin.ProgpowHasher.GetCacheAsync(logger, (int) blockTemplate.Height, ct);

                // init job
                job = new ZanoJob(blockTemplate, instanceId, coin, poolConfig, clusterConfig, newHash, progpowHasher);
                currentJob = job;

                // update stats
                BlockchainStats.LastNetworkBlockTime = clock.Now;
                BlockchainStats.BlockHeight = job.BlockTemplate.Height;
                BlockchainStats.NetworkDifficulty = job.BlockTemplate.Difficulty;
                BlockchainStats.NextNetworkTarget = "";
                BlockchainStats.NextNetworkBits = "";
            }

            else
            {
                if(via != null)
                    logger.Debug(() => $"Template update {blockTemplate.Height} [{via}]");
                else
                    logger.Debug(() => $"Template update {blockTemplate.Height}");
            }

            return isNew;
        }

        catch(OperationCanceledException)
        {
            // ignored
        }

        catch(Exception ex)
        {
            logger.Error(ex, () => $"Error during {nameof(UpdateJob)}");
        }

        return false;
    }

    private async Task<RpcResponse<GetBlockTemplateResponse>> GetBlockTemplateAsync(CancellationToken ct)
    {
        var request = new GetBlockTemplateRequest
        {
            WalletAddress = poolConfig.Address,
            ReserveSize = ZanoConstants.ReserveSize
        };

        return await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger, ZanoCommands.GetBlockTemplate, ct, request);
    }

    private RpcResponse<GetBlockTemplateResponse> GetBlockTemplateFromJson(string json)
    {
        var result = JsonConvert.DeserializeObject<JsonRpcResponse>(json);

        return new RpcResponse<GetBlockTemplateResponse>(result.ResultAs<GetBlockTemplateResponse>());
    }

    private async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, ZanoCommands.GetInfo, ct);
        var info = response.Response;

        if(info != null)
        {
            var lowestHeight = info.Height;

            var totalBlocks = info.TargetHeight;
            var percent = (double) lowestHeight / totalBlocks * 100;

            logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {info.OutgoingConnectionsCount} peers");
        }
    }

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        try
        {
            var response = await rpc.ExecuteAsync(logger, ZanoCommands.GetInfo, ct);

            if(response.Error != null)
                logger.Warn(() => $"Error(s) refreshing network stats: {response.Error.Message} (Code {response.Error.Code})");

            if(response.Response != null)
            {
                var info = response.Response.ToObject<GetInfoResponse>();

                BlockchainStats.NetworkHashrate = info.Difficulty / ZanoConstants.DifficultyPowTarget;
                BlockchainStats.ConnectedPeers = info.OutgoingConnectionsCount + info.IncomingConnectionsCount;
            }
        }

        catch(Exception e)
        {
            logger.Error(e);
        }
    }

    private async Task<bool> SubmitBlockAsync(Share share, string blobHex, string blobHash)
    {
        var response = await rpc.ExecuteAsync<SubmitResponse>(logger, ZanoCommands.SubmitBlock, CancellationToken.None, new[] { blobHex });

        if(response.Error != null || response?.Response?.Status != "OK")
        {
            var error = response.Error?.Message ?? response.Response?.Status;

            logger.Warn(() => $"Block {share.BlockHeight} [{blobHash[..6]}] submission failed with: {error}");
            messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {error}"));
            return false;
        }

        return true;
    }

    public ZanoWorkerJob PrepareWorkerJob(double difficulty)
    {
        var job = currentJob;
        return job.PrepareWorkerJob(difficulty);
    }

    public override ZanoJob GetJobForStratum()
    {
        var job = currentJob;
        return job;
    }

    #region API-Surface

    public IObservable<Unit> Blocks { get; private set; }

    public ZanoCoinTemplate Coin => coin;

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);

        logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<ZanoJob>), pc);
        poolConfig = pc;
        clusterConfig = cc;
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<ZanoPoolConfigExtra>();
        coin = pc.Template.As<ZanoCoinTemplate>();

        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = ZanoConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        if(cc.PaymentProcessing?.Enabled == true && pc.PaymentProcessing?.Enabled == true)
        {
            // extract wallet daemon endpoints
            walletDaemonEndpoints = pc.Daemons
                .Where(x => x.Category?.ToLower() == ZanoConstants.WalletDaemonCategory)
                .Select(x =>
                {
                    if(string.IsNullOrEmpty(x.HttpPath))
                        x.HttpPath = ZanoConstants.DaemonRpcLocation;

                    return x;
                })
                .ToArray();

            if(walletDaemonEndpoints.Length == 0)
                throw new PoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for monero-pools require an additional entry of category \'wallet' pointing to the wallet daemon)", pc.Id);
        }

        ConfigureDaemons();
    }

    public bool ValidateAddress(string address)
    {
        if(string.IsNullOrEmpty(address))
            return false;

        var addressPrefix = CryptonoteBindings.DecodeAddress(address);
        var addressIntegratedPrefix = CryptonoteBindings.DecodeIntegratedAddress(address);
        var coin = poolConfig.Template.As<ZanoCoinTemplate>();

        switch(networkType)
        {
            case ZanoNetworkType.Main:
                if(addressPrefix != coin.AddressPrefix &&
                   addressPrefix != coin.AuditableAddressPrefix &&
                   addressIntegratedPrefix != coin.AddressPrefixIntegrated &&
                   addressIntegratedPrefix != coin.AddressV2PrefixIntegrated &&
                   addressIntegratedPrefix != coin.AuditableAddressIntegratedPrefix)
                    return false;
                break;

            case ZanoNetworkType.Test:
                if(addressPrefix != coin.AddressPrefixTestnet &&
                   addressPrefix != coin.AuditableAddressPrefixTestnet &&
                   addressIntegratedPrefix != coin.AddressPrefixIntegratedTestnet &&
                   addressIntegratedPrefix != coin.AddressV2PrefixIntegratedTestnet &&
                   addressIntegratedPrefix != coin.AuditableAddressIntegratedPrefixTestnet)
                    return false;
                break;
        }

        return true;
    }

    public BlockchainStats BlockchainStats { get; } = new();

    public async ValueTask<Share> SubmitShareAsync(StratumConnection worker,
        ZanoSubmitShareRequest request, ZanoWorkerJob workerJob, CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(request);

        var context = worker.ContextAs<ZanoWorkerContext>();

        var job = currentJob;
        if(workerJob.Height != job?.Height)
            throw new StratumException(StratumError.MinusOne, "block expired");

        // validate & process
        var (share, blobHex) = job.ProcessShare(logger, request.Nonce, workerJob.ExtraNonce, worker);

        // enrich share with common data
        share.PoolId = poolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;
        share.UserAgent = context.UserAgent;
        share.Source = clusterConfig.ClusterName;
        share.NetworkDifficulty = job.BlockTemplate.Difficulty;
        share.Created = clock.Now;

        // if block candidate, submit & check if accepted by network
        if(share.IsBlockCandidate)
        {
            logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash[..6]}]");

            share.IsBlockCandidate = await SubmitBlockAsync(share, blobHex, share.BlockHash);

            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash[..6]}] submitted by {context.Miner}");

                OnBlockFound();

                share.TransactionConfirmationData = share.BlockHash;
            }

            else
            {
                // clear fields that no longer apply
                share.TransactionConfirmationData = null;
            }
        }

        return share;
    }

    #endregion // API-Surface

    private static JToken GetFrameAsJToken(byte[] frame)
    {
        var text = Encoding.UTF8.GetString(frame);

        // find end of message type indicator
        var index = text.IndexOf(":");

        if (index == -1)
            return null;

        var json = text.Substring(index + 1);

        return JToken.Parse(json);
    }

    #region Overrides

    protected override void ConfigureDaemons()
    {
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        rpc = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // also setup wallet daemon
            walletRpc = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
        }
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        // test daemons
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, ZanoCommands.GetInfo, ct);

        if(response.Error != null)
            return false;

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // test wallet daemons
            var responses2 = await walletRpc.ExecuteAsync<object>(logger, ZanoWalletCommands.GetAddress, ct);

            return responses2.Error == null;
        }

        return true;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        var response = await rpc.ExecuteAsync<GetInfoResponse>(logger, ZanoCommands.GetInfo, ct);

        return response.Error == null && response.Response != null &&
            (response.Response.OutgoingConnectionsCount + response.Response.IncomingConnectionsCount) > 0;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        var syncPendingNotificationShown = false;

        do
        {
            var request = new GetBlockTemplateRequest
            {
                WalletAddress = poolConfig.Address,
                ReserveSize = ZanoConstants.ReserveSize
            };

            var response = await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger,
                ZanoCommands.GetBlockTemplate, ct, request);

            var isSynched = response.Error is not {Code: -9};

            if(isSynched)
            {
                logger.Info(() => "All daemons synched with blockchain");
                break;
            }

            if(!syncPendingNotificationShown)
            {
                logger.Info(() => "Daemon is still syncing with network. Manager will be started once synced.");
                syncPendingNotificationShown = true;
            }

            await ShowDaemonSyncProgressAsync(ct);
        } while(await timer.WaitForNextTickAsync(ct));
    }

    protected override async Task PostStartInitAsync(CancellationToken ct)
    {
        SetInstanceId();

        // coin config
        var coin = poolConfig.Template.As<ZanoCoinTemplate>();
        var infoResponse = await rpc.ExecuteAsync(logger, ZanoCommands.GetInfo, ct);

        if(infoResponse.Error != null)
            throw new PoolStartupException($"Init RPC failed: {infoResponse.Error.Message} (Code {infoResponse.Error.Code})", poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            var addressResponse = await walletRpc.ExecuteAsync<GetAddressResponse>(logger, ZanoWalletCommands.GetAddress, ct);

            // ensure pool owns wallet
            if(clusterConfig.PaymentProcessing?.Enabled == true && addressResponse.Response?.Address != poolConfig.Address)
                throw new PoolStartupException($"Wallet-Daemon does not own pool-address '{poolConfig.Address}'", poolConfig.Id);
        }

        var info = infoResponse.Response.ToObject<GetInfoResponse>();

        // chain detection
        if(!string.IsNullOrEmpty(info.NetType))
        {
            switch(info.NetType.ToLower())
            {
                case "mainnet":
                    networkType = ZanoNetworkType.Main;
                    break;
                case "testnet":
                    networkType = ZanoNetworkType.Test;
                    break;
                default:
                    throw new PoolStartupException($"Unsupport net type '{info.NetType}'", poolConfig.Id);
            }
        }

        else
            networkType = info.IsTestnet ? ZanoNetworkType.Test : ZanoNetworkType.Main;

        // address validation
        poolAddressBase58Prefix = CryptonoteBindings.DecodeAddress(poolConfig.Address);
        if(poolAddressBase58Prefix == 0)
            throw new PoolStartupException("Unable to decode pool-address", poolConfig.Id);

        switch(networkType)
        {
            case ZanoNetworkType.Main:
                if(poolAddressBase58Prefix != coin.AddressPrefix && poolAddressBase58Prefix != coin.AuditableAddressPrefix)
                    throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefix} or {coin.AuditableAddressPrefix}, got {poolAddressBase58Prefix}", poolConfig.Id);
                break;

            case ZanoNetworkType.Test:
                if(poolAddressBase58Prefix != coin.AddressPrefixTestnet && poolAddressBase58Prefix != coin.AuditableAddressPrefixTestnet)
                    throw new PoolStartupException($"Invalid pool address prefix. Expected {coin.AddressPrefixTestnet} or {coin.AuditableAddressPrefixTestnet}, got {poolAddressBase58Prefix}", poolConfig.Id);
                break;
        }

        // update stats
        BlockchainStats.RewardType = "POW";
        BlockchainStats.NetworkType = networkType.ToString();

        await UpdateNetworkStatsAsync(ct);

        // Periodically update network stats
        Observable.Interval(TimeSpan.FromMinutes(1))
            .Select(via => Observable.FromAsync(() =>
                Guard(()=> UpdateNetworkStatsAsync(ct),
                    ex=> logger.Error(ex))))
            .Concat()
            .Subscribe();

        if(poolConfig.EnableInternalStratum == true)
        {
            // make sure we have a current light cache
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            do
            {
                var blockTemplate = await GetBlockTemplateAsync(ct);

                if(blockTemplate?.Response != null)
                {
                    logger.Info(() => "Loading current light cache ...");

                    await coin.ProgpowHasher.GetCacheAsync(logger, (int) blockTemplate.Response.Height, ct);

                    logger.Info(() => "Loaded current light cache");
                    break;
                }

                logger.Info(() => "Waiting for first valid block template");
            } while(await timer.WaitForNextTickAsync(ct));
        }

        SetupJobUpdates(ct);
    }

    private void SetInstanceId()
    {
        instanceId = new byte[ZanoConstants.InstanceIdSize];

        using(var rng = RandomNumberGenerator.Create())
        {
            rng.GetNonZeroBytes(instanceId);
        }

        if(clusterConfig.InstanceId.HasValue)
            instanceId[0] = clusterConfig.InstanceId.Value;
    }

    protected virtual void SetupJobUpdates(CancellationToken ct)
    {
        var blockSubmission = blockFoundSubject.Synchronize();
        var pollTimerRestart = blockFoundSubject.Synchronize();

        var triggers = new List<IObservable<(string Via, string Data)>>
        {
            blockSubmission.Select(x => (JobRefreshBy.BlockFound, (string) null))
        };

        if(extraPoolConfig?.BtStream == null)
        {
            // collect ports
            var zmq = poolConfig.Daemons
                .Where(x => !string.IsNullOrEmpty(x.Extra.SafeExtensionDataAs<ZanoDaemonEndpointConfigExtra>()?.ZmqBlockNotifySocket))
                .ToDictionary(x => x, x =>
                {
                    var extra = x.Extra.SafeExtensionDataAs<ZanoDaemonEndpointConfigExtra>();
                    var topic = !string.IsNullOrEmpty(extra.ZmqBlockNotifyTopic.Trim()) ? extra.ZmqBlockNotifyTopic.Trim() : BitcoinConstants.ZmqPublisherTopicBlockHash;

                    return (Socket: extra.ZmqBlockNotifySocket, Topic: topic);
                });

            if(zmq.Count > 0)
            {
                logger.Info(() => $"Subscribing to ZMQ push-updates from {string.Join(", ", zmq.Values)}");

                var blockNotify = rpc.ZmqSubscribe(logger, ct, zmq)
                    .Select(msg =>
                    {
                        using(msg)
                        {
                            // We just take the second frame's raw data and turn it into a hex string.
                            // If that string changes, we got an update (DistinctUntilChanged)
                            var result = msg[0].Read().ToHexString();
                            return result;
                        }
                    })
                    .DistinctUntilChanged()
                    .Select(_ => (JobRefreshBy.PubSub, (string) null))
                    .Publish()
                    .RefCount();

                pollTimerRestart = Observable.Merge(
                        blockSubmission,
                        blockNotify.Select(_ => Unit.Default))
                    .Publish()
                    .RefCount();

                triggers.Add(blockNotify);
            }

            if(poolConfig.BlockRefreshInterval > 0)
            {
                // periodically update block-template
                var pollingInterval = poolConfig.BlockRefreshInterval > 0 ? poolConfig.BlockRefreshInterval : 1000;

                triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                    .TakeUntil(pollTimerRestart)
                    .Select(_ => (JobRefreshBy.Poll, (string) null))
                    .Repeat());
            }

            else
            {
                // get initial blocktemplate
                triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                    .Select(_ => (JobRefreshBy.Initial, (string) null))
                    .TakeWhile(_ => !hasInitialBlockTemplate));
            }
        }

        else
        {
            triggers.Add(BtStreamSubscribe(extraPoolConfig.BtStream)
                .Select(json => (JobRefreshBy.BlockTemplateStream, json))
                .Publish()
                .RefCount());

            // get initial blocktemplate
            triggers.Add(Observable.Interval(TimeSpan.FromMilliseconds(1000))
                .Select(_ => (JobRefreshBy.Initial, (string) null))
                .TakeWhile(_ => !hasInitialBlockTemplate));
        }

        Blocks = triggers.Merge()
            .Select(x => Observable.FromAsync(() => UpdateJob(ct, x.Via, x.Data)))
            .Concat()
            .Where(isNew => isNew)
            .Do(_ => hasInitialBlockTemplate = true)
            .Select(_ => Unit.Default)
            .Publish()
            .RefCount();
    }

    #endregion // Overrides
}
