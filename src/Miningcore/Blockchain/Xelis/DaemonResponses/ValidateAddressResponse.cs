using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class ValidateAddressResponse
{
    [JsonProperty("is_integrated")]
    public bool IsIntegrated { get; set; }

    [JsonProperty("is_valid")]
    public bool IsValid { get; set; }
}
