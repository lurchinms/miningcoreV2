using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.DaemonRequests;

public class TransferDestination
{
    public string Address { get; set; }
    public ulong Amount { get; set; }

    // Salvium
    /// <summary>
    /// Define the type of coin to be received
    /// </summary>
    [JsonProperty("asset_id", NullValueHandling = NullValueHandling.Ignore)]
    public string AssetType { get; set; }
}

public class TransferRequest
{
    public TransferDestination[] Destinations { get; set; }

    /// <summary>
    /// Fee to be paid on behalf of sender's wallet(paid in native coins)
    /// </summary>
    [JsonProperty("fee", NullValueHandling = NullValueHandling.Ignore)]
    public ulong Fee { get; set; }

    /// <summary>
    /// Number of outpouts from the blockchain to mix with (0 means no mixing)
    /// </summary>
    public uint Mixin { get; set; }

    /// <summary>
    /// (Optional) Random 32-byte/64-character hex string to identify a transaction
    /// </summary>
    [JsonProperty("payment_id", NullValueHandling = NullValueHandling.Ignore)]
    public string PaymentId { get; set; }

    /// <summary>
    /// Text comment that is displayed in UI
    /// </summary>
    [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
    public string Comment { get; set; }

    /// <summary>
    /// Reveal information about sender of this transaction, basically add sender address to transaction in encrypted way, so only receiver can see who sent transaction
    /// </summary>
    [JsonProperty("push_payer")]
    public bool RevealSender { get; set; } = true;

    /// <summary>
    /// This add to transaction information about remote address(destination), might be needed when the wallet restored from seed phrase and fully resynched, if this option were true, then sender won't be able to see remote address for sent transactions anymore.
    /// </summary>
    [JsonProperty("hide_receiver")]
    public bool HideReceiver { get; set; } = false;
}
