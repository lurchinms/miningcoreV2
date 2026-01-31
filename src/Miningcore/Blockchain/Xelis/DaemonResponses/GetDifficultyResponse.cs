using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class GetDifficultyResponse
{
    public double Difficulty { get; set; }
    public double Hashrate { get; set; }

    [JsonProperty("hashrate_formatted")]
    public string HashrateFormatted { get; set; }
}
