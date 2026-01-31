using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.StratumRequests;

public class ZanoLoginRequest
{
    [JsonProperty("login")]
    public string Login { get; set; }

    [JsonProperty("pass")]
    public string Password { get; set; }

    [JsonProperty("agent")]
    public string UserAgent { get; set; }
}
