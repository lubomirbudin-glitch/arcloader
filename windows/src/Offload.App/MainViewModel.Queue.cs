using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Space;

namespace Offload.App;

public sealed partial class MainViewModel
{
    readonly JobQueue queue = new(m => AppLog.Write(m));
    readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(330) };
    bool closing, closeRaised, waitNoticeShown, shownHasFinished, shownCopyRunning, starting;
    string? startStatus;
    // Cancelled when the window closes: the Start waits stop at once and say nothing.
    readonly CancellationTokenSource lifetime = new();

    /// Offloads and verifications, newest on top.
    public ObservableCollection<QueueItem> Jobs { get; } = [];
    public RelayCommand StartCommand { get; private set; } = null!;
    public RelayCommand ClearFinishedCommand { get; private set; } = null!;

    /// A job finished: flash the taskbar button (true = with an error).
    public event Action<bool>? AttentionRequested;
    /// Closing was requested while jobs ran; they are cancelled and finished now — close the window.
    public event Action? CloseReady;
    /// Shows "Karty už jsou zálohované" (set by the window; modal, the user answers — no disk is waited on).
    public Func<BackedUpPromptModel, BackedUpChoice>? AskBackedUp { get; set; }
    /// A Czech yes/no question (set by the window); without it the answer is Ne.
    public Func<string, bool>? AskYesNo { get; set; }

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
    /// The Cíle section is needed only by full copies: appends go to the destinations of their earlier backup (spec 2.2).
    bool ReadyToStart => !closing && selected.Count > 0
        && (!AppendRules.NeedsDestinations(AppendRules.FullCopies(selected, AppendWanted)) || (Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists)))
        && LutProblem == null && ProxyFolderProblem == null && StartProblem == null && SpaceProblem == null;
    /// The hint under the start bar: why Start is off when the setup itself does not say it (a speed measurement runs).
    public string? StartProblem => QueueRules.StartProblem(Measuring);
    public bool HasJobs => Jobs.Count > 0;
    public bool HasFinished => Jobs.Any(j => j.IsFinished);

    /// The LUT and the proxy folder are checked once more right before the start (they may sit on a disk that just
    /// went away) — off the UI thread, at most 5 s each; meanwhile the start button is disabled. Then the inspections
    /// of the selected cards (at most 10 s) and the free space of every destination (at most 5 s) — all awaited, the UI
    /// thread never waits on a card or a disk (spec 2.5, ruling I4).
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
                var check = Task.Run(() => ProbeFolder(folder));
                bool answered = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
                bool exists = answered && check.IsCompletedSuccessfully && check.Result.Exists;
                if (!answered) AppLog.Write($"proxy folder check before start: no answer from {folder} in 5 s");
                if (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) != folder) continue;   // changed meanwhile
                if (answered && check.IsCompletedSuccessfully) ApplyFolderProbe(folder, check.Result);
                else SetProxyFolderState(folder, false);
                if (!exists) return;   // the red text says why
                break;
            }
            // Without the inspections a card history knows would be copied again in full, with no prompt: wait for the
            // ones still looking, at most 10 s (a card that does not answer never blocks the start - then what is known counts).
            List<string> Pending() => StartWait.PendingInspections(SelectedSources(), p => Insight(p) != null, inspections.IsRunning, history != null);
            bool looked = await StartWait.UntilAsync(() => Pending().Count, n => SetStartStatus(StartWait.InspectionStatus(n)),
                TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200), lifetime.Token);
            if (closing) return;
            if (!looked)
            {
                // Ruling S1: not knowing whether a card is backed up, a full copy needs the user's yes (default Ne).
                var names = Pending().Select(PlannedName).ToList();
                AppLog.Write("start: the card inspection did not finish in 10 s");
                SetStartStatus(null);
                if (!StartWait.ContinueAfterWait(false, names, q => AskYesNo?.Invoke(q) ?? false)) return;
                if (closing) return;
            }
            // The free space of every destination is read again now (ruling I4): a fixed disk is otherwise probed only
            // once, long before a running copy filled it.
            SetStartStatus(StartWait.FreeSpaceStatus);
            await ProbeFreeSpaceAsync(TimeSpan.FromSeconds(5));
            if (closing) return;
            SetStartStatus(null);
            if (SpaceProblem != null) return;   // the red text says why
            // Spec 2.1: cards history already knows are copied again in full only when the user says so.
            var skip = new List<string>();
            PruneAppend();   // after the awaits above: the cards may have changed meanwhile
            var backedUp = BackedUpPrompt.CardsToAsk(SelectedSources(), Insight, AppendWanted);
            if (backedUp.Count > 0 && AskBackedUp is { } ask)
            {
                var model = BackedUpPrompt.Build(backedUp.Select(p => (Insight(p)!.History!, PlannedName(p))).ToList(), DateString);
                switch (ask(model))
                {
                    case BackedUpChoice.Cancel:
                        return;
                    case BackedUpChoice.SkipBackedUp:
                        skip.AddRange(backedUp);
                        break;
                }
            }
            if (ReadyToStart) StartJobs(skip);   // anything may have changed while the LUT was checked
        }
        catch (OperationCanceledException) when (closing)
        {
            // the window is closing: nothing to say
        }
        catch (Exception e) when (!closing)
        {
            AppLog.Write($"start: {e}");
            ShowMessage($"Offload se nepodařilo spustit: {e.Message}");
        }
        finally
        {
            starting = false;
            SetStartStatus(null);
            Raise(nameof(CanStart));
        }
    }

    /// `skip`: backed-up cards the user chose not to copy again — they are unticked, not started.
    void StartJobs(IReadOnlyCollection<string> skip)
    {
        var gone = new List<string>();
        var targets = SpaceTargets();
        var selectedNow = SelectedSources();
        // Names as the rows show them now: Placeholder numbers against the cards still ticked, and the loop unticks cards.
        var names = selectedNow.ToDictionary(p => p, p => Placeholder(p).Name, StringComparer.OrdinalIgnoreCase);
        foreach (var src in selectedNow)
        {
            var space = CardSpace(src, targets);   // the job card keeps counting it until it has copied (spec 2.5)
            if (skip.Contains(src, StringComparer.OrdinalIgnoreCase))
            {
                selected.Remove(src);
                continue;
            }
            bool isVolume = volumes.Any(v => string.Equals(v.Path, src, StringComparison.OrdinalIgnoreCase));
            // Decided now, on the newest insight (the prompt above may have taken a while): a ticked append that can no
            // longer be offered is not turned into a full copy — the card stays unstarted and the user is told.
            var (kind, goneName) = appendTicks.Classify(src, Insight(src));
            if (kind == StartKind.AppendGone)
            {
                gone.Add(goneName ?? DefaultCardName(src));
                continue;
            }
            if (kind == StartKind.Append && Insight(src)?.History is { } known)
            {
                // Spec 2.2: only the new files, into the card folders of the earlier backup — not into the Cíle section.
                // The engine plans again under the folder lock and refuses with a Czech message when anything is off.
                var appendConfig = new JobConfig(src, known.Entry.Destinations.Select(d => d.Root).ToList())
                {
                    AllowSameDevice = true,   // the destinations are fixed by the earlier backup (it may have been made with --allow-same-device)
                    AppendInto = known.Entry.Destinations.Select(d => d.CardPath).ToList(),
                    Verify = Settings.Verify,
                    AnalyzeClips = Settings.AnalyzeClips,
                    Proxy = SettingsStore.Clone(Settings.Proxy),
                    Tools = SettingsStore.Clone(Settings.Tools),
                    Outputs = SettingsStore.Clone(Settings.Outputs),
                };
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), AppendRules.Title(known.Entry.CardName),
                    canEject: isVolume && CardEject.CanEject(src), space);
                Jobs.Insert(0, appendItem);
                selected.Remove(src);
                appendTicks.Untick(src);
                queue.Enqueue(appendItem.Runner);
                continue;
            }
            // Exactly what the row shows: the typed name, else a suggestion (spec 2.3); null = the volume label.
            var name = cardNames.GetValueOrDefault(src) is { Length: > 0 } n && n.Trim().Length > 0 ? n.Trim()
                : names[src] is var shown && shown != DefaultCardName(src) ? shown
                : null;
            var config = new JobConfig(src, Settings.DestRoots.ToList())
            {
                CardName = name,
                Date = DateString,
                Hashes = HashKinds.FromSetting(Settings.Hash),   // an append (above) keeps the earlier backup's (spec 3.5)
                Verify = Settings.Verify,
                AnalyzeClips = Settings.AnalyzeClips,
                Proxy = SettingsStore.Clone(Settings.Proxy),
                Tools = SettingsStore.Clone(Settings.Tools),
                Outputs = SettingsStore.Clone(Settings.Outputs),
            };
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src), space);
            Jobs.Insert(0, item);
            selected.Remove(src);
            queue.Enqueue(item.Runner);
        }
        RebuildSources();   // the started cards are unticked
        RaiseMany(nameof(HasJobs), nameof(HasFinished));
        if (gone.Count > 0) ShowMessage(AppendTicks.GoneMessage(gone));
    }

    void Tick()
    {
        foreach (var j in Jobs)
            if (!j.IsFinished) j.Refresh();
        RefreshSpace();   // memory only: a running job needs less as it copies (against its baseline)
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

    QueueItem? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Queued, job));

    void OnCopyFinished(IQueuedJob job)
    {
        if (Find(job) is not JobItemViewModel item) return;   // a verification has no stage 2: OnJobFinished follows
        item.Refresh();
        if (!closing && item.SafeToEject) SystemSounds.Asterisk.Play();
        if (!closing) ReloadHistory();   // the job has just recorded the card: its line says "Zálohováno"
        if (!closing) _ = ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30));   // the disks changed: free space again (ruling I4)
        // Ended in stage 1: only the day report is left and it may queue behind other cards' stage 2 — alert now.
        if (QueueRules.AlertAtCopyEnd(item.Phase)) Alert(item);
    }

    void OnJobFinished(IQueuedJob job)
    {
        if (Find(job) is { } item)
        {
            if (item is JobItemViewModel j) j.LeftQueue = true;
            item.Refresh();
            if (!item.Alerted) Alert(item);
        }
        Raise(nameof(HasFinished));
        CheckCloseReady();
    }

    void Alert(QueueItem item)
    {
        item.Alerted = true;
        bool error = item.IsError;   // a job the user cancelled is no error
        if (closing) return;
        if (error) SystemSounds.Hand.Play();
        AttentionRequested?.Invoke(error);
    }

    /// Still needs the app: a job that has not finished, or an eject in flight.
    static bool Busy(QueueItem j) => j.Busy;

    /// Closing waits for the cancelled jobs and ejects; once all are done the window is told to close — once.
    void CheckCloseReady()
    {
        if (!closing || closeRaised || Jobs.Any(Busy)) return;
        closeRaised = true;
        CloseReady?.Invoke();
    }

    void ClearFinished()
    {
        // Not one the app still waits for (a day report on the post thread, an eject): closing must still see it.
        foreach (var j in Jobs.Where(j => j.IsFinished && !j.Busy).ToList()) Jobs.Remove(j);
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
            lifetime.Cancel();
            inspections.CancelAll();
            foreach (var j in Jobs) j.Cancel();
            foreach (var j in Jobs) j.Refresh();
            Raise(nameof(CanStart));
            // A queued job reports Zrušeno at once; if nothing is left running, close right away.
            if (Jobs.Any(Busy)) return false;
        }
        closeRaised = true;
        closing = true;   // a late continuation must not start another inspection
        lifetime.Cancel();
        inspections.CancelAll();
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
        bool inUse = QueueRules.CardInUse(source, Jobs.OfType<JobItemViewModel>().Where(j => !ReferenceEquals(j, item)).Select(j => (j.Job.Config.Source, j.Phase)));
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
