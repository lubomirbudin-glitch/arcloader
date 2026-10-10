using System.Globalization;
using System.Text;
using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Outputs;

/// One backup the user picked: an entry of the card history, or a card folder (on a destination, or its local copy).
public sealed record SummaryInput(HistoryEntry? Entry, string? Folder)
{
    public static SummaryInput Of(HistoryEntry entry) => new(entry, null);
    public static SummaryInput Of(string cardFolder) => new(null, cardFolder);
}

/// Where the numbers of a card come from: its folder on a destination, the copy of its report on this computer
/// (`Offload reporty`, the disk went away), or nothing but the history entry.
public enum SummarySource { Destination, LocalCopy, None }

/// One copy of the backup: the destination card folder, whether it is connected now, and whether it was verified —
/// read on the disk (`Basis` empty), or what the report or the history says about a disk that is not connected.
public sealed record SummaryCopy(string Root, string CardPath, bool Connected, bool? Verified, string Basis);

/// The first or the last clapper: the clip and its start timecode (ALEXA: time of day).
public sealed record SummaryClap(string Clip, string Card, string Date, string? Timecode, bool TimeOfDay, DateTime? Recorded);

/// One backup in the summary report.
public sealed class SummaryCard
{
    public required string Name { get; init; }
    /// The shooting day: the date folder ("2026-10-06").
    public required string Date { get; init; }
    public HistoryEntry? Entry { get; init; }
    /// Every generation of the card's reports (full copy, appends, repairs); null when none could be found.
    public CardRecord? Record { get; init; }
    public SummarySource Source { get; init; }
    /// The folder the reports were read from (a destination card folder or the local copy).
    public string? ReadFrom { get; init; }
    /// The copy of the card's report on this computer (`<Offload reporty>\<datum>\<karta>`), null when there is none.
    public string? LocalCopy { get; init; }
    public IReadOnlyList<SummaryCopy> Copies { get; init; } = [];

    public bool HasReport => Record is { Generations.Count: > 0 };
    IEnumerable<ClipEntry> AllClips => Record?.Clips.Select(c => c.Entry) ?? [];
    /// The clips the camera recorded: camera proxies (Sony SUB, PROXY folders) are not counted.
    internal IReadOnlyList<ClipEntry> VideoClips => AllClips.Where(c => !c.Clip.IsCameraProxy).ToList();
    public int ClipCount => VideoClips.Count;
    /// From the reports; without one, what the history entry recorded.
    public int Files => HasReport ? Record!.TotalFiles : Entry?.Files ?? 0;
    public long Bytes => HasReport ? Record!.TotalBytes : Entry?.Bytes ?? 0;
    /// Running time: the sum of the clips' durations.
    public double Seconds => VideoClips.Sum(c => c.Info?.Duration ?? 0);
    public IReadOnlyList<string> Cameras => SummaryReport.CamerasOf(VideoClips);
    /// Every copy was verified (the shared definition, CardRecord.ReadBack) — never without a report.
    public bool Verified => HasReport && (Copies.Count > 0 ? Copies.All(c => c.Verified == true) : Record!.Verified);
    public SummaryClap? First => SummaryReport.Claps([this]).First;
    public SummaryClap? Last => SummaryReport.Claps([this]).Last;
}

/// A shooting day, or all the cards (Date "").
public sealed class SummaryGroup
{
    public required string Date { get; init; }
    public required IReadOnlyList<SummaryCard> Cards { get; init; }
    public int ClipCount => Cards.Sum(c => c.ClipCount);
    public int Files => Cards.Sum(c => c.Files);
    public long Bytes => Cards.Sum(c => c.Bytes);
    public double Seconds => Cards.Sum(c => c.Seconds);
    public IReadOnlyList<string> Cameras => SummaryReport.CamerasOf(Cards.SelectMany(c => c.VideoClips));
    public int VerifiedCards => Cards.Count(c => c.Verified);
    public int WithoutReport => Cards.Count(c => !c.HasReport);
    public SummaryClap? First => SummaryReport.Claps(Cards).First;
    public SummaryClap? Last => SummaryReport.Claps(Cards).Last;
}

