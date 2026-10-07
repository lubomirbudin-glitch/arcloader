using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Offload.Core;

public sealed class ReportDestination
{
    [JsonPropertyName("root")] public required string Root { get; init; }
    [JsonPropertyName("cardPath")] public required string CardPath { get; init; }
    [JsonPropertyName("filesystem")] public required string Filesystem { get; init; }
    [JsonPropertyName("filesOK")] public int FilesOK { get; init; }
    [JsonPropertyName("filesFailed")] public int FilesFailed { get; init; }
    /// Present (false) only when the volume refused unbuffered writes and the file cache was used.
    [JsonPropertyName("unbuffered")] public bool? Unbuffered { get; init; }
}

public sealed class ReportFile
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("modified")] public DateTime Modified { get; init; }
    [JsonPropertyName("xxh64")] public string? Xxh64 { get; init; }
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; init; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; init; }
}

public sealed partial class OffloadReport
{
    [JsonPropertyName("tool")] public string Tool { get; init; } = "offload";
    [JsonPropertyName("version")] public string Version { get; init; } = OffloadVersion.String;
    [JsonPropertyName("hashAlgorithm")] public string HashAlgorithm { get; init; } = "xxh64";
    [JsonPropertyName("cardName")] public required string CardName { get; init; }
    [JsonPropertyName("date")] public required string Date { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("sourceFilesystem")] public required string SourceFilesystem { get; init; }
    [JsonPropertyName("started")] public DateTime Started { get; init; }
    [JsonPropertyName("copyFinished")] public DateTime CopyFinished { get; init; }
    [JsonPropertyName("finished")] public DateTime Finished { get; init; }
    [JsonPropertyName("verified")] public bool Verified { get; init; }
    [JsonPropertyName("totalFiles")] public int TotalFiles { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<ReportDestination> Destinations { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyList<ReportFile> Files { get; init; }
    [JsonPropertyName("skipped")] public required IReadOnlyList<string> Skipped { get; init; }
    [JsonPropertyName("clips")] public List<ClipEntry> Clips { get; set; } = [];
    [JsonPropertyName("proxy")] public ProxySettings? Proxy { get; set; }
    /// Windows only: the ffmpeg encoder the proxies were rendered with (e.g. "h264_nvenc"); omitted when none were rendered.
    [JsonPropertyName("proxyEncoder")] public string? ProxyEncoder { get; set; }
    /// The ASC MHL generation this report belongs to: absent (= 1) for the full copy, 2, 3 … for appends (spec 2.2).
    [JsonPropertyName("generation")] public int? Generation { get; set; }

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles => Files.Where(IsFailed).ToList();

    /// The card could not be read, or a destination does not hold a good copy. A cancelled verification leaves files
    /// "written" or already "verified": both are fine then.
    public bool IsFailed(ReportFile f) =>
        f.SourceError != null || f.Destinations.Any(d => !(d == "verified" || (!Verified && d == "written")));

    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was read back with a matching hash.
    [JsonIgnore] public bool SafeToEject => Success && Verified;

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<FileHashes> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished)
    {
        bool IsOk(FileState s) => s == FileState.Verified || (!verified && s == FileState.Written);
        var files = plan.Scan.Files.Select((f, i) => new ReportFile
        {
            Path = f.RelPath,
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i].Xxh64Hex,
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
        }).ToList();
        var cardPaths = plan.CardPaths;
        var dests = plan.DestRoots.Select((root, d) => new ReportDestination
        {
            Root = root,
            CardPath = cardPaths[d],
            Filesystem = NativeIO.FilesystemType(root),
            FilesOK = destResults[d].Count(r => IsOk(r.State)),
            FilesFailed = destResults[d].Count(r => !IsOk(r.State)),
            Unbuffered = unbuffered[d] ? null : false,
        }).ToList();
        return new OffloadReport
        {
            CardName = plan.CardName,
            Date = plan.Date,
            Source = plan.Source,
            SourceFilesystem = NativeIO.FilesystemType(plan.Source),
            Started = started,
            CopyFinished = copyFinished,
            Finished = finished,
            Verified = verified,
            TotalFiles = files.Count,
            TotalBytes = plan.Scan.TotalBytes,
            Destinations = dests,
            Files = files,
            Skipped = plan.Scan.Skipped,
            Generation = plan.Generation > 1 ? plan.Generation : null,
        };
    }

