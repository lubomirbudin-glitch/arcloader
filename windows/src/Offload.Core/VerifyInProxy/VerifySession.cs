using System.IO.Hashing;

namespace Offload.Core.VerifyInProxy;

/// What the proxy loop gets for one clip from the session: mode A — the URL ffmpeg reads the clip from; mode B — nothing
/// (the clip's files were read and verified straight from the disk before the SDK). Dispose when the render ended.
public sealed class TapLease(string? url, Action release) : IDisposable
{
    int released;
    public string? Url => url;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0) release();
    }
}

/// One job's verification in stage 2 when verify-in-proxy runs (spec 3, 5, 6; R1: part of OffloadJob's stage 2, not a
/// second engine). The primary destination (the fastest one that may be verified this way, or the one the user chose)
/// is verified by what the proxies read (mode A: RangeServer + TapFile; mode B: CacheWarm), then the fill-in reads what
/// they did not; every other destination is read in the background — one thread per volume, low I/O priority; the
/// primary's files that no proxy reads (sidecars, audio, other segments) are read on the primary volume's thread while
/// no proxy reads the primary. Pause and cancel are honoured before every block. A destination that may not be verified
/// from reads that might come from the cache (UnbufferedCheck) is read in the background like a secondary one.
///
/// A copy is "verified" only when every block was hashed from a read of that destination: the tap and mode B read only
/// through a handle that really bypasses the cache; the background and the fill-in read through FileReader (ruling I3) —
/// the same, unless unbuffered reads are impossible there (the volume refuses or drops NO_BUFFERING, a compressed or EFS
/// file), then through the cache as 0.5.x did, and the notes say so. Any other error fails that copy; nothing is ever
/// written or repaired here (R7).
public sealed class VerifySession : IDisposable
{
    /// The most blocks per file noted with the card read again (spec 10); the rest are counted in one more note.
    const int MaxBlockNotes = 3;
    const string NoBlockHashes = "verify: no block hashes";
    const string BlockHashesDoNotFit = "verify: block hashes do not fit the file";

    readonly OffloadPlan plan;
    readonly IReadOnlyList<SourceFile> files;
    readonly IReadOnlyList<string> cardPaths;
    readonly BlockManifest?[] manifests;
    readonly DestFileResult[][] results;
    readonly BlockVerifier?[][] verifiers;
    readonly PauseGate pauses;
    readonly CancellationTokenSource stop;
    readonly CancellationToken token;
    readonly Func<int, int, ulong?> cardBlock;
    readonly ManualResetEventSlim tapIdle = new(true);
    readonly List<BackgroundReader> readers = [];
    readonly long[] verifiedBytes;
    readonly int[] cachedReads;
    readonly HashSet<string> proxyFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Lock gate = new();
    RangeServer? server;
    int active, taps;
    bool started, finished, disposed;

    /// The destination verified through the proxies; -1 when none may be (every one is read back).
    public int Primary { get; }
    /// Per destination: why it is read back although verify-in-proxy runs (Czech, the report's verifyNote); null for the
    /// primary.
    public IReadOnlyList<string?> ClassicWhy { get; }
    /// Per destination, per file: the verdict — the copy's states ("written", "failed") until the reads decide.
    public IReadOnlyList<DestFileResult[]> Results => results;
    /// Spec 10, Czech: unstable reads, blocks wrong on several destinations, reads through the cache — for the report.
    public List<string> Notes { get; } = [];
    /// A verification thread is reading now (a pause stands only when none is).
    public bool Reading => Volatile.Read(ref active) > 0;
    /// Bytes verified so far per destination (never above ToVerify), and what there is to verify (the "Dokončení
    /// ověření" bar).
    public long[] VerifiedBytes => Enumerable.Range(0, verifiedBytes.Length).Select(d => Math.Min(Interlocked.Read(ref verifiedBytes[d]), ToVerify[d])).ToArray();
    public long[] ToVerify { get; }
    /// Per destination: the copies verified through the file cache (ruling I3, only where reads past it are impossible).
    public int[] CachedReads
    {
        get { lock (gate) return [.. cachedReads]; }
    }

