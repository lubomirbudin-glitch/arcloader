using Offload.Core.Append;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Pozastavit on the whole job: stage 2 waits between clips (no tool is ever suspended), no day or history lock is held
/// while paused, an append keeps its folder lock, cancel (closing the app) while paused ends at once. The PC is kept
/// awake while paused and released on cancel, finish and crash (ruling M6); a job that ended stops the paused-time
/// clock (M1). Ruling I4: promptness is asserted at the gate, whole runs get 60 s — the class is on the "rerun alone
/// once" list.
public class JobPauseTests
{
    static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    static bool Until(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(30));

    /// Stands in for NativeIO.KeepAwake (ruling M6): how many "keep the PC awake" requests are on now.
    sealed class Awake
    {
        int active;
        public int Active => Volatile.Read(ref active);

        public IDisposable Take()
        {
            Interlocked.Increment(ref active);
            return new Release(this);
        }

        sealed class Release(Awake owner) : IDisposable
        {
            int done;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref done, 1) == 0) Interlocked.Decrement(ref owner.active);
            }
        }
    }

    /// Card with three clips; stage 2 without tools (each clip is a quick "ffprobe nenalezen").
    static (OffloadJob Job, string Dest) ThreeClips(TempDir dir, Action<OffloadJob>? configure = null, Awake? awake = null,
        OffloadOptions? options = null)
    {
        var card = dir.Sub("A001");
        for (int i = 1; i <= 3; i++) TestFs.WriteRandom(Path.Combine(card, "CLIP", $"A001C00{i}.mov"), 20_000, i);
        var dest = dir.Sub("d1");
        var job = new OffloadJob(new JobConfig(card, [dest])
        {
            Date = "2026-10-07", Tools = new ExternalTools { NoSearch = true }, HistoryFolder = dir.Sub("history"),
            Outputs = new OutputSettings { DayReport = true },
        }, options ?? Options())
        {
            KeepAwake = awake != null ? awake.Take : NativeIO.KeepAwake,
        };
        configure?.Invoke(job);
        return (job, dest);
    }

    [Fact]
    public async Task Stage2_PausedBeforeTheNextClip_ResumeFinishes()
    {
        using var dir = new TempDir();
        var awake = new Awake();
        var (job, _) = ThreeClips(dir, awake: awake);
        Assert.True(job.RunCopy());
        Assert.Equal(0, awake.Active);           // stage 1 released its own
        job.Pause();
        Assert.False(job.Status().PauseHeld);   // asked, the post thread has not reached the gate yet
        var post = TestThreads.Start(() => job.RunPost());

        Assert.True(Until(() => job.Status().PauseHeld));
        var held = job.Status();
        Assert.Equal((JobPhase.Analyzing, 0, 3), (held.Phase, held.ClipIndex, held.ClipCount));
        Assert.True(held.Paused);
        Assert.Equal(1, awake.Active);           // M6: the PC stays awake while the job stands
        Thread.Sleep(300);
        Assert.Equal(JobPhase.Analyzing, job.Status().Phase);
        Assert.False(post.IsCompleted);
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(60));

        var done = job.Status();
        Assert.Equal(JobPhase.Done, done.Phase);
        Assert.False(done.Paused);
        Assert.True(done.PausedTotal >= TimeSpan.FromMilliseconds(300));
        Assert.Equal(3, job.Report!.Clips.Count);
        Assert.Equal(0, awake.Active);           // M6: released when the job finished
    }

    // The day report and history locks are taken only for a moment and never while waiting: another job of the same
    // day makes its day report while this one stands paused.
    [Fact]
    public async Task PausedInStage2_HoldsNoDayLock()
    {
        using var dir = new TempDir();
        var (job, dest) = ThreeClips(dir);
        Assert.True(job.RunCopy());
        job.Pause();
        var post = TestThreads.Start(() => job.RunPost());
        Assert.True(Until(() => job.Status().PauseHeld));

        var html = DayReportService.Regenerate(dest, "2026-10-07", new OutputSettings(), wait: TimeSpan.FromSeconds(2));

        Assert.True(File.Exists(html));
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(60));
    }

    // Closing the app cancels every job: a paused one leaves the gate at once, writes its PDF and day report as a
    // cancelled job does and ends Zrušeno. Ruling I4: promptness = the gate is left within 5 s; the rest has 60 s.
    // M1: the paused-time clock stops with the job; M6: the PC is released.
    [Fact]
    public async Task CancelWhilePausedInStage2_EndsCancelledPromptly()
    {
        using var dir = new TempDir();
        var awake = new Awake();
        var (job, _) = ThreeClips(dir, awake: awake);
        Assert.True(job.RunCopy());
        job.Pause();
        var post = TestThreads.Start(() => job.RunPost());
        Assert.True(Until(() => job.Status().PauseHeld));
        Assert.Equal(1, awake.Active);

        job.Cancel();

        Assert.True(SpinWait.SpinUntil(() => job.Status() is { PauseHeld: false, Phase: not JobPhase.Analyzing }, TimeSpan.FromSeconds(5)),
            "the cancelled job did not leave the pause promptly");
        await post.WaitAsync(TimeSpan.FromSeconds(60));
        var s = job.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.False(s.Paused);
        Assert.True(s.SafeToEject);   // the copy itself was complete and verified before the pause
        Assert.Equal(0, awake.Active);
        AssertClockStopped(job);
    }

    // Stage 1: cancel while paused (closing the app) releases the PC and stops the paused-time clock (M1, M6).
    [Fact]
    public async Task CancelWhilePausedInStage1_ReleasesAwake_StopsTheClock()
    {
        using var dir = new TempDir();
        var awake = new Awake();
        var options = Options();
        var (job, _) = ThreeClips(dir, awake: awake, options: options);
        options.BeforeFileCopy = i => { if (i == 1) job.Pause(); };

        var copy = TestThreads.Start(() => job.RunCopy());
        Assert.True(Until(() => job.Status().PauseHeld));
        Assert.Equal(1, awake.Active);           // M6: the PC stays awake while the copy stands
        job.Cancel();
        Assert.True(SpinWait.SpinUntil(() => !job.Engine.Snapshot().PauseHeld, TimeSpan.FromSeconds(5)),
            "the cancelled copy did not leave the pause promptly");
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        Assert.Equal(0, awake.Active);
        AssertClockStopped(job);
    }

    // A crash of the job while it stood paused (here a failure right after Pokračovat) still releases the PC (M6) and
    // stops the clock. A crash of the whole process ends its thread's request with it (SetThreadExecutionState).
    [Fact]
    public async Task CrashAfterAPause_ReleasesAwake()
    {
        using var dir = new TempDir();
        var awake = new Awake();
        var options = Options();
        var (job, _) = ThreeClips(dir, awake: awake, options: options);
        options.BeforeFileCopy = i =>
        {
            if (i == 1) job.Pause();
            if (i == 2) throw new InvalidOperationException("boom");
        };

        var copy = TestThreads.Start(() => job.RunCopy());
        Assert.True(Until(() => job.Status().PauseHeld));
        Assert.Equal(1, awake.Active);
        job.Resume();
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal((JobPhase.Failed, "boom"), (job.Status().Phase, job.Status().Error));
        Assert.Equal(0, awake.Active);
        AssertClockStopped(job);
    }

    /// M1: a job that ended keeps its paused time — it does not grow, and Pozastavit on it changes nothing.
    static void AssertClockStopped(OffloadJob job)
    {
        var total = job.Status().PausedTotal;
        Assert.True(total > TimeSpan.Zero);
        Thread.Sleep(300);
        Assert.Equal(total, job.Status().PausedTotal);
        job.Pause();
        Thread.Sleep(100);
        Assert.False(job.IsPaused);
        Assert.Equal(total, job.Status().PausedTotal);
    }

    // An append keeps its card folders locked while paused (they are half-written); after Pokračovat it finishes and
    // the lock is free again.
    [Fact]
    public async Task PausedAppend_KeepsTheFolderLock()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var options = Options();
        OffloadJob? job = null;
        options.BeforeFileCopy = _ => job!.Pause();
        job = new OffloadJob(new JobConfig(card, []) { AppendInto = paths, AnalyzeClips = false, HistoryFolder = dir.Sub("history") }, options);

        var copy = TestThreads.Start(() => job.RunCopy());
        Assert.True(Until(() => job.Status().PauseHeld));
        var busy = Assert.Throws<OffloadError>(() => AppendLock.Acquire(paths));
        Assert.Equal(PreflightCode.AppendBusy, busy.Code);
        options.BeforeFileCopy = null;
        job.Resume();
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        AppendLock.Acquire(paths).Dispose();
    }

    [Fact]
    public void PauseOnAWaitingOrFinishedJob_ShowsNothing()
    {
        using var dir = new TempDir();
        var (job, _) = ThreeClips(dir);
        job.Pause();
        Assert.False(job.Status().Paused);   // waiting: the card shows no pause
        job.Resume();
        job.Run();
        job.Pause();
        Assert.False(job.Status().Paused);   // finished
    }

    // No ffmpeg is suspended: a clip being rendered finishes, the job stands before the next one, and the stall watchdog
    // (here 2 s) never fires however long the pause lasts.
    [FfmpegFact]
    public async Task ProxyRendering_FinishesTheClip_ThenStands_WatchdogNeverFires()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "A001C001.mov"), seconds: 4);
        TestMedia.MakeClip(Path.Combine(card, "A001C002.mov"), seconds: 4);
        var options = Options();
        options.ProxyEncoder = "libx264";
        options.ProxyStallTimeout = TimeSpan.FromSeconds(2);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Date = "2026-10-07", HistoryFolder = dir.Sub("history"), Proxy = new ProxySettings { Enabled = true, Height = 360 },
        }, options);
        Assert.True(job.RunCopy());
        var post = TestThreads.Start(() => job.RunPost());
        Assert.True(Until(() => job.Status() is { Phase: JobPhase.Proxies, ClipIndex: 0 }));
        job.Pause();

        Assert.True(Until(() => job.Status().PauseHeld));
        var held = job.Status();
        Assert.Equal(1, held.ProxiesDone);       // the clip being rendered finished
        Assert.Equal(0, held.ClipIndex);         // and the second one has not started
        Thread.Sleep(3000);                      // longer than the watchdog's limit
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(120));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.All(job.Report!.Clips, c => Assert.Equal(ProxyStatus.Done, c.ProxyStatus));
        Assert.All(job.Report.Clips, c => Assert.Null(c.ProxyNote));
    }
}
