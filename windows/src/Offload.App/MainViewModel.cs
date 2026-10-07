using System.Windows.Threading;
using Offload.Core.Desktop;

namespace Offload.App;

/// The window state. Split by area: Setup (left panel), Queue (right panel), Hardware.
public sealed partial class MainViewModel : ObservableObject
{
    readonly string settingsPath;
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public MainViewModel(StartupOptions options, string? settingsPath = null)
    {
        Options = options;
        this.settingsPath = settingsPath ?? SettingsStore.DefaultPath;
        AllSettings = SettingsStore.Load(this.settingsPath);
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            SaveNow();
        };
        InitSetup();
        InitQueue();
        InitHardware();
        InitHistory();
        InitVerify();
        InitDayReport();
        InitProjects();
        InitNotify();
        InitUpdates();
        InitSettingsPage();
    }

    public StartupOptions Options { get; }
    /// The whole settings.json: the global keys (tools, port names) and every project.
    public AppSettings AllSettings { get; }
    /// The profile the window shows and edits: the active project, else "Bez projektu" (spec 4.1). Every change goes
    /// into it; a started job keeps its own copy.
    public SettingsProfile Settings => AllSettings.Active;

    public event Action<string>? MessageRequested;

    /// Settings changed: written 400 ms after the last change, and on exit.
    public void SettingsChanged()
    {
        saveTimer.Stop();
        saveTimer.Start();
        RaiseMany(nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(EstimateText));   // step 3 and the estimate show the profile
    }

    public void SaveNow()
    {
        saveTimer.Stop();
        try
        {
            SettingsStore.Save(AllSettings, settingsPath);
        }
        catch (Exception e)
        {
            AppLog.Write($"settings save: {e.Message}");
        }
    }

    public void ShowMessage(string text)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) MessageRequested?.Invoke(text);
        else dispatcher.BeginInvoke(() => MessageRequested?.Invoke(text));
    }
}
