namespace Offload.Core;

public sealed class JobConfig(string source, IReadOnlyList<string> destRoots)
{
    public string Source { get; set; } = source;
    public IReadOnlyList<string> DestRoots { get; set; } = destRoots;
    public string? CardName { get; set; }
    public string? Date { get; set; }
    public bool Verify { get; set; } = true;
    public int ChunkMB { get; set; } = 16;
    public int Buffers { get; set; } = 16;
    public bool AllowSameDevice { get; set; }
    /// Probe clips and put thumbnails + metadata into the report (stage 2, plan 2).
    public bool AnalyzeClips { get; set; } = true;
    public ProxySettings Proxy { get; set; } = new();
}

public enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Done, Failed, Cancelled }

public static class JobPhaseExtensions
{
    public static string Label(this JobPhase p) => p switch
    {
        JobPhase.Waiting => "Čeká",
        JobPhase.Copying => "Kopírování",
        JobPhase.Flushing => "Zápis cache disků",
        JobPhase.Verifying => "Ověřování",
        JobPhase.Reporting => "Report",
        JobPhase.Analyzing => "Analýza klipů",
        JobPhase.Proxies => "Render proxy",
        JobPhase.Done => "Hotovo",
        JobPhase.Failed => "Chyba",
        _ => "Zrušeno",
    };

    public static bool IsFinished(this JobPhase p) => p is JobPhase.Done or JobPhase.Failed or JobPhase.Cancelled;
}

public sealed record JobStatus
{
    public JobPhase Phase { get; init; } = JobPhase.Waiting;
    public ProgressSnapshot? Copy { get; init; }
    public int ClipIndex { get; init; }
    public int ClipCount { get; init; }
    public string ClipName { get; init; } = "";
    public double ClipFraction { get; init; }
    public int ProxiesDone { get; init; }
    public int ProxiesFailed { get; init; }
    /// All files are on all destinations and verified — the card may be ejected even while proxies render.
    public bool SafeToEject { get; init; }
    public bool? CopySucceeded { get; init; }
    public string? Error { get; init; }
}

/// One card: copy → verify → report. Used by both the CLI and the app. (Plan 2 adds stage 2: clips and proxies.)
public sealed class OffloadJob
{
    readonly Lock gate = new();
    JobStatus state = new();

    public JobConfig Config { get; }
    public OffloadEngine Engine { get; }
    public OffloadPlan? Plan { get; private set; }
    public OffloadReport? Report { get; private set; }

    public OffloadJob(JobConfig config)
        : this(config, new OffloadOptions
        {
            ChunkSize = config.ChunkMB << 20,
            BufferCount = config.Buffers,
            Verify = config.Verify,
            AllowSameDevice = config.AllowSameDevice,
        })
    {
    }

    internal OffloadJob(JobConfig config, OffloadOptions options)
    {
        Config = config;
        Engine = new OffloadEngine(options);
    }

    public OffloadPlan Prepare() => Plan ??= Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date);

    public void Cancel() => Engine.Cancel();

    public bool IsCancelled => Engine.IsCancelled;

    public JobStatus Status()
    {
        JobStatus s;
        lock (gate) s = state;
        if (s.Phase is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting)
        {
            var snap = Engine.Snapshot();
            s = s with
            {
                Copy = snap,
                Phase = snap.Phase switch
                {
                    CopyPhase.Copy => JobPhase.Copying,
                    CopyPhase.Flush => JobPhase.Flushing,
                    CopyPhase.Verify => JobPhase.Verifying,
                    _ => JobPhase.Reporting,
                },
            };
        }
        return s;
    }

    void Update(Func<JobStatus, JobStatus> change)
    {
        lock (gate) state = change(state);
    }

    /// Runs the whole job synchronously. Never throws: the outcome is in Status() and the returned report.
    public OffloadReport? Run()
    {
        RunCopy();
        return Report;
    }

    /// Stage 1 — card → disks + verification. Returns true when stage 2 should follow (always false until plan 2).
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    public bool RunCopy()
    {
        using var awake = NativeIO.KeepAwake();
        try
        {
            var plan = Prepare();
            Update(s => s with { Phase = JobPhase.Copying });
            var report = Engine.Run(plan);
            Report = report;
            Update(s => s with
            {
                SafeToEject = report.SafeToEject,
                CopySucceeded = report.Success,
                Copy = Engine.Snapshot(),
                Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done,
            });
            return false;
        }
        catch (OperationCanceledException)
        {
            Update(s => s with { Phase = JobPhase.Cancelled });
            return false;
        }
        catch (Exception e)
        {
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
            return false;
        }
    }
}
