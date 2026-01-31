using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.DaemonRequests;

public class GetBlockTemplateRequest
{
    /// <summary>
    /// Address of wallet to receive coinbase transactions if block is successfully mined.
    /// </summary>
    [JsonProperty("wallet_address")]
    public string WalletAddress { get; set; }

    [JsonProperty("reserve_size")]
    public uint ReserveSize { get; set; }

    [JsonProperty("extra_text")]
    public string ExtraData { get; set; }
}
