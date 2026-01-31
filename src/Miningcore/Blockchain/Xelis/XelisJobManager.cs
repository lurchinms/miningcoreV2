using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using Autofac;
using Miningcore.Blockchain.Xelis.Configuration;
using Miningcore.Blockchain.Xelis.DaemonRequests;
using Miningcore.Blockchain.Xelis.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Notifications.Messages;
using Miningcore.Rest;
using Miningcore.Rpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Xelis;

public class XelisJobManager : JobManagerBase<XelisJob>
{
    public XelisJobManager(
        IComponentContext ctx,
        IMasterClock clock,
        IMessageBus messageBus,
        IExtraNonceProvider extraNonceProvider) :
        base(ctx, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);
        Contract.RequiresNonNull(extraNonceProvider);

        this.clock = clock;
        this.extraNonceProvider = extraNonceProvider;
    }

    private XelisCoinTemplate coin;
    private DaemonEndpointConfig[] daemonEndpoints;
    private DaemonEndpointConfig[] walletDaemonEndpoints;
    private RpcClient rpc;
    protected RpcClient rpcWallet;
    private string network;
    private readonly IExtraNonceProvider extraNonceProvider;
    private readonly IMasterClock clock;
    private XelisPoolConfigExtra extraPoolConfig;
    protected int maxActiveJobs;
    public string poolPublicKey { get; protected set; }

    private async Task<(RpcResponse<GetBlockHeaderResponse>, RpcResponse<GetBlockTemplateResponse>)> GetBlockTemplateAsync(CancellationToken ct)
    {
        var getBlockHeaderRequest = new GetBlockHeaderRequest
        {
            Address = poolConfig.Address
        };

        var getBlockHeaderResponse = await rpc.ExecuteAsync<GetBlockHeaderResponse>(logger, XelisCommands.GetBlockHeader, ct, getBlockHeaderRequest);

        var getBlockTemplateRequest = new GetBlockTemplateRequest
        {
            BlockHeader = getBlockHeaderResponse?.Response.Template,
            Address = poolConfig.Address
        };

        var getBlockTemplateResponse = await rpc.ExecuteAsync<GetBlockTemplateResponse>(logger, XelisCommands.GetBlockTemplate, ct, getBlockTemplateRequest);

        return (getBlockHeaderResponse, getBlockTemplateResponse);
    }

    protected async Task<bool> UpdateJob(CancellationToken ct, string via = null)
    {
        try
        {
            var (responseBlockHeader, responseBlockTemplate) = await GetBlockTemplateAsync(ct);
            if(responseBlockHeader.Error != null || responseBlockTemplate.Error != null)
                return false;

            var blockHeader = responseBlockHeader.Response;
            var blockTemplate = responseBlockTemplate.Response;
            var job = currentJob;

            logger.Debug(() => $" blockHeader.TopoHeight [{blockHeader.TopoHeight}] || blockTemplate.Difficulty [{blockTemplate.Difficulty}]] || blockTemplate.Template [{blockTemplate.Template}]");

            var newHash = blockTemplate.Template.HexToByteArray().AsSpan().Slice(XelisConstants.BlockTemplateOffsetBlockHeaderWork, XelisConstants.BlockTemplateOffsetTimestamp).ToHexString();
            var isNew = currentJob == null ||
                (newHash != job?.PrevHash);

            if(isNew)
            {
                messageBus.NotifyChainHeight(poolConfig.Id, blockTemplate.Height, poolConfig.Template);

                // update job
                job = new XelisJob();
                job.Init(blockHeader, blockTemplate, NextJobId(), clock, network, ShareMultiplier, newHash);

                logger.Debug(() => $"blockTargetValue: {job.blockTargetValue}");

                if(via != null)
                    logger.Info(() => $"Detected new block {blockHeader.TopoHeight} [{via}]");
                else
                    logger.Info(() => $"Detected new block {blockHeader.TopoHeight}");

                // update stats
                if (job.BlockHeader.TopoHeight > BlockchainStats.BlockHeight)
                {
                    BlockchainStats.LastNetworkBlockTime = clock.Now;
                    BlockchainStats.BlockHeight = job.BlockHeader.TopoHeight; // Miningpolstats seems to track "TopoHeight" not "Height" :/
                    BlockchainStats.NetworkDifficulty = job.Difficulty;
                }

                currentJob = job;
            }

            else
            {
                if(via != null)
                    logger.Debug(() => $"Template update {blockHeader.TopoHeight} [{via}]");
                else
                    logger.Debug(() => $"Template update {blockHeader.TopoHeight}");
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

    private async Task UpdateNetworkStatsAsync(CancellationToken ct)
    {
        var hashrate = await rpc.ExecuteAsync<GetDifficultyResponse>(logger, XelisCommands.GetDifficulty, ct);
        if(hashrate.Error == null)
            BlockchainStats.NetworkHashrate = hashrate.Response.Hashrate;

        var status = await rpc.ExecuteAsync<GetStatusResponse>(logger, XelisCommands.GetStatus, ct);
        if(status.Error == null)
            BlockchainStats.ConnectedPeers = status.Response.PeerCount;
    }

    private async Task ShowDaemonSyncProgressAsync(CancellationToken ct)
    {
        var status = await rpc.ExecuteAsync<GetStatusResponse>(logger, XelisCommands.GetStatus, ct);
        if(status.Error == null)
        {
            var percent = (double) status.Response.TopoHeight / status.Response.BestTopoHeight * 100;

            logger.Info(() => $"Daemon has downloaded {percent:0.00}% of blockchain from {status.Response.PeerCount} peer(s)");
        }
    }

    protected async Task<bool> SubmitBlockAsync(Share share, GetBlockHeaderResponse blockHeader, CancellationToken ct)
    {
        Contract.RequiresNonNull(blockHeader);

        var block = new SubmitBlockRequest
        {
            BlockTemplate = blockHeader.Template
        };

        var response = await rpc.ExecuteAsync<object>(logger, XelisCommands.SubmitBlock, ct, block);
        if(response.Error != null)
        {
            logger.Warn(() => $"Block {share.BlockHeight} submission failed with: {response.Error.Message} (Code {response.Error.Code})");
            messageBus.SendMessage(new AdminNotification("Block submission failed", $"Pool {poolConfig.Id} {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}failed to submit block {share.BlockHeight}: {response.Error.Message} (Code {response.Error.Code})"));
            return false;
        }
        
        logger.Debug(() => $"{XelisCommands.SubmitBlock}': {response.Response}");

        return (bool)response.Response;
    }

    protected object GetJobParamsForStratum(bool isNew)
    {
        var job = currentJob;
        return job?.GetJobParams(isNew);
    }

    public override XelisJob GetJobForStratum()
    {
        var job = currentJob;
        return job;
    }

    #region API-Surface

    public IObservable<object> Jobs { get; private set; }
    public BlockchainStats BlockchainStats { get; } = new();
    public string Network => network;

    public XelisCoinTemplate Coin => coin;

    public override void Configure(PoolConfig pc, ClusterConfig cc)
    {
        coin = pc.Template.As<XelisCoinTemplate>();

        extraPoolConfig = pc.Extra.SafeExtensionDataAs<XelisPoolConfigExtra>();

        maxActiveJobs = extraPoolConfig?.MaxActiveJobs ?? 8;

        // extract standard daemon endpoints
        daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = XelisConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        if(cc.PaymentProcessing?.Enabled == true && pc.PaymentProcessing?.Enabled == true)
        {
            // extract wallet daemon endpoints
            walletDaemonEndpoints = pc.Daemons
                .Where(x => x.Category?.ToLower() == XelisConstants.WalletDaemonCategory)
                .Select(x =>
                {
                    if(string.IsNullOrEmpty(x.HttpPath))
                        x.HttpPath = XelisConstants.DaemonRpcLocation;

                    return x;
                })
                .ToArray();

            if(walletDaemonEndpoints.Length == 0)
                throw new PoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for xelis-pools require an additional entry of category 'wallet' pointing to the wallet http port: https://docs.xelis.io/getting-started/configuration#wallet )", pc.Id);
        }

        base.Configure(pc, cc);
    }

    public object[] GetSubscriberData(StratumConnection worker)
    {
        Contract.RequiresNonNull(worker);

        var context = worker.ContextAs<XelisWorkerContext>();

        // assign unique ExtraNonce1 to worker (miner)
        context.ExtraNonce1 = extraNonceProvider.Next();

        // setup response data
        var responseData = new object[]
        {
            context.ExtraNonce1,
            XelisConstants.ExtranoncePlaceHolderLength,
        };

        return responseData;
    }

    public int GetExtraNonce1Size()
    {
        return extraPoolConfig?.ExtraNonce1Size ?? 32;
    }

    public virtual async ValueTask<Share> SubmitShareAsync(StratumConnection worker, object submission,
        CancellationToken ct)
    {
        Contract.RequiresNonNull(worker);
        Contract.RequiresNonNull(submission);

        if(submission is not object[] submitParams)
            throw new StratumException(StratumError.Other, "invalid params");

        var context = worker.ContextAs<XelisWorkerContext>();

        // extract params
        var jobId = submitParams[1] as string;
        var nonce = submitParams[2] as string;

        XelisJob job;

        lock(context)
        {
            job = context.GetJob(jobId);

            if(job == null)
                logger.Warn(() => $"[{context.Miner}] => jobId: {jobId} - Last known job: {context.validJobs.ToArray().FirstOrDefault()?.JobId}");
        }

        if(job == null)
            throw new StratumException(StratumError.JobNotFound, "job not found");

        // validate & process
        var share = job.ProcessShare(worker, nonce);

        // enrich share with common data
        share.PoolId = poolConfig.Id;
        share.IpAddress = worker.RemoteEndpoint.Address.ToString();
        share.Miner = context.Miner;
        share.Worker = context.Worker;
        share.UserAgent = context.UserAgent;
        share.Source = clusterConfig.ClusterName;
        share.Created = clock.Now;

        // if block candidate, submit & check if accepted by network
        if(share.IsBlockCandidate)
        {
            logger.Info(() => $"Submitting block {share.BlockHeight} [{share.BlockHash}][{job.BlockHeader.Template}]");

            var acceptResponse = await SubmitBlockAsync(share, job.BlockHeader, ct);

            // is it still a block candidate?
            share.IsBlockCandidate = acceptResponse;

            if(share.IsBlockCandidate)
            {
                logger.Info(() => $"Daemon accepted block {share.BlockHeight} [{share.BlockHash}] submitted by {context.Miner}");

                OnBlockFound();

                // persist the nonce to make block unlocking a bit more reliable
                share.TransactionConfirmationData = nonce;
            }

            else
            {
                // clear fields that no longer apply
                share.TransactionConfirmationData = null;
            }
        }

        return share;
    }

    public async Task<string> NormalizeAddressAsync(string address, CancellationToken ct)
    {
        if(string.IsNullOrEmpty(address))
            return address;

        var splitAddressRequest = new SplitAddressRequest
        {
            Address = address
        };

        var response = await rpc.ExecuteAsync<SplitAddressResponse>(logger, XelisCommands.SplitAddress, ct, splitAddressRequest);
        if(response.Error != null)
        {
            logger.Debug(() => $"'{address}': {response.Error.Message} (Code {response.Error.Code})");
            return address;
        }

        return response.Response.Address;
    }

    public async Task<bool> ValidateAddressAsync(string address, CancellationToken ct)
    {
        if(string.IsNullOrEmpty(address))
            return false;

        var validateAddressRequest = new ValidateAddressRequest
        {
            Address = address
        };

        var response = await rpc.ExecuteAsync<ValidateAddressResponse>(logger, XelisCommands.ValidateAddress, ct, validateAddressRequest);
        if(response.Error != null)
        {
            logger.Warn(() => $"'{address}': {response.Error.Message} (Code {response.Error.Code})");
            return false;
        }

        return response.Response.IsValid;
    }

    public double ShareMultiplier => coin.ShareMultiplier;

    #endregion // API-Surface

    #region Overrides

    protected override void ConfigureDaemons()
    {
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        rpc = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // extract wallet daemon endpoints
            var walletDaemonEndpoints = poolConfig.Daemons
                .Where(x => x.Category?.ToLower() == XelisConstants.WalletDaemonCategory)
                .ToArray();

            if(walletDaemonEndpoints.Length == 0)
                throw new PoolStartupException("Wallet-RPC daemon is not configured (Daemon configuration for xelis-pools require an additional entry of category 'wallet' pointing to the wallet http port: https://docs.xelis.io/getting-started/configuration#wallet )", poolConfig.Id);

            rpcWallet = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, poolConfig.Id);
        }
    }

    protected override async Task<bool> AreDaemonsHealthyAsync(CancellationToken ct)
    {
        logger.Debug(() => $"Checking if '{XelisCommands.DaemonName}' daemon is healthy...");

        // test daemon
        var info = await rpc.ExecuteAsync<GetChainInfoResponse>(logger, XelisCommands.GetChainInfo, ct);
        if(info.Error != null)
        {
            logger.Warn(() => $"'{XelisCommands.GetChainInfo}': {info.Error.Message} (Code {info.Error.Code})");
            return false;
        }

        // update stats
        if(!string.IsNullOrEmpty(info.Response.Version))
            BlockchainStats.NodeVersion = info.Response.Version;

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            logger.Debug(() => "Checking if '{XelisWalletCommands.DaemonName}' daemon is healthy...");
    
            // test wallet daemon
            var balance = await rpcWallet.ExecuteAsync<object>(logger, XelisWalletCommands.GetBalance, ct);

            if(balance.Error != null)
                logger.Debug(() => $"'{XelisWalletCommands.GetBalance}': {balance.Error.Message} (Code {balance.Error.Code})");

            return balance.Error == null;
        }

        return true;
    }

    protected override async Task<bool> AreDaemonsConnectedAsync(CancellationToken ct)
    {
        logger.Debug(() => $"Checking if '{XelisCommands.DaemonName}' daemon is connected...");

        var status = await rpc.ExecuteAsync<GetStatusResponse>(logger, XelisCommands.GetStatus, ct);
        if(status.Error != null)
        {
            logger.Warn(() => $"'{XelisCommands.GetStatus}': {status.Error.Message} (Code {status.Error.Code})");
            return false;
        }

        return status.Response.PeerCount > 0;
    }

    protected override async Task EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        logger.Debug(() => $"Checking if '{XelisCommands.DaemonName}' daemon is synched...");

        var syncPendingNotificationShown = false;

        do
        {
            try
            {
                var status = await rpc.ExecuteAsync<GetStatusResponse>(logger, XelisCommands.GetStatus, ct);
                if(status.Error != null)
                    logger.Warn(() => $"'{XelisCommands.GetStatus}': {status.Error.Message} (Code {status.Error.Code})");

                if(status.Response.BestTopoHeight <= status.Response.MedianTopoHeight)
                {
                    logger.Info(() => $"'{XelisCommands.DaemonName}' daemon synched with blockchain");
                    break;
                }
            }

            catch(Exception e)
            {
                logger.Error(e);
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
        // validate pool address
        if(string.IsNullOrEmpty(poolConfig.Address))
            throw new PoolStartupException("Pool address is not configured", poolConfig.Id);

        var info = await rpc.ExecuteAsync<GetChainInfoResponse>(logger, XelisCommands.GetChainInfo, ct);
        if(info.Error != null)
            throw new PoolStartupException("Init RPC failed...", poolConfig.Id);

        network = info.Response.Network;

        // update stats
        BlockchainStats.RewardType = "POW";
        BlockchainStats.NetworkType = network;

        var validateAddressRequest = new ValidateAddressRequest
        {
            Address = poolConfig.Address
        };

        var validateAddress = await rpc.ExecuteAsync<ValidateAddressResponse>(logger, XelisCommands.ValidateAddress, ct, validateAddressRequest);
        if(validateAddress.Error != null)
            throw new PoolStartupException($"Pool address '{poolConfig.Address}': {validateAddress.Error.Message} (Code {validateAddress.Error.Code})", poolConfig.Id);

        var extractKeyFromAddressRequest = new ExtractKeyFromAddressRequest
        {
            Address = poolConfig.Address
        };

        var extractKeyFromAddress = await rpc.ExecuteAsync<ExtractKeyFromAddressResponse>(logger, XelisCommands.ExtractKeyFromAddress, ct, extractKeyFromAddressRequest);
        if(extractKeyFromAddress.Error != null)
            throw new PoolStartupException($"Pool address public key '{poolConfig.Address}': {extractKeyFromAddress.Error} (Code {extractKeyFromAddress.Error.Code})", poolConfig.Id);

        logger.Info(() => $"Pool address public key: {extractKeyFromAddress.Response.PublicKey}");
        poolPublicKey = extractKeyFromAddress.Response.PublicKey;

        if(clusterConfig.PaymentProcessing?.Enabled == true && poolConfig.PaymentProcessing?.Enabled == true)
        {
            // validate wallet address
            var walletAddress = await rpcWallet.ExecuteAsync<string>(logger, XelisWalletCommands.GetAddress, ct);
            if(walletAddress.Error != null)
                throw new PoolStartupException($"'{XelisWalletCommands.GetAddress}': {walletAddress.Error.Message} (Code {walletAddress.Error.Code})", poolConfig.Id);

            if(walletAddress.Response != poolConfig.Address)
                throw new PoolStartupException($"Wallet address [{walletAddress.Response}] does not match pool address: {poolConfig.Address}", poolConfig.Id);
        }

        await UpdateNetworkStatsAsync(ct);

        // Periodically update network stats
        Observable.Interval(TimeSpan.FromMinutes(1))
            .Select(via => Observable.FromAsync(() =>
                Guard(()=> UpdateNetworkStatsAsync(ct),
                    ex=> logger.Error(ex))))
            .Concat()
            .Subscribe();

        SetupJobUpdates(ct);
    }

    protected virtual void SetupJobUpdates(CancellationToken ct)
    {
        var pollingInterval = poolConfig?.BlockRefreshInterval ?? 0;

        var blockSubmission = blockFoundSubject.Synchronize();
        var pollTimerRestart = blockFoundSubject.Synchronize();

        var triggers = new List<IObservable<(string Via, string Data)>>
        {
            blockSubmission.Select(_ => (JobRefreshBy.BlockFound, (string) null))
        };

        var endpointExtra = daemonEndpoints
            .Where(x => x.Extra.SafeExtensionDataAs<XelisDaemonEndpointConfigExtra>() != null)
            .Select(x=> Tuple.Create(x, x.Extra.SafeExtensionDataAs<XelisDaemonEndpointConfigExtra>()))
            .FirstOrDefault();

        if(endpointExtra?.Item2?.PortWs.HasValue == true)
        {
            var (endpointConfig, extra) = endpointExtra;

            var wsEndpointConfig = new DaemonEndpointConfig
            {
                Host = endpointConfig.Host,
                Port = extra.PortWs!.Value,
                HttpPath = extra.HttpPathWs,
                Ssl = extra.SslWs
            };

            logger.Info(() => $"Subscribing to WebSocket {(wsEndpointConfig.Ssl ? "wss" : "ws")}://{wsEndpointConfig.Host}:{wsEndpointConfig.Port}");

            var subscribeRequest = new SubscribeRequest
            {
                Notify = XelisCommands.NotifiyNewBlock
            };

            // stream work updates
            var getWorkObs = rpc.WebsocketSubscribe(logger, ct, wsEndpointConfig, XelisCommands.Subscribe, subscribeRequest)
                .Publish()
                .RefCount();

            var websocketNotify = getWorkObs.Where(x => x != null)
                .Publish()
                .RefCount();

            pollTimerRestart = blockSubmission.Merge(websocketNotify.Select(_ => Unit.Default))
                .Publish()
                .RefCount();

            triggers.Add(websocketNotify.Select(_ => (JobRefreshBy.WebSocket, (string) null)));

            if(pollingInterval > 0)
            {
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
            pollingInterval = pollingInterval > 0 ? pollingInterval : 1000;

            // ordinary polling (avoid this at all cost)
            triggers.Add(Observable.Timer(TimeSpan.FromMilliseconds(pollingInterval))
                .TakeUntil(pollTimerRestart)
                .Select(_ => (JobRefreshBy.Poll, (string) null))
                .Repeat());
        }

        Jobs = triggers.Merge()
            .Select(x => Observable.FromAsync(() => UpdateJob(ct, x.Via)))
            .Concat()
            .Where(x => x)
            .Do(x =>
            {
                if(x)
                    hasInitialBlockTemplate = true;
            })
            .Select(x => GetJobParamsForStratum(x))
            .Publish()
            .RefCount();
    }

    #endregion // Overrides
}
