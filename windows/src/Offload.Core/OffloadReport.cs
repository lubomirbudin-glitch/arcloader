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
    [JsonPropertyName("filesOK")] public int FilesOK { get; set; }
    [JsonPropertyName("filesFailed")] public int FilesFailed { get; set; }
    /// Present (false) only when the volume refused unbuffered writes and the file cache was used.
    [JsonPropertyName("unbuffered")] public bool? Unbuffered { get; init; }
    /// Plan 10 (spec 9): how this destination was verified — "verify-in-proxy" (the proxy's reads and the fill-in) or
    /// "read-back" (every file read again); absent while not verified (yet). The Mac ignores it.
    [JsonPropertyName("verifyMethod")] public string? VerifyMethod { get; set; }
    /// Why it was verified by read-back although verify-in-proxy ran (Czech, e.g. a network volume); absent otherwise.
    [JsonPropertyName("verifyNote")] public string? VerifyNote { get; set; }
    /// R12: the volume's serial number at the copy ("1A2B-3C4D") — which disk it really was, whatever its letter is now.
    [JsonPropertyName("volumeSerial")] public string? VolumeSerial { get; init; }

    public const string InProxy = "verify-in-proxy";
    public const string ReadBack = "read-back";
}

public sealed class ReportFile
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("modified")] public DateTime Modified { get; init; }
    [JsonPropertyName("xxh64")] public string? Xxh64 { get; init; }
    /// Only when the copy computed MD5 too (spec 3.5): 32 lower-case hex digits.
    [JsonPropertyName("md5")] public string? Md5 { get; init; }
    /// XXH3-128 of the card's file (plan 10, R2: into the ASC MHL next to xxh64), 32 lower-case hex digits; absent in
    /// older reports and for a file the card could not give. The Mac ignores it.
    [JsonPropertyName("xxh128")] public string? Xxh128 { get; init; }
    [JsonPropertyName("sourceError")] public string? SourceError { get; init; }
    [JsonPropertyName("destinations")] public required IReadOnlyList<string> Destinations { get; set; }
    [JsonPropertyName("errors")] public required IReadOnlyList<string?> Errors { get; set; }
    /// Repair only (Windows from 0.5.0): per destination, true = a new copy was written there, false = the good copy there
    /// was only read back (its state carries on the earlier one). The Mac ignores it.
    [JsonPropertyName("rewritten")] public IReadOnlyList<bool>? Rewritten { get; init; }
}

/// Repair: a copy that did not match the card, moved aside before the new copy took its place (never deleted).
public sealed record SetAsideCopy(
    [property: JsonPropertyName("destination")] int Destination,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("movedTo")] string MovedTo);

