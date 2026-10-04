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

    [JsonIgnore] public double CopySeconds => (CopyFinished - Started).TotalSeconds;
    [JsonIgnore] public double VerifySeconds => (Finished - CopyFinished).TotalSeconds;

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles
    {
        get
        {
            // A cancelled verification leaves files "written" or already "verified": both are fine then.
            bool IsOk(string d) => d == "verified" || (!Verified && d == "written");
            return Files.Where(f => f.SourceError != null || f.Destinations.Any(d => !IsOk(d))).ToList();
        }
    }

    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was read back with a matching hash.
    [JsonIgnore] public bool SafeToEject => Success && Verified;

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<ulong?> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished)
    {
        bool IsOk(FileState s) => s == FileState.Verified || (!verified && s == FileState.Written);
        var files = plan.Scan.Files.Select((f, i) => new ReportFile
        {
            Path = f.RelPath,
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i] is ulong h ? Format.Hex64(h) : null,
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
        };
    }

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
    public void Write(string cardPath, bool includeHashRecords)
    {
        var dir = System.IO.Path.Combine(cardPath, Layout.ReportDirName);
        NativeIO.Mkdirs(dir);
        var outputs = new List<(string Name, string Text)>
        {
            ("offload_report.json", ToJson()),
            ("offload_report.txt", Summary),
            ($"{CardName}_report.html", HtmlReport.Render(this)),
        };
        if (includeHashRecords) outputs.Add(($"{CardName}.xxh64", HashList));
        foreach (var (name, text) in outputs)
            NativeIO.WriteFileDurably(System.IO.Path.Combine(dir, name), Encoding.UTF8.GetBytes(text));
        if (includeHashRecords) Mhl.Write(this, cardPath);
    }

    static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        _ => node?.DeepClone(),
    };

    sealed class IsoSecondsDateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}
