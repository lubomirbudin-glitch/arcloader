using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Offload.Core.Verification;

/// One hash of a file in an ASC MHL generation; `Action`: "original", "verified", "failed" …
public sealed record MhlHash(string Algorithm, string Value, string? Action);

public sealed record MhlFileRecord(string Path, long? Size, DateTime? Modified, IReadOnlyList<MhlHash> Hashes, int Generation, string? PreviousPath = null)
{
    /// The copy recorded this file as failed (action="failed").
    public bool Failed => Hashes.Any(h => h.Action == "failed");
}

public sealed record MhlGeneration(int Seq, string FileName, IReadOnlyList<string> Ignore, IReadOnlyList<MhlFileRecord> Files);

/// One `<hashlist>` of ascmhl_chain.xml.
public sealed record MhlChainEntry(int Seq, string Path, string C4);

/// Everything ascmhl/ of a card folder says (spec 2.4). `Files`: per path the newest generation that lists it.
public sealed class MhlHistory
{
    public required IReadOnlyList<MhlGeneration> Generations { get; init; }
    public required IReadOnlyDictionary<string, MhlFileRecord> Files { get; init; }
    /// Per path the newest record that is not `failed` — what a file whose newest record failed is verified against.
    public required IReadOnlyDictionary<string, MhlFileRecord> LastGood { get; init; }
    /// The ignore patterns of every generation.
    public required IReadOnlyList<string> Ignore { get; init; }
    /// The chain as read, in order (an append writes it back with one more entry); empty when there is none.
    public required IReadOnlyList<MhlChainEntry> Chain { get; init; }
    /// Czech texts for the verify report: a changed or missing generation, a missing chain.
    public required IReadOnlyList<string> ChainProblems { get; init; }
}

public static partial class MhlReader
{
    public const string ChainFileName = "ascmhl_chain.xml";
    static readonly XNamespace Ns = "urn:ASC:MHL:v2.0";
    static readonly XNamespace DirNs = "urn:ASC:MHL:DIRECTORY:v2.0";

    [GeneratedRegex(@"^(\d+)_")]
    private static partial Regex LeadingNumber();

    static string Gen(int seq) => seq.ToString("0000", CultureInfo.InvariantCulture);

    /// Tests only: called with the path while a file is open for reading (this thread only).
    [ThreadStatic] internal static Action<string>? WhileOpen;

