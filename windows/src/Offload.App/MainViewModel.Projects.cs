using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// Projects (spec 4.1): "Projekt: [▾]" at the top of the left panel and the Projekt menu. Switching only changes which
/// profile the window shows; queued and running jobs keep the copy they were started with (JobConfigs clones).
public sealed partial class MainViewModel
{
    // The built-in LUTs once loaded: a new Proxy view model (another project) gets them at once.
    LutCatalog? lutCatalog;
    // The list the ComboBox shows: a new instance only when the projects changed, so a switch does not rebuild it.
    IReadOnlyList<string> projectChoices = [];

    public RelayCommand SaveAsProjectCommand { get; private set; } = null!;
    public RelayCommand RenameProjectCommand { get; private set; } = null!;
    public RelayCommand DeleteProjectCommand { get; private set; } = null!;

    /// Asks for a project name (set by the window): title, the name to start with, the check of a name (Czech problem
    /// or null). Returns the accepted name, or null when the user cancels.
    public Func<string, string, Func<string, string?>, string?>? AskProjectName { get; set; }

    void InitProjects()
    {
        SaveAsProjectCommand = new RelayCommand(SaveAsProject, () => CanEditProjects);
        RenameProjectCommand = new RelayCommand(RenameProject, () => CanEditProjects && ActiveProjectName != null);
        DeleteProjectCommand = new RelayCommand(DeleteProject, () => CanEditProjects && ActiveProjectName != null);
        RebuildProjectChoices();
    }

    /// Ruling I1: no switch, save as, rename or delete while Start checks and asks (its checks are for the profile shown).
    public bool CanSwitchProject => !starting && !closing;
    /// Save as, rename, delete: they write settings.json (off in the demo).
    bool CanEditProjects => CanSwitchProject && Allowed(DemoCommand.ProjectEdit);

    /// The active project's name; null = "Bez projektu" — also when activeProject names a project this version cannot
    /// read (never `ActiveProject == null`). What a started job, history and the report get.
    internal string? ActiveProjectName => Projects.ActiveName(AllSettings);

    /// "Bez projektu" first, then the projects in the order they were saved.
    public IReadOnlyList<string> ProjectChoices => projectChoices;

    void RebuildProjectChoices()
    {
        IReadOnlyList<string> now = [Projects.NoProject, .. AllSettings.Projects.Select(p => p.Name)];
        if (!now.SequenceEqual(projectChoices, StringComparer.Ordinal)) projectChoices = now;
        RebuildProjectItems();   // the top bar's switcher (the active one may have changed too)
    }

    public string ActiveProjectChoice
    {
        get => ActiveProjectName ?? Projects.NoProject;
        set
        {
            // null comes from the ComboBox while its list is rebuilt: not a choice.
            if (value == null || value == ActiveProjectChoice) return;
            if (!CanSwitchProject)
            {
                Raise();   // the ComboBox goes back to the profile shown
                return;
            }
            try
            {
                Projects.Activate(AllSettings, value);
            }
            catch (ArgumentException e)
            {
                ShowMessage(e.Message);
                Raise();
                return;
            }
            ProfileChanged();
        }
    }

    /// "Offload — Film X" (spec 4.1).
    public string WindowTitle => Projects.WindowTitle(ActiveProjectName);

    void SaveAsProject()
    {
        if (!CanEditProjects) return;
        if (AskProjectName?.Invoke("Uložit jako projekt", "", n => Projects.NameProblem(n, AllSettings.Projects.Select(p => p.Name))) is not string name) return;
        if (!CanEditProjects) return;
        Projects.SaveAs(AllSettings, name);   // the values shown now: nothing on screen changes but the name
        ProfileChanged();
    }

    void RenameProject()
    {
        if (!CanEditProjects || ActiveProjectName is not string current) return;
        if (AskProjectName?.Invoke("Přejmenovat projekt", current, n => Projects.NameProblem(n, AllSettings.Projects.Select(p => p.Name), except: current)) is not string name) return;
        if (!CanEditProjects || ActiveProjectName != current) return;
        Projects.Rename(AllSettings, current, name);
        ProfileChanged();
    }

    void DeleteProject()
    {
        if (!CanEditProjects || ActiveProjectName is not string current) return;
        if (AskYesNo?.Invoke(Projects.DeleteQuestion(current)) != true) return;
        if (!CanEditProjects || ActiveProjectName != current) return;
        Projects.Delete(AllSettings, current);
        ProfileChanged();
    }

    /// Another profile is shown (switch, save as, rename, delete): saved, and everything the left panel shows from it is
    /// read again — the destinations and their disks, the LUT and the proxy folder (off the UI thread), the names.
    void ProfileChanged()
    {
        SettingsChanged();   // the whole file from the live objects, 400 ms later
        RebuildProjectChoices();
        Proxy = new ProxyOptionsViewModel(Settings.Proxy, this);
        if (lutCatalog != null) Proxy.CatalogLoaded(lutCatalog);
        folderTarget = null;   // the proxy folder of the profile shown before: no longer counted
        folderFree = null;
        RebuildDestinations();
        RefreshVolumes();   // a disk that is a destination in this profile is no card here
        _ = ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30));
        RefreshLut();
        RefreshProxyFolder();
        NamingChanged();   // "next in line" counts the cards of this project
        Raise(string.Empty);   // every binding of the window
        SetupChanged?.Invoke();
    }
}
