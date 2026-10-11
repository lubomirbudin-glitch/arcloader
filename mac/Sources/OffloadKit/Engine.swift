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

/// Hashes the source stream. Runs on its own serial queue so hashing never blocks the card read.
final class SourceHasher {
    private let queue = DispatchQueue(label: "offload.hash")
    private var stream: XXH64Stream?
    private(set) var hashes: [UInt64?]

    init(fileCount: Int) { hashes = Array(repeating: nil, count: fileCount) }

    func begin() { queue.async { self.stream = XXH64Stream() } }

    func update(_ c: Chunk) {
        queue.async {
            self.stream?.update(c.buf, c.length)
            c.release()
        }
    }

    func finish(_ i: Int) { queue.async { self.hashes[i] = self.stream?.digest(); self.stream = nil } }
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
    private let queue: DispatchQueue
    private var fd: Int32 = -1
    private var partPath = ""
    private var currentError: String?
    private(set) var results: [DestFileResult]

    init(index: Int, cardPath: String, fileCount: Int, options: OffloadOptions, noReplace: Bool = false,
         beforeRename: ((String) -> Void)? = nil) {
        self.cardPath = cardPath
        self.options = options
        self.noReplace = noReplace
        self.beforeRename = beforeRename
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
                try IO.pwrite(self.fd, c.buf, c.length, c.offset, path: self.partPath)
                self.written.add(c.length)
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
            } catch {
                self.closeAndRemovePart()
                self.results[i] = DestFileResult(state: .failed, error: "\(error)")
            }
        }
    }

    func abort(_ i: Int, reason: String) {
        queue.async {
            self.closeAndRemovePart()
            self.results[i] = DestFileResult(state: .failed, error: reason)
        }
    }

    /// One F_FULLFSYNC flushes the whole drive cache, covering all the small files at once.
    func flushDevice() {
        queue.sync {
            let path = self.cardPath + "/.offload-flush"
            guard let fd = try? IO.openWrite(path) else { return }
            _ = "x".withCString { Darwin.write(fd, $0, 1) }
            try? IO.fullSync(fd, path: path)
            close(fd)
            unlink(path)
        }
    }

    func wait() { queue.sync {} }

    /// Re-reads every written file from the disk and compares it with the source hash.
    func verify(files: [SourceFile], sourceHashes: [UInt64?], isCancelled: () -> Bool) {
        let buf = IO.allocateAligned(options.chunkSize)
        defer { free(buf) }
        for (i, f) in files.enumerated() where results[i].state == .written {
            if isCancelled() { return }
            let path = cardPath + "/" + f.relPath
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
    public let options: OffloadOptions
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

    public init(options: OffloadOptions = OffloadOptions()) { self.options = options }

    /// Same destination given twice: compared after resolving symlinks, without trailing "/", ignoring case
    /// (APFS and HFS+ are case-insensitive by default, like NTFS on Windows).
    static func normalizedRoot(_ path: String) -> String {
        var s = URL(fileURLWithPath: path).resolvingSymlinksInPath().standardizedFileURL.path
        while s.count > 1 && s.hasSuffix("/") { s.removeLast() }
        return s.lowercased()
    }

    /// Scans the card and checks the destinations before a single byte is written.
    public func prepare(source: String, destRoots: [String], cardName: String?, date: String?) throws -> OffloadPlan {
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
        let needed = scan.totalBytes + Int64(scan.files.count) * 64 * 1024 + 64 << 20
        for root in destRoots {
            let free = IO.availableBytes(root) ?? 0
            if free < needed {
                throw OffloadError.preflight(Msg.noSpace(root, total: needed, parts: "cards \(formatBytesShort(needed))", free: free), code: .noSpace)
            }
        }

        let day = date ?? Layout.today()
        let name = Layout.uniqueCardFolder(roots: destRoots, date: day, name: cardName.map(Layout.sanitize) ?? Layout.defaultCardName(for: source))
        return OffloadPlan(source: source, scan: scan, date: day, cardName: name, destRoots: destRoots)
    }

    public func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
    }

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
            errors: errorCount)
    }

    private func setPhase(_ p: ProgressSnapshot.Phase) { lock.lock(); phase = p; lock.unlock() }

    public func run(_ plan: OffloadPlan) throws -> OffloadReport {
        let files = plan.scan.files
        let started = Date()
        for path in plan.cardPaths {
            try IO.mkdirs(path)
            for d in plan.scan.dirs { try IO.mkdirs(path + "/" + d) }
        }
        let ws = plan.cardPaths.enumerated().map {
            DestWriter(index: $0.offset, cardPath: $0.element, fileCount: files.count, options: options,
                       noReplace: plan.mode != .full, beforeRename: testBeforeRename)
        }
        lock.lock(); self.plan = plan; writers = ws; lock.unlock()

        let pool = BufferPool(count: options.bufferCount, size: options.chunkSize)
        let hasher = SourceHasher(fileCount: files.count)
        var sourceErrors: [String?] = Array(repeating: nil, count: files.count)

        // Copy: the card is read exactly once; every block fans out to the hasher and all destinations.
        for (i, f) in files.enumerated() {
            if isCancelled { sourceErrors[i] = "cancelled"; continue }
            options.beforeFileCopy?(i)
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

        setPhase(.flush)
        DispatchQueue.concurrentPerform(iterations: ws.count) { ws[$0].flushDevice() }
        let copyFinished = Date()

        if options.verify && !isCancelled {
            setPhase(.verify)
            let hashes = hasher.hashes
            DispatchQueue.concurrentPerform(iterations: ws.count) { d in
                ws[d].verify(files: files, sourceHashes: hashes, isCancelled: { self.isCancelled })
            }
        }
        let finished = Date()

        setPhase(.report)
        var report = OffloadReport.build(plan: plan, hashes: hasher.hashes, sourceErrors: sourceErrors,
                                         destResults: ws.map(\.results), verified: options.verify && !isCancelled,
                                         started: started, copyFinished: copyFinished, finished: finished)
        report.generation = plan.generation > 1 ? plan.generation : nil
        report.project = plan.project
        var reportErrors: [String] = []
        for path in plan.cardPaths {
            do { try report.write(cardPath: path, includeHashRecords: true) } catch { reportErrors.append("\(error)") }
        }
        setPhase(.done)
        if !reportErrors.isEmpty {
            throw OffloadError.failed(Msg.reportNotWritten(reportErrors.joined(separator: "; ")))
        }
        return report
    }
}