public sealed partial class OffloadReport
{
    [JsonPropertyName("tool")] public string Tool { get; init; } = "offload";
    [JsonPropertyName("version")] public string Version { get; init; } = OffloadVersion.String;
    /// Always "xxh64" (the Mac only shows it); `hashAlgorithms` says when there is more.
    [JsonPropertyName("hashAlgorithm")] public string HashAlgorithm { get; init; } = "xxh64";
    /// Only when it is not just xxh64 (spec 3.5): ["xxh64", "md5"].
    [JsonPropertyName("hashAlgorithms")] public IReadOnlyList<string>? HashAlgorithms { get; init; }
    [JsonPropertyName("cardName")] public required string CardName { get; init; }
    [JsonPropertyName("date")] public required string Date { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("sourceFilesystem")] public required string SourceFilesystem { get; init; }
    [JsonPropertyName("started")] public DateTime Started { get; init; }
    [JsonPropertyName("copyFinished")] public DateTime CopyFinished { get; init; }
    [JsonPropertyName("finished")] public DateTime Finished { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
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
    /// The project the card was copied in (spec 4.1); absent for "Bez projektu" and in older reports. The Mac ignores it.
    [JsonPropertyName("project")] public string? Project { get; set; }
    /// Seconds the copy / the read-back stood paused (Pozastavit, Windows from 0.5.0); absent when never paused. The speeds
    /// leave the pause out; the Mac ignores the keys (its speeds then include it).
    [JsonPropertyName("copyPausedSeconds")] public double? CopyPausedSeconds { get; set; }
    [JsonPropertyName("verifyPausedSeconds")] public double? VerifyPausedSeconds { get; set; }
    /// Plan 10: the copy is done and its verification runs with the proxies (stage 2) — written at the end of the copy,
    /// gone once verified. A report saying so has no ASC MHL generation yet (OffloadReport.Commit). Absent otherwise.
    [JsonPropertyName("verifyPending")] public bool? VerifyPending { get; set; }
    /// Spec 9: seconds from the start until every file was verified on every destination (pauses left out); absent when
    /// that was never reached. The Mac ignores it.
    [JsonPropertyName("safeToFormatSeconds")] public double? SafeToFormatSeconds { get; set; }
    /// Spec 10 (Czech): unstable reads, a block wrong on several destinations, what the card gave for it again.
    [JsonPropertyName("verifyNotes")] public List<string>? VerifyNotes { get; set; }
    /// "repair" for "Zkopírovat znovu" (Windows from 0.5.0); absent for a full copy and an append. The Mac ignores it.
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    /// Repair: the non-matching copies moved aside (`_REPORT/neshoda_NNNN/…`); absent when none was.
    [JsonPropertyName("setAside")] public IReadOnlyList<SetAsideCopy>? SetAside { get; set; }

    public const string RepairKind = "repair";
    [JsonIgnore] public bool IsRepair => Kind == RepairKind;

    /// The hashes of this copy: `hashAlgorithms`, else the one `hashAlgorithm` (reports without the key, the Mac).
    [JsonIgnore] public IReadOnlyList<string> Algorithms => HashAlgorithms is { Count: > 0 } all ? all : [HashAlgorithm];

    [JsonIgnore] public double CopySeconds => Math.Max(0, (CopyFinished - Started).TotalSeconds - (CopyPausedSeconds ?? 0));
    [JsonIgnore] public double VerifySeconds => Math.Max(0, (Finished - CopyFinished).TotalSeconds - (VerifyPausedSeconds ?? 0));
    [JsonIgnore] public double PausedSeconds => (CopyPausedSeconds ?? 0) + (VerifyPausedSeconds ?? 0);

    [JsonIgnore]
    public IReadOnlyList<ReportFile> FailedFiles => Files.Where(IsFailed).ToList();

    /// The card could not be read, or a destination does not hold a good copy. A cancelled verification leaves files
    /// "written" or already "verified": both are fine then.
    public bool IsFailed(ReportFile f) =>
        f.SourceError != null || f.Destinations.Any(d => !IsGood(d));

    /// THE per-destination definition of a good copy in this report (a job card shows it per disk): read back, or — in a
    /// report made without read-back — written.
    public bool IsGood(string state) => state == "verified" || (!Verified && state == "written");

    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was verified there (spec 2 safe_to_format for this generation).
    [JsonIgnore] public bool SafeToFormat => Success && Verified;

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<FileHashes> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
        DateTime started, DateTime copyFinished, DateTime finished, TimeSpan copyPaused = default, TimeSpan verifyPaused = default)
    {
        static double? Seconds(TimeSpan t) => t > TimeSpan.Zero ? Math.Round(t.TotalSeconds, 1) : null;
        bool IsOk(FileState s) => s == FileState.Verified || (!verified && s == FileState.Written);
        var files = plan.Scan.Files.Select((f, i) => new ReportFile
        {
            Path = f.RelPath,
            Size = f.Size,
            Modified = f.ModifiedUtc,
            Xxh64 = hashes[i].Xxh64Hex,
            Md5 = hashes[i].Md5Hex,
            Xxh128 = hashes[i].Xxh128,
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
            Rewritten = plan.Write?[i].ToList(),
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
            VerifyMethod = verified ? ReportDestination.ReadBack : null,
            VolumeSerial = NativeIO.VolumeSerialText(root),
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
            Project = plan.Project,
            Kind = plan.Mode == PlanMode.Repair ? RepairKind : null,
            CopyPausedSeconds = Seconds(copyPaused),
            VerifyPausedSeconds = Seconds(verifyPaused),
            SafeToFormatSeconds = verified && files.All(f => f.SourceError == null && f.Destinations.All(s => s == "verified"))
                ? Math.Round(Math.Max(0, (finished - started - copyPaused - verifyPaused).TotalSeconds), 1) : null,
            HashAlgorithms = HashKinds.Normalize(plan.Hashes).HasFlag(HashKind.Md5) ? HashKinds.Names(plan.Hashes) : null,
        };
    }

    /// Report file names of generation `generation`: the full copy keeps the plain names, an append adds "_NNNN" (R6).
    public static string JsonName(int generation) => generation <= 1 ? "offload_report.json" : $"offload_report_{generation:0000}.json";
    public static string TextName(int generation) => generation <= 1 ? "offload_report.txt" : $"offload_report_{generation:0000}.txt";
    public static string HtmlName(string cardName, int generation) => generation <= 1 ? $"{cardName}_report.html" : $"{cardName}_report_{generation:0000}.html";
    public static string PdfName(string cardName, int generation) => generation <= 1 ? $"{cardName}_report.pdf" : $"{cardName}_report_{generation:0000}.pdf";

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
    [JsonIgnore]
    public string HashList => HashListOf("xxh64");

    /// "<hash>  <path>" per file that has hash `algorithm` ("xxh64" or "md5", the md5sum format), "\n" after each.
    public string HashListOf(string algorithm) =>
        string.Concat(Files.Select(f => (Hash: algorithm == "md5" ? f.Md5 : f.Xxh64, f.Path)).Where(x => x.Hash != null).Select(x => $"{x.Hash}  {x.Path}\n"));

    /// `_REPORT\<card>.xxh64` or `.md5`.
    static string HashListPath(string cardPath, string cardName, string algorithm) =>
        System.IO.Path.Combine(cardPath, Layout.ReportDirName, $"{cardName}.{algorithm}");

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
            if (PausedSeconds > 0) s.Append($"Paused:      {PausedSeconds.ToString("0.0", inv)} s (not in the speeds)\n");
            s.Append($"Hash:        {string.Join("+", Algorithms)}\n");
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
    /// `createNew` (plan 10): whether the reports must not exist yet — by default an append's first write (with the hash
    /// records); the deferred first write of a verify-in-proxy append says so itself, its Commit replaces them.
    public void Write(string cardPath, bool includeHashRecords, bool? createNew = null)
    {
        WriteReports(cardPath, createNew ?? ((Generation ?? 1) > 1 && includeHashRecords));
        if (!includeHashRecords) return;
        Mhl.Write(this, cardPath);
        WriteHashLists(cardPath);
    }

    /// Plan 10: the generation's record once its verification ended in stage 2. The ASC MHL generation and the chain come
    /// first — the commit point — then the reports written at the end of the copy (with verifyPending) are replaced, then
    /// the hash lists. Ruling I1: a commit that fails at any step before the reports leaves the folder's reports saying
    /// verifyPending, so the folder is never read as verified (CardRecord.ReadBack, BackupState) without its MHL
    /// generation; one that fails at the hash lists leaves reports and chain in agreement, the next append adds the lines
    /// (as in Write). The MHL generation file is new (no stage wrote it before).
    /// Idempotent (fix round 1): a retry after a commit that failed past the chain finds its generation there
    /// (Mhl.HasGeneration) and writes only the reports and the hash lists — the chain keeps every generation, also one an
    /// append added meanwhile; the hash lists only get the lines they lack.
    public void Commit(string cardPath)
    {
        if (VerifyPending == true) throw new InvalidOperationException("a report still pending verification is never committed");
        if (!Mhl.HasGeneration(this, cardPath)) Mhl.Write(this, cardPath);
        WriteReports(cardPath, create: false);
        WriteHashLists(cardPath, merge: true);
    }

    /// The JSON, TXT and HTML of this generation: `create` = they must not exist yet (WriteFileNew).
    void WriteReports(string cardPath, bool create)
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
        foreach (var (name, text) in outputs)
        {
            var path = System.IO.Path.Combine(dir, name);
            if (create) NativeIO.WriteFileNew(path, Encoding.UTF8.GetBytes(text));
            else NativeIO.WriteFileDurably(path, Encoding.UTF8.GetBytes(text));
        }
    }

