using Offload.Core.Append;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core;

public sealed partial class OffloadEngine
{
    readonly Lock gate = new();
    readonly CancellationTokenSource cts = new();
    CopyPhase phase = CopyPhase.Copy;
    int fileIndex;
    string currentFile = "";
    int errorCount;
    long readBytes;
    DestWriter[] writers = [];
    /// Plan 10: appends the manifest's `file` records on its own thread (spec 4.5: "po každém souboru řádek").
    SerialWorker? recorder;
    long[]? planned;
    OffloadPlan? plan;

    static readonly System.Text.RegularExpressions.Regex DateFormat = new(@"^\d{4}-\d{2}-\d{2}\z");

    public OffloadOptions Options { get; }

    public OffloadEngine(OffloadOptions? options = null) => Options = options ?? new OffloadOptions();

    public void Cancel() => cts.Cancel();

    public bool IsCancelled => cts.IsCancellationRequested;

    /// "Pause": the copy and the read-back stop before their next file (the one in progress is finished); the job's
    /// stage 2 waits at the same gate between clips.
    public PauseGate Pauses { get; } = new();

    /// Waits while paused; false when the job was cancelled meanwhile (stage 2 uses it before every clip).
    public bool WaitWhilePaused() => Pauses.WaitWhilePaused(cts.Token);

    /// Cancelled with Cancel(): the verification of stage 2 stops on it too (plan 10).
    internal CancellationToken Cancellation => cts.Token;

    /// After Run (plan 10): per file, the XXH3-64 of its blocks as the card gave them (null when it was not read to the
    /// end); per destination, its write speed and whether it was written past the file cache.
    public ulong[]?[] Blocks { get; private set; } = [];
    public IReadOnlyList<double?> WriteRates { get; private set; } = [];
    public IReadOnlyList<bool> WrittenUnbuffered { get; private set; } = [];

    /// Ruling I1, asked again in stage 2 (plan 10, spec 10): is the copied card still in the drive? Null = never asked.
    internal bool? CardStillThere() => SameCard is null ? null : StillSameCard();

    /// The file error (report JSON, English like every file error) of the files left out because the card in the drive
    /// was not the copied one after a pause (ruling I1); the HTML says it in Czech.
    public const string CardChangedWhilePaused = "card changed while paused";

    /// Ruling I1: is the card in the drive still the one being copied? Asked on the copy thread before the next file
    /// after a pause held the copy (the card may have been swapped or pulled meanwhile — same names, sizes and times are
    /// no proof); false or an exception = it is not. Null = never asked (a folder source has no card identity).
    internal Func<bool>? SameCard { get; set; }

    /// Fix round 1: how many times more the card is identified, 250 ms apart, before it counts as changed — a reader
    /// that answers late once after a pause must not fail the job.
    internal const int IdentifyRetries = 3;

    /// The card was not the copied one after a pause: the remaining files were left out (CardChangedWhilePaused).
    public bool CardChanged => cardChanged;
    volatile bool cardChanged;

