using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.DaemonResponses;

public class SplitIntegratedAddressResponse
{
    [JsonProperty("standard_address")]
    public string StandardAddress { get; set; }

    /// <summary>
    /// Hex-encoded payment id
    /// </summary>
    [JsonProperty("payment_id")]
    public string Payment { get; set; }
}
