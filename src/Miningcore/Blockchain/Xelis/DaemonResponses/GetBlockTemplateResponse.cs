using Newtonsoft.Json;

namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class GetBlockTemplateResponse
{
    public string Algorithm { get; set; }
    public double Difficulty { get; set; }
    public ulong Height { get; set; }

    /*
     * https://github.com/xelis-project/xelis-blockchain/blob/master/xelis_common/src/block/header.rs#L175
     * Block Template (Miner Work) can be serialized/deserialized using following order on byte array:
     * ------------------------------------------------------------
     * 32 bytes for header work (immutable part in mining process)
     * 8 bytes for timestamp (u64) big endian format (this space is free and can be used to spread more the work or write anything)
     * 8 bytes for nonce (u64) big endian format (this space is free and can be used to spread more the work or write anything)
     * 32 bytes for extra nonce (this space is free and can be used to spread more the work or write anything)
     * 32 bytes for miner public key
     * ------------------------------------------------------------
     */
    [JsonProperty("miner_work")]
    public string Template { get; set; }

    public ulong TopoHeight { get; set; }
}
