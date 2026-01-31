using System;
using System.Globalization;
using System.Numerics;

namespace Miningcore.Blockchain.Xelis;

public static class XelisConstants
{
    public const int ExtranoncePlaceHolderLength = 32;
    public static int NonceLength = 16;

    public const decimal SmallestUnit = 100000000;
    public const string WalletDaemonCategory = "wallet";

    public const string DaemonRpcLocation = "json_rpc";

    public const int TargetPaddingLength = 32;
    public static readonly double Pow2x32 = Math.Pow(2, 32);
    public static BigInteger Diff1Target = BigInteger.Pow(2, 256) - 1;

    public const int BlockHeaderOffsetVersion = 0;
    public const int BlockHeaderOffsetHeight = 1;
    public const int BlockHeaderOffsetTimestamp = 9;
    public const int BlockHeaderOffsetNonce = 17;
    public const int BlockHeaderOffsetExtraNonce = 25;
    public const int BlockHeaderOffsetTipsCount = 57;
    public const int BlockHeaderOffsetTips = 58;
    public const int BlockHeaderSizeTransactionsCount = 2;
    public const int BlockHeaderSizeMinerPublicKey = 32;

    public const int BlockTemplateOffsetBlockHeaderWork = 0;
    public const int BlockTemplateOffsetTimestamp = 32;
    public const int BlockTemplateOffsetNonce = 40;
    public const int BlockTemplateOffsetExtraNonce = 48;
    public const int BlockTemplateOffsetMinerPublicKey = 80;

    public const int HashSize = 32;
    public const int BlockWorkSize = 112;

    public const string AlgorithmXelisHashV2 = "xel/v2";

    // Amount in ATOMIC (per KB)
    public const decimal MinimumTransactionFees = 1000;
    public const string TransactionDefaultAsset = "0000000000000000000000000000000000000000000000000000000000000000";
    public const int MaximumDestinationPerTransfer = 255;
}

public static class XelisCommands
{
    public const string DaemonName = "xelis_daemon";

    public const string GetChainInfo = "get_info";
    public const string GetBlockHeader = "get_block_template";
    public const string GetBlockTemplate = "get_miner_work";
    public const string GetBlockByHash = "get_block_by_hash";
    public const string GetDifficulty = "get_difficulty";
    public const string GetStatus = "p2p_status";

    public const string ExtractKeyFromAddress = "extract_key_from_address";
    public const string ValidateAddress = "validate_address";
    public const string SplitAddress = "split_address";

    public const string SubmitBlock = "submit_block";
    
    public const string Subscribe = "subscribe";
    public const string NotifiyNewBlock = "new_block";
}

public static class XelisWalletCommands
{
    public const string DaemonName = "xelis_wallet";

    public const string GetAddress = "get_address";
    public const string GetBalance = "get_balance";

    public const string EstimateFees = "estimate_fees";
    public const string BuildTransaction = "build_transaction";

    public const string IsOnline = "is_online";
    public const string SetOnlineMode = "set_online_mode";
}

public enum XelisRPCErrorCode
{
    // RPC_METHOD_NOT_FOUND is internally mapped to HTTP_NOT_FOUND (404).
    // It should not be used for application-layer errors.
    RPC_INVALID_PARAMS = -32602,
}