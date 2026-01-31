using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.DaemonResponses;

public class TransferResponse
{
    /// <summary>
    /// Publically searchable transaction hash
    /// </summary>
    [JsonProperty("tx_hash")]
    public string TxHash { get; set; }

    /// <summary>
    /// Raw transaction represented as hex string. For cold-signing process
    /// </summary>
    [JsonProperty("tx_unsigned_hex")]
    public string TxUnsignedHex { get; set; }

    /// <summary>
    /// Transaction size in bytes
    /// </summary>
    [JsonProperty("tx_size")]
    public string TxSize { get; set; }
}
