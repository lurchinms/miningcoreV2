using Newtonsoft.Json;

namespace Miningcore.Blockchain.Zano.DaemonResponses;

public class GetBalanceResponse
{
    public decimal Balance { get; set; }

    [JsonProperty("unlocked_balance")]
    public decimal UnlockedBalance { get; set; }

    public BalanceAsset[] Balances { get; set; }
}

public class BalanceAsset
{
    [JsonProperty("asset_info")]
    public BalanceAssetInfo Asset { get; set; }

    [JsonProperty("total")]
    public decimal Balance { get; set; }

    [JsonProperty("unlocked")]
    public decimal UnlockedBalance { get; set; }
}

public class BalanceAssetInfo
{
    [JsonProperty("asset_id")]
    public string Id { get; set; }

    [JsonProperty("full_name")]
    public string Name { get; set; }

    public string Ticker { get; set; }
}
