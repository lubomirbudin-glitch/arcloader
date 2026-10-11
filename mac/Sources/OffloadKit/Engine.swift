import Darwin
import Foundation

public struct OffloadOptions {
    /// Read/write block size. Large sequential blocks are what cards and disks like best.
    public var chunkSize = 16 << 20
    /// Blocks in flight. Lets a slower destination lag behind without stalling the card read.
    public var bufferCount = 16
    public var readRetries = 3
    public var verify = true
    /// Files at least this large get F_FULLFSYNC individually; the rest are flushed once at the end.
    public var fullSyncThreshold: Int64 = 256 << 20
    public var allowSameDevice = false
    /// Tests only (internal, reached through @testable): temporary folders share the source's volume.
    var allowSourceVolume = false
    /// Tests only (internal): called on the copy thread before file `i` is copied (a deterministic point to cancel at).
    var beforeFileCopy: ((Int) -> Void)?
    /// Tests only (internal): which card is at the source (nil = none) — asked at the start of the copy and again after
    /// a pause; default `OffloadEngine.cardFingerprint`.
    var cardIdentity: ((String) -> String?)?
    /// Tests only (internal): called on the copy thread after every block read from the card (with the file's index) —
    /// a deterministic point inside a file.
    var afterChunkRead: ((Int) -> Void)?
    /// A `part` checkpoint goes into the block manifest after this many blocks of a file being written (64 × 16 MiB =
    /// 1 GiB) — after an F_FULLFSYNC of the file: F_NOCACHE does not promise the medium (spec D.23.1).
    public var checkpointBlocks = 64
    /// Plan 10 (task 67): stage 1 does not read back — the copies verify in stage 2 with the proxies. The reports are
    /// written with `verifyPending: true`, without the ASC MHL generation and without the hash lists (the commit after the
    /// verification writes them, spec D.22.5).
    public var deferVerify = false

    public init() {}
}

public enum FileState: String, Codable {
    case pending, written, verified, failed
}

public struct DestFileResult {
    public var state: FileState = .pending
    public var error: String?
}

public enum PlanMode: Equatable { case full, append, repair }

public struct OffloadPlan {
    public let source: String
    public let scan: SourceScan
    public let date: String
    public let cardName: String
    public let destRoots: [String]
    public var mode: PlanMode = .full
    /// The ASC MHL generation this copy writes (1 = a full copy).
    public var generation = 1
    public var hashKinds: HashKinds = .xxh64
    /// The project of the report and of history (an append keeps the earlier backup's).
    public var project: String?
    /// Card files no report of the backup records (a repair, a resume of a committed generation): not safe to format.
    public var notInBackup = 0
    /// Files of the earlier backup that were never read back (an append): not safe to format.
    public var unverifiedEarlier = 0
    /// Append/repair: the backup's card folders (the same on every destination); a full copy derives them.
    public var fixedCardPaths: [String]?
    public var cardPaths: [String] { fixedCardPaths ?? destRoots.map { "\($0)/\(date)/\(cardName)" } }

    public init(source: String, scan: SourceScan, date: String, cardName: String, destRoots: [String]) {
        self.source = source
        self.scan = scan
        self.date = date
        self.cardName = cardName
        self.destRoots = destRoots
    }

    /// An append: only the new files (and their folders) into the backup's own card folders, as generation N, with the
    /// checksums and the project of the earlier backup.
    public static func from(_ a: AppendPlan) -> OffloadPlan {
        var p = OffloadPlan(source: a.source, scan: SourceScan(root: a.source, files: a.newFiles, dirs: a.newDirs, skipped: a.fullScan.skipped),
                            date: a.date, cardName: a.cardName, destRoots: a.destRoots)
        p.mode = .append
        p.generation = a.generation
        p.hashKinds = HashKinds.from(names: a.hashAlgorithms)
        p.unverifiedEarlier = a.unverifiedEarlier
        p.project = a.project
        p.fixedCardPaths = a.cardPaths
        return p
    }
}

public struct ProgressSnapshot {
    public enum Phase: String { case copy, flush, verify, report, done }
    public var phase: Phase
    public var totalBytes: Int64
    public var totalFiles: Int
    public var fileIndex: Int
    public var currentFile: String
    public var readBytes: Int64
    public var writtenBytes: [Int64]
    public var verifiedBytes: [Int64]
    public var errors: Int
    /// The files the read-backs read now (relative; what "Pausing (finishing …)" names).
    public var verifyingFiles: [String] = []
}