    /// Scans the card and checks the destinations before a single byte is written. `extra`: what proxies and the report
    /// will need besides the card (spec 2.5), worked out from the scan; null = the card alone.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date,
        Func<SourceScan, SpaceExtra>? extra = null)
    {
        CheckOptions();
        if (date != null && !DateFormat.IsMatch(date)) throw OffloadError.Preflight(PreflightCode.Date, Msg.Date(date));
        source = CheckSource(source);
        var roots = FullRoots(destRoots);
        CheckNotAnOffload(source);
        CheckDestinations(source, roots);
        var scan = ScanSource(source);
        var (space, warnings) = CheckFilesFit(roots, scan, extra?.Invoke(scan));

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan
        {
            Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots, Space = space, SpaceWarnings = warnings,
            Hashes = HashKinds.Normalize(Options.Hashes),
        };
    }

    /// An append (spec 2.2): the same checks as Prepare (options, source, destinations, Windows names, FAT32, space),
    /// but over the new files only, into the card folders of the earlier backup. `NothingNew` when there are none.
    public OffloadPlan PrepareAppend(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default,
        Func<SourceScan, SpaceExtra>? extra = null) =>
        PrepareAppend(PlanAppend(source, cardPaths, ct), extra);

    /// The first half of PrepareAppend: the checks of the source and the destinations, and what AppendPlanner finds —
    /// also when there is nothing new (a job then refreshes its history entry). Cancel() stops the card scan too.
    public AppendPlan PlanAppend(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)
    {
        CheckOptions();
        source = CheckSource(source);
        FullRoots(cardPaths);   // at least one, none twice
        CheckNotAnOffload(source);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
        var a = AppendPlanner.Plan(source, cardPaths, linked.Token);
        CheckDestinations(source, a.DestRoots);
        return a;
    }

    /// The second half of PrepareAppend: the new files must fit (names, FAT32, space) — then the plan of generation N.
    public OffloadPlan PrepareAppend(AppendPlan a, Func<SourceScan, SpaceExtra>? extra = null)
    {
        if (a.NewFiles.Count == 0) throw OffloadError.Preflight(PreflightCode.NothingNew, Msg.NothingNew);
        var scan = new SourceScan { Root = a.FullScan.Root, Files = a.NewFiles, Dirs = a.NewDirs, Skipped = a.FullScan.Skipped };
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0) throw OffloadError.Preflight(PreflightCode.BadNames, Msg.BadNames(problems));
        var (space, warnings) = CheckFilesFit(a.DestRoots, scan, extra?.Invoke(scan));
        // Ruling M2: a resume works in the block size of the run it completes — its manifest's, not the setting's (ChunkMB
        // may have changed since): the recorded blocks, the checkpoints and the verification must agree. A record of
        // another size has its hash read from the card again (HashOnly); a part of another size is copied from its start.
        if (a.Resuming && ManifestBlockSize(a) is int recorded) Options.ChunkSize = recorded;
        int size = Options.ChunkSize;
        return new OffloadPlan
        {
            Source = a.Source, Scan = scan, Date = a.Date, CardName = a.CardName, DestRoots = a.DestRoots,
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
            Space = space, SpaceWarnings = warnings,
            Hashes = HashKinds.FromNames(a.HashAlgorithms),   // the earlier backup's, not the current setting (spec 3.5)
            // R6: completing an unfinished generation — what is on the disks already is not written again.
            Resuming = a.Resuming, Write = a.Write, NotInBackup = a.Later,
            Known = a.Known?.Select(k => k?.BlockSize == size ? k : null).ToList(),
            HashOnly = a.HashOnly?.Select((h, i) => h || a.Known?[i] is { } k && k.BlockSize != size).ToList(),
            Resume = a.Resume?.Select(r => r?.BlockSize == size ? r : null).ToList(),
        };
    }

    /// Ruling M2: the block size the unfinished run recorded (its records and checkpoints — one run writes one size; the
    /// most common one otherwise), when it is one this engine can read and write (whole sectors, at most 256 MiB).
    static int? ManifestBlockSize(AppendPlan a)
    {
        var sizes = (a.Known ?? []).OfType<VerifyInProxy.ManifestRecord>().Select(k => k.BlockSize)
            .Concat((a.Resume ?? []).OfType<VerifyInProxy.PartCheckpoint>().Select(r => r.BlockSize)).ToList();
        if (sizes.Count == 0) return null;
        int size = sizes.GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        return size > 0 && size % 4096 == 0 && size <= 256 << 20 ? size : null;
    }

    /// A repair ("Copy again", UI návrh 4): the checks of an append (options, source, the earlier backup and its
    /// destinations), then what RepairPlanner finds — the card compared with the backup by size, time and hash before
    /// anything is written. `RepairNothing` when every file is good everywhere. Cancel() stops it too.
    public RepairPlan PlanRepair(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default)
    {
        CheckOptions();
        source = CheckSource(source);
        FullRoots(cardPaths);
        CheckNotAnOffload(source);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
        var r = RepairPlanner.Plan(source, cardPaths, linked.Token);
        CheckDestinations(source, r.DestRoots);
        return r;
    }

    /// The plan of a repair: only the files that failed, only onto the destinations where they failed (FAT32 and the
    /// space checked there), generation N in the same card folders. Always read back (ruling M16).
    public OffloadPlan PrepareRepair(RepairPlan r)
    {
        if (!Options.Verify) throw OffloadError.Preflight(PreflightCode.RepairConflict, Msg.RepairNeedsVerify);
        var scan = new SourceScan { Root = r.Source, Files = r.Files, Dirs = r.Dirs, Skipped = [] };
        var places = new List<SpacePlace>();
        for (int d = 0; d < r.DestRoots.Count; d++)
        {
            var written = r.Files.Where((_, i) => r.Write[i][d]).ToList();
            if (Fat32Problem(NativeIO.FilesystemType(r.DestRoots[d]), written) is string fat)
                throw OffloadError.Preflight(PreflightCode.Fat32, $"{r.DestRoots[d]}: {fat}");
            long card = written.Count == 0 ? 0 : SpaceEstimator.CardBytes(written.Sum(f => f.Size), written.Count);
            places.Add(new SpacePlace(r.DestRoots[d], HistoryText.DiskName(r.DestRoots[d]), new SpaceNeed(card, 0, SpaceEstimator.ReportBytes(0))));
        }
        var (problems, lines) = SpacePlanner.CheckPlaces(places, FreeBytes);
        if (problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking) is { } blocking)
            throw OffloadError.Preflight(PreflightCode.NoSpace, blocking.Text);
        return new OffloadPlan
        {
            Source = r.Source, Scan = scan, Date = r.Date, CardName = r.CardName, DestRoots = r.DestRoots,
            Mode = PlanMode.Repair, Generation = r.Generation, Space = lines, SpaceWarnings = problems.Select(p => p.Text).ToList(),
            Hashes = HashKinds.FromNames(r.HashAlgorithms), Write = r.Write, Expected = r.Expected, SetAside = r.SetAside,
            ReadBackBefore = r.ReadBackBefore, NotInBackup = r.NotInBackup,
        };
    }

    void CheckOptions()
    {
        if (Options.ChunkSize <= 0 || Options.ChunkSize % 4096 != 0 || Options.ChunkSize > 1 << 30)
            throw OffloadError.Preflight(PreflightCode.ChunkSize, Msg.ChunkSize(Options.ChunkSize));
        if (Options.BufferCount < 2)
            throw OffloadError.Preflight(PreflightCode.BufferCount, Msg.BufferCount(Options.BufferCount));
    }

    /// The source exists; returns its full path.
    static string CheckSource(string source)
    {
        if (!Directory.Exists(source)) throw OffloadError.Preflight(PreflightCode.SourceNotFound, Msg.SourceNotFound(source));
        return Path.GetFullPath(source);
    }

    /// Full paths of the destination roots: at least one, none twice.
    static List<string> FullRoots(IReadOnlyList<string> destRoots)
    {
        var roots = destRoots.Select(Path.GetFullPath).ToList();
        if (roots.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        for (int i = 0; i < roots.Count; i++)
            for (int j = 0; j < i; j++)
                if (string.Equals(roots[i].TrimEnd(Path.DirectorySeparatorChar), roots[j].TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw OffloadError.Preflight(PreflightCode.DuplicateDestination, Msg.DuplicateDestination(roots[i]));
        return roots;
    }

    /// A folder with _REPORT, _PROXY or ascmhl is a destination of an earlier offload, not a card.
    static void CheckNotAnOffload(string source)
    {
        foreach (var marker in new[] { Layout.ReportDirName, Layout.ProxyDirName, Mhl.DirName })
            if (Directory.EnumerateDirectories(source).Any(d => string.Equals(Path.GetFileName(d), marker, StringComparison.OrdinalIgnoreCase)))
                throw OffloadError.Preflight(PreflightCode.SourceIsOffload, Msg.SourceIsOffload);
    }

    /// Every destination exists, is writable, is not on the source's volume and (unless allowed) not on another
    /// destination's volume.
    void CheckDestinations(string source, IReadOnlyList<string> roots)
    {
        var sourceVolume = NativeIO.VolumeId(source) ?? throw OffloadError.Preflight(PreflightCode.SourceUnreadable, Msg.SourceUnreadable(source));
        var volumes = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) throw OffloadError.Preflight(PreflightCode.DestinationNotFound, Msg.DestinationNotFound(root));
            if (!IsWritable(root)) throw OffloadError.Preflight(PreflightCode.DestinationNotWritable, Msg.DestinationNotWritable(root));
            var volume = NativeIO.VolumeId(root) ?? throw OffloadError.Preflight(PreflightCode.DestinationUnreadable, Msg.DestinationUnreadable(root));
            if (volume == sourceVolume && !Options.AllowSourceVolume)
                throw OffloadError.Preflight(PreflightCode.SameVolumeAsSource, Msg.SameVolumeAsSource(root));
            if (volumes.Contains(volume) && !Options.AllowSameDevice)
                throw OffloadError.Preflight(PreflightCode.SharedVolume, Msg.SharedVolume(root));
            volumes.Add(volume);
        }
    }

    /// The card's files; none at all, or names Windows cannot store, stop the job.
    static SourceScan ScanSource(string source)
    {
        var scan = Scanner.Scan(source);
        if (scan.Files.Count == 0) throw OffloadError.Preflight(PreflightCode.EmptySource, Msg.EmptySource);
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0) throw OffloadError.Preflight(PreflightCode.BadNames, Msg.BadNames(problems));
        return scan;
    }

    /// The FAT32 limit on every destination, and the space per volume (spec 2.5: two destinations on one volume add up,
    /// the proxy folder counts on its own volume) for `scan` — the whole card, or only the new files of an append.
    /// Returns the per-volume breakdown and the "rezerva pod 5 %" warnings; not enough space throws.
    (IReadOnlyList<SpaceLine> Lines, IReadOnlyList<string> Warnings) CheckFilesFit(IReadOnlyList<string> roots, SourceScan scan, SpaceExtra? extra)
    {
        foreach (var root in roots)
            if (Fat32Problem(NativeIO.FilesystemType(root), scan.Files) is string fat)
                throw OffloadError.Preflight(PreflightCode.Fat32, $"{root}: {fat}");
        var places = SpacePlanner.Places(roots, extra, SpaceEstimator.CardBytes(scan.TotalBytes, scan.Files.Count), HistoryText.DiskName);
        var (problems, lines) = SpacePlanner.CheckPlaces(places, FreeBytes);
        if (problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking) is { } blocking)
            throw OffloadError.Preflight(PreflightCode.NoSpace, blocking.Text);
        return (lines, problems.Select(p => p.Text).ToList());
    }

    /// Free bytes of the volume a path is on (tests: OffloadOptions.FreeSpace).
    internal long? FreeBytes(string path) => Options.FreeSpace != null ? Options.FreeSpace(path) : NativeIO.AvailableBytes(path);

    /// FAT32 cannot hold files of 4 GiB or more — say so before the copy, not at 99 %.
    internal static string? Fat32Problem(string filesystem, IEnumerable<SourceFile> files)
    {
        if (!filesystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)) return null;
        var big = files.FirstOrDefault(f => f.Size > uint.MaxValue);
        return big == null ? null : Msg.Fat32(filesystem, big.RelPath, big.Size);
    }

    static bool IsWritable(string dir)
    {
        var probe = Path.Combine(dir, $".offload-write-test-{Environment.ProcessId}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public ProgressSnapshot Snapshot()
    {
        lock (gate)
        {
            return new ProgressSnapshot(phase, plan?.Scan.TotalBytes ?? 0, plan?.Scan.Files.Count ?? 0, fileIndex, currentFile,
                Interlocked.Read(ref readBytes), writers.Select(w => w.Written).ToArray(), writers.Select(w => w.Verified).ToArray(), errorCount,
                Pauses.IsPaused, Pauses.IsStanding && writers.All(w => w.IsIdle) && (recorder?.IsIdle ?? true), planned,
                writers.Select(w => w.VerifyPlanned).ToArray(), writers.Select(w => w.VerifyingFile).ToArray());
        }
    }

    void SetPhase(CopyPhase p)
    {
        lock (gate) phase = p;
    }
}
