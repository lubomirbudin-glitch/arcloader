import Darwin
import Foundation

/// One job's verification in stage 2 (tasks 67–68; Windows `VerifySession`): the copies of a copy whose reports say
/// `verifyPending` are verified here — every destination in the background, one thread per volume at a low I/O priority,
/// block by block against the card's block hashes (`BlockVerifier`, `BlockReads`). A cancel stops every reader before its
/// next block (the copies it did not finish stay "written"); Pause waits between files. A copy is "verified" only when
/// every block was hashed from a read of that destination; a read that may have come from the cache is noted (spec D.23.1).
public final class VerifySession {
    /// The most blocks per file noted with the card read again (spec 10); the rest are counted in one more note.
    static let maxBlockNotes = 3
    static let noBlockHashes = "verify: no block hashes"

    let files: [OffloadReport.FileEntry]
    let cardPaths: [String]
    let manifests: [BlockManifest?]
    private var results: [[DestFileResult]]
    private var verifiers: [[BlockVerifier?]]
    let gate: PauseGate
    let cardBlock: ((Int, Int) -> UInt64?)?
    let diskName: (Int) -> String
    private let lock = NSLock()
    private var stopped = false
    private var started = false
    private var finished = false
    private var readers: [Reader] = []
    private var cachedReads: [Int]
    private let verifiedCounters: [Counter]
    /// The destination verified through the proxies; -1 when none may be (every one is read back).
    public let primary: Int
    /// Per destination why it is read back although verify-in-proxy runs (the report's `verifyNote`); nil for the primary.
    public let classicWhy: [String?]
    /// Per destination the bytes there are to verify.
    public let toVerify: [Int64]
    /// Files of the primary that a proxy reads (the background leaves them to the tap and the fill-in) — task 68.
    var proxyFiles = Set<String>()
    /// How long a reader may go without reading a block before its disk counts as hung. Tests shorten it.
    var readStallTimeout: TimeInterval = ProxyRenderer.defaultStallTimeout
    /// Background readers at PRIO_DARWIN_BG (I/O throttled under other I/O — spec D.27 row 9). Tests may turn it off.
    static var backgroundPriority = true

    /// One background thread: the work of one volume.
    final class Reader {
        let volume: String
        let dests: [Int]
        let work: [(dest: Int, file: Int)]
        let gated: Bool
        var worker: PauseGate.Worker?
        var thread: Thread?
        let lock = NSLock()
        var progress: Int64 = 0
        var waiting = false
        var abandoned = false
        var done = false
        var current: String?

        init(volume: String, dests: [Int], work: [(dest: Int, file: Int)], gated: Bool) {
            self.volume = volume
            self.dests = dests
            self.work = work
            self.gated = gated
        }

        func with<T>(_ f: () -> T) -> T { lock.lock(); defer { lock.unlock() }; return f() }
        var isAbandoned: Bool { with { abandoned } }
        func tick(_ n: Int = 1) { with { progress += Int64(n) } }
    }