/// Byte counter shared between the I/O threads and the progress display.
final class Counter {
    private var value: Int64 = 0
    private let lock = NSLock()
    func add(_ n: Int) { lock.lock(); value += Int64(n); lock.unlock() }
    func get() -> Int64 { lock.lock(); defer { lock.unlock() }; return value }
}

/// Fixed set of page-aligned buffers; `take` blocks when all are in flight (backpressure).
final class BufferPool {
    let size: Int
    private var free: [UnsafeMutableRawPointer]
    private let all: [UnsafeMutableRawPointer]
    private let lock = NSLock()
    private let available: DispatchSemaphore

    init(count: Int, size: Int) {
        self.size = size
        all = (0..<count).map { _ in IO.allocateAligned(size) }
        free = all
        available = DispatchSemaphore(value: count)
    }

    deinit { all.forEach { Darwin.free($0) } }

    func take() -> UnsafeMutableRawPointer {
        available.wait()
        lock.lock(); defer { lock.unlock() }
        return free.removeLast()
    }

    func give(_ b: UnsafeMutableRawPointer) {
        lock.lock()
        free.append(b)
        lock.unlock()
        available.signal()
    }
}

/// One block read from the card, shared by the hasher and every writer; returns to the pool when all are done.
final class Chunk {
    let buf: UnsafeMutableRawPointer
    let length: Int
    let offset: Int64
    private var pending: Int
    private let lock = NSLock()
    private let pool: BufferPool

    init(buf: UnsafeMutableRawPointer, length: Int, offset: Int64, consumers: Int, pool: BufferPool) {
        self.buf = buf
        self.length = length
        self.offset = offset
        self.pending = consumers
        self.pool = pool
    }

    func release() {
        lock.lock()
        pending -= 1
        let last = pending == 0
        lock.unlock()
        if last { pool.give(buf) }
    }
}

/// Hashes the source stream in the one read of the card: xxh64 of every file and the XXH3-64 of every `blockSize` block
/// (the block manifest of verify-in-proxy, task 65) — `MultiHasher` on its own serial queue, so hashing never blocks
/// the card read.
final class SourceHasher {
    private let queue = DispatchQueue(label: "offload.hash")
    private let blockSize: Int
    private var stream: MultiHasher?
    private(set) var hashes: [UInt64?]
    /// Per file the XXH3-64 of its blocks as the card gave them; empty when the file was not read to its end.
    /// Read after `wait()`.
    private(set) var blocks: [[UInt64]]

    init(fileCount: Int, blockSize: Int) {
        self.blockSize = blockSize
        hashes = Array(repeating: nil, count: fileCount)
        blocks = Array(repeating: [], count: fileCount)
    }

    func begin() { queue.async { self.stream = MultiHasher(kinds: .xxh64, blockSize: self.blockSize, xxh128: false) } }

    func update(_ c: Chunk) {
        queue.async {
            self.stream?.update(c.buf, c.length)
            c.release()
        }
    }

    func finish(_ i: Int) {
        queue.async {
            if let r = self.stream?.finish() {
                self.hashes[i] = r.hashes.xxh64
                self.blocks[i] = r.blocks
            }
            self.stream = nil
        }
    }

    /// The xxh64 of file `i` once the hasher is past it (a writer asks after `finish(i)` was queued).
    func hash(_ i: Int) -> UInt64? { queue.sync { hashes[i] } }

    func abort() { queue.async { self.stream = nil } }
    func wait() { queue.sync {} }
}

