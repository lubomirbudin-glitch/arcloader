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

    void InitSettingsPage()
    {
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false, () => IsSettingsOpen);
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

/// The sections of the settings page (the keys the navigation and the panels compare with).
public static class SettingsSections
{
    public const string Folders = "folders";
    public const string Verify = "verify";
    public const string Analysis = "analysis";
    public const string Proxy = "proxy";
    public const string Report = "report";
    public const string Notify = "notify";
    public const string Decoders = "decoders";
    public const string App = "app";
}
