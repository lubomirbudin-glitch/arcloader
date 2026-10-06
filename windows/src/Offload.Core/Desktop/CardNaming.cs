using Offload.Core.History;

namespace Offload.Core.Desktop;

/// One card row for the name resolution: `Typed` is what the user wrote in "Název složky" (may be empty).
public sealed record CardNameInput(string Key, string? Typed, CardInsight? Insight, string Fallback);

/// What the empty "Název složky" field shows (spec 2.3) — and what Start uses when nothing is typed.
public static class CardNaming
{
    /// The clips' reel first; a card history does not know gets "next in line" for the date folder; otherwise
    /// `fallback` (the volume label or folder name). `Hint`: the grey line under the field, or null.
    /// `taken`: names already typed or suggested for other cards — "next in line" goes past them.
    public static (string Name, string? Hint) Placeholder(bool suggestFromClips, CardInsight? insight,
        IReadOnlyList<HistoryEntry>? history, string date, string today, string fallback, IEnumerable<string>? taken = null)
    {
        if (!suggestFromClips || insight == null) return (fallback, null);
        if (insight.Name is { } fromClips) return (fromClips.Name, fromClips.Hint);
        if (insight.NameNote is string note) return (fallback, note);
        // A card that could not be read is not "next" anything.
        if (insight.Error == null && insight.History == null && history != null
            && ReelSuggestion.Next(history, date, null, today, taken) is { } next)
            return (next.Name, next.Hint);
        return (fallback, null);
    }

    /// The names for all cards of one start, in list order: a typed name wins (no hint); the others get their suggestion,
    /// and unknown cards are numbered one after another (A002, A003 …) skipping typed and already suggested names.
    /// The GUI placeholders and Start both use this, so what is shown is what is copied.
    public static IReadOnlyDictionary<string, (string Name, string? Hint)> Resolve(bool suggestFromClips,
        IReadOnlyList<CardNameInput> cards, IReadOnlyList<HistoryEntry>? history, string date, string today)
    {
        var result = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
        var taken = new List<string>();
        foreach (var c in cards)
        {
            if (c.Typed?.Trim() is { Length: > 0 } typed) taken.Add(typed);
            else if (suggestFromClips && c.Insight?.Name is { } n) taken.Add(n.Name);
        }
        foreach (var c in cards)
        {
            if (c.Typed?.Trim() is { Length: > 0 } typed) { result[c.Key] = (typed, null); continue; }
            var p = Placeholder(suggestFromClips, c.Insight, history, date, today, c.Fallback, taken);
            if (p.Name != c.Fallback && c.Insight?.Name == null) taken.Add(p.Name);
            result[c.Key] = p;
        }
        return result;
    }
}
