using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class GetBlockByHashResponse
{
    [JsonProperty("block_type")]
    public string BlockType { get; set; }

    [JsonProperty("cumulative_difficulty")]
    public double CumulativeDifficulty { get; set; }

    [JsonProperty("dev_reward")]
    public ulong DevReward { get; set; }

    public double Difficulty { get; set; }

    [JsonProperty("extra_nonce")]
    public string ExtraNonce { get; set; }

    public string Hash { get; set; }
    public ulong Height { get; set; }
    public string Miner { get; set; }

    [JsonProperty("miner_reward")]
    public ulong MinerReward { get; set; }

    public string Nonce { get; set; }
    public ulong Reward { get; set; }
    public ulong Supply { get; set; }
    public ulong Timestamp { get; set; }
    public ulong TopoHeight { get; set; }

    [JsonProperty("total_fees")]
    public ulong TotalFees { get; set; }

    public byte Version { get; set; }
}
