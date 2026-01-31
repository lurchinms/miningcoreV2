using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using Miningcore.Mining;

namespace Miningcore.Blockchain.Kaspa;

public class KaspaWorkerContext : WorkerContextBase
{
    /// <summary>
    /// Usually a wallet address
    /// </summary>
    public override string Miner { get; set; }

    /// <summary>
    /// Arbitrary worker identififer for miners using multiple rigs
    /// </summary>
    public override string Worker { get; set; }

    /// <summary>
    /// Unique value assigned per worker
    /// </summary>
    public string ExtraNonce1 { get; set; }
    
    /// <summary>
    /// Some mining software require job to be sent in a specific way, we need to be able to identify them
    /// Default: false
    /// </summary>
    public bool IsLargeJob { get; set; } = false;

    /// <summary>
    /// Current N job(s) assigned to this worker
    /// </summary>
    public Queue<KaspaJob> validJobs { get; private set; } = new();

    public virtual void AddJob(KaspaJob job, int maxActiveJobs)
    {
        if(!validJobs.Contains(job))
            validJobs.Enqueue(job);

        while(validJobs.Count > maxActiveJobs)
            validJobs.Dequeue();
    }

    public KaspaJob GetJob(string jobId)
    {
        return validJobs.ToArray().FirstOrDefault(x => x.JobId == jobId);
    }
}