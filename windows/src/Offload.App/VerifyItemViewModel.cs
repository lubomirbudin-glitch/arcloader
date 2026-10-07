using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.App;

/// A verification in the queue (spec 2.4): progress, the result in Czech, the report and Explorer buttons.
public sealed class VerifyItemViewModel : QueueItem
{
    readonly MainViewModel owner;
    VerifyJobStatus status;
    DateTime lastTime = DateTime.UtcNow;
    long lastBytes;
    double rate;
    bool hadProgress;

    public VerifyItemViewModel(MainViewModel owner, VerifyJob job)
    {
        this.owner = owner;
        Job = job;
        status = job.Status();
        CancelCommand = new RelayCommand(() => { Job.Cancel(); Refresh(); });
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
    }

    public VerifyJob Job { get; }
    public override IQueuedJob Queued => Job;
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenReportCommand { get; }
    public RelayCommand RevealCommand { get; }

    public override JobPhase Phase => status.State switch
    {
        VerifyJobState.Waiting => JobPhase.Waiting,
        VerifyJobState.Running => JobPhase.Verifying,
        VerifyJobState.Done => JobPhase.Done,
        VerifyJobState.Cancelled => JobPhase.Cancelled,
        _ => JobPhase.Failed,
    };

    public override bool IsFinished => Phase.IsFinished();
    public override bool IsError => VerifyJobView.Tone(status) == CardTone.Error;
    public override void Cancel() => Job.Cancel();

    public override void Refresh()
    {
        status = Job.Status();
        var now = DateTime.UtcNow;
        double dt = (now - lastTime).TotalSeconds;
        if (status.Progress is { } first && !hadProgress)
        {
            // The first sample: the verification may have waited in the queue, so start the speed measurement here.
            hadProgress = true;
            lastTime = now;
            lastBytes = first.Bytes;
        }
        else if (status.Progress is { } p && dt > 0.2)
        {
            var current = Math.Max(0, (p.Bytes - lastBytes) / dt);
            rate = rate == 0 ? current : rate * 0.6 + current * 0.4;
            lastBytes = p.Bytes;
            lastTime = now;
        }
        Raise(string.Empty);
    }

    public string Title => VerifyJobView.Title(Job);
    public string FolderText => Job.CardPath;
    public string StatusText => VerifyJobView.StatusText(status);
    public CardTone Tone => VerifyJobView.Tone(status);
    public bool IsRunning => status.State == VerifyJobState.Running;
    public double Progress => VerifyJobView.Fraction(status.Progress);
    public string DetailText => VerifyJobView.Detail(status.Progress, rate);
    public string? ResultText => status.Result is { } r ? VerifyTexts.Summary(r).Text : null;
    public VerifyTone ResultTone => status.Result is { } r ? VerifyTexts.Summary(r).Tone : VerifyTone.Ok;
    public IReadOnlyList<string> Warnings => status.Result is { Cancelled: false } r ? [.. VerifyTexts.Warnings(r), .. (status.ReportError is { } e ? new[] { e } : [])] : [];
    public string? ErrorText => status.Error;
    public string? ReportNote => VerifyJobView.ReportNote(status);
    public bool HasReport => status.ReportPath != null;
    public bool CanCancel => !IsFinished;

    void OpenReport()
    {
        if (status.ReportPath is string path && Shell.Open(path) is string error) owner.ShowMessage(error);
    }

    void Reveal()
    {
        if (Shell.Reveal(Job.CardPath) is string error) owner.ShowMessage(error);
    }
}
