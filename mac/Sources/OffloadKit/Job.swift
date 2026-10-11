import Darwin
import Foundation

public struct JobConfig: Codable, Equatable {
    public var source: String
    public var destRoots: [String]
    public var cardName: String?
    public var date: String?
    public var verify = true
    public var chunkMB = 16
    public var buffers = 16
    public var allowSameDevice = false
    /// Probe clips and put thumbnails + metadata into the report.
    public var analyzeClips = true
    public var proxy = ProxySettings()
    public var tools = ExternalTools()
    /// Write the copy into history.json (CLI --no-history turns it off).
    public var recordHistory = true
    /// Where history.json lives; nil = Application Support (tests give a temporary folder).
    public var historyFolder: String?
    /// The project the job was started in (nil = no project): into history and the report.
    public var project: String?
    /// The card folders of an earlier backup to append the card's new files to (`<root>/<date>/<card>` on every
    /// destination); nil = a full copy into `destRoots`.
    public var appendInto: [String]?
    /// Plan 10: verify the copies while the proxies render (settings `verifyInProxy`, the "Verify during proxy render" row
    /// of step 3; CLI --classic-verify turns it off). Off, or without proxies: the classic read-back after the copy.
    public var verifyInProxy = true
    /// Which destination is verified through the proxies: "fastest" (the fastest in the copy) or "first" (settings
    /// `verifyPrimary`; any other value reads as "fastest").
    public var verifyPrimary = "fastest"
    /// CLI --primary N (0-based); nil = by `verifyPrimary`.
    public var primaryDestination: Int?
    /// The day report on every destination after the job (settings `outputs.dayReport`, CLI --no-day-report).
    public var dayReport = true

    public init(source: String, destRoots: [String]) {
        self.source = source
        self.destRoots = destRoots
    }
}

public struct JobStatus {
    public enum Phase: String {
        case waiting, copying, flushing, verifying, reporting, analyzing, proxies, finishingVerification, done, failed, cancelled

        public var label: String {
            switch self {
            case .waiting: return "Waiting"
            case .copying: return "Copying"
            case .flushing: return "Flushing disk caches"
            case .verifying: return "Verifying"
            case .reporting: return "Report"
            case .analyzing: return "Analysing clips"
            case .proxies: return "Rendering proxies"
            case .finishingVerification: return "Finishing verification"
            case .done: return "Done"
            case .failed: return "Error"
            case .cancelled: return "Cancelled"
            }
        }

        public var isFinished: Bool { self == .done || self == .failed || self == .cancelled }
    }

    public var phase: Phase = .waiting
    public var copy: ProgressSnapshot?
    public var clipIndex = 0
    public var clipCount = 0
    public var clipName = ""
    public var clipFraction = 0.0
    public var proxiesDone = 0
    public var proxiesFailed = 0
    /// Everything is written (and flushed) on every destination: the card may be taken out ("Safe to eject", Windows
    /// R5) — also while the verification still runs or when it is off. Never green on its own.
    public var safeToEject = false
    /// Every file verified on every destination and the result written everywhere ("Safe to format", R5); for an
    /// append or a repair the whole backup (BackupState). The only state that is green, chimes and exits 0.
    public var safeToFormat = false
    /// The first warning (orange, "needs attention"): no success chime, no green.
    public var warning: String?
    public var copySucceeded: Bool?
    public var error: String?
    /// Which check stopped the job (failed before anything was written), nil otherwise.
    public var errorCode: PreflightCode?
    /// A grey line of a finished job that is not a warning ("Nothing new — the backup is complete.").
    public var note: String?
    /// Pause was pressed and Resume not yet (only while the job runs) — it may still be finishing its step.
    public var paused = false
    /// …and the job really stands: the file or clip in progress is finished and nothing works.
    public var pauseHeld = false
    /// Paused, not standing yet: what it still finishes ("file A001C003.mov", "files a.mov, b.wav", "clip A003",
    /// "the proxy of clip A003") — "Pausing (finishing …)"; nil when unknown or not pausing.
    public var pauseStep: String?
    /// How long the job has stood paused so far (from the end of the step it finished to Resume).
    public var pausedTotal: TimeInterval = 0
    /// Plan 10: this job verifies its copies during the proxy render (stage 2), not by a read-back after the copy.
    public var verifyInProxy = false
    /// Stage 2's verification: bytes verified so far and to verify, summed over the destinations.
    public var verifyDone: Int64 = 0
    public var verifyTotal: Int64 = 0
    /// Task 70: clips whose decoder reported a defect (the copy matches the card) — "Safe to format" waits for a look.
    public var contentWarnings = 0

    public init() {}

    mutating func addWarning(_ w: String) { if warning == nil { warning = w } }

    /// A warning that no longer holds (a history record written later) goes.
    mutating func removeWarning(_ w: String) { if warning == w { warning = nil } }
}

/// One card: copy → verify → report → clip analysis → optional proxies. Used by both the CLI and the app.
public final class OffloadJob {
    public let config: JobConfig
    public let engine: OffloadEngine
    private var _plan: OffloadPlan?
    private var _report: OffloadReport?
    private var state = JobStatus()
    private var cancelled = false
    private var started = false
    private let lock = NSLock()
    /// R6: the history entry a full copy wrote when it started ("unfinished"); finished or removed after the copy.
    private var startEntry: String?
    /// Stage 2's thread as a worker of the pause gate (it waits between clips), nil outside stage 2.
    private var postWorker: PauseGate.Worker?
    /// Ruling I4: the card folders' lock of a copy whose reports say verifyPending — taken before they are written (stage
    /// 1), let go once the verification result is written (stage 2, another thread).
    private var verifyLock: FolderLocks?
    /// Stage 2's verification while it runs.
    private var session: VerifySession?
    /// The paused time when the copy ended: what stage 2's verification stood is counted from it.
    private var pausedAtCopyEnd: TimeInterval = 0
    /// Tests only (internal): verify in stage 2 whatever the proxies (task 67 — without ffmpeg); nil = the rule.
    var deferVerifyOverride: Bool?
    /// Tests only (internal): called before the verification result is written into each card folder.
    var beforeCommit: ((String) -> Void)?
    /// Tests only (internal): the cache check of a destination (nil = CacheProbe.problem).
    var cacheCheck: ((String) -> String?)?
    /// Tests only (internal): called when stage 2's verification starts, with its session.
    var onVerifySession: ((VerifySession) -> Void)?

