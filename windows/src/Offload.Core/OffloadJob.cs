using System.Diagnostics;
using Offload.Core.Desktop;
using Offload.Core.History;

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
    /// Write the finished copy into history.json (R27; CLI --no-history turns it off).
    public bool RecordHistory { get; set; } = true;
    /// Where history.json lives; null = HistoryStore.Default (%APPDATA%\Offload).
    public string? HistoryFolder { get; set; }
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
    bool started;

    public JobConfig Config { get; }
    public OffloadEngine Engine { get; }
    volatile OffloadPlan? planValue;
    volatile OffloadReport? reportValue;
    volatile string? sourceIdentity;
    CardVolume? cardVolume;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
    public OffloadReport? Report { get => reportValue; private set => reportValue = value; }
    /// The card in the source's drive when the copy started (`CardIdentity`), read on the copy thread; null before
    /// that or when unknown. "Vysunout kartu" ejects only this card.
    public string? SourceIdentity => sourceIdentity;

    /// Tests: what CardIdentity.Of / CardVolume.Of would read from the drive.
    internal Func<string, string?> Identify { get; init; } = CardIdentity.Of;
    internal Func<string, CardVolume?> VolumeOf { get; init; } = CardVolume.Of;

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
        bool wasStarted;
        lock (gate) { s = state; wasStarted = started; }
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
        // Cancelled while still queued: it never runs, so say so instead of "Čeká" forever.
        if (s.Phase == JobPhase.Waiting && !wasStarted && IsCancelled) s = s with { Phase = JobPhase.Cancelled };
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
        IdentifyCard(log);
        lock (gate)
        {
            started = true;
            // Cancelled before it began: stay Cancelled, never prepare or copy.
            if (IsCancelled)
            {
                state = state with { Phase = JobPhase.Cancelled };
                return false;
            }
        }
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
                return false;   // a cancelled copy is not recorded in history
            }
            RecordHistory(plan, report, log);
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

    /// Which card is in the drive — read on the copy thread before anything else (the UI never waits on a card).
    void IdentifyCard(Action<string>? log)
    {
        // Each read has its own try: one failing must not skip the other.
        try { sourceIdentity = Identify(Config.Source); }
        catch (Exception e) { log?.Invoke($"card identity: {e.Message}"); }
        try { cardVolume = VolumeOf(Config.Source); }
        catch (Exception e) { log?.Invoke($"card volume: {e.Message}"); }
    }

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job.
    void RecordHistory(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        try
        {
            var (fingerprint, shape) = CardFingerprint.Compute(plan.Scan);
            var cardPaths = plan.CardPaths;
            var entry = new HistoryEntry
            {
                Id = HistoryStore.NewId(report.Finished),
                Card = cardVolume?.ToHistoryCard(),
                Source = plan.Source,
                CardName = plan.CardName,
                Date = plan.Date,
                Finished = report.Finished,
                Success = report.Success,
                Verified = report.Verified,
                FailedFiles = report.Success ? null : report.FailedFiles.Count,
                HashAlgorithms = [report.HashAlgorithm],
                Files = plan.Scan.Files.Count,
                Bytes = plan.Scan.TotalBytes,
                Fingerprint = fingerprint,
                Shape = shape,
                Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
                Generations =
                [
                    new HistoryGeneration
                    {
                        Seq = 1, Kind = HistoryGeneration.Full, Finished = report.Finished,
                        Files = plan.Scan.Files.Count, Bytes = plan.Scan.TotalBytes, Verified = report.Verified,
                    },
                ],
                Reels = ReelSuggestion.FromClips(ClipDetector.Detect(plan.Scan.Files)).Reels.ToList(),
            };
            var store = Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);
            store.Record(entry, plan.Scan.Files);
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
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
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved()) { ForcedEncoder = Engine.Options.ProxyEncoder, Log = log };
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
                entries[i].ProbeError = Msg.NoVerifiedCopy;
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
                // The Sony XML is read from the destination: it sits next to the clip on the card, so it was copied too.
                entries[i].Lut = LutResolver.Resolve(entries[i].Clip, info, Layout.Join(cardPaths[d], entries[i].Clip.Path), Config.Proxy, LutCatalog.Default);
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if (entries[i].Clip.Format is not (ClipFormat.R3d or ClipFormat.Arriraw))
                    entries[i].Thumbnails = TryThumbnails(renderer, entries[i].Clip, cardPaths[d], info, entries[i].Lut, () => IsCancelled, log);
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
                entries[i].ProxyNote = entries[i].ProbeError ?? Msg.NoVerifiedCopy;
                Update(s => s with { ProxiesFailed = s.ProxiesFailed + 1 });
                continue;
            }
            var name = clip.Name;
            for (int n = 2; !usedNames.Add(name); n++) name = $"{clip.Name}_{n}";
            var rel = Layout.ProxyDirName + "/" + name + ".mov";
            var sw = Stopwatch.StartNew();
            try
            {
                renderer.Render(clip, cardPaths[d], info, entries[i].Lut, Layout.Join(cardPaths[d], rel),
                    f => Update(s => s with { ClipFraction = f }), () => IsCancelled);
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                if (Config.Proxy.CopyToAllDestinations)
                {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (int o = 0; o < cardPaths.Count; o++)
                    {
                        if (o == d) continue;
                        try { ProxyCopy.CopyFile(Layout.Join(cardPaths[d], rel), Layout.Join(cardPaths[o], rel)); }
                        catch (Exception e)
                        {
                            var note = $"kopie na {cardPaths[o]} selhala: {e.Message}";
                            entries[i].ProxyNote = entries[i].ProxyNote is string prev ? $"{prev}; {note}" : note;
                            log?.Invoke($"proxy {clip.Name}: copy to {cardPaths[o]} failed: {e.Message}");
                        }
                    }
                }
                if (Config.Proxy.ExtraFolder is string extra)
                {
                    // The user's proxy folder: also never fails the proxy (the disk may have been disconnected).
                    var (_, error) = ProxyCopy.ToExtraFolder(Layout.Join(cardPaths[d], rel), extra, plan.Date, plan.CardName, name + ".mov");
                    if (error != null)
                    {
                        var note = $"kopie do {extra} selhala: {error}";
                        entries[i].ProxyNote = entries[i].ProxyNote is string prev ? $"{prev}; {note}" : note;
                        log?.Invoke($"proxy {clip.Name}: copy to {extra} failed: {error}");
                    }
                }
                if (entries[i].Thumbnails.Count == 0)
                {
                    // RAW formats rendered via an intermediate: take the thumbnails from the finished proxy (it already has the LUT).
                    var proxyClip = new Clip { Name = name, Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 0 };
                    entries[i].Thumbnails = TryThumbnails(renderer, proxyClip, cardPaths[d], info, lut: null, () => IsCancelled, log);
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

    static List<Thumbnail> TryThumbnails(ProxyRenderer renderer, Clip clip, string cardPath, ClipInfo info, LutUse? lut,
        Func<bool> isCancelled, Action<string>? log)
    {
        try
        {
            return renderer.Thumbnails(clip, cardPath, info, lut, isCancelled);
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
                try { ProxyCopy.CopyFile(first, Path.Combine(other, Layout.ReportDirName, name)); }
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
}
