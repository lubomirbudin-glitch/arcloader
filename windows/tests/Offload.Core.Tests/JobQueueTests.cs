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

    // Ruling I2: the day report of a job without stage 2 is made on the post thread, after the append lock is released —
    // while another writer holds the day, the next card copies and the append's card folders are free.
    [Fact]
    public void DayReport_OnThePostThread_AfterTheAppendLock()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var d1 = dir.Sub("d1");
        OffloadJob Job(string card, IReadOnlyList<string> dests, IReadOnlyList<string>? into = null) =>
            new(new JobConfig(card, dests) { Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, AppendInto = into }, AppendFixtures.Options());
        var cardA = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(cardA, "CLIP", "A001C001.mov"), 10_000, 1);
        var full = Job(cardA, [d1]);
        full.Run();
        var cardPaths = full.Plan!.CardPaths;
        AppendFixtures.NewClip(cardA);
        var cardB = dir.Sub("B001");
        TestFs.WriteRandom(Path.Combine(cardB, "CLIP", "B001C001.mov"), 10_000, 2);
        var append = new JobQueue.Runner(Job(cardA, [], cardPaths), null);
        var next = new JobQueue.Runner(Job(cardB, [d1]), null);

        // Another writer holds the day (a CLI day-report, the post thread of an earlier card).
        var day = Path.Combine(d1, "2026-10-07");
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            Outputs.DayReportService.WhileHeld = _ => { inside.Set(); release.Wait(); };
            try { Outputs.DayReportService.RegenerateDay(day, new Outputs.OutputSettings()); }
            finally { Outputs.DayReportService.WhileHeld = null; }
        });
        holder.Start();
        Assert.True(inside.Wait(Wait));

        using var q = new JobQueue();
        using var nextCopied = new ManualResetEventSlim();
        using var appendFinished = new ManualResetEventSlim();
        q.CopyFinished += j => { if (j == next) nextCopied.Set(); };
        q.JobFinished += j => { if (j == append) appendFinished.Set(); };
        try
        {
            q.Enqueue(append);
            q.Enqueue(next);
            Assert.True(nextCopied.Wait(Wait));
            Assert.False(appendFinished.IsSet);   // its day report still waits for the day
            Assert.Equal(JobPhase.Done, append.Job.Status().Phase);
            Assert.Equal(JobPhase.Done, next.Job.Status().Phase);
            using (Append.AppendLock.Acquire(cardPaths)) { }   // the append's card folders are free again
        }
        finally
        {
            release.Set();
            holder.Join();
        }
        Assert.True(appendFinished.Wait(Wait));
        var html = File.ReadAllText(Assert.Single(append.Job.DayReports));
        Assert.Contains("<td>B001</td>", html);   // made after the next card's copy
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
