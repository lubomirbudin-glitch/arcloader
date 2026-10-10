using Offload.Core.Desktop;

namespace Offload.App;

/// One icon of the empty queue's flow: its label, which icon (card, disk, verify, proxy, report), the first is blue.
public sealed record FlowStep(string Label, string Icon, bool First);

/// One project in the switcher's list: its name, whether it is shown now, and the command that switches to it.
public sealed record ProjectItem(string Name, bool IsActive, RelayCommand Select);

/// The top bar (UI design): the project switcher in the place of the preset switcher, the queue pill, the Hardware dot.
public sealed partial class MainViewModel
{
    bool projectMenuOpen;
    string? queuePill;
    int queueBadge;
    IReadOnlyList<ProjectItem> projectItems = [];

    /// "No project" first, then the projects in the order they were saved; the active one ticked.
    public IReadOnlyList<ProjectItem> ProjectItems => projectItems;

    /// What the switcher shows after "Project".
    public string ProjectLabel => TopBar.ProjectLabel(ActiveProjectName);

    public bool ProjectMenuOpen
    {
        get => projectMenuOpen;
        set => Set(ref projectMenuOpen, value && CanSwitchProject);
    }

    /// The switcher's list changed (a project saved, renamed, deleted, or another one shown).
    void RebuildProjectItems()
    {
        projectItems = ProjectChoices.Select(name => new ProjectItem(name, name == ActiveProjectChoice, new RelayCommand(() =>
        {
            ProjectMenuOpen = false;
            ActiveProjectChoice = name;
        }, () => CanSwitchProject))).ToList();
        RaiseMany(nameof(ProjectItems), nameof(ProjectLabel));
    }

    /// "1 offload running · 1 in queue" — null hides the pill.
    public string? QueuePill => queuePill;

    /// The unfinished jobs, next to the "Queue" tab (0 = no number).
    public int QueueBadge => queueBadge;

    /// Green / yellow / none after the last USB check.
    public HardwareDot HardwareDot => TopBar.Dot(lastHwTest);

    public string HardwareTooltip => $"Test USB ports… (Ctrl+U) — {HardwareSummary}";

    /// Every tick: the pill and the tab's number follow the queue (raised only when they changed).
    void RefreshQueueSummary()
    {
        var (running, paused, waiting) = QueueSummary.Count(Jobs.Select(j => (j.Phase, j.IsPauseHeld)));
        var pill = QueueSummary.Pill(running, paused, waiting);
        int badge = running + paused + waiting;
        if (pill == queuePill && badge == queueBadge) return;
        queuePill = pill;
        queueBadge = badge;
        RaiseMany(nameof(QueuePill), nameof(QueueBadge), nameof(QueueBadgeText));
    }
}
