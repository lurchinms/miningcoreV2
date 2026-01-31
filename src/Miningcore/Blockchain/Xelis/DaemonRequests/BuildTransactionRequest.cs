using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonRequests;

public class BuildTransactionTransfer
{
    public ulong Amount { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string Asset { get; set; }

    public string Destination { get; set; }

    [JsonProperty("extra_data", NullValueHandling = NullValueHandling.Ignore)]
    public string ExtraData { get; set; }
}

public class BuildTransactionFee
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public double? Multiplier { get; set; }

    [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
    public ulong? Amount { get; set; }
}

public class BuildTransactionRequest
{
    public BuildTransactionTransfer[] Transfers { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public BuildTransactionFee Fee { get; set; }

    public bool broadcast { get; set; } = true;

    [JsonProperty("tx_as_hex")]
    public bool TransactionAsHexadecimal { get; set; } = true;
}

public class EstimateFeesRequest
{
    public BuildTransactionTransfer[] Transfers { get; set; }
}
