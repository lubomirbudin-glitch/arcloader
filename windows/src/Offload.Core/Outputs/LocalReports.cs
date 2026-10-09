using System.Text;
using System.Text.Json;

namespace Offload.Core.Outputs;

/// Task "Kopie reportu i na počítači" (2026-10-09): every card's `_REPORT` and `ascmhl` are copied to
/// `<Dokumenty>\Offload reporty\<datum>\<karta>\` (the layout of the destination), and copied again whenever the report
/// is rewritten — the disks with the footage are handed on, the report stays here. Reads the destinations only, never
/// copies media (a repair's `neshoda_NNNN` copies, clips, proxies) and never writes outside the local folder. Derived
/// files: a failure throws, the job only notes it.
public static class LocalReports
{
    public const string FolderName = "Offload reporty";

    /// The marker in a local card folder: which backup it holds (OriginOf) — the `_2` rule tells cards apart by it.
    const string Marker = ".offload-origin";

    /// Tests: where "Documents" is (the test assembly redirects it to %TEMP%, never the developer's own).
    internal static string? DocumentsOverride { get; set; }

    /// The user's Documents folder (the known folder, wherever it was moved — OneDrive, another disk).
    public static string Documents => DocumentsOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// `<Dokumenty>\Offload reporty`.
    public static string DefaultRoot => Path.Combine(Documents, FolderName);

    /// The folder a job copies into, or null when the copy is switched off (`outputs.localReport`).
    public static string? RootFor(OutputSettings o) =>
        !o.LocalReport ? null : string.IsNullOrWhiteSpace(o.LocalReportFolder) ? DefaultRoot : o.LocalReportFolder.Trim();

