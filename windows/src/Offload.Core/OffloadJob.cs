using System.Diagnostics;
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Space;
using Offload.Core.VerifyInProxy;

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
    /// "Zkopírovat znovu" (UI návrh 4): repair the files that failed in these card folders (`<root>\<date>\<card>` of the
    /// backup), each only where it failed (RepairPlanner); DestRoots, CardName and Date are taken from them.
    public IReadOnlyList<string>? RepairInto { get; set; }
    /// Ruling I1: the card the repaired backup was copied from (CardIdentity of the finished job) — a repair refuses
    /// another card in the drive before anything is read or written. Null = not known (a folder source): not checked.
    public string? RepairCardIdentity { get; set; }
    /// Where the end of the job is announced (spec 4.3): the copy taken when the job was started.
    public Notify.NotifySettings Notify { get; set; } = new();
    /// Plan 10 (R11): verify the primary destination from the proxies' reads and the others in the background, while
    /// the proxies render — on when proxies and verification are; settings "Ověřit primární disk během proxy", CLI
    /// --classic-verify turns it off (the read-back of every destination right after the copy, as before 0.6.0).
    public bool VerifyInProxy { get; set; } = true;
    /// Plan 10 (spec 6): the destination the proxies read and verify (0-based, CLI --primary N); null = the fastest one
    /// measured during the copy.
    public int? PrimaryDestination { get; set; }
    /// Task 2026-10-09: the folder every card's report is copied to on this computer (LocalReports.RootFor of the
    /// profile; CLI --no-local-report = null); null = no local copy (also every test that does not ask for one).
    public string? LocalReports { get; set; }
}

