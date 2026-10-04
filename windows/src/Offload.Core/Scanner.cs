namespace Offload.Core;

public sealed record SourceFile(string RelPath, long Size, DateTime ModifiedUtc);

public sealed class SourceScan
{
    public required string Root { get; init; }
    /// Relative paths with "/", sorted ordinally.
    public required IReadOnlyList<SourceFile> Files { get; init; }
    public required IReadOnlyList<string> Dirs { get; init; }
    /// System housekeeping, links and other non-regular entries that are not copied.
    public required IReadOnlyList<string> Skipped { get; init; }
    public long TotalBytes => Files.Sum(f => f.Size);
}

public static class Scanner
{
    /// Housekeeping folders of macOS and Windows that are not camera data.
    static readonly HashSet<string> JunkDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".Spotlight-V100", ".fseventsd", ".Trashes", ".TemporaryItems", ".DocumentRevisions-V100",
        "System Volume Information", "$RECYCLE.BIN",
    };

    static bool IsJunkFile(string name) =>
        name == ".DS_Store" || name.StartsWith("._", StringComparison.Ordinal)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    public static SourceScan Scan(string root)
    {
        var files = new List<SourceFile>();
        var dirs = new List<string>();
        var skipped = new List<string>();

        void Walk(string dir, string prefix)
        {
            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", OneLevel).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOError("scan", dir, e); }
            foreach (var e in entries)
            {
                var rel = prefix.Length == 0 ? e.Name : prefix + "/" + e.Name;
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skipped.Add(rel);
                }
                else if (e is DirectoryInfo d)
                {
                    if (JunkDirs.Contains(e.Name)) { skipped.Add(rel); continue; }
                    dirs.Add(rel);
                    Walk(d.FullName, rel);
                }
                else if (e is FileInfo f)
                {
                    if (IsJunkFile(e.Name)) skipped.Add(rel);
                    else files.Add(new SourceFile(rel, f.Length, f.LastWriteTimeUtc));
                }
            }
        }

        Walk(Path.GetFullPath(root), "");
        files.Sort((a, b) => string.CompareOrdinal(a.RelPath, b.RelPath));
        dirs.Sort(StringComparer.Ordinal);
        return new SourceScan { Root = root, Files = files, Dirs = dirs, Skipped = skipped };
    }

    /// Paths NTFS / exFAT destinations cannot hold as they are: names that differ only in case
    /// (they would overwrite each other) and names Windows forbids.
    public static IReadOnlyList<string> WindowsProblems(SourceScan scan)
    {
        var problems = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in scan.Dirs.Concat(scan.Files.Select(f => f.RelPath)))
        {
            if (seen.TryGetValue(p, out var other)) problems.Add($"{p}: same name as {other} except for upper/lower case");
            else seen[p] = p;
            var reason = p.Split('/').Select(Layout.InvalidNameReason).FirstOrDefault(r => r != null);
            if (reason != null) problems.Add($"{p}: {reason}");
        }
        return problems;
    }
}