    static readonly HashSet<string> MediaExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mov", ".mp4", ".m4v", ".mxf", ".braw", ".r3d", ".rdm", ".rdc", ".ari", ".arx", ".crm", ".cine", ".dng", ".avi", ".mts", ".m2ts",
        ".wav", ".bwf", ".mp3", ".aac", ".nef", ".cr2", ".cr3", ".arw", ".raf", ".insv", ".lrv", ".thm",
    };

    /// One copy at a time in this process: two jobs ending together must not claim the same `_2` folder.
    static readonly Lock gate = new();

    /// Copies the card's report from the first of `cardPaths` (the card folder on each destination) whose report is the
    /// newest; returns the local card folder, or null when no destination has a report. Throws on a failure.
    public static string? MirrorCard(IReadOnlyList<string> cardPaths, string localRoot)
    {
        var best = Newest(cardPaths);
        if (best == null) return null;
        var date = Path.GetFileName(Path.GetDirectoryName(best.TrimEnd('\\'))) ?? "";
        var card = Path.GetFileName(best.TrimEnd('\\'));
        if (date.Length == 0 || card.Length == 0) return null;
        var origin = OriginOf(best);
        lock (gate)
        {
            var local = Slot(Path.Combine(localRoot, date), card, origin);
            CopyTree(Path.Combine(best, Layout.ReportDirName), Path.Combine(local, Layout.ReportDirName));
            CopyTree(Path.Combine(best, Mhl.DirName), Path.Combine(local, Mhl.DirName));
            return local;
        }
    }

    /// Copies the day's `_REPORT` (day report HTML/PDF, the day's clip lists, verification reports kept there) from the
    /// first of `dayPaths` with the newest day report into `<local>\<datum>\_REPORT`; null when no day has a `_REPORT`.
    /// One `_REPORT` per date: another destination set's day report of the same date replaces it (it is a summary).
    public static string? MirrorDay(IReadOnlyList<string> dayPaths, string localRoot)
    {
        string? best = null;
        DateTime newest = DateTime.MinValue;
        foreach (var day in dayPaths)
        {
            var html = Path.Combine(day, Layout.ReportDirName, DayReport.HtmlName(Path.GetFileName(day.TrimEnd('\\'))));
            DateTime at;
            try
            {
                if (!File.Exists(html)) continue;
                at = File.GetLastWriteTimeUtc(html);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            // Written one after another by the same job: within two minutes they are the same report — the first wins.
            if (best == null || at > newest + TimeSpan.FromMinutes(2))
            {
                best = day;
                newest = at;
            }
        }
        // No day report (switched off): a day `_REPORT` that holds a verification report alone.
        best ??= dayPaths.FirstOrDefault(d => Directory.Exists(Path.Combine(d, Layout.ReportDirName)));
        if (best == null) return null;
        var local = Path.Combine(localRoot, Path.GetFileName(best.TrimEnd('\\')), Layout.ReportDirName);
        lock (gate) CopyTree(Path.Combine(best, Layout.ReportDirName), local);
        return local;
    }

    /// The card folder whose report is the newest: the highest generation, then committed over still verifying, then
    /// the later end, then more clips; the first destination on a tie. Reads only.
    static string? Newest(IReadOnlyList<string> cardPaths)
    {
        string? best = null;
        (int, int, int, long, int) bestKey = default;
        foreach (var path in cardPaths)
        {
            try
            {
                if (!Directory.Exists(Path.Combine(path, Layout.ReportDirName)) && !Directory.Exists(Path.Combine(path, Mhl.DirName))) continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { continue; }
            var key = KeyOf(path);
            if (best == null || key.CompareTo(bestKey) > 0)
            {
                best = path;
                bestKey = key;
            }
        }
        return best;
    }

    static (int Generation, int Committed, int Complete, long Finished, int Clips) KeyOf(string cardPath)
    {
        var dir = Path.Combine(cardPath, Layout.ReportDirName);
        int generation = 0;
        string? json = null;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "offload_report*.json"))
            {
                var name = Path.GetFileName(file);
                int g = string.Equals(name, CardRecord.MainReport, StringComparison.OrdinalIgnoreCase) ? 1
                    : name.Length == "offload_report_0000.json".Length && int.TryParse(name.AsSpan(15, 4), out var n) ? n : 0;
                if (g > generation)
                {
                    generation = g;
                    json = file;
                }
            }
            if (json == null) return (0, 0, 0, 0, 0);
            var r = OffloadReport.FromJson(File.ReadAllText(json));
            return (generation, r.VerifyPending == true ? 0 : 1, r.VerifyIncomplete == true ? 0 : 1, r.Finished.ToUniversalTime().Ticks, r.Clips.Count);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return (generation, 0, 0, 0, 0);   // unreadable: below a readable one of the same generation
        }
    }

    /// Which backup a card folder holds: its first report's start, card name and source — the same on every destination
    /// and for every later generation; the folder path when the report cannot be read.
    static string OriginOf(string cardPath)
    {
        try
        {
            var r = OffloadReport.FromJson(File.ReadAllText(Path.Combine(cardPath, Layout.ReportDirName, CardRecord.MainReport)));
            return $"{r.Started.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}|{r.CardName}|{r.Source}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return Path.GetFullPath(cardPath).ToUpperInvariant();
        }
    }

    /// The local folder of this backup in `dayDir`: NAME, NAME_2, NAME_3 … as on the destination (Layout.UniqueCardFolder)
    /// — the one whose marker names this backup, else the first free one, which is claimed. A folder without our marker
    /// (the user's own) is never taken.
    static string Slot(string dayDir, string name, string origin)
    {
        string? free = null;
        for (int n = 1; n < 1000; n++)
        {
            var candidate = Path.Combine(dayDir, n == 1 ? name : $"{name}_{n}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                free ??= candidate;
                // Keep looking only while the numbered folders go on: this backup may sit further up (its A001 was
                // deleted by hand). Stop at the first gap after a free one.
                if (!Directory.Exists(Path.Combine(dayDir, $"{name}_{n + 1}"))) break;
                continue;
            }
            var marker = Path.Combine(candidate, Marker);
            try
            {
                if (File.Exists(marker) && string.Equals(File.ReadAllText(marker, Encoding.UTF8).Trim(), origin, StringComparison.Ordinal)) return candidate;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        if (free == null) throw new IOException($"{dayDir}: příliš mnoho karet „{name}“");
        Directory.CreateDirectory(free);
        var path = Path.Combine(free, Marker);
        File.WriteAllText(path, origin, Encoding.UTF8);
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return free;
    }

    /// Every file under `from` into `to` (created), except media, a repair's set-aside copies (`neshoda_*`) and files in
    /// progress; a file already there with the same size and time is left alone, another is replaced via a temporary file.
    static void CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var name = Path.GetFileName(file);
            if (MediaExt.Contains(Path.GetExtension(name)) || name.StartsWith('.') || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || name.Contains(".offload-", StringComparison.OrdinalIgnoreCase)) continue;
            CopyFile(file, Path.Combine(to, name));
        }
        foreach (var sub in Directory.EnumerateDirectories(from))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith("neshoda_", StringComparison.OrdinalIgnoreCase) || name.StartsWith('.')) continue;
            CopyTree(sub, Path.Combine(to, name));
        }
    }

    static void CopyFile(string from, string to)
    {
        var source = new FileInfo(from);
        var target = new FileInfo(to);
        if (target.Exists && target.Length == source.Length && target.LastWriteTimeUtc == source.LastWriteTimeUtc) return;
        var tmp = to + ".offload-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.Copy(from, tmp);
            File.SetLastWriteTimeUtc(tmp, source.LastWriteTimeUtc);
            File.Move(tmp, to, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
