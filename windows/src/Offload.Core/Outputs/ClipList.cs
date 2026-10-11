using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Offload.Core.Outputs;

/// "outputs" in settings.json (spec 5.1) and in a job: the files made for production and editing.
public sealed class OutputSettings
{
    /// The day report after every job (spec 3.2) — on by default.
    [JsonPropertyName("dayReport")] public bool DayReport { get; set; } = true;
    /// `_REPORT\<card>_clips.csv` (and the day's) — on by default (R13).
    [JsonPropertyName("clipListCsv")] public bool ClipListCsv { get; set; } = true;
    /// `_REPORT\<card>_clips.ale` for Avid — off by default (R13).
    [JsonPropertyName("clipListAle")] public bool ClipListAle { get; set; }
    /// A copy of every card's report on this computer (task 2026-10-09) — on by default.
    [JsonPropertyName("localReport")] public bool LocalReport { get; set; } = true;
    /// Where: null = `<Dokumenty>\mmMover reports` (LocalReports.DefaultRoot).
    [JsonPropertyName("localReportFolder")] public string? LocalReportFolder { get; set; }

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// The clip list of a card or a day (spec 3.3): CSV for Resolve and Excel, ALE for Avid. Derived from the reports, so
/// written whole every time (tmp + rename, R25). Camera proxies (Sony SUB, PROXY folders) are not listed — they are not
/// the clips editing works with.
public static partial class ClipList
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// One clip: the card folder it is in, what the report knows, and the hashes of its main file (the first segment).
    public sealed record Row(string Card, Clip Clip, ClipInfo? Info, LutUse? Lut, string? Xxh64, string? Md5)
    {
        public string Reel => ReelResolver.Resolve(Clip, Info, Card);
        public double? Fps => Timecode.Fps(Info?.FrameRate);
        public string FpsText => Fps is double f ? f.ToString("0.###", Inv) : "";
        /// The capture rate (task 2026-10-10; CSV "Capture FPS", ALE "Sensor_fps" as ARRI's own ALEs name it): from the
        /// metadata, else the playback rate (nothing says otherwise); empty when neither is known.
        public string CaptureFpsText => (Timecode.Fps(Info?.CaptureFrameRate) ?? Fps) is double f ? f.ToString("0.###", Inv) : "";
        public string? StartTc => Timecode.IsValid(Info?.Timecode) ? Info!.Timecode : null;
        /// Frames of the clip: the stream's count, else duration × rate; null when either is unknown.
        public long? Frames => Info?.Frames is int n && n > 0 ? n : Info?.Duration is double d && Fps is double f ? (long)Math.Round(d * f) : null;
        public string FileName => Path.GetFileName((Clip.Segments.FirstOrDefault() ?? Clip.Path).Replace('/', '\\'));
        public string Codec => Clip.Format != ClipFormat.Video ? Clip.Format.Label() : Info?.Codec ?? "";
        public string Resolution => Info is { Width: int w, Height: int h } ? $"{w}x{h}" : "";
        public string LutName => Lut is { Source: not "none" } l ? l.Name : "";
    }

    /// The rows of a card folder: every generation's clips (CardRecord.Clips), camera proxies left out.
    public static List<Row> Rows(CardRecord record) =>
        record.Clips.Where(c => !c.Entry.Clip.IsCameraProxy).Select(c =>
        {
            var main = c.Entry.Clip.Segments.FirstOrDefault() ?? c.Entry.Clip.Path;
            var file = record.Files.GetValueOrDefault(main)?.File;
            return new Row(record.FolderName, c.Entry.Clip, c.Entry.Info, c.Entry.Lut, file?.Xxh64, file?.Md5);
        }).ToList();

    public static readonly IReadOnlyList<string> CsvColumns =
    [
        "Clip Name", "File Name", "Path", "Card", "Camera", "Reel", "Start TC", "Duration", "FPS", "Capture FPS", "Resolution", "Codec", "LUT",
        "Size (bytes)", "XXH64", "MD5",
    ];

    /// Duration as "HH:MM:SS:FF" in nominal frames; without a frame rate "H:MM:SS"; unknown: empty.
    static string DurationText(Row r) =>
        r.Frames is long n && r.Fps is double f ? Timecode.Length(n, f) : r.Info?.DurationLabel ?? "";

    /// RFC 4180, comma, CRLF, header always; UTF-8 with BOM (WriteForCard adds it). Values starting with = + - @ are left
    /// as they are — changing them would break the import into Resolve.
    public static string Csv(IEnumerable<Row> rows)
    {
        var s = new StringBuilder();
        void Line(IEnumerable<string> fields) => s.Append(string.Join(",", fields.Select(CsvField))).Append("\r\n");
        Line(CsvColumns);
        foreach (var r in rows)
            Line([r.Clip.Name, r.FileName, r.Clip.Path, r.Card, r.Info?.Camera ?? "", r.Reel, r.StartTc ?? "", DurationText(r), r.FpsText, r.CaptureFpsText,
                  r.Resolution, r.Codec, r.LutName, r.Clip.Bytes.ToString(Inv), r.Xxh64 ?? "", r.Md5 ?? ""]);
        return s.ToString();
    }

    static string CsvField(string v) =>
        v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    public static readonly IReadOnlyList<string> AleColumns =
        ["Name", "Tape", "Start", "End", "Tracks", "Source File", "Camera", "Sensor_fps", "LUT", "Resolution", "Codec", "XXH64", "MD5"];

