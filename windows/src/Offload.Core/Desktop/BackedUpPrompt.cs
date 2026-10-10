using Offload.Core.History;

namespace Offload.Core.Desktop;

public enum BackedUpChoice { CopyAgain, SkipBackedUp, Cancel }

/// What the window "Cards already backed up" shows (spec 2.1 "Varování před celou kopií").
public sealed record BackedUpPromptModel(string Title, IReadOnlyList<string> Lines, string Question);

public static class BackedUpPrompt
{
    public const string CopyAgainText = "Copy again in full";
    public const string SkipText = "Skip backed-up";
    public const string CancelText = "Cancel";

    /// A selected card that history knows is asked about before a full copy — except one that changed since: its own
    /// line already says that only a full copy is possible — and
    /// not for a card emptied in the camera (nothing on it to copy).
    public static bool ShouldAsk(CardInsight? insight) =>
        insight is { IsEmptied: false, History: { Kind: not HistoryMatchKind.Changed, Entry.Unfinished: false } };   // R6: an unfinished copy is no backup

    /// The selected cards the prompt asks about: backed up, and not ticked for the append (spec 2.2 — those add only the
    /// new files, there is no full copy to warn about).
    public static List<string> CardsToAsk(IEnumerable<string> selected, Func<string, CardInsight?> insight, Func<string, bool> appendChosen) =>
        selected.Where(p => ShouldAsk(insight(p)) && !appendChosen(p)).ToList();

    /// "A001 — 2026-10-06 08:15, Backup (D:), Backup2 (F:), verified · new files: 12"
    public static string Line(HistoryMatch m)
    {
        var e = m.Entry;
        var state = !e.Success ? $"with errors ({Texts.N(e.FailedFiles ?? 0, "file")})"
            : e.Verified ? "verified" : "NOT VERIFIED";
        var line = $"{e.CardName} — {HistoryText.When(e.Finished)}, {string.Join(", ", e.Destinations.Select(d => HistoryText.DiskName(d.Root)))}, {state}";
        return m.Kind == HistoryMatchKind.NewFiles ? $"{line} · new files: {m.New.Count}" : line;
    }

    /// The folder a full copy would probably create (the real name is picked at start by Layout.UniqueCardFolder, which
    /// reads the destinations — not done on the UI thread, so the prompt says "e.g."): "A001_2" when the card was copied under the same name into today's date
    /// folder, otherwise the name in the date folder ("2026-10-07\A001").
    public static string NewFolder(HistoryEntry entry, string date, string plannedName) =>
        entry.Date == date && string.Equals(entry.CardName, plannedName, StringComparison.OrdinalIgnoreCase)
            ? $"{plannedName}_2"
            : $@"{date}\{plannedName}";

    /// `cards`: each backed-up card with the folder name Start would use for it.
    public static BackedUpPromptModel Build(IReadOnlyList<(HistoryMatch Match, string PlannedName)> cards, string date)
    {
        var folders = string.Join(", ", cards.Select(c => NewFolder(c.Match.Entry, date, c.PlannedName)));
        return cards.Count == 1
            ? new BackedUpPromptModel("Card already backed up", [Line(cards[0].Match)], $"Copy it again in full? A new folder will be created (e.g. {folders}).")
            : new BackedUpPromptModel("Cards already backed up", cards.Select(c => Line(c.Match)).ToList(),
                $"Copy them again in full? New folders will be created (e.g. {folders}).");
    }
}
