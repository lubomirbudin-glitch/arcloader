using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.App;

public sealed partial class MainViewModel
{
    readonly JobQueue queue = new(m => AppLog.Write(m));
    // What the jobs of this session took — the start bar's estimate (UI návrh 10).
    readonly RateMemory rates = new();
    bool shownCompact, shownJobsRunning;
    DateTime shownDay = DateTime.Today;
    string? shownStartLabel;
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
        StartCommand = new RelayCommand(Start, () => CanStart && Allowed(DemoCommand.Start));
        ClearFinishedCommand = new RelayCommand(ClearFinished);
        queue.CopyFinished += j => Dispatch(() => OnCopyFinished(j));
        queue.JobFinished += j => Dispatch(() => OnJobFinished(j));
        ticker.Tick += (_, _) => Tick();
        ticker.Start();
        SetupChanged += () => RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart), nameof(StartProblem),
            nameof(StartCounts), nameof(StartSize), nameof(EstimateText), nameof(AfterCopyRows), nameof(AfterCopyLine), nameof(PathPreview),
            nameof(FlowSteps), nameof(QueueEmptyTitle), nameof(QueueEmptyText));
        RaiseMany(nameof(CardsText), nameof(DestinationsText), nameof(CanStart));
    }

    static void Dispatch(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public string CardsText => Texts.Cards(selected.Count);

    // ── The start bar and step 3 (UI návrh obr. 1) ─────────────────────────────────────────────────────────────
    /// What each ticked card will copy (an append only its new files), null while one is not known yet.
    long? SelectedBytes => SetupTexts.Total(SelectedSources().Select(p => AppendChosen(p) && Insight(p)?.History is { } h
        ? h.NewBytes : Insight(p) is { Error: null } i ? i.TotalBytes : (long?)null));

    /// "1 karta → 2 disky" (bold) and " · 1,70 GB".
    public string StartCounts => SetupTexts.StartSummary(selected.Count, Settings.DestRoots.Count, SelectedBytes).Counts;
    public string? StartSize => SetupTexts.StartSummary(selected.Count, Settings.DestRoots.Count, SelectedBytes).Size;

    /// "odhad ~8:00 vč. proxy" — only from what this session measured (RateMemory); null shows only the size.
    public string? EstimateText => SelectedBytes is long bytes && selected.Count > 0
        ? RateMemory.StartText(rates.Estimate(bytes, SelectedSources().SelectMany(p => Insight(p)?.Clips ?? []).ToList(), Settings.Verify,
            Settings.AnalyzeClips, Settings.Proxy), Settings.Proxy.Enabled)
        : null;

    /// A waiting card stands behind a job paused in its copy (memory only).
    internal bool BlockedByPaused(JobItemViewModel item) =>
        QueueRules.BlockedByPaused(item.Phase, Jobs.OfType<JobItemViewModel>().Where(j => !ReferenceEquals(j, item)).Select(j => (j.Phase, j.IsPaused)));

    /// "Spustit offload", or "Zařadit do fronty" while a card waits for or is in its copy.
    public string StartLabel => QueueSummary.StartLabel(Jobs.Select(j => j.Phase));

    /// No card and jobs in the queue: steps 2 and 3 compact (obr. 2, 3).
    public bool IsCompact => SetupTexts.Compact(HasSources, HasJobs);

    bool JobsRunning => Jobs.Any(j => !j.IsFinished);
    public string NoCardTitle => SetupTexts.NoCard(JobsRunning).Title;
    public string NoCardText => SetupTexts.NoCard(JobsRunning).Text;

    /// Step 3: Ověření · Analýza · Proxy · Report of the profile shown (the camera of the first ticked card names the LUT).
    public IReadOnlyList<AfterCopyRow> AfterCopyRows =>
        SetupTexts.AfterCopy(Settings, lutCatalog, SelectedSources().Select(p => Insight(p)?.Camera).FirstOrDefault(c => c != null));
    public string AfterCopyLine => SetupTexts.AfterCopyLine(Settings);
    public string VerifyOffWarning => SetupTexts.VerifyOffWarning;

    // ── The empty queue (obr. 1) ───────────────────────────────────────────────────────────────────────────────
    /// Karta → 2 disky naráz → Ověření → Proxy → Report (only what the profile shown does).
    public IReadOnlyList<FlowStep> FlowSteps => SetupTexts.Flow(Settings.DestRoots.Count, Settings.Verify, Settings.AnalyzeClips, Settings.Proxy.Enabled)
        .Select((label, i) => new FlowStep(label, i switch
        {
            0 => "card",
            1 => "disk",
            _ => label switch { "Ověření" => "verify", "Proxy" or "Analýza" => "proxy", _ => "report" },
        }, i == 0)).ToList();

    /// "Připraveno — A035_ARRI na 2 disky" with a ticked card, else "Zatím žádný offload".
    public string QueueEmptyTitle => SetupTexts.QueueEmpty(SelectedSources().Select(PlannedName).ToList(), Settings.DestRoots.Count, Settings.Verify).Title;
    public string QueueEmptyText => SetupTexts.QueueEmpty(SelectedSources().Select(PlannedName).ToList(), Settings.DestRoots.Count, Settings.Verify).Text;

    /// " 2" after "Fronta" (the unfinished jobs), nothing for none.
    public string QueueBadgeText => QueueBadge > 0 ? $" {QueueBadge}" : "";

    /// The parts of the left column that follow the queue (checked every tick, raised only when they change).
    void RefreshLayout()
    {
        // DNES and the day titles of the history tab move on at midnight (memory only, history as loaded).
        if (DateTime.Today != shownDay)
        {
            shownDay = DateTime.Today;
            RaiseMany(nameof(Today), nameof(HistoryDays));
        }
        if (IsCompact != shownCompact)
        {
            shownCompact = IsCompact;
            Raise(nameof(IsCompact));
        }
        if (JobsRunning != shownJobsRunning)
        {
            shownJobsRunning = JobsRunning;
            RaiseMany(nameof(NoCardTitle), nameof(NoCardText));
        }
        if (StartLabel != shownStartLabel)
        {
            shownStartLabel = StartLabel;
            Raise(nameof(StartLabel));
        }
    }
    public string DestinationsText => Texts.Destinations(Settings.DestRoots.Count) + (Settings.Proxy.Enabled ? " · proxy" : "");
    /// In memory only (evaluated 3×/s): the LUT, the proxy folder and the destinations come from checks made off the UI thread.
    public bool CanStart => !starting && ReadyToStart;
    /// The Cíle section is needed only by full copies: appends go to the destinations of their earlier backup (spec 2.2).
    bool ReadyToStart => !closing && selected.Count > 0
        && (!AppendRules.NeedsDestinations(AppendRules.FullCopies(selected, AppendWanted)) || (Settings.DestRoots.Count > 0 && Destinations.All(d => d.Exists)))
        && !ShownCheckMissing() && StartProblem == null && SpaceProblem == null;

    /// The LUTs and proxy folders a Start now would use (memory only): the profile shown only with a full copy selected,
    /// and the profile of every append's project (fix round 1, item 2a).
    List<StartCheck> StartChecksNow()
    {
        var sources = SelectedSources();
        var appends = sources.Where(AppendWanted).Select(Insight).Where(AppendRules.Offer).Select(i => i!.History!.Entry).ToList();
        return [.. AppendRules.StartChecks(AllSettings, AppendRules.FullCopies(sources, AppendWanted), appends)];
    }

    /// A LUT or proxy folder of the profile shown that this Start uses and the last check found missing (its red text
    /// says why). The profile shown does not block a Start of appends only — they do not use it.
    bool ShownCheckMissing() => StartChecksNow().Any(c => c.Shown && (c.Kind == StartCheckKind.Lut
        ? lutMissing != null && lutCheckedKey == ProxyPreflight.LutKey(LutFiles) && string.Equals(c.Path, lutMissing.Path, StringComparison.OrdinalIgnoreCase)
        : folderExists == false && string.Equals(c.Path, folderCheckedPath, StringComparison.OrdinalIgnoreCase)));
    /// The hint under the start bar: why Start is off when the setup itself does not say it (a speed measurement runs).
    public string? StartProblem => QueueRules.StartProblem(Measuring);
    public bool HasJobs => Jobs.Count > 0;
    public bool HasFinished => Jobs.Any(j => j.IsFinished);

    /// The LUTs and proxy folders the start uses are checked once more right before it (they may sit on a disk that just
    /// went away) — off the UI thread, at most 5 s each; meanwhile the start button is disabled. Then the inspections
    /// of the selected cards (at most 10 s) and the free space of every destination (at most 5 s) — all awaited, the UI
    /// thread never waits on a card or a disk (spec 2.5, ruling I4).
    async void Start()
    {
        if (!CanStart || !Allowed(DemoCommand.Start)) return;
        starting = true;
        ProjectMenuOpen = false;
        RaiseMany(nameof(CanStart), nameof(CanSwitchProject), nameof(AfterCopyEnabled));
        // Ruling I1: every check below is made for the profile shown now. The project switcher and the Projekt menu are
        // off until Start ends; should another profile be shown anyway, Start stops — its checks were not for that one.
        var stamp = ProfileStamp.Of(AllSettings);
        bool Switched()
        {
            if (!stamp.ChangedIn(AllSettings)) return false;
            AppLog.Write("start: another project was shown during the checks — not started");
            ShowMessage(ProfileStamp.NotStarted);
            return true;
        }
        try
        {
            var verified = new HashSet<string>(StringComparer.Ordinal);
            if (!await CheckStartFilesAsync(verified, Switched)) return;
            // Without the inspections a card history knows would be copied again in full, with no prompt: wait for the
            // ones still looking, at most 10 s (a card that does not answer never blocks the start - then what is known counts).
            List<string> Pending() => StartWait.PendingInspections(SelectedSources(), p => Insight(p) != null, inspections.IsRunning, history != null);
            bool looked = await StartWait.UntilAsync(() => Pending().Count, n => SetStartStatus(StartWait.InspectionStatus(n)),
                TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200), lifetime.Token);
            if (closing || Switched()) return;
            if (!looked)
            {
                // Ruling S1: not knowing whether a card is backed up, a full copy needs the user's yes (default Ne).
                var names = Pending().Select(PlannedName).ToList();
                AppLog.Write("start: the card inspection did not finish in 10 s");
                SetStartStatus(null);
                if (!StartWait.ContinueAfterWait(false, names, q => AskYesNo?.Invoke(q) ?? false)) return;
                if (closing || Switched()) return;
            }
            // The free space of every destination is read again now (ruling I4): a fixed disk is otherwise probed only
            // once, long before a running copy filled it.
            SetStartStatus(StartWait.FreeSpaceStatus);
            await ProbeFreeSpaceAsync(TimeSpan.FromSeconds(5));
            if (closing || Switched()) return;
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
                if (Switched()) return;
            }
            // A card ticked for an append meanwhile may use another project's LUT: checked too (nothing to do otherwise).
            if (!await CheckStartFilesAsync(verified, Switched)) return;
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
            RaiseMany(nameof(CanStart), nameof(CanSwitchProject), nameof(AfterCopyEnabled));
            AutoDrivesAfterStart();   // a disk connected during the checks is added now
        }
    }

    /// Every LUT and proxy folder this Start uses (StartChecksNow), checked off the UI thread, at most 5 s each. One of the
    /// profile shown keeps its answer for the red text, which says why; another project's is refused naming the project.
    /// False = do not start. `verified`: what already passed during this Start (checked once).
    async Task<bool> CheckStartFilesAsync(HashSet<string> verified, Func<bool> switched)
    {
        static string Key(StartCheck c) => $"{c.Kind}|{c.Path.ToUpperInvariant()}";
        while (StartChecksNow().FirstOrDefault(c => !verified.Contains(Key(c))) is { } c)
        {
            bool exists;
            if (c.Kind == StartCheckKind.Lut)
            {
                var check = Task.Run(() => File.Exists(c.Path));
                bool answered = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
                if (switched()) return false;
                exists = answered && check.IsCompletedSuccessfully && check.Result;
                if (!answered) AppLog.Write($"LUT check before start: no answer from {c.Path} in 5 s");
                if (!StartChecksNow().Any(n => Key(n) == Key(c))) continue;   // changed meanwhile
                if (c.Shown)
                {
                    // The shown profile's red text: a rule's file names its rule (LutFile.Rule), not just the file.
                    if (!exists) SetLutState(ProxyPreflight.LutKey(LutFiles), new LutFile(c.Path, c.Rule));
                    else if (lutMissing is { } was && string.Equals(was.Path, c.Path, StringComparison.OrdinalIgnoreCase))
                        SetLutState(ProxyPreflight.LutKey(LutFiles), null);
                }
            }
            else
            {
                var check = Task.Run(() => ProbeFolder(c.Path));
                bool answered = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))) == check;
                if (switched()) return false;
                exists = answered && check.IsCompletedSuccessfully && check.Result.Exists;
                if (!answered) AppLog.Write($"proxy folder check before start: no answer from {c.Path} in 5 s");
                if (!StartChecksNow().Any(n => Key(n) == Key(c))) continue;   // changed meanwhile
                if (c.Shown)
                {
                    if (answered && check.IsCompletedSuccessfully) ApplyFolderProbe(c.Path, check.Result);
                    else SetProxyFolderState(c.Path, false);
                }
            }
            if (!exists)
            {
                if (!c.Shown) ShowMessage(c.Problem);   // the profile shown: the red text says why
                return false;
            }
            verified.Add(Key(c));
        }
        return true;
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
                // Ruling I2: with the profile of the project that backup was made in, not the one shown now.
                var appendConfig = JobConfigs.Append(AllSettings, src, known.Entry);
                string? profileNote = null;
                if (AppendRules.ProfileFor(AllSettings, known.Entry.Project).Gone)
                {
                    // Deleted or renamed since: "Bez projektu", never the project shown (fix round 1, item 1).
                    AppLog.Write($"append {src}: project {known.Entry.Project} no longer exists — the Bez projektu settings are used");
                    profileNote = AppendRules.GoneNote(known.Entry.Project!);
                }
                var appendItem = new JobItemViewModel(this, new OffloadJob(appendConfig), known.Entry.CardName,
                    canEject: isVolume && CardEject.CanEject(src), space, profileNote,
                    new JobCardFacts(known.New.Count, ClipDetector.Detect(known.New).Count, known.NewBytes, null, Append: true));
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
            // The profile shown now, as the job's own copy (a later switch or edit does not change it), and its project.
            var config = JobConfigs.Full(AllSettings, src, name, DateString);
            var item = new JobItemViewModel(this, new OffloadJob(config), name ?? DefaultCardName(src),
                canEject: isVolume && CardEject.CanEject(src), space, facts: Insight(src) is { Error: null } look
                    ? new JobCardFacts(look.FileCount, look.Clips.Count, look.TotalBytes, look.Camera?.Camera, Append: false) : null);
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
        RefreshQueueSummary();   // the top bar's pill and the queue tab's number
        RefreshLayout();   // compact steps, the empty card text, the start button's label
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

    /// "Zkopírovat znovu" needs the card the job copied: a drive root must hold that card now; a folder is taken as there
    /// (the repair's planner reads it first and refuses a changed one). Memory only.
    internal bool SourcePresent(JobItemViewModel item)
    {
        var source = item.Job.Config.Source;
        try
        {
            bool driveRoot = string.Equals(Layout.DriveRootOf(source)?.TrimEnd('\\'), source.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            return !driveRoot || CardInDrive(item);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// A repair of the same backup is queued or running (one at a time; the folder lock would refuse a second).
    internal bool RepairPending(JobItemViewModel item) => item.Job.Plan is { } plan && Jobs.OfType<JobItemViewModel>().Any(j =>
        !ReferenceEquals(j, item) && !j.IsFinished && j.Job.Config.RepairInto is { } into && RepairRules.SameBackup(into, plan.CardPaths));

    /// A later repair of this backup has finished successfully (the jobs above it in the list were started after it).
    internal bool RepairedLater(JobItemViewModel item) => item.Job.Plan is { } plan && RepairRules.RepairedLater(plan.CardPaths,
        Jobs.TakeWhile(j => !ReferenceEquals(j, item)).OfType<JobItemViewModel>().Select(j => (j.Job.Config.RepairInto, j.Phase, j.Job.Report)));

    /// The finished cards show the repair button by what the queue holds now (they are not refreshed every tick).
    void RaiseRepairOffers()
    {
        foreach (var j in Jobs.OfType<JobItemViewModel>())
            if (j.IsFinished) j.RepairChanged();
    }

    /// "Zkopírovat znovu" (UI návrh 4): a repair job — only the failed files, only where they failed — with the finished
    /// job's own settings; it waits in the copy queue like any card (it reads the card).
    internal void Repair(JobItemViewModel item)
    {
        if (closing || !Allowed(DemoCommand.Repair) || item.Job.Plan is not { } plan || !item.CanRepair) return;
        // The finished job's own settings and the identity of the card it copied (ruling I1): another card is refused.
        var repair = new JobItemViewModel(this, new OffloadJob(JobConfigs.Repair(item.Job)), plan.CardName, item.CanEject);
        Jobs.Insert(0, repair);
        queue.Enqueue(repair.Runner);
        RaiseMany(nameof(HasJobs), nameof(HasFinished));
        RaiseRepairOffers();   // the offer on the finished card goes while this repair is queued
    }

    /// The rest of a running job (its phase's ETA and what follows), from what this session measured; null when not known.
    internal double? RemainingTotal(JobItemViewModel item, double? phaseEta) => item.Job.Plan is { } plan
        ? rates.Remaining(item.Phase, phaseEta, plan.Scan.TotalBytes, item.PlanClips, item.Job.Config.Verify,
            item.Job.Config.AnalyzeClips && plan.Mode != PlanMode.Repair, plan.Mode == PlanMode.Repair ? new ProxySettings() : item.Job.Config.Proxy)
        : null;

    /// Ruling I2 (plan 10): the success sound only when every copy is verified everywhere and nothing needs a look
    /// (JobOutcomes.Chime) — never for "Lze vyjmout" alone; once per job.
    void Chime(JobItemViewModel item)
    {
        if (closing || item.Chimed || !Offload.Core.Notify.JobOutcomes.Chime(item.Job.Status())) return;
        item.Chimed = true;
        SystemSounds.Asterisk.Play();
    }

    QueueItem? Find(IQueuedJob job) => Jobs.FirstOrDefault(j => ReferenceEquals(j.Queued, job));

    void OnCopyFinished(IQueuedJob job)
    {
        if (Find(job) is not JobItemViewModel item) return;   // a verification has no stage 2: OnJobFinished follows
        item.Refresh();
        Chime(item);   // ruling I2: only once everything is verified — with verify-in-proxy at the end of stage 2
        NotifyCopyDone(item);   // spec 4.3: "kartu lze vyjmout" while stage 2 still runs
        if (!closing) ReloadHistory();   // the job has just recorded the card: its line says "Zálohováno"
        if (!closing) _ = ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30));   // the disks changed: free space again (ruling I4)
        // Ended in stage 1: only the day report is left and it may queue behind other cards' stage 2 — alert now.
        if (QueueRules.AlertAtCopyEnd(item.Phase)) Alert(item);
        if (QueueRules.NotifyAtCopyEnd(item.Phase, item.Notified) && item is JobItemViewModel ended)
        {
            ended.Notified = true;
            NotifyJobEnd(ended);   // not after the day report, which may queue for hours
        }
    }

    void OnJobFinished(IQueuedJob job)
    {
        if (Find(job) is { } item)
        {
            if (item is JobItemViewModel j)
            {
                j.LeftQueue = true;
                if (j.Job.Report is { } r) rates.Learn(r, j.Job.Config.AnalyzeClips, j.Job.Config.Proxy, j.Stage2Elapsed, endedAs: j.Phase);   // the next estimate (M2)
            }
            item.Refresh();
            if (item is JobItemViewModel finished) Chime(finished);
            if (!item.Alerted) Alert(item);
            if (QueueRules.NotifyAtJobEnd(item.Notified) && item is JobItemViewModel ended)
            {
                ended.Notified = true;
                NotifyJobEnd(ended);   // spec 4.3
            }
        }
        Raise(nameof(HasFinished));
        RaiseRepairOffers();   // a finished repair takes the button off the earlier card
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
            RaiseMany(nameof(CanStart), nameof(CanSwitchProject), nameof(AfterCopyEnabled));
            // A queued job reports Zrušeno at once; if nothing is left running, close right away.
            if (Jobs.Any(Busy)) return false;
        }
        closeRaised = true;
        closing = true;   // a late continuation must not start another inspection
        lifetime.Cancel();
        inspections.CancelAll();
        ticker.Stop();
        healthTimer.Stop();
        updateTimer.Stop();
        queue.Dispose();
        SaveNow();
        return true;
    }

    /// Ejects only the card that was copied: the card in the drive now is identified off the UI thread and compared
    /// with the one recorded when the copy started; never while another job still needs the card.
    internal async void Eject(JobItemViewModel item)
    {
        if (!Allowed(DemoCommand.Eject) || item.Ejecting || item.Ejected) return;   // a double click must not start a second eject
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
