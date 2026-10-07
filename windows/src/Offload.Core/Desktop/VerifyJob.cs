using System.Globalization;
using Offload.Core.Verification;

namespace Offload.Core.Desktop;

public enum VerifyJobState { Waiting, Running, Done, Cancelled, Failed }

public sealed record VerifyJobStatus(VerifyJobState State, VerifyProgress? Progress, VerifyResult? Result, string? Error, string? ReportPath, string? ReportError = null);

/// "Ověřit zálohu…" as a queue item (spec 2.4): it runs in the copy queue, one after another with the copies — both
/// load the same disks. `RunCopy` verifies (and writes the report), returns false: there is no stage 2.
public sealed class VerifyJob(string cardPath, bool writeReport, Action<string>? log = null) : IQueuedJob
{
    readonly CancellationTokenSource cts = new();
    readonly Lock gate = new();
    VerifyJobStatus status = new(VerifyJobState.Waiting, null, null, null, null);

    public string CardPath { get; } = cardPath;
    public string CardName => Path.GetFileName(CardPath.TrimEnd('\\', '/'));
    public bool IsCancelled => cts.IsCancellationRequested;

    public void Cancel() => cts.Cancel();

    /// Read by the UI thread every tick.
    public VerifyJobStatus Status()
    {
        lock (gate)
        {
            // Cancelled while still queued: it never runs (JobQueue skips it), so say so instead of "Čeká" forever.
            return status.State == VerifyJobState.Waiting && IsCancelled ? status with { State = VerifyJobState.Cancelled } : status;
        }
    }

    void Set(Func<VerifyJobStatus, VerifyJobStatus> change)
    {
        lock (gate) status = change(status);
    }

    public bool RunCopy()
    {
        if (IsCancelled)
        {
            Set(s => s with { State = VerifyJobState.Cancelled });
            return false;
        }
        Set(s => s with { State = VerifyJobState.Running });
        using var awake = NativeIO.KeepAwake();
        try
        {
            var r = BackupVerifier.Verify(CardPath, p => Set(s => s with { Progress = p }), cts.Token);
            string? report = null, reportError = null;
            if (writeReport && !r.Cancelled)
            {
                // Writing the report and the PDF runs inside this queue slot and ignores Cancel (it is short, and a half-written report is worse).
                try { report = VerifyReport.Write(r, log); }
                catch (Exception e)
                {
                    log?.Invoke($"verify report: {e.Message}");
                    reportError = Msg.ReportNotWritten(e.Message);
                }
            }
            Set(s => s with { State = r.Cancelled ? VerifyJobState.Cancelled : VerifyJobState.Done, Result = r, ReportPath = report, ReportError = reportError });
        }
        catch (Exception e)
        {
            log?.Invoke($"verify: {e.Message}");
            Set(s => s with { State = VerifyJobState.Failed, Error = e.Message });
        }
        return false;
    }

    public void RunPost()
    {
    }
}

/// What a verification card in the queue shows (spec 2.4 UX).
public static class VerifyJobView
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    public static string Title(VerifyJob job) => $"Ověření {job.CardName}";

    public static string StatusText(VerifyJobStatus s) => s.State switch
    {
        VerifyJobState.Waiting => "Čeká",
        VerifyJobState.Running => "Ověřování",
        VerifyJobState.Done => s.Result is { Success: true } ? "Ověřeno" : "Problémy",
        VerifyJobState.Cancelled => "Zrušeno",
        _ => "Chyba",
    };

    public static CardTone Tone(VerifyJobStatus s) => s.State switch
    {
        VerifyJobState.Waiting => CardTone.Waiting,
        VerifyJobState.Running => CardTone.Active,
        VerifyJobState.Done => s.Result is { Success: true } ? CardTone.Ok : CardTone.Error,
        VerifyJobState.Cancelled => CardTone.Warning,
        _ => CardTone.Error,
    };

    /// "Ověřuji 120 / 1 234 souborů · 45 GB / 512 GB · 380 MB/s"
    public static string Detail(VerifyProgress? p, double bytesPerSecond)
    {
        if (p == null) return "Ověřuji…";
        var rate = (bytesPerSecond / 1_000_000).ToString("0", CultureInfo.InvariantCulture);
        return $"Ověřuji {(p.FileIndex + 1).ToString("N0", Cs)} / {p.FileCount.ToString("N0", Cs)} souborů · {Format.BytesCs(p.Bytes)} / {Format.BytesCs(p.TotalBytes)} · {rate} MB/s";
    }

    /// The Czech note when the report went outside the card folder (ruling V2); null otherwise.
    public static string? ReportNote(VerifyJobStatus s) =>
        s is { ReportPath: { } path, Result.ReportInsideCard: false } ? VerifyTexts.ReportOutsideNote(path) : null;

    public static double Fraction(VerifyProgress? p) => p is { TotalBytes: > 0 } ? Math.Clamp((double)p.Bytes / p.TotalBytes, 0, 1) : 0;
}
