using System.Collections.Concurrent;

namespace Offload.Core.Desktop;

/// A job as the queue runs it; `JobQueue.Runner` adapts `OffloadJob`, tests use fakes.
public interface IQueuedJob
{
    bool IsCancelled { get; }
    /// Stage 1 (card → disks + verification). True when stage 2 should follow (or only the day report, ruling I2).
    bool RunCopy();
    /// Stage 2 (clips, proxies, PDF, day report) — reads only the destinations, writes only reports.
    void RunPost();
}

/// Copies run one after another (they share the destination disks); stage 2 runs on its own thread, so the next
/// card copies while the previous one's proxies render (Mac copyQueue + postQueue). Events fire on the worker
/// threads; a job cancelled while queued is skipped but still reported as finished.
public sealed class JobQueue : IDisposable
{
    readonly BlockingCollection<IQueuedJob> copies = new();
    readonly BlockingCollection<IQueuedJob> posts = new();
    readonly Action<string>? log;

    public event Action<IQueuedJob>? CopyFinished;
    public event Action<IQueuedJob>? JobFinished;

    public JobQueue(Action<string>? log = null)
    {
        this.log = log;
        new Thread(CopyLoop) { IsBackground = true, Name = "offload-copy" }.Start();
        new Thread(PostLoop) { IsBackground = true, Name = "offload-post" }.Start();
    }

    public void Enqueue(IQueuedJob job) => copies.Add(job);

    void CopyLoop()
    {
        foreach (var job in copies.GetConsumingEnumerable())
        {
            bool post = false;
            if (!job.IsCancelled)
            {
                post = Guard(job.RunCopy, "copy");
                Raise(CopyFinished, job);
            }
            if (post && TryAddPost(job)) continue;
            Raise(JobFinished, job);
        }
    }

    bool TryAddPost(IQueuedJob job)
    {
        try
        {
            return posts.TryAdd(job);
        }
        catch (InvalidOperationException)
        {
            return false;   // shutting down
        }
    }

    void PostLoop()
    {
        foreach (var job in posts.GetConsumingEnumerable())
        {
            Guard(() => { job.RunPost(); return true; }, "post");
            Raise(JobFinished, job);
        }
    }

    bool Guard(Func<bool> body, string stage)
    {
        try
        {
            return body();
        }
        catch (Exception e)
        {
            log?.Invoke($"{stage}: {e.Message}");
            return false;
        }
    }

    void Raise(Action<IQueuedJob>? handler, IQueuedJob job)
    {
        try
        {
            handler?.Invoke(job);
        }
        catch (Exception e)
        {
            log?.Invoke($"event: {e.Message}");
        }
    }

    /// Stops taking jobs; a job that is running finishes on its own (cancel it first to stop it).
    public void Dispose()
    {
        copies.CompleteAdding();
        posts.CompleteAdding();
    }

    /// The job records which card is in the drive when its copy starts (OffloadJob.SourceIdentity, on the copy
    /// thread — the CLI needs it for history too); "Vysunout kartu" later ejects only that card.
    public sealed class Runner(OffloadJob job, Action<string>? log) : IQueuedJob
    {
        public OffloadJob Job { get; } = job;
        public bool IsCancelled => Job.IsCancelled;
        /// The card that was copied (`CardIdentity`), once the copy has started; null before or when unknown.
        public string? SourceIdentity => Job.SourceIdentity;

        public bool RunCopy() => Job.RunCopy(log);

        public void RunPost() => Job.RunPost(log);
    }
}
