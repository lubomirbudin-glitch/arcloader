using Offload.Core.History;

namespace Offload.Core.Desktop;

public enum BackedUpChoice { CopyAgain, SkipBackedUp, Cancel }

/// What the window "Karty už jsou zálohované" shows (spec 2.1 "Varování před celou kopií").
public sealed record BackedUpPromptModel(string Title, IReadOnlyList<string> Lines, string Question);

public static class BackedUpPrompt
{
    public const string CopyAgainText = "Kopírovat znovu celé";
    public const string SkipText = "Vynechat zálohované";
    public const string CancelText = "Zrušit";

    /// A selected card that history knows is asked about before a full copy — except one that changed since: its own
    /// line already says that only a full copy is possible — and
    /// not for a card emptied in the camera (nothing on it to copy).
    public static bool ShouldAsk(CardInsight? insight) => insight is { IsEmptied: false, History: { Kind: not HistoryMatchKind.Changed } };

    /// "A001 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha2 (F:), ověřeno · nové soubory: 12"
    public static string Line(HistoryMatch m)
    {
        var e = m.Entry;
        var state = !e.Success ? $"s chybami ({e.FailedFiles ?? 0} {Texts.Plural(e.FailedFiles ?? 0, "soubor", "soubory", "souborů")})"
            : e.Verified ? "ověřeno" : "NEOVĚŘENO";
        var line = $"{e.CardName} — {HistoryText.When(e.Finished)}, {string.Join(", ", e.Destinations.Select(d => HistoryText.DiskName(d.Root)))}, {state}";
        return m.Kind == HistoryMatchKind.NewFiles ? $"{line} · nové soubory: {m.New.Count}" : line;
    }

    /// The folder a full copy would probably create (the real name is picked at start by Layout.UniqueCardFolder, which
    /// reads the destinations — not done on the UI thread, so the prompt says "např."): "A001_2" when the card was copied under the same name into today's date
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
            ? new BackedUpPromptModel("Karta už je zálohovaná", [Line(cards[0].Match)], $"Zkopírovat ji znovu celou? Vznikne nová složka (např. {folders}).")
            : new BackedUpPromptModel("Karty už jsou zálohované", cards.Select(c => Line(c.Match)).ToList(),
                $"Zkopírovat je znovu celé? Vzniknou nové složky (např. {folders}).");
    }
}
