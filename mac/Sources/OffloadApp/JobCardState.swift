import AppKit
import OffloadKit

/// One segment of the phase indicator: "1 · Copy".
struct PhaseStep: Identifiable, Equatable {
    enum State { case done, active, pending }
    let number: Int
    let label: String
    let state: State
    let fill: Double
    var id: Int { number }
    var text: String { "\(number) · \(label)" }
}

/// One row of the speed table: "Card read" or a destination (with its number), the share done and the speed.
struct TransferRow: Identifiable, Equatable {
    let id: Int
    let name: String
    let number: Int?
    let share: Double
    let rate: String
    var tooltip: String?
}

/// One number of a finished card: "Copy" / "602 MB/s".
struct SummaryField: Identifiable, Equatable {
    let label: String
    let value: String
    var id: String { label }
}

/// A file that is not good on every destination, with "RAID_A ✓" / "SHUTTLE_B ✕ hash" per destination.
struct FailedFileRow: Identifiable, Equatable {
    struct Disk: Equatable { let ok: Bool; let text: String }
    let id: Int
    let path: String
    let disks: [Disk]
}

/// Everything a job card shows (UI návrh obr. 2 and 3, Windows `JobCard.cs`), built from the job every tick — or
/// made up by the snapshot renderer.
struct JobCardState {
    enum Kind { case waiting, running, done, problem, failed, cancelled }

    var kind: Kind
    var phase: JobStatus.Phase
    var name: String
    var sub: String
    /// The headline of a finished card and the sentence under it.
    var title = ""
    var text = ""
    var percent: Int?
    var remaining: String?
    var steps: [PhaseStep] = []
    var rows: [TransferRow] = []
    var fileCaption = ""
    var fileValue = ""
    var brake: String?
    /// Everything is copied while clips are still analysed / proxies rendered: the card may already go.
    var safeWhileRunning = false
    /// …and verified everywhere: the card may also be formatted (the strip is green only then).
    var safeFormat = false
    /// The two verdicts of the card row (Windows R5), never merged; none while the job waits.
    var ejectChip: CardChip?
    var formatChip: CardChip?
    var summary: [SummaryField] = []
    var thumbs: [ClipThumb] = []
    var moreThumbs = 0
    var failed: [FailedFileRow] = []
    var moreFailed: String?
    var path: String?
    var canCancel = false
    /// Pause / Resume (a running job only; the CLI has none).
    var canPause = false
    var paused = false
    /// "Pausing (finishing file A001C003.mov…)" / "Paused — Resume continues with the next file.", nil while it runs.
    var pauseText: String?
    /// A waiting card behind a job paused in its copy: "Waiting for a paused job."
    var waitingHint: String?
    var showEject = false
    var ejecting = false
    var ejected = false
    var hasReport = false
    var hasProxies = false
    /// Task 70: "A001C001 — corrupted data at 01:00:02:13" per clip with a content defect.
    var defects: [String] = []
    /// The card may be formatted but its defects have not been looked at yet.
    var canConfirmContent = false

    static let shownFailed = 5

