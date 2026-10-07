using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Desktop;
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
        UpdatesCommand = new RelayCommand(() => ShowUpdatesDialog?.Invoke(this), () => !closing && Allowed(DemoCommand.UpdateCheck));
        updateTimer.Tick += (_, _) =>
        {
            updateTimer.Stop();
            _ = CheckUpdatesAsync(force: false);
        };
        if (Allowed(DemoCommand.UpdateCheck)) updateTimer.Start();
    }

    /// "Nová verze 0.5.0" — null while there is none.
    public string? NewVersionText => newVersion is { } v ? $"Nová verze {UpdateChecker.Plain(v.Version)}" : null;
    public UpdateInfo? NewVersion => newVersion;
    /// Ruling I5 + U1: "Otevřít" only for an https address or a local folder / zip (decided by the text, no disk access);
    /// a network path or anything else is shown as copyable text with a note.
    public bool NewVersionCanOpen => newVersionLink.Kind is UpdateLinkKind.Web or UpdateLinkKind.Folder or UpdateLinkKind.File;
    public string? NewVersionNote => newVersion is { Url: { Length: > 0 } url } && !NewVersionCanOpen
        ? (newVersionLink.Kind == UpdateLinkKind.Network ? UpdateLinks.NetworkNote : $"Adresa v manifestu není https ani složka — zkopírujte ji a otevřete ručně: {url}") : null;

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
        if (checkingUpdates || closing || !Allowed(DemoCommand.UpdateCheck)) return null;
        checkingUpdates = true;
        try
        {
            var source = AllSettings.Updates.Manifest;
            var result = await Task.Run(() => UpdateChecker.CheckAsync(source, OffloadVersion.String, DateTime.UtcNow, UpdateChecker.DefaultStatePath, force));
            if (closing) return null;
            if (result.Status is UpdateStatus.NewVersion)
            {
                newVersion = result.Info;
                newVersionLink = UpdateLinks.Classify(result.Info?.Url);   // by the text only: no disk, no network
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

    /// "Otevřít": only now is the disk asked (off the UI thread, 5 s at most — a dead path must not freeze anything),
    /// and the check is repeated right before launching. https in the browser, a local folder or zip in Explorer;
    /// nothing is downloaded or started by Offload.
    internal async void OpenNewVersion()
    {
        var url = newVersion?.Url;
        var work = Task.Run(() => UpdateLinks.Resolve(url));
        if (await Task.WhenAny(work, Task.Delay(UpdateChecker.FileTimeout)) != work)
        {
            _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            ShowMessage("Adresa neodpovídá — otevřete ji ručně.");
            return;
        }
        var (kind, target) = await work;
        if (closing) return;
        var error = kind switch
        {
            UpdateLinkKind.Web => Shell.OpenWeb(target),
            UpdateLinkKind.Folder => Shell.OpenFolder(target),
            UpdateLinkKind.File => Shell.Reveal(target),
            UpdateLinkKind.Network => UpdateLinks.NetworkNote,
            _ => "Adresa už není https ani existující složka nebo zip — otevřete ji ručně.",
        };
        if (error != null) ShowMessage(error);
    }
}