/// Writes the stream to one destination. Each destination has its own serial queue, so disks run in parallel.
final class DestWriter {
    let cardPath: String
    let options: OffloadOptions
    /// An append or a repair writes into an existing card folder: a file that appeared meanwhile is never replaced.
    let noReplace: Bool
    /// Tests only: called right before a finished file is renamed into place (with its final path).
    let beforeRename: ((String) -> Void)?
    let written = Counter()
    let verified = Counter()
    /// The ASC MHL generation the copy writes — what its manifest records say (R6).
    let generation: Int
    /// The card's xxh64 of file `i` (from the hasher) — for the manifest's `file` record.
    var hashOf: ((Int) -> UInt64?)?
    private let queue: DispatchQueue
    private var fd: Int32 = -1
    private var partPath = ""
    private var currentError: String?
    /// XXH3-64 of every block of the file being written, as written (the card's bytes); `checkpointed` of them are in
    /// the manifest already.
    private var blockHashes: [UInt64] = []
    private var checkpointed = 0
    private var current: SourceFile?
    /// `file` records of files only fsync'd: written into the manifest after the next F_FULLFSYNC of the drive.
    private var awaitingFlush: [ManifestRecord] = []
    /// The last `flushDevice` went through (F_FULLFSYNC or fsync).
    private(set) var deviceFlushed = false
    /// Time spent in writes (the destination's own write rate — the primary of verify-in-proxy is the fastest).
    private var busyNanos: UInt64 = 0
    /// Bytes per second while writing; 0 when nothing was written. Read after `wait()`.
    var writeRate: Double { busyNanos > 0 ? Double(written.get()) / (Double(busyNanos) / 1e9) : 0 }
    private(set) var results: [DestFileResult]
    private let verifyingLock = NSLock()
    private var verifyingNow: String?
    /// The file the read-back reads now (relative), nil when none.
    var verifyingFile: String? { verifyingLock.lock(); defer { verifyingLock.unlock() }; return verifyingNow }

    init(index: Int, cardPath: String, fileCount: Int, options: OffloadOptions, noReplace: Bool = false,
         beforeRename: ((String) -> Void)? = nil, generation: Int = 1) {
        self.cardPath = cardPath
        self.options = options
        self.noReplace = noReplace
        self.beforeRename = beforeRename
        self.generation = generation
        queue = DispatchQueue(label: "offload.dest\(index)")
        results = Array(repeating: DestFileResult(), count: fileCount)
    }

    static func partName(_ rel: String) -> String {
        let dir = (rel as NSString).deletingLastPathComponent
        let name = "." + (rel as NSString).lastPathComponent + ".offload-part"
        return dir.isEmpty ? name : dir + "/" + name
    }

    func begin(_ file: SourceFile) {
        queue.async {
            self.currentError = nil
            self.current = file
            self.blockHashes = []
            self.checkpointed = 0
            self.partPath = self.cardPath + "/" + DestWriter.partName(file.relPath)
            do {
                self.fd = try IO.openWrite(self.partPath)
                IO.preallocate(self.fd, file.size)
            } catch {
                self.fd = -1
                self.currentError = "\(error)"
            }
        }
    }

    func write(_ c: Chunk) {
        queue.async {
            defer { c.release() }
            guard self.fd >= 0 else { return }
            do {
                let t0 = DispatchTime.now().uptimeNanoseconds
                try IO.pwrite(self.fd, c.buf, c.length, c.offset, path: self.partPath)
                self.busyNanos += DispatchTime.now().uptimeNanoseconds - t0
                self.written.add(c.length)
                self.blockHashes.append(BlockHash.xxh3(c.buf, c.length))
                if self.blockHashes.count - self.checkpointed >= max(1, self.options.checkpointBlocks) { try self.checkpoint() }
            } catch {
                self.currentError = "\(error)"
                self.closeAndRemovePart()
            }
        }
    }

    func finish(_ i: Int, _ file: SourceFile) {
        queue.async {
            guard self.fd >= 0, self.currentError == nil else {
                self.results[i] = DestFileResult(state: .failed, error: self.currentError ?? "write failed")
                return
            }
            let finalPath = self.cardPath + "/" + file.relPath
            do {
                if file.size >= self.options.fullSyncThreshold {
                    try IO.fullSync(self.fd, path: self.partPath)
                } else if fsync(self.fd) != 0 {
                    throw IOError("fsync", self.partPath)
                }
                IO.setModificationTime(self.fd, file.mtime)
                guard close(self.fd) == 0 else { throw IOError("close", self.partPath) }
                self.fd = -1
                self.beforeRename?(finalPath)
                if self.noReplace {
                    // Never over a file that appeared on the destination meanwhile: it stays, this copy fails.
                    do {
                        try ProxyCopy.renameNoReplace(self.partPath, finalPath)
                    } catch is ProxyCopy.Taken {
                        throw IOError(message: "file appeared meanwhile: \(file.relPath)")
                    }
                } else {
                    guard rename(self.partPath, finalPath) == 0 else { throw IOError("rename", finalPath) }
                }
                self.results[i].state = .written
                self.record(i, file, flushedAlone: file.size >= self.options.fullSyncThreshold)
            } catch {
                self.closeAndRemovePart()
                self.results[i] = DestFileResult(state: .failed, error: "\(error)")
            }
            self.current = nil
        }
    }

