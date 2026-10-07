using System.Diagnostics;
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Space;

namespace Offload.Core;

public sealed class JobConfig(string source, IReadOnlyList<string> destRoots)
{
    public string Source { get; set; } = source;
    public IReadOnlyList<string> DestRoots { get; set; } = destRoots;
    public string? CardName { get; set; }
    public string? Date { get; set; }
    public bool Verify { get; set; } = true;
    public int ChunkMB { get; set; } = 16;
    /// xxh64, or xxh64 + MD5 (spec 3.5; settings `hash`, CLI --hash). An append keeps the earlier backup's.
    public HashKind Hashes { get; set; } = HashKind.Xxh64;
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
    /// Append (spec 2.2): only the card's new files go into these card folders (`<root>\<date>\<card>` of the earlier
    /// backup); DestRoots, CardName and Date are then taken from them.
    public IReadOnlyList<string>? AppendInto { get; set; }
    /// Clip lists CSV / ALE and the day report (spec 3.2, 3.3; settings `outputs`, CLI --no-clip-list, --ale,
    /// --no-day-report).
    public OutputSettings Outputs { get; set; } = new();
    /// The project the job was started in (spec 4.1), null = "Bez projektu": into history.json and the report.
    public string? Project { get; set; }
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
    /// Information for the job card, e.g. "Nic nového — záloha je úplná."
    public string? Note { get; init; }
    /// Warnings that do not fail the job (orange), one per line — e.g. unverified files of the earlier backup.
    public string? Warning { get; init; }

