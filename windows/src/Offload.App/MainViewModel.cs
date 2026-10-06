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
        Settings = SettingsStore.Load(this.settingsPath);
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            SaveNow();
        };
        InitSetup();
        InitQueue();
        InitHardware();
        InitHistory();
    }

    public StartupOptions Options { get; }
    public AppSettings Settings { get; }

    public event Action<string>? MessageRequested;

    /// Settings changed: written 400 ms after the last change, and on exit.
    public void SettingsChanged()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    public void SaveNow()
    {
        saveTimer.Stop();
        try
        {
            SettingsStore.Save(Settings, settingsPath);
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