    /// One ALE per frame rate (an ALE has one FPS in its heading): `Suffix` null when the clips share one rate, else the
    /// rate ("25", "23.976") for `<name>_clips_<rate>.ale`. A clip whose rate is unknown goes with the most common rate;
    /// when no clip has one, the heading says 25.
    public static IReadOnlyList<(string? Suffix, string Text)> Ale(IReadOnlyList<Row> rows)
    {
        // Rates in the order they first appear; the most common one (ties: the first) also takes the clips without one.
        var rates = rows.Where(r => r.Fps != null).Select(r => r.FpsText).Distinct().ToList();
        if (rates.Count == 0) return [(null, AleText("25", 25, rows))];
        var main = rates.OrderByDescending(f => rows.Count(r => r.FpsText == f)).First();
        return rates.Select(f => (
            rates.Count == 1 ? null : f,
            AleText(f, rows.First(r => r.FpsText == f).Fps!.Value, rows.Where(r => r.FpsText == f || (f == main && r.Fps == null)).ToList())))
            .ToList();
    }

    /// Tabs and line breaks inside a value would break the columns: they become spaces.
    static string AleField(string v) => v.Replace("\r\n", " ").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    static string AleText(string fpsText, double fps, IReadOnlyList<Row> rows)
    {
        var heights = rows.Where(r => r.Info?.Height != null).GroupBy(r => r.Info!.Height!.Value).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        var format = heights.FirstOrDefault() switch { 1080 => "1080", 720 => "720", _ => "CUSTOM" };
        var s = new StringBuilder();
        void Line(params string[] fields) => s.Append(string.Join("\t", fields.Select(AleField))).Append("\r\n");
        Line("Heading");
        Line("FIELD_DELIM", "TABS");
        Line("VIDEO_FORMAT", format);
        Line("AUDIO_FORMAT", "48khz");
        Line("FPS", fpsText);
        s.Append("\r\n");
        Line("Column");
        Line([.. AleColumns]);
        Line("Data");
        foreach (var r in rows)
        {
            var start = r.StartTc ?? "00:00:00:00";
            var rate = r.Fps ?? fps;
            var end = r.Frames is long n ? Timecode.Add(start, n, rate) : start;
            int channels = Math.Min(r.Info?.AudioChannels ?? 0, 8);
            var tracks = "V" + string.Concat(Enumerable.Range(1, channels).Select(i => $"A{i}"));
            Line(r.Clip.Name, r.Reel, start, end, tracks, r.FileName, r.Info?.Camera ?? "", r.CaptureFpsText, r.LutName, r.Resolution, r.Codec, r.Xxh64 ?? "", r.Md5 ?? "");
        }
        return s.ToString();
    }

    /// Writes `<name>_clips.csv` and `<name>_clips[_<fps>].ale` into `dir` as `outputs` asks; an ALE of an earlier write
    /// that this one no longer makes (the rates changed) is removed. A list that is switched off but already there is
    /// rewritten all the same (an ALE: every rate file, stale ones removed) (it is derived, R25: left alone it would show only the earlier generations) — never
    /// deleted, the user may still use it, and never created. Each file is written on its own (task 6 review): a CSV
    /// held open in Excel does not stop the ALE; the first error is thrown once the rest is written (the caller logs it).
    /// Not under the append lock: a stage-2 rewrite racing an append of the same card from another process can leave
    /// the older snapshot behind; accepted (R25, ruling M10) — the next copy or append of the card rebuilds the list.
    public static void Write(string dir, string name, IReadOnlyList<Row> rows, OutputSettings outputs)
    {
        var csvPath = Path.Combine(dir, $"{name}_clips.csv");
        bool csv = outputs.ClipListCsv || File.Exists(csvPath);
        var ours = new Regex("^" + Regex.Escape(name) + @"_clips(_[0-9.]+)?\.ale$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var existingAle = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.ale").Select(Path.GetFileName).Where(f => ours.IsMatch(f!)).ToList()
            : [];
        if (!csv && !outputs.ClipListAle && existingAle.Count == 0) return;
        NativeIO.Mkdirs(dir);
        Exception? first = null;
        void Each(Action write)
        {
            try { write(); }
            catch (Exception e) { first ??= e; }
        }
        if (csv)
            Each(() => NativeIO.WriteFileReplacing(csvPath, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Csv(rows))]));
        // An ALE that exists is kept whole (all rate files, stale ones removed) even when switched off: derived, R25.
        bool ale = outputs.ClipListAle || existingAle.Count > 0;
        if (ale)
        {
            // Rates the ALE has now, also when a write below fails: the stale cleanup must not take a file that is still made.
            var made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (suffix, text) in Ale(rows))
            {
                var file = suffix == null ? $"{name}_clips.ale" : $"{name}_clips_{suffix}.ale";
                made.Add(file);
                Each(() => NativeIO.WriteFileReplacing(Path.Combine(dir, file), new UTF8Encoding(false).GetBytes(text)));
            }
            foreach (var stale in existingAle.Where(f => !made.Contains(f!)))
                Each(() => NativeIO.Delete(Path.Combine(dir, stale!)));
        }
        if (first != null) ExceptionDispatchInfo.Capture(first).Throw();
    }

    /// The clip lists of one card folder (all generations) into its `_REPORT`.
    public static void WriteForCard(string cardPath, OutputSettings outputs)
    {
        var record = CardRecord.Load(cardPath);
        if (record.Generations.Count == 0) return;
        Write(Path.Combine(cardPath, Layout.ReportDirName), record.FolderName, Rows(record), outputs);
    }
}