/// `Finishing` (plan 10): after the proxies, the verification reads what they did not and waits for the other disks.
public enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Finishing, Done, Failed, Cancelled }

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
        JobPhase.Finishing => "Dokončení ověření",
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
    /// R5 "Lze vyjmout": every file is copied onto every destination (written and flushed) — the card may be taken out,
    /// also while the verification still runs. Not "Lze formátovat", never green (ruling I2).
    public bool SafeToEject { get; init; }
    /// R5 "Lze formátovat": every file verified on every destination, none failed (and the whole backup so, for an append
    /// or a repair) — the one green verdict of a card (spec 2). With verify-in-proxy only at the end of stage 2.
    public bool SafeToFormat { get; init; }
    /// Spec 7: clips whose decoder reported a defect (content_warn) — formatting needs the user's confirmation.
    public int ContentWarnings { get; init; }
    /// Plan 10 "Dokončení ověření": bytes verified so far and to verify (all destinations).
    public long VerifiedBytes { get; init; }
    public long VerifyTotal { get; init; }
    public bool? CopySucceeded { get; init; }
    public string? Error { get; init; }
    /// Information for the job card, e.g. "Nic nového — záloha je úplná."
    public string? Note { get; init; }
    /// Pozastavit was pressed and not resumed (only while the job runs): it stops at its next safe point.
    public bool Paused { get; init; }
    /// The job really stands now — a worker waits at the gate ("Pozastaveno"; before that "Pozastavuje se…").
    public bool PauseHeld { get; init; }
    /// How long the job has stood paused so far (from Pozastavit to Pokračovat).
    public TimeSpan PausedTotal { get; init; }
    /// Warnings that do not fail the job (orange), one per line — e.g. unverified files of the earlier backup.
    public string? Warning { get; init; }
    /// Task 2026-10-09: why the copy of the report on this computer failed (Czech) — a note only, never the job's result.
    public string? LocalReportNote { get; init; }

    /// This status with `line` added to the warnings.
    public JobStatus WithWarning(string line) => this with { Warning = Warning == null ? line : Warning + "\n" + line };

    /// This status without the warning line `line` (one that no longer holds, e.g. a history record written later).
    public JobStatus WithoutWarning(string line) =>
        Warning?.Split('\n').Where(l => l != line).ToList() is { } rest ? this with { Warning = rest.Count == 0 ? null : string.Join("\n", rest) } : this;
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
    /// Plan 10: the verification running with the proxies (stage 2), null otherwise; the paused time when the copy ended.
    volatile VerifySession? session;
    TimeSpan pausedAtCopyEnd;
    /// Ruling I4: the card folders' lock a job whose reports say verifyPending holds until FinishVerification has written
    /// the verification results (taken in stage 1, let go in stage 2 — AppendLock.AcquireHeld).
    IDisposable? verifyLock;
    /// R6: the history entry a full copy wrote when it started ("unfinished"), finished by RecordHistory.
    string? startEntry;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
    public OffloadReport? Report { get => reportValue; private set => reportValue = value; }
    /// A repair (also "Nic k opravě"): the whole backup as its reports said it after the job (BackupState, ruling I2) —
    /// read on the job's thread, so the card's words need no disk on the UI thread; null for other jobs or unread.
    public BackupState? Backup { get => backupValue; private set => backupValue = value; }
    volatile BackupState? backupValue;
    /// The card in the source's drive when the copy started (`CardIdentity`), read on the copy thread; null before
    /// that or when unknown. "Vysunout kartu" ejects only this card.
    public string? SourceIdentity => sourceIdentity;
    /// The day reports (HTML) this job made again when it ended, one per destination that has it (the CLI prints them).
    public IReadOnlyList<string> DayReports => dayReports;
    /// Task 2026-10-09: the local copy of the card's report (`<folder>\<datum>\<karta>`) once made; null otherwise.
    public string? LocalReport => localReport;
    volatile string? localReport;

    /// Tests: what CardIdentity.Of / CardVolume.Of would read from the drive.
    internal Func<string, string?> Identify { get; init; } = CardIdentity.Of;
    internal Func<string, CardVolume?> VolumeOf { get; init; } = CardVolume.Of;
    /// Tests: NativeIO.KeepAwake. Ruling M6: the PC is kept awake for the whole of stage 1 and of stage 2 — also while
    /// the job stands paused (a sleep would drop the USB disks with open `.offload-part` files) — and released on
    /// cancel, at the end and on a failure (`using`); a crashed process ends its thread's request with it.
    internal Func<IDisposable> KeepAwake { get; init; } = NativeIO.KeepAwake;
    /// The whole backup's state after "Nic k opravě" (tests replace it to make it unreadable).
    internal Func<IReadOnlyList<string>, BackupState> ReadBackupState { get; init; } = BackupState.Of;
    /// Tests: UnbufferedCheck.Problem — whether a destination may be verified from the proxies' reads (spec 8, R8).
    internal Func<string, bool, string?> CacheCheck { get; init; } = UnbufferedCheck.Problem;
    /// Tests: called before the verified generation is committed into each card folder (a throw = that commit failed).
    internal Action<string>? BeforeCommit { get; init; }

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

    // A preview job (DemoScenario): its status comes from here; it never runs.
    Func<JobStatus>? preview;

    /// `Offload.exe --demo` only (DemoScenario): a job that never runs — Status() returns `status()`, Plan and Report are
    /// the given ones. RunCopy and RunPost do nothing.
    public static OffloadJob ForPreview(JobConfig config, OffloadPlan? plan, Func<JobStatus> status, OffloadReport? report = null) =>
        new(config) { preview = status, Plan = plan, Report = report };

    /// The project of the earlier backup: the first destination whose report can be read decides; `fallback` only when
    /// none can (M4).
    public static string? EarlierProject(IReadOnlyList<string> cardPaths, string? fallback)
    {
        var earlier = cardPaths.Select(p => CardRecord.Load(p)).FirstOrDefault(r => r.Generations.Count > 0);
        return earlier != null ? earlier.Project : fallback;
    }

    /// A full copy, or with Config.AppendInto an append into those card folders (`NothingNew` when it has nothing to copy).
    public OffloadPlan Prepare()
    {
        if (Plan is { } done) return done;
        if (Config.RepairInto is { Count: > 0 } broken)
        {
            var repair = Engine.PrepareRepair(Engine.PlanRepair(Config.Source, broken));
            repair.Project = EarlierProject(broken, Config.Project);   // the backup's project, as an append (M4)
            return Plan = repair;
        }
        if (Config.AppendInto is { Count: > 0 } into)
        {
            var a = appendPlan = Engine.PlanAppend(Config.Source, into);
            var append = Engine.PrepareAppend(a, EstimateExtra);
            // M4: an append records the project of the earlier backup (its first generation), never a different one;
            // Config.Project only stands in when that report cannot be read.
            append.Project = EarlierProject(into, Config.Project);
            return Plan = append;
        }
        var full = Engine.Prepare(Config.Source, Config.DestRoots, Config.CardName, Config.Date, EstimateExtra);
        full.Project = Config.Project;
        return Plan = full;
    }

    /// Spec 2.5: proxies and report on top of the card, from the clip names (the clips are not read before the copy).
    SpaceExtra EstimateExtra(SourceScan scan) => SpaceEstimator.Extra(ClipDetector.Detect(scan.Files), Config.Proxy, Config.Tools.Resolved());

    public void Cancel() => Engine.Cancel();

    /// Tests: the app ends right after stage 1 (a crash, the PC switched off) — the folders' lock goes with the process,
    /// nothing more is written (no stage 2, no commit): what a resume (R6) later finds.
    internal void Abandon() => Interlocked.Exchange(ref verifyLock, null)?.Dispose();

    public bool IsCancelled => Engine.IsCancelled;
    /// A DemoScenario preview: it never runs (`Offload.exe --demo`).
    public bool IsPreview => preview != null;

    /// "Pozastavit" (UI návrh 9): stage 1 stops before its next block of the card or of the read-back, stage 2 before
    /// its next clip and before the PDF and the day report — never in the middle of a block, a clip, a report or a lock
    /// it would hold (an append's or a repair's folder lock stays held through stage 1). Cancel while paused ends the
    /// job as a cancel does.
    public void Pause() => Engine.Pauses.Pause();

    public void Resume() => Engine.Pauses.Resume();

    public bool IsPaused => Engine.Pauses.IsPaused;

    public JobStatus Status()
    {
        if (preview != null)
        {
            var p = preview();
            if (IsCancelled && !p.Phase.IsFinished()) return p with { Phase = JobPhase.Cancelled };   // closing the demo cancels it
            // Pozastavit in the demo: nothing runs, so the pause holds at once.
            bool live = p.Phase != JobPhase.Waiting && !p.Phase.IsFinished();
            return live && Engine.Pauses.IsPaused ? p with { Paused = true, PauseHeld = true, PausedTotal = Engine.Pauses.PausedTotal } : p;
        }
        JobStatus s;
        bool wasStarted;
        lock (gate) { s = state; wasStarted = started; }
        bool stage1 = s.Phase is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting;
        if (s.Phase == JobPhase.Finishing && session is { } verifying)
            s = s with { VerifiedBytes = verifying.VerifiedBytes.Sum(), VerifyTotal = verifying.ToVerify.Sum() };
        if (stage1)
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
        var pauses = Engine.Pauses;
        bool running = s.Phase != JobPhase.Waiting && !s.Phase.IsFinished();
        // Stage 1 stands only when the destinations are idle too (the engine's snapshot says so — fix round 1); stage 2 only
        // when no verification thread is mid-block (plan 10).
        bool held = stage1 ? s.Copy!.PauseHeld : pauses.IsHolding && session?.Reading != true;
        return s with { Paused = running && pauses.IsPaused, PauseHeld = running && held, PausedTotal = pauses.PausedTotal };
    }

    void Update(Func<JobStatus, JobStatus> change)
    {
        bool ended;
        lock (gate)
        {
            state = change(state);
            ended = state.Phase.IsFinished();
        }
        if (ended) Engine.Pauses.Freeze();   // M1: the paused time stops with the job, also when it ended paused
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
        if (preview != null) return false;
        IdentifyCard(log);
        // Ruling I1: a repair copies only from the card the backup was made from (asked again like after a pause — a
        // reader may answer late once); the answer is checked below, where a refusal fails the job.
        bool otherCard = Config.RepairInto is { Count: > 0 } && Config.RepairCardIdentity is string want && !CardInDrive(want);
        // Ruling I1: after a pause the copy asks again which card is in the drive; a folder source (no identity) is not asked.
        if (sourceIdentity is string copied)
            Engine.SameCard = () => string.Equals(Identify(Config.Source), copied, StringComparison.OrdinalIgnoreCase);
        lock (gate)
        {
            started = true;
            // Cancelled before it began: stay Cancelled, never prepare or copy.
            if (IsCancelled)
            {
                state = state with { Phase = JobPhase.Cancelled };
                Engine.Pauses.Freeze();
                return false;
            }
        }
        using var awake = KeepAwake();
        IDisposable? folders = null;
        bool copying = false;
        bool writing = false;   // Engine.Run started: a failure from here on may be resumed (R6)   // the engine may have written to the destinations: a day report is due even on failure
        try
        {
            // An append holds its card folders until stage 1 ends (fix round 1, item 1) and plans under that hold — a
            // plan made before (the CLI prints one first) is made again, another append may have finished meanwhile.
            // Released in `finally`, so the NothingNew path below still holds them while it heals the hash list.
            // A repair takes the same lock: an append and a repair never number the same generation.
            // Ruling I4 (plan 10): held by a thread of its own — with verify-in-proxy it passes to stage 2 (another thread).
            if (otherCard) throw OffloadError.Preflight(PreflightCode.RepairConflict, Msg.RepairOtherCard);
            folders = (Config.RepairInto ?? Config.AppendInto) is { Count: > 0 } into ? AppendLock.AcquireHeld(into) : null;
            if (folders != null)
            {
                Plan = null;
                appendPlan = null;
            }
            var plan = Prepare();
            if (plan.UnverifiedEarlier > 0) Update(s => s.WithWarning(Msg.UnverifiedEarlier(plan.UnverifiedEarlier)));
            Update(s => s with { Phase = JobPhase.Copying });
            copying = true;
            // Plan 10 (R11): the read-back waits for stage 2, where the proxies read the copies.
            Engine.Options.DeferVerify = VerifiesInProxy(plan);
            // Ruling I4: a full copy that will write reports saying verifyPending takes its new card folders' lock first —
            // no append, repair or resume may touch the generation until its verification results are written.
            if (Engine.Options.DeferVerify && folders == null) folders = AppendLock.AcquireHeld(plan.CardPaths);
            // R6: history knows the copy from its start — one the app never finishes can be resumed from its card.
            if (plan.Mode == PlanMode.Full) RecordStart(plan, log);
            writing = true;   // from here on the destinations may hold what a resume completes
            var report = Engine.Run(plan);
            Report = report;
            pausedAtCopyEnd = Engine.Pauses.PausedTotal;
            // Ruling I4: the folders stay held until FinishVerification has written the verification results (stage 2,
            // or below when the copy was cancelled right after it wrote its reports).
            if (report.VerifyPending == true) (verifyLock, folders) = (folders, null);
            // R5: "Lze formátovat" only verified everywhere; "Lze vyjmout" once everything is on every disk.
            bool safe = report.SafeToFormat;
            bool allCopied = report.Success;
            BackupState? whole = null;
            if (plan.Mode == PlanMode.Repair)
            {
                // Ruling I2: a repair's report lists only the repaired files — whether the card may be ejected is read
                // from the whole backup after it (every card folder's reports, still under the folder lock).
                var w = whole = Backup = BackupState.Of(plan.CardPaths);
                safe &= w.Failed == 0 && w.Unverified == 0;
                allCopied &= w.Failed == 0;
                if (w.Unverified > 0) Update(s => s.WithWarning(Msg.UnverifiedEarlier(w.Unverified)));
            }
            // Fix round 1, item 1: files shot after the backup are in no report — a repair does not copy them (nor a
            // resume of a generation already in an ASC MHL chain, R6), and the card formatted now would lose them.
            safe &= plan.NotInBackup == 0;
            allCopied &= plan.NotInBackup == 0;
            if (plan.NotInBackup > 0) Update(s => s.WithWarning(Msg.CardFilesNotInBackup(plan.NotInBackup)));
            // The same for an append (fix round 1, item 1, plan 6's verdict checked): its new files are verified, but
            // files of the earlier backup that were never read back keep the card from "kartu lze vysunout".
            if (plan.Mode == PlanMode.Append) safe &= plan.UnverifiedEarlier == 0;
            Update(s => s with { SafeToEject = allCopied, SafeToFormat = safe, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                ForgetStart(log);   // a cancelled copy is not recorded in history (as before plan 10)
                // Cancelled after the copy wrote its verifyPending reports: nothing was verified — the generation is
                // committed with its copies "written" (unverified), as a cancel in stage 2 does (ruling I1: never green).
                if (report.VerifyPending == true) Report = FinishVerification(null, plan, report, log);
                CopyLocally(plan, log);
                // Marked before the phase changes: whoever sees it finished also sees the day report pending (fix round 1, item 3).
                var cancelled = DayReportNext();   // the card shows as not complete (spec 3.2); not recorded in history
                Update(s => s with { Phase = JobPhase.Cancelled });
                return cancelled;
            }
            if (whole is { } state) RecordRepair(plan, report, state, log);
            else RecordHistory(plan, report, log);
            if (Engine.CardChanged)
            {
                // Ruling I1: another card (or none) after Pokračovat — the files copied before the pause are in the
                // report and in history; the rest was never read. The job fails; no stage 2 (lists and PDF as without it).
                WriteClipLists(plan, log);
                WritePdfReports(report, plan, log);
                CopyLocally(plan, log);
                var next = DayReportNext();
                Update(s => s with { Phase = JobPhase.Failed, Error = Msg.CardChangedWhilePaused });
                return next;
            }
            // A repair rewrites files whose clips were analysed with the backup: no stage 2, only its PDF and the lists.
            if (plan.Mode == PlanMode.Repair || (!Config.AnalyzeClips && !Config.Proxy.Enabled))
            {
                WriteClipLists(plan, log);   // the clips found in the files, without metadata (spec 3.3)
                WritePdfReports(report, plan, log);
                CopyLocally(plan, log);
                var done = DayReportNext();
                Update(s => s with { Phase = JobPhase.Done });
                return done;
            }
            CopyLocally(plan, log);   // the report of the copy is on this computer before the proxies start
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.RepairNothing)
        {
            // Not an error: every file is good everywhere (another repair or an append got there first). Nothing to
            // copy does not make an earlier copy verified (fix round 1, item 3; as NothingNew says it).
            // Fix round 2, item B: history learns the backup as its reports say it is now (another job — the CLI, a
            // job without history — may have repaired or read it back since).
            // Batch 10–12 review: SafeToEject = (Warning == null) below, so every reason not to eject sets a warning —
            // card files no report records, files failed or unverified in the reports read now, or reports not read.
            if (e.NotInBackup > 0) Update(s => s.WithWarning(Msg.CardFilesNotInBackup(e.NotInBackup)));
            try
            {
                var whole = Backup = ReadBackupState(Config.RepairInto!);
                if (whole.Failed > 0) Update(s => s.WithWarning(Msg.BackupStillFailed(whole.Failed)));
                if (whole.Unverified > 0) Update(s => s.WithWarning(Msg.UnverifiedEarlier(whole.Unverified)));
                RefreshFromBackup(Config.RepairInto!, whole, log);
            }
            catch (Exception x)
            {
                // Not read means not checked: never "kartu lze vysunout" on it.
                log?.Invoke($"backup state: {x.Message}");
                Update(s => s.WithWarning(Msg.BackupStateUnreadable(x.Message)));
            }
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message, SafeToEject = s.Warning == null, SafeToFormat = s.Warning == null });
            return false;
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
                CopyLocally(a.CardPaths, log);   // a hash list healed, or a local copy still missing
            }
            // Conservative (no false green): any warning keeps both verdicts off, as before plan 10.
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message, SafeToEject = s.Warning == null, SafeToFormat = s.Warning == null });
            return false;
        }
        catch (OperationCanceledException)
        {
            ForgetStart(log);
            EndPendingUnverified(log);
            if (writing && Plan is { } cancelledPlan) CopyLocally(cancelledPlan, log);
            var next = copying && DayReportNext();
            Update(s => s with { Phase = JobPhase.Cancelled });
            return next;
        }
        catch (Exception e)
        {
            // Fix round 1 (M-e): failed before the copy wrote anything, or with nothing a resume could complete (no
            // manifest record, no pending report) — no "nedokončeno" entry.
            if (!writing || NothingToResume()) ForgetStart(log);
            EndPendingUnverified(log);
            if (writing && Plan is { } failedPlan) CopyLocally(failedPlan, log);   // whatever report the copy left
            var next = copying && DayReportNext();   // ruling M9: best effort after a failed copy too
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
            return next;
        }
        finally
        {
            folders?.Dispose();
        }
    }

    /// Fix round 1 (M-e): the card folders hold no generation a resume (R6) could complete.
    bool NothingToResume()
    {
        try { return Plan is not { } p || ResumePlanner.Find(p.CardPaths) == null; }
        catch (Exception) { return false; }   // diverged: kept, a look by hand is due
    }

    /// Stage 1 ended with an exception after its reports said verifyPending (no stage 2 follows): the generation is
    /// committed unverified and the folders are let go — never left locked, never green.
    void EndPendingUnverified(Action<string>? log)
    {
        if (Volatile.Read(ref verifyLock) == null || Plan is not { } plan || Report is not { VerifyPending: true } pending) return;
        Report = FinishVerification(null, plan, pending, log);
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

    /// Ruling I1 for a repair: the card in the drive is `want`. A different or unreadable answer is asked again
    /// OffloadEngine.IdentifyRetries times 250 ms apart, as after a pause.
    bool CardInDrive(string want)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (string.Equals(sourceIdentity, want, StringComparison.OrdinalIgnoreCase)) return true;
            if (attempt >= OffloadEngine.IdentifyRetries || IsCancelled) return false;
            Thread.Sleep(250);
            try { sourceIdentity = Identify(Config.Source); }
            catch (Exception) { sourceIdentity = null; }
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

    /// Tests: how long a record of this job waits for the history lock (fix round 1, item 2b: 60 s — jobs never run on
    /// the UI thread), and the background retry of a record that still could not be written: its pause and attempts.
    internal TimeSpan HistoryLockWait { get; init; } = TimeSpan.FromSeconds(60);
    internal TimeSpan HistoryRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    internal int HistoryRetries { get; init; } = 20;

    readonly Lock historyGate = new();
    readonly Queue<(string What, Func<HistoryStore, HistoryWrite> Write)> historyBacklog = new();
    bool historyRetrying;

    /// Fix round 1, item 2b: a record of this job's own (its start, its copy, its verification, a repair, a removal) is
    /// never dropped silently. It waits up to HistoryLockWait for the history lock; still busy — logged, a Czech warning on
    /// the job card, and it is written in the background as soon as it can be (in order: a later record waits behind it);
    /// the warning goes once it is written, or says so for good after HistoryRetries attempts.
    void RecordReliably(string what, Func<HistoryStore, HistoryWrite> write, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        lock (historyGate)
        {
            if (historyBacklog.Count > 0)
            {
                historyBacklog.Enqueue((what, write));
                return;
            }
        }
        if (AttemptHistory(what, write, log) != HistoryWrite.Busy) return;
        log?.Invoke($"history: busy for {HistoryLockWait.TotalSeconds:0} s — the {what} record is written later");
        lock (historyGate)
        {
            historyBacklog.Enqueue((what, write));
            if (historyRetrying) return;
            historyRetrying = true;
        }
        Update(s => s.WithWarning(Msg.HistoryDelayed));
        new Thread(() => RetryHistory(log)) { IsBackground = true, Name = "offload.history-retry" }.Start();
    }

    HistoryWrite AttemptHistory(string what, Func<HistoryStore, HistoryWrite> write, Action<string>? log)
    {
        try
        {
            var folder = Config.HistoryFolder ?? HistoryStore.DefaultFolder;
            return write(new HistoryStore(folder, log) { LockTimeout = HistoryLockWait });
        }
        catch (Exception e)
        {
            log?.Invoke($"history ({what}): {e.Message}");
            return HistoryWrite.Failed;
        }
    }

    void RetryHistory(Action<string>? log)
    {
        for (int attempt = 0; attempt < HistoryRetries; attempt++)
        {
            Thread.Sleep(HistoryRetryDelay);
            while (true)
            {
                (string What, Func<HistoryStore, HistoryWrite> Write) next;
                lock (historyGate)
                {
                    if (historyBacklog.Count == 0)
                    {
                        historyRetrying = false;
                        Update(s => s.WithoutWarning(Msg.HistoryDelayed));
                        return;
                    }
                    next = historyBacklog.Peek();
                }
                if (AttemptHistory(next.What, next.Write, log) == HistoryWrite.Busy) break;
                log?.Invoke($"history: the {next.What} record written after the history was busy");
                lock (historyGate) historyBacklog.Dequeue();
            }
        }
        int lost;
        lock (historyGate)
        {
            lost = historyBacklog.Count;
            historyBacklog.Clear();
            historyRetrying = false;
        }
        log?.Invoke($"history: still busy — {lost} record(s) of this job not written");
        Update(s => s.WithoutWarning(Msg.HistoryDelayed).WithWarning(Msg.HistoryNotWritten));
    }

    /// Tests: the history records of this job are all written (none waits in the background).
    internal bool HistorySettled
    {
        get { lock (historyGate) return historyBacklog.Count == 0 && !historyRetrying; }
    }

    /// Fix round 1, item 2c: the backup has no generation left to complete and no report still pending — a stale
    /// "nedokončeno" heals. Read before the history lock is taken (it reads the card folders).
    internal static bool Settled(IReadOnlyList<string> cardPaths)
    {
        try
        {
            return ResumePlanner.Find(cardPaths) == null && cardPaths.All(p => !CardRecord.Load(p).VerifyPending);
        }
        catch (Exception)
        {
            return false;   // diverged or unreadable: stays as it is
        }
    }

    /// R6: the entry of a full copy as it starts — "unfinished", no files yet; RecordHistory replaces it at the end.
    void RecordStart(OffloadPlan plan, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        var id = HistoryStore.NewId(DateTime.UtcNow);
        var cardPaths = plan.CardPaths;
        var entry = new HistoryEntry
        {
            Id = id, Source = plan.Source, CardName = plan.CardName, Date = plan.Date, Project = plan.Project, Unfinished = true,
            Finished = DateTime.UtcNow, HashAlgorithms = [.. HashKinds.Names(plan.Hashes)], Card = cardVolume?.ToHistoryCard(),
            Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
        };
        startEntry = id;   // the later records of this job name it, also while it waits to be written (in order)
        RecordReliably("start", store => store.Write(id, _ => (entry, [])), log);
    }

    /// The start entry of a copy that was cancelled (or failed before it wrote anything) goes again.
    void ForgetStart(Action<string>? log)
    {
        if (startEntry is not string id) return;
        startEntry = null;
        RecordReliably("removal", store => store.RemoveEntry(id), log);
    }

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job. An append adds
    /// its generation to the entry of the card folder it went into (spec 5.2) — or starts one when history does not
    /// know the backup — under one hold of the history lock (ruling I2).
    void RecordHistory(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        var card = plan.FullScan ?? plan.Scan;
        var (fingerprint, shape) = CardFingerprint.Compute(card);
        var cardPaths = plan.CardPaths;
        var start = startEntry;
        // Fix round 2, item C (ruling R2): verified as the card folders' reports say it now — the one definition the
        // day report uses too (a generation cancelled during its read-back is not in history, but is on the disks).
        var whole = BackupState.Of(cardPaths);
        bool settled = plan.Resuming && report.VerifyPending != true && Settled(cardPaths);
        RecordReliably("copy", store =>
        {
            // The id only finds the entry; everything about it is read and changed under the lock below.
            var id = (plan.Mode == PlanMode.Append ? EntryOf(store, cardPaths)?.Id : start) ?? HistoryStore.NewId(report.Finished);
            return store.Write(id, existing =>
            {
                if (existing is { Unfinished: true, Generations.Count: 0 })
                {
                    // R6: the start entry (RecordStart, no generation yet) becomes the copy's entry.
                    existing.Unfinished = false;
                    existing.Success = true;
                    existing.Verified = true;
                    existing.FailedFiles = null;
                }
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
                entry.Verified = whole.Unverified == 0;
                entry.FailedFiles = entry.Success ? null : (entry.FailedFiles ?? 0) + report.FailedFiles.Count;
                entry.Generations.RemoveAll(g => g.Seq == plan.Generation);
                entry.Generations.Add(new HistoryGeneration
                {
                    Seq = plan.Generation,
                    Kind = plan.Generation == 1 ? HistoryGeneration.Full : HistoryGeneration.Append,   // a resumed full copy is "full"
                    Finished = report.Finished,
                    Files = plan.Scan.Files.Count,
                    Bytes = plan.Scan.TotalBytes,
                    Verified = report.Verified,
                });
                entry.Reels = ReelSuggestion.FromClips(ClipDetector.Detect(card.Files)).Reels.ToList();
                // R6: a resumed generation — the entry as the folders say it now (its first run may have counted failures).
                if (plan.Resuming) Apply(entry, whole, settled);
                // Ruling I5: "nedokončeno" until the verification results are written (RecordVerified) — a backup whose
                // app ended in stage 2 offers "Dokončit přerušenou zálohu" on its card, also with nothing new on it.
                entry.Unfinished = report.VerifyPending == true;
                return WithCard(store, entry, existing, card, fingerprint, shape);
            });
        }, log);
    }

    /// A repair adds its generation to the entry of the card folders it repaired; whether the card is now backed up and
    /// verified is read from the reports on the destinations (R4, `whole`) — so a repair that fixed the last file makes
    /// the entry good again. The card's files, fingerprint and reels stay as they were (nothing new was copied).
    void RecordRepair(OffloadPlan plan, OffloadReport report, BackupState whole, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        bool settled = Settled(plan.CardPaths);
        RecordReliably("repair", store =>
        {
            if (EntryOf(store, plan.CardPaths) is not { } found)
            {
                log?.Invoke("history: the repaired backup is not in history — nothing recorded");
                return HistoryWrite.Unchanged;
            }
            return store.Write(found.Id, existing =>
            {
                if (existing == null) return null;
                existing.Finished = report.Finished;
                Apply(existing, whole, settled);
                existing.Generations.RemoveAll(g => g.Seq == plan.Generation);
                existing.Generations.Add(new HistoryGeneration
                {
                    Seq = plan.Generation, Kind = HistoryGeneration.Repair, Finished = report.Finished,
                    Files = plan.Scan.Files.Count, Bytes = plan.Scan.TotalBytes, Verified = report.Verified,
                });
                return (existing, null);
            });
        }, log);
    }

    /// Ruling R2: whether the backup is complete and read back, as BackupState read it from the card folders — the one
    /// definition the day report (CardRecord.Verified) and the eject verdict use. `settled` (fix round 1, item 2c): no
    /// generation is left to complete and no report is pending — a stale "nedokončeno" heals.
    static void Apply(HistoryEntry entry, BackupState whole, bool settled)
    {
        entry.Success = whole.Failed == 0;
        entry.FailedFiles = whole.Failed == 0 ? null : whole.Failed;
        entry.Verified = whole.Unverified == 0;
        if (settled) entry.Unfinished = false;
    }

    /// "Nic k opravě": the entry of the backup learns its state from the reports — no new generation.
    void RefreshFromBackup(IReadOnlyList<string> cardPaths, BackupState whole, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        bool settled = Settled(cardPaths);
        RecordReliably("refresh", store => EntryOf(store, cardPaths) is { } found
            ? store.Write(found.Id, existing =>
            {
                if (existing == null) return null;
                Apply(existing, whole, settled);
                return (existing, null);
            })
            : HistoryWrite.Unchanged, log);
    }

    /// "Nic nového" (minors M17): the entry of that backup learns the card as it is now — no new generation.
    void RefreshHistory(AppendPlan a, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        var (fingerprint, shape) = CardFingerprint.Compute(a.FullScan);
        var whole = BackupState.Of(a.CardPaths);   // fix round 2, item C: as the reports say it now
        bool settled = Settled(a.CardPaths);
        RecordReliably("refresh", store => EntryOf(store, a.CardPaths) is { } found
            ? store.Write(found.Id, existing =>
            {
                if (existing == null) return null;
                Apply(existing, whole, settled);
                return WithCard(store, existing, existing, a.FullScan, fingerprint, shape);
            })
            : HistoryWrite.Unchanged, log);
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

    /// Plan 10 (R11): whether this job verifies with the proxies — proxies and verification on, verify-in-proxy not switched
    /// off, ffmpeg found, and not a repair (a repair has no stage 2 and always reads back, ruling M16).
    bool VerifiesInProxy(OffloadPlan plan) =>
        Config.Verify && Config.VerifyInProxy && Config.Proxy.Enabled && plan.Mode != PlanMode.Repair && Config.Tools.Resolved().Ffmpeg != null;

    /// Stage 2 — clip analysis, thumbnails, optional proxies, PDF. Reads only from the destinations.
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it). With verify-in-proxy the
    /// verification ends here too (FinishVerification) — also when the analysis or the proxies failed or were cancelled.
    public void RunPost(Action<string>? log = null)
    {
        if (preview != null) return;
        if (dayReportOnly)
        {
            try
            {
                using var keepAwake = KeepAwake();
                if (Plan is { } p)
                {
                    RegenerateDayReports(p, log);
                    CopyLocally(p, log, day: true);
                }
            }
            finally
            {
                dayReportPending = false;
            }
            return;
        }
        if (Plan is not { } plan || Report is not { } report) return;
        using var awake = KeepAwake();
        VerifySession? verifying = null;
        OffloadReport final = report;
        Exception? failed = null;
        try
        {
            // Inside the try (ruling M2): a session that cannot start fails stage 2, the generation is still committed
            // below (unverified) and the folders let go.
            verifying = session = StartVerification(plan, report);
            final = AnalyzeAndRender(plan, report, log);
        }
        catch (Exception e)
        {
            failed = e;
            log?.Invoke($"stage 2 failed: {e.Message}");
        }
        // Spec 7 (final review, item 1): the decoder's content warnings, however the copy was verified (in the proxies,
        // the classic read-back, or not at all) — the card then asks for a confirmation before formatting, no chime.
        int warnings = final.ContentWarnings.Count;
        if (warnings > 0) Update(st => st.WithWarning(Msg.ContentWarnings(warnings)) with { ContentWarnings = warnings });
        try
        {
            if (report.VerifyPending == true) final = FinishVerification(verifying, plan, final, log);
        }
        finally
        {
            session = null;
            verifying?.Dispose();
        }
        Report = final;
        if (failed == null)
        {
            Engine.WaitWhilePaused();   // before the PDF and the day report (a cancel meanwhile still writes them)
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
            CopyLocally(plan, log, day: true);   // analysis, proxies, verification results, PDF and the day report
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        else
        {
            WriteClipLists(plan, log);   // the generation must not be missing from the lists
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
            CopyLocally(plan, log, day: true);
            Update(s => s with { Phase = JobPhase.Failed, Error = failed.Message });
        }
    }

    /// Plan 10: the session of a copy whose reports say verifyPending — every destination's cache check first (spec 8),
    /// then the background reads start; null when this job verified in stage 1.
    VerifySession? StartVerification(OffloadPlan plan, OffloadReport report)
    {
        if (report.VerifyPending != true) return null;
        var paths = plan.CardPaths;
        var why = paths.Select((p, d) => Config.VerifyInProxy ? CacheCheck(p, d < Engine.WrittenUnbuffered.Count && Engine.WrittenUnbuffered[d]) : Msg.ClassicSetting).ToList();
        var s = new VerifySession(plan, report, Engine.Blocks, Engine.Options.ChunkSize, Engine.WriteRates, Config.PrimaryDestination, why,
            Engine.Pauses, Engine.Cancellation, (file, block) => Engine.CardStillThere() != false   // a folder source has no card to lose
                ? VerifySession.ReadCardBlock(Layout.Join(plan.Source, plan.Scan.Files[file].RelPath), block, Engine.Options.ChunkSize)
                : null)
        {
            StallTimeout = Engine.Options.ProxyStallTimeout ?? ProxyRenderer.DefaultStallTimeout,
        };
        s.StartBackground();
        return s;
    }

    /// Plan 10, the end of the verification (spec 6): the fill-in and the background reads end, the report gets the
    /// verdicts and is committed — ASC MHL generation, reports, hash lists (OffloadReport.Commit) — into every card folder
    /// while the job still holds their lock (ruling I4, taken in stage 1), history learns it, the lock goes, and the
    /// card's verdict "Lze formátovat" is decided. `s` null: nothing was verified (cancelled right after the copy, or the
    /// session never started) — the copies stay "written", unverified. Never throws.
    /// Ruling I1: "Lze formátovat" only when the results were written into EVERY folder and the folders say so — read
    /// back with BackupState (the one definition history and the day report use), never from this report alone. A
    /// folder whose commit failed keeps its reports saying verifyPending (unverified); "Dokončit přerušenou zálohu"
    /// (an append, R6) finishes it.
    OffloadReport FinishVerification(VerifySession? s, OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        var held = Interlocked.Exchange(ref verifyLock, null);
        try
        {
            if (s != null) Update(st => st with { Phase = JobPhase.Finishing });
            try { s?.Finish(); }
            catch (Exception e) { log?.Invoke($"verification: {e.Message}"); }
            IReadOnlyList<string> notes = s?.Notes ?? [];
            var paused = Engine.Pauses.PausedTotal - pausedAtCopyEnd;
            var final = s != null
                ? report.WithVerification(s.Results, !IsCancelled, s.Primary, s.ClassicWhy, notes, DateTime.UtcNow, paused)
                : report.WithVerification(Unread(report), verified: false, -1, [.. plan.CardPaths.Select(_ => (string?)null)], notes, DateTime.UtcNow, paused);
            foreach (var note in notes) log?.Invoke(note);
            if (held == null)
            {
                // Never without the folders' lock (another job may be at them): taken now or not committed at all.
                try { held = AppendLock.AcquireHeld(plan.CardPaths); }
                catch (OffloadError e) { Update(st => st.WithWarning(Msg.VerifyNotCommitted(e.Message))); }
            }
            bool committed = held != null;
            var written = new List<string>();
            if (held != null)
            {
                foreach (var path in plan.CardPaths)
                {
                    try
                    {
                        BeforeCommit?.Invoke(path);
                        // An unplugged disk: never a folder made anew for the record alone.
                        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(Msg.CardFolderGone);
                        final.Commit(path);
                        written.Add(path);
                    }
                    catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException or OffloadError or InvalidOperationException)
                    {
                        committed = false;
                        log?.Invoke($"commit {path}: {e.Message}");
                        Update(st => st.WithWarning(Msg.VerifyNotCommitted($"{path}: {e.Message}")));
                    }
                }
            }
            if (!committed && written.Count > 0)
            {
                // Fix round 1 (M-a): the folders that were written say so — on its own a folder would look verified
                // (its day report green) while another destination still lacks the results. A resume clears it.
                final.VerifyIncomplete = true;
                foreach (var path in written)
                {
                    try { final.Commit(path); }
                    catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException or OffloadError or InvalidOperationException)
                    {
                        log?.Invoke($"commit {path} (incomplete): {e.Message}");
                    }
                }
            }
            BackupState? whole = null;
            try { whole = ReadBackupState(plan.CardPaths); }
            catch (Exception e)
            {
                log?.Invoke($"backup state: {e.Message}");
                Update(st => st.WithWarning(Msg.BackupStateUnreadable(e.Message)));
            }
            if (whole is { } w) RecordVerified(plan, final, w, committed, log);
            bool safe = committed && final.SafeToFormat && whole is { Failed: 0, Unverified: 0 } && plan.NotInBackup == 0;
            if (notes.Count > 0) Update(st => st.WithWarning(notes[0]));
            Update(st => st with { SafeToFormat = safe, CopySucceeded = st.CopySucceeded != false && final.Success });
            return final;
        }
        catch (Exception e)
        {
            // Never thrown out of stage 2, never green.
            log?.Invoke($"verification: {e.Message}");
            Update(st => st.WithWarning(Msg.VerifyNotCommitted(e.Message)) with { SafeToFormat = false });
            return report;
        }
        finally
        {
            held?.Dispose();
        }
    }

    /// Every copy as the copy left it (nothing read): "written" stays unverified, "failed" stays failed.
    static List<DestFileResult[]> Unread(OffloadReport report) =>
        [.. Enumerable.Range(0, report.Destinations.Count).Select(d => report.Files.Select(f => (d < f.Destinations.Count ? f.Destinations[d] : "pending") switch
        {
            "written" when f.SourceError == null => new DestFileResult(FileState.Written, null),
            "verified" => new DestFileResult(FileState.Verified, null),
            "failed" => new DestFileResult(FileState.Failed, d < f.Errors.Count ? f.Errors[d] : null),
            _ => new DestFileResult(FileState.Pending, null),
        }).ToArray())];

    /// History learns the verification (fix round 2, item C: as the card folders' reports say it now — `whole`).
    /// `committed`: the results were written into every folder — only then the entry is no longer "nedokončeno" (I5).
    void RecordVerified(OffloadPlan plan, OffloadReport report, BackupState whole, bool committed, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        RecordReliably("verification", store => EntryOf(store, plan.CardPaths) is { } found
            ? store.Write(found.Id, existing =>
            {
                if (existing == null) return null;
                Apply(existing, whole, settled: false);
                existing.Finished = report.Finished;
                existing.Unfinished = !committed;
                foreach (var g in existing.Generations.Where(g => g.Seq == plan.Generation)) g.Verified = report.Verified;
                return (existing, null);
            })
            : HistoryWrite.Unchanged, log);
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
            StallTimeout = Engine.Options.ProxyStallTimeout ?? ProxyRenderer.DefaultStallTimeout,
            Log = log,
        };
        var cardPaths = plan.CardPaths;
        var fileIndex = report.Files.Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i);
        bool Good(string state) => state == "verified" || (!report.Verified && state == "written");
        var verifying = session;
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card. With
        /// verify-in-proxy the primary destination first (its copy is verified by this read, spec 5).
        int? GoodDest(Clip clip)
        {
            if (verifying is { Primary: >= 0 and var p } && clip.Segments.All(seg => fileIndex.TryGetValue(seg, out var fi) && Good(report.Files[fi].Destinations[p])))
                return p;
            for (int d = 0; d < cardPaths.Count; d++)
                if (clip.Segments.All(seg => fileIndex.TryGetValue(seg, out var fi) && Good(report.Files[fi].Destinations[d]))) return d;
            return null;
        }

        var entries = clips.Select(c => new ClipEntry { Clip = c }).ToList();
        Update(s => s with { Phase = JobPhase.Analyzing, ClipCount = entries.Count });
        for (int i = 0; i < entries.Count; i++)
        {
            // A pause waits between clips: the clip being probed or rendered finishes first (no ffmpeg is suspended, so
            // its stall watchdog never sees the pause).
            if (IsCancelled || !Engine.WaitWhilePaused()) break;
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
                entries[i].Lut = LutResolver.Resolve(entries[i].Clip, info, Layout.Join(cardPaths[d], entries[i].Clip.Path), Config.Proxy, LutCatalog.Default, plan.CardName);
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
        CopyLocally(plan, log);   // the analysed report, before the proxies (they may take hours)

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
        // 0.5.1: _PROXY mirrors the card's folders. An append keeps the layout of the backup it goes into (an older flat
        // one stays flat — its proxies are never moved) and never takes a name a proxy there already has.
        var tree = plan.Mode == PlanMode.Full ? ProxyTree.Mirror : ProxyPaths.Of(cardPaths);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // R6 (plan 10): a resume of generation G takes only the names its earlier generations recorded — a proxy under the
        // name a clip of G gets is that clip's own, finished by the run that ended (kept below, never rendered as "_2").
        if (plan.Resuming) usedNames.UnionWith(ProxyPaths.Recorded(cardPaths, plan.Generation));
        else if (plan.Mode != PlanMode.Full) usedNames.UnionWith(ProxyPaths.Existing(cardPaths));
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled || !Engine.WaitWhilePaused()) break;
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
            if (LighterSource.Note(clip, info, Config.Proxy, entries[i].Lut) is string lighter)
            {
                // 0.5.1: the proxy would be no lighter than the original — none, and no failure (the thumbnails are made).
                entries[i].ProxyStatus = ProxyStatus.Skipped;
                entries[i].ProxyNote = lighter;
                continue;
            }
            var (rel, name) = ProxyPaths.Pick(clip, tree, usedNames);
            // R6 (plan 10): resuming, the proxy the run that ended finished (a complete file under its final name, not a
            // part) is kept as it is — not rendered again, not copied over; the fill-in read verifies its clip's copy.
            int? kept = plan.Resuming ? ProxyPaths.Finished(cardPaths, rel, prefer: d) : null;
            int from = kept ?? d;
            var sw = Stopwatch.StartNew();
            try
            {
                if (kept != null)
                {
                    entries[i].ProxyNote = entries[i].ProxyNote is string earlier ? $"{earlier}; {Msg.ProxyKeptOnResume}" : Msg.ProxyKeptOnResume;
                    log?.Invoke($"proxy {clip.Name}: kept from the interrupted run ({rel})");
                }
                else
                {
                    // Plan 10: the primary's copy is verified by this read (mode A: ffmpeg reads through the tap; mode B: read
                    // and verified before the SDK); spec 7: the decoder's words are the clip's content verdict.
                    var content = new ContentWatch();
                    using (var lease = verifying?.Prepare(clip, d))
                        renderer.Render(clip, cardPaths[d], info, entries[i].Lut, Layout.Join(cardPaths[d], rel),
                            f => Update(s => s with { ClipFraction = f }), () => IsCancelled, lease?.Url, content);
                    entries[i].Content = content.Verdict(info);
                    if (entries[i].Content!.IsWarn) entries[i].Content!.Jpeg = renderer.Snapshot(clip, cardPaths[d], info, entries[i].Content!.Frame ?? 0);
                }
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                // Spec 3.4: relinking goes by name - a proxy that had to be renamed must be attached by hand.
                if (name != clip.Name) entries[i].ProxyNote = entries[i].ProxyNote is string earlier ? $"{earlier}; {Msg.ProxyRenamed(name)}" : Msg.ProxyRenamed(name);
                // A custom LUT gone since the start: the proxy exists, but without the look — never silently.
                if (kept == null && renderer.LutNote(entries[i].Lut) is string lutNote)
                {
                    entries[i].ProxyNote = entries[i].ProxyNote is string before ? $"{before}; {lutNote}" : lutNote;
                    log?.Invoke($"proxy {clip.Name}: {lutNote}");
                }
                if (Config.Proxy.CopyToAllDestinations)
                {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (int o = 0; o < cardPaths.Count; o++)
                    {
                        if (o == from) continue;
                        // A kept proxy already copied there by the run that ended: never copied over.
                        if (kept != null && ProxyPaths.Finished([cardPaths[o]], rel, prefer: 0) != null) continue;
                        try { ProxyCopy.CopyFile(Layout.Join(cardPaths[from], rel), Layout.Join(cardPaths[o], rel), replace: plan.Mode == PlanMode.Full); }
                        catch (Exception e)
                        {
                            var note = $"kopie na {cardPaths[o]} selhala: {e.Message}";
                            entries[i].ProxyNote = entries[i].ProxyNote is string prev ? $"{prev}; {note}" : note;
                            log?.Invoke($"proxy {clip.Name}: copy to {cardPaths[o]} failed: {e.Message}");
                        }
                    }
                }
                // A kept proxy the run that ended already copied into the user's folder is not copied there again ("_2").
                if (Config.Proxy.ExtraFolder is string extra
                    && !(kept != null && File.Exists(Layout.Join(Layout.CardPath(extra, plan.Date, plan.CardName), rel[(Layout.ProxyDirName.Length + 1)..]))))
                {
                    // The user's proxy folder: also never fails the proxy (the disk may have been disconnected).
                    var (_, error) = ProxyCopy.ToExtraFolder(Layout.Join(cardPaths[from], rel), extra, plan.Date, plan.CardName,
                        rel[(Layout.ProxyDirName.Length + 1)..]);   // the same folders as in _PROXY
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
                    entries[i].Thumbnails = TryThumbnails(renderer, proxyClip, cardPaths[from], info, lut: null, () => IsCancelled, log);
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
        var luts = new Dictionary<Clip, LutUse?>(ReferenceEqualityComparer.Instance);
        foreach (var e in entries)
        {
            if (e.Info != null) infos[e.Clip] = e.Info;
            luts[e.Clip] = e.Lut;
        }
        long proxy = SpaceEstimator.ProxyBytes(entries.Select(e => e.Clip), Config.Proxy,
            c => infos.ContainsKey(c) && renderer.UnsupportedReason(c) == null, c => infos.GetValueOrDefault(c), c => luts.GetValueOrDefault(c));
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

    void CopyLocally(OffloadPlan plan, Action<string>? log, bool day = false) => CopyLocally(plan.CardPaths, log, day ? plan : null);

    /// Task 2026-10-09: the card's report (and with `dayOf` the day report) copied to this computer again — after every
    /// rewrite of the report. Reads the destinations only; a failure is a note on the job and a log line, never its result.
    /// A local folder on the card itself is refused: nothing is ever written to the card.
    void CopyLocally(IReadOnlyList<string> cardPaths, Action<string>? log, OffloadPlan? dayOf = null)
    {
        if (Config.LocalReports is not string root) return;
        try
        {
            if (Inside(root, Config.Source)) throw new IOException(Msg.LocalReportOnCard);
            if (Outputs.LocalReports.MirrorCard(cardPaths, root) is string copied)
            {
                if (copied != localReport) log?.Invoke($"local report copy: {copied}");
                localReport = copied;
            }
            if (dayOf != null && Config.Outputs.DayReport) Outputs.LocalReports.MirrorDay([.. dayOf.DestRoots.Select(r => Path.Combine(r, dayOf.Date))], root);
        }
        catch (Exception e)
        {
            log?.Invoke($"local report copy: {e.Message}");
            var note = Msg.LocalReportFailed(e.Message);
            Update(s => s with { LocalReportNote = note });
        }
    }

    /// `path` is `folder` or inside it (a drive root holds everything on its drive). String work only.
    static bool Inside(string path, string folder)
    {
        try
        {
            const char Sep = (char)92;
            var p = Path.GetFullPath(path).TrimEnd(Sep) + Sep;
            var f = Path.GetFullPath(folder.Length == 2 && folder[1] == ':' ? folder + Sep : folder).TrimEnd(Sep) + Sep;
            return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
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
