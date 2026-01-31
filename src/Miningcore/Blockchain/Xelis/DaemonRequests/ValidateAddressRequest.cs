using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonRequests;

public class ValidateAddressRequest
{
    public string Address { get; set; }

    [JsonProperty("allow_integrated")]
    public bool AllowIntegrated { get; set; } = true;
}
