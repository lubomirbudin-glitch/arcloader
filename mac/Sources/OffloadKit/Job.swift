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

    public init(source: String, destRoots: [String]) {
        self.source = source
        self.destRoots = destRoots
    }
}

public struct JobStatus {
    public enum Phase: String {
        case waiting, copying, flushing, verifying, reporting, analyzing, proxies, done, failed, cancelled

        public var label: String {
            switch self {
            case .waiting: return "Waiting"
            case .copying: return "Copying"
            case .flushing: return "Flushing disk caches"
            case .verifying: return "Verifying"
            case .reporting: return "Report"
            case .analyzing: return "Analysing clips"
            case .proxies: return "Rendering proxies"
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
            let ap = try AppendPlanner.plan(config.source, cardPaths: into)
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
        return s
    }

    private func update(_ f: (inout JobStatus) -> Void) {
        lock.lock(); f(&state); lock.unlock()
    }

    /// Runs the whole job synchronously. Never throws: the outcome is in `status()` and the returned report.
    @discardableResult
    public func run(log: ((String) -> Void)? = nil) -> OffloadReport? {
        guard runCopy() else { return report }
        runPost(log: log)
        return report
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
        if cancelledBeforeStart { return false }
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
            // R6: history knows the copy from its start — one the app never finishes can be resumed from its card.
            if let historyFolder, plan.mode == .full { recordStart(plan, volume: volume, folder: historyFolder) }
            let report = try engine.run(plan)
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
                forgetStart()   // a cancelled copy is not recorded in history
                update { $0.phase = .cancelled }
                return false
            }
            if let historyFolder { recordFinished(plan, report: report, volume: volume, card: plan.scan, folder: historyFolder) }
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
        defer { held.release() }
        let ap: AppendPlan
        do {
            ap = try AppendPlanner.plan(config.source, cardPaths: into, isCancelled: { self.isCancelled })
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
        let report: OffloadReport
        do {
            report = try engine.run(plan)
        } catch {
            fail(error)
            return false
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
            update { $0.phase = .cancelled }
            return false
        }
        if let historyFolder { recordFinished(plan, report: report, volume: volume, card: ap.fullScan, folder: historyFolder) }
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
        let final = analyzeAndRender(plan: plan, report: report, log: log)
        setReport(final)
        update { $0.phase = .reporting }
        writePDFReports(final, plan: plan)
        let finalPhase: JobStatus.Phase = isCancelled ? .cancelled : .done
        update { $0.phase = finalPhase }
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
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card.
        func goodDest(_ clip: Clip) -> Int? {
            report.destinations.indices.first { d in
                clip.segments.allSatisfy { seg in
                    fileIndex[seg].map { OffloadReport.isGood(report.files[$0].destinations[d], verified: verified) } ?? false
                }
            }
        }

        var entries = clips.map(ClipEntry.init)
        update {
            $0.phase = .analyzing
            $0.clipCount = entries.count
        }
        for i in entries.indices {
            if isCancelled { break }
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
                        entries[i].info = info
                        entries[i].probeError = "codec not supported by ffmpeg (e.g. Sony X-OCN) — copied and verified, no proxy"
                        continue
                    }
                }
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
        // An append writes next to earlier proxies: every name already in _PROXY on any destination is taken. (A full
        // copy has new card folders; a name taken there meanwhile still fails the render instead of replacing it.)
        var usedNames = plan.mode == .full ? Set<String>() : JobFiles.proxyNames(plan.cardPaths)
        for i in entries.indices {
            if isCancelled { break }
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
            var name = clip.name
            var n = 2
            while usedNames.contains(name.lowercased()) { name = "\(clip.name)_\(n)"; n += 1 }
            usedNames.insert(name.lowercased())
            let rel = Layout.proxyDirName + "/" + name + ".mov"

            let t0 = Date()
            do {
                try renderer.render(clip, cardPath: plan.cardPaths[d], info: info, lut: entries[i].lut,
                                    output: plan.cardPaths[d] + "/" + rel,
                                    progress: { f in self.update { $0.clipFraction = f } },
                                    isCancelled: { self.isCancelled })
                entries[i].proxyStatus = .done
                entries[i].proxyPath = rel
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
                    let (_, error) = ProxyCopy.toExtraFolder(plan.cardPaths[d] + "/" + rel, extraFolder: extra, date: plan.date,
                                                             cardName: plan.cardName, fileName: name + ".mov")
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
        let first = plan.cardPaths[0] + "/" + Layout.reportDirName + "/" + name
        do {
            try PDFReport.write(report, to: first)
            for other in plan.cardPaths.dropFirst() {
                try? ProxyCopy.copyFile(first, to: other + "/" + Layout.reportDirName + "/" + name)
            }
        } catch {
            // The HTML report is already on disk; the PDF is a convenience copy.
            fputs("PDF report failed: \(error)\n", stderr)
        }
        LocalReports.sync(date: plan.date, cardName: plan.cardName, cardPaths: plan.cardPaths)
    }

    private func rewriteReports(_ report: OffloadReport, plan: OffloadPlan) {
        for path in plan.cardPaths {
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
