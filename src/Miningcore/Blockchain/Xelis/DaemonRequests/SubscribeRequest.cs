using Newtonsoft.Json;
namespace Miningcore.Blockchain.Xelis.DaemonRequests;

public class SubscribeRequest
{
    [JsonProperty("notify")]
    public string Notify { get; set; }
}
