using System.Diagnostics;
using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Plan 10, the verification of stage 2 without a job (spec 3, 5, 6, 10): which disk is the primary one, what the
/// background reads, mode A and B through Prepare, the fill-in in Finish, the notes of spec 10, the report it gives.
public class VerifySessionTests
{
    const int Block = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, DeferVerify = true };

    /// A deferred copy of `files` onto two destinations: what stage 2 starts from.
    static (OffloadEngine Engine, OffloadPlan Plan, OffloadReport Report, string Card) Copy(TempDir dir, params (string Rel, int Size)[] files) =>
        CopyTo(dir, 2, files);

    static (OffloadEngine Engine, OffloadPlan Plan, OffloadReport Report, string Card) CopyTo(TempDir dir, int destinations, params (string Rel, int Size)[] files)
    {
        var card = dir.Sub("A001");
        foreach (var (rel, size) in files) TestFs.WriteRandom(Layout.Join(card, rel), size, size);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, Enumerable.Range(1, destinations).Select(d => dir.Sub("d" + d)).ToList(), null, "2026-10-08");
        return (engine, plan, engine.Run(plan), card);
    }

    static VerifySession Session(OffloadEngine e, OffloadPlan plan, OffloadReport report, int? preferred = null, string?[]? why = null,
        double?[]? rates = null, CancellationToken ct = default, Func<int, int, ulong?>? card = null, PauseGate? pauses = null,
        Func<int, string>? volumes = null, IReadOnlyList<ulong[]?>? blocks = null) =>
        new(plan, report, blocks ?? e.Blocks, Block, rates ?? plan.CardPaths.Select(_ => (double?)1).ToArray(), preferred,
            why ?? new string?[plan.CardPaths.Count], pauses ?? new PauseGate(), ct, card ?? ((_, _) => null))
        {
            VolumeOfDestination = volumes,
        };

    static int Index(OffloadPlan p, string rel) => p.Scan.Files.ToList().FindIndex(f => f.RelPath == rel);

    static Clip MovClip(string rel) => new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel] };

    /// Waits up to 10 s for `condition`.
    static bool Eventually(Func<bool> condition, TimeSpan? limit = null)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > (limit ?? TimeSpan.FromSeconds(10))) return false;
            Thread.Sleep(20);
        }
        return true;
    }

    [Fact]
    public void Primary_TheFastestThatMayBe_OrTheChosenOne()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("a.mov", 1000));

        using (var s = Session(e, plan, report, rates: [100, 200])) Assert.Equal(1, s.Primary);
        using (var s = Session(e, plan, report, preferred: 0, rates: [100, 200])) Assert.Equal(0, s.Primary);
        using (var s = Session(e, plan, report, why: [null, Msg.ClassicNetwork], rates: [100, 200]))
        {
            Assert.Equal(0, s.Primary);
            Assert.Equal([null, Msg.ClassicNetwork], s.ClassicWhy);
        }
        using (var s = Session(e, plan, report, preferred: 1, why: [null, Msg.ClassicNetwork])) Assert.Equal(0, s.Primary);   // not allowed: the fastest allowed
        using (var s = Session(e, plan, report, why: [Msg.ClassicNetwork, Msg.ClassicCacheCheck]))
        {
            Assert.Equal(-1, s.Primary);
            Assert.Equal([Msg.ClassicNetwork, Msg.ClassicCacheCheck], s.ClassicWhy);
        }
        using (var s = Session(e, plan, report, preferred: 0)) Assert.Equal([null, Msg.ClassicSecondary], s.ClassicWhy);
    }

    // Without any proxy: the background reads the other disk and the primary's files no proxy reads; Finish reads on the
    // primary what a proxy would have read.
    [Fact]
    public void NoProxy_BackgroundAndFillIn_VerifyEverything()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        using var s = Session(e, plan, report, preferred: 0);

        s.StartBackground();
        s.Finish();

        Assert.All(s.Results, d => Assert.All(d, r => Assert.Equal(FileState.Verified, r.State)));
        Assert.Equal(s.ToVerify, s.VerifiedBytes);
        Assert.Empty(s.Notes);
    }

    // Mode A: the URL serves the primary's copy, every byte read through it is verified.
    [Fact]
    public async Task ModeA_PrepareGivesTheTap_ReadThroughItIsVerified()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5));
        using var s = Session(e, plan, report, preferred: 1);
        var clip = MovClip("CLIP/a.mov");
        Assert.Null(s.Prepare(clip, 0));   // the other disk: no tap

        byte[] got;
        using (var lease = s.Prepare(clip, 1)!)
        {
            using var http = new HttpClient();
            got = await http.GetByteArrayAsync(lease.Url);
        }
        s.Finish();

        Assert.Equal(File.ReadAllBytes(Layout.Join(plan.CardPaths[1], "CLIP/a.mov")), got);
        Assert.Equal(FileState.Verified, s.Results[1][Index(plan, "CLIP/a.mov")].State);
        Assert.Equal(FileState.Written, s.Results[0][Index(plan, "CLIP/a.mov")].State);   // never started: no background
    }

    // Spec 5, 11: a flipped byte on the primary is caught through the tap, and the fill-in never hides it.
    [Fact]
    public async Task ModeA_FlippedOnThePrimary_Failed()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5));
        DiskFlip.Flip(Layout.Join(plan.CardPaths[0], "CLIP/a.mov"), Block + 9);
        using var s = Session(e, plan, report, preferred: 0);

        using (var lease = s.Prepare(MovClip("CLIP/a.mov"), 0)!)
        {
            using var http = new HttpClient();
            await http.GetByteArrayAsync(lease.Url);
        }
        s.StartBackground();
        s.Finish();

        Assert.StartsWith("HASH MISMATCH: block 1 dest ", s.Results[0][0].Error);
        Assert.Equal(FileState.Verified, s.Results[1][0].State);
    }

    // Mode B: Prepare reads the RAW clip's files straight from the disk (verified) before the SDK decodes them.
    [Fact]
    public void ModeB_PrepareWarmsAndVerifies()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("A001_0001.braw", 2 * Block + 9));
        using var s = Session(e, plan, report, preferred: 0);
        var clip = new Clip { Name = "A001_0001", Path = "A001_0001.braw", Format = ClipFormat.Braw, Segments = ["A001_0001.braw"] };

        using (var lease = s.Prepare(clip, 0)) Assert.Null(lease!.Url);
        File.Delete(Layout.Join(plan.CardPaths[0], "A001_0001.braw"));   // Finish must not need to read it again
        s.Finish();

        Assert.Equal(FileState.Verified, s.Results[0][0].State);
        Assert.False(s.Reading);
    }

    // Final review, item 4: mode B's read hangs on the primary disk (not unplugged) — Prepare gives up after the stall
    // limit instead of holding the job thread; the copy is not verified by it, the fill-in verifies it.
    [Fact]
    public async Task ModeB_HungPrimaryDisk_PrepareGivesUp_FillInDecides()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("A001_0001.braw", 2 * Block + 9));
        var s = new VerifySession(plan, report, e.Blocks, Block, plan.CardPaths.Select(_ => (double?)1).ToArray(), 0,
            new string?[plan.CardPaths.Count], new PauseGate(), default, (_, _) => null)
        {
            StallTimeout = TimeSpan.FromSeconds(3),   // room for the reader thread to start under the full suite's load
        };
        using var _s = s;
        var clip = new Clip { Name = "A001_0001", Path = "A001_0001.braw", Format = ClipFormat.Braw, Segments = ["A001_0001.braw"] };
        var file = Path.GetFullPath(Layout.Join(plan.CardPaths[0], "A001_0001.braw"));
        using var disk = new ManualResetEventSlim();
        int hung = 0;
        Assert.True(DiskBlocks.ReadHooks.TryAdd(file, () =>
        {
            if (Interlocked.Exchange(ref hung, 1) == 0) disk.Wait(TimeSpan.FromSeconds(60));   // the first read hangs
        }));
        try
        {
            s.StartBackground();
            var sw = Stopwatch.StartNew();
            var preparing = Task.Run(() => s.Prepare(clip, 0));
            Assert.Same(preparing, await Task.WhenAny(preparing, Task.Delay(TimeSpan.FromSeconds(20))));   // not held by the disk
            Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(2.5));
            using (var lease = await preparing) Assert.Null(lease!.Url);
            Assert.Equal(1, Volatile.Read(ref hung));
            Assert.Equal(FileState.Written, s.Results[0][0].State);   // nothing verified by the hung read

            s.Finish();   // the read still hangs: the fill-in reads the copy itself, the background the other disk

            Assert.False(s.Reading);   // the hung read no longer counts as reading (a pause can stand)
            Assert.Equal(FileState.Verified, s.Results[0][0].State);
            Assert.Equal(FileState.Verified, s.Results[1][0].State);
        }
        finally
        {
            DiskBlocks.ReadHooks.TryRemove(file, out _);
            disk.Set();
        }
    }

    // 0.6.1: the fill-in (and the classic read) of a disk that hangs for good is bounded by the stall limit: the destination's
    // files fail with the Czech reason, Finish returns, nothing is green.
    [Fact]
    public async Task FillIn_HungDisk_FailsItsFiles_FinishReturns()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("a.mov", 2 * Block + 9), ("b.mov", Block));
        var s = new VerifySession(plan, report, e.Blocks, Block, plan.CardPaths.Select(_ => (double?)1).ToArray(), 0,
            new string?[plan.CardPaths.Count], new PauseGate(), default, (_, _) => null)
        {
            ReadStallTimeout = TimeSpan.FromSeconds(3),   // room for the reader thread to start under the full suite's load
        };
        using var _s = s;
        var file = Path.GetFullPath(Layout.Join(plan.CardPaths[0], "a.mov"));
        using var disk = new ManualResetEventSlim();
        Assert.True(BlockReads.ReadHooks.TryAdd(file, () => disk.Wait(TimeSpan.FromSeconds(60))));   // every read hangs
        try
        {
            s.StartBackground();
            var finishing = Task.Run(() => s.Finish());
            Assert.Same(finishing, await Task.WhenAny(finishing, Task.Delay(TimeSpan.FromSeconds(30))));   // not held by the disk
            await finishing;
            foreach (var r in s.Results[0])
            {
                Assert.Equal(FileState.Failed, r.State);
                Assert.Equal(Msg.VerifyDiskHung, r.Error);
            }
            Assert.False(s.Reading);
        }
        finally
        {
            BlockReads.ReadHooks.TryRemove(file, out _);
            disk.Set();
        }
    }

    [Fact]
    public void FlippedByteOnTheSecondary_Failed_WithTheBlock()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 3 * Block));
        DiskFlip.Flip(Layout.Join(plan.CardPaths[1], "SOUND/b.wav"), Block + 3);
        using var s = Session(e, plan, report, preferred: 0);

        s.StartBackground();
        s.Finish();

        Assert.Equal(FileState.Verified, s.Results[0][0].State);
        Assert.Equal(FileState.Failed, s.Results[1][0].State);
        Assert.StartsWith("HASH MISMATCH: block 1 dest ", s.Results[1][0].Error);
        Assert.True(s.VerifiedBytes[1] <= s.ToVerify[1]);
    }

    // Spec 10: the same block wrong on both disks — the card is asked again and the note says what it gives.
    [Fact]
    public void SameBlockOnBothDisks_TheCardIsReadAgain()
    {
        using var dir = new TempDir();
        var (e, plan, report, card) = Copy(dir, ("SOUND/b.wav", 3 * Block));
        foreach (var p in plan.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/b.wav"), 2 * Block + 1);
        using var s = Session(e, plan, report, preferred: 0,
            card: (file, block) => VerifySession.ReadCardBlock(Layout.Join(card, plan.Scan.Files[file].RelPath), block, Block));

        s.StartBackground();
        s.Finish();

        var note = Assert.Single(s.Notes);
        Assert.StartsWith("SOUND/b.wav: blok 2 nesouhlasí stejně na d1", note);
        Assert.EndsWith(Msg.CardGivesRecorded, note);
        Assert.All(s.Results, d => Assert.Equal(FileState.Failed, d[0].State));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameBlockOnBothDisks_CardGivesWhatTheDisksHave_OrSomethingElse(bool whatTheDisksHave)
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 3 * Block));
        foreach (var p in plan.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/b.wav"), Block + 1);
        var onDisk = XxHash3.HashToUInt64(File.ReadAllBytes(Layout.Join(plan.CardPaths[0], "SOUND/b.wav")).AsSpan(Block, Block));
        using var s = Session(e, plan, report, preferred: 0, card: (_, _) => whatTheDisksHave ? onDisk : 12345UL);

        s.StartBackground();
        s.Finish();

        Assert.EndsWith(whatTheDisksHave ? Msg.CardGivesWhatDisksHave : Msg.CardGivesOther, Assert.Single(s.Notes));
    }

    // Wrong on one disk only: a copy error, not the card's — the card is not asked.
    [Fact]
    public void WrongOnOneDiskOnly_TheCardIsNotAsked()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 3 * Block));
        DiskFlip.Flip(Layout.Join(plan.CardPaths[1], "SOUND/b.wav"), 7);
        int asked = 0;
        using var s = Session(e, plan, report, preferred: 0, card: (_, _) => { Interlocked.Increment(ref asked); return null; });

        s.StartBackground();
        s.Finish();

        Assert.Equal(0, asked);
        Assert.Empty(s.Notes);
    }

    [Fact]
    public void CardGone_TheNoteSaysSo()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block));
        foreach (var p in plan.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/b.wav"), 7);
        using var s = Session(e, plan, report, preferred: 0);

        s.StartBackground();
        s.Finish();

        Assert.EndsWith(Msg.CardNotReadAgain, Assert.Single(s.Notes));
    }

    // R7: a failed copy is never overwritten, nothing is repaired automatically, the card is only read — every byte and
    // time stamp under the card and both destinations is as it was.
    [Fact]
    public void Mismatches_NothingIsEverWritten_CardAndCopiesUntouched()
    {
        using var dir = new TempDir();
        var (e, plan, report, card) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 3 * Block));
        foreach (var p in plan.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/b.wav"), 2 * Block + 1);
        DiskFlip.Flip(Layout.Join(plan.CardPaths[1], "CLIP/a.mov"), 5);
        var before = Snapshot(dir.Path);
        using var s = Session(e, plan, report, preferred: 0,
            card: (file, block) => VerifySession.ReadCardBlock(Layout.Join(card, plan.Scan.Files[file].RelPath), block, Block));

        s.StartBackground();
        s.Finish();

        Assert.Equal(FileState.Failed, s.Results[1][Index(plan, "CLIP/a.mov")].State);
        Assert.Equal(before, Snapshot(dir.Path));
    }

    static string Snapshot(string root) => string.Join("\n", Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order()
        .Select(p => File.Exists(p)
            ? $"{p} {new FileInfo(p).Length} {File.GetLastWriteTimeUtc(p).Ticks} {Convert.ToHexString(XxHash128.Hash(File.ReadAllBytes(p)))}"
            : $"{p}/"));

    [Fact]
    public void Cancelled_NothingRead_CopiesStayWritten()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var s = Session(e, plan, report, preferred: 0, ct: cts.Token);

        s.StartBackground();
        s.Finish();

        Assert.All(s.Results, d => Assert.Equal(FileState.Written, d[0].State));
    }

    // ----- ruling I3: a destination read back the classic way, through FileReader with the 0.5.x fallback -----

    // A volume that refuses unbuffered reads (some network shares): read back through the cache like 0.5.x — verified,
    // never a mass failure — and the report's notes say how.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClassicDestination_UnbufferedImpossible_VerifiedThroughTheCache_AndNoted(bool refuses)
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        var d2 = Path.GetFullPath(plan.CardPaths[1]);
        NativeIO.TestVolumes[d2] = new NativeIO.TestVolume { RefuseUnbuffered = refuses, IgnoreUnbuffered = !refuses };
        try
        {
            using var s = Session(e, plan, report, preferred: 0, why: [null, refuses ? Msg.ClassicNetwork : Msg.ClassicCacheCheck]);
            s.StartBackground();
            s.Finish();

            Assert.All(s.Results, d => Assert.All(d, r => Assert.Equal(FileState.Verified, r.State)));
            Assert.Equal([0, 2], s.CachedReads);
            Assert.Equal(Msg.VerifiedThroughCache(History.HistoryText.DiskName(plan.DestRoots[1]), 2), Assert.Single(s.Notes));
        }
        finally
        {
            NativeIO.TestVolumes.TryRemove(d2, out _);
        }
    }

    // The fallback is only for "unbuffered is impossible": a destination that reads unbuffered never reads through the
    // cache, and any other error (a copy locked by another program, a disk gone) is a failure, never a cached read.
    [Fact]
    public void UnbufferedPossible_NeverACachedRead_OtherErrorsFail()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        using var s = Session(e, plan, report, preferred: 0);
        using (File.Open(Layout.Join(plan.CardPaths[1], "SOUND/b.wav"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            s.StartBackground();
            s.Finish();
        }

        Assert.All(s.Results[0], r => Assert.Equal(FileState.Verified, r.State));
        Assert.Equal(FileState.Verified, s.Results[1][Index(plan, "CLIP/a.mov")].State);
        var locked = s.Results[1][Index(plan, "SOUND/b.wav")];
        Assert.Equal(FileState.Failed, locked.State);
        Assert.StartsWith("verify: ", locked.Error);
        Assert.Equal([0, 0], s.CachedReads);
        Assert.Empty(s.Notes);
    }

    // An NTFS-compressed copy on the primary: the tap refuses it (its reads may come through the cache), so the proxy
    // reads the file itself and the fill-in verifies it through the cache — noted, not failed.
    [Fact]
    public void CompressedCopyOnThePrimary_NoTap_FillInThroughTheCache_Noted()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5));
        var path = Layout.Join(plan.CardPaths[0], "CLIP/a.mov");
        try { ToolProcess.Run(Path.Combine(Environment.SystemDirectory, "compact.exe"), ["/c", "/q", path]); }
        catch (ToolError) { return; }   // not NTFS here: nothing to test
        if (!File.GetAttributes(path).HasFlag(FileAttributes.Compressed)) return;
        using var s = Session(e, plan, report, preferred: 0);

        Assert.Null(s.Prepare(MovClip("CLIP/a.mov"), 0));
        s.StartBackground();
        s.Finish();

        Assert.Equal(FileState.Verified, s.Results[0][0].State);
        Assert.Equal([1, 0], s.CachedReads);
        Assert.Single(s.Notes);
    }

    // ----- spec 6: one reader per volume, low I/O priority, never against the proxy -----

    [Fact]
    public void OneBackgroundReaderPerVolume_AtLowIoPriority()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = CopyTo(dir, 3, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        using var s = Session(e, plan, report, preferred: 0, volumes: d => d == 2 ? "Y" : "X");

        s.StartBackground();
        s.Finish();

        Assert.Equal(2, s.Readers.Count);
        var x = Assert.Single(s.Readers, r => r.Volume == "X");
        var y = Assert.Single(s.Readers, r => r.Volume == "Y");
        Assert.Equal([0, 1], x.Dests);
        Assert.True(x.Gated);   // the primary's volume
        Assert.Equal([2], y.Dests);
        Assert.False(y.Gated);
        Assert.All(s.Readers, r => Assert.Equal(0, r.IoPriority));   // IoPriorityVeryLow while it read
        Assert.All(s.Results, d => Assert.All(d, r => Assert.Equal(FileState.Verified, r.State)));
    }

    // While a proxy reads the primary through the tap, the primary volume's reader waits; another volume's reads on.
    [Fact]
    public void WhileTheTapReads_ThePrimaryVolumeWaits_AnotherVolumeReadsOn()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        using var s = Session(e, plan, report, preferred: 1, volumes: d => d == 1 ? "P" : "S");

        var lease = s.Prepare(MovClip("CLIP/a.mov"), 1)!;
        s.StartBackground();
        Assert.True(Eventually(() => s.VerifiedBytes[0] == s.ToVerify[0], TimeSpan.FromSeconds(60)), "the other volume did not read on");
        Thread.Sleep(300);
        Assert.Equal(0, s.VerifiedBytes[1]);   // SOUND/b.wav on the primary: not while the proxy reads
        lease.Dispose();
        s.Finish();

        Assert.All(s.Results, d => Assert.All(d, r => Assert.Equal(FileState.Verified, r.State)));
    }

    // ----- pause, cancel, a disk unplugged -----

    [Fact]
    public async Task Paused_NothingRead_NotReading_ResumeFinishes()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        var pauses = new PauseGate();
        using var s = Session(e, plan, report, preferred: 0, pauses: pauses);
        pauses.Pause();

        s.StartBackground();
        Assert.True(Eventually(() => pauses.IsHolding));
        Thread.Sleep(200);
        Assert.Equal([0, 0], s.VerifiedBytes);
        Assert.False(s.Reading);   // a pause stands: nobody reads
        var finish = Task.Run(() => s.Finish());
        Thread.Sleep(200);
        Assert.False(finish.IsCompleted);   // Finish waits for the paused readers
        pauses.Resume();
        await finish.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(s.Results, d => Assert.All(d, r => Assert.Equal(FileState.Verified, r.State)));
    }

    [Fact]
    public void PausedThenCancelled_CopiesStayWritten_FinishReturns()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block));
        var pauses = new PauseGate();
        using var cts = new CancellationTokenSource();
        using var s = Session(e, plan, report, preferred: 0, pauses: pauses, ct: cts.Token);
        pauses.Pause();
        s.StartBackground();
        Assert.True(Eventually(() => pauses.IsHolding));

        cts.Cancel();
        s.Finish();

        Assert.All(s.Results, d => Assert.Equal(FileState.Written, d[0].State));
    }

    [Fact]
    public async Task DisposedWhilePaused_ReadersEnd()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block));
        var pauses = new PauseGate();
        var s = Session(e, plan, report, preferred: 0, pauses: pauses);
        pauses.Pause();
        s.StartBackground();
        Assert.True(Eventually(() => pauses.IsHolding));

        await Task.Run(s.Dispose).WaitAsync(TimeSpan.FromSeconds(30));   // TimeoutException if a reader hangs
    }

    // Task 2026-10-09: Pozastavit while a background reader reads a long file — it reads that file to its end (each block
    // keeps the hung-disk watchdog quiet however long the file takes), then the session stands; standing, the watchdog
    // never fires however long the pause; Pokračovat verifies the rest. Nothing is read twice.
    [Fact]
    public async Task PausedMidFile_TheReaderFinishesItsFile_ThenStands_WatchdogQuiet()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 8 * Block), ("SOUND/c.wav", 2 * Block));
        var pauses = new PauseGate();
        using var s = new VerifySession(plan, report, e.Blocks, Block, plan.CardPaths.Select(_ => (double?)1).ToArray(), 0,
            new string?[plan.CardPaths.Count], pauses, default, (_, _) => null)
        {
            VolumeOfDestination = d => "V" + d,   // one reader per destination
            ReadStallTimeout = TimeSpan.FromSeconds(3),   // room for the low-priority reads under the full suite's load
        };
        var file = Path.GetFullPath(Layout.Join(plan.CardPaths[1], "SOUND/b.wav"));
        int reads = 0;
        Assert.True(BlockReads.ReadHooks.TryAdd(file, () =>
        {
            if (Interlocked.Increment(ref reads) == 1) pauses.Pause();
            Thread.Sleep(450);   // 3.6 s after Pozastavit, over the watchdog's limit — never that long without a block
        }));
        try
        {
            s.StartBackground();
            var finishing = Task.Run(() => s.Finish());   // its watchdog watches the readers from now on
            Assert.True(Eventually(() => s.CurrentFiles.Contains("SOUND/b.wav")));
            Assert.True(Eventually(() => pauses.IsStanding, TimeSpan.FromSeconds(30)));
            int b = Index(plan, "SOUND/b.wav"), c = Index(plan, "SOUND/c.wav");
            Assert.Equal(8L * Block, s.VerifiedBytes[1]);                       // the file it was on, to its end
            Assert.Equal((FileState.Verified, FileState.Written), (s.Results[1][b].State, s.Results[1][c].State));
            Assert.False(s.Reading);
            Assert.Empty(s.CurrentFiles);
            Thread.Sleep(4000);                                                  // standing, longer than the watchdog's limit
            Assert.False(finishing.IsCompleted);
            pauses.Resume();
            await finishing.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.All(s.Results, d => Assert.All(d, r => Assert.Equal(FileState.Verified, r.State)));
            Assert.Equal(8, Volatile.Read(ref reads));                           // b.wav read once
        }
        finally
        {
            BlockReads.ReadHooks.TryRemove(file, out _);
        }
    }

    // Review M-2: Pozastavit while one background reader stands at the gate and another hangs inside a read on its disk:
    // every reader is watched at once — the hung one is given up on after the stall limit (its copies fail with the
    // hung-disk note, never green), and then the job stands instead of "Pozastavuji…" for ever. Pokračovat finishes.
    [Fact]
    public async Task PausedWhileAnotherDiskHangs_HungReaderGivenUp_ThenStands()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block), ("SOUND/c.wav", 2 * Block));
        var pauses = new PauseGate();
        using var s = new VerifySession(plan, report, e.Blocks, Block, plan.CardPaths.Select(_ => (double?)1).ToArray(), 0,
            new string?[plan.CardPaths.Count], pauses, default, (_, _) => null)
        {
            VolumeOfDestination = d => "V" + d,
            ReadStallTimeout = TimeSpan.FromSeconds(3),
        };
        var file = Path.GetFullPath(Layout.Join(plan.CardPaths[1], "SOUND/b.wav"));
        var slowFile = Path.GetFullPath(Layout.Join(plan.CardPaths[0], "SOUND/b.wav"));
        using var disk = new ManualResetEventSlim();
        Assert.True(BlockReads.ReadHooks.TryAdd(slowFile, () => Thread.Sleep(300)));   // destination 1 still on b.wav at the press
        Assert.True(BlockReads.ReadHooks.TryAdd(file, () =>
        {
            pauses.Pause();
            disk.Wait(TimeSpan.FromSeconds(60));   // the disk of destination 2 stops answering
        }));
        using var stage = pauses.Work();   // stage 2: its clip is done, it waits at the gate
        var standing = TestThreads.Start(() =>
        {
            Assert.True(SpinWait.SpinUntil(() => pauses.IsPaused, TimeSpan.FromSeconds(30)));
            return stage.WaitWhilePaused(CancellationToken.None);
        });
        try
        {
            s.StartBackground(stage);
            Assert.True(Eventually(() => pauses.IsStanding, TimeSpan.FromSeconds(30)));   // without Finish: watched all along
            Assert.All(s.Results[1], r => Assert.Equal((FileState.Failed, Msg.VerifyDiskHung), (r.State, r.Error)));
            pauses.Resume();
            Assert.True(await standing.WaitAsync(TimeSpan.FromSeconds(30)));
            await Task.Run(() => s.Finish(stage)).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.All(s.Results[0], r => Assert.Equal(FileState.Verified, r.State));
            Assert.All(s.Results[1], r => Assert.Equal(FileState.Failed, r.State));
        }
        finally
        {
            BlockReads.ReadHooks.TryRemove(file, out _);
            BlockReads.ReadHooks.TryRemove(slowFile, out _);
            disk.Set();
        }
    }

    // Re-review N-1: Pozastavit while stage 2 still renders its clip ("Pozastavuji…"): a background reader at the lowest
    // I/O priority that the render starves for longer than the stall limit — alive, not hung — is never judged then (as
    // without a pause): no VerifyDiskHung, no "Neformátovat" for a healthy disk. Only once stage 2 waits at the gate.
    [Fact]
    public async Task PausedWhileTheClipRenders_StarvedReaderNotGivenUp()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block), ("SOUND/c.wav", 2 * Block));
        var pauses = new PauseGate();
        using var s = new VerifySession(plan, report, e.Blocks, Block, plan.CardPaths.Select(_ => (double?)1).ToArray(), 0,
            new string?[plan.CardPaths.Count], pauses, default, (_, _) => null)
        {
            VolumeOfDestination = d => "V" + d,
            ReadStallTimeout = TimeSpan.FromSeconds(3),   // as wide as in the test above: low-priority reads under load
        };
        var file = Path.GetFullPath(Layout.Join(plan.CardPaths[1], "SOUND/b.wav"));
        int calls = 0;
        using var starved = new ManualResetEventSlim();
        Assert.True(BlockReads.ReadHooks.TryAdd(file, () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            pauses.Pause();
            Thread.Sleep(7000);   // one block starved by the render: over 2× the limit, then it reads on
            starved.Set();
        }));
        using var stage = pauses.Work();   // stage 2 busy: the clip still renders
        try
        {
            s.StartBackground(stage);
            Assert.True(starved.Wait(TimeSpan.FromSeconds(30)));
            Thread.Sleep(300);
            Assert.DoesNotContain(s.Results[1], r => r.State == FileState.Failed);
            var standing = TestThreads.Start(() => stage.WaitWhilePaused(CancellationToken.None));   // the clip is done
            Assert.True(Eventually(() => pauses.IsStanding, TimeSpan.FromSeconds(30)));
            pauses.Resume();
            Assert.True(await standing.WaitAsync(TimeSpan.FromSeconds(30)));
            await Task.Run(() => s.Finish(stage)).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.All(s.Results, rs => Assert.All(rs, r => Assert.Equal(FileState.Verified, r.State)));
        }
        finally
        {
            BlockReads.ReadHooks.TryRemove(file, out _);
        }
    }

    // A disk unplugged during stage 2: its copies fail (repair copies them again later), the other disk is verified.
    [Fact]
    public void DiskUnplugged_ItsCopiesFail_TheOtherDiskVerified()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        using var s = Session(e, plan, report, preferred: 0);
        Directory.Delete(plan.DestRoots[1], recursive: true);

        s.StartBackground();
        s.Finish();

        Assert.All(s.Results[0], r => Assert.Equal(FileState.Verified, r.State));
        Assert.All(s.Results[1], r =>
        {
            Assert.Equal(FileState.Failed, r.State);
            Assert.StartsWith("verify: ", r.Error);
        });
    }

    // Preflight M6: a copy without block hashes is not left "written" without a reason.
    [Fact]
    public void NoBlockHashes_Failed_WithTheReason()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        var blocks = e.Blocks.ToArray();
        blocks[Index(plan, "SOUND/b.wav")] = null;
        blocks[Index(plan, "CLIP/a.mov")] = [1, 2];   // another block size (preflight M2): never verified
        using var s = Session(e, plan, report, preferred: 0, blocks: blocks);

        s.StartBackground();
        s.Finish();

        Assert.All(s.Results, d =>
        {
            Assert.Equal(new DestFileResult(FileState.Failed, "verify: no block hashes"), d[Index(plan, "SOUND/b.wav")]);
            Assert.Equal(new DestFileResult(FileState.Failed, "verify: block hashes do not fit the file"), d[Index(plan, "CLIP/a.mov")]);
        });
        Assert.Equal([0, 0], s.ToVerify);
    }

    // The report of stage 2: the verdicts, the method per destination, no verifyPending; a copy left "written" in a
    // verified report is not "verified" — nor in the ASC MHL (spec 9: only verified files go in).
    [Fact]
    public void WithVerification_TheFinalReport_AndItsCommit()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 2 * Block), ("SOUND/b.wav", Block));
        using var s = Session(e, plan, report, preferred: 1);
        s.StartBackground();
        s.Finish();
        var results = s.Results.Select(d => d.ToArray()).ToList();
        results[0][Index(plan, "SOUND/b.wav")] = new DestFileResult(FileState.Written, null);   // as if a read never ended

        var final = report.WithVerification(results, verified: true, s.Primary, s.ClassicWhy, ["poznámka"], DateTime.UtcNow, TimeSpan.Zero);

        Assert.Null(final.VerifyPending);
        Assert.True(final.Verified);
        Assert.Equal([ReportDestination.ReadBack, ReportDestination.InProxy], final.Destinations.Select(d => d.VerifyMethod));
        Assert.Equal(Msg.ClassicSecondary, final.Destinations[0].VerifyNote);
        Assert.Equal((1, 1), (final.Destinations[0].FilesOK, final.Destinations[0].FilesFailed));
        Assert.Equal(["poznámka"], final.VerifyNotes);
        Assert.False(final.SafeToFormat);
        Assert.Null(final.SafeToFormatSeconds);
        Assert.True(report.VerifyPending);   // the stage-1 report is a different object, unchanged
        foreach (var p in plan.CardPaths) final.Commit(p);
        var mhl = Verification.MhlReader.Read(plan.CardPaths[0])!;
        Assert.False(mhl.Files.ContainsKey("SOUND/b.wav"));
        Assert.True(mhl.Files.ContainsKey("CLIP/a.mov"));
    }

    [Fact]
    public void WithVerification_AllVerified_SafeToFormat_WithItsTime()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 2 * Block), ("SOUND/b.wav", Block));
        using var s = Session(e, plan, report, preferred: 0);
        s.StartBackground();
        s.Finish();

        var final = report.WithVerification(s.Results, verified: true, s.Primary, s.ClassicWhy, s.Notes, DateTime.UtcNow, TimeSpan.Zero);

        Assert.True(final.SafeToFormat);
        Assert.NotNull(final.SafeToFormatSeconds);
        Assert.Null(final.VerifyNotes);
        Assert.Equal([ReportDestination.InProxy, ReportDestination.ReadBack], final.Destinations.Select(d => d.VerifyMethod));
    }

    [Fact]
    public void BackgroundIo_LowersTheThreadsIoPriority_AndRestoresIt()
    {
        int before = Win32.IoPriority();
        using (Win32.BackgroundIo()) Assert.Equal(0, Win32.IoPriority());
        Assert.Equal(before, Win32.IoPriority());
    }
}
