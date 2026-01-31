using System.Globalization;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Math;

namespace Miningcore.Blockchain.Zano;

public enum ZanoNetworkType
{
    Main = 1,
    Test
}

public static class ZanoConstants
{
    public const int EpochLength = 30000;

    public const string WalletDaemonCategory = "wallet";

    public const string DaemonRpcLocation = "json_rpc";
    public const int RpcMethodNotFound = -32601;
    public const int PaymentIdHexLength = 64;
    public static readonly Regex RegexValidNonce = new("^[0-9a-f]{16}$", RegexOptions.Compiled);

    public static readonly BigInteger Diff1 = new("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);
    public static readonly System.Numerics.BigInteger Diff1b = System.Numerics.BigInteger.Parse("00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);
    public static double DifficultyPowTarget = 120.0d;

#if DEBUG
    public const int PayoutMinBlockConfirmations = 2;
#else
        public const int PayoutMinBlockConfirmations = 60;
#endif

    public const int InstanceIdSize = 4;
    public const int ExtraNonceSize = 4;

    public const int BlockTemplateReservedOffset = 1;
    public const int BlockTemplateInstanceIdOffset = 8;
    public const int BlockTemplateExtraNonceOffset = 12;

    public const int EncodeBlobSize = 32;
    public const int TargetPaddingLength = 32;

    // NOTE: for whatever strange reason only reserved_size -1 can be used,
    // the LAST byte MUST be zero or nothing works
    public const int ReserveSize = ExtraNonceSize + InstanceIdSize + 1;

    // Offset to prevHash in block blob
    public const int BlobPrevHashOffset = 9;

    // Offset to nonce in block blob
    public const int BlobNonceOffset = 39;

    public const ulong MinimumTransactionFee = 10000000000; // in zano smallest unit
}

public static class ZanoCommands
{
    // https://github.com/hyle-team/zano/blob/master/src/rpc/core_rpc_server.h
    // https://github.com/hyle-team/zano/blob/master/src/rpc/core_rpc_server_commands_defs.h
    public const string GetInfo = "getinfo";
    public const string GetBlockTemplate = "getblocktemplate";
    public const string SubmitBlock = "submitblock";
    public const string GetBlockHeaderByHash = "getblockheaderbyhash";
    public const string GetBlockHeaderByHeight = "getblockheaderbyheight";
}

public static class ZanoWalletCommands
{
    // https://github.com/hyle-team/zano/blob/master/src/wallet/wallet_rpc_server.h
    // https://github.com/hyle-team/zano/blob/master/src/rpc/core_rpc_server_commands_defs.h

    public const string GetBalance = "getbalance";
    public const string GetAddress = "getaddress";
    public const string Transfer = "transfer";
    public const string TransferSplit = "transfer_split";
    public const string GetTransfers = "get_payments";
    public const string SplitIntegratedAddress = "split_integrated_address";
    public const string Store = "store";
}
