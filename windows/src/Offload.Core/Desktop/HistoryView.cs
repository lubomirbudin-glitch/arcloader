using System.Globalization;
using Offload.Core.History;

namespace Offload.Core.Desktop;

/// One card in "DNES" and in "Historie karet": name, size, time, "✓ 2 kopie ověřeny".
public sealed record HistoryRow(string Id, string CardName, string Size, string Time, string Status, HistoryTone Tone, string? Project);

/// One day of the history tab: "Dnes", "Včera", "6. 10. 2026" with its summary.
public sealed record HistoryDay(string Title, string Summary, IReadOnlyList<HistoryRow> Rows);

/// Today's history in the right column (UI návrh, nová věc 7) — from history.json as the window already loads it.
public static class HistoryView
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// The history tab shows at most this many entries (history.json keeps up to 5 000).
    public const int MaxEntries = 300;

    public static HistoryRow Row(HistoryEntry e)
    {
        int copies = e.Destinations.Count;
        var (status, tone) = e.Unfinished ? ("! nedokončeno", HistoryTone.Warning)
            : !e.Success
            ? ($"✕ s chybami ({e.FailedFiles ?? 0} {Texts.Plural(e.FailedFiles ?? 0, "soubor", "soubory", "souborů")})", HistoryTone.Error)
            : !e.Verified ? ("! neověřeno · neformátovat", HistoryTone.Warning)
            : ($"✓ {copies} {Texts.Plural(copies, "kopie ověřena", "kopie ověřeny", "kopií ověřeno")} · lze formátovat", HistoryTone.Ok);
        return new HistoryRow(e.Id, e.CardName, Format.BytesCs3(e.Bytes), e.Finished.ToLocalTime().ToString("H:mm", Cs), status, tone, e.Project);
    }

    /// "3 karty · 412 GB · vše ověřeno", "2 karty · 18,0 GB · 1 karta s chybami", "1 karta · 2,00 GB · 1 neověřená".
    public static string Summary(IReadOnlyList<HistoryEntry> entries)
    {
        int n = entries.Count;
        var parts = new List<string> { $"{n} {Texts.Plural(n, "karta", "karty", "karet")}", Format.BytesCs3(entries.Sum(e => e.Bytes)) };
        // R6, ruling I5: an unfinished backup is neither verified nor failed — its own count.
        int unfinished = entries.Count(e => e.Unfinished);
        int failed = entries.Count(e => !e.Unfinished && !e.Success), unverified = entries.Count(e => !e.Unfinished && e.Success && !e.Verified);
        if (failed == 0 && unverified == 0 && unfinished == 0) parts.Add("vše ověřeno");
        if (unfinished > 0) parts.Add($"{unfinished} {Texts.Plural(unfinished, "nedokončená", "nedokončené", "nedokončených")}");
        if (failed > 0) parts.Add($"{failed} {Texts.Plural(failed, "karta", "karty", "karet")} s chybami");
        if (unverified > 0) parts.Add($"{unverified} {Texts.Plural(unverified, "neověřená", "neověřené", "neověřených")}");
        return string.Join(" · ", parts);
    }

    /// The cards finished today (local time), newest first; null when there are none (the section hides).
    public static HistoryDay? Today(IEnumerable<HistoryEntry> entries, DateTime nowLocal) =>
        Days(entries, nowLocal).FirstOrDefault(d => d.Title == "Dnes");

    /// Every day with a card, newest first (at most MaxEntries cards in all): "Dnes", "Včera", then "6. 10. 2026".
    public static IReadOnlyList<HistoryDay> Days(IEnumerable<HistoryEntry> entries, DateTime nowLocal)
    {
        var today = nowLocal.Date;
        return entries.OrderByDescending(e => e.Finished).Take(MaxEntries)
            .GroupBy(e => e.Finished.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var list = g.ToList();
                var title = g.Key == today ? "Dnes" : g.Key == today.AddDays(-1) ? "Včera" : g.Key.ToString("d. M. yyyy", Cs);
                return new HistoryDay(title, Summary(list), list.Select(Row).ToList());
            })
            .ToList();
    }
}