    /// Tests: the volume of destination d (null = NativeIO.VolumeId of its card folder).
    internal Func<int, string>? VolumeOfDestination { get; init; }

    /// The background threads StartBackground started: their volume, destinations, whether they wait for the tap, and
    /// the I/O priority they read at (Win32.IoPriority; -1 until known).
    internal sealed class BackgroundReader(string volume, int[] dests, bool gated)
    {
        public string Volume => volume;
        public int[] Dests => dests;
        public bool Gated => gated;
        public volatile int IoPriority = -1;
        // Watched by WaitFor (a disk that hangs for good is given up on): blocks read so far, a wait (pause, tap) under
        // way, the thread given up on, whether it still counts as reading, and the thread.
        public long Progress;
        public int Waiting;
        public volatile bool Abandoned;
        public int ActiveHeld;
        public Thread? Thread;
    }

    internal IReadOnlyList<BackgroundReader> Readers
    {
        get { lock (gate) return [.. readers]; }
    }

    /// `stage1`: the copy's report (states "written"/"failed" per destination); `blocks`: per file the card's block hashes
    /// (OffloadEngine.Blocks); `rates`: per destination the write rate of the copy (OffloadEngine.WriteRates);
    /// `preferred`: the user's primary destination (0-based) or null = the fastest; `classicWhy`: per destination a reason
    /// to read it back (UnbufferedCheck or the setting) or null; `cardBlock`: the XXH3-64 of block b of file i read from
    /// the card again — null when the card is not the copied one any more (spec 10).
    public VerifySession(OffloadPlan plan, OffloadReport stage1, IReadOnlyList<ulong[]?> blocks, int blockSize, IReadOnlyList<double?> rates,
        int? preferred, IReadOnlyList<string?> classicWhy, PauseGate pauses, CancellationToken ct, Func<int, int, ulong?> cardBlock)
    {
        this.plan = plan;
        files = plan.Scan.Files;
        cardPaths = plan.CardPaths;
        this.pauses = pauses;
        stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        token = stop.Token;
        this.cardBlock = cardBlock;
        int dests = cardPaths.Count;
        var why = new string?[files.Count];
        manifests = files.Select((f, i) => ManifestOf(f, i < blocks.Count ? blocks[i] : null, blockSize, out why[i])).ToArray();
        results = Enumerable.Range(0, dests).Select(d => files.Select((_, i) => StateOf(stage1.Files[i], d)).ToArray()).ToArray();
        // Preflight M6/M2: a copy without block hashes that fit it is never verified — failed with the reason, not left "written".
        foreach (var r in results)
            for (int i = 0; i < files.Count; i++)
                if (r[i].State == FileState.Written && manifests[i] == null) r[i] = new DestFileResult(FileState.Failed, why[i]);
        verifiers = Enumerable.Range(0, dests).Select(_ => new BlockVerifier?[files.Count]).ToArray();
        verifiedBytes = new long[dests];
        cachedReads = new int[dests];
        ToVerify = Enumerable.Range(0, dests).Select(d => files.Where((_, i) => results[d][i].State == FileState.Written).Sum(f => f.Size)).ToArray();
        var eligible = Enumerable.Range(0, dests).Where(d => classicWhy[d] == null).ToList();
        Primary = preferred is int p && eligible.Contains(p) ? p
            : eligible.Count == 0 ? -1 : eligible.OrderByDescending(d => rates.Count > d ? rates[d] ?? 0 : 0).ThenBy(d => d).First();
        ClassicWhy = Enumerable.Range(0, dests).Select(d => d == Primary ? null : classicWhy[d] ?? Msg.ClassicSecondary).ToList();
        if (Primary >= 0)
        {
            foreach (var clip in ClipDetector.Detect(files))
            {
                var mode = TapRegistry.ModeFor(clip);
                if (mode == TapMode.Http) proxyFiles.Add(clip.Path);
                else if (mode == TapMode.CacheWarm) proxyFiles.UnionWith(clip.Segments);
            }
        }
    }

