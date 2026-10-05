using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.App;

public sealed partial class MainViewModel
{
    readonly JobQueue queue = new(m => AppLog.Write(m));
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(330) };
    bool closing, closeRaised, waitNoticeShown, shownHasFinished, shownCopyRunning, starting;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];
    public RelayCommand StartCommand { get; private set; } = null!;
    public RelayCommand ClearFinishedCommand { get; private set; } = null!;

    /// A job finished: flash the taskbar button (true = with an error).
    public event Action<bool>? AttentionRequested;
    /// Closing was requested while jobs ran; they are cancelled and finished now — close the window.
    public event Action? CloseReady;

    void InitQueue()
    {
        StartCommand = new RelayCommand(Start, () => CanStart);
        ClearFinishedCommand = new RelayCommand(ClearFinished);
        queue.CopyFinished += j => Dispatch(() => OnCopyFinished(j));
        queue.JobFinished += j => Dispatch(() => OnJobFinished(j));
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart), nameof(StartProblem));
        RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }

    static void Dispatch(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public string CardsText => Texts.Cards(selected.Count);
    public string DestinationsText => Texts.Destinations(Settings.DestRoots.Count) + (Settings.Proxy.Enabled ? " · proxy" : "");
    /// In memory only (evaluated 3×/s): the LUT, the proxy folder and the destinations come from checks made off the UI thread.
    public bool CanStart => !starting && ReadyToStart;
    bool ReadyToStart => !closing && selected.Count > 0 && Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists)
        && LutProblem == null && ProxyFolderProblem == null && StartProblem == null;
    /// The hint under the start bar: why Start is off when the setup itself does not say it (a speed measurement runs).
    public string? StartProblem => QueueRules.StartProblem(Measuring);
    public bool HasJobs => Jobs.Count > 0;
    public bool HasFinished => Jobs.Any(j => j.IsFinished);

    /// The LUT and the proxy folder are checked once more right before the start (they may sit on a disk that just
    /// went away) — off the UI thread, at most 5 s each; meanwhile the start button is disabled.
    async void Start()
    {
        if (!CanStart) return;
        starting = true;
        Raise(nameof(CanStart));
        try
        {
            while (ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) is string lut)
            {
                var check = Task.Run(() => File.Exists(lut));
                bool answered = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
                bool exists = answered && check.IsCompletedSuccessfully && check.Result;
                if (!answered) AppLog.Write($"LUT check before start: no answer from {lut} in 5 s");
                if (ProxyPreflight.LutInUse(Settings.Proxy, Settings.AnalyzeClips) != lut) continue;   // changed meanwhile
                SetLutState(lut, exists);
                if (!exists) return;   // the red text says why
                break;
            }
            while (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) is string folder)
            {
                var check = Task.Run(() => Directory.Exists(folder));
                bool answered = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
                bool exists = answered && check.IsCompletedSuccessfully && check.Result;
                if (!answered) AppLog.Write($"proxy folder check before start: no answer from {folder} in 5 s");
                if (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) != folder) continue;   // changed meanwhile
                SetProxyFolderState(folder, exists);
                if (!exists) return;   // the red text says why
                break;
            }
            if (ReadyToStart) StartJobs();   // anything may have changed while the LUT was checked
        }
        catch (Exception e)
        {
            AppLog.Write($"start: {e}");
            ShowMessage($"Offload se nepodařilo spustit: {e.Message}");
        }
        finally
        {
            starting = false;
            Raise(nameof(CanStart));
        }
    }

    void StartJobs()
    {
        var sources = volumes.Select(v => v.Path).Concat(manualSources).Where(selected.Contains).ToList();
        foreach (var src in sources)
        {
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim() : null;
            var config = new JobConfig(src, Settings.DestRoots.ToList())
            {
                CardName = name,
                Date = DateString,
                Verify = Settings.Verify,
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
                Tools = SettingsStore.Clone(Settings.Tools),
            };
            bool isVolume = volumes.Any(v => string.Equals(v.Path, src, StringComparison.OrdinalIgnoreCase));
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src));
            Jobs.Insert(0, item);
            selected.Remove(src);
            queue.Enqueue(item.Runner);
        }
        RebuildSources();   // the started cards are unticked
        RaiseMany(nameof(HasJobs), nameof(HasFinished));
    }

    void Tick()
    {
        foreach (var j in Jobs)
            if (!j.IsFinished) j.Refresh();
        // A queued job cancelled behind a running copy is finished before the queue reports it.
        if (HasFinished != shownHasFinished)
        {
            shownHasFinished = HasFinished;
            Raise(nameof(HasFinished));
        }
        if (CopyRunning != shownCopyRunning)
        {
            shownCopyRunning = CopyRunning;
            openHardware?.CopyStateChanged();
        }
        CommandManager.InvalidateRequerySuggested();
    }

    /// A copy writes or verifies the destinations — no speed measurement now (it writes 1 GB to them).
    internal bool CopyRunning => QueueRules.MeasureBlocked(Jobs.Select(j => j.Phase));

    /// The copied card is in its drive per the last scan (not pulled out, not replaced by another card).
    internal bool CardInDrive(JobItemViewModel item) => QueueRules.CardInDrive(item.Job.Config.Source, item.Runner.SourceIdentity, volumes);

    JobItemViewModel? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Runner, job));

    void OnCopyFinished(IQueuedJob job)
    {
        if (Find(job) is not { } item) return;
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
    }

    void OnJobFinished(IQueuedJob job)
    {
        if (Find(job) is { } item)
        {
            item.Refresh();
            bool error = item.IsError;   // a job the user cancelled is no error
            if (!closing)
            {
                if (error) SystemSounds.Hand.Play();
                AttentionRequested?.Invoke(error);
            }
        }
        Raise(nameof(HasFinished));
        CheckCloseReady();
    }

    /// Still needs the app: a job that has not finished, or an eject in flight.
    static bool Busy(JobItemViewModel j) => !j.IsFinished || j.Ejecting;

    /// Closing waits for the cancelled jobs and ejects; once all are done the window is told to close — once.
    void CheckCloseReady()
    {
        if (!closing || closeRaised || Jobs.Any(Busy)) return;
        closeRaised = true;
        CloseReady?.Invoke();
    }

    void ClearFinished()
    {
        foreach (var j in Jobs.Where(j => j.IsFinished).ToList()) Jobs.Remove(j);
        RaiseMany(nameof(HasJobs), nameof(HasFinished));
    }

    /// The window asks to close. True = close now. With unfinished jobs: confirm, cancel them all and close once
    /// they have cleaned up (CloseReady). An eject in flight is waited for as well (no question — nothing is lost).
    public bool TryClose(Func<bool> confirm)
    {
        foreach (var j in Jobs) j.Refresh();
        if (Jobs.Any(Busy))
        {
            if (closing)
            {
                // Asked again while the cancelled jobs clean up: say so once (deferred — not inside the Closing event).
                if (!waitNoticeShown)
                {
                    waitNoticeShown = true;
                    Dispatch(() => ShowMessage("Ukončuji — čekám, až se zrušené úlohy uklidí."));
                }
                return false;
            }
            if (Jobs.Any(j => !j.IsFinished) && !confirm()) return false;
            closing = true;
            foreach (var j in Jobs) j.Job.Cancel();
            foreach (var j in Jobs) j.Refresh();
            Raise(nameof(CanStart));
            // A queued job reports Zrušeno at once; if nothing is left running, close right away.
            if (Jobs.Any(Busy)) return false;
        }
        closeRaised = true;
        ticker.Stop();
        healthTimer.Stop();
        queue.Dispose();
        SaveNow();
        return true;
    }

    /// Ejects only the card that was copied: the card in the drive now is identified off the UI thread and compared
    /// with the one recorded when the copy started; never while another job still needs the card.
    internal async void Eject(JobItemViewModel item)
    {
        if (item.Ejecting || item.Ejected) return;   // a double click must not start a second eject
        var source = item.Job.Config.Source;
        var copied = item.Runner.SourceIdentity;
        bool inUse = QueueRules.CardInUse(source, Jobs.Where(j => !ReferenceEquals(j, item)).Select(j => (j.Job.Config.Source, j.Phase)));
        try
        {
            item.Ejecting = true;
            var result = await Task.Run(() => QueueRules.EjectProblem(copied, CardIdentity.Of(source), inUse) is string problem
                ? new EjectResult(false, problem)
                : CardEject.Eject(source));
            item.Ejecting = false;
            if (result.Ok)
            {
                item.Ejected = true;
                CardEjected(source);
            }
            else
            {
                ShowMessage($"Kartu se nepodařilo vysunout: {result.Error}");
            }
        }
        catch (Exception e)
        {
            item.Ejecting = false;
            ShowMessage($"Kartu se nepodařilo vysunout: {e.Message}");
        }
        finally
        {
            CheckCloseReady();   // the window may be waiting for this eject to close
        }
    }
}
