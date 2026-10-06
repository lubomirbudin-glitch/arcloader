using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core.Verification;

public sealed record VerifyProgress(int FileIndex, int FileCount, long Bytes, long TotalBytes, string CurrentFile);

public sealed record UnreadableFile(string Path, string Error);

/// The outcome of re-verifying one card folder (spec 2.4). Paths are relative with "/".
public sealed class VerifyResult
{
    public required string CardPath { get; init; }
    public DateTime Started { get; init; }
    public DateTime Finished { get; set; }
    /// "ascmhl" (every generation of ascmhl/) or "hashList" (only `_REPORT\<card>.xxh64` / `.md5`).
    public required string Basis { get; init; }
    public int Generations { get; init; }
    public IReadOnlyList<string> HashAlgorithms { get; init; } = [];
    public int TotalFiles { get; init; }
    public long TotalBytes { get; init; }
    public int Ok { get; set; }
    /// Bytes read and hashed so far.
    public long BytesRead { get; set; }
    /// Files looked at (a cancelled run says "po 120 / 1 234 souborech").
    public int Checked { get; set; }
    public List<string> Changed { get; } = [];
    public List<string> Missing { get; } = [];
    public List<UnreadableFile> Unreadable { get; } = [];
    public List<string> Extra { get; } = [];
    /// Failed at the copy and no earlier good record exists: there is nothing to verify the file against.
    public List<string> FailedAtCopy { get; } = [];
    /// The newest record failed at the copy but an older one did not: the file was verified against that one.
    public List<string> FailedSuperseded { get; } = [];
    public List<string> ChainProblems { get; } = [];
    public bool Cancelled { get; set; }
    /// The card folder itself vanished during the run (a disconnected disk).
    public bool FolderGone { get; set; }

    /// False when the MHL history does not ignore _REPORT: a report inside the card folder would break `ascmhl verify`,
    /// so VerifyReport.Write puts it into the day-level _REPORT beside the card folder.
    public bool ReportInsideCard { get; init; } = true;

    public bool Success => !Cancelled && Changed.Count == 0 && Missing.Count == 0 && Unreadable.Count == 0 && FailedAtCopy.Count == 0;
}

/// Re-reads a card folder on a destination and compares every file with its ASC MHL (all generations, every hash type
/// R9) — or, without ascmhl/, with the hash lists in _REPORT. Writes nothing (R8): the report is a separate step.
public static class BackupVerifier
{
    sealed record Expected(string Path, long? Size, IReadOnlyList<MhlHash> Hashes, bool Failed, bool FailedNewer = false);

    static readonly string[] Ours = [Layout.ReportDirName + "/", Layout.ProxyDirName + "/", Mhl.DirName + "/"];

