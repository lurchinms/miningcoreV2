namespace Miningcore.Blockchain.Xelis;

/*
 * Xelis Stratum RPC protocol: https://docs.xelis.io/developers-api/stratum
 */
public class XelisStratumMethods
{
    /// <summary>
    /// Used to subscribe to work from a server, required before all other communication.
    /// </summary>
    public const string Subscribe = "mining.subscribe";
    
    /// <summary>
    /// Used to authorize a worker, required before any shares can be submitted.
    /// </summary>
    public const string Authorize = "mining.authorize";

    /// <summary>
    /// Used to push new work to the miner.
    /// </summary>
    public const string MiningNotify = "mining.notify";

    /// <summary>
    /// Used to submit shares
    /// </summary>
    public const string SubmitShare = "mining.submit";

    /// <summary>
    /// Used to signal the miner to stop submitting shares under the new difficulty.
    /// </summary>
    public const string SetDifficulty = "mining.set_difficulty";

    /// <summary>
    /// Used to subscribe to work from a server, required before all other communication.
    /// </summary>
    public const string SetExtraNonce = "mining.set_extranonce";
    
    /// <summary>
    /// Used to check if a miner connection is still alive.
    /// </summary>
    public const string Ping = "mining.ping";
    
    /// <summary>
    /// Used to signify that the miner connection is still alive.
    /// </summary>
    public const string Pong = "mining.pong";

    /// <summary>
    /// Used to send a message to the miner to print on screen.
    /// </summary>
    public const string Print = "mining.print";

    /// <summary>
    /// Used to submit the reported hashrate (in miner) to the pool (similar to eth_submitHashrate in ethash).
    /// </summary>
    public const string SubmitHashrate = "mining.hashrate";
}