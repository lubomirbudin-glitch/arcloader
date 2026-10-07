namespace Offload.Core.Desktop;

/// Projects (spec 4.1, R17): named profiles in settings.json. The app edits the active profile (AppSettings.Active);
/// these operations change which one that is. Names are trimmed, 1–60 characters, unique regardless of case, and
/// "Bez projektu" is reserved. Every method that takes a name throws ArgumentException with a Czech message.
public static class Projects
{
    public const string NoProject = "Bez projektu";
    public const int MaxNameLength = 60;

    /// Why `name` cannot be a project's name (Czech), or null. `except`: the project being renamed (may keep its name).
    public static string? NameProblem(string? name, IEnumerable<string> existing, string? except = null)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return "Název nesmí být prázdný.";
        if (n.Length > MaxNameLength) return $"Název může mít nejvýš {MaxNameLength} znaků.";
        if (string.Equals(n, NoProject, StringComparison.OrdinalIgnoreCase)) return $"„{NoProject}“ je vyhrazený název.";
        if (existing.Any(e => string.Equals(e, n, StringComparison.OrdinalIgnoreCase) && !string.Equals(e, except, StringComparison.OrdinalIgnoreCase)))
            return "Projekt s tímto názvem už existuje.";
        return null;
    }

    /// The project of that name regardless of case; null for null, "Bez projektu" or an unknown name.
    public static ProjectSettings? Find(AppSettings s, string? name) =>
        name == null ? null : s.Projects.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// "Uložit jako projekt…": a new project with the values shown now (the active profile); it becomes the active one.
    public static ProjectSettings SaveAs(AppSettings s, string name)
    {
        if (NameProblem(name, s.Projects.Select(p => p.Name)) is string problem) throw new ArgumentException(problem);
        var project = new ProjectSettings { Name = name.Trim() };
        CopyProfile(s.Active, project);
        s.Projects.Add(project);
        s.ActiveProject = project.Name;
        return project;
    }

    /// "Přejmenovat…" of a project; the active one stays active under its new name.
    public static void Rename(AppSettings s, string name, string newName)
    {
        var project = Find(s, name) ?? throw new ArgumentException(Unknown(name, s));
        if (NameProblem(newName, s.Projects.Select(p => p.Name), except: project.Name) is string problem) throw new ArgumentException(problem);
        bool active = s.ActiveProject == project.Name;
        project.Name = newName.Trim();
        if (active) s.ActiveProject = project.Name;
    }

    /// "Smazat…": the project's settings are gone (backups on the disks stay); the active one → "Bez projektu".
    public static void Delete(AppSettings s, string name)
    {
        var project = Find(s, name) ?? throw new ArgumentException(Unknown(name, s));
        s.Projects.Remove(project);
        if (s.ActiveProject == project.Name) s.ActiveProject = null;
    }

    /// Switches to a project, or to "Bez projektu" for null / "Bez projektu". Nothing is copied: each profile keeps
    /// its own values, so switching there and back loses nothing.
    public static void Activate(AppSettings s, string? name)
    {
        if (name == null || string.Equals(name.Trim(), NoProject, StringComparison.OrdinalIgnoreCase))
        {
            s.ActiveProject = null;
            return;
        }
        s.ActiveProject = (Find(s, name) ?? throw new ArgumentException(Unknown(name, s))).Name;
    }

    /// A deep copy of the profile values (not the keys only a newer version knows — they belong to the profile they came with).
    public static void CopyProfile(SettingsProfile from, SettingsProfile to)
    {
        to.DestRoots = [.. from.DestRoots];
        to.Verify = from.Verify;
        to.AnalyzeClips = from.AnalyzeClips;
        to.Hash = from.Hash;
        to.Proxy = SettingsStore.Clone(from.Proxy);
        to.Naming = SettingsStore.Clone(from.Naming);
        to.Outputs = SettingsStore.Clone(from.Outputs);
        to.Notify = SettingsStore.Clone(from.Notify);
    }

    /// The project's name, or null for "Bez projektu" — what a job, history and the report record.
    public static string? ActiveName(AppSettings s) => Find(s, s.ActiveProject)?.Name;

    /// The window title: "Offload", or "Offload — Film X".
    public static string WindowTitle(string? project) => project == null ? "Offload" : $"Offload — {project}";

    public static string DeleteQuestion(string name) => $"Smazat projekt {name}? Nastavení projektu se ztratí, zálohy na discích zůstanou.";

    /// The CLI's answer to an unknown --project.
    public static string Unknown(string name, AppSettings s) => s.Projects.Count == 0
        ? $"Projekt „{name}“ neexistuje. Žádný projekt není uložený."
        : $"Projekt „{name}“ neexistuje. Projekty: {string.Join(", ", s.Projects.Select(p => p.Name))}.";
}