    /// `report`: the copy's report (states per destination); `blocks`: per file the card's XXH3-64 block hashes
    /// (`OffloadEngine.fileBlocks`); `rates`: per destination its write rate in the copy; `preferred`: the user's primary
    /// (0-based) or nil = the fastest; `classicWhy`: per destination a reason to read it back (CacheProbe, the setting) or
    /// nil; `cardBlock`: the XXH3-64 of block b of file i read from the card again — nil when the card is not the copied
    /// one any more (spec 10).
    public init(report: OffloadReport, cardPaths: [String], blocks: [[UInt64]], blockSize: Int, rates: [Double], preferred: Int?,
                classicWhy: [String?], gate: PauseGate, cardBlock: ((Int, Int) -> UInt64?)?, diskName: ((Int) -> String)? = nil) {
        let entries = report.files
        files = entries
        self.cardPaths = cardPaths
        self.gate = gate
        self.cardBlock = cardBlock
        let roots = report.destinations.map(\.root)
        self.diskName = diskName ?? { d in HistoryText.diskName(roots.indices.contains(d) ? roots[d] : "") }
        let dests = cardPaths.count
        let ms: [BlockManifest?] = entries.indices.map { i in
            BlockManifest.make(size: entries[i].size, blockSize: blockSize, blocks: i < blocks.count ? blocks[i] : [])
        }
        manifests = ms
        var res: [[DestFileResult]] = (0..<dests).map { d in entries.map { VerifySession.stateOf($0, d) } }
        // A copy without block hashes that fit it is never verified — failed with the reason, not left "written".
        for d in 0..<dests {
            for i in entries.indices where res[d][i].state == .written && ms[i] == nil {
                res[d][i] = DestFileResult(state: .failed, error: VerifySession.noBlockHashes)
            }
        }
        results = res
        verifiers = Array(repeating: Array(repeating: nil, count: entries.count), count: dests)
        cachedReads = Array(repeating: 0, count: dests)
        verifiedCounters = (0..<dests).map { _ in Counter() }
        toVerify = (0..<dests).map { d in entries.indices.filter { res[d][$0].state == .written }.reduce(Int64(0)) { $0 + entries[$1].size } }
        let eligible = (0..<dests).filter { $0 < classicWhy.count && classicWhy[$0] == nil }
        let p: Int
        if let preferred, eligible.contains(preferred) {
            p = preferred
        } else {
            p = eligible.max { a, b in
                let ra = a < rates.count ? rates[a] : 0, rb = b < rates.count ? rates[b] : 0
                return ra != rb ? ra < rb : a > b   // the fastest; on a tie the first
            } ?? -1
        }
        primary = p
        self.classicWhy = (0..<dests).map { d in d == p ? nil : (d < classicWhy.count ? classicWhy[d] : nil) ?? VIPMsg.classicSecondary }
    }

    static func stateOf(_ f: OffloadReport.FileEntry, _ d: Int) -> DestFileResult {
        let s = d < f.destinations.count ? f.destinations[d] : "pending"
        switch s {
        case "written" where f.sourceError == nil: return DestFileResult(state: .written, error: nil)
        case "verified": return DestFileResult(state: .verified, error: nil)
        case "failed": return DestFileResult(state: .failed, error: d < f.errors.count ? f.errors[d] : nil)
        default: return DestFileResult(state: .pending, error: nil)
        }
    }

    var isStopped: Bool { lock.lock(); defer { lock.unlock() }; return stopped }

    /// Bytes verified so far per destination (never above `toVerify`).
    public var verifiedBytes: [Int64] { verifiedCounters.indices.map { min(verifiedCounters[$0].get(), toVerify[$0]) } }

    /// The files the verification reads now (relative), for "Pausing (finishing …)".
    public var currentFiles: [String] {
        lock.lock(); let all = readers; lock.unlock()
        var out: [String] = []
        for r in all { if let c = r.with({ r.abandoned ? nil : r.current }), !out.contains(c) { out.append(c) } }
        return out
    }

    func verifier(_ d: Int, _ i: Int) -> BlockVerifier? {
        lock.lock(); defer { lock.unlock() }
        guard let m = manifests[i] else { return nil }
        if let v = verifiers[d][i] { return v }
        let v = BlockVerifier(m)
        verifiers[d][i] = v
        return v
    }

    func isWritten(_ d: Int, _ i: Int) -> Bool {
        lock.lock(); defer { lock.unlock() }
        return results[d][i].state == .written && manifests[i] != nil
    }

    func volumeOf(_ d: Int) -> String {
        IO.device(of: cardPaths[d]).map { "dev:\($0)" } ?? cardPaths[d]
    }

