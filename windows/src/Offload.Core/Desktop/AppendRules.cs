using Offload.Core.History;

namespace Offload.Core.Desktop;

/// "Append (new clips only)" in the Cards list (spec 2.2 UX), without a window.
public static class AppendRules
{
    /// Offered only for a card volume whose history match found new files (and nothing changed) and whose earlier card
    /// folders can be reached. Never for Changed, another card or no history, and never for a manual folder: that is
    /// matched by its shape alone, which is too weak a proof that it is the same card (plan author's ruling).
    /// Ruling I5 (plan 10): a backup the app never finished ("unfinished") is offered also with nothing new on the card —
    /// "Finish interrupted backup" completes its copy or its verification (R6).
    public static bool Offer(CardInsight? insight) =>
        insight is { Error: null, Volume: not null, AppendReachable: true, History: { } m }
        && m.Entry.Destinations.Count > 0
        && (m.Kind == HistoryMatchKind.NewFiles || m is { Kind: HistoryMatchKind.Identical, Entry.Unfinished: true });

    /// "Append new clips only to the earlier backup (12 files · 3.2 GB)"; R6: "Finish interrupted backup (…)" for a copy
    /// the app never finished (what is on the disks already is not copied again).
    public static string CheckboxText(HistoryMatch m) => m.Entry.Unfinished
        ? m.New.Count == 0 ? "Finish interrupted backup (finishes the verification)"
            : $"Finish interrupted backup ({Texts.N(m.New.Count, "file")} · {Format.BytesShort(m.NewBytes)})"
        : $"Append new clips only to the earlier backup ({Texts.N(m.New.Count, "file")} · {Format.BytesShort(m.NewBytes)})";

    /// "Folder: 2026-10-06\A001 on 2 destinations" — taken from the backup's own card folders (the name may be "A001_2").
    public static string FolderText(HistoryEntry e)
    {
        var first = e.Destinations.FirstOrDefault()?.CardPath ?? "";
        var trimmed = first.TrimEnd(Path.DirectorySeparatorChar);
        var folder = Path.GetFileName(trimmed);
        var date = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? "");
        int n = e.Destinations.Count;
        return $@"Folder: {date}\{folder} on {Texts.N(n, "destination")}";
    }

    /// The job card title: "A001 · append".
    public static string Title(string cardName) => $"{cardName} · append";

    /// The Cíle section matters only for full copies: a selection of appends alone goes to the destinations in history.
    public static bool NeedsDestinations(int fullCopies) => fullCopies > 0;

    /// How many of the selected cards are full copies (those without the append ticked).
    public static int FullCopies(IEnumerable<string> selected, Func<string, bool> appendChosen) => selected.Count(p => !appendChosen(p));

    /// The profile an append runs with (ruling I2): that of the project the earlier backup was made in — verify, stage 2,
    /// outputs and, from task 9, notifications follow the card, not the project shown now (its ntfy topic may be another
    /// client's). No project = "No project" (the top level, always there). A project deleted or renamed since: also
    /// "No project" — never the project shown now — with `Gone` = true (the job card says so, GoneNote; the job still
    /// records the entry's project).
    public static (SettingsProfile Profile, bool Gone) ProfileFor(AppSettings s, string? entryProject)
    {
        if (entryProject == null) return (s, false);
        return Projects.Find(s, entryProject) is { } p ? (p, false) : (s, true);
    }

    /// The job card's note for an append whose project is gone.
    public static string GoneNote(string project) => $"Project {project} no longer exists — the append runs with the settings of {Projects.NoProject}";

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

/// A file or folder a Start uses (AppendRules.StartChecks): `Project` is the profile's name ("No project" for the top
/// level); `Shown` = of the profile shown now; `Rule` = the number of the LUT rule whose own .cube it is (null: the custom
/// LUT or a folder).
public sealed record StartCheck(StartCheckKind Kind, string Path, string Project, bool Shown, int? Rule = null)
{
    /// Why the Start is refused when it is missing — for another project's file (the shown one has its red text).
    public string Problem => Kind == StartCheckKind.Lut
        ? Rule is int n
            ? $"LUT of rule {n} of project {Project} not found: {System.IO.Path.GetFileName(Path)} — the card's append will not start. Select another LUT in the project, or remove it."
            : $"LUT of project {Project} not found: {System.IO.Path.GetFileName(Path)} — the card's append will not start. Select another LUT in the project, or remove it."
        : $"The proxy folder of project {Project} is not available: {Path} — the card's append will not start.";
}
