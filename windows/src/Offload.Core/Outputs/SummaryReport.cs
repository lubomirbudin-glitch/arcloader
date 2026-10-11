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
/// (`mmMover reports`, the disk went away), or nothing but the history entry.
public enum SummarySource { Destination, LocalCopy, None }

/// One copy of the backup: the destination card folder, whether it is connected now, and whether it was verified —
/// read on the disk (`Basis` empty), or what the report or the history says about a disk that is not connected.
public sealed record SummaryCopy(string Root, string CardPath, bool Connected, bool? Verified, string Basis);

/// The first or the last clapper: the clip and its start timecode (ALEXA: time of day).
public sealed record SummaryClap(string Clip, string Card, string Date, string? Timecode, bool TimeOfDay, DateTime? Recorded)
{
    /// The clip's path in the card folder — the last tie-break of the order.
    public string ClipPath { get; init; } = "";
}

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
    /// The copy of the card's report on this computer (`<mmMover reports>\<datum>\<karta>`), null when there is none.
    public string? LocalCopy { get; init; }
    public IReadOnlyList<SummaryCopy> Copies { get; init; } = [];

    public bool HasReport => Record is { Generations.Count: > 0 };
    IReadOnlyList<ClipEntry>? videoClips;
    /// The clips the camera recorded: camera proxies (Sony SUB, PROXY folders) are not counted. Worked out once.
    internal IReadOnlyList<ClipEntry> VideoClips => videoClips ??=
        HasReport ? Record!.Clips.Select(c => c.Entry).Where(c => !c.Clip.IsCameraProxy).ToList() : [];
    public int ClipCount => VideoClips.Count;
    /// From the reports; without one, what the history entry recorded.
    public int Files => HasReport ? Record!.TotalFiles : Entry?.Files ?? 0;
    public long Bytes => HasReport ? Record!.TotalBytes : Entry?.Bytes ?? 0;
    /// Running time: the sum of the clips' durations.
    public double Seconds => VideoClips.Sum(c => c.Info?.Duration ?? 0);
    public IReadOnlyList<string> Cameras => SummaryReport.CamerasOf(VideoClips);
    /// Every copy was verified (the shared definition, CardRecord.ReadBack) — never without a report.
    public bool Verified => HasReport && (Copies.Count > 0 ? Copies.All(c => c.Verified == true) : Record!.Verified);
    ClapSet? claps;
    internal ClapSet Claps => claps ??= ClapSet.Of(this);
    public SummaryClap? First => Claps.First;
    public SummaryClap? Last => Claps.Last;
}

/// The candidates for the first and the last clapper of a card, both ways (record time; day + clip number) — a day or the
/// whole summary picks among its cards' candidates, so nothing is sorted (S-9).
internal sealed class ClapSet
{
    public bool AllTimed { get; private init; }
    public SummaryClap? FirstByTime { get; private init; }
    public SummaryClap? LastByTime { get; private init; }
    public SummaryClap? FirstByNumber { get; private init; }
    public SummaryClap? LastByNumber { get; private init; }
    public SummaryClap? First => AllTimed ? FirstByTime : FirstByNumber;
    public SummaryClap? Last => AllTimed ? LastByTime : LastByNumber;

    public static ClapSet Of(SummaryCard card)
    {
        var clips = card.VideoClips.Select(c => SummaryReport.ClapOf(card, c)).ToList();
        if (clips.Count == 0) return new ClapSet();
        bool timed = clips.All(c => c.Recorded != null);
        return new ClapSet
        {
            AllTimed = timed,
            FirstByTime = timed ? Min(clips, SummaryReport.ByTime) : null,
            LastByTime = timed ? Max(clips, SummaryReport.ByTime) : null,
            FirstByNumber = Min(clips, SummaryReport.ByNumber),
            LastByNumber = Max(clips, SummaryReport.ByNumber),
        };
    }

    /// The first and the last of `cards` together: by record time when every clip of every card has one (decision of
    /// 2026-10-09), otherwise by day and clip number.
    public static (SummaryClap? First, SummaryClap? Last) Of(IReadOnlyList<SummaryCard> cards)
    {
        var sets = cards.Select(c => c.Claps).Where(s => s.FirstByNumber != null).ToList();
        if (sets.Count == 0) return (null, null);
        return sets.All(s => s.AllTimed)
            ? (Min(sets.Select(s => s.FirstByTime!), SummaryReport.ByTime), Max(sets.Select(s => s.LastByTime!), SummaryReport.ByTime))
            : (Min(sets.Select(s => s.FirstByNumber!), SummaryReport.ByNumber), Max(sets.Select(s => s.LastByNumber!), SummaryReport.ByNumber));
    }

