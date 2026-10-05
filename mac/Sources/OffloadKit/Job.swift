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
            case .waiting: return "Čeká"
            case .copying: return "Kopírování"
            case .flushing: return "Zápis cache disků"
            case .verifying: return "Ověřování"
            case .reporting: return "Report"
            case .analyzing: return "Analýza klipů"
            case .proxies: return "Render proxy"
            case .done: return "Hotovo"
            case .failed: return "Chyba"
            case .cancelled: return "Zrušeno"
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
    /// All files are on all destinations and verified — the card may be ejected even while proxies render.
    public var safeToEject = false
    public var copySucceeded: Bool?
    public var error: String?

    public init() {}
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

    /// Set on a worker thread, read by the app's main thread — always under the lock.
    public var plan: OffloadPlan? { lock.lock(); defer { lock.unlock() }; return _plan }
    public var report: OffloadReport? { lock.lock(); defer { lock.unlock() }; return _report }

    public convenience init(config: JobConfig) {
        self.init(config: config, options: OffloadJob.engineOptions(config))
    }

    /// Tests pass their own engine options (allowSourceVolume, beforeFileCopy …).
    init(config: JobConfig, options: OffloadOptions) {
        self.config = config
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
        let p = try engine.prepare(source: config.source, destRoots: config.destRoots, cardName: config.cardName, date: config.date)
        lock.lock(); _plan = p; lock.unlock()
        return p
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
        // Cancelled while still queued: it never runs, so say so instead of "Čeká" forever.
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
        do {
            let plan = try prepare()
            update { $0.phase = .copying }
            let report = try engine.run(plan)
            setReport(report)
            update {
                $0.safeToEject = report.safeToEject
                $0.copySucceeded = report.success
                $0.copy = engine.snapshot()
            }
            if isCancelled {
                update { $0.phase = .cancelled }
                return false
            }
            guard config.analyzeClips || config.proxy.enabled else {
                writePDFReports(report, plan: plan)
                update { $0.phase = .done }
                return false
            }
            update { $0.phase = .analyzing }
            return true
        } catch {
            update {
                $0.phase = .failed
                $0.error = "\(error)"
            }
            return false
        }
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

    // MARK: - Clips

    private func analyzeAndRender(plan: OffloadPlan, report input: OffloadReport, log: ((String) -> Void)?) -> OffloadReport {
        var report = input
        let tools = config.tools.resolved()
        let renderer = ProxyRenderer(settings: config.proxy, tools: tools)
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
                entries[i].probeError = "no verified copy"
                continue
            }
            if let reason = renderer.unsupportedReason(clip), clip.format != .video {
                entries[i].probeError = reason
                continue
            }
            do {
                var info = try renderer.probe(clip, cardPath: plan.cardPaths[d])
                if clip.format == .video && info.width == nil {
                    if let (arri, arriInfo) = renderer.reclassifyUndecodableMXF(clip, cardPath: plan.cardPaths[d]) {
                        entries[i].clip = arri
                        info = arriInfo
                    } else {
                        entries[i].info = info
                        entries[i].probeError = "kodek nepodporován ffmpegem (např. Sony X-OCN) — zkopírováno a ověřeno, bez proxy"
                        continue
                    }
                }
                entries[i].info = info
                // RED and ARRI raw cannot give a single frame directly; their thumbnails come from the proxy later.
                if entries[i].clip.format != .r3d && entries[i].clip.format != .arriraw {
                    entries[i].thumbnails = tryThumbnails(renderer, entries[i].clip, cardPath: plan.cardPaths[d], info: info,
                                                          applyLUT: true, log: log)
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
        update {
            $0.phase = .proxies
            $0.clipCount = entries.count
        }
        var usedNames = Set<String>()
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
                entries[i].proxyNote = entries[i].probeError ?? "no verified copy"
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
                try renderer.render(clip, cardPath: plan.cardPaths[d], info: info, output: plan.cardPaths[d] + "/" + rel,
                                    progress: { f in self.update { $0.clipFraction = f } },
                                    isCancelled: { self.isCancelled })
                entries[i].proxyStatus = .done
                entries[i].proxyPath = rel
                if config.proxy.copyToAllDestinations {
                    // The proxy exists; a failed copy elsewhere is noted, not a failed proxy.
                    for (o, other) in plan.cardPaths.enumerated() where o != d {
                        do {
                            try copyFile(plan.cardPaths[d] + "/" + rel, to: other + "/" + rel)
                        } catch {
                            let note = "kopie na \(other) selhala: \(error)"
                            entries[i].proxyNote = entries[i].proxyNote.map { "\($0); \(note)" } ?? note
                            log?("proxy \(clip.name): copy to \(other) failed: \(error)")
                        }
                    }
                }
                if entries[i].thumbnails.isEmpty {
                    // RAW formats rendered via an intermediate: take the thumbnails from the finished proxy.
                    let proxyClip = Clip(name: name, path: rel, format: .video, segments: [rel], bytes: 0, isCameraProxy: false)
                    entries[i].thumbnails = tryThumbnails(renderer, proxyClip, cardPath: plan.cardPaths[d], info: info,
                                                          applyLUT: false, log: log)
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

    /// PDF copy of the report on every destination. Rendered once, then copied; a failure here never fails the offload.
    private func writePDFReports(_ report: OffloadReport, plan: OffloadPlan) {
        let name = "\(plan.cardName)_report.pdf"
        let first = plan.cardPaths[0] + "/" + Layout.reportDirName + "/" + name
        do {
            try PDFReport.write(report, to: first)
            for other in plan.cardPaths.dropFirst() {
                try? copyFile(first, to: other + "/" + Layout.reportDirName + "/" + name)
            }
        } catch {
            // The HTML report is already on disk; the PDF is a convenience copy.
            fputs("PDF report failed: \(error)\n", stderr)
        }
    }

    private func rewriteReports(_ report: OffloadReport, plan: OffloadPlan) {
        for path in plan.cardPaths {
            try? report.write(cardPath: path, includeHashRecords: false)
        }
    }

    private func tryThumbnails(_ renderer: ProxyRenderer, _ clip: Clip, cardPath: String, info: ClipInfo,
                               applyLUT: Bool, log: ((String) -> Void)?) -> [Thumbnail] {
        do {
            return try renderer.thumbnails(clip, cardPath: cardPath, info: info, applyLUT: applyLUT, isCancelled: { self.isCancelled })
        } catch {
            if !isCancelled { log?("thumbnails \(clip.name): \(error)") }
            return []
        }
    }

    private func copyFile(_ src: String, to dst: String) throws {
        try IO.mkdirs((dst as NSString).deletingLastPathComponent)
        let part = (dst as NSString).deletingLastPathComponent + "/." + (dst as NSString).lastPathComponent + ".part"
        unlink(part)
        do {
            try FileManager.default.copyItem(atPath: src, toPath: part)
            guard rename(part, dst) == 0 else { throw IOError("rename", dst) }
        } catch {
            unlink(part)
            throw error
        }
    }
}
