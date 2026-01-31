using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class GetStatusResponse
{
    [JsonProperty("best_topoheight")]
    public ulong BestTopoHeight { get; set; }

    [JsonProperty("max_peers")]
    public uint MaxPeers { get; set; }

    [JsonProperty("median_topoheight")]
    public ulong MedianTopoHeight { get; set; }

    [JsonProperty("our_topoheight")]
    public ulong TopoHeight { get; set; }

    [JsonProperty("peer_count")]
    public int PeerCount { get; set; }

    [JsonProperty("peer_id")]
    public string PeerId { get; set; }
}