    static SummaryClap Min(IEnumerable<SummaryClap> clips, Comparison<SummaryClap> by) => clips.Aggregate((a, b) => by(b, a) < 0 ? b : a);
    static SummaryClap Max(IEnumerable<SummaryClap> clips, Comparison<SummaryClap> by) => clips.Aggregate((a, b) => by(b, a) > 0 ? b : a);
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
    (SummaryClap? First, SummaryClap? Last)? claps;
    (SummaryClap? First, SummaryClap? Last) Claps => claps ??= ClapSet.Of(Cards);
    public SummaryClap? First => Claps.First;
    public SummaryClap? Last => Claps.Last;
}

public sealed class SummaryModel
{
    public required IReadOnlyList<SummaryCard> Cards { get; init; }
    IReadOnlyList<SummaryGroup>? days;
    SummaryGroup? total;
    public IReadOnlyList<SummaryGroup> Days => days ??= Cards.GroupBy(c => c.Date).OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => new SummaryGroup { Date = g.Key, Cards = g.ToList() }).ToList();
    public SummaryGroup Total => total ??= new() { Date = "", Cards = Cards };
}

/// The volume a target folder is on, as CheckTarget looks at it (tests replace it: OverrideVolume).
public sealed record SummaryVolume(string Root, DriveType Type, bool ReadOnly);

/// What Save wrote: the local HTML (always), its PDF (null when Edge could not print it), the copies on the disk the user
/// picked, or why they could not be written there.
public sealed record SummaryFiles(string Html, string? Pdf, string? ExtraHtml, string? ExtraPdf, string? ExtraError);