    /// Every file is read once, without the file cache, through all hashes the record has for it. `ct` cancels between
    /// blocks: the result then has Cancelled = true (nothing is thrown). Throws OffloadError (NoHashRecord) when the
    /// folder has neither ASC MHL nor a hash list.
    public static VerifyResult Verify(string cardPath, Action<VerifyProgress>? progress = null, CancellationToken ct = default, int chunkSize = 16 << 20)
    {
        var started = DateTime.UtcNow;
        var mhl = MhlReader.Read(cardPath);
        List<Expected> expected;
        IReadOnlyList<string> ignore;
        string basis;
        if (mhl is { Generations.Count: > 0 })
        {
            expected = mhl.Files.Values.OrderBy(f => f.Path, StringComparer.Ordinal)
                .Select(f =>
                {
                    // A newest record that failed at the copy: verify against the newest one that did not (a corrupt file
                    // must not pass); with none, there is nothing to compare with.
                    if (f.Failed && mhl.LastGood.TryGetValue(f.Path, out var good))
                        return new Expected(f.Path, good.Size, good.Hashes.Where(h => HashAlgo.IsKnown(h.Algorithm)).ToList(), false, true);
                    return new Expected(f.Path, f.Size, f.Hashes.Where(h => HashAlgo.IsKnown(h.Algorithm)).ToList(), f.Failed);
                }).ToList();
            ignore = mhl.Ignore;
            basis = "ascmhl";
        }
        else
        {
            expected = ReadHashLists(cardPath) ?? throw OffloadError.Preflight(PreflightCode.NoHashRecord,
                string.Join(" ", new[] { Msg.NoHashRecord(cardPath) }.Concat(mhl?.ChainProblems ?? [])));
            ignore = [];
            basis = "hashList";
        }

        long total = expected.Sum(e => e.Size ?? SizeOnDisk(Layout.Join(cardPath, e.Path)));
        var result = new VerifyResult
        {
            CardPath = cardPath,
            Started = started,
            Basis = basis,
            Generations = basis == "ascmhl" ? mhl!.Generations.Count : 0,
            HashAlgorithms = expected.SelectMany(e => e.Hashes.Select(h => h.Algorithm)).Distinct().OrderBy(a => HashAlgo.Names.ToList().IndexOf(a)).ToList(),
            TotalFiles = expected.Count,
            TotalBytes = total,
            // Only an ascmhl history can be broken by a report: _REPORT must be ignored by every generation.
            ReportInsideCard = basis != "ascmhl" || mhl!.Generations.All(g => Ignored(Layout.ReportDirName + "/x", g.Ignore)),
        };
        result.ChainProblems.AddRange(mhl?.ChainProblems ?? []);

        using var buf = new AlignedBuffer(chunkSize);
        for (int i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            if (ct.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }
            result.Checked = i;
            progress?.Invoke(new VerifyProgress(i, expected.Count, result.BytesRead, total, e.Path));
            if (e.Failed)
            {
                result.FailedAtCopy.Add(e.Path);
                continue;
            }
            var path = Layout.Join(cardPath, e.Path);
            if (!File.Exists(path))
            {
                // A card folder that vanished (disk pulled) makes every remaining file unreadable, not missing.
                if (!Directory.Exists(cardPath)) result.Unreadable.Add(new UnreadableFile(e.Path, "složka zálohy zmizela"));
                else result.Missing.Add(e.Path);
                continue;
            }
            if (e.Hashes.Count == 0)
            {
                result.Unreadable.Add(new UnreadableFile(e.Path, "záznam nemá žádný podporovaný kontrolní součet"));
                continue;
            }
            try
            {
                var hashers = e.Hashes.Select(h => HashAlgo.Create(h.Algorithm)).ToList();
                long size = Hashing.HashFile(path, buf, hashers, n =>
                {
                    result.BytesRead += n;
                    progress?.Invoke(new VerifyProgress(i, expected.Count, result.BytesRead, total, e.Path));
                }, ct);
                bool same = e.Size is not long want || want == size;
                for (int h = 0; same && h < hashers.Count; h++)
                    same = HashAlgo.Same(e.Hashes[h].Algorithm, hashers[h].Finish(), e.Hashes[h].Value);
                if (same)
                {
                    result.Ok++;
                    if (e.FailedNewer) result.FailedSuperseded.Add(e.Path);   // only when it checks out against the older good record
                }
                else result.Changed.Add(e.Path);
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                result.Unreadable.Add(new UnreadableFile(e.Path, ex.Message));
            }
        }
        if (!result.Cancelled)
        {
            result.Checked = expected.Count;
            AddExtra(result, cardPath, expected, ignore);
        }
        result.FolderGone = result.Unreadable.Count + result.Missing.Count > 0 && !Directory.Exists(cardPath);
        result.Finished = DateTime.UtcNow;
        return result;
    }

