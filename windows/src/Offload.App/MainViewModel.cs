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
        demo = options.Demo;   // before the scans start: the demo replaces them (MainViewModel.Demo.cs)
        this.settingsPath = settingsPath ?? SettingsStore.DefaultPath;
        AllSettings = SettingsStore.Load(this.settingsPath);
        SeedDemo();
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
        InitDemo();
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
        if (!Allowed(DemoCommand.SettingsSave)) { RaiseMany(nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(EstimateText), nameof(FlowSteps), nameof(QueueEmptyText)); return; }
        saveTimer.Stop();
        saveTimer.Start();
        RaiseMany(nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(EstimateText), nameof(FlowSteps), nameof(QueueEmptyText));   // they show the profile
    }

    public void SaveNow()
    {
        saveTimer.Stop();
        if (!Allowed(DemoCommand.SettingsSave)) return;
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