    static BlockManifest? ManifestOf(SourceFile f, ulong[]? blocks, int blockSize, out string? why)
    {
        why = null;
        if (blocks == null)
        {
            why = NoBlockHashes;
            return null;
        }
        try
        {
            return new BlockManifest(f.Size, blockSize, blocks);
        }
        catch (ArgumentException)
        {
            why = BlockHashesDoNotFit;
            return null;
        }
    }

    static DestFileResult StateOf(ReportFile f, int d) => (d < f.Destinations.Count ? f.Destinations[d] : "pending") switch
    {
        "written" when f.SourceError == null => new DestFileResult(FileState.Written, null),
        "verified" => new DestFileResult(FileState.Verified, null),
        "failed" => new DestFileResult(FileState.Failed, d < f.Errors.Count ? f.Errors[d] : null),
        _ => new DestFileResult(FileState.Pending, null),
    };

    /// Starts the background reads: every destination but the primary in full, and the primary's files no proxy reads —
    /// one thread per volume; the primary volume's thread waits while a proxy reads the primary.
    public void StartBackground()
    {
        lock (gate)
        {
            if (started || finished || disposed) return;
            started = true;
        }
        var byVolume = new Dictionary<string, List<(int Dest, int File)>>(StringComparer.OrdinalIgnoreCase);
        string? primaryVolume = Primary >= 0 ? VolumeOf(Primary) : null;
        for (int d = 0; d < cardPaths.Count; d++)
        {
            var volume = VolumeOf(d);
            for (int i = 0; i < files.Count; i++)
            {
                if (results[d][i].State != FileState.Written || manifests[i] == null) continue;
                if (d == Primary && proxyFiles.Contains(files[i].RelPath)) continue;   // the proxy reads it (or the fill-in)
                (byVolume.TryGetValue(volume, out var list) ? list : byVolume[volume] = []).Add((d, i));
            }
        }
        foreach (var (volume, work) in byVolume)
        {
            var reader = new BackgroundReader(volume, work.Select(w => w.Dest).Distinct().Order().ToArray(),
                string.Equals(volume, primaryVolume, StringComparison.OrdinalIgnoreCase));
            var t = new Thread(() => Background(reader, work)) { IsBackground = true, Name = "offload.verify." + work[0].Dest };
            reader.Thread = t;
            lock (gate) readers.Add(reader);
            t.Start();
        }
    }

    string VolumeOf(int d) => VolumeOfDestination?.Invoke(d) ?? NativeIO.VolumeId(cardPaths[d]) ?? cardPaths[d];

    void Background(BackgroundReader reader, List<(int Dest, int File)> work)
    {
        try
        {
            using var low = Win32.BackgroundIo();
            reader.IoPriority = Win32.IoPriority();
            Interlocked.Increment(ref active);
            Volatile.Write(ref reader.ActiveHeld, 1);
            try
            {
                foreach (var (d, i) in work)
                {
                    if (token.IsCancellationRequested || reader.Abandoned) return;
                    Verify(d, i, reader.Gated, reader);
                }
            }
            finally
            {
                ReleaseActive(reader);
            }
        }
        catch (Exception)
        {
            // Never take the process down from a background thread: Verify already made every file's error its verdict.
        }
    }

    /// A reader that ends, or is given up on, no longer counts as reading — once.
    void ReleaseActive(BackgroundReader r)
    {
        if (Interlocked.Exchange(ref r.ActiveHeld, 0) == 1) Interlocked.Decrement(ref active);
    }

