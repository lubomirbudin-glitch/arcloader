using Offload.Core.History;

namespace Offload.Core.Desktop;

/// "Doplnit jen nové klipy" in the Karty list (spec 2.2 UX), without a window.
public static class AppendRules
{
    /// Offered only for a card volume whose history match found new files (and nothing changed) and whose earlier card
    /// folders can be reached. Never for Changed, another card or no history, and never for a manual folder: that is
    /// matched by its shape alone, which is too weak a proof that it is the same card (plan author's ruling).
    public static bool Offer(CardInsight? insight) =>
        insight is { Error: null, Volume: not null, AppendReachable: true, History: { Kind: HistoryMatchKind.NewFiles } m }
        && m.Entry.Destinations.Count > 0;

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)"
    public static string CheckboxText(HistoryMatch m) =>
        $"Doplnit jen nové klipy do dřívější zálohy ({m.New.Count} {Texts.Plural(m.New.Count, "soubor", "soubory", "souborů")} · {Format.BytesCs(m.NewBytes)})";

    /// "Složka: 2026-10-06\A001 na 2 cílech" — taken from the backup's own card folders (the name may be "A001_2").
    public static string FolderText(HistoryEntry e)
    {
        var first = e.Destinations.FirstOrDefault()?.CardPath ?? "";
        var trimmed = first.TrimEnd(Path.DirectorySeparatorChar);
        var folder = Path.GetFileName(trimmed);
        var date = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? "");
        int n = e.Destinations.Count;
        return $@"Složka: {date}\{folder} na {n} {Texts.Plural(n, "cíli", "cílech", "cílech")}";
    }

    /// The job card title: "A001 · doplnění".
    public static string Title(string cardName) => $"{cardName} · doplnění";

    /// The Cíle section matters only for full copies: a selection of appends alone goes to the destinations in history.
    public static bool NeedsDestinations(int fullCopies) => fullCopies > 0;

    /// How many of the selected cards are full copies (those without the append ticked).
    public static int FullCopies(IEnumerable<string> selected, Func<string, bool> appendChosen) => selected.Count(p => !appendChosen(p));

    /// The profile an append runs with (ruling I2): that of the project the earlier backup was made in — verify, stage 2,
    /// outputs and, from task 9, notifications follow the card, not the project shown now (its ntfy topic may be another
    /// client's). No project = "Bez projektu" (the top level, always there). A project deleted since: the active profile,
    /// `Gone` = true (the app notes it in the log; the job still records the entry's project).
    public static (SettingsProfile Profile, bool Gone) ProfileFor(AppSettings s, string? entryProject)
    {
        if (entryProject == null) return (s, false);
        return Projects.Find(s, entryProject) is { } p ? (p, false) : (s.Active, true);
    }
}
