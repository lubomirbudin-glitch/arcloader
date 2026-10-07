using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.App;

/// One segment of the phase indicator, updated in place every tick (no rebuilt rows).
public sealed class PhaseStepViewModel : ObservableObject
{
    public string Text { get; private set; } = "";
    public StepState State { get; private set; }
    public double Fill { get; private set; }

    internal void Set(PhaseStep s)
    {
        if (Text == s.Text && State == s.State && Fill == s.Fill) return;
        (Text, State, Fill) = (s.Text, s.State, s.Fill);
        Raise(string.Empty);
    }
}

/// One row of the speed table, updated in place every tick; the tooltip is the old chain line (product, port, % linky).
public sealed class TransferRowViewModel : ObservableObject
{
    public string Name { get; private set; } = "";
    public int? Number { get; private set; }
    public bool HasNumber => Number != null;
    public double Share { get; private set; }
    public string RateText { get; private set; } = "";
    public string? Tooltip { get; private set; }

    internal void Set(TransferRow r, string? tooltip)
    {
        if (Name == r.Name && Number == r.Number && Share == r.Share && RateText == r.RateText && Tooltip == tooltip) return;
        (Name, Number, Share, RateText, Tooltip) = (r.Name, r.Number, r.Share, r.RateText, tooltip);
        Raise(string.Empty);
    }
}

/// A thumbnail of the finished card: the clip's name and its middle frame (decoded off the UI thread); the last tile
/// may be "+18" (More) instead.
public sealed class ThumbViewModel(string clip, string? more = null) : ObservableObject
{
    public string Clip => clip;
    public string? More => more;
    ImageSource? image;
    public ImageSource? Image
    {
        get => image;
        internal set => Set(ref image, value);
    }
}

/// The new parts of the job card (UI návrh, nové věci 1–4 and 9): phase indicator, speed table, file line, pause,
/// remaining time, the brake sentence, the summary fields, thumbnails, the failed files and "Zkopírovat znovu".
public sealed partial class JobItemViewModel
{
    IReadOnlyList<ChainElement>? chain;
    IReadOnlyList<Clip>? planClips;
    bool thumbsLoaded;

    /// The clips of what this job copies (once the plan exists), found once.
    internal IReadOnlyList<Clip> PlanClips
    {
        get
        {
            if (planClips == null && Job.Plan is { } p) planClips = ClipDetector.Detect(p.Scan.Files);
            return planClips ?? [];
        }
    }

    public ObservableCollection<PhaseStepViewModel> Steps { get; } = [];
    public ObservableCollection<TransferRowViewModel> Rows { get; } = [];
    public ObservableCollection<ThumbViewModel> Thumbs { get; } = [];
    public RelayCommand PauseCommand { get; private set; } = null!;
    public RelayCommand ResumeCommand { get; private set; } = null!;
    public RelayCommand RepairCommand { get; private set; } = null!;
    public RelayCommand OpenProxyCommand { get; private set; } = null!;

    void InitCard()
    {
        PauseCommand = new RelayCommand(() => { if (owner.AllowedFor(DemoCommand.Pause, Job.IsPreview)) { Job.Pause(); Refresh(); } },
            () => CanPause && owner.AllowedFor(DemoCommand.Pause, Job.IsPreview));
        ResumeCommand = new RelayCommand(() => { if (owner.AllowedFor(DemoCommand.Resume, Job.IsPreview)) { Job.Resume(); Refresh(); } },
            () => IsPaused && owner.AllowedFor(DemoCommand.Resume, Job.IsPreview));
        RepairCommand = new RelayCommand(() => owner.Repair(this), () => CanRepair && owner.Allowed(DemoCommand.Repair));
        OpenProxyCommand = new RelayCommand(OpenProxies);
    }

