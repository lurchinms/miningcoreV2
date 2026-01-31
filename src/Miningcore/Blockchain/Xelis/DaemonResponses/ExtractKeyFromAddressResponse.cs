using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class ExtractKeyFromAddressResponse
{
    [JsonProperty("hex")]
    public string PublicKey { get; set; }
}
