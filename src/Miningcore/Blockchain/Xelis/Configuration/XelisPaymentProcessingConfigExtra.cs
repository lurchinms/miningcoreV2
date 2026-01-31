namespace Miningcore.Blockchain.Xelis.Configuration;

public class XelisPaymentProcessingConfigExtra
{
    /// <summary>
    /// Minimum block confirmations
    /// Default: "Mainnet" (60) - "Testnet" (50)
    /// </summary>
    public int? MinimumConfirmations { get; set; }

    /// <summary>
    /// Maximum of simultaneous destination address in a single transaction
    /// Default: 255
    /// </summary>
    public int? MaximumDestinationPerTransfer { get; set; }

    /// <summary>
    /// If True, miners pay payment tx fees
    /// Default: False
    /// </summary>
    public bool KeepTransactionFees { get; set; }
}