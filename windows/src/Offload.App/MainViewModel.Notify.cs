using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.App;

/// Spec 4.3: at the end of a job a Windows notification (when the window is not in front) and, when switched on, an
/// ntfy push — both from the job's own copy of the settings (the project it was started in). The sound and the
/// taskbar flash stay as they were. Nástroje → Oznámení… edits the active profile's `notify`.
public sealed partial class MainViewModel
{
    /// Shows a Windows notification (set by the window; it knows whether it is in front).
    public Action<JobOutcome>? ShowNotification { get; set; }
    /// The main window is in front: no Windows notification.
    public Func<bool>? WindowInFront { get; set; }
    /// Opens Nástroje → Oznámení… with a copy of the active profile's settings; returns the edited copy or null.
    public Func<NotifySettings, NotifySettings?>? EditNotifyDialog { get; set; }

    public RelayCommand NotifySettingsCommand { get; private set; } = null!;

    void InitNotify() => NotifySettingsCommand = new RelayCommand(EditNotify, () => !closing && Allowed(DemoCommand.Notify));

    void EditNotify()
    {
        if (!Allowed(DemoCommand.Notify)) return;
        if (EditNotifyDialog?.Invoke(SettingsStore.Clone(Settings.Notify)) is not { } edited) return;
        Settings.Notify = edited;
        SettingsChanged();
        Raise(nameof(NotifySummary));
    }

    /// End of stage 1, stage 2 still running and the card safe to eject: "kartu lze vysunout" (Windows only).
    void NotifyCopyDone(JobItemViewModel item)
    {
        if (closing || !Allowed(DemoCommand.Notify) || !item.Job.Config.Notify.Windows) return;
        if (JobOutcomes.CopyDone(item.CardName, item.Job.Status(), item.Job.Config.Proxy.Enabled) is { } outcome) Windows(outcome);
    }

    /// The job has ended: Windows (when not in front) and the phone (fire and forget — the job never waits for it).
    void NotifyJobEnd(JobItemViewModel item)
    {
        if (closing || !Allowed(DemoCommand.Notify)) return;
        var config = item.Job.Config;
        if (JobOutcomes.Final(item.CardName, item.Job.Status(), item.Job.Report) is not { } outcome) return;
        if (config.Notify.Windows) Windows(outcome);
        var name = item.CardName;
        // Ruling M1: off the UI thread (the WinHTTP proxy lookup and the request build are synchronous), bounded by the
        // client's 10 s, and a failure never reaches the job: SendOutcomeAsync never throws, the catch is a second belt.
        var ntfy = config.Notify.Ntfy;
        _ = Task.Run(async () =>
        {
            try { await NtfyClient.SendOutcomeAsync(ntfy, outcome, m => AppLog.Write($"{name}: {m}")).ConfigureAwait(false); }
            catch (Exception e) { AppLog.Write($"{name}: ntfy: {e.Message}"); }
        });
    }

    void Windows(JobOutcome outcome)
    {
        if (WindowInFront?.Invoke() == true) return;
        ShowNotification?.Invoke(outcome);
    }
}
