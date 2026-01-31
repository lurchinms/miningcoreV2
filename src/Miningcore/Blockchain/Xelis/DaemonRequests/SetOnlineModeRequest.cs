using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonRequests;

public class SetOnlineModeRequest
{
    [JsonProperty("daemon_address")]
    public string DaemonAddress { get; set; }

    [JsonProperty("auto_reconnect")]
    public bool AutoReconnect { get; set; } = true;
}
