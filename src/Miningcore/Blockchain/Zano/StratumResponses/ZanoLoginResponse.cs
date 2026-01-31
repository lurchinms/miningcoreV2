using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.StratumResponses;

public class ZanoJobParams
{
    [JsonProperty("job_id")]
    public string JobId { get; set; }

    public string Blob { get; set; }
    public string Target { get; set; }

    [JsonProperty("seed_hash")]
    public string SeedHash { get; set; }

    [JsonProperty("algo")]
    public string Algorithm { get; set; }

    /// <summary>
    /// Introduced for CNv4 (aka CryptonightR)
    /// </summary>
    public ulong Height { get; set; }
}

public class ZanoLoginResponse : ZanoResponseBase
{
    public string Id { get; set; }
    public ZanoJobParams Job { get; set; }
}
