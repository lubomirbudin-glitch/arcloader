import Foundation

/// Writes what a job did into history.json (FORMATY „Místní soubory“; Windows R27, R6). Never fails a job and never
/// drops a record silently: a record waits up to `waitLimit` for the history lock; still busy, it goes to a background
/// queue (in order, retried every `retryDelay` while the app runs) and the job gets the warning `Msg.historyDelayed`.
public enum HistoryRecorder {
    /// How long one record waits for the history lock (60 s; tests make it short).
    public static var waitLimit: TimeInterval = 60
    /// The pause between two background attempts.
    public static var retryDelay: TimeInterval = 5

    struct Pending {
        let what: String
        let folder: String
        let write: (HistoryStore) -> HistoryWrite
        let done: (Bool) -> Void
    }

    private static let gate = NSLock()
    private static var pending: [Pending] = []
    private static var retrying = false

    /// The history folder of a job: `historyFolder` (tests) or Application Support. Nil = history is off: never the
    /// user's own folder from inside XCTest unless OFFLOAD_SETTINGS_DIR points elsewhere.
    static func folder(_ config: JobConfig) -> String? {
        guard config.recordHistory else { return nil }
        if let f = config.historyFolder { return f }
        let underTests = NSClassFromString("XCTestCase") != nil
        if underTests && ProcessInfo.processInfo.environment["OFFLOAD_SETTINGS_DIR"] == nil { return nil }
        return AppSupport.folder
    }

    /// One record. `delayed`: called when it had to go to the background; `done(true/false)`: written (or not needed) /
    /// given up — only for a delayed record.
    static func record(_ what: String, folder: String, log: ((String) -> Void)?, delayed: @escaping () -> Void,
                       done: @escaping (Bool) -> Void, _ write: @escaping (HistoryStore) -> HistoryWrite) {
        let item = Pending(what: what, folder: folder, write: write, done: done)
        gate.lock()
        if !pending.isEmpty {
            // Records keep their order: the start of a copy is never written after its end.
            pending.append(item)
            gate.unlock()
            delayed()
            return
        }
        gate.unlock()
        if attempt(item, log: log) != .busy { return }
        log?("history: busy for \(Int(waitLimit)) s — the \(what) record is written later")
        gate.lock()
        pending.append(item)
        let start = !retrying
        retrying = true
        gate.unlock()
        delayed()
        if start {
            let t = Thread { retryLoop(log: log) }
            t.name = "offload.history-retry"
            t.start()
        }
    }

    static func attempt(_ p: Pending, log: ((String) -> Void)?) -> HistoryWrite {
        let store = HistoryStore(folder: p.folder, lockTimeout: waitLimit)
        store.log = log
        let r = p.write(store)
        if r == .failed { log?("history (\(p.what)): not written") }
        return r
    }

    static func retryLoop(log: ((String) -> Void)?) {
        while true {
            gate.lock()
            guard let next = pending.first else {
                retrying = false
                gate.unlock()
                return
            }
            gate.unlock()
            if attempt(next, log: log) == .busy {
                Thread.sleep(forTimeInterval: retryDelay)
                continue
            }
            log?("history: the \(next.what) record written after the history was busy")
            gate.lock()
            pending.removeFirst()
            gate.unlock()
            next.done(true)
        }
    }

    /// True when no record waits (tests; the app before it quits, the CLI before it exits). Waits up to `timeout`.
    @discardableResult
    public static func drain(timeout: TimeInterval) -> Bool {
        let end = Date().addingTimeInterval(timeout)
        while true {
            gate.lock()
            let empty = pending.isEmpty
            gate.unlock()
            if empty { return true }
            if Date() >= end { return false }
            Thread.sleep(forTimeInterval: 0.05)
        }
    }

    /// Records still waiting (the app logs them when it quits before they got through).
    public static var waiting: Int {
        gate.lock()
        defer { gate.unlock() }
        return pending.count
    }

    // MARK: - What a job records

