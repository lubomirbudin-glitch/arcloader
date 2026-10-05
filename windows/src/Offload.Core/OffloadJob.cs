using System.Diagnostics;

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
    /// Probe clips and put thumbnails + metadata into the report (stage 2).
    public bool AnalyzeClips { get; set; } = true;
    public ProxySettings Proxy { get; set; } = new();
    public ExternalTools Tools { get; set; } = new();
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

/// One card: copy → verify → report → clip analysis → optional proxies → PDF. Used by both the CLI and the app.
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
    public OffloadReport? Run(Action<string>? log = null)
    {
        if (RunCopy(log)) RunPost(log);
        return Report;
    }

    /// Stage 1 — card → disks + verification. Returns true when stage 2 should follow.
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    public bool RunCopy(Action<string>? log = null)
    {
        using var awake = NativeIO.KeepAwake();
        try
        {
            var plan = Prepare();
            Update(s => s with { Phase = JobPhase.Copying });
            var report = Engine.Run(plan);
            Report = report;
            Update(s => s with { SafeToEject = report.SafeToEject, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                Update(s => s with { Phase = JobPhase.Cancelled });
                return false;
            }
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
                WritePdfReports(report, plan, log);
                Update(s => s with { Phase = JobPhase.Done });
                return false;
            }
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
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

    /// Stage 2 — clip analysis, thumbnails, optional proxies, PDF. Reads only from the destinations.
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it).
    public void RunPost(Action<string>? log = null)
    {
        if (Plan is not { } plan || Report is not { } report) return;
        using var awake = NativeIO.KeepAwake();
        try
        {
            var final = AnalyzeAndRender(plan, report, log);
            Report = final;
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        catch (Exception e)
        {
            log?.Invoke($"stage 2 failed: {e.Message}");
            WritePdfReports(report, plan, log);
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
        }
    }

    OffloadReport AnalyzeAndRender(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        var clips = ClipDetector.Detect(plan.Scan.Files);
        if (clips.Count == 0) return report;
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved());
        var cardPaths = plan.CardPaths;
        var fileIndex = report.Files.Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i);
        bool Good(string state) => state == "verified" || (!report.Verified && state == "written");
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card.
        int? GoodDest(Clip clip)
        {
            for (int d = 0; d < cardPaths.Count; d++)
                if (clip.Segments.All(seg => fileIndex.TryGetValue(seg, out var fi) && Good(report.Files[fi].Destinations[d]))) return d;
            return null;
        }

        var entries = clips.Select(c => new ClipEntry { Clip = c }).ToList();
        Update(s => s with { Phase = JobPhase.Analyzing, ClipCount = entries.Count });
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled) break;
            var clip = entries[i].Clip;
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
            if (GoodDest(clip) is not int d)
            {
                entries[i].ProbeError = "no verified copy";
                continue;
            }
            if (clip.Format != ClipFormat.Video && renderer.UnsupportedReason(clip) is string reason)
            {
                entries[i].ProbeError = reason;
                continue;
            }
            try
            {
                var info = renderer.Probe(clip, cardPaths[d]);
                if (clip.Format == ClipFormat.Video && info.Width == null)
                {
                    if (renderer.ReclassifyUndecodableMxf(clip, cardPaths[d]) is { } arri)
                    {
                        entries[i].Clip = arri.Clip;
                        info = arri.Info;
                    }
                    else
                    {
                        entries[i].Info = info;
                        entries[i].ProbeError = "kodek nepodporován ffmpegem (např. Sony X-OCN) — zkopírováno a ověřeno, bez proxy";
                        continue;
                    }
                }
                entries[i].Info = info;
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if (entries[i].Clip.Format is not (ClipFormat.R3d or ClipFormat.Arriraw))
                    entries[i].Thumbnails = TryThumbnails(renderer, entries[i].Clip, cardPaths[d], info, applyLut: true, () => IsCancelled, log);
            }
            catch (Exception e)
            {
                entries[i].ProbeError = e.Message;
                log?.Invoke($"probe {clip.Name}: {e.Message}");
            }
        }
        report.Clips = entries;
        report.Proxy = Config.Proxy;
        RewriteReports(report, plan, log);

        if (!Config.Proxy.Enabled || IsCancelled) return report;
        Update(s => s with { Phase = JobPhase.Proxies, ClipCount = entries.Count });
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled) break;
            var clip = entries[i].Clip;  // may have been reclassified during analysis (MXF → ARRIRAW)
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
            if (clip.IsCameraProxy && Config.Proxy.SkipCameraProxies)
            {
                entries[i].ProxyStatus = ProxyStatus.Skipped;
                continue;
            }
            var unsupported = renderer.UnsupportedReason(clip)
                ?? (entries[i].Info is { Width: null } ? entries[i].ProbeError : null);
            if (unsupported != null)
            {
                entries[i].ProxyStatus = ProxyStatus.Unsupported;
                entries[i].ProxyNote = unsupported;
                continue;
            }
            if (GoodDest(clip) is not int d || entries[i].Info is not { } info)
            {
                entries[i].ProxyStatus = ProxyStatus.Failed;
                entries[i].ProxyNote = entries[i].ProbeError ?? "no verified copy";
                Update(s => s with { ProxiesFailed = s.ProxiesFailed + 1 });
                continue;
            }
            var name = clip.Name;
            for (int n = 2; !usedNames.Add(name); n++) name = $"{clip.Name}_{n}";
            var rel = Layout.ProxyDirName + "/" + name + ".mov";
            var sw = Stopwatch.StartNew();
            try
            {
                renderer.Render(clip, cardPaths[d], info, Layout.Join(cardPaths[d], rel),
                    f => Update(s => s with { ClipFraction = f }), () => IsCancelled);
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                if (Config.Proxy.CopyToAllDestinations)
                {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (int o = 0; o < cardPaths.Count; o++)
                    {
                        if (o == d) continue;
                        try { CopyFile(Layout.Join(cardPaths[d], rel), Layout.Join(cardPaths[o], rel)); }
                        catch (Exception e)
                        {
                            var note = $"kopie na {cardPaths[o]} selhala: {e.Message}";
                            entries[i].ProxyNote = entries[i].ProxyNote is string prev ? $"{prev}; {note}" : note;
                            log?.Invoke($"proxy {clip.Name}: copy to {cardPaths[o]} failed: {e.Message}");
                        }
                    }
                }
                if (entries[i].Thumbnails.Count == 0)
                {
                    // RAW formats rendered via an intermediate: take the thumbnails from the finished proxy.
                    var proxyClip = new Clip { Name = name, Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 0 };
                    entries[i].Thumbnails = TryThumbnails(renderer, proxyClip, cardPaths[d], info, applyLut: false, () => IsCancelled, log);
                }
                entries[i].ProxySeconds = sw.Elapsed.TotalSeconds;
                Update(s => s with { ProxiesDone = s.ProxiesDone + 1 });
            }
            catch (Exception e)
            {
                entries[i].ProxyStatus = ProxyStatus.Failed;
                entries[i].ProxyNote = e.Message;
                Update(s => s with { ProxiesFailed = s.ProxiesFailed + 1 });
                log?.Invoke($"proxy {clip.Name}: {e.Message}");
            }
        }
        report.Clips = entries;
        if (entries.Any(e => e.ProxyStatus == ProxyStatus.Done)) report.ProxyEncoder = renderer.EncoderName;
        RewriteReports(report, plan, log);
        return report;
    }

    static List<Thumbnail> TryThumbnails(ProxyRenderer renderer, Clip clip, string cardPath, ClipInfo info, bool applyLut,
        Func<bool> isCancelled, Action<string>? log)
    {
        try
        {
            return renderer.Thumbnails(clip, cardPath, info, applyLut, isCancelled);
        }
        catch (Exception e)
        {
            if (!isCancelled()) log?.Invoke($"thumbnails {clip.Name}: {e.Message}");
            return [];
        }
    }

    /// PDF copy of the report on every destination. Rendered once, then copied; a failure here never fails the offload.
    static void WritePdfReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        var name = $"{plan.CardName}_report.pdf";
        var first = Path.Combine(plan.CardPaths[0], Layout.ReportDirName, name);
        try
        {
            PdfReport.Write(report, first);
            foreach (var other in plan.CardPaths.Skip(1))
            {
                try { CopyFile(first, Path.Combine(other, Layout.ReportDirName, name)); }
                catch (Exception e) { log?.Invoke($"PDF copy to {other}: {e.Message}"); }
            }
        }
        catch (Exception e)
        {
            // The HTML report is already on disk; the PDF is a convenience copy.
            log?.Invoke($"PDF report failed: {e.Message}");
        }
    }

    static void RewriteReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        foreach (var path in plan.CardPaths)
        {
            try { report.Write(path, includeHashRecords: false); }
            catch (Exception e) { log?.Invoke($"report rewrite {path}: {e.Message}"); /* the first report is already there; this only adds clip details */ }
        }
    }

    static void CopyFile(string src, string dst)
    {
        var dir = Path.GetDirectoryName(dst)!;
        NativeIO.Mkdirs(dir);
        var part = Path.Combine(dir, "." + Path.GetFileName(dst) + ".part");
        NativeIO.Delete(part);
        try
        {
            File.Copy(src, part);
            NativeIO.Move(part, dst, replace: true);
        }
        catch
        {
            NativeIO.Delete(part);
            throw;
        }
    }
}
