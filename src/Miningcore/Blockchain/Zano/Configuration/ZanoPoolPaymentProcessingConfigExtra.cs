namespace Miningcore.Blockchain.Zano.Configuration;

public class ZanoPoolPaymentProcessingConfigExtra
{
    public decimal MinimumPaymentToPaymentId { get; set; }

    /// <summary>
    /// Reveal information about sender of this transaction, basically add sender address to transaction in encrypted way, so only receiver can see who sent transaction
    /// Default: true
    /// </summary>
    public bool RevealPoolAddress { get; set; }

    /// <summary>
    /// This add to transaction information about remote address(destination), might be needed when the wallet restored from seed phrase and fully resynched, if this option were true, then sender won't be able to see remote address for sent transactions anymore.
    /// Default: False
    /// </summary>
    public bool HideMinerAddress { get; set; }

    /// <summary>
    /// Maximum of simultaneous destination address in a single transaction
    /// Default: 256
    /// </summary>
    public int? MaximumDestinationPerTransfer { get; set; }

    /// <summary>
    /// If True, miners pay payment tx fees
    /// Default: False
    /// </summary>
    public bool KeepTransactionFees { get; set; }

    /// <summary>
    /// Maximum amount you're willing to pay (in coin smallest unit)
    /// Default: 10000000000 (0.01)
    /// </summary>
    public ulong? MaxFee { get; set; }
}