    /// R6: the entry of a full copy as it starts — "unfinished", no files yet; `finish` replaces it at the end.
    static func startEntry(id: String, plan: OffloadPlan, config: JobConfig, volume: CardVolume?, started: Date) -> HistoryEntry {
        var e = HistoryEntry(id: id, source: plan.source, cardName: plan.cardName, date: plan.date, finished: started)
        e.project = config.project
        e.unfinished = true
        e.hashAlgorithms = plan.hashKinds.names
        e.card = volume.map(historyCard)
        e.destinations = zip(plan.destRoots, plan.cardPaths).map { HistoryDestination(root: $0, cardPath: $1) }
        return e
    }

    static func historyCard(_ v: CardVolume) -> HistoryCard {
        HistoryCard(identity: v.identity, fsSerial: v.fsSerial, capacity: v.capacity, label: v.label, filesystem: v.filesystem)
    }

    /// The finished stage 1 (with failed files too). A full copy replaces its start entry; an append adds its generation
    /// to the entry whose destinations are these card folders (or starts one when history does not know the backup) —
    /// under one hold of the history lock. `card`: the whole card as scanned at the start. `whole`: the backup as its
    /// card folders' reports say it now (BackupState — the one definition of "verified").
    static func finishedEntry(_ existing: HistoryEntry?, id: String, plan: OffloadPlan, report: OffloadReport, config: JobConfig,
                              volume: CardVolume?, card: SourceScan, whole: BackupState, storedFiles: [SourceFile]?)
        -> (HistoryEntry, [SourceFile]) {
        var e: HistoryEntry
        if var x = existing, !(x.unfinished && x.generations.isEmpty) {
            x.success = x.success && report.success
            e = x
        } else {
            // A new entry, or the start entry of this copy (no generation yet) becoming the copy's entry.
            e = existing ?? HistoryEntry(id: id, source: plan.source, cardName: plan.cardName, date: plan.date, finished: report.finished)
            e.source = plan.source
            e.cardName = plan.cardName
            e.date = plan.date
            if existing == nil || plan.mode == .full { e.project = plan.project ?? config.project }
            e.hashAlgorithms = report.algorithms
            e.destinations = zip(plan.destRoots, plan.cardPaths).map { HistoryDestination(root: $0, cardPath: $1) }
            e.success = report.success
        }
        e.finished = report.finished
        e.success = e.success && whole.failed == 0
        e.failedFiles = e.success ? nil : max(whole.failed, report.failedFiles.count)
        e.verified = e.success && whole.unverified == 0 && whole.failed == 0
        e.generations.removeAll { $0.seq == plan.generation }
        e.generations.append(HistoryGeneration(seq: plan.generation, kind: plan.mode == .full ? "full" : plan.mode == .append ? "append" : "repair",
                                               finished: report.finished, files: plan.scan.files.count,
                                               bytes: plan.scan.totalBytes, verified: report.safeToFormat))
        e.generations.sort { $0.seq < $1.seq }
        e.reels = ReelSuggestion.fromClips(ClipDetector.detect(card.files)).reels
        e.unfinished = report.verifyPending == true
        return withCard(e, volume: volume, card: card, storedFiles: storedFiles)
    }

    /// The card as it is now (fingerprint, shape, the volume) into `entry`; its file list, `files` and `bytes` are the
    /// union with the list stored before (an append).
    static func withCard(_ entry: HistoryEntry, volume: CardVolume?, card: SourceScan, storedFiles: [SourceFile]?) -> (HistoryEntry, [SourceFile]) {
        var e = entry
        var all: [String: SourceFile] = [:]
        for f in storedFiles ?? [] { all[f.relPath] = f }
        for f in card.files { all[f.relPath] = f }
        let files = all.values.sorted { CardFingerprint.ordinal($0.relPath, $1.relPath) }
        let (fp, shape) = CardFingerprint.compute(card.files)
        e.card = volume.map(historyCard) ?? e.card
        e.fingerprint = fp
        e.shape = shape
        e.files = files.count
        e.bytes = files.reduce(0) { $0 + $1.size }
        return (e, files)
    }

    /// The newest entry that has one of these card folders as a destination.
    static func entryOf(_ entries: [HistoryEntry], _ cardPaths: [String]) -> HistoryEntry? {
        let wanted = Set(cardPaths.map { CardRecord.normal($0) })
        return entries.first { e in e.destinations.contains { wanted.contains(CardRecord.normal($0.cardPath)) } }
    }
}
