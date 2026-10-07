using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// One report of a card folder: the full copy (generation 1, offload_report.json) or an append (offload_report_NNNN.json).
public sealed record CardRecordGeneration(int Generation, OffloadReport Report);

/// A file as the card folder's reports record it (spec 3.1): the newest generation that lists it.
public sealed record RecordedFile(ReportFile File, int Generation, OffloadReport Report);

/// A clip as the card folder's reports record it (spec 3.1): the newest generation that lists its path.
public sealed record RecordedClip(ClipEntry Entry, int Generation);

/// What the reports in `<card>\_REPORT` say about a card folder — every generation, also reports written by the Mac
/// (no "generation", "lut", "proxyEncoder", "md5" …). The source of appends (plan 6), the day report and the clip lists
/// (plan 7).
public sealed partial class CardRecord
{
    public const string MainReport = "offload_report.json";

    public required string CardPath { get; init; }
    public IReadOnlyList<CardRecordGeneration> Generations { get; init; } = [];
    public IReadOnlyDictionary<string, RecordedFile> Files { get; init; } = new Dictionary<string, RecordedFile>();
    /// Every clip of every generation, in generation order (an append adds the clips of its new files); a clip path a
    /// later generation lists again replaces the earlier entry. A generation without clip analysis (no clips in its
    /// report) gets the clips ClipDetector finds in its files — without metadata (Info null).
    public IReadOnlyList<RecordedClip> Clips { get; init; } = [];

    /// The card folder's name ("A001") — how the day report and the clip lists call the card.
    public string FolderName => Path.GetFileName(CardPath.TrimEnd('\\', '/'));
    /// The first start and the last end over the generations read; null when none was.
    public DateTime? Started => Generations.Count > 0 ? Generations.Min(g => g.Report.Started) : null;
    public DateTime? Finished => Generations.Count > 0 ? Generations.Max(g => g.Report.Finished) : null;
    /// The project of the card (spec 4.1): the first generation's that names one; null = none (or older reports only).
    public string? Project => Generations.Select(g => g.Report.Project).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
    public int TotalFiles => Files.Count;
    public long TotalBytes => Files.Values.Sum(f => f.File.Size);
    /// Every generation was read back — an append report nobody can read leaves it unknown, so not verified.
    public bool Verified => Generations.Count > 0 && Problems.Count == 0 && Generations.All(g => g.Report.Verified);
    /// Paths whose newest record failed (the card or a destination), sorted.
    public IReadOnlyList<string> FailedFiles =>
        Files.Values.Where(f => f.Report.IsFailed(f.File)).Select(f => f.File.Path).Order(StringComparer.Ordinal).ToList();
    /// _REPORT\offload_report.json is not there.
    public bool ReportMissing { get; init; }
    /// Why the main report could not be read; null when it was read (or is missing).
    public string? Error { get; init; }
    /// Append reports that could not be read (file name + reason).
    public IReadOnlyList<string> Problems { get; init; } = [];
    /// The highest generation any report file in _REPORT stands for (ruling I1): the generations read, and every
    /// `…_report_NNNN.json/.txt/.html/.pdf` — also one left without its JSON by an append that stopped half way.
    public int HighestReportNumber { get; init; }

    [GeneratedRegex(@"^offload_report_(\d{4})\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex AppendReport();

    [GeneratedRegex(@"_report_(\d{4})\.(json|txt|html|pdf)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedReportFile();

    public static CardRecord Load(string cardPath)
    {
        var dir = Path.Combine(cardPath, Layout.ReportDirName);
        var main = Path.Combine(dir, MainReport);
        if (!File.Exists(main)) return new CardRecord { CardPath = cardPath, ReportMissing = true };
        var generations = new List<CardRecordGeneration>();
        var problems = new List<string>();
        try
        {
            var r = Read(main);
            generations.Add(new CardRecordGeneration(r.Generation ?? 1, r));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException
                                     or NotSupportedException or ArgumentException)
        {
            return new CardRecord { CardPath = cardPath, Error = e.Message };
        }
        int highest = generations[0].Generation;
        string[] reportFiles;
        try
        {
            reportFiles = Directory.GetFiles(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The appends cannot be listed: what the folder holds is unknown (fix round 1, item 7).
            return new CardRecord { CardPath = cardPath, Error = e.Message };
        }
        foreach (var file in reportFiles)
        {
            var name = Path.GetFileName(file);
            if (NumberedReportFile().Match(name) is { Success: true } n)
                highest = Math.Max(highest, int.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture));
            if (AppendReport().Match(name) is not { Success: true } m) continue;
            try
            {
                var r = Read(file);
                var generation = r.Generation ?? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                generations.Add(new CardRecordGeneration(generation, r));
                highest = Math.Max(highest, generation);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException
                                         or NotSupportedException or ArgumentException)
            {
                problems.Add($"{name}: {e.Message}");
            }
        }
        generations.Sort((a, b) => a.Generation.CompareTo(b.Generation));
        var files = new Dictionary<string, RecordedFile>(StringComparer.OrdinalIgnoreCase);
        var clips = new List<RecordedClip>();
        var clipAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in generations)
        {
            foreach (var f in g.Report.Files) files[f.Path] = new RecordedFile(f, g.Generation, g.Report);
            var generationClips = g.Report.Clips.Count > 0 ? g.Report.Clips
                : ClipDetector.Detect(g.Report.Files.Select(f => new SourceFile(f.Path, f.Size, f.Modified)).ToList()).Select(c => new ClipEntry { Clip = c });
            foreach (var c in generationClips)
            {
                var recorded = new RecordedClip(c, g.Generation);
                if (clipAt.TryGetValue(c.Clip.Path, out var i)) clips[i] = recorded;
                else
                {
                    clipAt[c.Clip.Path] = clips.Count;
                    clips.Add(recorded);
                }
            }
        }
        return new CardRecord
        {
            CardPath = cardPath, Generations = generations, Files = files, Clips = clips, Problems = problems, HighestReportNumber = highest,
        };
    }

    /// A report whose lists are there: a hand-edited null would otherwise surface later as a crash, not as "nejde přečíst".
    /// A missing or null "clips" (a report without analysis, an older Mac) is an empty list — Load then finds the clips
    /// in the files; a null clip entry is dropped — the files are the record of the copy, the clips only describe them.
    static OffloadReport Read(string path)
    {
        var r = OffloadReport.FromJson(File.ReadAllText(path));
        if (r == null || r.Files == null || r.Destinations == null
            || r.Files.Any(f => f == null || f.Path == null || f.Destinations == null)
            || r.Destinations.Any(d => d == null || d.CardPath == null))
            throw new InvalidOperationException("chybí seznam souborů nebo cílů");
        r.Clips = (r.Clips ?? []).Where(c => c?.Clip?.Path != null && c.Clip.Name != null && c.Clip.Segments != null).ToList();
        return r;
    }

    /// Which column of a report's per-destination states belongs to `cardPath`: the destination with that card path;
    /// otherwise (a report from the Mac, a disk under another letter) the only one, or the same position.
    public static int DestinationIndex(OffloadReport report, string cardPath, int position)
    {
        for (int i = 0; i < report.Destinations.Count; i++)
            if (string.Equals(report.Destinations[i].CardPath.TrimEnd('\\', '/'), cardPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return i;
        return report.Destinations.Count == 1 ? 0 : Math.Clamp(position, 0, Math.Max(0, report.Destinations.Count - 1));
    }
}
