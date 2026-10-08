using System.IO;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.Space;

namespace Offload.App;

/// One card in the queue: mirrors the job's status every tick (Mac `JobItem` + `JobCard`).
public sealed partial class JobItemViewModel : QueueItem
{
    readonly MainViewModel owner;
    readonly string sourceName;
    readonly JobProgress progress = new(DateTime.UtcNow);
    IReadOnlyList<RoleLink>? links;
    IReadOnlyList<UsbConnector>? connectors;
    IReadOnlyDictionary<string, string>? portNames;
    string? filesText;
    bool chainRequested, ejected, ejecting, openingReport;

    readonly IReadOnlyList<VolumeDemand> space;
    // What the card header says before the plan exists (from the look at the card when it was started).
    readonly JobCardFacts? facts;
    CardHeadline headline;
    IReadOnlyList<string>? destNames;

    /// `space`: what the card needs on each destination volume (spec 2.5) — counted while this job is queued or copies.
    /// `profileNote`: orange, for the whole life of the card — e.g. an append whose project no longer exists.
    /// `facts`: the clips, size and camera the card had when it was started (the header while the job waits).
    public JobItemViewModel(MainViewModel owner, OffloadJob job, string sourceName, bool canEject, IReadOnlyList<VolumeDemand>? space = null,
        string? profileNote = null, JobCardFacts? facts = null)
    {
        this.owner = owner;
        this.facts = facts;
        headline = new CardHeadline(JobCardKind.Waiting, sourceName, "");
        ProfileNote = profileNote;
        this.sourceName = sourceName;
        this.space = space ?? [];
        Job = job;
        CanEject = canEject;
        Runner = new JobQueue.Runner(job, m => AppLog.Write($"{sourceName}: {m}"));
        CancelCommand = new RelayCommand(() => { if (owner.AllowedFor(DemoCommand.Cancel, Job.IsPreview)) { Job.Cancel(); Refresh(); } },
            () => owner.AllowedFor(DemoCommand.Cancel, Job.IsPreview));
        EjectCommand = new RelayCommand(() => owner.Eject(this), () => !Ejecting && owner.Allowed(DemoCommand.Eject));
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
        InitCard();
    }

