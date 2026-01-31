using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonRequests;

public class GetBlockTemplateRequest
{
    [JsonProperty("template")]
    public string BlockHeader { get; set; }

    public string Address { get; set; }
}
