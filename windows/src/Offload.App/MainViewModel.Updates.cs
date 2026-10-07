using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Updates;

namespace Offload.App;

/// Spec 4.4: 5 s after the start (and at most once in 24 h) the manifest is read off the UI thread; a newer version shows
/// as a quiet link on the right of the menu bar. Nástroje → Aktualizace… sets the manifest and checks now.
public sealed partial class MainViewModel
{
    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    UpdateInfo? newVersion;
    (UpdateLinkKind Kind, string Target) newVersionLink;
    bool checkingUpdates;

    public RelayCommand UpdatesCommand { get; private set; } = null!;
    /// Opens the Aktualizace window (set by the main window) with this view model.
    public Action<MainViewModel>? ShowUpdatesDialog { get; set; }

    void InitUpdates()
    {
        UpdatesCommand = new RelayCommand(() => ShowUpdatesDialog?.Invoke(this), () => !closing);
        updateTimer.Tick += (_, _) =>
        {
            updateTimer.Stop();
            _ = CheckUpdatesAsync(force: false);
        };
        updateTimer.Start();
    }

    /// "Nová verze 0.5.0" — null while there is none.
    public string? NewVersionText => newVersion is { } v ? $"Nová verze {v.Version}" : null;
    public UpdateInfo? NewVersion => newVersion;
    /// Ruling I5: "Otevřít" only for an https address or an existing folder / zip; anything else is shown as text.
    public bool NewVersionCanOpen => newVersionLink.Kind != UpdateLinkKind.None;
    public string? NewVersionNote => newVersion is { Url: { Length: > 0 } url } && !NewVersionCanOpen
        ? $"Adresa v manifestu není https ani složka — otevřete ji ručně: {url}" : null;

    /// The manifest source in settings.json (`updates.manifest`); empty = off.
    public string UpdateManifest
    {
        get => AllSettings.Updates.Manifest ?? "";
        set
        {
            AllSettings.Updates.Manifest = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            SettingsChanged();
            Raise();
        }
    }

    /// Reads the manifest off the UI thread (the file read and HTTP have their own limits). The Czech result line, or null.
    internal async Task<string?> CheckUpdatesAsync(bool force)
    {
        if (checkingUpdates || closing) return null;
        checkingUpdates = true;
        try
        {
            var source = AllSettings.Updates.Manifest;
            var result = await Task.Run(() => UpdateChecker.CheckAsync(source, OffloadVersion.String, DateTime.UtcNow, UpdateChecker.DefaultStatePath, force));
            if (closing) return null;
            if (result.Status is UpdateStatus.NewVersion)
            {
                newVersion = result.Info;
                newVersionLink = await ClassifyLinkAsync(result.Info?.Url);   // the disk is touched off the UI thread
                if (closing) return null;
            }
            else if (result.Status is UpdateStatus.UpToDate or UpdateStatus.Off)
            {
                newVersion = null;
                newVersionLink = default;
            }
            if (result.Status == UpdateStatus.Failed) AppLog.Write($"update check: {result.Error}");
            RaiseMany(nameof(NewVersionText), nameof(NewVersion), nameof(NewVersionCanOpen), nameof(NewVersionNote));
            return result.Text(OffloadVersion.String);
        }
        catch (Exception e)
        {
            AppLog.Write($"update check: {e.Message}");
            return $"Manifest nejde načíst: {e.Message}";
        }
        finally
        {
            checkingUpdates = false;
        }
    }

    /// UpdateLinks.Target on its own task, 5 s at most (a dead UNC path); a timeout means "not openable".
    static async Task<(UpdateLinkKind, string)> ClassifyLinkAsync(string? url)
    {
        var work = Task.Run(() => UpdateLinks.Target(url));
        if (await Task.WhenAny(work, Task.Delay(UpdateChecker.FileTimeout)) != work)
        {
            _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            return (UpdateLinkKind.None, "");
        }
        return await work;
    }

    /// "Otevřít": https in the browser, a folder or a zip in Explorer (never downloaded or started by Offload).
    internal void OpenNewVersion()
    {
        var (kind, target) = newVersionLink;
        var error = kind switch
        {
            UpdateLinkKind.Web => Shell.OpenWeb(target),
            UpdateLinkKind.Folder => Shell.OpenFolder(target),
            UpdateLinkKind.File => Shell.Reveal(target),
            _ => null,
        };
        if (error != null) ShowMessage(error);
    }
}