    public OffloadJob Job { get; }
    /// "Projekt Film X už neexistuje — doplnění běží s nastavením Bez projektu" (AppendRules.GoneNote), or null.
    public string? ProfileNote { get; }
    public JobQueue.Runner Runner { get; }
    public bool CanEject { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand EjectCommand { get; }
    public RelayCommand OpenReportCommand { get; }
    public RelayCommand RevealCommand { get; }
    public override IQueuedJob Queued => Runner;
    public override void Cancel() => Job.Cancel();
    /// Also the day report the post thread still makes after a job that ended in stage 1 (ruling I2): closing waits for
    /// it as for stage 2 — until the queue reports the job finished (also when it never got to the post thread).
    public override bool Busy => QueueRules.StillNeeded(Phase, Ejecting, Job.DayReportPending, LeftQueue);
    /// JobQueue.JobFinished has fired for this job (set on the UI thread).
    public bool LeftQueue { get; set; }

    public override void Refresh()
    {
        progress.Update(Job.Status(), DateTime.UtcNow);
        if (!chainRequested && Job.Plan is { } plan && progress.Status.Phase == JobPhase.Copying)
        {
            chainRequested = true;
            ResolveChain(plan);
        }
        // The whole backup's state comes from the job (read on its thread); a waiting card says when a paused job holds it.
        headline = JobCard.Headline(CardName, progress.Status, Job.Report, DestNames, Job.Backup, owner.BlockedByPaused(this));
        RefreshCard();
        Raise(string.Empty);   // every binding of the card may have changed
    }

    // ── The new card (UI návrh obr. 2, 3) ──────────────────────────────────────────────────────────────────────
    /// Waiting, Running, Done (green), Problem (yellow), Failed (red), Cancelled (grey) — which look the card has.
    public JobCardKind Kind => headline.Kind;
    /// "A035_ARRI — kartu lze vysunout" / "B012_FX6 — kartu zatím nevysunujte" …
    public string HeadTitle => headline.Title;
    /// "Všech 24 souborů je na 2 discích a ověřených (xxHash64)." / "1 soubor na disku SHUTTLE_B nesouhlasí s kartou. …"
    public string HeadSub => headline.Sub;

    /// The destinations as the card names them ("RAID_A").
    internal IReadOnlyList<string> DestNames => destNames ??= JobCard.DiskNames(Job.Plan?.DestRoots ?? Job.Config.DestRoots);

    /// "Čeká na pozastavenou úlohu." under a waiting card that stands behind a paused job, or null.
    public string? WaitingHint => JobCard.WaitingHint(headline);

    /// Minor M12: every card folder of the job, one per line (the tooltip of the path on a finished card).
    public string? PathsTooltip => Job.Plan is { } plan ? string.Join("\n", plan.CardPaths) : null;

    /// "24 klipů · 1,70 GB · ALEXA 35"; before the plan, what the card had when it was started.
    public string Sub
    {
        get
        {
            if (Job.Plan is { } p)
                return JobCard.Sub(new JobCardFacts(p.Scan.Files.Count, PlanClips.Count, p.Scan.TotalBytes, facts?.Camera, p.Mode == PlanMode.Append));
            return facts is { } f ? JobCard.Sub(f) : FilesText;
        }
    }

    /// The waiting row: "B012_FX6" + " — doplnění" (or " — oprava").
    public string TitleName => CardName;
    public string TitleSuffix => Job.Config.AppendInto != null ? " — doplnění" : Job.Config.RepairInto != null ? " — oprava" : "";

    /// "38 %" — the phase's share (the indicator shows which phase).
    public string PercentText => $"{Math.Round(Progress * 100):0} %";

    /// "…\2026-10-07\A035_ARRI\" on the finished card.
    public string PathText => Job.Plan is { } plan ? SetupTexts.PathPreview(plan.Date, plan.CardName) : "";

    /// Where the card and each destination are connected — resolved once, off the UI thread.
    void ResolveChain(OffloadPlan plan)
    {
        var names = new Dictionary<string, string>(owner.AllSettings.PortNames, StringComparer.OrdinalIgnoreCase);
        Task.Run(() =>
        {
            var l = new List<RoleLink> { new("Karta", DriveLinks.Resolve(plan.Source)) };
            l.AddRange(plan.DestRoots.Select((d, i) => new RoleLink($"Cíl {i + 1}", DriveLinks.Resolve(d))));
            return (Links: l, Connectors: UsbScanner.Scan().Connectors);
        }).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                AppLog.Write($"{sourceName}: chain: {t.Exception?.GetBaseException().Message}");
                return;
            }
            links = t.Result.Links;
            connectors = t.Result.Connectors;
            portNames = names;
            progress.AttachBrakeMeter(new BrakeMeter((long)Job.Config.ChunkMB << 20, Job.Config.Buffers,
                links.Select(ChainView.LinkCeiling).ToList()));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public override JobPhase Phase => progress.Status.Phase;

    /// Bytes written so far to the least written destination (the safe side); 0 before the copy.
    internal long WrittenNow() => progress.Status.Copy?.WrittenBytes is { Length: > 0 } w ? w.Min() : 0;

    /// What this job still needs (spec 2.5): all of it while queued, the rest while copying - `writtenAtProbe` is what had
    /// been written when the destinations' free space was last read (ruling I4: never what was written since, the free
    /// figure does not show it yet) - proxies and report in stage 2, nothing once finished.
    internal IEnumerable<VolumeDemand> SpaceDemands(long writtenAtProbe) =>
        space.Select(d => d with { Need = SpaceCheck.Remaining(d.Need, Phase, writtenAtProbe) });
    public bool IsRunning => progress.IsRunning;
    public override bool IsPaused => progress.Status.Paused;
    /// The copy or the read-back really waits now (the pill counts only these as paused).
    public override bool IsPauseHeld => progress.Status.PauseHeld;
    /// How long stage 2 took without pauses, once finished (RateMemory learns the next estimate from it).
    public TimeSpan? Stage2Elapsed => progress.Stage2Elapsed;
    public override bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    /// R5 (plan 10): every copy verified everywhere — the one green verdict (ruling I2).
    public bool SafeToFormat => progress.Status.SafeToFormat;
    /// Ruling I2: the success sound plays once, when the job first reaches it (JobOutcomes.Chime).
    internal bool Chimed { get; set; }
    public bool? CopySucceeded => progress.Status.CopySucceeded;
    /// Failed or copied with errors; a cancelled job is not an error.
    public override bool IsError => progress.IsError;
    public CardTone Tone => progress.Tone;
    public string StatusText => progress.StatusText;
    public double Progress => progress.PhaseProgress;
    public string DetailText => progress.DetailText;

    public string Title => Job.Plan switch
    {
        { Mode: PlanMode.Append } p => AppendRules.Title(p.CardName),
        { Mode: PlanMode.Repair } p => RepairRules.Title(p.CardName),
        { } p => p.CardName,
        _ => sourceName,
    };
    /// The card folder's name (notifications, spec 4.3).
    internal string CardName => Job.Plan?.CardName ?? sourceName;
    public string FilesText
    {
        get
        {
            if (filesText == null && Job.Plan is { } p) filesText = Texts.Files(p.Scan.Files.Count, p.Scan.TotalBytes);
            return filesText ?? "";
        }
    }


    public bool Ejected
    {
        get => ejected;
        set { ejected = value; Raise(string.Empty); }
    }

    public bool Ejecting
    {
        get => ejecting;
        set { ejecting = value; Raise(string.Empty); }
    }

    public bool ShowSafe => SafeToEject && !Ejected;
    /// Only while the copied card is in its drive per the last scan (pulled out or replaced by another card: hidden).
    public bool ShowEjectButton => ShowSafe && CanEject && owner.CardInDrive(this);

    /// A volume scan finished (a finished card is not refreshed every tick).
    internal void VolumesChanged() => RaiseMany(nameof(ShowEjectButton), nameof(CanRepair));
    /// A repair of this backup was queued or finished: the "Zkopírovat znovu" button may have to go (a finished card is not refreshed).
    internal void RepairChanged() => Raise(nameof(CanRepair));
    /// R5: "lze vyjmout" while the verification still runs, "lze vyjmout i formátovat" once it is done.
    public string SafeText => SafeToFormat ? Texts.SafeToFormat(Job.Plan?.CardPaths.Count ?? 0) : Texts.SafeToEject(Job.Plan?.CardPaths.Count ?? 0);
    // Cancelled (unlike the Mac): no red "N souborů selhalo" and no rate of a copy that was cut short — one orange line.
    bool IsCancelled => Phase == JobPhase.Cancelled;
    public string? ErrorText => progress.Status.Error;
    /// "Nic nového — záloha je úplná." (spec 2.2) and similar information.
    public string? NoteText => progress.Status.Note;
    /// Orange, never an error: e.g. "5 souborů z dřívější zálohy nebylo ověřeno — doporučeno Ověřit zálohu."
    public string? WarningText => progress.Status.Warning;
    public string? SummaryText => IsFinished && !IsCancelled && Job.Report is { } r ? JobProgress.SummaryText(r) : null;
    public bool HasReport => Job.Report != null;
    public bool CanCancel => IsRunning || Phase == JobPhase.Waiting;

    /// The report from the first destination that has it (one may be disconnected), looked up off the UI thread.
    async void OpenReport()
    {
        if (Job.Plan is not { } plan || openingReport) return;
        openingReport = true;
        try
        {
            var cards = plan.CardPaths;
            var name = plan.CardName;
            var generation = plan.Generation;
            var path = await Task.Run(() => QueueRules.FirstReport(cards, name, generation));
            if (path == null)
            {
                owner.ShowMessage("Report nenalezen na žádném cíli:\n" + string.Join("\n", cards.Select(c => QueueRules.ReportPath(c, name, generation))));
                return;
            }
            if (Shell.Open(path) is string error) owner.ShowMessage(error);
        }
        catch (Exception e)
        {
            owner.ShowMessage($"Report nejde otevřít: {e.Message}");
        }
        finally
        {
            openingReport = false;
        }
    }

    void Reveal()
    {
        if (Job.Plan is { } plan && Shell.Reveal(plan.CardPaths[0]) is string error) owner.ShowMessage(error);
    }
}
