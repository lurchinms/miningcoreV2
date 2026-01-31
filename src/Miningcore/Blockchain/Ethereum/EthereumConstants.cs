using System.Numerics;
using System.Text.RegularExpressions;

namespace Miningcore.Blockchain.Ethereum;

public class EthereumConstants
{
    public const ulong EpochLength = 30000;
    public static BigInteger BigMaxValue = BigInteger.Pow(2, 256);
    public static double Pow2x32 = Math.Pow(2, 32);
    public static BigInteger BigPow2x32 = new(Pow2x32);
    public const int AddressLength = 20;
    public const decimal Wei = 1000000000000000000;
    public static BigInteger WeiBig = new(1000000000000000000);
    public const string EthereumStratumVersion = "EthereumStratum/1.0.0";
    public const decimal StaticTransactionFeeReserve = 0.0025m; // in ETH
    public const string BlockTypeUncle = "uncle";
    public const string BlockTypeBlock = "block";

#if !DEBUG
    public const int MinPayoutPeerCount = 1;
#else
    public const int MinPayoutPeerCount = 1;
#endif

    public static readonly Regex ValidAddressPattern = new("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    public static readonly Regex ZeroHashPattern = new("^0?x?0+$", RegexOptions.Compiled);

    public const ulong ByzantiumHardForkHeight = 4370000;
    public const ulong ConstantinopleHardForkHeight = 7280000;
    public const decimal HomesteadBlockReward = 5.0m;
    public const decimal ByzantiumBlockReward = 3.0m;
    public const decimal ConstantinopleReward = 2.0m;

    public const int MinConfimations = 16;

    public const string RpcRequestWorkerPropertyName = "worker";
}

// ETC block reward distribution - ECIP 1017
// https://ecips.ethereumclassic.org/ECIPs/ecip-1017
public class EthereumClassicConstants
{
    public const ulong HardForkBlockMainnet = 11700000;
    public const ulong HardForkBlockMordor = 2520000;
    public const ulong EpochLength = 60000;
    public const ulong EraLength = 5000001;
    public const double DisinflationRateQuotient = 4.0;
    public const double DisinflationRateDivisor = 5.0;
    public const decimal BaseRewardInitial = 5.0m;
}

// Callisto Monetary Policy
// https://github.com/EthereumCommonwealth/Roadmap/issues/56
public class CallistoConstants
{
    public const decimal BaseRewardInitial = 77.76m;
    public const decimal TreasuryPercent = 50m;
}

public class EthOneConstants
{
    public const decimal BaseRewardInitial = 2.0m;
}

// OCTA block reward distribution - 
// https://docs.octa.space/cryptocurrency/monetary-policy
public class OctaSpaceConstants
{
    public const ulong TriangulumHardForkHeight = 10000000;
    public const decimal TriangulumBlockReward = 1.0m;
    public const ulong VegaHardForkHeight = 8000000;
    public const decimal VegaBlockReward = 1.1m;
    public const ulong BlackeyeHardForkHeight = 6000000;
    public const decimal BlackeyeBlockReward = 1.2m;
    public const ulong DneprHardForkHeight = 4000000;
    public const decimal DneprBlockReward = 1.85m;
    public const ulong MahasimHardForkHeight = 3000000;
    public const decimal MahasimBlockReward = 2.3m;
    public const ulong PolarisHardForkHeight = 2500000;
    public const decimal PolarisBlockReward = 2.8m;
    public const ulong SpringwaterHardForkHeight = 2000000;
    public const decimal SpringwaterBlockReward = 3.0m;
    public const ulong ZagamiHardForkHeight = 1500000;
    public const decimal ZagamiBlockReward = 3.5m;
    public const ulong OldenburgHardForkHeight = 1000000;
    public const decimal OldenburgBlockReward = 4.0m;
    public const ulong ArcturusHardForkHeight = 650000;
    public const decimal ArcturusBlockReward = 5.0m;
    public const decimal BaseRewardInitial = 6.5m;
}

public class PinkConstants
{
    public const decimal BaseRewardInitial = 1.0m;
}

// Hypra
// https://github.com/Rethereum-blockchain/open-rethereum-pool/blob/master/payouts/unlocker.go
public class HypraConstants
{
    public const ulong EpochLength = 32000;
    public const ulong LondonHeight = 15787969;
    public const decimal LondonBlockReward = 3.0m;
    public const ulong ArrowGlacierHeight = 27200177;
    public const decimal ArrowGlacierBlockReward = 2.0m;
    public const ulong GrayGlacierHeight = 40725107;
    public const decimal GrayGlacierBlockReward = 1.0m;
    public const decimal BaseRewardInitial = 4.0m;
}

// UBIQ block reward distribution - 
// https://github.com/ubiq/UIPs/issues/16 - https://ubiqsmart.com/en/monetary-policy
public class UbiqConstants
{
    public const ulong YearOneHeight = 358363;
    public const decimal YearOneBlockReward = 7.0m;
    public const ulong YearTwoHeight = 716727;
    public const decimal YearTwoBlockReward = 6.0m;
    public const ulong YearThreeHeight = 1075090;
    public const decimal YearThreeBlockReward = 5.0m;
    public const ulong YearFourHeight = 1433454;
    public const decimal YearFourBlockReward = 4.0m;
    public const ulong OrionHardForkHeight = 1791793;
    public const decimal OrionBlockReward = 1.5m;
    public const decimal BaseRewardInitial = 8.0m;
}

// CORTEX block reward distribution - 
// https://github.com/CortexFoundation/CortexTheseus/blob/master/params/config.go#L88
public class CortexConstants
{
    public static double Pow2x30 = Math.Pow(2, 30);
    public static BigInteger BigPow2x30 = new(Pow2x30);

