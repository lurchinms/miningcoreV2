using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.StratumRequests;

public class ZanoGetJobRequest
{
    [JsonProperty("id")]
    public string WorkerId { get; set; }
}