    /// R6: the blocks written since the last checkpoint go into the manifest — once F_FULLFSYNC has put them on the medium
    /// (F_NOCACHE alone does not). A sync that fails is a write error; a manifest line that cannot be written only means a
    /// resume starts earlier.
    private func checkpoint() throws {
        guard let f = current else { return }
        try IO.fullSync(fd, path: partPath)
        let record = ManifestRecord(kind: ManifestRecord.partKind, gen: generation, path: f.relPath, size: f.size,
                                    modified: f.modifiedUtc, blockSize: options.chunkSize, first: checkpointed,
                                    blocks: ManifestRecord.hex(Array(blockHashes[checkpointed...])))
        if (try? ManifestFile.append(cardPath, [record])) != nil { checkpointed = blockHashes.count }
    }

    /// The `file` record of a file in its place: now when the file had its own F_FULLFSYNC, else after the next flush of
    /// the drive (its data may still be in the drive's cache). Only with the card's hashes and blocks that fit the file.
    private func record(_ i: Int, _ f: SourceFile, flushedAlone: Bool) {
        guard let x = hashOf?(i), blockHashes.count == BlockManifest.countFor(f.size, options.chunkSize) else { return }
        let r = ManifestRecord(gen: generation, path: f.relPath, size: f.size, modified: f.modifiedUtc, xxh64: hex64(x),
                               blockSize: options.chunkSize, blocks: ManifestRecord.hex(blockHashes))
        if flushedAlone { try? ManifestFile.append(cardPath, [r]) } else { awaitingFlush.append(r) }
    }

    func abort(_ i: Int, reason: String) {
        queue.async {
            self.closeAndRemovePart()
            self.results[i] = DestFileResult(state: .failed, error: reason)
        }
    }

    /// One F_FULLFSYNC flushes the whole drive cache, covering all the small files at once; then the manifest learns the
    /// files that waited for it (none when the flush failed — that data may never have reached the disk).
    func flushDevice() {
        queue.sync {
            let path = self.cardPath + "/.offload-flush"
            self.deviceFlushed = false
            guard let fd = try? IO.openWrite(path) else { return }
            _ = "x".withCString { Darwin.write(fd, $0, 1) }
            self.deviceFlushed = (try? IO.fullSync(fd, path: path)) != nil
            close(fd)
            unlink(path)
            if self.deviceFlushed && !self.awaitingFlush.isEmpty {
                // Best effort: without its record a file is only copied again by a resume, never trusted blindly.
                if (try? ManifestFile.append(self.cardPath, self.awaitingFlush)) != nil { self.awaitingFlush = [] }
            }
        }
    }

    func wait() { queue.sync {} }

    /// Re-reads every written file from the disk and compares it with the source hash. `gate`: the read-back's
    /// membership of the pause gate — it waits there before every file, never inside one (Pause lets the file being
    /// read finish).
    func verify(files: [SourceFile], sourceHashes: [UInt64?], gate: PauseGate.Worker? = nil, isCancelled: () -> Bool) {
        let buf = IO.allocateAligned(options.chunkSize)
        defer { free(buf) }
        for (i, f) in files.enumerated() where results[i].state == .written {
            if isCancelled() { return }
            if let gate, !gate.waitWhilePaused(isCancelled: isCancelled) { return }
            let path = cardPath + "/" + f.relPath
            setVerifying(f.relPath)
            defer { setVerifying(nil) }
            do {
                let r = try hashFile(path, chunkSize: options.chunkSize, buffer: buf) { self.verified.add($0) }
                if r.size != f.size {
                    results[i] = DestFileResult(state: .failed, error: "size mismatch: \(r.size) != \(f.size)")
                } else if r.hashes.xxh64! != sourceHashes[i] {
                    results[i] = DestFileResult(state: .failed, error: "HASH MISMATCH: dest \(hex64(r.hashes.xxh64!)) != source \(sourceHashes[i].map(hex64) ?? "-")")
                } else {
                    results[i].state = .verified
                }
            } catch {
                results[i] = DestFileResult(state: .failed, error: "verify: \(error)")
            }
        }
    }

    private func setVerifying(_ rel: String?) { verifyingLock.lock(); verifyingNow = rel; verifyingLock.unlock() }

    private func closeAndRemovePart() {
        if fd >= 0 { close(fd); fd = -1 }
        if !partPath.isEmpty { unlink(partPath) }
    }
}

