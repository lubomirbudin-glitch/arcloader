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
    /// Files of the primary that a proxy reads (lower-cased): the background leaves them to the tap, the pre-read and the
    /// fill-in (task 68).
    private(set) var proxyFiles = Set<String>()
    /// Index of every file by its path (lower-cased).
    private var indexOf: [String: Int] = [:]
    /// Proxies reading the primary now (the primary volume's background reader waits meanwhile).
    private let tapIdle = NSCondition()
    private var taps = 0
    private var server: RangeServer?
    /// Clips served through the tap so far (tests, the log).
    private(set) var tapsServed = 0
    /// Mode B: how long the pre-read of a clip may go without a block before it is given up on (a hung primary disk) —
    /// the proxy's stall limit. Tests shorten it.
    var stallTimeout: TimeInterval = ProxyRenderer.defaultStallTimeout
    /// How long a reader may go without reading a block before its disk counts as hung. Tests shorten it.
    var readStallTimeout: TimeInterval = ProxyRenderer.defaultStallTimeout
    /// Background readers at the utility I/O tier (`setiopolicy_np` IOPOL_UTILITY): full speed alone, they yield to the next
    /// card's copy. Not PRIO_DARWIN_BG — measured 2026-10-11 (D.27 row 9) it held a lone read at 155 MB/s instead of 630.
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
        for (i, f) in entries.enumerated() where indexOf[f.path.lowercased()] == nil { indexOf[f.path.lowercased()] = i }
        if p >= 0 {
            let sources = entries.map { SourceFile(relPath: $0.path, size: $0.size, modified: $0.modified) }
            for clip in ClipDetector.detect(sources) {
                switch TapRegistry.mode(for: clip) {
                case .http: proxyFiles.insert(clip.path.lowercased())
                case .preRead: proxyFiles.formUnion(clip.segments.map { $0.lowercased() })
                case .none: break
                }
            }
        }
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
        for v in order { startReader(volume: v, work: byVolume[v]!, gated: v == primaryVolume) }
    }

    private func startReader(volume: String, work: [(dest: Int, file: Int)], gated: Bool) {
        let reader = Reader(volume: volume, dests: Array(Set(work.map(\.dest))).sorted(), work: work, gated: gated)
        reader.worker = gate.work()   // joined before its thread starts: the job never stands while it has yet to begin
        let t = Thread { [weak self] in
            self?.background(reader)
            reader.with { reader.done = true }
            reader.worker?.leave()
        }
        t.name = "offload.verify." + volume
        reader.thread = t
        lock.lock(); readers.append(reader); lock.unlock()
        t.start()
    }

    private func background(_ reader: Reader) {
        if VerifySession.backgroundPriority { setiopolicy_np(IOPOL_TYPE_DISK, IOPOL_SCOPE_THREAD, IOPOL_UTILITY) }
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

    /// Before every block: a gated reader (the primary's volume) waits while a proxy reads the primary — waiting is not
    /// reading (the watchdog does not count it). False = stop.
    func beforeBlock(_ reader: Reader) -> Bool {
        if isStopped || reader.isAbandoned { return false }
        guard reader.gated else { return true }
        tapIdle.lock()
        if taps > 0 { reader.with { reader.waiting = true } }
        while taps > 0 && !isStopped && !reader.isAbandoned { _ = tapIdle.wait(until: Date().addingTimeInterval(0.1)) }
        tapIdle.unlock()
        reader.with { reader.waiting = false }
        reader.tick()
        return !isStopped && !reader.isAbandoned
    }

    private func tapBusy() {
        tapIdle.lock(); taps += 1; tapIdle.unlock()
    }

    private func tapDone() {
        tapIdle.lock()
        taps = max(0, taps - 1)
        tapIdle.broadcast()
        tapIdle.unlock()
    }

    /// What the proxy of one clip gets from the session: mode A — the URL ffmpeg reads the clip from (and the tap's
    /// activity for the stall watchdog); mode B — nothing (the clip's files were read and verified before the SDK).
    public final class Lease {
        public let url: String?
        public let activity: (() -> Int64)?
        private let release: () -> Void
        private let once = NSLock()
        private var done = false

        init(url: String?, activity: (() -> Int64)?, release: @escaping () -> Void) {
            self.url = url
            self.activity = activity
            self.release = release
        }

        /// The render ended (or failed): the tap stops serving the file.
        public func end() {
            once.lock()
            let first = !done
            done = true
            once.unlock()
            if first { release() }
        }

        deinit { end() }
    }

    func fileIndex(_ rel: String) -> Int? { indexOf[rel.lowercased()] }

    /// Before the proxy of `clip` renders from destination `dest`. Mode A: the URL to read it from — only when its copy is
    /// "written" and none of its pages is in memory (Review focus 9; else nil — ffmpeg reads the file itself and the
    /// fill-in verifies it, noting the cache). Mode B: its files are read now straight from the disk (F_NOCACHE), every
    /// block into the verifier; the SDK then reads them from the disk again (ruling B1) — a pre-read without a block for
    /// `stallTimeout` is given up on (the fill-in decides). Nil when the proxy reads another destination or the clip is not
    /// read through the session.
    public func prepare(_ clip: Clip, dest: Int) -> Lease? {
        lock.lock(); let over = finished || stopped; lock.unlock()
        guard !over, dest == primary, primary >= 0 else { return nil }
        switch TapRegistry.mode(for: clip) {
        case .http:
            guard let i = fileIndex(clip.path), isWritten(primary, i), let v = verifier(primary, i) else { return nil }
            let tap: TapFile
            do { tap = try TapFile(path: cardPaths[primary] + "/" + files[i].path, verifier: v) } catch { return nil }
            let srv: RangeServer
            lock.lock()
            if let have = server {
                srv = have
            } else if let made = try? RangeServer() {
                server = made
                srv = made
            } else {
                lock.unlock()
                tap.close()
                return nil
            }
            lock.unlock()
            let url = srv.register(tap, name: (clip.path as NSString).lastPathComponent)
            lock.lock(); tapsServed += 1; lock.unlock()
            tapBusy()
            let counter = verifiedCounters[primary]
            return Lease(url: url, activity: { srv.activity(url) ?? 0 }, release: { [weak self] in
                srv.unregister(url)
                tap.close()
                counter.add(Int(v.verifiedBytes))
                self?.tapDone()
            })
        case .preRead:
            let segments = clip.segments.compactMap(fileIndex).filter { isWritten(primary, $0) }
            guard !segments.isEmpty else { return nil }
            tapBusy()
            let reader = Reader(volume: "pre-read", dests: [primary], work: segments.map { (primary, $0) }, gated: false)
            let t = Thread { [weak self] in
                guard let self else { return }
                for (d, i) in reader.work {
                    if self.isStopped || reader.isAbandoned { break }
                    // Never "verified" from a read that may come from memory: a resident file is left to the fill-in.
                    guard CacheProbe.readsFromDisk(self.cardPaths[d] + "/" + self.files[i].path) else { continue }
                    self.verify(d, i, reader)
                }
                reader.with { reader.done = true }
            }
            t.name = "offload.verify.pre-read"
            reader.thread = t
            t.start()
            var seen: Int64 = -1
            var since = Date()
            while !reader.with({ reader.done }) {
                let now = reader.with { reader.progress }
                if now != seen {
                    seen = now
                    since = Date()
                } else if Date().timeIntervalSince(since) >= stallTimeout || isStopped {
                    reader.with { reader.abandoned = true }   // stops at its next block, or stays in the read it hangs in
                    break
                }
                usleep(50_000)
            }
            tapDone()
            return Lease(url: nil, activity: nil, release: {})
        case .none:
            return nil
        }
    }

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
        tapIdle.lock(); tapIdle.broadcast(); tapIdle.unlock()
    }

    /// The end of the session (the job's stage 2 ended in any way): every reader stops at its next block, the tap closes.
    public func close() {
        stop()
        lock.lock(); let srv = server; server = nil; lock.unlock()
        srv?.close()
    }

    /// After the proxies: the background reads end (waiting while paused), then the notes of spec 10. `pausing`: the
    /// caller's membership of the pause gate — idle while it only waits for the readers. `isCancelled` true: the readers
    /// stop, the copies they did not finish stay "written", `verified` false.
    public func finish(pausing: PauseGate.Worker? = nil, isCancelled: @escaping () -> Bool)
        -> (results: [[DestFileResult]], verified: Bool, notes: [String], throughCache: [Int]) {
        startBackground()   // nothing started yet (no proxies): every copy is read now
        lock.lock(); finished = true; lock.unlock()
        // No proxy reads the primary any more: its volume's reader goes on.
        tapIdle.lock(); taps = 0; tapIdle.broadcast(); tapIdle.unlock()
        // The fill-in (spec 6): on the primary, what the proxies did not read of the files they were meant to read (a clip
        // without a proxy, a failed or cancelled render, a file left to it because it was in memory).
        if primary >= 0 {
            let fill = files.indices.filter { proxyFiles.contains(files[$0].path.lowercased()) && isWritten(primary, $0) }
            if !fill.isEmpty && !isStopped && !isCancelled() {
                startReader(volume: "fill-in", work: fill.map { (primary, $0) }, gated: false)
            }
        }
        lock.lock(); let all = readers; lock.unlock()
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

    func verifiersSnapshot(_ d: Int, _ i: Int) -> BlockVerifier? {
        lock.lock(); defer { lock.unlock() }
        return verifiers[d][i]
    }

    /// The XXH3-64 of block `block` (of `blockSize` bytes) of `path` on the card, read with F_NOCACHE. Only reads.
    public static func readCardBlock(_ path: String, block: Int, blockSize: Int) -> UInt64? {
        guard let fd = try? IO.openRead(path) else { return nil }
        defer { Darwin.close(fd) }
        let buf = IO.allocateAligned(max(blockSize, 4096))
        defer { free(buf) }
        guard let n = try? IO.pread(fd, buf, blockSize, Int64(block) * Int64(blockSize), path: path) else { return nil }
        return BlockHash.xxh3(buf, n)
    }
}