    /// `<card>.xxh64` always, `<card>.md5` with MD5 (spec 3.5). An append: the earlier lines byte for byte, then the
    /// missed ones, then this generation's.
    /// `merge` (Commit): generation 1 too keeps a list that is there and only adds what it lacks — a retry, or an append
    /// that ran between a failed commit and its retry, may have written it.
    void WriteHashLists(string cardPath, bool merge = false)
    {
        int generation = Generation ?? 1;
        foreach (var algorithm in Algorithms.Where(a => a is "xxh64" or "md5"))
        {
            var hashList = HashListPath(cardPath, CardName, algorithm);
            var lines = Encoding.UTF8.GetBytes(HashListOf(algorithm));
            if (generation == 1 && !(merge && File.Exists(hashList)))
            {
                NativeIO.WriteFileReplacing(hashList, lines);
                continue;
            }
            var (earlier, missed) = HashListAndMissed(hashList, cardPath, Files.Select(f => f.Path), algorithm);
            byte[] added = [.. missed, .. NotIn(earlier, lines)];
            if (added.Length == 0 && File.Exists(hashList)) continue;   // nothing new (a repair): the list is left as it is
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. added]);
        }
    }

    /// The lines of `lines` the list does not have word for word yet: a repair's file is listed already with the hash the
    /// card gave then (a repair records only that one — ruling C1), so the list keeps one line per path and hash.
    static byte[] NotIn(byte[] earlier, byte[] lines)
    {
        var have = Encoding.UTF8.GetString(earlier).Split('\n').Select(l => l.TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
        var keep = Encoding.UTF8.GetString(lines).Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !have.Contains(l));
        return Encoding.UTF8.GetBytes(string.Concat(keep.Select(l => l + "\n")));
    }

    /// `<card>/_REPORT/<card name>.xxh64` (and `.md5` when the ASC MHL history has MD5) gets the lines of every path the
    /// history records but the list lacks (a hash list whose write failed, step 3 of Write) — the record files that may
    /// be rewritten. Used by an append with nothing new (fix round 2, item 6b); an append with new files does the same
    /// in Write. False: nothing missed.
    public static bool HealHashList(string cardPath, string cardName)
    {
        bool healed = false;
        foreach (var algorithm in new[] { "xxh64", "md5" })
        {
            var hashList = HashListPath(cardPath, cardName, algorithm);
            var (earlier, missed) = HashListAndMissed(hashList, cardPath, [], algorithm);
            if (missed.Length == 0) continue;
            NativeIO.WriteFileReplacing(hashList, [.. earlier, .. missed]);
            healed = true;
        }
        return healed;
    }

    /// The card's hash list as the earlier generations left it (exact bytes, then a line break if it had none), and
    /// the `algorithm` lines of every path the ASC MHL history records but neither the list nor `alsoListed` has.
    static (byte[] Earlier, byte[] Missed) HashListAndMissed(string path, string cardPath, IEnumerable<string> alsoListed, string algorithm)
    {
        var earlier = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (earlier.Length > 0 && earlier[^1] != (byte)'\n') earlier = [.. earlier, (byte)'\n'];
        var listed = new HashSet<string>(alsoListed, StringComparer.OrdinalIgnoreCase);
        foreach (var line in Encoding.UTF8.GetString(earlier).Split('\n'))
            if (line.TrimEnd('\r').IndexOf("  ", StringComparison.Ordinal) is int at and >= 0) listed.Add(line.TrimEnd('\r')[(at + 2)..]);
        var missed = new StringBuilder();
        if (Verification.MhlReader.Read(cardPath) is { } mhl)
            foreach (var r in mhl.LastGood.Values.OrderBy(r => r.Path, StringComparer.Ordinal))
                if (!listed.Contains(r.Path) && r.Hashes.FirstOrDefault(h => h.Algorithm == algorithm) is { } h)
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