    @MainActor
    static func make(_ item: JobItem, model: AppModel) -> JobCardState {
#if DEBUG
        if let preview = item.preview { return preview }
#endif
        var s = item.status
        // Task 70: once the user has looked at the content defects, they are no reason to hold the card back.
        let defectWarning = s.contentWarnings > 0 && s.warning == VIPMsg.contentWarnings(s.contentWarnings)
        if item.contentConfirmed && defectWarning { s.warning = nil }
        let plan = item.plan
        let report = item.report
        let config = item.job.config
        let names = item.destNames
        let name = item.isAppend ? AppendRules.title(plan?.cardName ?? item.sourceName) : (plan?.cardName ?? item.sourceName)
        var c = JobCardState(kind: kind(s, report), phase: s.phase, name: name, sub: "")
        if let plan {
            c.sub = "\(Fmt.count(plan.scan.files.count, "file")) · \(Fmt.bytes(plan.scan.totalBytes))"
            // Which way this card is verified (user requirement 2026-10-11).
            if config.verify { c.sub += s.verifyInProxy ? " · verify during proxy render" : " · verify by read-back" }
            c.path = "…/\(plan.date)/\(plan.cardName)/"
        } else {
            c.sub = ([item.estimatedBytes.map { "~" + Fmt.bytes($0) }] + [Fmt.count(names.count, "disk")])
                .compactMap { $0 }.joined(separator: " · ")
        }
        c.canCancel = item.isRunning || s.phase == .waiting
        c.showEject = model.canShowEject(item)
        c.ejecting = item.ejecting
        c.ejected = item.ejected
        c.hasReport = report != nil && plan != nil
        c.hasProxies = report?.proxy?.enabled == true && s.proxiesDone > 0
        if let r = report {
            c.defects = r.contentWarnings.map { e in
                "\(e.clip.name) — \(ClipContent.kindText(e.content?.kind)) at \(e.content?.timecode ?? "?")"
            }
        }
        c.canConfirmContent = !item.contentConfirmed && s.contentWarnings > 0 && s.safeToFormat && s.phase.isFinished
        if c.kind != .waiting {
            let chips = JobProgress.chips(s, contentConfirmed: item.contentConfirmed, contentWarnings: s.contentWarnings)
            c.ejectChip = chips.eject
            c.formatChip = chips.format
        }
        switch c.kind {
        case .waiting:
            c.title = "Waiting in queue"
            if model.waitsForPausedJob(item) { c.waitingHint = PausedQueue.waitingText }
            else if model.waitsForPreviousJob(item) { c.waitingHint = PausedQueue.oneAtATimeText }
        case .running:
            c.canPause = item.isRunning
            c.paused = s.paused
            c.pauseText = item.progress.pauseText
            if item.job.isCancelled {
                // Cancel pressed, the job is stopping: say so, and no second Cancel or Pause meanwhile.
                c.pauseText = JobProgress.cancellingText(s.phase)
                c.canPause = false
                c.canCancel = false
            }
            c.steps = steps(s, config, item.phaseProgress)
            c.percent = Int((item.phaseProgress * 100).rounded(.down))
            c.remaining = s.paused ? nil : remaining(s.phase, item.progress.eta)
            c.rows = rows(item, names)
            (c.fileCaption, c.fileValue) = fileLine(s)
            if s.phase == .copying && !s.paused, let chain = item.chain { c.brake = brakeSentence(item.progress.brake, chain, names) }
            c.safeWhileRunning = s.safeToEject
            c.safeFormat = s.safeToFormat
        case .done, .problem, .failed, .cancelled:
            (c.title, c.text) = headline(c.kind, name, s, report, names)
            if c.kind != .cancelled, let r = report {
                // Total without the time the job stood paused (Windows JobCard.Summary).
                c.summary = summary(r, seconds: item.startedAt.flatMap { start in
                    item.finishedAt.map { max(0, $0.timeIntervalSince(start) - s.pausedTotal) }
                })
                if !r.success {
                    c.failed = failedRows(r, names)
                    c.moreFailed = moreFiles(r.failedFiles.count)
                }
            }
            c.thumbs = item.thumbs
            c.moreThumbs = item.moreThumbs
        }
        return c
    }

    // MARK: Rules (Windows JobCard.cs, English)

    static func kind(_ s: JobStatus, _ r: OffloadReport?) -> Kind {
        switch s.phase {
        case .waiting: return .waiting
        case .cancelled: return .cancelled
        case .done, .failed:
            // No report: failed before the copy, or an append with nothing new (green only when the backup is safe).
            guard let r else { return s.phase == .failed ? .failed : s.safeToFormat && s.warning == nil ? .done : .problem }
            // Spec D.22.7: green (done) only when the card may be formatted; copied alone is a problem card.
            if !r.success || !r.verified || !s.safeToFormat || s.warning != nil { return .problem }
            return .done   // a failed stage 2 after a good copy: still safe, the text says what failed
        default: return .running
        }
    }

