using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Runs chunk-generation jobs with a bounded number of concurrent workers, using the
/// standard .NET ThreadPool instead of spinning up a new System.Threading.Thread per chunk.
///
/// The old code (TerrainManager.BeginGeneration) created a brand-new native OS thread for
/// every single chunk generation request. Under normal streaming that's a few threads at a
/// time, but on the very first frame (EnsureInitialChunks) or after a fast teleport, dozens
/// of chunks can need generation simultaneously — creating dozens of OS threads at once,
/// which is expensive to spin up and contends heavily for CPU cache/cores. This scheduler
/// queues jobs and only ever runs `maxConcurrent` of them at a time; everything else just
/// waits its turn cheaply in a queue.
/// </summary>
public class ChunkGenerationScheduler
{
    readonly int maxConcurrent;
    int active;
    readonly object gate = new object();
    readonly Queue<Action> queue = new Queue<Action>();

    public ChunkGenerationScheduler(int maxConcurrent)
    {
        this.maxConcurrent = maxConcurrent < 1 ? 1 : maxConcurrent;
    }

    public void Enqueue(Action job)
    {
        lock (gate)
        {
            queue.Enqueue(job);
        }
        TryDispatch();
    }

    void TryDispatch()
    {
        lock (gate)
        {
            while (active < maxConcurrent && queue.Count > 0)
            {
                Action job = queue.Dequeue();
                active++;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        job();
                    }
                    finally
                    {
                        lock (gate) { active--; }
                        TryDispatch();
                    }
                });
            }
        }
    }
}