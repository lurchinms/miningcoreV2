using Miningcore.Configuration;

namespace Miningcore.Blockchain.Xelis.Configuration;

public class XelisPoolConfigExtra
{
    /// <summary>
    /// Maximum number of tracked jobs.
    /// Default: 8
    /// </summary>
    public int? MaxActiveJobs { get; set; }

    public int? ExtraNonce1Size { get; set; }
}