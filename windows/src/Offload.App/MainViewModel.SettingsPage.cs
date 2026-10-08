using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

/// One choice of a segmented control (Kodek, Výška): selected when the setting has its value; selecting it sets it.
public sealed class Choice(string label, Func<bool> isSelected, Action select) : ObservableObject
{
    public string Label => label;

    public bool IsSelected
    {
        get => isSelected();
        set
        {
            if (value && !isSelected()) select();
        }
    }

    internal void Changed() => Raise(nameof(IsSelected));
}

/// The settings page (UI návrh obr. 4) in place of the two columns: "‹ Zpět", the project, "Uložit jako nový projekt",
/// "Hotovo"; sections on the left. Everything edits the profile shown (as the left panel did) and is saved as before.
public sealed partial class MainViewModel
{
    bool settingsOpen;
    string settingsSection = SettingsSections.Folders;

    public RelayCommand OpenSettingsCommand { get; private set; } = null!;
    public RelayCommand CloseSettingsCommand { get; private set; } = null!;
    /// A row of step 3 (or "Upravit projekt"): the settings page at that section (SettingsSections).
    public RelayCommand<string> OpenSettingsSectionCommand { get; private set; } = null!;
    /// The tick of a step 3 row: flips the setting of the profile shown (saved as everything else).
    public RelayCommand<AfterCopyKind> ToggleAfterCopyCommand { get; private set; } = null!;

    void InitSettingsPage()
    {
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        OpenSettingsSectionCommand = new RelayCommand<string>(section =>
        {
            SettingsSection = section ?? SettingsSections.Folders;
            IsSettingsOpen = true;
        });
        ToggleAfterCopyCommand = new RelayCommand<AfterCopyKind>(ToggleAfterCopy, kind => AfterCopyEnabledFor(kind));
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false, () => IsSettingsOpen);
    }

    /// The ticks of step 3 work unless Start is running (as the project switcher) or the demo is on.
    public bool AfterCopyEnabled => CanSwitchProject && Allowed(DemoCommand.SettingsSave);

    bool AfterCopyEnabledFor(AfterCopyKind kind) => AfterCopyRules.CanToggle(kind, CanSwitchProject, Allowed(DemoCommand.SettingsSave));

    void ToggleAfterCopy(AfterCopyKind kind)
    {
        if (!AfterCopyEnabledFor(kind)) return;
        switch (kind)   // through the properties the settings page uses: they save, recheck the LUT and refresh the texts
        {
            case AfterCopyKind.Verify: Verify = !Verify; break;
            case AfterCopyKind.Analysis: AnalyzeClips = !AnalyzeClips; break;
            case AfterCopyKind.Proxy: Proxy.Enabled = !Proxy.Enabled; break;
        }
    }

    public bool IsSettingsOpen
    {
        get => settingsOpen;
        set
        {
            if (!Set(ref settingsOpen, value)) return;
            ProjectMenuOpen = false;
            Raise(nameof(NotifySummary));
        }
    }

    /// The section shown: one of SettingsSections.
    public string SettingsSection
    {
        get => settingsSection;
        set => Set(ref settingsSection, value ?? SettingsSections.Folders);
    }

    /// "Windows: zapnuto · telefon (ntfy): vypnuto".
    public string NotifySummary => SettingsTexts.NotifySummary(Settings.Notify);

    /// "Offload 0.5.0-win".
    public string VersionText => $"Offload {OffloadVersion.String}";
}