    static func headline(_ kind: Kind, _ card: String, _ s: JobStatus, _ r: OffloadReport?, _ names: [String]) -> (String, String) {
        switch kind {
        case .cancelled:
            return ("\(card) — cancelled", s.safeToFormat
                ? "The copy is done and verified; only the processing after it was cancelled."
                : s.safeToEject
                ? "The copy is done but not verified — the card is safe to eject; don't format it."
                : "The card isn't fully backed up — copy it again in full.")
        case .failed:
            return ("\(card) — offload failed", s.error ?? "The copy didn't finish.")
        case .problem:
            let notYet = s.safeToEject ? "\(card) — safe to eject, don't format yet" : "\(card) — don't eject the card yet"
            guard let r else { return (notYet, s.warning ?? s.error ?? "The backup isn't complete yet.") }
            // A copy that stopped with a reason of its own (the card swapped while paused) says it.
            if s.phase == .failed, let e = s.error { return (notYet, e) }
            if !r.success { return (notYet, problemText(r, names)) }
            if !r.verified { return ("\(card) — copied without verification", AfterCopyTexts.verifyOffWarning) }
            if let w = s.warning { return (notYet, w) }
            if r.verifyPending == true { return (notYet, "The verification hasn't finished yet.") }
            return (notYet, "The verification result isn't written on every destination yet.")
        case .done:
            guard let r else { return ("\(card) — safe to format", s.note ?? "Nothing new to copy.") }
            var safe = safeText(r.totalFiles, r.destinations.count)
            if s.phase == .failed { safe += " Processing after the copy failed: \(s.error ?? "unknown error")" }
            return ("\(card) — safe to format", safe)
        default:
            return (card, "")
        }
    }

    /// "All 24 files are on 2 disks and verified (xxHash64)."
    static func safeText(_ files: Int, _ disks: Int) -> String {
        let on = "on \(Fmt.count(disks, "disk"))"
        return files == 1 ? "The file is \(on) and verified (xxHash64)." : "All \(files) files are \(on) and verified (xxHash64)."
    }

    /// "1 file on SHUTTLE_B doesn't match the card. RAID_A is fine."
    static func problemText(_ r: OffloadReport, _ names: [String]) -> String {
        let failed = r.failedFiles
        let fromCard = failed.filter { $0.sourceError != nil }.count
        let perDisk = names.enumerated().map { d, name in
            (name, failed.filter { $0.sourceError == nil && !good(r, $0, d) }.count)
        }
        let bad = perDisk.filter { $0.1 > 0 }
        let fine = perDisk.filter { $0.1 == 0 }.map(\.0)
        var parts: [String] = []
        if fromCard > 0 { parts.append("\(files(fromCard)) couldn't be read from the card.") }
        if bad.count == 1 {
            parts.append("\(files(bad[0].1)) on \(bad[0].0) \(bad[0].1 == 1 ? "doesn't" : "don't") match the card.")
        } else if bad.count > 1 {
            parts.append("Not matching the card: " + bad.map { "\($0.0) \(files($0.1))" }.joined(separator: ", ") + ".")
        }
        if !bad.isEmpty && !fine.isEmpty && fromCard == 0 {
            parts.append("\(fine.joined(separator: " and ")) \(fine.count == 1 ? "is" : "are") fine.")
        }
        return parts.isEmpty ? "\(files(failed.count)) failed — see the report." : parts.joined(separator: " ")
    }

    private static func files(_ n: Int) -> String { Fmt.count(n, "file") }

    static func good(_ r: OffloadReport, _ f: OffloadReport.FileEntry, _ d: Int) -> Bool {
        guard d < f.destinations.count else { return false }
        let state = f.destinations[d]
        return state == FileState.verified.rawValue || (!r.verified && state == FileState.written.rawValue)
    }

    /// Why a copy failed, in a word: hash, size, read, cancelled, card, write, not copied.
    static func problemWord(_ state: String, _ error: String?) -> String {
        if state == FileState.pending.rawValue { return "not copied" }
        guard let e = error else { return "write" }
        if e.hasPrefix("HASH MISMATCH") || e.hasPrefix("MD5 MISMATCH") { return "hash" }
        if e.hasPrefix("size mismatch") { return "size" }
        if e.hasPrefix("verify:") { return "read" }
        if e == OffloadEngine.cardChangedWhilePaused { return "card" }
        if e.contains("cancelled") { return "cancelled" }
        if e.hasPrefix("source:") { return "card" }
        return "write"
    }