    static long SizeOnDisk(string path)
    {
        try { return new FileInfo(path) is { Exists: true } f ? f.Length : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// Files on disk that the record does not know: not ours (_REPORT, _PROXY, ascmhl) and not ignored by the MHL.
    static void AddExtra(VerifyResult result, string cardPath, List<Expected> expected, IReadOnlyList<string> ignore)
    {
        var known = expected.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            result.Extra.AddRange(Scanner.Scan(cardPath).Files.Select(f => f.RelPath)
                .Where(p => !known.Contains(p) && !Ours.Any(o => p.StartsWith(o, StringComparison.OrdinalIgnoreCase)) && !Ignored(p, ignore)));
        }
        catch (IOError)
        {
            // Extra files are information only; an unreadable folder already shows as missing or unreadable files.
        }
    }

    /// ASC MHL ignore patterns, gitwildmatch style: a name ("*.tmp", ".DS_Store") matches any path component, "name/" only
    /// a folder; a pattern with "/" inside, or a leading "/", is anchored at the card folder. `*` and `?` never cross a
    /// "/", `**` does. A matching folder ignores everything below it.
    internal static bool Ignored(string relPath, IReadOnlyList<string> patterns)
    {
        var parts = relPath.Split('/');
        foreach (var raw in patterns)
        {
            bool folderOnly = raw.EndsWith('/');
            var pattern = raw.TrimEnd('/');
            if (pattern.Length == 0) continue;
            if (pattern.Contains('/'))
            {
                var rx = Wildcard(pattern.TrimStart('/'));
                var tail = folderOnly ? "/.*" : "(/.*)?";
                if (Regex.IsMatch(relPath, "^" + rx + tail + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
                continue;
            }
            var name = "^" + Wildcard(pattern) + "$";
            var candidates = folderOnly ? parts[..^1] : parts;
            if (candidates.Any(c => Regex.IsMatch(c, name, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) return true;
        }
        return false;
    }

    /// A gitwildmatch glob as a regex body (no anchors).
    static string Wildcard(string glob)
    {
        var rx = new StringBuilder();
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/')
                {
                    i++;
                    rx.Append("(.*/)?");
                }
                else rx.Append(".*");
            }
            else if (c == '*') rx.Append("[^/]*");
            else if (c == '?') rx.Append("[^/]");
            else rx.Append(Regex.Escape(c.ToString()));
        }
        return rx.ToString();
    }

    /// `_REPORT\*.xxh64` and `*.md5` ("<hash><two spaces><path>" lines, also with \r\n): one record per path.
    static List<Expected>? ReadHashLists(string cardPath)
    {
        var dir = Path.Combine(cardPath, Layout.ReportDirName);
        if (!Directory.Exists(dir)) return null;
        var byPath = new Dictionary<string, List<MhlHash>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var (pattern, algo) in new[] { ("*.xxh64", "xxh64"), ("*.md5", "md5") })
        {
            if (Directory.GetFiles(dir, pattern).Order(StringComparer.Ordinal).FirstOrDefault() is not string list) continue;
            foreach (var raw in File.ReadAllText(list, Encoding.UTF8).Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                int sep = line.IndexOf("  ", StringComparison.Ordinal);
                if (line.Length == 0 || sep < 0) continue;
                var rel = line[(sep + 2)..];
                if (!byPath.TryGetValue(rel, out var hashes))
                {
                    byPath[rel] = hashes = [];
                    order.Add(rel);
                }
                hashes.Add(new MhlHash(algo, line[..sep], "original"));
            }
        }
        return order.Count == 0 ? null : order.Select(p => new Expected(p, null, byPath[p], false)).ToList();
    }
}

/// Which card folders a chosen folder means (spec 2.4): a card folder (ascmhl\, or a report or hash list in _REPORT\)
/// is itself; a day folder means every card folder in it (its own _REPORT holds only the day report, plan 7).
public static class VerifyTargets
{
    static bool IsCard(string dir)
    {
        if (Directory.Exists(Path.Combine(dir, Mhl.DirName))) return true;
        var report = Path.Combine(dir, Layout.ReportDirName);
        return File.Exists(Path.Combine(report, "offload_report.json"))
               || (Directory.Exists(report) && Directory.EnumerateFiles(report, "*.xxh64").Any());
    }

    public static IReadOnlyList<string> Resolve(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            if (IsCard(folder)) return [folder];
            return Directory.GetDirectories(folder)
                .Where(d => !string.Equals(Path.GetFileName(d), Layout.ReportDirName, StringComparison.OrdinalIgnoreCase) && IsCard(d))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