    /// Report file names of generation `generation`: the full copy keeps the plain names, an append adds "_NNNN" (R6).
    public static string JsonName(int generation) => generation <= 1 ? "offload_report.json" : $"offload_report_{generation:0000}.json";
    public static string TextName(int generation) => generation <= 1 ? "offload_report.txt" : $"offload_report_{generation:0000}.txt";
    public static string HtmlName(string cardName, int generation) => generation <= 1 ? $"{cardName}_report.html" : $"{cardName}_report_{generation:0000}.html";
    public static string PdfName(string cardName, int generation) => generation <= 1 ? $"{cardName}_report.pdf" : $"{cardName}_report_{generation:0000}.pdf";

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
    [JsonIgnore]
    public string HashList => string.Join("\n", Files.Where(f => f.Xxh64 != null).Select(f => $"{f.Xxh64}  {f.Path}")) + "\n";

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var inv = CultureInfo.InvariantCulture;
            var s = new StringBuilder();
            s.Append($"OFFLOAD REPORT — {(Success ? "OK" : "FAILED")}\n");
            s.Append($"Card:        {CardName}\n");
            s.Append($"Source:      {Source} [{SourceFilesystem}]\n");
            s.Append($"Started:     {LocalIso(Started)}\n");
            s.Append($"Finished:    {LocalIso(Finished)}\n");
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : "NOT VERIFIED") + "\n");
            s.Append($"Hash:        {HashAlgorithm}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}\n");
            if (Skipped.Count > 0) s.Append($"\nSkipped (system files): {string.Join(", ", Skipped)}\n");
            var failed = FailedFiles;
            if (failed.Count > 0)
            {
                s.Append($"\nFAILED FILES ({failed.Count}):\n");
                foreach (var f in failed)
                {
                    s.Append($"  {f.Path}\n");
                    if (f.SourceError != null) s.Append($"    source: {f.SourceError}\n");
                    for (int d = 0; d < f.Errors.Count; d++)
                        if (f.Errors[d] is string e) s.Append($"    dest {d + 1}: {e}\n");
                }
            }
            return s.ToString();
        }
    }

    static string LocalIso(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new IsoSecondsDateConverter() },
    };

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// Pretty JSON with sorted keys and second-precision UTC dates — the shape the Mac version writes.
    public string ToJson() => Sorted(JsonSerializer.SerializeToNode(this, SerializeOptions))!.ToJsonString(WriteOptions);

    public static OffloadReport FromJson(string json) => JsonSerializer.Deserialize<OffloadReport>(json, SerializeOptions)!;

    /// Writes all report files into `<card>/_REPORT`. The hash list and MHL are the record of the copy itself,
    /// so they are written once (`includeHashRecords`); the HTML/JSON are refreshed as clips get analysed.
    /// An append (Generation > 1) writes its own `_NNNN` files — created, never replacing one (ruling I1; only the
    /// job's own refresh later replaces them) — and adds its lines to the hash list; the reports of the earlier
    /// generations are never rewritten (R5, R6).
    ///
    /// Order (fix round 1, item 6) — no single failure may leave a folder that ruling I1 refuses for the wrong reason:
    ///   1. the `_NNNN` reports, created (one that is there already stops everything before a shared file is touched);
    ///   2. the MHL generation file, created, then the chain, replaced — the chain is the commit point: reports and
    ///      MHL now agree, and a failure before it leaves reports without their generation, which I1 rightly refuses
    ///      (those files have no ASC MHL record yet);
    ///   3. the hash list last: it is derived from the MHL (spec 2.2), so a failure there (a reader holding it without
    ///      delete sharing, a full disk) leaves reports and chain in agreement — the next append goes ahead and adds the
    ///      lines that were missed (any MHL-recorded path not in the list).
    /// The hash list and the chain are swapped with ReplaceFile (NativeIO.WriteFileReplacing), which works while a
    /// reader holds them with delete sharing.
    public void Write(string cardPath, bool includeHashRecords)
    {
        int generation = Generation ?? 1;
        var dir = System.IO.Path.Combine(cardPath, Layout.ReportDirName);
        NativeIO.Mkdirs(dir);
        var outputs = new List<(string Name, string Text)>
        {
            (JsonName(generation), ToJson()),
            (TextName(generation), Summary),
            (HtmlName(CardName, generation), HtmlReport.Render(this)),
        };
        bool create = generation > 1 && includeHashRecords;
        foreach (var (name, text) in outputs)
        {
            var path = System.IO.Path.Combine(dir, name);
            if (create) NativeIO.WriteFileNew(path, Encoding.UTF8.GetBytes(text));
            else NativeIO.WriteFileDurably(path, Encoding.UTF8.GetBytes(text));
        }
        if (!includeHashRecords) return;
        Mhl.Write(this, cardPath);
        // An append: the earlier lines byte for byte, then the missed ones, then this generation's.
        var hashList = System.IO.Path.Combine(dir, $"{CardName}.xxh64");
        var lines = Encoding.UTF8.GetBytes(HashList);
        if (generation == 1)
        {
            NativeIO.WriteFileReplacing(hashList, lines);
            return;
        }
        var (earlier, missed) = HashListAndMissed(hashList, cardPath, Files.Select(f => f.Path));
        NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed, .. lines]);
    }

    /// `<card>/_REPORT/<card name>.xxh64` gets the lines of every path the ASC MHL history records but the list lacks
    /// (a hash list whose write failed, step 3 of Write) — the one record file that may be rewritten. Used by an append
    /// with nothing new (fix round 2, item 6b); an append with new files does the same in Write. False: nothing missed.
    public static bool HealHashList(string cardPath, string cardName)
    {
        var hashList = System.IO.Path.Combine(cardPath, Layout.ReportDirName, $"{cardName}.xxh64");
        var (earlier, missed) = HashListAndMissed(hashList, cardPath, []);
        if (missed.Length == 0) return false;
        NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed]);
        return true;
    }

    /// The card's hash list as the earlier generations left it (exact bytes, then a line break if it had none), and
    /// the xxh64 lines of every path the ASC MHL history records but neither the list nor `alsoListed` has.
    static (byte[] Earlier, byte[] Missed) HashListAndMissed(string path, string cardPath, IEnumerable<string> alsoListed)
    {
        var earlier = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (earlier.Length > 0 && earlier[^1] != (byte)'\n') earlier = [.. earlier, (byte)'\n'];
        var listed = new HashSet<string>(alsoListed, StringComparer.OrdinalIgnoreCase);
        foreach (var line in Encoding.UTF8.GetString(earlier).Split('\n'))
            if (line.TrimEnd('\r').IndexOf("  ", StringComparison.Ordinal) is int at and >= 0) listed.Add(line.TrimEnd('\r')[(at + 2)..]);
        var missed = new StringBuilder();
        if (Verification.MhlReader.Read(cardPath) is { } mhl)
            foreach (var r in mhl.LastGood.Values.OrderBy(r => r.Path, StringComparer.Ordinal))
                if (!listed.Contains(r.Path) && r.Hashes.FirstOrDefault(h => h.Algorithm == "xxh64") is { } h)
                    missed.Append($"{h.Value}  {r.Path}\n");
        return (earlier, Encoding.UTF8.GetBytes(missed.ToString()));
    }

    static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        _ => node?.DeepClone(),
    };

    internal sealed class IsoSecondsDateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}