    /// Starts the background reads: every destination but the primary in full, and the primary's files no proxy reads —
    /// one thread per volume.
    public func startBackground() {
        lock.lock()
        if started || finished || stopped { lock.unlock(); return }
        started = true
        lock.unlock()
        var byVolume: [String: [(dest: Int, file: Int)]] = [:]
        var order: [String] = []
        let primaryVolume = primary >= 0 ? volumeOf(primary) : nil
        for d in cardPaths.indices {
            let v = volumeOf(d)
            for i in files.indices where isWritten(d, i) {
                if d == primary && proxyFiles.contains(files[i].path.lowercased()) { continue }   // the proxy reads it
                if byVolume[v] == nil { order.append(v) }
                byVolume[v, default: []].append((d, i))
            }
        }
        for v in order {
            let work = byVolume[v]!
            let reader = Reader(volume: v, dests: Array(Set(work.map(\.dest))).sorted(), work: work, gated: v == primaryVolume)
            reader.worker = gate.work()   // joined before its thread starts: the job never stands while it has yet to begin
            let t = Thread { [weak self] in
                self?.background(reader)
                reader.with { reader.done = true }
                reader.worker?.leave()
            }
            t.name = "offload.verify." + String(work[0].dest)
            reader.thread = t
            lock.lock(); readers.append(reader); lock.unlock()
            t.start()
        }
    }

    private func background(_ reader: Reader) {
        if VerifySession.backgroundPriority { setpriority(PRIO_DARWIN_THREAD, 0, PRIO_DARWIN_BG) }
        for (d, i) in reader.work {
            if isStopped || reader.isAbandoned { return }
            // Pause, before the next file (the one in progress is finished).
            if let w = reader.worker, gate.isPaused {
                reader.with { reader.waiting = true }
                let go = w.waitWhilePaused(isCancelled: { self.isStopped || reader.isAbandoned })
                reader.with { reader.waiting = false }
                reader.tick()
                if !go { return }
            }
            verify(d, i, reader)
        }
    }

    /// Hook between blocks of a gated reader (task 68: waits while a proxy reads the primary); true = go on.
    func beforeBlock(_ reader: Reader) -> Bool { !isStopped && !reader.isAbandoned }

    /// Reads what destination `d`'s copy of file `i` still lacks and records the verdict; a cancel leaves it "written",
    /// any error (an unplugged disk, a vanished copy) fails it — never a verdict without the whole read.
    func verify(_ d: Int, _ i: Int, _ reader: Reader) {
        guard let v = verifier(d, i), isWritten(d, i) else { return }
        reader.with { reader.current = files[i].path }
        defer { reader.with { reader.current = nil } }
        let path = cardPaths[d] + "/" + files[i].path
        do {
            let (outcome, cached) = try BlockReads.complete(path, v, beforeBlock: {
                reader.tick()
                return self.beforeBlock(reader)
            }, isCancelled: { self.isStopped || reader.isAbandoned }, progress: { n in
                self.verifiedCounters[d].add(n)
                reader.tick(n)
            })
            let result = BlockReads.result(v, outcome)
            lock.lock()
            if !reader.isAbandoned {
                results[d][i] = result
                if cached && result.state == .verified { cachedReads[d] += 1 }
            }
            lock.unlock()
        } catch is CancellationError {
        } catch {
            lock.lock()
            if !reader.isAbandoned { results[d][i] = DestFileResult(state: .failed, error: "verify: \(error)") }
            lock.unlock()
        }
    }

    /// Waits until every reader has ended or was given up on. A reader without a block read for `readStallTimeout` (a
    /// wait at the gate or for the tap, or the job standing paused, not counting) has a hung disk — not an unplugged one,
    /// that fails the read: it is given up on and every copy of its destinations still "written" fails.
    func waitFor(_ all: [Reader], isCancelled: () -> Bool) {
        var seen = all.map { _ in Int64(-1) }
        var since = all.map { _ in Date() }
        while true {
            if isCancelled() { stop() }
            var alive = false
            for (k, r) in all.enumerated() {
                let (done, abandoned, now, waiting) = r.with { (r.done, r.abandoned, r.progress, r.waiting) }
                if done || abandoned { continue }
                alive = true
                if now != seen[k] || waiting || gate.isHeld {
                    seen[k] = now
                    since[k] = Date()
                } else if Date().timeIntervalSince(since[k]) >= readStallTimeout {
                    giveUp(r)
                }
            }
            if !alive { return }
            usleep(50_000)
        }
    }

