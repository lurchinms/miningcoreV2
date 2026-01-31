using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class GetChainInfoResponse
{
    [JsonProperty("average_block_time")]
    public ulong AverageBlockTime { get; set; }

    [JsonProperty("block_reward")]
    public ulong BlockReward { get; set; }

    [JsonProperty("block_time_target")]
    public ulong BlockTimeTarget { get; set; }

    [JsonProperty("circulating_supply")]
    public ulong CirculatingSupply { get; set; }

    [JsonProperty("dev_reward")]
    public ulong DevReward { get; set; }

    public ulong Height { get; set; }

    [JsonProperty("maximum_supply")]
    public ulong MaximumSupply { get; set; }

    [JsonProperty("miner_reward")]
    public ulong MinerReward { get; set; }

    public string Network { get; set; }
    public ulong StableHeight { get; set; }

    [JsonProperty("top_block_hash")]
    public string TopBlockHash { get; set; }

    public ulong TopoHeight { get; set; }
    public string Version { get; set; }
}