    /// This status with `line` added to the warnings.
    public JobStatus WithWarning(string line) => this with { Warning = Warning == null ? line : Warning + "\n" + line };
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
    volatile IReadOnlyList<string> dayReports = [];
    /// Ruling I2: stage 1 ended without stage 2 — RunPost only makes the day reports.
    volatile bool dayReportOnly, dayReportPending;
    CardVolume? cardVolume;
    /// Append: what the planner found (also when there is nothing new); read on the copy thread only.
    AppendPlan? appendPlan;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
    public OffloadReport? Report { get => reportValue; private set => reportValue = value; }
    /// The card in the source's drive when the copy started (`CardIdentity`), read on the copy thread; null before
    /// that or when unknown. "Vysunout kartu" ejects only this card.
    public string? SourceIdentity => sourceIdentity;
    /// The day reports (HTML) this job made again when it ended, one per destination that has it (the CLI prints them).
    public IReadOnlyList<string> DayReports => dayReports;

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
            Hashes = config.Hashes,
        })
    {
    }

    internal OffloadJob(JobConfig config, OffloadOptions options)
    {
        Config = config;
        Engine = new OffloadEngine(options);
    }

    /// A full copy, or with Config.AppendInto an append into those card folders (`NothingNew` when it has nothing to copy).
    public OffloadPlan Prepare()
    {
        if (Plan is { } done) return done;
        if (Config.AppendInto is { Count: > 0 } into)
        {
            var a = appendPlan = Engine.PlanAppend(Config.Source, into);
            var append = Engine.PrepareAppend(a, EstimateExtra);
            // M4: an append records the project of the earlier backup (its first generation), never a different one;
            // Config.Project only stands in when that report cannot be read.
            var earlier = CardRecord.Load(into[0]);
            append.Project = earlier.Generations.Count > 0 ? earlier.Project : Config.Project;
            return Plan = append;
        }
        var full = Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date, EstimateExtra);
        full.Project = Config.Project;
        return Plan = full;
    }

    /// Spec 2.5: proxies and report on top of the card, from the clip names (the clips are not read before the copy).
    SpaceExtra EstimateExtra(SourceScan scan) => SpaceEstimator.Extra(ClipDetector.Detect(scan.Files), Config.Proxy, Config.Tools.Resolved());

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

    /// Stage 1 — card → disks + verification. Returns true when stage 2 should follow — also when stage 1 ended (done
    /// without stage 2, cancelled, failed) and only the day report is left: RunPost then makes just that (ruling I2).
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
        AppendLock? folders = null;
        bool copying = false;   // the engine may have written to the destinations: a day report is due even on failure
        try
        {
            // An append holds its card folders until stage 1 ends (fix round 1, item 1) and plans under that hold — a
            // plan made before (the CLI prints one first) is made again, another append may have finished meanwhile.
            // Released in `finally`, so the NothingNew path below still holds them while it heals the hash list.
            folders = Config.AppendInto is { Count: > 0 } into ? AppendLock.Acquire(into) : null;
            if (folders != null)
            {
                Plan = null;
                appendPlan = null;
            }
            var plan = Prepare();
            if (plan.UnverifiedEarlier > 0) Update(s => s.WithWarning(Msg.UnverifiedEarlier(plan.UnverifiedEarlier)));
            Update(s => s with { Phase = JobPhase.Copying });
            copying = true;
            var report = Engine.Run(plan);
            Report = report;
            Update(s => s with { SafeToEject = report.SafeToEject, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                // Marked before the phase changes: whoever sees it finished also sees the day report pending (fix round 1, item 3).
                var cancelled = DayReportNext();   // the card shows as not complete (spec 3.2); not recorded in history
                Update(s => s with { Phase = JobPhase.Cancelled });
                return cancelled;
            }
            RecordHistory(plan, report, log);
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
                WriteClipLists(plan, log);   // the clips found in the files, without metadata (spec 3.3)
                WritePdfReports(report, plan, log);
                var done = DayReportNext();
                Update(s => s with { Phase = JobPhase.Done });
                return done;
            }
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
        {
            // Not an error: the earlier backup already has every file of the card — history still learns the card as it is.
            if (appendPlan is { } a)
            {
                RefreshHistory(a, log);
                // A hash list whose write failed in the last append gets its missed lines now (fix round 2, item 6b).
                foreach (var cardPath in a.CardPaths)
                {
                    try
                    {
                        if (OffloadReport.HealHashList(cardPath, a.CardName)) log?.Invoke($"hash list healed: {cardPath}");
                    }
                    catch (Exception x) when (x is IOException or UnauthorizedAccessException or OffloadError)
                    {
                        var message = Msg.HashListNotHealed(cardPath, x.Message);
                        Update(s => s.WithWarning(message));
                    }
                }
                // Nothing to copy does not make the earlier backup verified (fix round 1, item 3).
                if (a.UnverifiedEarlier > 0) Update(s => s.WithWarning(Msg.UnverifiedEarlier(a.UnverifiedEarlier)));
            }
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message });
            return false;
        }
        catch (OperationCanceledException)
        {
            var next = copying && DayReportNext();
            Update(s => s with { Phase = JobPhase.Cancelled });
            return next;
        }
        catch (Exception e)
        {
            var next = copying && DayReportNext();   // ruling M9: best effort after a failed copy too
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
            return next;
        }
        finally
        {
            folders?.Dispose();
        }
    }

    /// Ruling I2: stage 1 has ended and only the day report is left — RunPost makes it on the post thread, after the
    /// append lock above is released, so neither the next card's copy nor the card folders wait for it (another writer
    /// may hold the day for up to DayReportService.DefaultWait). False when the day report is switched off.
    bool DayReportNext()
    {
        if (!Config.Outputs.DayReport || Plan == null) return false;
        dayReportOnly = true;
        dayReportPending = true;
        return true;
    }

    /// The job has ended (Done, Cancelled, Failed) but its day report is still to be made on the post thread: the app
    /// keeps running until it is (closing waits for it, as for stage 2).
    public bool DayReportPending => dayReportPending;

    /// Which card is in the drive — read on the copy thread before anything else (the UI never waits on a card).
    void IdentifyCard(Action<string>? log)
    {
        // Each read has its own try: one failing must not skip the other.
        try { sourceIdentity = Identify(Config.Source); }
        catch (Exception e) { log?.Invoke($"card identity: {e.Message}"); }
        try { cardVolume = VolumeOf(Config.Source); }
        catch (Exception e) { log?.Invoke($"card volume: {e.Message}"); }
    }

    HistoryStore Store(Action<string>? log) => Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job. An append adds
    /// its generation to the entry of the card folder it went into (spec 5.2) — or starts one when history does not
    /// know the backup — under one hold of the history lock (ruling I2).
    void RecordHistory(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        try
        {
            var store = Store(log);
            var card = plan.FullScan ?? plan.Scan;
            var (fingerprint, shape) = CardFingerprint.Compute(card);
            var cardPaths = plan.CardPaths;
            // The id only finds the entry; everything about it is read and changed under the lock below.
            var id = (plan.Mode == PlanMode.Append ? EntryOf(store, cardPaths)?.Id : null) ?? HistoryStore.NewId(report.Finished);
            store.UpdateWithFiles(id, existing =>
            {
                var entry = existing ?? new HistoryEntry
                {
                    Id = id,
                    Source = plan.Source,
                    CardName = plan.CardName,
                    Date = plan.Date,
                    Project = plan.Project,   // a new entry only: an append keeps the project of the entry it adds to
                    Success = true,
                    Verified = true,
                    HashAlgorithms = [.. report.Algorithms],
                    Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
                };
                entry.Finished = report.Finished;
                entry.Success &= report.Success;
                entry.Verified &= report.Verified;
                entry.FailedFiles = entry.Success ? null : (entry.FailedFiles ?? 0) + report.FailedFiles.Count;
                entry.Generations.RemoveAll(g => g.Seq == plan.Generation);
                entry.Generations.Add(new HistoryGeneration
                {
                    Seq = plan.Generation,
                    Kind = plan.Mode == PlanMode.Append ? HistoryGeneration.Append : HistoryGeneration.Full,
                    Finished = report.Finished,
                    Files = plan.Scan.Files.Count,
                    Bytes = plan.Scan.TotalBytes,
                    Verified = report.Verified,
                });
                entry.Reels = ReelSuggestion.FromClips(ClipDetector.Detect(card.Files)).Reels.ToList();
                return WithCard(store, entry, existing, card, fingerprint, shape);
            });
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
        }
    }

    /// "Nic nového" (minors M17): the entry of that backup learns the card as it is now — no new generation.
    void RefreshHistory(AppendPlan a, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        try
        {
            var store = Store(log);
            if (EntryOf(store, a.CardPaths) is not { } found) return;
            var (fingerprint, shape) = CardFingerprint.Compute(a.FullScan);
            store.UpdateWithFiles(found.Id, existing => existing == null ? null : WithCard(store, existing, existing, a.FullScan, fingerprint, shape));
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
        }
    }

    /// The card as it is now (`fingerprint`, `shape`, the volume) into `entry`; its file list, `files` and `bytes` are
    /// the union with the list stored for `existing` (spec 5.2) — read while the caller holds the history lock.
    (HistoryEntry, IReadOnlyList<SourceFile>?) WithCard(HistoryStore store, HistoryEntry entry, HistoryEntry? existing, SourceScan card,
        string fingerprint, string shape)
    {
        var all = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        if (existing != null)
            foreach (var f in store.ReadFileList(existing) ?? []) all[f.RelPath] = f;
        foreach (var f in card.Files) all[f.RelPath] = f;
        var files = all.Values.OrderBy(f => f.RelPath, StringComparer.Ordinal).ToList();
        entry.Card = cardVolume?.ToHistoryCard() ?? entry.Card;
        entry.Fingerprint = fingerprint;
        entry.Shape = shape;
        entry.Files = files.Count;
        entry.Bytes = files.Sum(f => f.Size);
        return (entry, files);
    }

    /// The newest history entry that has one of these card folders as a destination.
    static HistoryEntry? EntryOf(HistoryStore store, IReadOnlyList<string> cardPaths) =>
        store.Load().FirstOrDefault(e => e.Destinations.Any(d => cardPaths.Any(p =>
            string.Equals(d.CardPath.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))));

    /// Stage 2 — clip analysis, thumbnails, optional proxies, PDF. Reads only from the destinations.
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it).
    public void RunPost(Action<string>? log = null)
    {
        if (dayReportOnly)
        {
            try
            {
                using var keepAwake = NativeIO.KeepAwake();
                if (Plan is { } p) RegenerateDayReports(p, log);
            }
            finally
            {
                dayReportPending = false;
            }
            return;
        }
        if (Plan is not { } plan || Report is not { } report) return;
        using var awake = NativeIO.KeepAwake();
        try
        {
            var final = AnalyzeAndRender(plan, report, log);
            Report = final;
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        catch (Exception e)
        {
            log?.Invoke($"stage 2 failed: {e.Message}");
            WriteClipLists(plan, log);   // the generation must not be missing from the lists
            WritePdfReports(report, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
        }
    }

    /// How long a cancelled job waits for another writer of its day before it leaves the day report out.
    internal static readonly TimeSpan CancelledDayReportWait = TimeSpan.FromSeconds(5);

    /// Spec 3.2: the day report of every destination of this job, made again at its end (also an append, also a
    /// cancelled copy). Waits for another writer of the same day; a failure is only logged — never the job's result.
    void RegenerateDayReports(OffloadPlan plan, Action<string>? log)
    {
        if (!Config.Outputs.DayReport) return;
        // Cancelled (closing the app cancels every job): a busy day is skipped after a short wait (fix round 1, item 4).
        TimeSpan? wait = IsCancelled ? CancelledDayReportWait : null;
        var written = new List<string>();
        foreach (var root in plan.DestRoots)
        {
            try { written.Add(DayReportService.Regenerate(root, plan.Date, Config.Outputs, log, wait)); }
            catch (Exception e) { log?.Invoke($"day report {Path.Combine(root, plan.Date)}: {e.Message}"); }
        }
        dayReports = written;
    }

    OffloadReport AnalyzeAndRender(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        var clips = ClipDetector.Detect(plan.Scan.Files);
        if (clips.Count == 0) return report;
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved())
        {
            ForcedEncoder = Engine.Options.ProxyEncoder,
            Log = log,
            ReplaceExisting = plan.Mode == PlanMode.Full,   // an append never overwrites a proxy (spec 2.2)
        };
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
                        info.Reel = ReelResolver.Resolve(clip, info, plan.CardName);
                        entries[i].Info = info;
                        entries[i].ProbeError = "kodek nepodporován ffmpegem (např. Sony X-OCN) — zkopírováno a ověřeno, bez proxy";
                        continue;
                    }
                }
                // Spec 3.4: the camera's tag, else the clip name, else the card folder — into the report and the proxy.
                info.Reel = ReelResolver.Resolve(entries[i].Clip, info, plan.CardName);
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
        if (ProxySpaceProblem(entries, plan, renderer) is string noRoom)
        {
            // Spec 2.5: the copy stays verified; the proxies are left out and the job card says why (orange).
            foreach (var e in entries.Where(e => e.ProxyStatus == ProxyStatus.None))
            {
                e.ProxyStatus = ProxyStatus.Skipped;
                e.ProxyNote = noRoom;
            }
            Update(s => s.WithWarning(noRoom));
            log?.Invoke(noRoom);
            RewriteReports(report, plan, log);
            return report;
        }
        Update(s => s with { Phase = JobPhase.Proxies, ClipCount = entries.Count });
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (plan.Mode == PlanMode.Append) usedNames.UnionWith(ExistingProxyNames(cardPaths));
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
                // Spec 3.4: relinking goes by name - a proxy that had to be renamed must be attached by hand.
                if (name != clip.Name) entries[i].ProxyNote = entries[i].ProxyNote is string earlier ? $"{earlier}; {Msg.ProxyRenamed(name)}" : Msg.ProxyRenamed(name);
                if (Config.Proxy.CopyToAllDestinations)
                {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (int o = 0; o < cardPaths.Count; o++)
                    {
                        if (o == d) continue;
                        try { ProxyCopy.CopyFile(Layout.Join(cardPaths[d], rel), Layout.Join(cardPaths[o], rel), replace: plan.Mode == PlanMode.Full); }
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

    /// Spec 2.5: before the proxies, their need again from the analysed clips (real duration, height, audio) against the
    /// free space now. The message when they do not fit, or null. Same place list and check as the engine's (one calculation).
    string? ProxySpaceProblem(List<ClipEntry> entries, OffloadPlan plan, ProxyRenderer renderer)
    {
        var infos = new Dictionary<Clip, ClipInfo>(ReferenceEqualityComparer.Instance);
        foreach (var e in entries)
            if (e.Info != null) infos[e.Clip] = e.Info;
        long proxy = SpaceEstimator.ProxyBytes(entries.Select(e => e.Clip), Config.Proxy,
            c => infos.ContainsKey(c) && renderer.UnsupportedReason(c) == null, c => infos.GetValueOrDefault(c));
        if (proxy == 0) return null;
        var extra = new SpaceExtra(proxy, 0, Config.Proxy.CopyToAllDestinations, Config.Proxy.ExtraFolder);
        var places = SpacePlanner.Places(plan.DestRoots, extra, 0, HistoryText.DiskName);
        var (problems, _) = SpacePlanner.CheckPlaces(places, Engine.FreeBytes);
        return problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking) is { } b
            ? Msg.ProxiesSkippedForSpace(b.Label, b.Need.Proxy, b.Free)
            : null;
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

    /// The proxies earlier generations rendered: a new clip of the same name becomes "_2" in an append (spec 2.2).
    static List<string> ExistingProxyNames(IReadOnlyList<string> cardPaths)
    {
        var names = new List<string>();
        foreach (var p in cardPaths)
        {
            var dir = Path.Combine(p, Layout.ProxyDirName);
            try
            {
                if (Directory.Exists(dir)) names.AddRange(Directory.EnumerateFiles(dir, "*.mov").Select(f => Path.GetFileNameWithoutExtension(f)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A destination that went away: nothing is rendered or copied there now either.
            }
        }
        return names;
    }

    /// PDF copy of the report on every destination. Rendered once, then copied; a failure here never fails the offload.
    /// An append's `_NNNN` PDF never replaces a file (spec 2.2).
    static void WritePdfReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        var name = OffloadReport.PdfName(plan.CardName, plan.Generation);
        var first = Path.Combine(plan.CardPaths[0], Layout.ReportDirName, name);
        bool replace = plan.Mode == PlanMode.Full;
        try
        {
            PdfReport.Write(report, first, replace: replace);
            foreach (var other in plan.CardPaths.Skip(1))
            {
                try { ProxyCopy.CopyFile(first, Path.Combine(other, Layout.ReportDirName, name), replace); }
                catch (Exception e) { log?.Invoke($"PDF copy to {other}: {e.Message}"); }
            }
        }
        catch (Exception e)
        {
            // The HTML report is already on disk; the PDF is a convenience copy.
            log?.Invoke($"PDF report failed: {e.Message}");
        }
    }

    void RewriteReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        foreach (var path in plan.CardPaths)
        {
            try { report.Write(path, includeHashRecords: false); }
            catch (Exception e) { log?.Invoke($"report rewrite {path}: {e.Message}"); /* the first report is already there; this only adds clip details */ }
        }
        WriteClipLists(plan, log);
    }

    /// Spec 3.3: `_REPORT\<card>_clips.csv/.ale` on every destination, from the reports there — every generation (R25).
    /// Derived files: a failure (a list held open in Excel, a disk gone) is only logged.
    void WriteClipLists(OffloadPlan plan, Action<string>? log)
    {
        foreach (var path in plan.CardPaths)
        {
            try { ClipList.WriteForCard(path, Config.Outputs); }
            catch (Exception e) { log?.Invoke($"clip list {path}: {e.Message}"); }
        }
    }
}
