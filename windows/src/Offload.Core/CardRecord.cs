using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// One report of a card folder: the full copy (generation 1, offload_report.json) or an append (offload_report_NNNN.json).
public sealed record CardRecordGeneration(int Generation, OffloadReport Report);

/// A file as the card folder's reports record it (spec 3.1): the newest generation that lists it.
public sealed record RecordedFile(ReportFile File, int Generation, OffloadReport Report);

/// What the reports in `<card>\_REPORT` say about a card folder — every generation, also reports written by the Mac
/// (no "generation", "lut", "proxyEncoder" …). The core for appends (plan 6); plan 7 adds the day report and clip lists.
public sealed partial class CardRecord
{
    public const string MainReport = "offload_report.json";

    public required string CardPath { get; init; }
    public IReadOnlyList<CardRecordGeneration> Generations { get; init; } = [];
    public IReadOnlyDictionary<string, RecordedFile> Files { get; init; } = new Dictionary<string, RecordedFile>();
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
        var files = new Dictionary<string, RecordedFile>(StringComparer.Ordinal);
        foreach (var g in generations)
            foreach (var f in g.Report.Files) files[f.Path] = new RecordedFile(f, g.Generation, g.Report);
        return new CardRecord { CardPath = cardPath, Generations = generations, Files = files, Problems = problems, HighestReportNumber = highest };
    }

    /// A report whose lists are there: a hand-edited null would otherwise surface later as a crash, not as "nejde přečíst".
    static OffloadReport Read(string path)
    {
        var r = OffloadReport.FromJson(File.ReadAllText(path));
        if (r == null || r.Files == null || r.Destinations == null
            || r.Files.Any(f => f == null || f.Path == null || f.Destinations == null)
            || r.Destinations.Any(d => d == null || d.CardPath == null))
            throw new InvalidOperationException("chybí seznam souborů nebo cílů");
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