public enum OffloadError: Error, CustomStringConvertible {
    case preflight(String, code: PreflightCode = .other)
    case failed(String)

    public var description: String {
        switch self {
        case .preflight(let m, _), .failed(let m): return m
        }
    }

    public var code: PreflightCode? {
        if case .preflight(_, let c) = self { return c }
        return nil
    }

    public var isPreflight: Bool { code != nil }
}

public final class OffloadEngine {
    /// Set before `run` (the job decides `deferVerify` once it has the plan); never while it runs.
    public var options: OffloadOptions
    private let lock = NSLock()
    private var phase: ProgressSnapshot.Phase = .copy
    private var fileIndex = 0
    private var currentFile = ""
    private var errorCount = 0
    private var cancelled = false
    private let readCounter = Counter()
    private var writers: [DestWriter] = []
    private var plan: OffloadPlan?
    /// Tests only: called in DestWriter.finish right before a finished file is renamed into place.
    var testBeforeRename: ((String) -> Void)?
    /// After `run`: per file, the XXH3-64 of its blocks (`chunkSize`) as the card gave them — empty when the file was
    /// not read to its end. The block manifest of verify-in-proxy (task 65) is made from it.
    public private(set) var fileBlocks: [[UInt64]] = []
    /// After `run`: per destination its write rate in the copy (bytes/s) — verify-in-proxy's primary is the fastest.
    public private(set) var writeRates: [Double] = []
    /// The card's identity when the copy started (nil for a folder or an unreadable one) — stage 2 reads a block of the
    /// card again only while it is still there (spec 10).
    public private(set) var cardAtStart: String?
    /// Called once the reports are about to be written with `verifyPending` (the job takes the card folders' lock then —
    /// the folders exist, ruling I4).
    public var beforePendingReports: (() -> Void)?
    /// Tests only: the free space of a path (nil = ask the volume).
    var testFreeSpace: ((String) -> Int64?)?
    /// "Pause": the copy and the read-back stop before their next file (the one in progress is finished on every
    /// destination); the job's stage 2 waits at the same gate between clips.
    public let pauses = PauseGate()
    /// The card was not the copied one after a pause: the remaining files were left out (`cardChangedWhilePaused`).
    public private(set) var cardChanged = false
    /// The file error (report JSON, English like every file error) of a file left out because the card at the source was
    /// not the copied one after a pause (Windows ruling I1).
    public static let cardChangedWhilePaused = "card changed while paused"
    /// How many times more the card is identified, 250 ms apart, before it counts as changed — a reader that answers
    /// late once after a pause must not fail the job.
    static let identifyRetries = 3
    /// Space warnings of the last `prepare` / `checkSpace` (less than 5 % would be left) — never blocking.
    public private(set) var spaceWarnings: [String] = []
    /// What every volume needs and has free (the CLI's --dry-run).
    public private(set) var spaceLines: [SpaceLine] = []

    public init(options: OffloadOptions = OffloadOptions()) { self.options = options }

    /// Same destination given twice: compared after resolving symlinks, without trailing "/", ignoring case
    /// (APFS and HFS+ are case-insensitive by default, like NTFS on Windows).
    static func normalizedRoot(_ path: String) -> String {
        var s = URL(fileURLWithPath: path).resolvingSymlinksInPath().standardizedFileURL.path
        while s.count > 1 && s.hasSuffix("/") { s.removeLast() }
        return s.lowercased()
    }

