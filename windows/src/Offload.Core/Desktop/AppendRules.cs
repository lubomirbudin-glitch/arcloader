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
    /// client's). No project = "Bez projektu" (the top level, always there). A project deleted or renamed since: also
    /// "Bez projektu" — never the project shown now — with `Gone` = true (the job card says so, GoneNote; the job still
    /// records the entry's project).
    public static (SettingsProfile Profile, bool Gone) ProfileFor(AppSettings s, string? entryProject)
    {
        if (entryProject == null) return (s, false);
        return Projects.Find(s, entryProject) is { } p ? (p, false) : (s, true);
    }

    /// The job card's note for an append whose project is gone.
    public static string GoneNote(string project) => $"Projekt {project} už neexistuje — doplnění běží s nastavením {Projects.NoProject}";

    /// The LUTs and proxy folders a Start really uses, each once (any case), checked off the UI thread before it starts:
    /// the profile shown — only when a full copy is selected (appends do not use it) — and the profile of every append
    /// (ProfileFor). The profile shown comes first; its checks are `Shown` (its red text in the panel says why).
    public static IReadOnlyList<StartCheck> StartChecks(AppSettings s, int fullCopies, IEnumerable<HistoryEntry> appendEntries)
    {
        var profiles = new List<SettingsProfile>();
        if (fullCopies > 0) profiles.Add(s.Active);
        foreach (var e in appendEntries)
        {
            var p = ProfileFor(s, e.Project).Profile;
            if (!profiles.Contains(p)) profiles.Add(p);
        }
        // The profile shown first, whichever card brought it in.
        if (profiles.Remove(s.Active)) profiles.Insert(0, s.Active);
        var checks = new List<StartCheck>();
        foreach (var p in profiles)
        {
            bool shown = ReferenceEquals(p, s.Active);
            var name = p is ProjectSettings named ? named.Name : Projects.NoProject;
            void Add(StartCheckKind kind, string? path, int? rule = null)
            {
                if (path != null && !checks.Any(c => c.Kind == kind && string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
                    checks.Add(new StartCheck(kind, path, name, shown, rule));
            }
            // The custom LUT, or the own .cube files of the rules that apply (Auto mode, rules this version knows).
            foreach (var f in ProxyPreflight.LutFilesInUse(p.Proxy, p.AnalyzeClips)) Add(StartCheckKind.Lut, f.Path, f.Rule);
            Add(StartCheckKind.ProxyFolder, ProxyPreflight.ExtraFolderInUse(p.Proxy));
        }
        return checks;
    }
}

public enum StartCheckKind { Lut, ProxyFolder }

/// A file or folder a Start uses (AppendRules.StartChecks): `Project` is the profile's name ("Bez projektu" for the top
/// level); `Shown` = of the profile shown now; `Rule` = the number of the LUT rule whose own .cube it is (null: the custom
/// LUT or a folder).
public sealed record StartCheck(StartCheckKind Kind, string Path, string Project, bool Shown, int? Rule = null)
{
    /// Why the Start is refused when it is missing — for another project's file (the shown one has its red text).
    public string Problem => Kind == StartCheckKind.Lut
        ? Rule is int n
            ? $"LUT z pravidla {n} projektu {Project} nenalezen: {System.IO.Path.GetFileName(Path)} — doplnění karty se nespustí. Vyberte v projektu jiný LUT, nebo ho odeberte."
            : $"LUT projektu {Project} nenalezen: {System.IO.Path.GetFileName(Path)} — doplnění karty se nespustí. Vyberte v projektu jiný LUT, nebo ho odeberte."
        : $"Složka pro proxy projektu {Project} není dostupná: {Path} — doplnění karty se nespustí.";
}