public sealed class SummaryModel
{
    public required IReadOnlyList<SummaryCard> Cards { get; init; }
    public IReadOnlyList<SummaryGroup> Days => Cards.GroupBy(c => c.Date).OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => new SummaryGroup { Date = g.Key, Cards = g.ToList() }).ToList();
    public SummaryGroup Total => new() { Date = "", Cards = Cards };
}

/// What Save wrote: the local HTML (always), its PDF (null when Edge could not print it), the copies on the disk the user
/// picked, or why they could not be written there.
public sealed record SummaryFiles(string Html, string? Pdf, string? ExtraHtml, string? ExtraPdf, string? ExtraError);

/// Task "Souhrnný report z vybraných záloh" (2026-10-09): one HTML + PDF (no thumbnails) from backups picked in the card
/// history — cards of different days and disks. Read from the cards' reports on the destinations (every generation),
/// else from their copy on this computer (LocalReports), else only what history.json says — never an error. Reads only;
/// writes nothing but `<Offload reporty>\souhrn\` and, when the user picks one, a folder on a disk (never over a file).
public static class SummaryReport
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string FolderName = "souhrn";

    /// Where the summary is saved on this computer: the folder of the local report copies (setting
    /// `outputs.localReportFolder`, default `<Dokumenty>\Offload reporty`) — also when that copy is switched off.
    public static string LocalRoot(OutputSettings o) =>
        string.IsNullOrWhiteSpace(o.LocalReportFolder) ? LocalReports.DefaultRoot : o.LocalReportFolder.Trim();

    /// Every folder the local report copies may be in: the default one and each profile's (projects may have their own).
    public static IReadOnlyList<string> LocalRoots(AppSettings s) =>
        new[] { LocalReports.DefaultRoot }.Concat(new SettingsProfile[] { s }.Concat(s.Projects).Select(p => LocalRoot(p.Outputs)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    static bool HasReport(string folder)
    {
        try { return File.Exists(Path.Combine(folder, Layout.ReportDirName, CardRecord.MainReport)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    /// The command line's arguments: history ids, card folders, or date folders (every card in them). Throws
    /// ArgumentException (Czech) on anything else.
    public static IReadOnlyList<SummaryInput> Inputs(IEnumerable<string> args, IReadOnlyList<HistoryEntry> history)
    {
        var list = new List<SummaryInput>();
        foreach (var arg in args)
        {
            if (history.FirstOrDefault(e => string.Equals(e.Id, arg, StringComparison.OrdinalIgnoreCase)) is { } entry)
            {
                list.Add(SummaryInput.Of(entry));
                continue;
            }
            if (!Directory.Exists(arg)) throw new ArgumentException(Msg.SummaryUnknown(arg));
            var full = DayReport.FullPath(arg);
            if (HasReport(full))
            {
                list.Add(SummaryInput.Of(full));
                continue;
            }
            var cards = Directory.GetDirectories(full).Where(HasReport).ToList();
            if (cards.Count == 0) throw new ArgumentException(Msg.SummaryNotACard(full));
            cards.Sort((a, b) => Win32.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
            list.AddRange(cards.Select(SummaryInput.Of));
        }
        return list;
    }

    /// The picked backups, by day and card name. `localRoots`: where the local report copies may be. Reads only.
    public static SummaryModel Load(IReadOnlyList<SummaryInput> inputs, IReadOnlyList<string> localRoots)
    {
        var cards = new List<SummaryCard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            var card = input.Entry is { } entry ? FromEntry(entry, localRoots) : FromFolder(input.Folder!, localRoots);
            // The same backup picked twice (an entry and its folder): once.
            var key = card.ReadFrom ?? card.Entry?.Id ?? card.Name + "|" + card.Date;
            if (seen.Add(key)) cards.Add(card);
        }
        cards.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date) is var d && d != 0 ? d : Win32.StrCmpLogicalW(a.Name, b.Name));
        return new SummaryModel { Cards = cards };
    }

    static SummaryCard FromEntry(HistoryEntry e, IReadOnlyList<string> localRoots)
    {
        var copies = e.Destinations.Select(d => (d.Root, d.CardPath)).ToList();
        var folderName = copies.Select(c => Path.GetFileName(c.CardPath.TrimEnd('\\', '/'))).FirstOrDefault(n => n.Length > 0) ?? e.CardName;
        return Build(e, copies, null, folderName, e.Date, localRoots);
    }

    static SummaryCard FromFolder(string folder, IReadOnlyList<string> localRoots)
    {
        folder = DayReport.FullPath(folder);
        var record = CardRecord.Load(folder);
        var newest = record.Generations.Count > 0 ? record.Generations[^1].Report : null;
        var copies = (newest?.Destinations ?? []).Select(d => (d.Root, d.CardPath)).ToList();
        bool isLocal = File.Exists(Path.Combine(folder, LocalReports.Marker));
        // A destination folder the report names differently (another letter, a Mac report): it is a copy too.
        if (!isLocal && !copies.Any(c => SamePath(c.CardPath, folder)))
            copies.Insert(0, (Path.GetDirectoryName(Path.GetDirectoryName(folder)) ?? folder, folder));
        var date = record.Generations.FirstOrDefault()?.Report.Date ?? Path.GetFileName(Path.GetDirectoryName(folder)) ?? "";
        return Build(null, copies, isLocal ? folder : null, Path.GetFileName(folder), date, localRoots);
    }

    /// `copies`: the destination card folders of the backup; `localFolder`: the local copy the user picked itself.
    static SummaryCard Build(HistoryEntry? entry, List<(string Root, string CardPath)> copies, string? localFolder, string folderName, string date,
        IReadOnlyList<string> localRoots)
    {
        var connected = copies.Where(c => HasReport(c.CardPath)).Select(c => c.CardPath).ToList();
        CardRecord? record = null;
        string? readFrom = null;
        var source = SummarySource.None;
        var records = new Dictionary<string, CardRecord>(StringComparer.OrdinalIgnoreCase);
        CardRecord LoadAt(string path) => records.TryGetValue(path, out var r) ? r : records[path] = CardRecord.Load(path);
        if (connected.Count > 0 && (LocalReports.Newest(connected) ?? connected[0]) is var best)
        {
            record = LoadAt(best);
            readFrom = best;
            source = SummarySource.Destination;
        }
        var local = localFolder ?? FindLocal(localRoots, date, folderName, record, entry);
        if (record == null && local != null)
        {
            record = CardRecord.Load(local);
            readFrom = local;
            source = SummarySource.LocalCopy;
        }
        var summaryCopies = copies.Select(c =>
        {
            bool on = connected.Contains(c.CardPath, StringComparer.OrdinalIgnoreCase);
            if (on) return new SummaryCopy(c.Root, c.CardPath, true, LoadAt(c.CardPath).Verified, "");
            if (record is { Generations.Count: > 0 }) return new SummaryCopy(c.Root, c.CardPath, false, VerifiedAt(record, c.CardPath), "podle reportu");
            return new SummaryCopy(c.Root, c.CardPath, false, entry is { Unfinished: false } e ? e.Verified : null, entry != null ? "podle historie" : "");
        }).ToList();
        var name = readFrom != null && source == SummarySource.Destination ? Path.GetFileName(readFrom) : folderName;
        if (string.IsNullOrEmpty(date)) date = record?.Generations.FirstOrDefault()?.Report.Date ?? "";
        return new SummaryCard
        {
            Name = name, Date = date, Entry = entry, Record = record, Source = source, ReadFrom = readFrom, LocalCopy = local, Copies = summaryCopies,
        };
    }

    /// The shared "verified" definition (CardRecord.ReadBack) for a destination card folder that is not connected now:
    /// the report's own column of that folder (its recorded serial, so the column is found by disk and path).
    static bool VerifiedAt(CardRecord r, string cardPath) =>
        r.Generations.Count > 0 && r.Problems.Count == 0 && !r.Incomplete
        && (r.Files.Count == 0 ? r.Generations.All(g => g.Report.Verified)
            : r.Files.Values.All(f => CardRecord.ReadBack(f, cardPath, f.Report.Destinations.FirstOrDefault(d => SamePath(d.CardPath, cardPath))?.VolumeSerial)));

    static bool SamePath(string a, string b) => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// The local copy of this backup: `<root>\<date>\<folder>` or `…_2`, `_3` (LocalReports' rule) whose marker names it —
    /// exactly (the report's start, card and source) when a report was read, else by card and source from the history,
    /// the one whose reports ended when the history's generations did when several match.
    static string? FindLocal(IReadOnlyList<string> roots, string date, string folderName, CardRecord? record, HistoryEntry? entry)
    {
        if (date.Length == 0 || folderName.Length == 0) return null;
        var main = record?.Generations.FirstOrDefault(g => g.Generation == 1)?.Report;
        var origin = main != null ? LocalReports.Origin(main) : null;
        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var day = Path.Combine(root, date);
            string[] dirs;
            try
            {
                if (!Directory.Exists(day)) continue;
                dirs = Directory.GetDirectories(day);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { continue; }
            foreach (var dir in dirs)
            {
                var n = Path.GetFileName(dir);
                bool named = string.Equals(n, folderName, StringComparison.OrdinalIgnoreCase)
                    || (n.Length > folderName.Length + 1 && n.StartsWith(folderName + "_", StringComparison.OrdinalIgnoreCase) && n[(folderName.Length + 1)..].All(char.IsAsciiDigit));
                if (!named || !HasReport(dir)) continue;
                string marker;
                try { marker = File.ReadAllText(Path.Combine(dir, LocalReports.Marker), Encoding.UTF8).Trim(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                if (origin != null ? string.Equals(marker, origin, StringComparison.Ordinal) : entry != null && MarkerFits(marker, entry))
                    candidates.Add(dir);
            }
        }
        if (candidates.Count <= 1 || entry == null || origin != null) return candidates.FirstOrDefault();
        var ends = entry.Generations.Select(g => g.Finished).Append(entry.Finished).ToList();
        return candidates.FirstOrDefault(c => CardRecord.Load(c).Generations.Any(g => ends.Any(t => Math.Abs((g.Report.Finished - t).TotalSeconds) <= 2)))
            ?? candidates[0];
    }

    /// `<started>|<cardName>|<source>`: the card and the source of the history entry.
    static bool MarkerFits(string marker, HistoryEntry e)
    {
        var parts = marker.Split('|');
        return parts.Length == 3 && string.Equals(parts[1], e.CardName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2].TrimEnd('\\', '/'), e.Source.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> CamerasOf(IEnumerable<ClipEntry> clips) =>
        clips.Select(c => c.Info?.Camera?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).ToList()!;

    /// ALEXA records time-of-day timecode: its start TC is the time of the clapper.
    static bool TimeOfDay(ClipEntry c) =>
        c.Clip.Format == ClipFormat.Arriraw || c.Info?.Camera?.Contains("ALEXA", StringComparison.OrdinalIgnoreCase) == true;

    /// The first and the last recorded clip of `cards` (decision of 2026-10-09): ordered by the camera's record time when
    /// every clip has one, otherwise by the shooting day and the clip number (A001C001 … A001C010, Explorer order).
    internal static (SummaryClap? First, SummaryClap? Last) Claps(IEnumerable<SummaryCard> cards)
    {
        var clips = cards.SelectMany(card => card.VideoClips.Select(clip => (card, clip))).ToList();
        if (clips.Count == 0) return (null, null);
        bool byTime = clips.All(c => c.clip.Info?.Recorded != null);
        clips.Sort((a, b) =>
        {
            int d = byTime ? Nullable.Compare(a.clip.Info!.Recorded, b.clip.Info!.Recorded) : string.CompareOrdinal(a.card.Date, b.card.Date);
            if (d == 0) d = Win32.StrCmpLogicalW(a.clip.Clip.Name, b.clip.Clip.Name);
            return d != 0 ? d : string.CompareOrdinal(a.clip.Clip.Path, b.clip.Clip.Path);
        });
        static SummaryClap Of((SummaryCard Card, ClipEntry Clip) c) =>
            new(c.Clip.Clip.Name, c.Card.Name, c.Card.Date, string.IsNullOrWhiteSpace(c.Clip.Info?.Timecode) ? null : c.Clip.Info!.Timecode!.Trim(),
                TimeOfDay(c.Clip), c.Clip.Info?.Recorded);
        return (Of(clips[0]), Of(clips[^1]));
    }

    // MARK: HTML

    /// "6. 10. 2026" for "2026-10-06"; the text itself when it is not a date.
    static string DateText(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d.ToString("d. M. yyyy", Cs) : date;

    static string Stat(string value, string label) => $"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n";

    static string ClapCell(SummaryClap? c, bool withDate)
    {
        if (c == null) return "<td class=\"muted\">—</td>";
        var tc = c.Timecode is string t ? HtmlReport.Esc(t) : "<span class=\"muted\">bez TC</span>";
        var note = (withDate ? DateText(c.Date) + " · " : "") + c.Clip + (c.TimeOfDay ? " · TC denní doby" : c.Timecode != null ? " · TC" : "");
        return $"<td class=\"nw\"><span class=\"mono\">{tc}</span><div class=\"note\">{HtmlReport.Esc(note)}</div></td>";
    }

    static (string Class, string Text) State(SummaryCard c)
    {
        if (!c.HasReport)
        {
            if (c.Entry is not { } e) return ("warn", "neznámý");
            return e.Unfinished ? ("warn", "nedokončeno (historie)") : !e.Success ? ("bad", "s chybami (historie)")
                : !e.Verified ? ("warn", "neověřeno (historie)") : ("ok", "ověřeno (historie)");
        }
        var r = c.Record!;
        return r.VerifyPending ? ("warn", "OVĚŘUJE SE") : r.Incomplete ? ("warn", "NEDOKONČENO") : !c.Verified ? ("warn", "NEOVĚŘENO")
            : r.FailedFiles.Count > 0 ? ("warn", "ověřeno, s chybami") : ("ok", "ověřeno");
    }

    static string SourceText(SummaryCard c) => c.Source switch
    {
        SummarySource.Destination => "z disku",
        SummarySource.LocalCopy => "kopie reportu na počítači",
        _ => "report není k dispozici",
    };

    static string Cameras(IReadOnlyList<string> cameras) => cameras.Count == 0 ? "—" : string.Join(", ", cameras);

    static string Seconds(double s) => s > 0 ? HtmlReport.Duration(s) : "—";

    /// Self-contained HTML, A4 landscape when printed (PdfReport zooms anything wider to the page); no thumbnails.
    /// `pdfName`: the PDF next to it, for the toolbar's link.
    public static string Html(SummaryModel model, bool forPrint = false, string? pdfName = null, DateTime? createdLocal = null)
    {
        static string E(string s) => HtmlReport.Esc(s);
        var total = model.Total;
        var days = model.Days;
        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Souhrnný report</title>\n<style>\n{HtmlReport.Styles(forPrint)}</style></head><body><main>\n");
        h.Append(HtmlReport.Toolbar(pdfName ?? "souhrn.pdf", forPrint));
        int cards = total.Cards.Count;
        var (badge, badgeClass) = cards > 0 && total.VerifiedCards == cards ? ("VŠE OVĚŘENO", "ok") : ("VYŽADUJE POZORNOST", "warn");
        var dayList = days.Count == 0 ? "" : days.Count == 1 ? DateText(days[0].Date) : $"{DateText(days[0].Date)} – {DateText(days[^1].Date)}";
        h.Append($"<header><h1>Souhrnný report{(dayList.Length > 0 ? " — " + E(dayList) : "")}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">Natáčecí {Texts.Plural(days.Count, "den", "dny", "dnů")}: {E(string.Join(", ", days.Select(d => DateText(d.Date))))}</span>");
        var projects = total.Cards.Select(c => c.Record?.Project ?? c.Entry?.Project).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (projects.Count > 0) h.Append($"<span class=\"sub\">{(projects.Count == 1 ? "Projekt" : "Projekty")}: {E(string.Join(", ", projects!))}</span>");
        h.Append("</header>\n");

        h.Append("<div class=\"stats\">\n");
        h.Append(Stat(cards.ToString(Inv), Texts.Plural(cards, "karta", "karty", "karet")));
        h.Append(Stat(days.Count.ToString(Inv), "natáčecí " + Texts.Plural(days.Count, "den", "dny", "dnů")));
        h.Append(Stat(total.ClipCount.ToString(Inv), "klipů"));
        h.Append(Stat(total.Files.ToString(Inv), "souborů"));
        h.Append(Stat(Format.Bytes(total.Bytes), "data"));
        h.Append(Stat(HtmlReport.Duration(total.Seconds), "stopáž"));
        h.Append(Stat($"{total.VerifiedCards} / {cards}", "ověřené karty"));
        h.Append("</div>\n");
        if (total.WithoutReport > 0)
            h.Append($"<p class=\"sub warn\">{total.WithoutReport} {Texts.Plural(total.WithoutReport, "karta nemá", "karty nemají", "karet nemá")} report k dispozici: "
                + "soubory a velikost podle historie, klipy a stopáž bez nich.</p>\n");

        const string Head = "<th class=\"num\">Klipy</th><th class=\"num\">Soubory</th><th class=\"num\">Data</th><th class=\"num\">Stopáž</th><th>První klapka</th><th>Poslední klapka</th><th>Kamery</th>";
        string Numbers(int clips, int files, long bytes, double seconds) =>
            $"<td class=\"num\">{clips}</td><td class=\"num\">{files}</td><td class=\"num nw\">{Format.Bytes(bytes)}</td><td class=\"num\">{Seconds(seconds)}</td>";

        h.Append("\n<h2>Natáčecí dny</h2><div class=\"panel\"><table>\n");
        h.Append($"<tr><th>Den</th><th class=\"num\">Karty</th>{Head}</tr>\n");
        foreach (var d in days)
        {
            h.Append($"<tr><td class=\"nw\">{E(DateText(d.Date))}</td><td class=\"num\">{d.Cards.Count}</td>{Numbers(d.ClipCount, d.Files, d.Bytes, d.Seconds)}");
            h.Append($"{ClapCell(d.First, false)}{ClapCell(d.Last, false)}<td class=\"wrap\">{E(Cameras(d.Cameras))}</td></tr>\n");
        }
        h.Append("</table></div>\n");

        h.Append("\n<h2>Karty</h2><div class=\"panel\"><table>\n");
        h.Append($"<tr><th>Karta</th><th>Den</th>{Head}<th>Ověření</th><th>Údaje</th></tr>\n");
        foreach (var c in model.Cards)
        {
            var (vClass, vText) = State(c);
            h.Append($"<tr><td class=\"nw\"><b>{E(c.Name)}</b></td><td class=\"nw\">{E(DateText(c.Date))}</td>");
            if (c.HasReport) h.Append(Numbers(c.ClipCount, c.Files, c.Bytes, c.Seconds) + ClapCell(c.First, false) + ClapCell(c.Last, false));
            else h.Append($"<td class=\"num muted\">—</td><td class=\"num\">{c.Files}</td><td class=\"num nw\">{Format.Bytes(c.Bytes)}</td><td class=\"num muted\">—</td><td class=\"muted\">—</td><td class=\"muted\">—</td>");
            h.Append($"<td class=\"wrap\">{E(Cameras(c.Cameras))}</td><td class=\"{vClass}\">{vText}</td>");
            h.Append($"<td class=\"{(c.Source == SummarySource.None ? "warn" : c.Source == SummarySource.LocalCopy ? "muted" : "")}\">{SourceText(c)}</td></tr>\n");
        }
        h.Append($"<tr><td class=\"nw\"><b>Celkem</b></td><td class=\"nw\">{days.Count} {Texts.Plural(days.Count, "den", "dny", "dnů")}</td>{Numbers(total.ClipCount, total.Files, total.Bytes, total.Seconds)}");
        h.Append($"{ClapCell(total.First, days.Count > 1)}{ClapCell(total.Last, days.Count > 1)}<td class=\"wrap\">{E(Cameras(total.Cameras))}</td>");
        h.Append($"<td>{total.VerifiedCards} / {cards} ověřeno</td><td></td></tr>\n");
        h.Append("</table></div>\n");
        h.Append("<p class=\"sub\">Klapka = začátek prvního a posledního nahraného klipu (pořadí podle času záznamu z metadat kamery, "
            + "jinak podle čísla klipu); čas = startovní timecode klipu, u ALEXY TC denní doby. Stopáž = součet délek klipů.</p>\n");

        h.Append("\n<h2>Kde je co zálohované</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Karta</th><th>Disk</th><th>Cesta</th><th>Disk připojen</th><th>Ověřeno</th><th>Kopie reportu na počítači</th></tr>\n");
        foreach (var c in model.Cards)
        {
            List<SummaryCopy?> rows = c.Copies.Count == 0 ? [null] : [.. c.Copies];
            var span = rows.Count > 1 ? $" rowspan=\"{rows.Count}\"" : "";
            for (int i = 0; i < rows.Count; i++)
            {
                h.Append("<tr>");
                if (i == 0) h.Append($"<td{span}><b>{E(c.Name)}</b><div class=\"note\">{E(DateText(c.Date))}</div></td>");
                if (rows[i] is { } copy)
                {
                    var (vc, vt) = copy.Verified switch { true => ("ok", "ano"), false => ("warn", "ne"), _ => ("muted", "nezjištěno") };
                    if (copy.Basis.Length > 0) vt += $" <span class=\"note\">({copy.Basis})</span>";
                    h.Append($"<td class=\"mono\">{E(copy.Root)}</td><td class=\"mono\">{E(copy.CardPath)}</td>");
                    h.Append($"<td class=\"{(copy.Connected ? "ok" : "muted")}\">{(copy.Connected ? "ano" : "nepřipojen")}</td><td class=\"{vc}\">{vt}</td>");
                }
                else h.Append("<td colspan=\"4\" class=\"muted\">cíle zálohy nejsou známé</td>");
                if (i == 0) h.Append($"<td{span} class=\"mono\">{(c.LocalCopy is string l ? E(l) : "<span class=\"muted\">není</span>")}</td>");
                h.Append("</tr>\n");
            }
        }
        h.Append("</table></div>\n");

        var notes = Notes(model);
        if (notes.Count > 0)
        {
            h.Append($"\n<h2 class=\"warn\">Upozornění ({notes.Count})</h2><div class=\"panel\"><table>\n");
            foreach (var (card, text, bad) in notes)
                h.Append($"<tr><td>{E(card)}</td><td class=\"{(bad ? "err" : "warn wrap")}\">{E(text)}</td></tr>\n");
            h.Append("</table></div>\n");
        }
        h.Append($"<footer>offload {E(OffloadVersion.String)} · souhrnný report · {HtmlReport.DateTimeText(createdLocal ?? DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    /// Cards without a report, read from the local copy, with unreadable reports, failed files, not verified.
    public static IReadOnlyList<(string Card, string Text, bool Bad)> Notes(SummaryModel model)
    {
        var list = new List<(string, string, bool)>();
        foreach (var c in model.Cards)
        {
            var name = $"{c.Name} ({DateText(c.Date)})";
            if (c.Source == SummarySource.None)
                list.Add((name, "report není k dispozici — disk se zálohou není připojený a kopie reportu na počítači chybí; "
                    + (c.Entry != null ? "soubory, velikost a ověření podle historie" : "údaje chybí"), false));
            else if (c.Source == SummarySource.LocalCopy)
                list.Add((name, "disk se zálohou není připojený — údaje z kopie reportu na počítači", false));
            if (c.Record is not { } r) continue;
            foreach (var text in DayReport.UnreadableTexts(r)) list.Add((name, text, true));
            if (r.Generations.Count == 0) continue;
            var failed = r.FailedFiles;
            if (failed.Count > 0)
                list.Add((name, $"{failed.Count} {Texts.Plural(failed.Count, "soubor selhal", "soubory selhaly", "souborů selhalo")}: "
                    + string.Join(", ", failed.Take(5)) + (failed.Count > 5 ? ", …" : ""), true));
            if (!c.Verified && !r.VerifyPending) list.Add((name, "neověřeno na všech discích — kartu neformátujte, dokud záloha není ověřená", false));
        }
        return list;
    }

    // MARK: Saving

    /// `<datum>_<čas>_souhrn` of `now`.
    public static string BaseName(DateTime nowLocal) => nowLocal.ToString("yyyy-MM-dd'_'HHmmss", Inv) + "_souhrn";

    /// `name`, `name_2`, `name_3` … — the first whose HTML and PDF are both free in `dir`.
    static string FreeName(string dir, string name)
    {
        for (int n = 1; ; n++)
        {
            var candidate = n == 1 ? name : $"{name}_{n}";
            if (!File.Exists(Path.Combine(dir, candidate + ".html")) && !File.Exists(Path.Combine(dir, candidate + ".pdf"))) return candidate;
        }
    }

    /// Throws (Czech) unless the summary may be written into `folder` (picked by the user): it exists, it is not in a card
    /// folder of a backup (`_REPORT\offload_report.json` or `ascmhl` there or above) and not on a card being offloaded
    /// (`cards`: the sources).
    public static void CheckTarget(string folder, IEnumerable<string>? cards = null)
    {
        var full = DayReport.FullPath(folder);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(Msg.SummaryTargetMissing(full));
        foreach (var card in cards ?? [])
        {
            var c = DayReport.FullPath(card);
            if (string.Equals(full, c, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(Path.TrimEndingDirectorySeparator(c) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Msg.SummaryTargetIsSource(full));
        }
        for (var dir = full; dir != null; dir = Path.GetDirectoryName(dir))
            if (HasReport(dir) || Directory.Exists(Path.Combine(dir, Mhl.DirName)))
                throw new InvalidOperationException(Msg.SummaryTargetIsCard(full));
    }

    /// Writes the summary into `<localRoot>\souhrn\<datum>_<čas>_souhrn.html/.pdf` and, with `extraFolder`, the same into
    /// that folder; nothing is ever overwritten (`_2`, `_3` …). The PDF is best effort (logged); a failure on the picked
    /// disk is returned (ExtraError), the local copy stays. Throws when the local HTML cannot be written.
    public static SummaryFiles Save(SummaryModel model, string localRoot, string? extraFolder, Action<string>? log = null, DateTime? nowLocal = null,
        IEnumerable<string>? cards = null)
    {
        var now = nowLocal ?? DateTime.Now;
        var name = BaseName(now);
        var dir = Path.Combine(localRoot, FolderName);
        NativeIO.Mkdirs(dir);
        var local = FreeName(dir, name);
        var html = Path.Combine(dir, local + ".html");
        NativeIO.WriteFileNew(html, new UTF8Encoding(false).GetBytes(Html(model, pdfName: local + ".pdf", createdLocal: now)));
        string? pdf = Path.Combine(dir, local + ".pdf");
        try
        {
            PdfReport.WriteHtml(Html(model, forPrint: true, createdLocal: now), pdf, replace: false);
        }
        catch (Exception e)
        {
            log?.Invoke($"summary report PDF {pdf}: {e.Message}");   // the HTML is there; the PDF is a convenience copy
            pdf = null;
        }
        if (string.IsNullOrWhiteSpace(extraFolder)) return new SummaryFiles(html, pdf, null, null, null);
        try
        {
            CheckTarget(extraFolder, cards);
            var target = DayReport.FullPath(extraFolder);
            var free = FreeName(target, name);
            var extraHtml = Path.Combine(target, free + ".html");
            NativeIO.WriteFileNew(extraHtml, new UTF8Encoding(false).GetBytes(Html(model, pdfName: free + ".pdf", createdLocal: now)));
            string? extraPdf = null;
            if (pdf != null)
            {
                extraPdf = Path.Combine(target, free + ".pdf");
                NativeIO.WriteFileNew(extraPdf, File.ReadAllBytes(pdf));
            }
            return new SummaryFiles(html, pdf, extraHtml, extraPdf, null);
        }
        catch (Exception e)
        {
            log?.Invoke($"summary report on {extraFolder}: {e.Message}");
            return new SummaryFiles(html, pdf, null, null, e.Message);
        }
    }
}