    /// Scans the card and checks the destinations before a single byte is written.
    /// `extra`: what the job needs besides the card on the destinations (proxies, report) — from the scan.
    public func prepare(source: String, destRoots: [String], cardName: String?, date: String?,
                        extra: ((SourceScan) -> SpaceExtra?)? = nil) throws -> OffloadPlan {
        guard options.chunkSize > 0, options.chunkSize % 4096 == 0, options.chunkSize <= 1 << 30 else {
            throw OffloadError.preflight(Msg.chunkSize(options.chunkSize), code: .chunkSize)
        }
        guard options.bufferCount >= 2 else {
            throw OffloadError.preflight(Msg.bufferCount(options.bufferCount), code: .bufferCount)
        }
        // \z, not $: "$" would also accept a trailing newline.
        if let date, date.range(of: #"^\d{4}-\d{2}-\d{2}\z"#, options: .regularExpression) == nil {
            throw OffloadError.preflight(Msg.date(date), code: .date)
        }
        let fm = FileManager.default
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: source, isDirectory: &isDir), isDir.boolValue else {
            throw OffloadError.preflight(Msg.sourceNotFound(source), code: .sourceNotFound)
        }
        guard !destRoots.isEmpty else { throw OffloadError.preflight(Msg.noDestination, code: .noDestination) }
        var seen: [String] = []
        for root in destRoots {
            let n = OffloadEngine.normalizedRoot(root)
            if seen.contains(n) { throw OffloadError.preflight(Msg.duplicateDestination(root), code: .duplicateDestination) }
            seen.append(n)
        }
        let top = (try? fm.contentsOfDirectory(atPath: source)) ?? []
        for marker in [Layout.reportDirName, Layout.proxyDirName, MHL.dirName] {
            let found = top.contains { name in
                var d: ObjCBool = false
                return name.caseInsensitiveCompare(marker) == .orderedSame
                    && fm.fileExists(atPath: source + "/" + name, isDirectory: &d) && d.boolValue
            }
            if found {
                throw OffloadError.preflight(Msg.sourceIsOffload, code: .sourceIsOffload)
            }
        }
        guard let srcDev = IO.device(of: source) else { throw OffloadError.preflight(Msg.sourceUnreadable(source), code: .sourceUnreadable) }

        var devices: [dev_t] = []
        for root in destRoots {
            guard fm.fileExists(atPath: root, isDirectory: &isDir), isDir.boolValue else {
                throw OffloadError.preflight(Msg.destinationNotFound(root), code: .destinationNotFound)
            }
            guard fm.isWritableFile(atPath: root) else { throw OffloadError.preflight(Msg.destinationNotWritable(root), code: .destinationNotWritable) }
            guard let dev = IO.device(of: root) else { throw OffloadError.preflight(Msg.destinationUnreadable(root), code: .destinationUnreadable) }
            if dev == srcDev && !options.allowSourceVolume {
                throw OffloadError.preflight(Msg.sameVolumeAsSource(root), code: .sameVolumeAsSource)
            }
            if devices.contains(dev) && !options.allowSameDevice {
                throw OffloadError.preflight(Msg.sharedVolume(root), code: .sharedVolume)
            }
            devices.append(dev)
        }

        let scan = try Scanner.scan(source)
        guard !scan.files.isEmpty else { throw OffloadError.preflight(Msg.emptySource, code: .emptySource) }
        try checkSpace(destRoots, card: SpaceEstimator.cardBytes(scan.totalBytes, files: scan.files.count), extra: extra?(scan))

