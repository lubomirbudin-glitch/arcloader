using Offload.Core.History;

namespace Offload.Core.Desktop;

/// What the empty "Název složky" field shows (spec 2.3) — and what Start uses when nothing is typed.
public static class CardNaming
{
    /// The clips' reel first; a card history does not know gets "next in line" for the date folder; otherwise
    /// `fallback` (the volume label or folder name). `Hint`: the grey line under the field, or null.
    public static (string Name, string? Hint) Placeholder(bool suggestFromClips, CardInsight? insight,
        IReadOnlyList<HistoryEntry>? history, string date, string today, string fallback)
    {
        if (!suggestFromClips || insight == null) return (fallback, null);
        if (insight.Name is { } fromClips) return (fromClips.Name, fromClips.Hint);
        if (insight.NameNote is string note) return (fallback, note);
        if (insight.History == null && history != null && ReelSuggestion.Next(history, date, null, today) is { } next)
            return (next.Name, next.Hint);
        return (fallback, null);
    }
}
