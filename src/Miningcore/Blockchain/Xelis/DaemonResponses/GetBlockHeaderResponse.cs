namespace Miningcore.Blockchain.Xelis.DaemonResponses;

public class GetBlockHeaderResponse
{
    public string Algorithm { get; set; }
    public double Difficulty { get; set; }
    public ulong Height { get; set; }

    /*
     * https://github.com/xelis-project/xelis-blockchain/blob/master/xelis_common/src/block/header.rs#L198
     * Block Header can be serialized/deserialized using following order on byte array:
     * ------------------------------------------------------------
     * 1 byte for version
     * 8 bytes for height (u64) big endian format
     * 8 bytes for timestamp (u64) big endian format
     * 8 bytes for nonce (u64) big endian format
     * 32 bytes for extra nonce (this space is free and can be used to spread more the work or write anything)
     * 1 byte for tips count
     * 32 bytes per hash (count of elements is based on previous byte)
     * 32 bytes for miner public key
     * ------------------------------------------------------------
     */
    public string Template { get; set; }

    public ulong TopoHeight { get; set; }
}