    /// Every tick: the indicator and the table are updated in place; the thumbnails once the job has finished.
    void RefreshCard()
    {
        var steps = JobCard.Steps(progress.Status, Job.Config, Progress);
        Sync(Steps, steps.Count, () => new PhaseStepViewModel(), (vm, i) => vm.Set(steps[i]));
        chain = links != null && Phase is JobPhase.Copying or JobPhase.Verifying
            ? ChainView.Build(links, [Phase == JobPhase.Copying ? progress.ReadRate / 1e6 : 0, .. progress.DestRates.Select(r => r / 1e6)], connectors, portNames)
            : null;
        var rows = JobCard.Rows(progress, DestNames);
        bool verifying = Phase == JobPhase.Verifying;
        Sync(Rows, rows.Count, () => new TransferRowViewModel(), (vm, i) =>
        {
            // The old chain line as the row's tooltip: the card reader is chain[0], destination d is chain[d + 1].
            int element = verifying ? i + 1 : i;
            var tip = chain is { } c && element < c.Count ? $"{c[element].Role} {c[element].Product} — {c[element].Connection}" : null;
            vm.Set(rows[i], tip);
        });
        if (!thumbsLoaded && IsFinished && Job.Report is { } report)
        {
            thumbsLoaded = true;
            LoadThumbs(report);
        }
    }

    static void Sync<T>(ObservableCollection<T> list, int count, Func<T> create, Action<T, int> update)
    {
        while (list.Count > count) list.RemoveAt(list.Count - 1);
        while (list.Count < count) list.Add(create());
        for (int i = 0; i < count; i++) update(list[i], i);
    }

    // ── Running ────────────────────────────────────────────────────────────────────────────────────────────────
    /// "zbývá ~0:02 kopie · ~6:00 celkem" (the whole job only when the session's speeds are known).
    public string? RemainingText => JobCard.Remaining(Phase, progress.Eta, owner.RemainingTotal(this, progress.Eta));

    public string FileCaption => JobCard.FileLine(progress.Status).Caption;
    public string FileValue => JobCard.FileLine(progress.Status).Value;

    /// "Pozastavuje se…" / "Pozastaví se po proxy klipu A003…" / "Pozastaveno — …", or null.
    public string? PauseText => progress.PauseText;
    public bool CanPause => IsRunning && !IsPaused && !Job.IsCancelled;

    /// "Nejpomalejší článek: čtečka karty (…). Disky by zvládly víc." while copying.
    public string? BrakeText => Phase == JobPhase.Copying && chain is { } c ? JobCard.BrakeSentence(progress.Brake, c, DestNames) : null;

    // ── Done ───────────────────────────────────────────────────────────────────────────────────────────────────
    /// Kopie · Ověření · Proxy · Celkem.
    public IReadOnlyList<SummaryField> SummaryFields => IsFinished && Job.Report is { } r ? JobCard.Summary(r, progress.Elapsed) : [];
    public bool HasThumbs => Thumbs.Count > 0;
    public bool HasProxies => Job.Report?.Proxy?.Enabled == true && progress.Status.ProxiesDone > 0;

    void LoadThumbs(OffloadReport report)
    {
        var (thumbs, more) = JobCard.Thumbs(report);
        foreach (var (clip, jpeg) in thumbs)
        {
            var vm = new ThumbViewModel(clip);
            Thumbs.Add(vm);
            // Decoding is CPU work on bytes in memory (the report kept them): off the UI thread, frozen for it.
            Task.Run(() =>
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 240;
                image.StreamSource = new MemoryStream(jpeg);
                image.EndInit();
                image.Freeze();
                return image;
            }).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) vm.Image = t.Result;
                else AppLog.Write($"{CardName}: thumbnail {clip}: {t.Exception?.GetBaseException().Message}");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (more > 0) Thumbs.Add(new ThumbViewModel("", $"+{more}"));   // the sixth tile
        Raise(nameof(HasThumbs));
    }

    void OpenProxies()
    {
        if (Job.Plan is { } plan && Shell.OpenFolder(Path.Combine(plan.CardPaths[0], Layout.ProxyDirName)) is string error) owner.ShowMessage(error);
    }

    // ── Not on every disk ──────────────────────────────────────────────────────────────────────────────────────
    const int ShownFailed = 5;

    /// The files that are not on every destination (first five), with "RAID_A ✓" / "SHUTTLE_B ✕ hash" per destination.
    public IReadOnlyList<FailedFileRow> FailedRows => Job.Report is { Success: false } r ? JobCard.FailedRows(r, DestNames).Take(ShownFailed).ToList() : [];
    public string? MoreFailed => Job.Report is { Success: false } r ? JobCard.MoreFiles(r.FailedFiles.Count, ShownFailed) : null;
    public int DiskCount => DestNames.Count;

    /// "Zkopírovat znovu": only the failed files, only where they failed (a repair job, RepairRules).
    public bool CanRepair => RepairRules.Offer(Phase, Job.Report, owner.SourcePresent(this), owner.RepairPending(this), owner.RepairedLater(this));
}
