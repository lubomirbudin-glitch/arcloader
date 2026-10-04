using System.Text;

namespace Offload.Core;

/// Re-verifies an existing offload against the hash list in its _REPORT folder (also one written by the Mac version).
public static class Verifier
{
    public sealed class Result
    {
        public List<string> Ok { get; } = [];
        public List<string> Mismatched { get; } = [];
        public List<string> Missing { get; } = [];
        public List<string> Unreadable { get; } = [];
        public List<string> Extra { get; } = [];
        public long Bytes { get; set; }
        public bool Success => Mismatched.Count == 0 && Missing.Count == 0 && Unreadable.Count == 0;
    }

    public static Result Verify(string cardPath, int chunkSize = 16 << 20, Action<string, long>? progress = null, CancellationToken ct = default)
    {
        var reportDir = Path.Combine(cardPath, Layout.ReportDirName);
        var list = Directory.Exists(reportDir) ? Directory.GetFiles(reportDir, "*.xxh64").FirstOrDefault() : null;
        if (list == null) throw OffloadError.Preflight($"No .xxh64 hash list in {reportDir}");

        var expected = new List<(string Hash, string Path)>();
        foreach (var raw in File.ReadAllText(list, Encoding.UTF8).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int sep = line.IndexOf("  ", StringComparison.Ordinal);
            if (line.Length == 0 || sep < 0) continue;
            expected.Add((line[..sep], line[(sep + 2)..]));
        }

        var result = new Result();
        using var buf = new AlignedBuffer(chunkSize);
        foreach (var (hash, rel) in expected)
        {
            ct.ThrowIfCancellationRequested();
            var path = Layout.Join(cardPath, rel);
            if (!File.Exists(path))
            {
                result.Missing.Add(rel);
                continue;
            }
            try
            {
                var (h, _) = Hashing.HashFile(path, buf, n =>
                {
                    result.Bytes += n;
                    progress?.Invoke(rel, result.Bytes);
                }, ct);
                (Format.Hex64(h) == hash ? result.Ok : result.Mismatched).Add(rel);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                result.Unreadable.Add($"{rel}: {e.Message}");
            }
        }

        var known = expected.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] ours = [Layout.ReportDirName + "/", Layout.ProxyDirName + "/", Mhl.DirName + "/"];
        try
        {
            result.Extra.AddRange(Scanner.Scan(cardPath).Files.Select(f => f.RelPath)
                .Where(p => !known.Contains(p) && !ours.Any(o => p.StartsWith(o, StringComparison.OrdinalIgnoreCase))));
        }
        catch (IOError)
        {
            // Extra files are informational; an unreadable folder is already reported above.
        }
        return result;
    }
}
