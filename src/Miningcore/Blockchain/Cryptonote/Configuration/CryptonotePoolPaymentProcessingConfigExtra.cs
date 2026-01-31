namespace Miningcore.Blockchain.Cryptonote.Configuration;

public class CryptonotePoolPaymentProcessingConfigExtra
{
    public decimal MinimumPaymentToPaymentId { get; set; }

    /// <summary>
    /// Maximum of simultaneous destination address in a single transaction
    /// Default: 16
    /// </summary>
    public int? MaximumDestinationPerTransfer { get; set; }
}
