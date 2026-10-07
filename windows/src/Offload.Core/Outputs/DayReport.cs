using System.Globalization;
using System.Text;
using Offload.Core.Desktop;

namespace Offload.Core.Outputs;

/// One folder of a day as the day report shows it (spec 3.2) — sums over every generation of its reports.
public sealed class DayCard
{
    public required CardRecord Record { get; init; }
    /// Ruling M13: the folder has `_REPORT\` or `ascmhl\` — an offload card (also one whose copy stopped). Any other
    /// folder (a hand-copied SOUND) gets a row, but is not counted as a card and is no problem.
    public bool IsCard { get; init; } = true;
    public string Name => Record.FolderName;
    /// The folder has no `_REPORT\offload_report.json`: a copy in progress, or one that was stopped.
    public bool NoReport => Record.ReportMissing;
    /// Why a report of the card cannot be read — a Czech sentence, the reader's own (English) reason only in
    /// parentheses (task 1–2 review); null when every report was read.
    public string? Unreadable => DayReport.UnreadableTexts(Record).FirstOrDefault();
    IEnumerable<ClipEntry> Clips => Record.Clips.Select(c => c.Entry);
    IEnumerable<ClipEntry> VideoClips => Clips.Where(c => !c.Clip.IsCameraProxy);
    public int ClipCount => VideoClips.Count();
    public double Seconds => VideoClips.Sum(c => c.Info?.Duration ?? 0);
    public IReadOnlyList<string> Cameras =>
        VideoClips.Select(c => c.Info?.Camera?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList()!;
    /// LUT name → clips (no LUT and switched-off LUT are left out).
    public IReadOnlyDictionary<string, int> Luts =>
        VideoClips.Where(c => c.Lut is { Source: not "none" }).GroupBy(c => c.Lut!.Name).ToDictionary(g => g.Key, g => g.Count());
    public int ProxiesDone => Clips.Count(c => c.ProxyStatus == ProxyStatus.Done);
    public int ProxiesFailed => Clips.Count(c => c.ProxyStatus == ProxyStatus.Failed);
}

/// A day on one destination (spec 3.2): every folder in `<root>\<date>\`.
public sealed class DayModel
{
    public required string DayPath { get; init; }
    /// The date folder's name ("2026-10-06").
    public string Date => Path.GetFileName(DayPath.TrimEnd('\\', '/'));
    /// The destination the day is on.
    public string Root => Path.GetDirectoryName(DayPath.TrimEnd('\\', '/')) ?? DayPath;
    /// Every folder of the day in Explorer order — the table's rows.
    public required IReadOnlyList<DayCard> Folders { get; init; }
    /// The cards among them (ruling M13): what the tiles, "N / M" and the badge count.
    public IReadOnlyList<DayCard> Cards => Folders.Where(f => f.IsCard).ToList();
    public int VerifiedCards => Cards.Count(c => c.Record.Verified && c.Record.FailedFiles.Count == 0);
    /// The project in the header (spec 3.2): only when every card with a readable report names the same one (any case).
    public string? Project
    {
        get
        {
            var read = Cards.Where(c => c.Record.Generations.Count > 0).Select(c => c.Record.Project).ToList();
            return read.Count > 0 && read[0] is string first && read.All(p => string.Equals(p, first, StringComparison.OrdinalIgnoreCase)) ? first : null;
        }
    }
}

/// The day report (spec 3.2, R12): `<root>\<date>\_REPORT\<date>_day_report.html/.pdf` and the day's clip lists
/// `<date>_clips.csv/.ale`. Derived from the cards' reports — written whole every time (R25), never anything else in
/// that folder (plan 6's verify reports live there too), never anything in a card folder.
public static class DayReport
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string HtmlName(string date) => $"{date}_day_report.html";
    public static string PdfName(string date) => $"{date}_day_report.pdf";

