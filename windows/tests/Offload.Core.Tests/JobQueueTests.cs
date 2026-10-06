using System.Collections.Concurrent;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

public class JobQueueTests
{
    /// Generous: every wait is for an event, so a passing test never sleeps this long, but a loaded full suite
    /// (proxy renders, copies in parallel) can delay a queue thread by seconds.
    static readonly TimeSpan Wait = TestAssembly.JobWait;   // fix round 2, item 9: longer than a PDF may take

    sealed class FakeJob(string name, bool post = false) : IQueuedJob
    {
        public readonly string Name = name;
        public readonly ManualResetEventSlim CopyStarted = new(), CopyGate = new(true), PostStarted = new(), PostGate = new(true);
        public volatile bool Cancelled;
        public Exception? Throw;
        public int CopyCalls;
        public bool IsCancelled => Cancelled;

        public bool RunCopy()
        {
            Interlocked.Increment(ref CopyCalls);
            CopyStarted.Set();
            CopyGate.Wait(Wait);
            if (Throw != null) throw Throw;
            return post;
        }

        public void RunPost()
        {
            PostStarted.Set();
            PostGate.Wait(Wait);
        }
    }

    [Fact]
    public void Copies_RunOneAfterAnother()
    {
        using var q = new JobQueue();
        var a = new FakeJob("a");
        var b = new FakeJob("b");
        a.CopyGate.Reset();
        q.Enqueue(a);
        q.Enqueue(b);
        Assert.True(a.CopyStarted.Wait(Wait));
        Assert.False(b.CopyStarted.Wait(300));
        a.CopyGate.Set();
        Assert.True(b.CopyStarted.Wait(Wait));
    }

    [Fact]
    public void NextCopy_RunsWhilePreviousProxiesRender()
    {
        using var q = new JobQueue();
        var a = new FakeJob("a", post: true);
        var b = new FakeJob("b");
        a.PostGate.Reset();
        q.Enqueue(a);
        q.Enqueue(b);
        Assert.True(a.PostStarted.Wait(Wait));
        Assert.True(b.CopyStarted.Wait(Wait));
        a.PostGate.Set();
    }

    [Fact]
    public void Events_CopyFinishedBeforeJobFinished_ForEveryJob()
    {
        using var q = new JobQueue();
        var events = new ConcurrentQueue<string>();
        using var done = new CountdownEvent(2);
        q.CopyFinished += j => events.Enqueue("copy:" + ((FakeJob)j).Name);
        q.JobFinished += j => { events.Enqueue("done:" + ((FakeJob)j).Name); done.Signal(); };
        q.Enqueue(new FakeJob("a"));
        q.Enqueue(new FakeJob("c", post: true));
        Assert.True(done.Wait(Wait));
        var list = events.ToList();
        Assert.True(list.IndexOf("copy:a") < list.IndexOf("done:a"));
        Assert.True(list.IndexOf("copy:c") < list.IndexOf("done:c"));
    }

    // Review focus 5
    [Fact]
    public void CancelledBeforeStart_Skipped_ButFinished()
    {
        using var q = new JobQueue();
        var a = new FakeJob("a") { Cancelled = true };
        var copied = false;
        using var finished = new ManualResetEventSlim();
        q.CopyFinished += _ => copied = true;
        q.JobFinished += _ => finished.Set();
        q.Enqueue(a);
        Assert.True(finished.Wait(Wait));
        Assert.Equal(0, a.CopyCalls);
        Assert.False(copied);
    }

    // Review focus 5
    [Fact]
    public void ThrowingJob_DoesNotStopTheQueue()
    {
        var log = new ConcurrentQueue<string>();
        using var q = new JobQueue(log.Enqueue);
        var a = new FakeJob("a") { Throw = new InvalidOperationException("boom") };
        var b = new FakeJob("b");
        using var bDone = new ManualResetEventSlim();
        q.JobFinished += j => { if (j == b) bDone.Set(); };
        q.Enqueue(a);
        q.Enqueue(b);
        Assert.True(bDone.Wait(Wait));
        Assert.Contains(log, l => l.Contains("boom"));
    }

    [Fact]
    public void Runner_RunsARealOffloadJob()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(card, "A001.MOV"), 100_000, 1);
        var config = new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-05", AnalyzeClips = false };
        var job = new OffloadJob(config, new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        using var q = new JobQueue();
        using var finished = new ManualResetEventSlim();
        q.JobFinished += _ => finished.Set();
        q.Enqueue(new JobQueue.Runner(job, null));
        // Completion, not speed: ~9 s alone, 27 s+ in the parallel full suite on a busy PC — 30 s was too tight.
        Assert.True(finished.Wait(TimeSpan.FromSeconds(120)));
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(File.Exists(Path.Combine(dir.Path, "d1", "2026-10-05", "card", "A001.MOV")));
    }
}