    /// Every file of ascmhl/ is opened with ReadWrite | Delete sharing: a verify or a planner reading the chain must
    /// never stop an append from swapping it (ReplaceFile; fix round 2, item 6a).
    static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    static byte[] ReadShared(string path)
    {
        using var s = OpenShared(path);
        WhileOpen?.Invoke(path);
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    static XDocument LoadShared(string path)
    {
        using var s = OpenShared(path);
        WhileOpen?.Invoke(path);
        return XDocument.Load(s);
    }

    /// Null when the card folder has no ascmhl/ or no generation in it (then a hash list is the only record).
    public static MhlHistory? Read(string cardPath)
    {
        var dir = Path.Combine(cardPath, Mhl.DirName);
        if (!Directory.Exists(dir)) return null;
        var mhlFiles = Directory.GetFiles(dir, "*.mhl").Select(f => Path.GetFileName(f)).ToList();
        var problems = new List<string>();
        var chain = ReadChain(dir, problems);
        var order = new List<(int Seq, string File)>();
        if (chain != null)
        {
            foreach (var c in chain)
            {
                var path = Path.Combine(dir, c.Path);
                if (c.Path.IndexOfAny(['/', '\\']) >= 0 || !File.Exists(path))
                {
                    problems.Add($"The ASC MHL chain has generation {Gen(c.Seq)}, but its file is missing: {c.Path}");
                    continue;
                }
                if (Mhl.C4(ReadShared(path)) != c.C4)
                    problems.Add($"ASC MHL generation {Gen(c.Seq)} does not match the chain (the MHL file was changed).");
                order.Add((c.Seq, c.Path));
            }
            foreach (var extra in mhlFiles.Where(f => !chain.Any(c => string.Equals(c.Path, f, StringComparison.OrdinalIgnoreCase))))
                problems.Add($"An ASC MHL file is not in the chain: {extra}");
        }
        else if (mhlFiles.Count > 0)
        {
            problems.Add($"{ChainFileName} is missing — the generations are read by the number in the file name.");
            order.AddRange(mhlFiles
                .Select(f => (Seq: LeadingNumber().Match(f) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : int.MaxValue, File: f))
                .OrderBy(x => x.Seq).ThenBy(x => x.File, StringComparer.Ordinal));
        }

        var generations = new List<MhlGeneration>();
        foreach (var (seq, file) in order)
        {
            try
            {
                generations.Add(ParseGeneration(Path.Combine(dir, file), seq));
            }
            catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException or FormatException)
            {
                problems.Add($"Cannot read ASC MHL generation {Gen(seq)}: {e.Message}");
            }
        }
        if (generations.Count == 0 && problems.Count == 0) return null;

        // Case-insensitive keys (the disk is, and Extra compares that way). A later generation wins; a path a later
        // generation lists as previousPath was renamed, so its old record no longer describes a file.
        var files = new Dictionary<string, MhlFileRecord>(StringComparer.OrdinalIgnoreCase);
        var lastGood = new Dictionary<string, MhlFileRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in generations)
            foreach (var f in g.Files)
            {
                if (f.PreviousPath is { Length: > 0 } prev && !string.Equals(prev, f.Path, StringComparison.OrdinalIgnoreCase))
                {
                    files.Remove(prev);
                    lastGood.Remove(prev);
                }
                files[f.Path] = f;
                if (!f.Failed) lastGood[f.Path] = f;
            }
        return new MhlHistory
        {
            Generations = generations,
            Files = files,
            LastGood = lastGood,
            Ignore = generations.SelectMany(g => g.Ignore).Distinct(StringComparer.Ordinal).ToList(),
            Chain = chain ?? [],
            ChainProblems = problems,
        };
    }

    /// ascmhl_chain.xml in `ascmhlDir`, or null when it is missing; a broken one is a problem and null.
    public static IReadOnlyList<MhlChainEntry>? ReadChain(string ascmhlDir, List<string>? problems = null)
    {
        var path = Path.Combine(ascmhlDir, ChainFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var doc = LoadShared(path);
            return doc.Descendants(DirNs + "hashlist")
                .Select(h => new MhlChainEntry(
                    int.Parse((string?)h.Attribute("sequencenr") ?? "0", CultureInfo.InvariantCulture),
                    ((string?)h.Element(DirNs + "path") ?? "").Trim(),
                    ((string?)h.Element(DirNs + "c4") ?? "").Trim()))
                .OrderBy(e => e.Seq)
                .ToList();
        }
        catch (Exception e) when (e is XmlException or IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            problems?.Add($"Cannot read {ChainFileName}: {e.Message}");
            return null;
        }
    }

    internal static MhlGeneration ParseGeneration(string file, int seq)
    {
        var doc = LoadShared(file);
        var ignore = doc.Descendants(Ns + "ignore").Elements(Ns + "pattern").Select(p => p.Value.Trim()).Where(p => p.Length > 0).ToList();
        var files = new List<MhlFileRecord>();
        foreach (var h in doc.Descendants(Ns + "hashes").Elements(Ns + "hash"))
        {
            if (h.Element(Ns + "path") is not { } p) continue;
            long? size = long.TryParse((string?)p.Attribute("size"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;
            DateTime? modified = DateTime.TryParse((string?)p.Attribute("lastmodificationdate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var m) ? m : null;
            var hashes = h.Elements()
                .Where(e => e.Name != Ns + "path" && e.Name != Ns + "previousPath")
                .Select(e => new MhlHash(e.Name.LocalName, e.Value.Trim(), (string?)e.Attribute("action")))
                .ToList();
            var previous = ((string?)h.Element(Ns + "previousPath"))?.Trim().Replace('\\', '/');
            files.Add(new MhlFileRecord(p.Value.Trim().Replace('\\', '/'), size, modified, hashes, seq, previous));
        }
        return new MhlGeneration(seq, Path.GetFileName(file), ignore, files);
    }
}
