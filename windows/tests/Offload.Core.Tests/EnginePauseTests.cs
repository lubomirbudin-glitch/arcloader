using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Pozastavit in stage 1: the copy and the read-back stop between blocks, the report leaves the pause out of the
/// speeds, a cancel or a disk pulled out while paused end as they do without a pause, a card swapped while paused is
/// never copied into the backup (ruling I1). Ruling I4: promptness is asserted where it is decided (leaving the gate);
/// the whole run gets 60 s and every other wait at least 30 s — the class is on the "rerun alone once" list.
public class EnginePauseTests
{
    const int Chunk = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Chunk, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    /// A card with `count` files of `size` bytes.
    static string Card(TempDir dir, int count = 3, int size = 3 * Chunk + 5)
    {
        var card = dir.Sub("A001");
        for (int i = 0; i < count; i++) TestFs.WriteRandom(Path.Combine(card, "CLIP", $"A001C{i + 1:000}.mov"), size, i + 1);
        return card;
    }

    static bool Until(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(30));

    /// Paused, the job comes to rest — the writers finish the blocks they hold, then nothing moves: two snapshots 300 ms
    /// apart are the same (within 30 s). Returns the resting snapshot.
    static ProgressSnapshot AssertComesToRest(OffloadEngine engine)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var a = engine.Snapshot();
            Thread.Sleep(300);
            var b = engine.Snapshot();
            if (a.ReadBytes == b.ReadBytes && a.WrittenBytes.SequenceEqual(b.WrittenBytes) && a.VerifiedBytes.SequenceEqual(b.VerifiedBytes)) return b;
            Assert.True(DateTime.UtcNow < deadline, "the paused job did not come to rest");
        }
    }

    [Fact]
    public async Task PauseBeforeAFile_CopyHolds_ResumeFinishes_SpeedsLeaveThePauseOut()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        options.BeforeFileCopy = i => { if (i == 1) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        var held = AssertComesToRest(engine);
        Assert.True(held.Paused);
        Assert.Equal(CopyPhase.Copy, held.Phase);
        Assert.Equal(3 * Chunk + 5, held.ReadBytes);   // file 1 not started
        Assert.Equal([3 * Chunk + 5, 3 * Chunk + 5], held.WrittenBytes);
        Thread.Sleep(400);
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToEject);
        Assert.False(engine.Snapshot().Paused);
        Assert.True(report.CopyPausedSeconds >= 0.6, $"{report.CopyPausedSeconds}");
        Assert.Null(report.VerifyPausedSeconds);
        Assert.Equal((report.CopyFinished - report.Started).TotalSeconds - report.CopyPausedSeconds!.Value, report.CopySeconds, 3);
        foreach (var path in plan.CardPaths) Assert.True(BackupVerifier.Verify(path, chunkSize: 4096).Success);
    }

    // The gate is checked before every block: a pause pressed inside a big file stops it within the buffer pool.
    [Fact]
    public async Task PauseInsideAFile_StopsBetweenBlocks()
    {
        using var dir = new TempDir();
        var card = Card(dir, count: 1, size: 40 * Chunk);
        var options = Options();
        var engine = new OffloadEngine(options);
        int blocks = 0;
        options.BeforeDestWrite = d => { if (d == 0 && Interlocked.Increment(ref blocks) == 2) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        var held = AssertComesToRest(engine);
        Assert.InRange(held.ReadBytes, 2L * Chunk, (2L + options.BufferCount + 1) * Chunk);   // mid-file, one pool ahead at most
        engine.Pauses.Resume();

        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(60))).SafeToEject);
    }

    [Fact]
    public async Task PauseDuringReadBack_VerifyHolds_ResumeVerifies()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        options.BeforeVerify = () => engine.Pauses.Pause();
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        Assert.Equal(CopyPhase.Verify, AssertComesToRest(engine).Phase);
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToEject);
        Assert.True(report.VerifyPausedSeconds >= 0.3, $"{report.VerifyPausedSeconds}");
        Assert.Null(report.CopyPausedSeconds);
    }

    // Cancel while paused (and closing the app, which cancels): the copy leaves the gate at once, nothing half-written
    // stays. Ruling I4: the promptness is the gate (left within 5 s), the whole run then has 60 s (flush, reports, MHL).
    [Fact]
    public async Task CancelWhilePaused_EndsPromptly_NoPartFiles()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        options.BeforeFileCopy = i => { if (i == 1) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        engine.Cancel();

        Assert.True(SpinWait.SpinUntil(() => engine.Snapshot() is { PauseHeld: false, Phase: not CopyPhase.Copy }, TimeSpan.FromSeconds(5)),
            "the cancelled copy did not leave the pause promptly");
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.False(report.Success);
        Assert.Equal(["cancelled", "cancelled"], report.Files.Skip(1).Select(f => f.SourceError));
        Assert.Equal(["written", "written"], report.Files[0].Destinations);   // copied before the pause; a cancel skips the read-back
        foreach (var root in plan.DestRoots)
            Assert.Empty(Directory.GetFiles(root, "*.offload-part", SearchOption.AllDirectories));
    }

    // A disk pulled out while paused: the next write after Resume fails there like any write error — that file fails on
    // that destination only, the others go on and the report says which ("Zkopírovat znovu" repairs it later).
    [Fact]
    public async Task DiskGoneWhilePaused_FailsOnThatDestinationOnly()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        bool gone = false;
        options.BeforeFileCopy = i => { if (i == 1) engine.Pauses.Pause(); };
        options.BeforeDestWrite = d => { if (d == 1 && Volatile.Read(ref gone)) throw new IOException("Zařízení není připojeno."); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        AssertComesToRest(engine);                    // file 0 is on both disks
        Volatile.Write(ref gone, true);
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.False(report.Success);
        Assert.Equal(["verified", "verified"], report.Files[0].Destinations);
        foreach (var f in report.Files.Skip(1))
        {
            Assert.Equal("verified", f.Destinations[0]);
            Assert.Equal("failed", f.Destinations[1]);
            Assert.Contains("Zařízení není připojeno.", f.Errors[1]);
        }
        Assert.Empty(Directory.GetFiles(plan.DestRoots[1], "*.offload-part", SearchOption.AllDirectories));
    }

    /// A job over `card` whose "card in the drive" is whatever `identity()` says; Pozastavit before file 1.
    static OffloadJob PausingJob(TempDir dir, string card, Func<string?> identity, Action onIdentify)
    {
        var options = Options();
        OffloadJob? job = null;
        options.BeforeFileCopy = i => { if (i == 1) job!.Engine.Pauses.Pause(); };
        job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = dir.Sub("history"),
        }, options)
        {
            Identify = _ => { onIdentify(); return identity(); },
        };
        return job;
    }

    // Ruling I1: another card put into the reader while paused — same names, same sizes, same times, only the card
    // differs (Sony C000N naming, fixed-size span segments) — is never copied into this backup. After the pause the card
    // is identified again before the next file: the remaining files fail, nothing more is read, the job fails in Czech.
    [Theory]
    [InlineData("card-B")]
    [InlineData(null)]   // pulled out: no volume to read
    public async Task CardSwappedWhilePaused_RestNotCopied_JobFails(string? afterSwap)
    {
        using var dir = new TempDir();
        var card = Card(dir);
        string? identity = "card-A";
        int reads = 0;
        var job = PausingJob(dir, card, () => Volatile.Read(ref identity), () => Interlocked.Increment(ref reads));

        var copy = TestThreads.Start(() => job.RunCopy());
        Assert.True(Until(() => job.Engine.Snapshot().PauseHeld));
        AssertComesToRest(job.Engine);
        var plan = job.Plan!;
        foreach (var f in plan.Scan.Files.Skip(1))
        {
            var path = Layout.Join(card, f.RelPath);
            TestFs.WriteRandom(path, (int)f.Size, 1000 + f.RelPath.Length);   // other bytes ...
            File.SetLastWriteTimeUtc(path, f.ModifiedUtc);                      // ... same name, size and time
        }
        Volatile.Write(ref identity, afterSwap);
        job.Engine.Pauses.Resume();
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Equal(Msg.CardChangedWhilePaused, s.Error);
        Assert.False(s.SafeToEject);
        Assert.Equal(2, Volatile.Read(ref reads));   // when the copy started, and once after the pause
        var report = job.Report!;
        Assert.Equal(["verified", "verified"], report.Files[0].Destinations);
        Assert.All(report.Files.Skip(1), f => Assert.Equal(OffloadEngine.CardChangedWhilePaused, f.SourceError));
        Assert.All(report.Files.Skip(1), f => Assert.Equal(["pending", "pending"], f.Destinations));
        foreach (var cardPath in plan.CardPaths)
        {
            foreach (var f in plan.Scan.Files.Skip(1)) Assert.False(File.Exists(Layout.Join(cardPath, f.RelPath)), f.RelPath);
            Assert.Empty(Directory.GetFiles(cardPath, "*.offload-part", SearchOption.AllDirectories));
            Assert.False(OffloadReport.FromJson(File.ReadAllText(Path.Combine(cardPath, Layout.ReportDirName, OffloadReport.JsonName(1)))).Success);
        }
    }

    // A folder source has no card identity (null when the copy started): nothing to compare, the copy goes on.
    [Fact]
    public async Task NoIdentityAtStart_NoCheckAfterThePause()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        string? identity = null;
        int reads = 0;
        var job = PausingJob(dir, card, () => Volatile.Read(ref identity), () => Interlocked.Increment(ref reads));

        var copy = TestThreads.Start(() => job.RunCopy());
        Assert.True(Until(() => job.Engine.Snapshot().PauseHeld));
        Volatile.Write(ref identity, "card-B");
        job.Engine.Pauses.Resume();
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Report!.SafeToEject);
        Assert.Equal(1, Volatile.Read(ref reads));
    }

    [Fact]
    public void ReadBack_BeforeBlock_RunsBeforeEveryBlock()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        TestFs.WriteRandom(path, 3 * 4096 + 1, 1);
        using var buf = new AlignedBuffer(4096);
        int calls = 0;
        Hashing.HashFile(path, buf, HashKind.Xxh64, beforeBlock: () => calls++);
        Assert.Equal(4, calls);
    }
}