        let day = date ?? Layout.today()
        let name = Layout.uniqueCardFolder(roots: destRoots, date: day, name: cardName.map(Layout.sanitize) ?? Layout.defaultCardName(for: source))
        return OffloadPlan(source: source, scan: scan, date: day, cardName: name, destRoots: destRoots)
    }

    /// Spec 2.5 per volume (two destinations on one volume add up; the proxy folder is a place of its own): blocking →
    /// `noSpace` before anything is written; less than 5 % left → `spaceWarnings`. Unknown free space counts as 0 here —
    /// the engine is the last word.
    public func checkSpace(_ roots: [String], card: Int64, extra: SpaceExtra?) throws {
        let places = SpacePlanner.places(roots, extra: extra, card: card, label: HistoryText.diskName)
        let (problems, lines) = SpacePlanner.checkPlaces(places, freeOf: { self.testFreeSpace?($0) ?? IO.availableBytes($0) })
        spaceLines = lines
        spaceWarnings = problems.filter { $0.severity == .warning }.map(\.text)
        if let block = problems.first(where: { $0.severity == .blocking }) {
            throw OffloadError.preflight(block.text, code: .noSpace)
        }
    }

    public func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
    }

    /// The XXH3-64 block hashes of file `i` of the last run (empty when it was not read to its end).
    public func blocks(_ i: Int) -> [UInt64] { fileBlocks.indices.contains(i) ? fileBlocks[i] : [] }

    private var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return cancelled }

    public func snapshot() -> ProgressSnapshot {
        lock.lock(); defer { lock.unlock() }
        return ProgressSnapshot(
            phase: phase,
            totalBytes: plan?.scan.totalBytes ?? 0,
            totalFiles: plan?.scan.files.count ?? 0,
            fileIndex: fileIndex,
            currentFile: currentFile,
            readBytes: readCounter.get(),
            writtenBytes: writers.map { $0.written.get() },
            verifiedBytes: writers.map { $0.verified.get() },
            errors: errorCount,
            verifyingFiles: phase == .verify ? writers.compactMap(\.verifyingFile) : [])
    }

    private func setPhase(_ p: ProgressSnapshot.Phase) { lock.lock(); phase = p; lock.unlock() }

    /// Which card is at `source`: the volume identity (`CardIdentity`) and, for a volume root, its file system serial and
    /// capacity (`CardVolume`). Nil when nothing readable is there. Touches the card — never on the main thread.
    public static func cardFingerprint(_ source: String) -> String? {
        guard let id = CardIdentity.of(source) else { return nil }
        guard let v = CardVolume.of(source) else { return id }
        return "\(id)|\(v.fsSerial)|\(v.capacity)"
    }

    /// Is the card at the source still `want`? A different or missing answer is asked again `identifyRetries` times,
    /// 250 ms apart.
    private func stillSameCard(_ source: String, _ want: String, _ identify: (String) -> String?) -> Bool {
        for attempt in 0...OffloadEngine.identifyRetries {
            if identify(source) == want { return true }
            if attempt < OffloadEngine.identifyRetries && !isCancelled { usleep(250_000) }
        }
        return false
    }

    /// Pause, before the next file (user requirement 2026-10-09, Windows 0.8.0): the file just read from the card is
    /// finished on EVERY destination first — the writers empty their queues (no `.offload-part` stays open), the drives'
    /// caches are flushed (a durable checkpoint) — and only then the copy waits at the gate. False = cancelled.
    private func holdBetweenFiles(_ worker: PauseGate.Worker, _ ws: [DestWriter]) -> Bool {
        guard pauses.isPaused else { return !isCancelled }
        ws.forEach { $0.wait() }
        if pauses.isPaused && !isCancelled {
            DispatchQueue.concurrentPerform(iterations: ws.count) { ws[$0].flushDevice() }
        }
        return worker.waitWhilePaused(isCancelled: { self.isCancelled })
    }

    /// Seconds paused, to 0.1 s; nil when the job never stood.
    static func pausedField(_ t: TimeInterval) -> Double? {
        let r = (t * 10).rounded() / 10
        return r > 0 ? r : nil
    }

    public func run(_ plan: OffloadPlan) throws -> OffloadReport {
        let files = plan.scan.files
        let started = Date()
        for path in plan.cardPaths {
            try IO.mkdirs(path)
            for d in plan.scan.dirs { try IO.mkdirs(path + "/" + d) }
        }
        let ws = plan.cardPaths.enumerated().map {
            DestWriter(index: $0.offset, cardPath: $0.element, fileCount: files.count, options: options,
                       noReplace: plan.mode != .full, beforeRename: testBeforeRename, generation: plan.generation)
        }
        lock.lock(); self.plan = plan; writers = ws; lock.unlock()

        let pool = BufferPool(count: options.bufferCount, size: options.chunkSize)
        let hasher = SourceHasher(fileCount: files.count, blockSize: options.chunkSize)
        for w in ws { w.hashOf = { hasher.hash($0) } }
        var sourceErrors: [String?] = Array(repeating: nil, count: files.count)
        let pausedBefore = pauses.pausedTotal
        var requestsChecked = pauses.requests
        // Ruling I1: the card is identified now and again after any pause (it may have been swapped or pulled meanwhile —
        // the same names, sizes and times are no proof). Nil = nothing to compare with: never asked again.
        let identify = options.cardIdentity ?? OffloadEngine.cardFingerprint
        let cardAtStart = identify(plan.source)
        self.cardAtStart = cardAtStart
        // The copy is one worker of the pause gate: it stops only between files, once every destination has the file.
        let copying = pauses.work()

        // Copy: the card is read exactly once; every block fans out to the hasher and all destinations.
        for (i, f) in files.enumerated() {
            if isCancelled { sourceErrors[i] = "cancelled"; continue }
            if !cardChanged {
                options.beforeFileCopy?(i)
                if !holdBetweenFiles(copying, ws) { sourceErrors[i] = "cancelled"; continue }
                // Pause was pressed since the last file (whether the copy stood or was resumed while it still finished its
                // file): the card is identified again before anything more is read from it.
                if pauses.requests != requestsChecked {
                    requestsChecked = pauses.requests
                    if let want = cardAtStart, !stillSameCard(plan.source, want, identify) { cardChanged = true }
                }
            }
            if cardChanged {
                sourceErrors[i] = OffloadEngine.cardChangedWhilePaused
                lock.lock(); errorCount += 1; lock.unlock()
                continue
            }
            lock.lock(); fileIndex = i; currentFile = f.relPath; lock.unlock()
            let src = plan.source + "/" + f.relPath
            ws.forEach { $0.begin(f) }
            hasher.begin()
            var failure: String?
            var offset: Int64 = 0
            var fd: Int32 = -1
            do { fd = try IO.openRead(src) } catch { failure = "\(error)" }
            while failure == nil && !isCancelled {
                let buf = pool.take()
                var n = 0
                var attempt = 0
                while true {
                    do {
                        n = try IO.pread(fd, buf, options.chunkSize, offset, path: src)
                        break
                    } catch {
                        attempt += 1
                        if attempt > options.readRetries { failure = "\(error) (after \(options.readRetries) retries)"; break }
                        usleep(250_000)
                    }
                }
                if failure != nil || n == 0 { pool.give(buf); break }
                let chunk = Chunk(buf: buf, length: n, offset: offset, consumers: ws.count + 1, pool: pool)
                hasher.update(chunk)
                ws.forEach { $0.write(chunk) }
                offset += Int64(n)
                readCounter.add(n)
                options.afterChunkRead?(i)
                if n < options.chunkSize { break }
            }
            if fd >= 0 { close(fd) }
            if failure == nil && isCancelled { failure = "cancelled" }
            if failure == nil && offset != f.size { failure = "source size changed during copy (\(offset) != \(f.size))" }
            if let failure {
                sourceErrors[i] = failure
                hasher.abort()
                ws.forEach { $0.abort(i, reason: "source: \(failure)") }
                lock.lock(); errorCount += 1; lock.unlock()
            } else {
                hasher.finish(i)
                ws.forEach { $0.finish(i, f) }
            }
        }
        hasher.wait()
        ws.forEach { $0.wait() }
        copying.leave()   // past the last file: a pause pressed now stands before the read-back's first file (or in stage 2)
        fileBlocks = hasher.blocks
        writeRates = ws.map(\.writeRate)

        setPhase(.flush)
        DispatchQueue.concurrentPerform(iterations: ws.count) { ws[$0].flushDevice() }
        let copyFinished = Date()
        let copyPaused = pauses.pausedTotal - pausedBefore

        // Task 67: the read-back waits for stage 2 (the proxies read the copies) — unless the copy was cancelled.
        let deferring = options.verify && options.deferVerify && !isCancelled
        if options.verify && !deferring && !isCancelled {
            setPhase(.verify)
            let hashes = hasher.hashes
            // Every destination's read-back joins the gate before any starts (the job never looks standing while one of
            // them has yet to begin); each stops only between files.
            let readers = ws.map { _ in pauses.work() }
            DispatchQueue.concurrentPerform(iterations: ws.count) { d in
                ws[d].verify(files: files, sourceHashes: hashes, gate: readers[d], isCancelled: { self.isCancelled })
                readers[d].leave()
            }
        }
        let finished = Date()
        let verifyPaused = pauses.pausedTotal - pausedBefore - copyPaused

        setPhase(.report)
        var report = OffloadReport.build(plan: plan, hashes: hasher.hashes, sourceErrors: sourceErrors,
                                         destResults: ws.map(\.results), verified: options.verify && !deferring && !isCancelled,
                                         started: started, copyFinished: copyFinished, finished: finished)
        report.generation = plan.generation > 1 ? plan.generation : nil
        report.project = plan.project
        report.copyPausedSeconds = OffloadEngine.pausedField(copyPaused)
        report.verifyPausedSeconds = OffloadEngine.pausedField(verifyPaused)
        if deferring { report.verifyPending = true }
        var reportErrors: [String] = []
        if deferring {
            // Spec D.22.5: the reports say verifyPending — no ASC MHL generation, no hash lists until the commit. A later
            // generation's reports are only created new (an earlier one is never replaced).
            beforePendingReports?()
            for path in plan.cardPaths {
                do { try report.write(cardPath: path, includeHashRecords: false, createNew: plan.generation > 1) } catch { reportErrors.append("\(error)") }
            }
        } else {
            for path in plan.cardPaths {
                do { try report.write(cardPath: path, includeHashRecords: true) } catch { reportErrors.append("\(error)") }
            }
        }
        setPhase(.done)
        if !reportErrors.isEmpty {
            throw OffloadError.failed(Msg.reportNotWritten(reportErrors.joined(separator: "; ")))
        }
        return report
    }
}
