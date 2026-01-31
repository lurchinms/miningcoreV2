using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonRequests;

public class ExtractKeyFromAddressRequest
{
    public string Address { get; set; }

    [JsonProperty("as_hex")]
    public bool AsHexadecimal { get; set; } = true;
}
