using System.Collections.ObjectModel;
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
    bool closing, closeRaised, waitNoticeShown, shownHasFinished;

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
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
        RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }

    static void Dispatch(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public string CardsText => Texts.Cards(selected.Count);
    public string DestinationsText => Texts.Destinations(Settings.DestRoots.Count) + (Settings.Proxy.Enabled ? " · proxy" : "");
    public bool CanStart => !closing && selected.Count > 0 && Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists) && LutProblem == null;
    public bool HasJobs => Jobs.Count > 0;
    public bool HasFinished => Jobs.Any(j => j.IsFinished);

    void Start()
    {
        if (!CanStart) return;
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
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? SourceItemViewModel.SafeDefaultName(src),
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
        CommandManager.InvalidateRequerySuggested();
    }

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
            bool error = item.Phase == JobPhase.Failed || item.CopySucceeded == false;
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
        queue.Dispose();
        SaveNow();
        return true;
    }

    internal async void Eject(JobItemViewModel item)
    {
        try
        {
            item.Ejecting = true;
            var result = await Task.Run(() => CardEject.Eject(item.Job.Config.Source));
            item.Ejecting = false;
            if (result.Ok)
            {
                item.Ejected = true;
                RefreshVolumes();
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