    func giveUp(_ r: Reader) {
        let first = r.with { () -> Bool in
            if r.abandoned { return false }
            r.abandoned = true
            return true
        }
        guard first else { return }
        r.worker?.leave()   // a thread given up on never keeps a pause from standing
        lock.lock()
        for d in r.dests {
            for j in files.indices where results[d][j].state == .written {
                results[d][j] = DestFileResult(state: .failed, error: VIPMsg.verifyDiskHung)
            }
        }
        lock.unlock()
    }

    /// Stops every reader at its next block (a cancel, the job closed).
    public func stop() {
        lock.lock(); stopped = true; lock.unlock()
    }

    /// After the proxies: the background reads end (waiting while paused), then the notes of spec 10. `pausing`: the
    /// caller's membership of the pause gate — idle while it only waits for the readers. `isCancelled` true: the readers
    /// stop, the copies they did not finish stay "written", `verified` false.
    public func finish(pausing: PauseGate.Worker? = nil, isCancelled: @escaping () -> Bool)
        -> (results: [[DestFileResult]], verified: Bool, notes: [String], throughCache: [Int]) {
        startBackground()   // nothing started yet (no proxies): every copy is read now
        lock.lock(); finished = true; let all = readers; lock.unlock()
        if let pausing { pausing.whileIdle { waitFor(all, isCancelled: isCancelled) } } else { waitFor(all, isCancelled: isCancelled) }
        let cancelled = isCancelled() || isStopped
        let notes = cancelled ? [] : annotate()
        lock.lock(); defer { lock.unlock() }
        return (results, !cancelled, notes, cachedReads)
    }

    /// Spec 10: unstable reads; a block wrong on several destinations, with what the card gives now; reads through the
    /// cache (spec D.23.1).
    func annotate() -> [String] {
        var notes: [String] = []
        for i in files.indices {
            let path = files[i].path
            for d in cardPaths.indices {
                guard let v = verifiersSnapshot(d, i) else { continue }
                let unstable = v.unstableBlocks.filter { v.diskHash($0) == nil }
                if !unstable.isEmpty { notes.append(VIPMsg.unstableRead(path, diskName(d), unstable)) }
            }
            guard let m = manifests[i] else { continue }
            var noted = 0, more = 0
            for b in 0..<m.count {
                let dests = cardPaths.indices.filter { verifiersSnapshot($0, i)?.diskHash(b) != nil }
                guard dests.count >= 2 else { continue }
                if noted == VerifySession.maxBlockNotes { more += 1; continue }
                noted += 1
                let disks = Set(dests.compactMap { verifiersSnapshot($0, i)?.diskHash(b) })
                let card = cardBlock?(i, b)
                let says: String
                if card == nil {
                    says = VIPMsg.cardNotReadAgain
                } else if card == m.blocks[b] {
                    says = VIPMsg.cardGivesRecorded
                } else if disks.count == 1, card == disks.first {
                    says = VIPMsg.cardGivesWhatDisksHave
                } else {
                    says = VIPMsg.cardGivesOther
                }
                notes.append(VIPMsg.sameBlockWrong(path, b, dests.map(diskName).joined(separator: ", "), says))
            }
            if more > 0 { notes.append(VIPMsg.sameBlockWrongMore(path, more)) }
        }
        lock.lock(); let cached = cachedReads; lock.unlock()
        for d in cardPaths.indices where cached[d] > 0 { notes.append(VIPMsg.verifiedThroughCache(diskName(d), cached[d])) }
        return notes
    }

    private func verifiersSnapshot(_ d: Int, _ i: Int) -> BlockVerifier? {
        lock.lock(); defer { lock.unlock() }
        return verifiers[d][i]
    }

    /// The XXH3-64 of block `block` (of `blockSize` bytes) of `path` on the card, read with F_NOCACHE. Only reads.
    public static func readCardBlock(_ path: String, block: Int, blockSize: Int) -> UInt64? {
        guard let fd = try? IO.openRead(path) else { return nil }
        defer { close(fd) }
        let buf = IO.allocateAligned(max(blockSize, 4096))
        defer { free(buf) }
        guard let n = try? IO.pread(fd, buf, blockSize, Int64(block) * Int64(blockSize), path: path) else { return nil }
        return BlockHash.xxh3(buf, n)
    }
}
