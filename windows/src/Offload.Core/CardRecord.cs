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
    /// Every file's newest record was read back here (ReadBack) — an append report nobody can read leaves it unknown, so
    /// not verified. Per file, not per report (plan 9 fix round 1, item 2): a cancelled generation whose files a later
    /// one read back no longer leaves the card unverified for ever. A folder whose reports list no file: every report
    /// was read back.
    public bool Verified => Generations.Count > 0 && Problems.Count == 0
        && (Files.Count == 0 ? Generations.All(g => g.Report.Verified) : Files.Values.All(f => ReadBack(f, CardPath)));
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

    /// `maxGeneration` (R6): generations above it are left out — the unfinished one a resume completes (its reports, if
    /// any, and its numbered files are not the backup's record yet).
    public static CardRecord Load(string cardPath, int? maxGeneration = null)
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
            if (NumberedReportFile().Match(name) is { Success: true } n && int.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture) is var number
                && number <= (maxGeneration ?? int.MaxValue))
                highest = Math.Max(highest, number);
            if (AppendReport().Match(name) is not { Success: true } m) continue;
            if (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) > (maxGeneration ?? int.MaxValue)) continue;
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
            bool analysed = g.Report.Clips.Count > 0;
            foreach (var c in generationClips)
            {
                var recorded = new RecordedClip(c, g.Generation);
                // A generation without analysis (a repair rewrites a clip's file) never replaces a clip described before.
                if (clipAt.TryGetValue(c.Clip.Path, out var i)) { if (analysed || clips[i].Entry.Info == null) clips[i] = recorded; }
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

    /// The state the newest record of a file gives the copy in `cardPath` ("verified", "written", "failed", "pending");
    /// `position`: the folder's place among the backup's destinations, for a report that names it differently.
    public static string StateOf(RecordedFile rec, string cardPath, int position)
    {
        int column = DestinationIndex(rec.Report, cardPath, position);
        return column < rec.File.Destinations.Count ? rec.File.Destinations[column] : "pending";
    }

    /// Controller ruling R2 — THE one definition of "verified for this destination folder" (the day report's Verified,
    /// history, a repair's eject verdict and warnings via BackupState, AppendPlanner's and RepairPlanner's unverified
    /// files): the newest record of the copy in `cardPath` says it was read back with the recorded hash — or it failed in
    /// a copy that was read back (a failure counts as a failure, not as unverified). "Written", or anything in a copy that
    /// was not read back (cancelled, made without verification), is not. The column is the report destination whose card
    /// path is this folder (ColumnOf); when none is (a Mac report, the disk under another letter) every column must say
    /// so — never falsely green.
    public static bool ReadBack(RecordedFile rec, string cardPath)
    {
        int column = ColumnOf(rec.Report, cardPath);
        var states = column >= 0
            ? [column < rec.File.Destinations.Count ? rec.File.Destinations[column] : "pending"]
            : rec.File.Destinations.Count > 0 ? rec.File.Destinations : ["pending"];
        // Plan 10: a report whose verification has not ended (verifyPending) is never read as verified, whatever it says.
        return rec.Report.VerifyPending != true && states.All(state => state == "verified" || (rec.Report.Verified && state != "written"));
    }

    /// The column of `report` whose destination card path is `cardPath` (full path, any case, trailing separator), or
    /// -1 when none is. A path that is not a full Windows path (a Mac `/Volumes/…`) is compared as written.
    public static int ColumnOf(OffloadReport report, string cardPath)
    {
        var me = Normal(cardPath);
        for (int i = 0; i < report.Destinations.Count; i++)
            if (string.Equals(Normal(report.Destinations[i].CardPath), me, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    static string Normal(string path)
    {
        try
        {
            if (Path.IsPathFullyQualified(path)) path = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        return path.TrimEnd('\\', '/');
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