    /// Reads what destination `d`'s copy of file `i` still lacks and records the verdict; a cancel leaves it "written",
    /// any error (an unplugged disk, a locked or vanished copy) fails it — never a verdict without the whole read.
    /// `reader` carries the progress WaitFor watches and the flag that the thread was given up on (then it writes nothing).
    void Verify(int d, int i, bool gated, BackgroundReader reader)
    {
        BlockVerifier v;
        lock (gate) v = verifiers[d][i] ??= new BlockVerifier(manifests[i]!);
        try
        {
            var (outcome, fromCache) = BlockReads.CompleteClassic(Layout.Join(cardPaths[d], files[i].RelPath), v,
                () =>
                {
                    Volatile.Write(ref reader.Waiting, 1);
                    try { return !reader.Abandoned && Before(gated); }
                    finally
                    {
                        Volatile.Write(ref reader.Waiting, 0);
                        Interlocked.Increment(ref reader.Progress);
                    }
                },
                n =>
                {
                    Interlocked.Add(ref verifiedBytes[d], n);
                    Interlocked.Add(ref reader.Progress, n);
                }, token);
            var result = BlockReads.Result(outcome, v);
            lock (gate)
            {
                if (reader.Abandoned) return;
                results[d][i] = result;
                if (fromCache && result.State == FileState.Verified) cachedReads[d]++;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            lock (gate)
                if (!reader.Abandoned) results[d][i] = new DestFileResult(FileState.Failed, "verify: " + e.Message);
        }
    }

    /// The fill-in's read of one file, on a thread of its own so that a disk that hangs cannot hold the finishing thread.
    void VerifyWatched(int d, int i)
    {
        var reader = new BackgroundReader(VolumeOf(d), [d], gated: false);
        var worker = new Thread(() => Verify(d, i, gated: false, reader)) { IsBackground = true, Name = "offload.verify.fill." + d };
        reader.Thread = worker;
        worker.Start();
        WaitFor(reader);
    }

    /// Waits for a reader's thread. With no block read for StallTimeout (a pause or a wait for the tap not counting) its
    /// disk is hung — not unplugged, that would fail the read: the thread is given up on (it stops at its next block, or
    /// stays in the read it hangs in and writes nothing), every file of its destinations still "written" fails with
    /// Msg.VerifyDiskHung, and the job goes on — never green (item 1 of 0.6.1, the pattern of mode B in Prepare).
    void WaitFor(BackgroundReader reader)
    {
        var thread = reader.Thread!;
        long seen = -1;
        var still = System.Diagnostics.Stopwatch.StartNew();
        while (!thread.Join(PollInterval))
        {
            long now = Interlocked.Read(ref reader.Progress);
            if (now != seen || pauses.IsPaused || Volatile.Read(ref reader.Waiting) != 0)
            {
                seen = now;
                still.Restart();
            }
            else if (still.Elapsed >= ReadStallTimeout)
            {
                lock (gate) reader.Abandoned = true;
                ReleaseActive(reader);
                foreach (var d in reader.Dests) FailHungDestination(d);
                return;
            }
        }
    }

    /// A destination's disk stopped answering: every file of it still "written" is failed — never green.
    void FailHungDestination(int d)
    {
        lock (gate)
            for (int j = 0; j < files.Count; j++)
                if (results[d][j].State == FileState.Written) results[d][j] = new DestFileResult(FileState.Failed, Msg.VerifyDiskHung);
    }

    /// Before every block: wait while a proxy reads the primary (`gated`), then at the pause gate; false = cancelled.
    /// Waiting is not reading (a pause stands then).
    bool Before(bool gated)
    {
        Interlocked.Decrement(ref active);
        try
        {
            if (gated) WaitHandle.WaitAny([tapIdle.WaitHandle, token.WaitHandle]);
            return !token.IsCancellationRequested && pauses.WaitWhilePaused(token);
        }
        finally
        {
            Interlocked.Increment(ref active);
        }
    }

    /// A proxy starts or stops reading the primary (the primary volume's background thread waits meanwhile).
    void TapBusy()
    {
        lock (gate)
            if (taps++ == 0 && !finished && !disposed) tapIdle.Reset();
    }

    void TapDone()
    {
        lock (gate)
        {
            if (--taps > 0) return;
            if (!disposed) tapIdle.Set();
            else tapIdle.Dispose();   // the last lease ended after the session
        }
    }

    /// Before the proxy of `clip` renders from destination `dest`: mode A gives the URL to read it from; mode B reads its
    /// files now, straight from the disk (verified; ruling B1 — the SDK then reads them from the disk again). Null when
    /// the proxy reads another destination, the clip is not read through the session, its copy here is not "written",
    /// or the tap cannot open it unbuffered — the fill-in verifies it then.
    public TapLease? Prepare(Clip clip, int dest)
    {
        lock (gate)
            if (finished || disposed) return null;
        if (dest != Primary || token.IsCancellationRequested) return null;
        var mode = TapRegistry.ModeFor(clip);
        if (mode == TapMode.Http && FileIndex(clip.Path) is int i && Writable(i))
        {
            BlockVerifier v;
            lock (gate) v = verifiers[Primary][i] ??= new BlockVerifier(manifests[i]!);
            TapFile tap;
            try { tap = new TapFile(Layout.Join(cardPaths[Primary], clip.Path), v); }
            catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { return null; }
            RangeServer srv;
            lock (gate) srv = server ??= new RangeServer();
            var url = srv.Register(tap, Path.GetFileName(clip.Path));
            TapBusy();
            return new TapLease(url, () =>
            {
                srv.Unregister(url);
                tap.Dispose();
                Interlocked.Add(ref verifiedBytes[Primary], v.VerifiedBytes);
                TapDone();
            });
        }
        if (mode == TapMode.CacheWarm)
        {
            var segments = clip.Segments.Select(FileIndex).OfType<int>().Where(Writable).ToList();
            if (segments.Count == 0 || !CacheWarm.Fits(segments.Sum(s => files[s].Size))) return null;
            TapBusy();
            Interlocked.Increment(ref active);
            // Final review, item 4: the reads run on their own thread, watched like the proxy (StallTimeout without a
            // block read, not counting a pause) — a hung (not unplugged) primary disk never holds the job thread. Given
            // up on, the thread stops at its next block (or stays in the read it hangs in); the fill-in decides.
            int ended = 0, abandoned = 0;
            long progress = 0;
            void End()
            {
                if (Interlocked.Exchange(ref ended, 1) == 0)
                {
                    Interlocked.Decrement(ref active);
                    TapDone();
                }
            }
            var done = new ManualResetEventSlim();
            var reader = new Thread(() =>
            {
                try
                {
                    foreach (var s in segments)
                    {
                        BlockVerifier v;
                        lock (gate) v = verifiers[Primary][s] ??= new BlockVerifier(manifests[s]!);
                        try
                        {
                            CacheWarm.VerifyAndWarm(Layout.Join(cardPaths[Primary], files[s].RelPath), v,
                                () => Volatile.Read(ref abandoned) == 0 && Before(gated: false),
                                n =>
                                {
                                    Interlocked.Add(ref verifiedBytes[Primary], n);
                                    Interlocked.Add(ref progress, n);
                                }, token);
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception) { }   // the fill-in decides (and fails the copy if it cannot read it either)
                    }
                }
                finally
                {
                    End();
                    done.Set();
                }
            }) { IsBackground = true, Name = "Offload verify (mode B)" };
            reader.Start();
            long seen = -1;
            var still = System.Diagnostics.Stopwatch.StartNew();
            while (!done.Wait(PollInterval))
            {
                long now = Interlocked.Read(ref progress);
                if (now != seen || pauses.IsPaused)
                {
                    seen = now;
                    still.Restart();
                }
                else if (still.Elapsed >= StallTimeout || token.IsCancellationRequested)
                {
                    Volatile.Write(ref abandoned, 1);
                    End();
                    return new TapLease(null, () => { });
                }
            }
            done.Dispose();
            return new TapLease(null, () => { });
        }
        return null;
    }

    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// Mode B: how long Prepare's read of a clip may go without reading a block before it is given up on (a hung primary
    /// disk) — the proxy's stall watchdog's limit (OffloadJob gives it).
    internal TimeSpan StallTimeout { get; init; } = ProxyRenderer.DefaultStallTimeout;

    /// The same limit for the background reads and the fill-in (WaitFor). Kept apart from StallTimeout: those reads run at
    /// the lowest I/O priority and a busy disk may starve them for a long while without being hung, so tests that make
    /// StallTimeout short for mode B do not make them look hung.
    internal TimeSpan ReadStallTimeout { get; init; } = ProxyRenderer.DefaultStallTimeout;

    int? FileIndex(string rel)
    {
        for (int i = 0; i < files.Count; i++)
            if (string.Equals(files[i].RelPath, rel, StringComparison.OrdinalIgnoreCase)) return i;
        return null;
    }

    bool Writable(int i)
    {
        lock (gate) return Primary >= 0 && results[Primary][i].State == FileState.Written && manifests[i] != null;
    }

    /// After the proxies (spec 6): the fill-in reads on the primary what the proxies did not (the files a proxy was meant to
    /// read — the others are the background thread's), the background threads finish (waiting while paused), then the
    /// notes of spec 10 — unstable reads, a block wrong on several destinations compared with the card — and of the
    /// reads through the cache (ruling I3).
    public void Finish()
    {
        lock (gate)
        {
            if (finished || disposed) return;
            finished = true;
            tapIdle.Set();
        }
        if (Primary >= 0)
        {
            Interlocked.Increment(ref active);
            try
            {
                for (int i = 0; i < files.Count && !token.IsCancellationRequested; i++)
                    if (proxyFiles.Contains(files[i].RelPath) && Writable(i)) VerifyWatched(Primary, i);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
        foreach (var r in Readers) WaitFor(r);
        Annotate();
    }

    void Annotate()
    {
        for (int i = 0; i < files.Count; i++)
        {
            var path = files[i].RelPath;
            for (int d = 0; d < cardPaths.Count; d++)
            {
                // Unstable, but matching in the end (a block still wrong fails the copy, its error says so).
                if (verifiers[d][i] is not { } v) continue;
                var unstable = v.UnstableBlocks.Where(b => v.DiskHash(b) == null).ToList();
                if (unstable.Count > 0) Notes.Add(Msg.UnstableRead(path, DiskOf(d), unstable));
            }
            if (manifests[i] is not { } manifest) continue;
            int noted = 0, more = 0;
            for (int block = 0; block < manifest.Count; block++)
            {
                var dests = Enumerable.Range(0, cardPaths.Count).Where(d => verifiers[d][i]?.DiskHash(block) != null).ToList();
                if (dests.Count < 2) continue;
                if (noted == MaxBlockNotes)
                {
                    more++;
                    continue;
                }
                noted++;
                var recorded = manifest.Blocks[block];
                var disks = dests.Select(d => verifiers[d][i]!.DiskHash(block)).Distinct().ToList();
                ulong? card;
                try { card = token.IsCancellationRequested ? null : cardBlock(i, block); }
                catch (Exception) { card = null; }   // the card gone or unreadable: the note says so
                Notes.Add(Msg.SameBlockWrong(path, block, string.Join(", ", dests.Select(DiskOf)), card switch
                {
                    null => Msg.CardNotReadAgain,
                    ulong c when c == recorded => Msg.CardGivesRecorded,
                    ulong c when disks.Count == 1 && c == disks[0] => Msg.CardGivesWhatDisksHave,
                    _ => Msg.CardGivesOther,
                }));
            }
            if (more > 0) Notes.Add(Msg.SameBlockWrongMore(path, more));
        }
        var cached = CachedReads;
        for (int d = 0; d < cardPaths.Count; d++)
            if (cached[d] > 0) Notes.Add(Msg.VerifiedThroughCache(DiskOf(d), cached[d]));
    }

    string DiskOf(int d) => History.HistoryText.DiskName(plan.DestRoots[d]);

    /// The XXH3-64 of block `block` (of `blockSize` bytes) of `path` on the card, read past the file cache. Only reads.
    public static ulong ReadCardBlock(string path, int block, int blockSize)
    {
        using var reader = new FileReader(path);
        using var buffer = new AlignedBuffer(blockSize);
        int n = reader.Read(buffer.Span, (long)block * blockSize);
        return XxHash3.HashToUInt64(buffer.Span[..n]);
    }

    /// Ends what still reads (a session never finished: the job failed or was closed) — the readers stop at their next
    /// block — and closes the tap server.
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        stop.Cancel();
        tapIdle.Set();
        foreach (var r in Readers) WaitFor(r);
        server?.Dispose();
        stop.Dispose();
        lock (gate)
            if (taps == 0) tapIdle.Dispose();   // else the last lease's end does it
    }
}
