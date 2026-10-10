namespace Offload.Core.Desktop;

/// Projects (spec 4.1, R17): named profiles in settings.json. The app edits the active profile (AppSettings.Active);
/// these operations change which one that is. Names are trimmed, 1–60 characters, unique regardless of case, and
/// "No project" (and the old "No project") is reserved. Every method that takes a name throws ArgumentException with
/// an English message.
public static class Projects
{
    public const string NoProject = "No project";

    /// What the top level was called before 0.9.0 (Czech): still reserved, so no project is ever named so.
    public const string NoProjectOld = "Bez projektu";
    public const int MaxNameLength = 60;

    /// "No project" in either spelling (scripts written for 0.8 and older pass "Bez projektu").
    public static bool IsNoProject(string? name) =>
        name != null && (string.Equals(name.Trim(), NoProject, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(name.Trim(), NoProjectOld, StringComparison.OrdinalIgnoreCase));

    /// Why `name` cannot be a project's name, or null. `except`: the project being renamed (may keep its name).
    public static string? NameProblem(string? name, IEnumerable<string> existing, string? except = null)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return "The name must not be empty.";
        if (n.Length > MaxNameLength) return $"The name can have at most {MaxNameLength} characters.";
        if (string.Equals(n, NoProject, StringComparison.OrdinalIgnoreCase) || string.Equals(n, NoProjectOld, StringComparison.OrdinalIgnoreCase))
            return $"\"{n}\" is a reserved name.";
        if (existing.Any(e => string.Equals(e, n, StringComparison.OrdinalIgnoreCase) && !string.Equals(e, except, StringComparison.OrdinalIgnoreCase)))
            return "A project with this name already exists.";
        return null;
    }

    /// The project of that name regardless of case; null for null, "No project" or an unknown name.
    public static ProjectSettings? Find(AppSettings s, string? name) =>
        name == null ? null : s.Projects.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// "Save as project…": a new project with the values shown now (the active profile); it becomes the active one.
    public static ProjectSettings SaveAs(AppSettings s, string name)
    {
        if (NameProblem(name, s.Projects.Select(p => p.Name)) is string problem) throw new ArgumentException(problem);
        var project = new ProjectSettings { Name = name.Trim() };
        CopyProfile(s.Active, project);
        s.Projects.Add(project);
        s.ActiveProject = project.Name;
        return project;
    }

    /// "Rename…" of a project; the active one stays active under its new name.
    public static void Rename(AppSettings s, string name, string newName)
    {
        var project = Find(s, name) ?? throw new ArgumentException(Unknown(name, s));
        if (NameProblem(newName, s.Projects.Select(p => p.Name), except: project.Name) is string problem) throw new ArgumentException(problem);
        bool active = s.ActiveProject == project.Name;
        project.Name = newName.Trim();
        if (active) s.ActiveProject = project.Name;
    }

    /// "Smazat…": the project's settings are gone (backups on the disks stay); the active one → "No project".
    public static void Delete(AppSettings s, string name)
    {
        var project = Find(s, name) ?? throw new ArgumentException(Unknown(name, s));
        s.Projects.Remove(project);
        if (s.ActiveProject == project.Name) s.ActiveProject = null;
    }

    /// Switches to a project, or to "No project" for null / "No project". Nothing is copied: each profile keeps
    /// its own values, so switching there and back loses nothing.
    public static void Activate(AppSettings s, string? name)
    {
        if (name == null || IsNoProject(name))
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
        to.VerifyInProxy = from.VerifyInProxy;   // plan 10 (R11)
        to.VerifyPrimary = from.VerifyPrimary;
        to.AnalyzeClips = from.AnalyzeClips;
        to.Hash = from.Hash;
        to.Proxy = SettingsStore.Clone(from.Proxy);
        to.Naming = SettingsStore.Clone(from.Naming);
        to.Outputs = SettingsStore.Clone(from.Outputs);
        to.Notify = SettingsStore.Clone(from.Notify);
        to.AutoDrive = SettingsStore.Clone(from.AutoDrive);   // the destinations it added go with DestRoots
    }

    /// The project's name, or null for "No project" — what a job, history and the report record.
    public static string? ActiveName(AppSettings s) => Find(s, s.ActiveProject)?.Name;

    /// The window title: "Offload", or "Offload — Film X".
    public static string WindowTitle(string? project) => project == null ? "Offload" : $"Offload — {project}";

    public static string DeleteQuestion(string name) => $"Delete project {name}? The project's settings will be lost; the backups on the disks stay.";

    /// The CLI's answer to an unknown --project.
    public static string Unknown(string name, AppSettings s) => s.Projects.Count == 0
        ? $"Project \"{name}\" does not exist. No project is saved."
        : $"Project \"{name}\" does not exist. Projects: {string.Join(", ", s.Projects.Select(p => p.Name))}.";
}

/// What Start saw when it began (ruling I1): the profile shown and its project's name. Start awaits checks of that
/// profile (LUT, proxy folder, card inspections, free space, questions); if another profile is shown afterwards
/// (switched, renamed, deleted, saved as a new project), those checks were not made for it and Start stops.
/// Values edited inside the same profile do not count — Start's own loops re-check those.
public readonly record struct ProfileStamp(SettingsProfile Profile, string? Name)
{
    public const string NotStarted = "The project changed while the start was being prepared — the offload did not start. Start it again.";

    public static ProfileStamp Of(AppSettings s) => new(s.Active, Projects.ActiveName(s));

    public bool ChangedIn(AppSettings s) => !ReferenceEquals(Profile, s.Active) || !string.Equals(Name, Projects.ActiveName(s), StringComparison.Ordinal);
}