    static func failedRows(_ r: OffloadReport, _ names: [String]) -> [FailedFileRow] {
        r.failedFiles.prefix(shownFailed).enumerated().map { i, f in
            FailedFileRow(id: i, path: f.path, disks: names.enumerated().map { d, name in
                let state = d < f.destinations.count ? f.destinations[d] : FileState.pending.rawValue
                let ok = f.sourceError == nil && good(r, f, d)
                let why = f.sourceError != nil ? "card" : problemWord(state, d < f.errors.count ? f.errors[d] : nil)
                return FailedFileRow.Disk(ok: ok, text: ok ? "\(name) ✓" : "\(name) ✕ \(why)")
            })
        }
    }

    static func moreFiles(_ total: Int) -> String? {
        total > shownFailed ? "… and \(Fmt.count(total - shownFailed, "more file")) in the report" : nil
    }

    /// Copy · Verify · Proxy (or Analysis) · Report — the phases this job does; done ones full, the active one as far
    /// as it got. Empty for a failed or cancelled job.
    static func steps(_ s: JobStatus, _ config: JobConfig, _ progress: Double) -> [PhaseStep] {
        if s.verifyInProxy { return proxySteps(s, progress) }
        let stage2 = config.proxy.enabled || config.analyzeClips
        var labels = ["Copy"]
        if config.verify { labels.append("Verify") }
        if stage2 { labels.append(config.proxy.enabled ? "Proxy" : "Analysis") }
        labels.append("Report")
        if s.phase == .failed || s.phase == .cancelled { return [] }
        let stage1Report = s.phase == .reporting && s.copySucceeded == nil
        let active: Int
        switch s.phase {
        case .waiting: active = -1
        case .copying, .flushing: active = 0
        case .verifying: active = 1
        case .reporting where stage1Report: active = config.verify ? 1 : 0
        case .analyzing, .proxies: active = stage2 ? labels.count - 2 : labels.count - 1
        case .reporting: active = labels.count - 1
        default: active = labels.count
        }
        return labels.enumerated().map { i, label in
            let state: PhaseStep.State = i < active ? .done : i == active ? .active : .pending
            let fill = state == .done ? 1 : state == .active ? (stage1Report ? 1 : min(1, max(0, progress))) : 0
            return PhaseStep(number: i + 1, label: label, state: state, fill: fill)
        }
    }

    /// Verify-in-proxy (task 70): Copy · Proxy · Verify · Report — the verification ends after the proxies.
    static func proxySteps(_ s: JobStatus, _ progress: Double) -> [PhaseStep] {
        let labels = ["Copy", "Proxy", "Verify", "Report"]
        if s.phase == .failed || s.phase == .cancelled { return [] }
        let stage1Report = s.phase == .reporting && s.copySucceeded == nil
        let active: Int
        switch s.phase {
        case .waiting: active = -1
        case .copying, .flushing: active = 0
        case .reporting where stage1Report: active = 0
        case .analyzing, .proxies: active = 1
        case .finishingVerification: active = 2
        case .reporting: active = 3
        default: active = labels.count
        }
        return labels.enumerated().map { i, label in
            let state: PhaseStep.State = i < active ? .done : i == active ? .active : .pending
            let fill: Double
            if state == .done { fill = 1 } else if state != .active { fill = 0 } else if i == 2, s.verifyTotal > 0 {
                fill = min(1, Double(s.verifyDone) / Double(s.verifyTotal))
            } else {
                fill = stage1Report ? 1 : min(1, max(0, progress))
            }
            return PhaseStep(number: i + 1, label: label, state: state, fill: fill)
        }
    }

    /// "~0:02 left · copy" while copying or verifying.
    static func remaining(_ phase: JobStatus.Phase, _ eta: Double?) -> String? {
        guard let eta, phase == .copying || phase == .verifying else { return nil }
        return "~\(Fmt.clock(eta)) left · \(phase == .copying ? "copy" : "verify")"
    }

