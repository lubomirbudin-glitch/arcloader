using Offload.Core.Append;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Pozastavit in stage 1 (task 2026-10-09): the copy finishes the file it is on — on every destination — and the
/// read-back the file it reads, then they stand; Pokračovat goes on with the next file. The report leaves only the time
/// the job stood out of the speeds, a cancel while pausing or paused or a disk pulled out while paused end as they do
/// without a pause, a card swapped while paused is never copied into the backup (ruling I1). Ruling I4: promptness is asserted where it is decided (leaving the gate);
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

        Assert.True(report.SafeToFormat);
        Assert.False(engine.Snapshot().Paused);
        Assert.True(report.CopyPausedSeconds >= 0.6, $"{report.CopyPausedSeconds}");
        Assert.Null(report.VerifyPausedSeconds);
        Assert.Equal((report.CopyFinished - report.Started).TotalSeconds - report.CopyPausedSeconds!.Value, report.CopySeconds, 3);
        foreach (var path in plan.CardPaths) Assert.True(BackupVerifier.Verify(path, chunkSize: 4096).Success);
    }

    /// Two files: a big one (40 blocks) and a small one.
    static string BigThenSmall(TempDir dir, int blocks = 40)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), blocks * Chunk, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 3 * Chunk + 5, 2);
        return card;
    }

    static void AssertNoPart(OffloadPlan plan)
    {
        foreach (var path in plan.CardPaths) Assert.Empty(Directory.GetFiles(path, "*.offload-part", SearchOption.AllDirectories));
    }

    // Task 2026-10-09: Pozastavit inside a big file lets the file finish on EVERY destination (the card is read once for
    // all of them), its manifest record is written, nothing is left half written — then the job stands. Pokračovat goes
    // on with the next file; nothing is copied again.
    [Fact]
    public async Task PauseInsideAFile_FinishesItOnEveryDestination_ThenStands_ResumeGoesOnWithTheNext()
    {
        using var dir = new TempDir();
        var card = BigThenSmall(dir);
        var options = Options();
        options.FullSyncThreshold = 0;   // every file flushed on its own: its manifest record is written right after it
        var engine = new OffloadEngine(options);
        int blocks = 0;
        options.BeforeDestWrite = d => { if (d == 0 && Interlocked.Increment(ref blocks) == 2) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        var held = AssertComesToRest(engine);
        Assert.Equal(40L * Chunk, held.ReadBytes);                    // the whole first file, nothing of the second
        Assert.Equal([40L * Chunk, 40L * Chunk], held.WrittenBytes);  // on both destinations
        AssertNoPart(plan);
        foreach (var path in plan.CardPaths)
        {
            Assert.Equal(40L * Chunk, new FileInfo(Layout.Join(path, "CLIP/A001C001.mov")).Length);
            Assert.False(File.Exists(Layout.Join(path, "CLIP/A001C002.mov")));
            // Resume after a crash now: the finished file is recorded, nothing of the next one is.
            var files = ManifestFile.Files(ManifestFile.Read(path), 1);
            Assert.Equal(["CLIP/A001C001.mov"], files.Keys);
        }
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        Assert.Equal(40L * Chunk + 3 * Chunk + 5, engine.Snapshot().ReadBytes);   // the card was read once: no file twice
        Assert.True(report.CopyPausedSeconds > 0);
    }

    // The timers: the minutes a long file still takes after Pozastavit are copy time, not pause — copyPausedSeconds is
    // only the time the job stood (from the end of that file to Pokračovat).
    [Fact]
    public async Task PausePendingOnALongFile_IsCopyTime_PausedSecondsOnlyTheStandingTime()
    {
        using var dir = new TempDir();
        var card = BigThenSmall(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        int blocks = 0;
        DateTime pressed = default;
        options.BeforeDestWrite = d =>
        {
            if (d != 0) return;
            int b = Interlocked.Increment(ref blocks);
            if (b == 2)
            {
                pressed = DateTime.UtcNow;
                engine.Pauses.Pause();
            }
            else if (b > 2 && b <= 40) Thread.Sleep(30);   // the rest of the long file: ≥ 1.1 s after Pozastavit
        };
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().Paused));
        Assert.False(engine.Snapshot().PauseHeld);                    // still finishing the file
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        Thread.Sleep(500);
        var resumed = DateTime.UtcNow;
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        double pending = 38 * 0.030;
        Assert.InRange(report.CopyPausedSeconds!.Value, 0.4, (resumed - pressed).TotalSeconds - pending + 0.05);
        Assert.True(report.CopySeconds >= pending, $"{report.CopySeconds}");
        Assert.Null(report.VerifyPausedSeconds);
    }

    // Cancel while the pause is still pending on a long file: the cancel does not wait for the file — it stops it
    // between blocks, as without a pause; nothing half written stays, no paused time is counted.
    [Fact]
    public async Task CancelWhilePausing_DoesNotWaitForTheFile_NoPartFiles()
    {
        using var dir = new TempDir();
        var card = BigThenSmall(dir, blocks: 200);
        var options = Options();
        var engine = new OffloadEngine(options);
        int blocks = 0;
        options.BeforeDestWrite = d =>
        {
            if (d != 0) return;
            int b = Interlocked.Increment(ref blocks);
            if (b == 2) engine.Pauses.Pause();
            else if (b > 2) Thread.Sleep(20);   // ≈ 4 s to the end of the file
        };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().Paused));
        Thread.Sleep(300);
        Assert.False(engine.Snapshot().PauseHeld);
        engine.Cancel();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(Volatile.Read(ref blocks) < 150, $"{blocks}");      // the file was not read to its end first
        Assert.False(report.Success);
        Assert.Equal(["cancelled", "cancelled"], report.Files.Select(f => f.SourceError));
        Assert.Null(report.CopyPausedSeconds);
        AssertNoPart(plan);
    }

    // The classic read-back: Pozastavit while a destination reads a file back lets that file be read to its end (on each
    // destination), then the read-back stands before the next file; Pokračovat verifies the rest.
    [Fact]
    public async Task PauseInsideAReadBack_FinishesTheFile_ThenStands_ResumeVerifiesTheRest()
    {
        using var dir = new TempDir();
        var card = BigThenSmall(dir, blocks: 20);
        var options = Options();
        var engine = new OffloadEngine(options);
        int blocks = 0;
        options.BeforeReadBackBlock = d => { if (d == 0 && Interlocked.Increment(ref blocks) == 3) engine.Pauses.Pause(); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        var held = AssertComesToRest(engine);
        Assert.Equal(CopyPhase.Verify, held.Phase);
        Assert.Equal(20L * Chunk, held.VerifiedBytes[0]);              // the file it was on, to its end
        Assert.Contains(held.VerifiedBytes[1], new[] { 20L * Chunk, 23L * Chunk + 5 });   // the other disk: a file boundary
        Assert.All(held.VerifyingFiles!, Assert.Null);                  // nobody reads
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        Assert.True(report.VerifyPausedSeconds > 0, $"{report.VerifyPausedSeconds}");
        Assert.Null(report.CopyPausedSeconds);
    }

    // An append ("Doplnit"): the same — the new file being copied finishes and is recorded, then the job stands.
    [Fact]
    public async Task PausedAppend_FinishesTheNewFile_Recorded_ThenStands_ResumeCopiesTheNext()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card, "A001C003.mov", size: 6 * Chunk, seed: 3);
        AppendFixtures.NewClip(card, "A001C004.mov", size: 2 * Chunk, seed: 4);
        var options = AppendFixtures.Options();
        options.FullSyncThreshold = 0;
        var engine = new OffloadEngine(options);
        int blocks = 0;
        options.BeforeDestWrite = d => { if (d == 0 && Interlocked.Increment(ref blocks) == 1) engine.Pauses.Pause(); };
        var plan = engine.PrepareAppend(card, paths);

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        AssertComesToRest(engine);
        AssertNoPart(plan);
        foreach (var path in paths)
        {
            Assert.Equal(6L * Chunk, new FileInfo(Layout.Join(path, "CLIP/A001C003.mov")).Length);
            Assert.False(File.Exists(Layout.Join(path, "CLIP/A001C004.mov")));
            Assert.Equal(["CLIP/A001C003.mov"], ManifestFile.Files(ManifestFile.Read(path), 2).Keys);
        }
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        Assert.Equal(["CLIP/A001C003.mov", "CLIP/A001C004.mov"], report.Files.Select(f => f.Path));
    }

    // A repair ("Zkopírovat znovu"): the file being copied again finishes (and takes its place), the job stands before
    // the read-back; Pokračovat verifies it — never a half-written copy at the file's place.
    [Fact]
    public async Task PausedRepair_FinishesTheFile_StandsBeforeTheReadBack()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var options = AppendFixtures.Options();
        var engine = new OffloadEngine(options);
        options.BeforeDestWrite = _ => engine.Pauses.Pause();
        var plan = engine.PrepareRepair(engine.PlanRepair(card, paths));

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        Assert.Equal(CopyPhase.Verify, AssertComesToRest(engine).Phase);
        AssertNoPart(plan);
        Assert.Equal(File.ReadAllBytes(Path.Combine(card, "CLIP", "A001C002.mov")), File.ReadAllBytes(Layout.Join(paths[1], RepairFixtures.Bad)));
        engine.Pauses.Resume();
        var report = await run.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        Assert.Equal(["verified", "verified"], Assert.Single(report.Files).Destinations);
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

        Assert.True(report.SafeToFormat);
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
        Assert.Equal(2 + OffloadEngine.IdentifyRetries, Volatile.Read(ref reads));   // at the start; after the pause with retries
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

    // Fix round 1: a reader that answers late once after the pause (no volume yet, or an error) is asked again 250 ms
    // later, like a card read — the same card then goes on and the job ends Done.
    [Theory]
    [InlineData(false)]   // null once
    [InlineData(true)]    // throws once
    public async Task CardUnreadableOnceAfterThePause_AskedAgain_JobDone(bool throws)
    {
        using var dir = new TempDir();
        var card = Card(dir);
        int reads = 0;
        var job = PausingJob(dir, card,
            () => Volatile.Read(ref reads) != 2 ? "card-A" : throws ? throw new IOException("Zařízení není připraveno.") : null,
            () => Interlocked.Increment(ref reads));

        var copy = TestThreads.Start(() => job.RunCopy());
        Assert.True(Until(() => job.Engine.Snapshot().PauseHeld));
        job.Engine.Pauses.Resume();
        await copy.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Report!.SafeToFormat);
        Assert.Equal(3, Volatile.Read(ref reads));   // at the start, the failed read after the pause, the retry
    }

    // Fix round 1: "Pozastaveno" only once every destination has written all it was handed — a slow disk still
    // writing its queued blocks (up to the whole buffer pool) must not look safe to unplug.
    [Fact]
    public async Task PauseHeld_OnlyAfterTheSlowDiskDrained()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var options = Options();
        var engine = new OffloadEngine(options);
        using var slow = new ManualResetEventSlim(false);
        options.BeforeFileCopy = i => { if (i == 1) engine.Pauses.Pause(); };
        options.BeforeDestWrite = d => { if (d == 1) slow.Wait(TimeSpan.FromSeconds(60)); };
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");

        var run = TestThreads.Start(() => engine.Run(plan));
        Assert.True(Until(() => engine.Snapshot().ReadBytes == 3 * Chunk + 5));   // the card read of file 0 is done ...
        Thread.Sleep(500);
        var draining = engine.Snapshot();
        Assert.True(draining.Paused);
        Assert.False(draining.PauseHeld);                      // ... but disk 2 still has blocks to write
        Assert.False(engine.Pauses.IsHolding);                 // the copy waits for it before it stands
        Assert.True(draining.WrittenBytes[1] < draining.ReadBytes, $"{draining.WrittenBytes[1]} < {draining.ReadBytes}");
        slow.Set();
        Assert.True(Until(() => engine.Snapshot().PauseHeld));
        var held = engine.Snapshot();
        Assert.Equal([held.ReadBytes, held.ReadBytes], held.WrittenBytes);
        engine.Pauses.Resume();

        Assert.True((await run.WaitAsync(TimeSpan.FromSeconds(60))).SafeToFormat);
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
        Assert.True(job.Report!.SafeToFormat);
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