    public const ulong BaseBlockRewardPeriod = 8409600; // Halving every four years: 365 days * 24 hours * 60 minutes * 4 blocks * 4 years = 8409600
    public const ulong DoloresBlockRewardPeriod = 1000000;

    public const decimal BaseRewardInitial = 7.0m;

    public const int CuckarooHeaderNonceSize = 40;
    public const int CuckarooSolutionSize = 42;
}

public enum EthereumNetworkType
{
    Main = 1,
    Ropsten = 3,
    Ubiq = 8,
    Classic = 1,
    Mordor = 7,
    Callisto = 820,
    MainPow = 10001,
    EtherOne = 4949,
    Pink = 10100,
    OctaSpace = 800001,
    OctaSpaceTestnet = 800002,
    Hypra = 622277,
    Cortex = 21,
    Dolores = 43,
    Bernard = 42,

    Unknown = -1,
}

public enum GethChainType
{
    Main,
    Ropsten,
    Ubiq,
    Classic,
    Mordor,
    Callisto,
    MainPow = 10001,
    EtherOne = 4949,
    Pink = 10100,
    OctaSpace,
    OctaSpaceTestnet,
    Hypra,
    Cortex = 21,
    Dolores = 43,
    Bernard = 42,
    
    Unknown = -1,
}

public static class EthCommands
{
    public const string GetWork = "_getWork";
    public const string SubmitWork = "_submitWork";
    public const string Sign = "_sign";
    public const string GetNetVersion = "net_version";
    public const string GetClientVersion = "web3_clientVersion";
    public const string GetCoinbase = "_coinbase";
    public const string GetAccounts = "_accounts";
    public const string GetPeerCount = "net_peerCount";
    public const string GetSyncState = "_syncing";
    public const string GetBlockNumber = "_blockNumber";
    public const string GetBlockByNumber = "_getBlockByNumber";
    public const string GetBlockByHash = "_getBlockByHash";
    public const string GetUncleByBlockNumberAndIndex = "_getUncleByBlockNumberAndIndex";
    public const string GetTxReceipt = "_getTransactionReceipt";
    public const string SendTx = "_sendTransaction";
    public const string UnlockAccount = "personal_unlockAccount";
    public const string Subscribe = "_subscribe";
    public const string MaxPriorityFeePerGas = "_maxPriorityFeePerGas";
}
