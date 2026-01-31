using System;
using System.Linq;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Xelis.Configuration;
using Miningcore.Blockchain.Xelis.DaemonRequests;
using Miningcore.Blockchain.Xelis.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Block = Miningcore.Persistence.Model.Block;
using Contract = Miningcore.Contracts.Contract;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Xelis;

[CoinFamily(CoinFamily.Xelis)]
public class XelisPayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public XelisPayoutHandler(
        IComponentContext ctx,
        IConnectionFactory cf,
        IMapper mapper,
        IShareRepository shareRepo,
        IBlockRepository blockRepo,
        IBalanceRepository balanceRepo,
        IPaymentRepository paymentRepo,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(balanceRepo);
        Contract.RequiresNonNull(paymentRepo);

        this.ctx = ctx;
    }

    protected readonly IComponentContext ctx;
    private RpcClient rpcClient;
    private RpcClient rpcClientWallet;
    private string network;
    private XelisPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;
    private int minConfirmations;

    protected override string LogCategory => "Xelis Payout Handler";
    
    #region IPayoutHandler

    public virtual async Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        poolConfig = pc;
        clusterConfig = cc;
        extraPoolPaymentProcessingConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<XelisPaymentProcessingConfigExtra>();
    
        logger = LogUtil.GetPoolScopedLogger(typeof(XelisPayoutHandler), pc);

        // configure standard daemon
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        var daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = XelisConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        rpcClient = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);

        // configure wallet daemon
        var walletDaemonEndpoints = pc.Daemons
            .Where(x => x.Category?.ToLower() == XelisConstants.WalletDaemonCategory)
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = XelisConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        rpcClientWallet = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);

        var info = await rpcClient.ExecuteAsync<GetChainInfoResponse>(logger, XelisCommands.GetChainInfo, ct);
        if(info.Error != null)
            throw new Exception($"'{XelisCommands.DaemonName}' returned error: {info.Error.Message} (Code {info.Error.Code})");

        network = info.Response.Network;

        // wallet daemon needs to be online
        var isOnline = await rpcClientWallet.ExecuteAsync<object>(logger, XelisWalletCommands.IsOnline, ct);
        if(isOnline.Error != null)
            throw new Exception($"'{XelisWalletCommands.IsOnline}': {isOnline.Error.Message} (Code {isOnline.Error.Code})");

        // wallet daemon is offline
        if(!(bool)isOnline.Response)
        {
            logger.Warn(() => $"[{LogCategory}] '{XelisWalletCommands.DaemonName}' is offline...");

            var setOnlineMode = await rpcClientWallet.ExecuteAsync<object>(logger, XelisWalletCommands.SetOnlineMode, ct);
            if(setOnlineMode.Error != null)
                throw new Exception($"'{XelisWalletCommands.SetOnlineMode}': {setOnlineMode.Error.Message} (Code {setOnlineMode.Error.Code})");

            // wallet daemon is online
            if((bool)setOnlineMode.Response)
                logger.Info(() => $"[{LogCategory}] '{XelisWalletCommands.DaemonName}' is now online...");
        }

        minConfirmations = extraPoolPaymentProcessingConfig?.MinimumConfirmations ?? (network == "mainnet" ? 60 : 50);
    }

    public virtual async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        if(blocks.Length == 0)
            return blocks;

        var info = await rpcClient.ExecuteAsync<GetChainInfoResponse>(logger, XelisCommands.GetChainInfo, ct);
        if(info.Error != null)
        {
            logger.Warn(() => $"[{LogCategory}] '{XelisCommands.GetChainInfo}': {info.Error.Message} (Code {info.Error.Code})");
            return blocks;
        }

        var coin = poolConfig.Template.As<XelisCoinTemplate>();
        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
        var result = new List<Block>();

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = blocks
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            for(var j = 0; j < page.Length; j++)
            {
                var block = page[j];

                var getBlockByHashRequest = new GetBlockByHashRequest
                {
                    Hash = block.Hash
                };
                
                var response = await rpcClient.ExecuteAsync<GetBlockByHashResponse>(logger, XelisCommands.GetBlockByHash, ct, getBlockByHashRequest);
                if(response.Error != null)
                {
                    logger.Warn(() => $"[{LogCategory}] '{XelisCommands.GetBlockByHash}': {response.Error.Message} (Code {response.Error.Code})");

                    // we lost that battle
                    if(response.Error.Code == (int)XelisRPCErrorCode.RPC_INVALID_PARAMS)
                    {
                        result.Add(block);

                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;

                        logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned because it's not on chain");

                        messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    }
                }

                // we lost that battle
                else if(response.Response.Miner != poolConfig.Address)
                {
                    result.Add(block);

                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;

                    logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} [Type: {response.Response.BlockType}] classified as orphaned because another miner [{response.Response.Miner}] was rewarded");

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }
                else
                {
                    logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} [Type: {response.Response.BlockType}] uses a custom minimum confirmations calculation [{minConfirmations}]");

                    block.ConfirmationProgress = Math.Min(1.0d, (double) (info.Response.TopoHeight - block.BlockHeight) / minConfirmations);

                    result.Add(block);

                    messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);

                    // matured and spendable?
                    if(block.ConfirmationProgress >= 1)
                    {
                        block.ConfirmationProgress = 1;

                        block.Reward = (decimal) response.Response.MinerReward / XelisConstants.SmallestUnit;

                        // security
                        if (block.Reward > 0)
                        {
                            block.Status = BlockStatus.Confirmed;

                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} [Type: {response.Response.BlockType}] worth {FormatAmount(block.Reward)}");
                        }
                        else
                        {
                            block.Status = BlockStatus.Orphaned;
                            block.Reward = 0;
                        }
                        
                        messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    }
                }
            }
        }

        return result.ToArray();
    }

    public virtual async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        // ensure we have enough peers
        var enoughPeers = await EnsureDaemonsSynchedAsync(ct);
        if(!enoughPeers)
            return;

        // build args
        var amounts = balances
            .Where(x => x.Amount > 0)
            .ToDictionary(x => x.Address, x => x.Amount);

        if(amounts.Count == 0)
            return;
        
        var coin = poolConfig.Template.As<XelisCoinTemplate>();

        var balancesTotal = amounts.Sum(x => x.Value);
        logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balancesTotal)} to {balances.Length} addresses");

        logger.Info(() => $"[{LogCategory}] Validating addresses...");
        foreach(var pair in amounts)
        {
            var validateAddressRequest = new ValidateAddressRequest
            {
                Address = pair.Key
            };

            var validateAddress = await rpcClient.ExecuteAsync<ValidateAddressResponse>(logger, XelisCommands.ValidateAddress, ct, validateAddressRequest);
            if(validateAddress.Error != null)
                logger.Warn(()=> $"[{LogCategory}] Address {pair.Key} is not valid: {validateAddress.Error.Message} (Code {validateAddress.Error.Code})");
        }

        var responseBalance = await rpcClientWallet.ExecuteAsync<object>(logger, XelisWalletCommands.GetBalance, ct);
        if(responseBalance.Error != null)
        {
            logger.Warn(()=> $"[{LogCategory}] '{XelisWalletCommands.GetBalance}': {responseBalance.Error.Message} (Code {responseBalance.Error.Code})");
            return;
        }

        var walletBalance = Convert.ToDecimal(responseBalance.Response) / XelisConstants.SmallestUnit;
    
        logger.Info(() => $"[{LogCategory}] Current wallet balance - Total: [{FormatAmount(walletBalance)}]");

        // bail if balance does not satisfy payments
        if(walletBalance < balancesTotal)
        {
            logger.Warn(() => $"[{LogCategory}] Wallet balance currently short of {FormatAmount(balancesTotal - walletBalance)}. Will try again");
            return;
        }

        var pageSize = extraPoolPaymentProcessingConfig?.MaximumDestinationPerTransfer ?? XelisConstants.MaximumDestinationPerTransfer;
        var pageCount = (int) Math.Ceiling((double) balances.Length / pageSize);

        logger.Info(() => $"[{LogCategory}] Maximum of simultaneous destination address in a single transaction: {pageSize}");

        for(var i = 0; i < pageCount; i++)
        {
            logger.Info(() => $"[{LogCategory}] Processing batch {i + 1}/{pageCount}");

            var page = balances
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            var buildTransactionRequest = new BuildTransactionRequest
            {
                Transfers = page
                    .Where(x => x.Amount > 0)
                    .Select(x =>
                    {
                        return new BuildTransactionTransfer
                        {
                            Destination = x.Address,
                            Asset = XelisConstants.TransactionDefaultAsset,
                            Amount = (ulong) Math.Floor(x.Amount * XelisConstants.SmallestUnit)
                        };
                    }).ToArray()
            };

            if(extraPoolPaymentProcessingConfig?.KeepTransactionFees == true)
            {
                var estimateFeesRequest = new EstimateFeesRequest
                {
                    Transfers = buildTransactionRequest.Transfers
                };

                var estimateFeesResponse = await rpcClientWallet.ExecuteAsync<object>(logger, XelisWalletCommands.EstimateFees, ct, estimateFeesRequest);
                if(estimateFeesResponse.Error != null)
                {
                    logger.Warn(()=> $"[{LogCategory}] '{XelisWalletCommands.EstimateFees}': {estimateFeesResponse.Error.Message} (Code {estimateFeesResponse.Error.Code})");
                    continue;
                }

                var estimatedTransactionFees = Convert.ToDecimal(estimateFeesResponse.Response) / XelisConstants.SmallestUnit;
                logger.Info(() => $"[{LogCategory}] Estimated transaction fees: {FormatAmount(estimatedTransactionFees)}");

                logger.Debug(() => $"[{LogCategory}] Pool does not pay the transaction fee, so each address will have its payout deducted with [{FormatAmount(estimatedTransactionFees / page.Length)}]");

                buildTransactionRequest = new BuildTransactionRequest
                {
                    Transfers = page
                        .Where(x => x.Amount > 0)
                        .Select(x =>
                        {
                            return new BuildTransactionTransfer
                            {
                                Destination = x.Address,
                                Asset = XelisConstants.TransactionDefaultAsset,
                                Amount = (ulong) Math.Floor((x.Amount > (estimatedTransactionFees / page.Length) ? x.Amount - (estimatedTransactionFees / page.Length) : x.Amount) * XelisConstants.SmallestUnit)
                            };
                        }).ToArray()
                };

                buildTransactionRequest.Fee = new BuildTransactionFee
                {
                    Amount = (ulong) (estimatedTransactionFees * XelisConstants.SmallestUnit)
                };
            }

            var buildTransactionResponse = await rpcClientWallet.ExecuteAsync<BuildTransactionResponse>(logger, XelisWalletCommands.BuildTransaction, ct, buildTransactionRequest);
            if(buildTransactionResponse.Error != null)
            {
                logger.Error(()=> $"[{LogCategory}] '{XelisWalletCommands.BuildTransaction}': {buildTransactionResponse.Error.Message} (Code {buildTransactionResponse.Error.Code})");
                NotifyPayoutFailure(poolConfig.Id, page, $"Daemon command '{XelisWalletCommands.BuildTransaction}' returned error: {buildTransactionResponse.Error.Message} code {buildTransactionResponse.Error.Code}", null);
                continue;
            }

            if(string.IsNullOrEmpty(buildTransactionResponse.Response.Hash))
            {
                logger.Warn(() => $"[{LogCategory}] Payment transaction failed to return a transaction id");
                continue;
            }
            else
            {
                // payment successful
                var finalTransactionFees = (decimal) buildTransactionResponse.Response.Fee / XelisConstants.SmallestUnit;

                logger.Info(() => $"[{LogCategory}] Payment transaction id: {buildTransactionResponse.Response.Hash} || Payment transaction fees: {FormatAmount(finalTransactionFees)}");

                await PersistPaymentsAsync(page, buildTransactionResponse.Response.Hash);
                NotifyPayoutSuccess(poolConfig.Id, page, new[] { buildTransactionResponse.Response.Hash }, finalTransactionFees);
            }
        }
    }

    public double AdjustBlockEffort(double effort)
    {
        return effort;
    }

    #endregion // IPayoutHandler

    private async Task<bool> EnsureDaemonsSynchedAsync(CancellationToken ct)
    {
        // ensure we have enough peers
        var status = await rpcClient.ExecuteAsync<GetStatusResponse>(logger, XelisCommands.GetStatus, ct);
        if(status.Error != null)
        {
            logger.Warn(() => $"'{XelisCommands.GetStatus}': {status.Error.Message} (Code {status.Error.Code})");
            return false;
        }

        if(network.ToLower() == "mainnet")
            return status.Response.PeerCount > 0;
        else
            return status.Response.PeerCount >= 0;
    }
}