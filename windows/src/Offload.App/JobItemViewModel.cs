using System.IO;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.Space;

namespace Offload.App;

public sealed record DestinationLine(string Name, string Sub, string FullPath);

/// One card in the queue: mirrors the job's status every tick (Mac `JobItem` + `JobCard`).
public sealed class JobItemViewModel : QueueItem
{
    readonly MainViewModel owner;
    readonly string sourceName;
    readonly JobProgress progress = new(DateTime.UtcNow);
    IReadOnlyList<RoleLink>? links;
    IReadOnlyList<UsbConnector>? connectors;
    IReadOnlyDictionary<string, string>? portNames;
    IReadOnlyList<DestinationLine>? destinationLines;
    string? filesText;
    List<string> chainLines = [];
    bool chainRequested, ejected, ejecting, openingReport;

    readonly IReadOnlyList<VolumeDemand> space;

    /// `space`: what the card needs on each destination volume (spec 2.5) — counted while this job is queued or copies.
    /// `profileNote`: orange, for the whole life of the card — e.g. an append whose project no longer exists.
    public JobItemViewModel(MainViewModel owner, OffloadJob job, string sourceName, bool canEject, IReadOnlyList<VolumeDemand>? space = null,
        string? profileNote = null)
    {
        this.owner = owner;
        ProfileNote = profileNote;
        this.sourceName = sourceName;
        this.space = space ?? [];
        Job = job;
        CanEject = canEject;
        Runner = new JobQueue.Runner(job, m => AppLog.Write($"{sourceName}: {m}"));
        CancelCommand = new RelayCommand(() => { Job.Cancel(); Refresh(); });
        EjectCommand = new RelayCommand(() => owner.Eject(this), () => !Ejecting);
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
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
        Raise(string.Empty);   // every binding of the card may have changed
    }

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
    public override bool IsFinished => Phase.IsFinished();
    public bool SafeToEject => progress.Status.SafeToEject;
    public bool? CopySucceeded => progress.Status.CopySucceeded;
    /// Failed or copied with errors; a cancelled job is not an error.
    public override bool IsError => progress.IsError;
    public CardTone Tone => progress.Tone;
    public string StatusText => progress.StatusText;
    public double Progress => progress.PhaseProgress;
    public string DetailText => progress.DetailText;

    public string Title => Job.Plan is { Mode: PlanMode.Append } p ? AppendRules.Title(p.CardName) : Job.Plan?.CardName ?? sourceName;
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

    /// Computed once the plan exists (it never changes) — a new list every tick would rebuild the rows.
    public IReadOnlyList<DestinationLine> DestinationLines
    {
        get
        {
            if (destinationLines != null) return destinationLines;
            if (Job.Plan is not { } p) return [];
            return destinationLines = p.DestRoots.Select((root, i) => new DestinationLine(
                Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root, $"› {p.Date}/{p.CardName}", p.CardPaths[i])).ToList();
        }
    }

    /// The hardware chain with live throughput and the brake (spec 8a.3), while copying or verifying.
    /// The same list instance comes back while the lines are unchanged, so the rows are not rebuilt every tick.
    public IReadOnlyList<string> ChainLines
    {
        get
        {
            List<string> lines = [];
            if (links != null && Phase is JobPhase.Copying or JobPhase.Verifying)
            {
                var rates = new List<double> { Phase == JobPhase.Copying ? progress.ReadRate / 1e6 : 0 };
                rates.AddRange(progress.DestRates.Select(r => r / 1e6));
                var chain = ChainView.Build(links, rates, connectors, portNames);
                lines = ChainView.Lines(chain, Phase == JobPhase.Copying ? progress.Brake : null);
            }
            if (!lines.SequenceEqual(chainLines)) chainLines = lines;
            return chainLines;
        }
    }

    public bool HasChain => links != null && Phase is JobPhase.Copying or JobPhase.Verifying;

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
    internal void VolumesChanged() => Raise(nameof(ShowEjectButton));
    public string SafeText => Texts.SafeToEject(Job.Plan?.CardPaths.Count ?? 0);
    // Cancelled (unlike the Mac): no red "N souborů selhalo" and no rate of a copy that was cut short — one orange line.
    bool IsCancelled => Phase == JobPhase.Cancelled;
    public string? FailedText => !IsCancelled && Job.Report is { Success: false } r ? Texts.Failed(r.FailedFiles.Count) : null;
    public string? CancelledText => IsCancelled && !SafeToEject ? "Zrušeno — karta není kompletně zálohovaná." : null;
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