    /// The speed table while the card is read and written or read back.
    @MainActor
    static func rows(_ item: JobItem, _ names: [String]) -> [TransferRow] {
        let s = item.status
        guard let c = s.copy, [.copying, .flushing, .verifying].contains(s.phase) else { return [] }
        let verifying = s.phase == .verifying
        let p = item.progress
        let total = max(1, c.totalBytes)
        func share(_ v: Int64) -> Double { min(1, max(0, Double(v) / Double(total))) }
        func tip(_ i: Int) -> String? {
            guard let chain = item.chain, i < chain.count else { return nil }
            return "\(chain[i].role) \(chain[i].product) — \(chain[i].connection)"
        }
        var rows: [TransferRow] = []
        if !verifying {
            rows.append(TransferRow(id: 0, name: "Card read", number: nil, share: share(c.readBytes), rate: Fmt.rate(p.readRate), tooltip: tip(0)))
        }
        for (d, name) in names.enumerated() {
            let done = verifying ? (d < c.verifiedBytes.count ? c.verifiedBytes[d] : 0) : (d < c.writtenBytes.count ? c.writtenBytes[d] : 0)
            let rate = d < p.destRates.count ? p.destRates[d] : 0
            rows.append(TransferRow(id: d + 1, name: name, number: d + 1, share: share(done), rate: Fmt.rate(rate), tooltip: tip(d + 1)))
        }
        if s.paused {
            // Paused: no speeds (they are measured again from Resume).
            rows = rows.map { TransferRow(id: $0.id, name: $0.name, number: $0.number, share: $0.share, rate: "—", tooltip: $0.tooltip) }
        }
        return rows
    }

    /// "File 10 / 24" + the file's name, "Proxy 3 / 24" + the clip — under the speed table.
    static func fileLine(_ s: JobStatus) -> (String, String) {
        switch s.phase {
        case .copying:
            guard let c = s.copy else { return ("Copying", "") }
            return ("File \(min(c.fileIndex + 1, c.totalFiles)) / \(c.totalFiles)", (c.currentFile as NSString).lastPathComponent)
        case .flushing: return ("Flushing disk caches", "")
        case .verifying:
            guard let c = s.copy else { return ("Verifying", "") }
            return ("Verifying", "\(Fmt.bytes(c.verifiedBytes.reduce(0, +))) / \(Fmt.bytes(c.totalBytes * Int64(max(1, c.verifiedBytes.count))))")
        case .analyzing: return ("Clip \(s.clipIndex + 1) / \(s.clipCount)", s.clipName)
        case .proxies:
            return ("Proxy \(s.clipIndex + 1) / \(s.clipCount)", "\(s.clipName) · \(Int((s.clipFraction * 100).rounded())) %")
        case .reporting: return ("Writing the report", "")
        default: return (s.phase.label, "")
        }
    }

    /// "Slowest link: card reader (USB 3 (5 Gb/s)). The disks could go faster."
    static func brakeSentence(_ brake: Brake?, _ chain: [ChainElement], _ names: [String]) -> String? {
        guard let b = brake, b.element >= 0, b.element < chain.count else { return nil }
        let e = chain[b.element]
        let link = e.link.map { " (\($0)\(b.linkFull ? " — the link is full" : ""))" } ?? ""
        if b.element == 0 {
            return "Slowest link: card reader\(link). \(names.count == 1 ? "The disk could go faster." : "The disks could go faster.")"
        }
        let name = b.element - 1 < names.count ? names[b.element - 1] : e.role
        return "Slowest link: disk \(name)\(link). The card could be read faster."
    }

    /// Copy · Verify · Proxy · Total of a finished job.
    static func summary(_ r: OffloadReport, seconds: Double?) -> [SummaryField] {
        let proxy: String
        if r.proxy?.enabled == true {
            let done = r.clips.filter { $0.proxyStatus == .done }.count
            // A lighter original (task 54) is no proxy the job owes — not counted, like a skipped camera proxy.
            let total = r.clips.filter { !($0.proxyStatus == .skipped && ($0.proxyNote == nil || $0.originalLighter)) && !$0.clip.isCameraProxy }.count
            proxy = "\(done) / \(total)"
        } else {
            proxy = "off"
        }
        return [
            SummaryField(label: "Copy", value: Fmt.rate(r.totalBytes, r.copySeconds)),
            SummaryField(label: "Verify", value: r.verified ? Fmt.rate(r.totalBytes, r.verifySeconds) : "off"),
            SummaryField(label: "Proxy", value: proxy),
            SummaryField(label: "Total", value: Fmt.duration(seconds ?? r.finished.timeIntervalSince(r.started))),
        ]
    }
}
