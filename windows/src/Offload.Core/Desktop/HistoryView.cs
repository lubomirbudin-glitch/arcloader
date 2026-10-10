using System.Globalization;
using Offload.Core.History;

namespace Offload.Core.Desktop;

/// One card in "TODAY" and in "Card history": name, size, time, "✓ 2 copies verified".
public sealed record HistoryRow(string Id, string CardName, string Size, string Time, string Status, HistoryTone Tone, string? Project);

/// One day of the history tab: "Today", "Yesterday", "2026-10-06" with its summary.
public sealed record HistoryDay(string Title, string Summary, IReadOnlyList<HistoryRow> Rows);

/// Today's history in the right column (UI návrh, nová věc 7) — from history.json as the window already loads it.
public static class HistoryView
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// The history tab shows at most this many entries (history.json keeps up to 5 000).
    public const int MaxEntries = 300;

    public static HistoryRow Row(HistoryEntry e)
    {
        int copies = e.Destinations.Count;
        var (status, tone) = e.Unfinished ? ("! unfinished", HistoryTone.Warning)
            : !e.Success
            ? ($"✕ with errors ({Texts.N(e.FailedFiles ?? 0, "file")})", HistoryTone.Error)
            : !e.Verified ? ("! not verified · do not format", HistoryTone.Warning)
            : ($"✓ {Texts.N(copies, "copy", "copies")} verified · safe to format", HistoryTone.Ok);
        return new HistoryRow(e.Id, e.CardName, Format.Bytes3(e.Bytes), e.Finished.ToLocalTime().ToString("HH:mm", Inv), status, tone, e.Project);
    }

    /// "3 cards · 412 GB · all verified", "2 cards · 18.0 GB · 1 card with errors", "1 card · 2.00 GB · 1 not verified".
    public static string Summary(IReadOnlyList<HistoryEntry> entries)
    {
        int n = entries.Count;
        var parts = new List<string> { Texts.N(n, "card"), Format.Bytes3(entries.Sum(e => e.Bytes)) };
        // R6, ruling I5: an unfinished backup is neither verified nor failed — its own count.
        int unfinished = entries.Count(e => e.Unfinished);
        int failed = entries.Count(e => !e.Unfinished && !e.Success), unverified = entries.Count(e => !e.Unfinished && e.Success && !e.Verified);
        if (failed == 0 && unverified == 0 && unfinished == 0) parts.Add("all verified");
        if (unfinished > 0) parts.Add($"{unfinished} unfinished");
        if (failed > 0) parts.Add($"{Texts.N(failed, "card")} with errors");
        if (unverified > 0) parts.Add($"{unverified} not verified");
        return string.Join(" · ", parts);
    }

    /// The cards finished today (local time), newest first; null when there are none (the section hides).
    public static HistoryDay? Today(IEnumerable<HistoryEntry> entries, DateTime nowLocal) =>
        Days(entries, nowLocal).FirstOrDefault(d => d.Title == TodayTitle);

    public const string TodayTitle = "Today";

    /// Every day with a card, newest first (at most MaxEntries cards in all): "Today", "Yesterday", then "2026-10-06".
    public static IReadOnlyList<HistoryDay> Days(IEnumerable<HistoryEntry> entries, DateTime nowLocal)
    {
        var today = nowLocal.Date;
        return entries.OrderByDescending(e => e.Finished).Take(MaxEntries)
            .GroupBy(e => e.Finished.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var list = g.ToList();
                var title = g.Key == today ? TodayTitle : g.Key == today.AddDays(-1) ? "Yesterday" : g.Key.ToString("yyyy-MM-dd", Inv);
                return new HistoryDay(title, Summary(list), list.Select(Row).ToList());
            })
            .ToList();
    }
}
