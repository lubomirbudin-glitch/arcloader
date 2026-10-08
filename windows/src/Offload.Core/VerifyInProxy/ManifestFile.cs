using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.VerifyInProxy;

/// One line of `<card>\_REPORT\offload-manifest.jsonl` (spec 4.5, R4). `file`: a file in its place on this destination,
/// with the card's hashes and the XXH3-64 of every block. `part`: blocks `first` … of a file still being written under its
/// `.offload-part` name — on the disk, written through — the checkpoint a later append resumes from (R6). Generation
/// `gen` is the ASC MHL generation the copy wrote. The Mac ignores the file (FORMATY).
public sealed class ManifestRecord
{
    public const string FileKind = "file";
    public const string PartKind = "part";

    [JsonPropertyName("v")] public int Version { get; init; } = 1;
    [JsonPropertyName("kind")] public string Kind { get; init; } = FileKind;
    [JsonPropertyName("gen")] public int Generation { get; init; } = 1;
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("modified")] public DateTime Modified { get; init; }
    [JsonPropertyName("xxh64"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Xxh64 { get; init; }
    [JsonPropertyName("md5"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Md5 { get; init; }
    [JsonPropertyName("xxh128"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Xxh128 { get; init; }
    [JsonPropertyName("blockSize")] public int BlockSize { get; init; }
    /// `part` only: the index of the first block in `blocks`.
    [JsonPropertyName("first"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int First { get; init; }
    /// XXH3-64 of each block, 16 lower-case hex digits.
    [JsonPropertyName("blocks")] public IReadOnlyList<string> Blocks { get; init; } = [];

    public static IReadOnlyList<string> Hex(IEnumerable<ulong> blocks) => blocks.Select(Format.Hex64).ToList();

    /// The blocks as numbers; null when one is not 16 hex digits.
    public ulong[]? BlockValues()
    {
        var values = new ulong[Blocks.Count];
        for (int i = 0; i < values.Length; i++)
            if (Blocks[i] is not { Length: 16 } h || !ulong.TryParse(h, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }

    /// A `file` record as what the destination's copy is checked against; null when it does not hold together.
    public BlockManifest? ToManifest()
    {
        if (Kind != FileKind || BlockSize <= 0 || BlockValues() is not { } values || values.Length != BlockManifest.CountFor(Size, BlockSize)) return null;
        return new BlockManifest(Size, BlockSize, values);
    }

    /// The hashes the card gave, as the copy records them; null when xxh64 is not readable.
    public FileHashes? Hashes()
    {
        if (Xxh64 is not { Length: 16 } x || !ulong.TryParse(x, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var xx)) return null;
        byte[]? md5 = null;
        if (Md5 != null)
        {
            try { md5 = Convert.FromHexString(Md5); }
            catch (FormatException) { return null; }
        }
        return new FileHashes(xx, md5, Xxh128);
    }
}

/// A checkpoint put together from a file's `part` records: the first `Blocks.Length` blocks are on the disk.
public sealed record PartCheckpoint(string Path, long Size, DateTime Modified, int BlockSize, ulong[] Blocks);

/// `<card>\_REPORT\offload-manifest.jsonl` — only ever appended to: a line is written through and flushed, nothing in the
/// file is rewritten (plan 6–9 rule "nikdy nepřepsat"; the manifest is an append-only file of its own, FORMATY). A line
/// left half-written by a crash is skipped when read, and the next append starts on a new line.
public static class ManifestFile
{
    public const string FileName = "offload-manifest.jsonl";

    static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new OffloadReport.IsoSecondsDateConverter() },
    };

    static readonly Dictionary<string, Lock> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static string PathIn(string cardPath) => System.IO.Path.Combine(cardPath, Layout.ReportDirName, FileName);

    static Lock LockFor(string path)
    {
        lock (Locks)
        {
            if (!Locks.TryGetValue(path, out var l)) Locks[path] = l = new Lock();
            return l;
        }
    }

    /// Appends one record (the recorder and a writer's checkpoints share the file: one at a time). IOError on failure.
    public static void Append(string cardPath, ManifestRecord record) => Append(cardPath, [record]);

    /// Appends records, one line each, with one flush (many small files finish together).
    public static void Append(string cardPath, IReadOnlyList<ManifestRecord> records)
    {
        if (records.Count == 0) return;
        var path = PathIn(cardPath);
        var line = Encoding.UTF8.GetBytes(string.Concat(records.Select(r => JsonSerializer.Serialize(r, Json) + "\n")));
        lock (LockFor(System.IO.Path.GetFullPath(path)))
        {
            try
            {
                using var f = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.WriteThrough);
                if (f.Length > 0)
                {
                    f.Seek(-1, SeekOrigin.End);
                    if (f.ReadByte() != '\n') f.WriteByte((byte)'\n');   // a torn last line stays alone on its line
                }
                f.Seek(0, SeekOrigin.End);
                f.Write(line);
                f.Flush(flushToDisk: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new IOError("write", path, e);
            }
        }
    }

    /// Every record that can be read, in the order written; none when there is no manifest.
    public static IReadOnlyList<ManifestRecord> Read(string cardPath)
    {
        var path = PathIn(cardPath);
        if (!File.Exists(path)) return [];
        string text;
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var r = new StreamReader(s, Encoding.UTF8))
            text = r.ReadToEnd();
        var records = new List<ManifestRecord>();
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length == 0) continue;
            try
            {
                if (JsonSerializer.Deserialize<ManifestRecord>(line, Json) is { Version: 1, Path.Length: > 0 } r) records.Add(r);
            }
            catch (JsonException) { /* torn by a crash, or not ours */ }
        }
        return records;
    }

    /// The newest `file` record of every path of generation `generation` (paths compared in any case).
    public static IReadOnlyDictionary<string, ManifestRecord> Files(IEnumerable<ManifestRecord> records, int generation)
    {
        var files = new Dictionary<string, ManifestRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
            if (r.Kind == ManifestRecord.FileKind && r.Generation == generation) files[r.Path] = r;
        return files;
    }

    /// The checkpoint of every path of generation `generation` from its `part` records: a record with `first` 0 starts a
    /// new attempt, one that continues the blocks so far extends it, anything else (a gap) is ignored. A path whose
    /// size, time or block size changes between records starts again with the record that changed it.
    public static IReadOnlyDictionary<string, PartCheckpoint> Parts(IEnumerable<ManifestRecord> records, int generation)
    {
        var parts = new Dictionary<string, PartCheckpoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (r.Kind != ManifestRecord.PartKind || r.Generation != generation || r.BlockSize <= 0 || r.BlockValues() is not { } values) continue;
            parts.TryGetValue(r.Path, out var had);
            bool same = had != null && had.Size == r.Size && had.Modified == r.Modified && had.BlockSize == r.BlockSize;
            if (r.First == 0) parts[r.Path] = new PartCheckpoint(r.Path, r.Size, r.Modified, r.BlockSize, values);
            else if (same && r.First == had!.Blocks.Length) parts[r.Path] = had with { Blocks = [.. had.Blocks, .. values] };
        }
        return parts;
    }
}