    /// The folders of `dayPath` the report looks at: every subfolder but `_REPORT` and hidden ones. Reads only.
    static List<string> FoldersOf(string dayPath) =>
        Directory.GetDirectories(dayPath)
            .Where(d => Path.GetFileName(d) is var n && !n.StartsWith('.') && !string.Equals(n, Layout.ReportDirName, StringComparison.OrdinalIgnoreCase)
                        && (File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .ToList();

    static bool HasReport(string folder) => File.Exists(Path.Combine(folder, Layout.ReportDirName, CardRecord.MainReport));

    /// A folder an offload made (ruling M13): `_REPORT\` or `ascmhl\` in it.
    static bool IsCard(string folder) =>
        Directory.Exists(Path.Combine(folder, Layout.ReportDirName)) || Directory.Exists(Path.Combine(folder, Mhl.DirName));

    /// Throws (Czech message) unless `dayPath` may get a day report: it exists, it has at least one card folder with its
    /// report — so a destination root, a card folder or any other folder never gets a `_REPORT` — and its `_REPORT` is
    /// not itself a card folder (ruling M8: the day files would land in that card, outside its own `_REPORT`).
    public static void CheckDay(string dayPath)
    {
        dayPath = FullPath(dayPath);
        if (!Directory.Exists(dayPath)) throw new DirectoryNotFoundException(Msg.DayNotFound(dayPath));
        if (IsVolumeRoot(dayPath)) throw new InvalidOperationException(Msg.DayIsVolumeRoot(dayPath));
        var report = Path.Combine(dayPath, Layout.ReportDirName);
        if (HasReport(report) || Directory.Exists(Path.Combine(report, Mhl.DirName)))
            throw new InvalidOperationException(Msg.DayReportFolderIsCard(dayPath));
        if (!FoldersOf(dayPath).Any(HasReport)) throw new InvalidOperationException(Msg.NotADayFolder(dayPath));
    }

    /// The full path without a trailing separator — a root keeps its own ("E:\"; "E:" alone would be the current folder
    /// on E:, fix round 1, item 1).
    public static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// A drive or share root, or the folder a volume is mounted at: a destination root, never a day.
    static bool IsVolumeRoot(string full)
    {
        static bool Same(string? a, string b) => a != null && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
        return Same(Path.GetPathRoot(full), full) || Same(NativeIO.VolumeRoot(full), full);
    }

    /// The folders of `dayPath`, in Explorer order. Reads only.
    public static DayModel Load(string dayPath)
    {
        dayPath = FullPath(dayPath);
        var folders = FoldersOf(dayPath);
        folders.Sort((a, b) => Win32.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
        return new DayModel { DayPath = dayPath, Folders = folders.Select(f => new DayCard { Record = CardRecord.Load(f), IsCard = IsCard(f) }).ToList() };
    }

    /// The reports of a card that cannot be read, one Czech sentence each (the reason in parentheses).
    internal static IEnumerable<string> UnreadableTexts(CardRecord r)
    {
        if (r.Error is string error) yield return $"report nejde přečíst ({error})";
        foreach (var p in r.Problems) yield return $"report doplnění nejde přečíst ({p})";
    }

    /// "6. 10. 2026" for "2026-10-06"; the folder name itself when it is not a date.
    static string DateText(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d.ToString("d. M. yyyy", Cs) : date;

    static string Time(DateTime utc) => utc.ToLocalTime().ToString("H:mm", Cs);

    static string Files(int n) => $"{n} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");

    /// Self-contained HTML, A4 landscape when printed; no thumbnails.
    public static string Html(DayModel day, bool forPrint = false)
    {
        string E(string s) => HtmlReport.Esc(s);
        var cards = day.Cards;
        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Denní report {E(day.Date)}</title>\n<style>\n{HtmlReport.Styles(forPrint)}</style></head><body><main>\n");
        int verified = day.VerifiedCards;
        var (badge, badgeClass) = verified == cards.Count && cards.Count > 0 ? ("VŠE OVĚŘENO", "ok") : ("VYŽADUJE POZORNOST", "warn");
        h.Append($"<header><h1>Denní report — {E(DateText(day.Date))}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">Cíl: <span class=\"mono\">{E(day.Root)}</span></span>");
        if (day.Project is string project) h.Append($"<span class=\"sub\">Projekt: {E(project)}</span>");
        h.Append("</header>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, cards.Count.ToString(Inv), Texts.Plural(cards.Count, "karta", "karty", "karet"));
        Stat(h, cards.Sum(c => c.Record.TotalFiles).ToString(Inv), "souborů");
        Stat(h, Format.Bytes(cards.Sum(c => c.Record.TotalBytes)), "data");
        Stat(h, cards.Sum(c => c.ClipCount).ToString(Inv), "klipů");
        Stat(h, HtmlReport.Duration(cards.Sum(c => c.Seconds)), "celková délka");
        Stat(h, $"{verified} / {cards.Count}", "ověřené karty");
        h.Append("</div>\n\n");

        h.Append("<h2>Karty</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Karta</th><th>Čas</th><th class=\"num\">Soubory</th><th class=\"num\">Data</th><th class=\"num\">Klipy</th><th class=\"num\">Délka</th>");
        h.Append("<th>Kamery</th><th>Ověření</th><th class=\"num\">Chyby</th><th>Proxy</th><th>LUTy</th><th class=\"num\">Doplnění</th></tr>\n");
        foreach (var c in day.Folders)
        {
            var r = c.Record;
            if (!c.IsCard)
            {
                h.Append($"<tr><td>{E(c.Name)}</td><td colspan=\"11\" class=\"muted\">jiná složka (bez reportu)</td></tr>\n");
                continue;
            }
            if (c.NoReport || r.Generations.Count == 0)
            {
                var why = c.NoReport ? "bez reportu (probíhá, nebo byl přerušen)" : "report nejde přečíst";
                h.Append($"<tr><td>{E(c.Name)}</td><td colspan=\"11\" class=\"warn\">{why}</td></tr>\n");
                continue;
            }
            int failed = r.FailedFiles.Count;
            // Read back, but a file failed: not green (fix round 1, item 6).
            var (vClass, vText) = !r.Verified ? ("warn", "NEOVĚŘENO") : failed > 0 ? ("warn", "ověřeno, s chybami") : ("ok", "ověřeno");
            var proxies = c.ProxiesDone + c.ProxiesFailed == 0 ? "—" : $"{c.ProxiesDone} / {c.ProxiesFailed}";
            var lutNames = c.Luts.Count == 0 ? "—" : string.Join(", ", c.Luts.Keys);
            var generations = r.Generations.Count > 1 ? r.Generations.Count.ToString(Inv) : "—";
            h.Append($"<tr><td>{E(c.Name)}</td><td>{Time(r.Started!.Value)}–{Time(r.Finished!.Value)}</td>");
            h.Append($"<td class=\"num\">{r.TotalFiles}</td><td class=\"num\">{Format.Bytes(r.TotalBytes)}</td><td class=\"num\">{c.ClipCount}</td>");
            h.Append($"<td class=\"num\">{(c.Seconds > 0 ? HtmlReport.Duration(c.Seconds) : "—")}</td><td class=\"wrap\">{E(c.Cameras.Count == 0 ? "—" : string.Join(", ", c.Cameras))}</td>");
            h.Append($"<td class=\"{vClass}\">{vText}</td><td class=\"num {(failed > 0 ? "bad" : "muted")}\">{failed}</td>");
            h.Append($"<td>{proxies}</td><td class=\"wrap\">{E(lutNames)}</td><td class=\"num\">{generations}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        h.Append("<p class=\"sub\">Proxy: hotovo / selhalo. Doplnění: počet generací ASC MHL, když karta byla doplněna.</p>\n");

        var problems = Problems(day);
        if (problems.Count > 0)
        {
            h.Append($"<h2 class=\"warn\">Problémy ({problems.Count})</h2><div class=\"panel\"><table>\n");
            foreach (var (name, text, bad) in problems)
                h.Append($"<tr><td>{E(name)}</td><td class=\"{(bad ? "err" : "warn wrap")}\">{E(text)}</td></tr>\n");
            h.Append("</table></div>\n");
        }

        var luts = cards.SelectMany(c => c.Luts).GroupBy(kv => kv.Key).Select(g => (Name: g.Key, Clips: g.Sum(kv => kv.Value)))
            .OrderByDescending(x => x.Clips).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        if (luts.Count > 0)
        {
            h.Append("<h2>LUTy</h2><div class=\"panel\"><table>\n<tr><th>LUT</th><th class=\"num\">Klipy</th></tr>\n");
            foreach (var (name, clips) in luts) h.Append($"<tr><td>{E(name)}</td><td class=\"num\">{clips}</td></tr>\n");
            h.Append("</table></div>\n");
        }
        h.Append($"<footer>offload {E(OffloadVersion.String)} · denní report · {HtmlReport.DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    /// The Problems section: unreadable reports, card folders without one, failed files (count + the first five), cards
    /// not verified. A folder that is no card (ruling M13) is none. `Bad`: red.
    public static IReadOnlyList<(string Card, string Text, bool Bad)> Problems(DayModel day)
    {
        var list = new List<(string, string, bool)>();
        foreach (var c in day.Cards)
        {
            if (c.NoReport)
            {
                list.Add((c.Name, "bez reportu (probíhá, nebo byl přerušen)", false));
                continue;
            }
            var unreadable = UnreadableTexts(c.Record).ToList();
            foreach (var text in unreadable) list.Add((c.Name, text, true));
            if (c.Record.Generations.Count == 0) continue;
            var failed = c.Record.FailedFiles;
            if (failed.Count > 0)
                list.Add((c.Name, $"{Files(failed.Count)} {Texts.Plural(failed.Count, "selhal", "selhaly", "selhalo")}: "
                    + string.Join(", ", failed.Take(5)) + (failed.Count > 5 ? ", …" : ""), true));
            if (!c.Record.Verified && unreadable.Count == 0) list.Add((c.Name, "neověřeno — kopie nebyla přečtena zpět", false));
        }
        return list;
    }

    /// Writes the HTML, the PDF (best effort, logged) and the day's clip lists into `<day>\_REPORT`, each swapped in
    /// with ReplaceFile (a reader with delete sharing — a browser, the Mac over SMB — does not stop it). Returns the
    /// HTML path. Throws when the folder is no day (CheckDay), cannot be read, or the HTML cannot be written.
    public static string Write(string dayPath, OutputSettings outputs, Action<string>? log = null)
    {
        dayPath = FullPath(dayPath);
        CheckDay(dayPath);
        var day = Load(dayPath);
        var dir = Path.Combine(dayPath, Layout.ReportDirName);
        NativeIO.Mkdirs(dir);
        var html = Path.Combine(dir, HtmlName(day.Date));
        NativeIO.WriteFileReplacing(html, new UTF8Encoding(false).GetBytes(Html(day)));
        try
        {
            ClipList.Write(dir, day.Date, day.Cards.SelectMany(c => ClipList.Rows(c.Record)).ToList(), outputs);
        }
        catch (Exception e)
        {
            log?.Invoke($"day clip list {dir}: {e.Message}");
        }
        try
        {
            WritePdf(day, Path.Combine(dir, PdfName(day.Date)));
        }
        catch (Exception e)
        {
            log?.Invoke($"day report PDF {dir}: {e.Message}");   // the HTML is there; the PDF is a convenience copy
        }
        return html;
    }

    /// Edge prints into a folder of its own under %TEMP%; the result is swapped in with ReplaceFile — a plain
    /// rename-over is denied while someone reads the earlier PDF.
    static void WritePdf(DayModel day, string path)
    {
        var work = Path.Combine(Path.GetTempPath(), "offload-day-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var printed = Path.Combine(work, Path.GetFileName(path));
            PdfReport.WriteHtml(Html(day, forPrint: true), printed);
            NativeIO.WriteFileReplacing(path, File.ReadAllBytes(printed));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
