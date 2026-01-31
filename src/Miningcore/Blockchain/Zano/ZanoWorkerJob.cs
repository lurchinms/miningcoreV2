using System.Collections.Concurrent;

namespace Miningcore.Blockchain.Zano;

public class ZanoWorkerJob
{
    public ZanoWorkerJob(string jobId, double difficulty)
    {
        Id = jobId;
        Difficulty = difficulty;
    }

    public string Id { get; }
    public string Height { get; set; }
    public uint ExtraNonce { get; set; }
    public double Difficulty { get; set; }
    public string Target { get; set; }
    public string SeedHash { get; set; }

    public readonly ConcurrentDictionary<string, bool> Submissions = new(StringComparer.OrdinalIgnoreCase);
}
