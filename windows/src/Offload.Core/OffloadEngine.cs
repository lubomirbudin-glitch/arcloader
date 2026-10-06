using Offload.Core.Append;

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
    OffloadPlan? plan;

    static readonly System.Text.RegularExpressions.Regex DateFormat = new(@"^\d{4}-\d{2}-\d{2}\z");

    public OffloadOptions Options { get; }

    public OffloadEngine(OffloadOptions? options = null) => Options = options ?? new OffloadOptions();

    public void Cancel() => cts.Cancel();

    public bool IsCancelled => cts.IsCancellationRequested;

    /// Scans the card and checks the destinations before a single byte is written.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date)
    {
        CheckOptions();
        if (date != null && !DateFormat.IsMatch(date)) throw OffloadError.Preflight(PreflightCode.Date, Msg.Date(date));
        source = CheckSource(source);
        var roots = FullRoots(destRoots);
        CheckNotAnOffload(source);
        CheckDestinations(source, roots);
        var scan = ScanSource(source);
        CheckFilesFit(roots, scan.Files, scan.TotalBytes);

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

    /// An append (spec 2.2): the same checks as Prepare (options, source, destinations, Windows names, FAT32, space),
    /// but over the new files only, into the card folders of the earlier backup. `NothingNew` when there are none.
    public OffloadPlan PrepareAppend(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default) =>
        PrepareAppend(PlanAppend(source, cardPaths, ct));

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
    public OffloadPlan PrepareAppend(AppendPlan a)
    {
        if (a.NewFiles.Count == 0) throw OffloadError.Preflight(PreflightCode.NothingNew, Msg.NothingNew);
        var scan = new SourceScan { Root = a.FullScan.Root, Files = a.NewFiles, Dirs = a.NewDirs, Skipped = a.FullScan.Skipped };
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0) throw OffloadError.Preflight(PreflightCode.BadNames, Msg.BadNames(problems));
        CheckFilesFit(a.DestRoots, scan.Files, scan.TotalBytes);
        return new OffloadPlan
        {
            Source = a.Source, Scan = scan, Date = a.Date, CardName = a.CardName, DestRoots = a.DestRoots,
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
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

    /// The FAT32 limit and free space on every destination for `files` (all of them, or only the new ones of an append).
    void CheckFilesFit(IReadOnlyList<string> roots, IReadOnlyList<SourceFile> files, long totalBytes)
    {
        long needed = totalBytes + files.Count * 64L * 1024 + (64L << 20);
        foreach (var root in roots)
        {
            if (Fat32Problem(NativeIO.FilesystemType(root), files) is string fat)
                throw OffloadError.Preflight(PreflightCode.Fat32, $"{root}: {fat}");
            long free = NativeIO.AvailableBytes(root) ?? 0;
            if (free < needed) throw OffloadError.Preflight(PreflightCode.NoSpace, Msg.NoSpace(root, needed, free));
        }
    }

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
                Interlocked.Read(ref readBytes), writers.Select(w => w.Written).ToArray(), writers.Select(w => w.Verified).ToArray(), errorCount);
        }
    }

    void SetPhase(CopyPhase p)
    {
        lock (gate) phase = p;
    }
}