/// Task "Souhrnný report z vybraných záloh" (2026-10-09): one HTML + PDF (no thumbnails) from backups picked in the card
/// history — cards of different days and disks. Read from the cards' reports on the destinations (every generation),
/// else from their copy on this computer (LocalReports), else only what history.json says — never an error. Reads only;
/// writes nothing but `<mmMover reports>\summary\` and, when the user picks one, a folder on a disk (never over a file).
public static class SummaryReport
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string FolderName = "summary";

    /// Where the summary is saved on this computer: the folder of the local report copies (setting
    /// `outputs.localReportFolder`, default `<Documents>\mmMover reports`) — also when that copy is switched off.
    public static string LocalRoot(OutputSettings o) =>
        string.IsNullOrWhiteSpace(o.LocalReportFolder) ? LocalReports.DefaultRoot : o.LocalReportFolder.Trim();

    /// Every folder the local report copies may be in: the default one, the defaults of earlier versions
    /// (`Offload reports`, `Offload reporty`, still read) and each profile's (projects may have their own).
    public static IReadOnlyList<string> LocalRoots(AppSettings s) =>
        new[] { LocalReports.DefaultRoot }.Concat(LocalReports.OldDefaultRoots).Concat(new SettingsProfile[] { s }.Concat(s.Projects).Select(p => LocalRoot(p.Outputs)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    static bool HasReport(string folder)
    {
        try { return File.Exists(Path.Combine(folder, Layout.ReportDirName, CardRecord.MainReport)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    /// The command line's arguments: history ids, card folders, or date folders (every card in them). Throws
    /// ArgumentException on anything else.
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
    /// One card is one card (review S-3): backups of the same card (the history's identity — the shape of its files, and
    /// its name) are joined — counted once, every copy listed.
    public static SummaryModel Load(IReadOnlyList<SummaryInput> inputs, IReadOnlyList<string> localRoots)
    {
        var roots = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);   // each destination root is looked at once
        var groups = new List<(HashSet<string> Keys, List<SummaryCard> Cards)>();
        foreach (var input in inputs)
        {
            var card = input.Entry is { } entry ? FromEntry(entry, localRoots, roots) : FromFolder(input.Folder!, localRoots, roots);
            var keys = Identity(card);
            var hits = groups.Where(g => g.Keys.Overlaps(keys)).ToList();
            if (hits.Count == 0)
            {
                groups.Add((keys, [card]));
                continue;
            }
            // A card that joins several groups (an entry known by its shape, a folder by its report) joins them all.
            var into = hits[0];
            into.Keys.UnionWith(keys);
            into.Cards.Add(card);
            foreach (var other in hits.Skip(1))
            {
                into.Keys.UnionWith(other.Keys);
                into.Cards.AddRange(other.Cards);
                groups.Remove(other);
            }
        }
        var cards = groups.Select(g => Join(g.Cards)).ToList();
        cards.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date) is var d && d != 0 ? d : Win32.StrCmpLogicalW(a.Name, b.Name));
        return new SummaryModel { Cards = cards };
    }

    /// `relpath\tsize` lines of every file the card's reports record — the history's `shape` (CardFingerprint); null
    /// without a readable report or files.
    public static string? ShapeOf(CardRecord record) =>
        record.Generations.Count == 0 || record.Files.Count == 0 ? null
            : CardFingerprint.Compute(record.Files.Values.Select(f => new SourceFile(f.File.Path, f.File.Size, f.File.Modified))).Shape;

    /// How a card is known again: its shape and its name (from the report and from the history entry), the folder it was
    /// read from, the history id.
    static HashSet<string> Identity(SummaryCard c)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = c.Record?.Generations.FirstOrDefault()?.Report.CardName ?? c.Entry?.CardName ?? c.Name;
        if (c.Record is { } r && ShapeOf(r) is string shape) keys.Add($"shape|{shape}|{name}");
        if (c.Entry is { Shape.Length: > 0 } e) keys.Add($"shape|{e.Shape}|{e.CardName}");
        if (c.ReadFrom != null) keys.Add("path|" + Path.TrimEndingDirectorySeparator(c.ReadFrom));
        if (c.Entry != null) keys.Add("id|" + c.Entry.Id);
        if (keys.Count == 0) keys.Add($"card|{c.Name}|{c.Date}|{Guid.NewGuid():N}");
        return keys;
    }

    /// Backups of one card as one: numbers from the best read one (a destination, then the local copy, then the one with
    /// more generations), every copy of every backup (once per folder).
    static SummaryCard Join(List<SummaryCard> cards)
    {
        if (cards.Count == 1) return cards[0];
        var best = cards.OrderBy(c => c.Source switch { SummarySource.Destination => 0, SummarySource.LocalCopy => 1, _ => 2 })
            .ThenByDescending(c => c.Record?.Generations.Count ?? 0).First();
        var copies = new List<SummaryCopy>();
        foreach (var copy in new[] { best }.Concat(cards.Where(c => c != best)).SelectMany(c => c.Copies))
            if (!copies.Any(c => SamePath(c.CardPath, copy.CardPath))) copies.Add(copy);
        return new SummaryCard
        {
            Name = best.Name, Date = best.Date, Entry = best.Entry ?? cards.Select(c => c.Entry).FirstOrDefault(e => e != null), Record = best.Record,
            Source = best.Source, ReadFrom = best.ReadFrom, LocalCopy = best.LocalCopy ?? cards.Select(c => c.LocalCopy).FirstOrDefault(l => l != null),
            Copies = copies,
        };
    }

    static SummaryCard FromEntry(HistoryEntry e, IReadOnlyList<string> localRoots, Dictionary<string, bool> roots)
    {
        var copies = e.Destinations.Select(d => (d.Root, d.CardPath)).ToList();
        var folderName = copies.Select(c => Path.GetFileName(c.CardPath.TrimEnd('\\', '/'))).FirstOrDefault(n => n.Length > 0) ?? e.CardName;
        return Build(e, copies, null, folderName, e.Date, localRoots, roots);
    }

    static SummaryCard FromFolder(string folder, IReadOnlyList<string> localRoots, Dictionary<string, bool> roots)
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
        return Build(null, copies, isLocal ? folder : null, Path.GetFileName(folder), date, localRoots, roots);
    }

    /// The folder's report is there — when its root is reachable (looked at once per root: a network path that is gone
    /// costs its timeout once, not once per card).
    static bool Reachable(string cardPath, Dictionary<string, bool> roots)
    {
        string? root;
        try { root = Path.IsPathFullyQualified(cardPath) ? Path.GetPathRoot(cardPath) : null; }
        catch (ArgumentException) { root = null; }
        if (root == null) return false;
        if (!roots.TryGetValue(root, out var up)) roots[root] = up = Directory.Exists(root);
        return up && HasReport(cardPath);
    }

    /// `copies`: the destination card folders of the backup; `localFolder`: the local copy the user picked itself.
    /// The reports are read from a connected destination (the newest first); one that cannot be read gives way to the next,
    /// then to the local copy, then to none (review S-5) — its reason stays in the record for the notes.
    static SummaryCard Build(HistoryEntry? entry, List<(string Root, string CardPath)> copies, string? localFolder, string folderName, string date,
        IReadOnlyList<string> localRoots, Dictionary<string, bool> roots)
    {
        var connected = copies.Where(c => Reachable(c.CardPath, roots)).Select(c => c.CardPath).ToList();
        CardRecord? record = null, unreadable = null;
        string? readFrom = null;
        var source = SummarySource.None;
        var records = new Dictionary<string, CardRecord>(StringComparer.OrdinalIgnoreCase);
        CardRecord LoadAt(string path) => records.TryGetValue(path, out var r) ? r : records[path] = CardRecord.Load(path);
        var newest = connected.Count > 0 ? LocalReports.Newest(connected) : null;
        foreach (var path in connected.OrderBy(p => SamePath(p, newest ?? "") ? 0 : 1))
        {
            var r = LoadAt(path);
            if (r.Generations.Count == 0)
            {
                unreadable ??= r;
                continue;
            }
            record = r;
            readFrom = path;
            source = SummarySource.Destination;
            break;
        }
        var local = localFolder ?? FindLocal(localRoots, date, folderName, record, entry);
        if (record == null && local != null && CardRecord.Load(local) is { Generations.Count: > 0 } copy)
        {
            record = copy;
            readFrom = local;
            source = SummarySource.LocalCopy;
        }
        record ??= unreadable;
        var summaryCopies = copies.Select(c =>
        {
            bool on = connected.Contains(c.CardPath, StringComparer.OrdinalIgnoreCase);
            if (on) return new SummaryCopy(c.Root, c.CardPath, true, LoadAt(c.CardPath).Verified, "");
            if (record is { Generations.Count: > 0 }) return new SummaryCopy(c.Root, c.CardPath, false, VerifiedAt(record, c.CardPath), "per the report");
            return new SummaryCopy(c.Root, c.CardPath, false, entry is { Unfinished: false } e ? e.Verified : null, entry != null ? "per the history" : "");
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
    /// exactly (the report's start, card and source) when a report was read; otherwise (review S-4) only the one copy whose
    /// marker fits the history's card and source AND whose reports ended when the history's generations did — never a
    /// guess: none, or several, is "not found".
    static string? FindLocal(IReadOnlyList<string> roots, string date, string folderName, CardRecord? record, HistoryEntry? entry)
    {
        if (date.Length == 0 || folderName.Length == 0) return null;
        var main = record?.Generations.FirstOrDefault(g => g.Generation == 1)?.Report;
        var origin = main != null ? LocalReports.Origin(main) : null;
        if (origin == null && entry == null) return null;
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
                if (origin != null ? string.Equals(marker, origin, StringComparison.Ordinal) : MarkerFits(marker, entry!))
                    candidates.Add(dir);
            }
        }
        if (origin != null) return candidates.FirstOrDefault();   // the same backup: any of its copies
        var ends = entry!.Generations.Select(g => g.Finished).Append(entry.Finished).ToList();
        var fitting = candidates.Where(c => CardRecord.Load(c).Generations.Any(g => ends.Any(t => Math.Abs((g.Report.Finished - t).TotalSeconds) <= 2))).ToList();
        return fitting.Count == 1 ? fitting[0] : null;
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

    /// A clip of `card` as a clapper candidate: its start TC, and its record time when that is plausible (S-7).
    internal static SummaryClap ClapOf(SummaryCard card, ClipEntry c) =>
        new(c.Clip.Name, card.Name, card.Date, string.IsNullOrWhiteSpace(c.Info?.Timecode) ? null : c.Info!.Timecode!.Trim(), TimeOfDay(c),
            ClipInfo.Plausible(c.Info?.Recorded))
        { ClipPath = c.Clip.Path };

    /// By the camera's record time, then the clip number.
    internal static int ByTime(SummaryClap a, SummaryClap b) =>
        Nullable.Compare(a.Recorded, b.Recorded) is var d && d != 0 ? d : ByNumber(a, b);

    /// By the shooting day, then the clip number (Explorer order: A001C002 before A001C010), then the path.
    internal static int ByNumber(SummaryClap a, SummaryClap b)
    {
        int d = string.CompareOrdinal(a.Date, b.Date);
        if (d == 0) d = Win32.StrCmpLogicalW(a.Clip, b.Clip);
        return d != 0 ? d : string.CompareOrdinal(a.ClipPath, b.ClipPath);
    }

    /// The first and the last recorded clip of `cards` (decision of 2026-10-09): ordered by the camera's record time when
    /// every clip has one, otherwise by the shooting day and the clip number (A001C001 … A001C010, Explorer order).
    internal static (SummaryClap? First, SummaryClap? Last) Claps(IReadOnlyList<SummaryCard> cards) => ClapSet.Of(cards);

    // MARK: HTML

    /// "2026-10-06" for "2026-10-06" (checked as a date); the text itself when it is not a date.
    static string DateText(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", Inv) : date;

    static string Stat(string value, string label) => $"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n";

    static string ClapCell(SummaryClap? c, bool withDate)
    {
        if (c == null) return "<td class=\"muted\">—</td>";
        var tc = c.Timecode is string t ? HtmlReport.Esc(t) : "<span class=\"muted\">no TC</span>";
        var note = (withDate ? DateText(c.Date) + " · " : "") + c.Clip + (c.TimeOfDay ? " · time-of-day TC" : c.Timecode != null ? " · TC" : "");
        return $"<td class=\"nw\"><span class=\"mono\">{tc}</span><div class=\"note\">{HtmlReport.Esc(note)}</div></td>";
    }

    static (string Class, string Text) State(SummaryCard c)
    {
        if (!c.HasReport)
        {
            if (c.Entry is not { } e) return ("warn", "unknown");
            return e.Unfinished ? ("warn", "unfinished (history)") : !e.Success ? ("bad", "with errors (history)")
                : !e.Verified ? ("warn", "not verified (history)") : ("ok", "verified (history)");
        }
        var r = c.Record!;
        return r.VerifyPending ? ("warn", "VERIFYING") : r.Incomplete ? ("warn", "UNFINISHED") : !c.Verified ? ("warn", "NOT VERIFIED")
            : r.FailedFiles.Count > 0 ? ("warn", "verified, with errors") : ("ok", "verified");
    }

    static string SourceText(SummaryCard c) => c.Source switch
    {
        SummarySource.Destination => "from the disk",
        SummarySource.LocalCopy => "report copy on this computer",
        _ => "report not available",
    };

    static string Cameras(IReadOnlyList<string> cameras) => cameras.Count == 0 ? "—" : string.Join(", ", cameras);

    static string Seconds(double s) => s > 0 ? HtmlReport.Duration(s) : "—";

    /// Self-contained HTML, A4 landscape when printed (PdfReport zooms anything wider to the page); no thumbnails.
    /// `pdfName`: the PDF next to it, for the toolbar's link; null = there is none, no link.
    public static string Html(SummaryModel model, bool forPrint = false, string? pdfName = null, DateTime? createdLocal = null)
    {
        static string E(string s) => HtmlReport.Esc(s);
        var total = model.Total;
        var days = model.Days;
        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Summary report</title>\n<style>\n{HtmlReport.Styles(forPrint)}</style></head><body><main>\n");
        // Without a PDF next to it (Edge missing, printing failed): no link to a file that is not there (S-10).
        h.Append(pdfName != null || forPrint ? HtmlReport.Toolbar(pdfName ?? "", forPrint)
            : "<div class=\"toolbar\"><button type=\"button\" onclick=\"window.print()\">Save as PDF</button>"
              + "<span class=\"hint\">in the print dialog choose \u201cSave as PDF\u201d</span></div>\n");
        int cards = total.Cards.Count;
        var (badge, badgeClass) = cards > 0 && total.VerifiedCards == cards ? ("ALL VERIFIED", "ok") : ("NEEDS ATTENTION", "warn");
        var dayList = days.Count == 0 ? "" : days.Count == 1 ? DateText(days[0].Date) : $"{DateText(days[0].Date)} – {DateText(days[^1].Date)}";
        h.Append($"<header><h1>Summary report{(dayList.Length > 0 ? " — " + E(dayList) : "")}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">Shooting {Texts.Plural(days.Count, "day", "days")}: {E(string.Join(", ", days.Select(d => DateText(d.Date))))}</span>");
        var projects = total.Cards.Select(c => c.Record?.Project ?? c.Entry?.Project).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (projects.Count > 0) h.Append($"<span class=\"sub\">{(projects.Count == 1 ? "Project" : "Projects")}: {E(string.Join(", ", projects!))}</span>");
        h.Append("</header>\n");

        h.Append("<div class=\"stats\">\n");
        h.Append(Stat(cards.ToString(Inv), Texts.Plural(cards, "card", "cards")));
        h.Append(Stat(days.Count.ToString(Inv), "shooting " + Texts.Plural(days.Count, "day", "days")));
        h.Append(Stat(total.ClipCount.ToString(Inv), "clips"));
        h.Append(Stat(total.Files.ToString(Inv), "files"));
        h.Append(Stat(Format.Bytes(total.Bytes), "data"));
        h.Append(Stat(HtmlReport.Duration(total.Seconds), "duration"));
        h.Append(Stat($"{total.VerifiedCards} / {cards}", "verified cards"));
        h.Append("</div>\n");
        if (total.WithoutReport > 0)
            h.Append($"<p class=\"sub warn\">{Texts.N(total.WithoutReport, "card has", "cards have")} no report available: "
                + "files and size per the history, clips and duration without them.</p>\n");

        const string Head = "<th class=\"num\">Clips</th><th class=\"num\">Files</th><th class=\"num\">Data</th><th class=\"num\">Duration</th><th>First slate</th><th>Last slate</th><th>Cameras</th>";
        string Numbers(int clips, int files, long bytes, double seconds) =>
            $"<td class=\"num\">{clips}</td><td class=\"num\">{files}</td><td class=\"num nw\">{Format.Bytes(bytes)}</td><td class=\"num\">{Seconds(seconds)}</td>";

        h.Append("\n<h2>Shooting days</h2><div class=\"panel\"><table>\n");
        h.Append($"<tr><th>Day</th><th class=\"num\">Cards</th>{Head}</tr>\n");
        foreach (var d in days)
        {
            h.Append($"<tr><td class=\"nw\">{E(DateText(d.Date))}</td><td class=\"num\">{d.Cards.Count}</td>{Numbers(d.ClipCount, d.Files, d.Bytes, d.Seconds)}");
            h.Append($"{ClapCell(d.First, false)}{ClapCell(d.Last, false)}<td class=\"wrap\">{E(Cameras(d.Cameras))}</td></tr>\n");
        }
        h.Append("</table></div>\n");

        h.Append("\n<h2>Cards</h2><div class=\"panel\"><table>\n");
        h.Append($"<tr><th>Card</th><th>Day</th>{Head}<th>Verification</th><th>Data from</th></tr>\n");
        foreach (var c in model.Cards)
        {
            var (vClass, vText) = State(c);
            h.Append($"<tr><td class=\"nw\"><b>{E(c.Name)}</b></td><td class=\"nw\">{E(DateText(c.Date))}</td>");
            if (c.HasReport) h.Append(Numbers(c.ClipCount, c.Files, c.Bytes, c.Seconds) + ClapCell(c.First, false) + ClapCell(c.Last, false));
            else h.Append($"<td class=\"num muted\">—</td><td class=\"num\">{c.Files}</td><td class=\"num nw\">{Format.Bytes(c.Bytes)}</td><td class=\"num muted\">—</td><td class=\"muted\">—</td><td class=\"muted\">—</td>");
            h.Append($"<td class=\"wrap\">{E(Cameras(c.Cameras))}</td><td class=\"{vClass}\">{vText}</td>");
            h.Append($"<td class=\"{(c.Source == SummarySource.None ? "warn" : c.Source == SummarySource.LocalCopy ? "muted" : "")}\">{SourceText(c)}</td></tr>\n");
        }
        h.Append($"<tr><td class=\"nw\"><b>Total</b></td><td class=\"nw\">{Texts.N(days.Count, "day")}</td>{Numbers(total.ClipCount, total.Files, total.Bytes, total.Seconds)}");
        h.Append($"{ClapCell(total.First, days.Count > 1)}{ClapCell(total.Last, days.Count > 1)}<td class=\"wrap\">{E(Cameras(total.Cameras))}</td>");
        h.Append($"<td>{total.VerifiedCards} / {cards} verified</td><td></td></tr>\n");
        h.Append("</table></div>\n");
        h.Append("<p class=\"sub\">Slate = the start of the first and the last recorded clip (ordered by the record time from the camera metadata, "
            + "otherwise by the clip number); time = the clip's start timecode, time-of-day TC on an ALEXA. Duration = the sum of the clip lengths.</p>\n");

        h.Append("\n<h2>Where everything is backed up</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Card</th><th>Disk</th><th>Path</th><th>Disk connected</th><th>Verified</th><th>Report copy on this computer</th></tr>\n");
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
                    var (vc, vt) = copy.Verified switch { true => ("ok", "yes"), false => ("warn", "no"), _ => ("muted", "unknown") };
                    if (copy.Basis.Length > 0) vt += $" <span class=\"note\">({copy.Basis})</span>";
                    h.Append($"<td class=\"mono\">{E(copy.Root)}</td><td class=\"mono\">{E(copy.CardPath)}</td>");
                    h.Append($"<td class=\"{(copy.Connected ? "ok" : "muted")}\">{(copy.Connected ? "yes" : "not connected")}</td><td class=\"{vc}\">{vt}</td>");
                }
                else h.Append("<td colspan=\"4\" class=\"muted\">the backup's destinations are not known</td>");
                if (i == 0) h.Append($"<td{span} class=\"mono\">{(c.LocalCopy is string l ? E(l) : "<span class=\"muted\">none</span>")}</td>");
                h.Append("</tr>\n");
            }
        }
        h.Append("</table></div>\n");

        var notes = Notes(model);
        if (notes.Count > 0)
        {
            h.Append($"\n<h2 class=\"warn\">Warnings ({notes.Count})</h2><div class=\"panel\"><table>\n");
            foreach (var (card, text, bad) in notes)
                h.Append($"<tr><td>{E(card)}</td><td class=\"{(bad ? "err" : "warn wrap")}\">{E(text)}</td></tr>\n");
            h.Append("</table></div>\n");
        }
        h.Append($"<footer>{Product.Name} {E(OffloadVersion.String)} · summary report · {HtmlReport.DateTimeText(createdLocal ?? DateTime.Now)}</footer></main></body></html>\n");
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
                list.Add((name, "report not available — the backup disk is not connected and there is no report copy on this computer; "
                    + (c.Entry != null ? "files, size and verification per the history" : "no data"), false));
            else if (c.Source == SummarySource.LocalCopy)
                list.Add((name, "the backup disk is not connected — data from the report copy on this computer", false));
            if (c.Record is not { } r) continue;
            foreach (var text in DayReport.UnreadableTexts(r)) list.Add((name, text, true));
            if (r.Generations.Count == 0) continue;
            var failed = r.FailedFiles;
            if (failed.Count > 0)
                list.Add((name, $"{Texts.N(failed.Count, "file")} failed: "
                    + string.Join(", ", failed.Take(5)) + (failed.Count > 5 ? ", …" : ""), true));
            if (!c.Verified && !r.VerifyPending) list.Add((name, "not verified on all disks — do not format the card until the backup is verified", false));
        }
        return list;
    }

    // MARK: Saving

    /// `<datum>_<čas>_souhrn` of `now`.
    public static string BaseName(DateTime nowLocal) => nowLocal.ToString("yyyy-MM-dd'_'HHmmss", Inv) + "_summary";

    /// `name`, `name_2`, `name_3` … — the first whose HTML and PDF are both free in `dir`.
    static string FreeName(string dir, string name)
    {
        for (int n = 1; ; n++)
        {
            var candidate = n == 1 ? name : $"{name}_{n}";
            if (!File.Exists(Path.Combine(dir, candidate + ".html")) && !File.Exists(Path.Combine(dir, candidate + ".pdf"))) return candidate;
        }
    }

    /// Tests: the volume CheckTarget looks at instead of the real one (per async flow, so parallel tests never see it).
    static readonly AsyncLocal<Func<string, SummaryVolume>?> volumeOverride = new();

    internal static IDisposable OverrideVolume(Func<string, SummaryVolume> probe)
    {
        volumeOverride.Value = probe;
        return new VolumeScope();
    }

    sealed class VolumeScope : IDisposable
    {
        public void Dispose() => volumeOverride.Value = null;
    }

    /// The volume `full` is on: its root (a mount point too), its type and whether it is read-only.
    static SummaryVolume VolumeOf(string full)
    {
        if (volumeOverride.Value is { } probe) return probe(full);
        var root = NativeIO.VolumeRoot(full) ?? Path.GetPathRoot(full) ?? full;
        DriveType type;
        try { type = new DriveInfo(root).DriveType; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { type = DriveType.Unknown; }
        return new SummaryVolume(root, type, NativeIO.IsReadOnlyVolume(root));
    }

    static bool IsDate(string? name) =>
        name != null && DateTime.TryParseExact(name, "yyyy-MM-dd", Inv, DateTimeStyles.None, out _);

    /// Throws (Czech, why) unless the summary may be written into `folder`, which the user picked (review S-1, S-2):
    /// - it exists;
    /// - it is in no backup's card folder, also one still being copied or stopped: no folder from it up to the root holds
    ///   `_REPORT` or `ascmhl` (ruling M13 — an empty `_REPORT` is there from the copy's start; a day folder's own `_REPORT`
    ///   does not count), and it is not inside a dated backup tree `<YYYY-MM-DD>\<karta>\…`;
    /// - it is not on a card being offloaded (`cards`: the sources);
    /// - its volume is no camera card (the camera's structure at its root — Volumes.CameraAtRoot, the automatic destinations' rule,
    ///   unless Offload's own output is on it: a backup disk, as for an automatic destination), not removable media (a card
    ///   in a reader, a flash drive) and not read-only.
    /// Reads only — never on the UI thread.
    public static void CheckTarget(string folder, IEnumerable<string>? cards = null)
    {
        var full = DayReport.FullPath(folder);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(Msg.SummaryTargetMissing(full));
        for (var dir = full; dir != null; dir = Path.GetDirectoryName(dir))
        {
            bool cardFolder = !IsDate(Path.GetFileName(dir))
                && (Directory.Exists(Path.Combine(dir, Layout.ReportDirName)) || Directory.Exists(Path.Combine(dir, Mhl.DirName)));
            if (cardFolder || IsDate(Path.GetFileName(Path.GetDirectoryName(dir)))) throw new InvalidOperationException(Msg.SummaryTargetIsCard(full));
        }
        foreach (var card in cards ?? [])
        {
            var c = DayReport.FullPath(card);
            if (string.Equals(full, c, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(Path.TrimEndingDirectorySeparator(c) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Msg.SummaryTargetIsSource(full));
        }
        var volume = VolumeOf(full);
        if (volume.Type == DriveType.Removable) throw new InvalidOperationException(Msg.SummaryTargetRemovable(full));
        if (volume.ReadOnly) throw new InvalidOperationException(Msg.SummaryTargetReadOnly(full));
        if (Volumes.CameraAtRoot(volume.Root) && !Volumes.HasOffloadOutput(volume.Root))
            throw new InvalidOperationException(Msg.SummaryTargetCameraCard(full));
    }

    /// Writes the summary into `<localRoot>\summary\<date>_<time>_summary.html/.pdf` and, with `extraFolder`, the same into
    /// that folder (CheckTarget first); nothing is ever overwritten (`_2`, `_3` …). The PDF is best effort (logged) and
    /// printed first, so an HTML links to its PDF only when there is one (S-10). A failure on the picked disk is returned
    /// (ExtraError), the local copy stays. Throws when the local HTML cannot be written.
    public static SummaryFiles Save(SummaryModel model, string localRoot, string? extraFolder, Action<string>? log = null, DateTime? nowLocal = null,
        IEnumerable<string>? cards = null)
    {
        var now = nowLocal ?? DateTime.Now;
        var name = BaseName(now);
        var dir = Path.Combine(localRoot, FolderName);
        NativeIO.Mkdirs(dir);
        var local = FreeName(dir, name);
        string? pdf = Path.Combine(dir, local + ".pdf");
        try
        {
            PdfReport.WriteHtml(Html(model, forPrint: true, createdLocal: now), pdf, replace: false);
        }
        catch (Exception e)
        {
            log?.Invoke($"summary report PDF {pdf}: {e.Message}");   // the HTML is still made; the PDF is a convenience copy
            pdf = null;
        }
        var html = Path.Combine(dir, local + ".html");
        NativeIO.WriteFileNew(html, new UTF8Encoding(false).GetBytes(Html(model, pdfName: pdf != null ? local + ".pdf" : null, createdLocal: now)));
        if (string.IsNullOrWhiteSpace(extraFolder)) return new SummaryFiles(html, pdf, null, null, null);
        try
        {
            CheckTarget(extraFolder, cards);
            var target = DayReport.FullPath(extraFolder);
            var free = FreeName(target, name);
            string? extraPdf = null;
            if (pdf != null)
            {
                extraPdf = Path.Combine(target, free + ".pdf");
                NativeIO.WriteFileNew(extraPdf, File.ReadAllBytes(pdf));
            }
            var extraHtml = Path.Combine(target, free + ".html");
            NativeIO.WriteFileNew(extraHtml, new UTF8Encoding(false).GetBytes(Html(model, pdfName: extraPdf != null ? free + ".pdf" : null, createdLocal: now)));
            return new SummaryFiles(html, pdf, extraHtml, extraPdf, null);
        }
        catch (Exception e)
        {
            log?.Invoke($"summary report on {extraFolder}: {e.Message}");
            return new SummaryFiles(html, pdf, null, null, e.Message);
        }
    }
}