    /// Set on a worker thread, read by the app's main thread — always under the lock.
    public var plan: OffloadPlan? { lock.lock(); defer { lock.unlock() }; return _plan }
    public var report: OffloadReport? { lock.lock(); defer { lock.unlock() }; return _report }

    public convenience init(config: JobConfig) {
        self.init(config: config, options: OffloadJob.engineOptions(config))
    }

    /// The built-in LUTs the job chooses from (tests pass the repo's shared/luts).
    let luts: LutCatalog

    /// Tests pass their own engine options (allowSourceVolume, beforeFileCopy …) and LUT catalog.
    init(config: JobConfig, options: OffloadOptions, luts: LutCatalog = .shared) {
        self.config = config
        self.luts = luts
        engine = OffloadEngine(options: options)
    }

    static func engineOptions(_ config: JobConfig) -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = config.chunkMB << 20
        o.bufferCount = config.buffers
        o.verify = config.verify
        o.allowSameDevice = config.allowSameDevice
        return o
    }

    private func setReport(_ r: OffloadReport) {
        lock.lock(); _report = r; lock.unlock()
    }

    @discardableResult
    public func prepare() throws -> OffloadPlan {
        if let p = plan { return p }
        if let into = config.appendInto {
            // Shown before the run (CLI, GUI); the run plans again under the card folders' lock.
            let ap = try AppendPlanner.plan(config.source, cardPaths: into, blockSize: engine.options.chunkSize)
            if ap.newFiles.isEmpty { throw OffloadError.preflight(Msg.nothingNew, code: .nothingNew) }
            var p = OffloadPlan.from(ap)
            if p.project == nil { p.project = config.project }
            lock.lock(); _plan = p; lock.unlock()
            return p
        }
        let p = try engine.prepare(source: config.source, destRoots: config.destRoots, cardName: config.cardName, date: config.date,
                                   extra: spaceExtra)
        lock.lock(); _plan = p; lock.unlock()
        for w in engine.spaceWarnings { update { $0.addWarning(w) } }
        return p
    }

    /// Proxies (estimated from the clip names and sizes) and the report on top of the card (spec 2.5, R10).
    private func spaceExtra(_ scan: SourceScan) -> SpaceExtra {
        SpaceEstimator.extra(ClipDetector.detect(scan.files), config.proxy, config.tools.resolved())
    }

    public func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
        engine.cancel()
    }

    public var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return cancelled }

    /// "Pause" (user requirement 2026-10-09, Windows 0.8.0): the step in progress finishes — the file being copied (on
    /// every destination) or read back, the clip being analysed or rendered — then the job stands: stage 1 before its
    /// next file, stage 2 before its next clip and before the PDF — never in the middle of a file, a clip, a report or
    /// an ffmpeg run, and never holding the history lock (an append's folder lock stays held through stage 1). Resume
    /// goes on with the next file or clip. Cancel while pausing or paused ends the job as a cancel does. The GUI only;
    /// the CLI has no pause.
    public func pause() { engine.pauses.pause() }

    public func resume() { engine.pauses.resume() }

    public var isPaused: Bool { engine.pauses.isPaused }

    public func status() -> JobStatus {
        lock.lock()
        var s = state
        let wasStarted = started
        let wasCancelled = cancelled
        lock.unlock()
        if s.phase == .copying || s.phase == .verifying || s.phase == .flushing || s.phase == .reporting {
            let snap = engine.snapshot()
            s.copy = snap
            switch snap.phase {
            case .copy: s.phase = .copying
            case .flush: s.phase = .flushing
            case .verify: s.phase = .verifying
            case .report, .done: s.phase = .reporting
            }
        }
        // Cancelled while still queued: it never runs, so say so instead of "Waiting" forever.
        if s.phase == .waiting && !wasStarted && wasCancelled { s.phase = .cancelled }
        lock.lock(); let verifying = session; lock.unlock()
        if let verifying {
            s.verifyDone = verifying.verifiedBytes.reduce(0, +)
            s.verifyTotal = verifying.toVerify.reduce(0, +)
        }
        let gate = engine.pauses
        let running = s.phase != .waiting && !s.phase.isFinished
        s.paused = running && gate.isPaused
        s.pauseHeld = s.paused && gate.isHeld
        s.pausedTotal = gate.pausedTotal
        if s.paused && !s.pauseHeld {
            lock.lock(); let clipDone = postWorker?.isHolding == true; lock.unlock()
            s.pauseStep = OffloadJob.finishing(s, clipDone: clipDone, verifying: verifying?.currentFiles ?? [])
        }
        return s
    }

    /// Paused, not standing yet: what the job still finishes — the file the copy is on, the files the read-backs read,
    /// the clip stage 2 analyses or renders (`clipDone`: its thread waits already); nil when none is known.
    static func finishing(_ s: JobStatus, clipDone: Bool, verifying: [String] = []) -> String? {
        func files(_ rel: [String]) -> String? {
            var names: [String] = []
            for r in rel where !r.isEmpty {
                let n = (r as NSString).lastPathComponent
                if !names.contains(where: { $0.caseInsensitiveCompare(n) == .orderedSame }) { names.append(n) }
            }
            return names.isEmpty ? nil : names.count == 1 ? "file " + names[0] : "files " + names.joined(separator: ", ")
        }
        switch s.phase {
        case .copying: return files([s.copy?.currentFile ?? ""])
        case .verifying: return files(s.copy?.verifyingFiles ?? [])
        case .analyzing where !clipDone && !s.clipName.isEmpty: return "clip " + s.clipName
        case .proxies where !clipDone && !s.clipName.isEmpty: return "the proxy of clip " + s.clipName
        case .finishingVerification: return files(verifying)
        default: return nil
        }
    }

    private func update(_ f: (inout JobStatus) -> Void) {
        lock.lock()
        f(&state)
        let ended = state.phase.isFinished
        lock.unlock()
        if ended { engine.pauses.end() }   // the paused time stops with the job, also when it ended paused
    }

    /// Runs the whole job synchronously. Never throws: the outcome is in `status()` and the returned report.
    @discardableResult
    public func run(log: ((String) -> Void)? = nil) -> OffloadReport? {
        if runCopy() { runPost(log: log) }
        writeDayReports(log: log)
        return report
    }

    /// The day report on each destination of the job (FORMATY „Denní report“): after the whole job — also an append and a
    /// cancelled or failed one, whenever its card folders exist. Waits at most 5 s for a busy day, then skips it (log);
    /// never fails the job. Returns the HTML files written. Off with `config.dayReport == false`.
    @discardableResult
    public func writeDayReports(log: ((String) -> Void)? = nil) -> [String] {
        guard config.dayReport, let plan else { return [] }
        let written = DayReportService.afterJob(cardPaths: plan.cardPaths, pdf: DayReportService.jobPDF, log: log)
        lock.lock(); dayReports = written; lock.unlock()
        return written
    }

    private var dayReports: [String] = []
    /// The day reports the last `writeDayReports` wrote (HTML paths).
    public var writtenDayReports: [String] {
        lock.lock(); defer { lock.unlock() }
        return dayReports
    }

    /// Stage 1 — card → disks + verification. Returns true when stage 2 should follow.
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    @discardableResult
    public func runCopy() -> Bool {
        lock.lock()
        started = true
        let cancelledBeforeStart = cancelled
        if cancelledBeforeStart { state.phase = .cancelled }
        lock.unlock()
        // Cancelled before it began: stay Cancelled, never prepare or copy.
        if cancelledBeforeStart {
            engine.pauses.end()
            return false
        }
        let activity = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiated, .idleSystemSleepDisabled], reason: "Offloading camera card")
        defer { ProcessInfo.processInfo.endActivity(activity) }
        if let into = config.appendInto { return runAppend(into) }
        do {
            var plan = try prepare()
            if plan.project == nil && config.project != nil {
                plan.project = config.project
                lock.lock(); _plan = plan; lock.unlock()
            }
            update { $0.phase = .copying }
            let historyFolder = HistoryRecorder.folder(config)
            // The card volume as it is now (history's key); a folder has none. Read before the copy, off the main thread.
            let volume = historyFolder == nil ? nil : CardVolume.of(plan.source)
            // Plan 10 (R11): the read-back waits for stage 2, where the proxies read the copies. Ruling I4: the new card
            // folders are locked before their reports say verifyPending, until the verification result is written.
            let deferring = verifiesInProxy(plan)
            engine.options.deferVerify = deferring
            if deferring { engine.beforePendingReports = { [weak self] in self?.takeVerifyLock(plan.cardPaths) } }
            update { $0.verifyInProxy = deferring }
            // R6: history knows the copy from its start — one the app never finishes can be resumed from its card.
            if let historyFolder, plan.mode == .full { recordStart(plan, volume: volume, folder: historyFolder) }
            let report: OffloadReport
            do {
                report = try engine.run(plan)
            } catch {
                releaseVerifyLock()
                throw error
            }
            pausedAtCopyEnd = engine.pauses.pausedTotal
            // A read-back through the macOS cache (spec D.23.1): never a silent green.
            if let note = report.verifyNotes?.first { update { $0.addWarning(note) } }
            // The record of the copy (report, hashes, MHL) on this Mac too, right after verification.
            LocalReports.sync(date: plan.date, cardName: plan.cardName, cardPaths: plan.cardPaths)
            setReport(report)
            update {
                $0.safeToEject = report.success
                $0.safeToFormat = report.safeToFormat
                $0.copySucceeded = report.success
                $0.copy = engine.snapshot()
            }
            if isCancelled {
                if report.verifyPending == true, let historyFolder {
                    // Cancelled once the reports said verifyPending: nothing was verified, the generation is not committed
                    // — history keeps it "unfinished", "Finish interrupted backup" completes it.
                    recordFinished(plan, report: report, volume: volume, card: plan.scan, folder: historyFolder)
                } else {
                    forgetStart()   // a cancelled copy is not recorded in history
                }
                releaseVerifyLock()
                update { $0.phase = .cancelled }
                return false
            }
            if let historyFolder { recordFinished(plan, report: report, volume: volume, card: plan.scan, folder: historyFolder) }
            if engine.cardChanged {
                // The files copied before the pause are verified now (no proxies follow), then the job fails.
                if report.verifyPending == true { setReport(verifyNow(plan, report)) }
                return endCardChanged(self.report ?? report, plan: plan)
            }
            if report.verifyPending == true && !(config.analyzeClips || config.proxy.enabled) {
                update { $0.phase = .analyzing }
                return true   // only the verification (tests: deferVerifyOverride)
            }
            guard config.analyzeClips || config.proxy.enabled else {
                writePDFReports(report, plan: plan)
                update { $0.phase = .done }
                return false
            }
            update { $0.phase = .analyzing }
            return true
        } catch {
            fail(error)
            return false
        }
    }

    /// Ruling I1: another card (or none) after Resume — the files copied before the pause are in the report and in
    /// history; the rest was never read. The job fails; no stage 2 (the PDF as without it). An append with the original
    /// card completes the backup.
    private func endCardChanged(_ report: OffloadReport, plan: OffloadPlan) -> Bool {
        writePDFReports(report, plan: plan)
        update {
            $0.phase = .failed
            $0.error = Msg.cardChangedWhilePaused
        }
        return false
    }

    private func fail(_ error: Error) {
        update {
            $0.phase = .failed
            $0.error = "\(error)"
            $0.errorCode = (error as? OffloadError)?.code
        }
    }

    /// An append (FORMATY „Doplnění“): the card folders' lock first (busy = refused at once, nothing written), the plan
    /// under it, then only the new files into the same card folders as generation N. The lock is held for stage 1.
    private func runAppend(_ into: [String]) -> Bool {
        let held: FolderLocks
        do {
            held = try FolderLocks.acquire(into)
        } catch {
            fail(error)
            return false
        }
        // Released when stage 1 ends — unless its reports say verifyPending: then stage 2 holds it until the result is
        // written (ruling I4).
        var keepHeld = false
        defer { if !keepHeld { held.release() } }
        let ap: AppendPlan
        do {
            ap = try AppendPlanner.plan(config.source, cardPaths: into, blockSize: engine.options.chunkSize, isCancelled: { self.isCancelled })
        } catch is CancellationError {
            update { $0.phase = .cancelled }
            return false
        } catch {
            fail(error)
            return false
        }
        let historyFolder = HistoryRecorder.folder(config)
        let volume = historyFolder == nil ? nil : CardVolume.of(ap.source)
        if ap.newFiles.isEmpty {
            // Not an error: the backup already has every file of the card. A hash list whose write failed in an earlier
            // append gets its missing lines now; nothing copied does not make an earlier copy verified.
            for p in ap.cardPaths {
                do { _ = try OffloadReport.healHashList(cardPath: p, cardName: ap.cardName) } catch {
                    update { $0.addWarning(Msg.hashListNotHealed(p, "\(error)")) }
                }
            }
            let whole = BackupState.of(ap.cardPaths)
            if whole.failed > 0 { update { $0.addWarning(Msg.backupStillFailed(whole.failed)) } }
            if whole.unverified > 0 { update { $0.addWarning(Msg.unverifiedEarlier(whole.unverified)) } }
            if let historyFolder { recordRefresh(ap, whole: whole, volume: volume, folder: historyFolder) }
            update {
                $0.note = Msg.nothingNew
                $0.safeToEject = whole.failed == 0
                $0.safeToFormat = whole.failed == 0 && whole.unverified == 0
                $0.copySucceeded = true
                $0.phase = .done
            }
            return false
        }
        var plan = OffloadPlan.from(ap)
        if plan.project == nil { plan.project = config.project }
        lock.lock(); _plan = plan; lock.unlock()
        do {
            try engine.checkSpace(plan.cardPaths, card: SpaceEstimator.cardBytes(plan.scan.totalBytes, files: plan.scan.files.count),
                                  extra: spaceExtra(plan.scan))
        } catch {
            fail(error)
            return false
        }
        for w in engine.spaceWarnings { update { $0.addWarning(w) } }
        if plan.unverifiedEarlier > 0 { update { $0.addWarning(Msg.unverifiedEarlier(plan.unverifiedEarlier)) } }
        update { $0.phase = .copying }
        let deferring = verifiesInProxy(plan)
        engine.options.deferVerify = deferring
        update { $0.verifyInProxy = deferring }
        let report: OffloadReport
        do {
            report = try engine.run(plan)
        } catch {
            fail(error)
            return false
        }
        pausedAtCopyEnd = engine.pauses.pausedTotal
        if let note = report.verifyNotes?.first { update { $0.addWarning(note) } }
        if report.verifyPending == true && !isCancelled {
            keepHeld = true
            lock.lock(); verifyLock = held; lock.unlock()
        }
        LocalReports.sync(date: plan.date, cardName: plan.cardName, cardPaths: plan.cardPaths)
        setReport(report)
        // The whole backup as its card folders' reports say it now (the one definition of "verified").
        let whole = BackupState.of(plan.cardPaths)
        update {
            $0.safeToEject = report.success && whole.failed == 0
            $0.safeToFormat = report.safeToFormat && whole.failed == 0 && whole.unverified == 0 && plan.unverifiedEarlier == 0
            $0.copySucceeded = report.success
            $0.copy = engine.snapshot()
        }
        if isCancelled {
            if report.verifyPending == true, let historyFolder {
                recordFinished(plan, report: report, volume: volume, card: ap.fullScan, folder: historyFolder)
            }
            update { $0.phase = .cancelled }
            return false
        }
        if let historyFolder { recordFinished(plan, report: report, volume: volume, card: ap.fullScan, folder: historyFolder) }
        if engine.cardChanged {
            if report.verifyPending == true { setReport(verifyNow(plan, report)) }
            return endCardChanged(self.report ?? report, plan: plan)
        }
        if report.verifyPending == true && !(config.analyzeClips || config.proxy.enabled) {
            update { $0.phase = .analyzing }
            return true
        }
        guard config.analyzeClips || config.proxy.enabled else {
            writePDFReports(report, plan: plan)
            update { $0.phase = .done }
            return false
        }
        update { $0.phase = .analyzing }
        return true
    }

    /// Stage 2 — clip analysis, report with thumbnails, optional proxies. Reads only from the destinations.
    public func runPost(log: ((String) -> Void)? = nil) {
        guard let plan, let report else { return }
        let activity = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiated, .idleSystemSleepDisabled], reason: "Rendering proxies")
        defer { ProcessInfo.processInfo.endActivity(activity) }
        // Stage 2 joins the pause gate: Pause lets the clip in progress finish, then the job stands before the next.
        let worker = engine.pauses.work()
        lock.lock(); postWorker = worker; lock.unlock()
        defer {
            lock.lock(); postWorker = nil; lock.unlock()
            worker.leave()
        }
        let pending = report.verifyPending == true
        // Plan 10: the copies verify while the clips are analysed and the proxies render (the background readers start
        // now; the primary's clips are read through the tap — task 68).
        if pending { startVerification(plan, report) }
        var final = config.analyzeClips || config.proxy.enabled ? analyzeAndRender(plan: plan, report: report, log: log) : report
        // Spec 7 (task 70): the decoder's content warnings, however the copy was verified — the card then asks for a look
        // before formatting, no chime.
        let defects = final.contentWarnings.count
        if defects > 0 {
            update {
                $0.contentWarnings = defects
                $0.addWarning(VIPMsg.contentWarnings(defects))
            }
        }
        // The verification ends here — also when the analysis or the proxies were cancelled.
        if pending { final = finishVerification(plan, final, log: log) }
        setReport(final)
        _ = waitBetweenSteps()   // not in the middle of the PDF; a cancel while paused still writes it, as before
        update { $0.phase = .reporting }
        writePDFReports(final, plan: plan)
        let finalPhase: JobStatus.Phase = isCancelled ? .cancelled : .done
        update { $0.phase = finalPhase }
    }

    // MARK: - Verification during the proxies (plan 10)

    /// Plan 10 (R11): whether this job verifies with the proxies — verification and proxies on, verify-in-proxy not
    /// switched off, ffmpeg found (a repair always reads back; the Mac has none yet).
    public func verifiesInProxy(_ plan: OffloadPlan) -> Bool {
        guard config.verify, plan.mode != .repair else { return false }
        if let forced = deferVerifyOverride { return forced }
        return config.verifyInProxy && config.proxy.enabled && config.tools.resolved().ffmpeg != nil
    }

    /// Ruling I4 for a full copy: the new card folders' lock, right before their reports say verifyPending (the folders
    /// exist then). Busy cannot really happen (new folders); then the commit takes it or does not commit.
    private func takeVerifyLock(_ cardPaths: [String]) {
        lock.lock(); let have = verifyLock != nil; lock.unlock()
        guard !have, let held = try? FolderLocks.acquire(cardPaths) else { return }
        lock.lock(); verifyLock = held; lock.unlock()
    }

    private func releaseVerifyLock() {
        lock.lock(); let held = verifyLock; verifyLock = nil; lock.unlock()
        held?.release()
    }

    /// The preferred primary destination: CLI --primary, else "first" = destination 1, else the fastest (nil).
    var preferredPrimary: Int? {
        if let p = config.primaryDestination { return p }
        return config.verifyPrimary == "first" ? 0 : nil
    }

    /// The session of a copy whose reports say verifyPending: every destination's cache check first (spec D.23.1), then the
    /// background reads start.
    private func startVerification(_ plan: OffloadPlan, _ report: OffloadReport) {
        let check = cacheCheck ?? CacheProbe.problem
        let why: [String?] = plan.cardPaths.map { config.verifyInProxy || deferVerifyOverride == true ? check($0) : VIPMsg.classicSetting }
        let source = plan.source
        let files = plan.scan.files
        let identity = engine.cardAtStart
        let blockSize = engine.options.chunkSize
        let s = VerifySession(report: report, cardPaths: plan.cardPaths, blocks: engine.fileBlocks, blockSize: blockSize,
                              rates: engine.writeRates, preferred: preferredPrimary, classicWhy: why, gate: engine.pauses,
                              cardBlock: { file, block in
                                  // Only while the copied card is still at the source (a folder has no card to lose).
                                  if let identity, OffloadEngine.cardFingerprint(source) != identity { return nil }
                                  return VerifySession.readCardBlock(source + "/" + files[file].relPath, block: block, blockSize: blockSize)
                              },
                              diskName: { d in HistoryText.diskName(plan.destRoots[d]) })
        lock.lock(); session = s; lock.unlock()
        onVerifySession?(s)
        s.startBackground()
    }

    /// Verifies the copies of a pending report right away (no proxies follow — the card changed while paused).
    private func verifyNow(_ plan: OffloadPlan, _ report: OffloadReport) -> OffloadReport {
        startVerification(plan, report)
        return finishVerification(plan, report, log: nil)
    }

    /// The end of the verification (spec 6; Windows FinishVerification): the readers end, the report gets the verdicts and
    /// is committed into every card folder while the job holds their lock (ruling I4) — ASC MHL generation and chain, then
    /// reports, then hash lists; history learns it, the lock goes, and "Safe to format" is decided from the folders
    /// (BackupState — the one definition of "verified"). Cancelled: nothing is committed — the reports stay verifyPending
    /// (never verified; "Finish interrupted backup" completes them) and history keeps the backup "unfinished". Never throws.
    private func finishVerification(_ plan: OffloadPlan, _ report: OffloadReport, log: ((String) -> Void)?) -> OffloadReport {
        lock.lock(); let held = verifyLock; verifyLock = nil; let s = session; lock.unlock()
        defer {
            held?.release()
            s?.close()
            lock.lock(); session = nil; lock.unlock()
        }
        guard let s else { return report }
        update { $0.phase = .finishingVerification }
        lock.lock(); let worker = postWorker; lock.unlock()
        let fin = s.finish(pausing: worker, isCancelled: { self.isCancelled })
        if isCancelled || !fin.verified { return report }
        let paused = max(0, engine.pauses.pausedTotal - pausedAtCopyEnd)
        let final = report.withVerification(results: fin.results, verified: true, primary: s.primary, classicWhy: s.classicWhy,
                                            notes: fin.notes, finished: Date(), verifyPaused: paused)
        for n in fin.notes { log?(n) }
        // Never without the folders' lock (another job may be at them): taken now, or not committed at all.
        var lockNow: FolderLocks?
        if held == nil {
            do { lockNow = try FolderLocks.acquire(plan.cardPaths) } catch {
                update { $0.addWarning(VIPMsg.verifyNotCommitted("\(error)")); $0.safeToFormat = false }
                return report   // nothing written: the folders still say verifyPending, and so does the job's report
            }
        }
        defer { lockNow?.release() }
        let outcome = Verification.commit(final, cardPaths: plan.cardPaths, log: log, beforeCommit: beforeCommit)
        LocalReports.sync(date: plan.date, cardName: plan.cardName, cardPaths: plan.cardPaths)
        if let w = outcome.warning { update { $0.addWarning(w) } }
        let whole = BackupState.of(plan.cardPaths)
        recordVerified(plan, outcome.report, whole: whole, committed: outcome.committed)
        var safe = outcome.committed && outcome.report.safeToFormat && whole.failed == 0 && whole.unverified == 0 && plan.notInBackup == 0
        if plan.mode == .append { safe = safe && plan.unverifiedEarlier == 0 }
        if let first = fin.notes.first { update { $0.addWarning(first) } }
        update {
            $0.safeToFormat = safe
            $0.copySucceeded = $0.copySucceeded != false && outcome.report.success
        }
        // The job's report (its PDF, the app) never looks verified where the result is not written everywhere.
        var mine = outcome.report
        if !outcome.committed { mine.verifyIncomplete = true }
        return mine
    }

    /// History learns the verification (I5): "unfinished" until the result is written into every folder.
    private func recordVerified(_ plan: OffloadPlan, _ report: OffloadReport, whole: BackupState, committed: Bool) {
        guard let folder = HistoryRecorder.folder(config) else { return }
        historyRecord("verification", folder: folder) { store in
            guard let found = HistoryRecorder.entryOf(store.load(), plan.cardPaths) else { return .unchanged }
            return store.update(id: found.id) { existing in
                guard let e = existing else { return nil }
                return (HistoryRecorder.verifiedEntry(e, plan: plan, report: report, whole: whole, committed: committed), nil)
            }
        }
    }

    // MARK: - History

    private func historyRecord(_ what: String, folder: String, _ write: @escaping (HistoryStore) -> HistoryWrite) {
        HistoryRecorder.record(what, folder: folder, log: { NSLog("%@", $0) },
                               delayed: { [weak self] in self?.update { $0.addWarning(Msg.historyDelayed) } },
                               done: { [weak self] ok in
                                   // The warning goes once nothing of this (or any) job waits any more.
                                   let settled = HistoryRecorder.waiting == 0
                                   self?.update {
                                       if settled || !ok { $0.removeWarning(Msg.historyDelayed) }
                                       if !ok { $0.addWarning(Msg.historyNotWritten) }
                                   }
                               }, write)
    }

    private func recordStart(_ plan: OffloadPlan, volume: CardVolume?, folder: String) {
        let now = Date()
        let id = HistoryStore.newId(finished: now)
        let entry = HistoryRecorder.startEntry(id: id, plan: plan, config: config, volume: volume, started: now)
        lock.lock(); startEntry = id; lock.unlock()
        historyRecord("start", folder: folder) { $0.update(id: id) { _ in (entry, nil) } }
    }

    /// The start entry of a copy that was cancelled goes again.
    private func forgetStart() {
        lock.lock()
        let id = startEntry
        startEntry = nil
        lock.unlock()
        guard let id, let folder = HistoryRecorder.folder(config) else { return }
        historyRecord("removal", folder: folder) { $0.remove(id: id) }
    }

    /// "Nothing new": the entry of that backup learns the card as it is now — no new generation.
    private func recordRefresh(_ ap: AppendPlan, whole: BackupState, volume: CardVolume?, folder: String) {
        historyRecord("refresh", folder: folder) { store in
            guard let found = HistoryRecorder.entryOf(store.load(), ap.cardPaths) else { return .unchanged }
            return store.update(id: found.id) { existing in
                guard var e = existing else { return nil }
                e.success = whole.failed == 0
                e.failedFiles = whole.failed == 0 ? nil : whole.failed
                e.verified = whole.failed == 0 && whole.unverified == 0
                return HistoryRecorder.withCard(e, volume: volume, card: ap.fullScan, storedFiles: store.readFileList(e))
            }
        }
    }

    /// The finished stage 1 (with failed files too) into history.json — never fails the job. `card`: the whole card
    /// as scanned at the start (an append: every file, not just the new ones).
    private func recordFinished(_ plan: OffloadPlan, report: OffloadReport, volume: CardVolume?, card: SourceScan, folder: String) {
        lock.lock()
        let start = startEntry
        startEntry = nil
        lock.unlock()
        // As the card folders' reports say it now — the one definition of "verified" (read before the lock is taken).
        let whole = BackupState.of(plan.cardPaths)
        let config = self.config
        historyRecord(plan.mode == .full ? "copy" : "append", folder: folder) { store in
            let entries = store.load()
            let id = (plan.mode == .full ? start : HistoryRecorder.entryOf(entries, plan.cardPaths)?.id)
                ?? HistoryStore.newId(finished: report.finished)
            return store.update(id: id) { existing in
                let stored = existing.flatMap { store.readFileList($0) }
                return HistoryRecorder.finishedEntry(existing, id: id, plan: plan, report: report, config: config, volume: volume,
                                                     card: card, whole: whole, storedFiles: stored)
            }
        }
    }

    /// Stage 2, between clips (and before the PDF): waits while paused; false when the job was cancelled meanwhile.
    private func waitBetweenSteps() -> Bool {
        lock.lock(); let w = postWorker; lock.unlock()
        let cancelled = { self.isCancelled }
        return w?.waitWhilePaused(isCancelled: cancelled) ?? engine.pauses.waitWhilePaused(isCancelled: cancelled)
    }

    // MARK: - Clips

    private func analyzeAndRender(plan: OffloadPlan, report input: OffloadReport, log: ((String) -> Void)?) -> OffloadReport {
        var report = input
        let tools = config.tools.resolved()
        let renderer = ProxyRenderer(settings: config.proxy, tools: tools)
        renderer.log = log   // software retries and unkillable ffmpeg processes go to the job's log
        let clips = ClipDetector.detect(plan.scan.files)
        guard !clips.isEmpty else { return report }

        let verified = report.verified
        let fileIndex = Dictionary(uniqueKeysWithValues: report.files.enumerated().map { ($0.element.path, $0.offset) })
        lock.lock(); let verifying = session; lock.unlock()
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card. With
        /// verify-in-proxy the primary first: its copy is what the proxies verify.
        func goodDest(_ clip: Clip) -> Int? {
            func good(_ d: Int) -> Bool {
                clip.segments.allSatisfy { seg in
                    fileIndex[seg].map { OffloadReport.isGood(report.files[$0].destinations[d], verified: verified) } ?? false
                }
            }
            if let p = verifying?.primary, report.destinations.indices.contains(p), good(p) { return p }
            return report.destinations.indices.first(where: good)
        }

        var entries = clips.map(ClipEntry.init)
        update {
            $0.phase = .analyzing
            $0.clipCount = entries.count
        }
        for i in entries.indices {
            // A pause waits between clips: the clip being probed finishes first (no ffmpeg is ever suspended).
            if isCancelled || !waitBetweenSteps() { break }
            let clip = entries[i].clip
            update {
                $0.clipIndex = i
                $0.clipName = clip.name
                $0.clipFraction = 0
            }
            guard let d = goodDest(clip) else {
                entries[i].probeError = Msg.noVerifiedCopy
                continue
            }
            if let reason = renderer.unsupportedReason(clip), clip.format != .video {
                entries[i].probeError = reason
                continue
            }
            do {
                var info: ClipInfo
                do {
                    info = try renderer.probe(clip, cardPath: plan.cardPaths[d])
                } catch where clip.format == .video && (clip.path as NSString).pathExtension.lowercased() == "mxf" {
                    info = ClipInfo()   // ffprobe failed or timed out on an MXF: let the ARRI tool try below
                    log?("probe \(clip.name): ffprobe: \(error) — trying art-cmd")
                }
                if clip.format == .video && info.width == nil {
                    if let (arri, arriInfo) = renderer.reclassifyUndecodableMXF(clip, cardPath: plan.cardPaths[d]) {
                        entries[i].clip = arri
                        info = arriInfo
                    } else {
                        info.reel = ReelResolver.resolve(clip, info: info, cardName: plan.cardName)
                        entries[i].info = info
                        entries[i].probeError = "codec not supported by ffmpeg (e.g. Sony X-OCN) — copied and verified, no proxy"
                        continue
                    }
                }
                // Task 54: the camera's tag, else the clip name, else the card folder — into the report and the proxy.
                info.reel = ReelResolver.resolve(entries[i].clip, info: info, cardName: plan.cardName)
                entries[i].info = info
                // The Sony XML is read from the destination: it sits next to the clip on the card, so it was copied too.
                entries[i].lut = LutResolver.resolve(entries[i].clip, info: info,
                                                     clipFullPath: plan.cardPaths[d] + "/" + entries[i].clip.path,
                                                     settings: config.proxy, catalog: luts)
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if entries[i].clip.format != .r3d && entries[i].clip.format != .arriraw {
                    entries[i].thumbnails = tryThumbnails(renderer, entries[i].clip, cardPath: plan.cardPaths[d], info: info,
                                                          lut: entries[i].lut, log: log)
                }
            } catch {
                entries[i].probeError = "\(error)"
                log?("probe \(clip.name): \(error)")
            }
        }
        report.clips = entries
        report.proxy = config.proxy
        rewriteReports(report, plan: plan)

        guard config.proxy.enabled, !isCancelled else { return report }
        // Spec 2.5: the need again from the real clip lengths — proxies that do not fit are not rendered (the copy stays
        // verified; the job needs attention).
        if let problem = proxySpaceProblem(entries, plan: plan, tools: tools) {
            update { $0.addWarning(problem) }
            log?("proxies skipped: \(problem)")
            for i in entries.indices where entries[i].proxyStatus == .none {
                entries[i].proxyStatus = .skipped
                entries[i].proxyNote = problem
            }
            report.clips = entries
            rewriteReports(report, plan: plan)
            return report
        }
        update {
            $0.phase = .proxies
            $0.clipCount = entries.count
        }
        // Task 54 (Windows 0.5.1): _PROXY mirrors the card's folders. An append keeps the layout of the backup it goes into
        // (an older flat one stays flat — its proxies are never moved) and writes next to earlier proxies: every proxy
        // already in _PROXY on any destination is taken. (A full copy has new card folders; a name taken there meanwhile
        // still fails the render instead of replacing it.)
        let tree: ProxyTree = plan.mode == .full ? .mirror : ProxyPaths.of(plan.cardPaths)
        var usedNames = plan.mode == .full ? Set<String>() : ProxyPaths.existing(plan.cardPaths)
        for i in entries.indices {
            // A pause waits between clips: the proxy being rendered finishes first.
            if isCancelled || !waitBetweenSteps() { break }
            let clip = entries[i].clip  // may have been reclassified during analysis (MXF → ARRIRAW)
            update {
                $0.clipIndex = i
                $0.clipName = clip.name
                $0.clipFraction = 0
            }
            if clip.isCameraProxy && config.proxy.skipCameraProxies {
                entries[i].proxyStatus = .skipped
                continue
            }
            // Task 69: the proxy the interrupted run finished stays — never rendered again, never "_2", nothing replaced.
            if let kept = plan.keptProxies[clip.path.lowercased()],
               let at = plan.cardPaths.firstIndex(where: { FileManager.default.fileExists(atPath: $0 + "/" + kept) }) {
                entries[i].proxyStatus = .done
                entries[i].proxyPath = kept
                entries[i].proxyNote = VIPMsg.proxyKeptOnResume
                if config.proxy.copyToAllDestinations {
                    for (o, other) in plan.cardPaths.enumerated() where o != at && !FileManager.default.fileExists(atPath: other + "/" + kept) {
                        try? ProxyCopy.copyFile(plan.cardPaths[at] + "/" + kept, to: other + "/" + kept, replace: false)
                    }
                }
                update { $0.proxiesDone += 1 }
                continue
            }
            if let reason = renderer.unsupportedReason(clip) ?? (entries[i].info != nil && entries[i].info?.width == nil ? entries[i].probeError : nil) {
                entries[i].proxyStatus = .unsupported
                entries[i].proxyNote = reason
                continue
            }
            guard let d = goodDest(clip), let info = entries[i].info else {
                entries[i].proxyStatus = .failed
                entries[i].proxyNote = entries[i].probeError ?? Msg.noVerifiedCopy
                update { $0.proxiesFailed += 1 }
                continue
            }
            let (rel, name) = ProxyPaths.pick(clip, tree: tree, used: &usedNames)

            let t0 = Date()
            do {
                // Verify-in-proxy (task 68): the primary's copy read through the tap (mode A) or read before the SDK (mode B).
                let lease = verifying?.prepare(clip, dest: d)
                defer { lease?.end() }
                // Spec 7 (task 70): the decoder's words while it makes the proxy are the clip's content verdict.
                let content = ContentCheck()
                try renderer.render(clip, cardPath: plan.cardPaths[d], info: info, lut: entries[i].lut,
                                    output: plan.cardPaths[d] + "/" + rel,
                                    progress: { f in self.update { $0.clipFraction = f } },
                                    isCancelled: { self.isCancelled },
                                    inputURL: lease?.url, activity: lease?.activity, content: content)
                var verdict = content.verdict(info)
                if verdict.isWarn {
                    verdict.jpeg = renderer.snapshot(clip, cardPath: plan.cardPaths[d], info: info, frame: verdict.frame ?? 0)
                    log?("content \(clip.name): \(ClipContent.kindText(verdict.kind)) at \(verdict.timecode ?? "?") — \(verdict.message ?? "")")
                }
                entries[i].content = verdict
                entries[i].proxyStatus = .done
                entries[i].proxyPath = rel
                // Relinking goes by name — a proxy that had to be renamed must be attached by hand.
                if name != clip.name {
                    entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(Msg.proxyRenamed(name))" } ?? Msg.proxyRenamed(name)
                }
                if config.proxy.copyToAllDestinations {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (o, other) in plan.cardPaths.enumerated() where o != d {
                        do {
                            try ProxyCopy.copyFile(plan.cardPaths[d] + "/" + rel, to: other + "/" + rel, replace: false)
                        } catch {
                            let note = "copy to \(other) failed: \(error)"
                            entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(note)" } ?? note
                            log?("proxy \(clip.name): copy to \(other) failed: \(error)")
                        }
                    }
                }
                if let extra = config.proxy.extraFolder {
                    // The user's proxy folder: also never fails the proxy (the disk may have been disconnected).
                    // The same folders as in _PROXY (task 54).
                    let (_, error) = ProxyCopy.toExtraFolder(plan.cardPaths[d] + "/" + rel, extraFolder: extra, date: plan.date,
                                                             cardName: plan.cardName,
                                                             fileName: String(rel.dropFirst(Layout.proxyDirName.count + 1)))
                    if let error {
                        let note = "copy to \(extra) failed: \(error)"
                        entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(note)" } ?? note
                        log?("proxy \(clip.name): copy to \(extra) failed: \(error)")
                    }
                }
                if entries[i].thumbnails.isEmpty {
                    // RAW formats rendered via an intermediate: take the thumbnails from the finished proxy (it has the LUT).
                    let proxyClip = Clip(name: name, path: rel, format: .video, segments: [rel], bytes: 0, isCameraProxy: false)
                    entries[i].thumbnails = tryThumbnails(renderer, proxyClip, cardPath: plan.cardPaths[d], info: info,
                                                          lut: nil, log: log)
                }
                entries[i].proxySeconds = Date().timeIntervalSince(t0)
                update { $0.proxiesDone += 1 }
            } catch {
                entries[i].proxyStatus = .failed
                entries[i].proxyNote = "\(error)"
                update { $0.proxiesFailed += 1 }
                log?("proxy \(clip.name): \(error)")
            }
        }
        report.clips = entries
        rewriteReports(report, plan: plan)
        return report
    }

    /// Nil when the proxies fit (or the free space cannot be asked); otherwise the warning text.
    private func proxySpaceProblem(_ entries: [ClipEntry], plan: OffloadPlan, tools: ExternalTools) -> String? {
        let infos = Dictionary(entries.map { ($0.clip.path, $0.info) }, uniquingKeysWith: { a, _ in a })
        let bytes = SpaceEstimator.proxyBytes(entries.map(\.clip), config.proxy, renderable: { SpaceEstimator.renderable($0, tools) },
                                              info: { infos[$0.path] ?? nil })
        guard bytes > 0 else { return nil }
        let extra = SpaceExtra(proxy: bytes, report: 0, proxyOnAllDestinations: config.proxy.copyToAllDestinations,
                               proxyFolder: config.proxy.extraFolder)
        let places = SpacePlanner.places(plan.cardPaths, extra: extra, card: 0, label: HistoryText.diskName)
        let free = { (p: String) in self.engine.testFreeSpace?(p) ?? IO.availableBytes(p) }
        let (problems, _) = SpacePlanner.checkPlaces(places, freeOf: free)
        guard let block = problems.first(where: { $0.severity == .blocking }) else { return nil }
        return Msg.proxiesSkippedForSpace(block.label, need: block.need.total, free: block.free)
    }

    /// PDF copy of the report on every destination. Rendered once, then copied; a failure here never fails the offload.
    private func writePDFReports(_ report: OffloadReport, plan: OffloadPlan) {
        let name = OffloadReport.pdfName(report.cardName, report.generationNumber)
        // Only into card folders that are there: one gone with its disk is never made anew.
        let present = plan.cardPaths.filter { FileManager.default.fileExists(atPath: $0) }
        guard let firstFolder = present.first else { return }
        let first = firstFolder + "/" + Layout.reportDirName + "/" + name
        do {
            try PDFReport.write(report, to: first)
            for other in present.dropFirst() {
                try? ProxyCopy.copyFile(first, to: other + "/" + Layout.reportDirName + "/" + name)
            }
        } catch {
            // The HTML report is already on disk; the PDF is a convenience copy.
            fputs("PDF report failed: \(error)\n", stderr)
        }
        LocalReports.sync(date: plan.date, cardName: plan.cardName, cardPaths: plan.cardPaths)
    }

    private func rewriteReports(_ report: OffloadReport, plan: OffloadPlan) {
        for path in plan.cardPaths where FileManager.default.fileExists(atPath: path) {
            try? report.write(cardPath: path, includeHashRecords: false)
        }
        LocalReports.sync(date: plan.date, cardName: plan.cardName, cardPaths: plan.cardPaths)
    }

    private func tryThumbnails(_ renderer: ProxyRenderer, _ clip: Clip, cardPath: String, info: ClipInfo,
                               lut: LutUse?, log: ((String) -> Void)?) -> [Thumbnail] {
        do {
            return try renderer.thumbnails(clip, cardPath: cardPath, info: info, lut: lut, isCancelled: { self.isCancelled })
        } catch {
            if !isCancelled { log?("thumbnails \(clip.name): \(error)") }
            return []
        }
    }
}
