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

    public OffloadOptions Options { get; }

    public OffloadEngine(OffloadOptions? options = null) => Options = options ?? new OffloadOptions();

    public void Cancel() => cts.Cancel();

    public bool IsCancelled => cts.IsCancellationRequested;

    /// Scans the card and checks the destinations before a single byte is written.
    public OffloadPlan Prepare(string source, IReadOnlyList<string> destRoots, string? cardName, string? date)
    {
        if (!Directory.Exists(source)) throw OffloadError.Preflight($"Source not found: {source}");
        source = Path.GetFullPath(source);
        var roots = destRoots.Select(Path.GetFullPath).ToList();
        if (roots.Count == 0) throw OffloadError.Preflight("No destination given");
        var sourceVolume = NativeIO.VolumeId(source) ?? throw OffloadError.Preflight($"Cannot stat source {source}");

        var volumes = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) throw OffloadError.Preflight($"Destination not found: {root}");
            if (!IsWritable(root)) throw OffloadError.Preflight($"Destination not writable: {root}");
            var volume = NativeIO.VolumeId(root) ?? throw OffloadError.Preflight($"Cannot stat destination {root}");
            if (volume == sourceVolume && !Options.AllowSourceVolume)
                throw OffloadError.Preflight($"Destination {root} is on the same volume as the source");
            if (volumes.Contains(volume) && !Options.AllowSameDevice)
                throw OffloadError.Preflight($"Destinations share a volume ({root}) — that is not a second copy. Use --allow-same-device to override.");
            volumes.Add(volume);
        }

        var scan = Scanner.Scan(source);
        if (scan.Files.Count == 0) throw OffloadError.Preflight("Source contains no files");
        var problems = Scanner.WindowsProblems(scan);
        if (problems.Count > 0)
        {
            var more = problems.Count > 20 ? $"\n  … and {problems.Count - 20} more" : "";
            throw OffloadError.Preflight("Names Windows cannot store:\n  " + string.Join("\n  ", problems.Take(20)) + more);
        }

        long needed = scan.TotalBytes + scan.Files.Count * 64L * 1024 + (64L << 20);
        foreach (var root in roots)
        {
            if (Fat32Problem(NativeIO.FilesystemType(root), scan.Files) is string fat)
                throw OffloadError.Preflight($"{root}: {fat}");
            long free = NativeIO.AvailableBytes(root) ?? 0;
            if (free < needed)
                throw OffloadError.Preflight($"Not enough space on {root}: need {Format.Bytes(needed)}, free {Format.Bytes(free)}");
        }

        var day = date ?? Layout.Today();
        var name = Layout.UniqueCardFolder(roots, day, cardName != null ? Layout.Sanitize(cardName) : Layout.DefaultCardName(source));
        return new OffloadPlan { Source = source, Scan = scan, Date = day, CardName = name, DestRoots = roots };
    }

    /// FAT32 cannot hold files of 4 GiB or more — say so before the copy, not at 99 %.
    internal static string? Fat32Problem(string filesystem, IEnumerable<SourceFile> files)
    {
        if (!filesystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)) return null;
        var big = files.FirstOrDefault(f => f.Size > uint.MaxValue);
        return big == null ? null : $"{filesystem} cannot store files of 4 GB or more ({big.RelPath}, {Format.Bytes(big.Size)})";
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
