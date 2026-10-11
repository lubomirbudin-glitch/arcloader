import Darwin
import Foundation

/// How the proxy's reads of a clip verify its copy (Windows R3, R10, spec 5 "registr kodeků").
public enum TapMode: Equatable {
    /// Not through the proxy: the fill-in reads it the classic way.
    case none
    /// Mode A: ffmpeg decodes it itself — it reads the primary's copy through the tap (RangeServer). MOV, MP4, MXF.
    case http
    /// Mode B: a vendor SDK reads it (BRAW, R3D, ARRIRAW): Offload reads and verifies it first, straight from the disk.
    case preRead
}

public enum TapRegistry {
    static let httpExtensions: Set<String> = ["mov", "mp4", "mxf"]

    public static func mode(for clip: Clip) -> TapMode {
        switch clip.format {
        case .video where httpExtensions.contains((clip.path as NSString).pathExtension.lowercased()): return .http
        case .braw, .r3d, .arriraw: return .preRead
        default: return .none
        }
    }
}

/// The tap refuses a file that may be read from memory (Review focus 9): the fill-in verifies it instead.
struct TapRefused: Error {}

/// Mode A's source (Windows `TapFile`): the primary destination's copy of a clip, read in whole blocks with F_NOCACHE —
/// opened only when `mincore` shows none of its pages in memory (after one eviction), so what it reads comes from the disk;
/// each block read is handed to the verifier once, then served to ffmpeg from a small cache (a seek back to a block just
/// read — moov at the end, an MXF footer — costs no second read). A block that fell out of the cache and is read again
/// goes to the verifier again (another answer is a mismatch, never hidden). The next block is read ahead while ffmpeg
/// decodes the current one (Mac: measured 2026-10-11, a synchronous read of 16 MiB stalled ffmpeg). Thread-safe.
/// `activity` rises with every piece (1 MiB) read from the disk — the proxy's stall watchdog sees a slow disk as alive.
/// `close()` never waits for a read that hangs on the disk: the blocks and the descriptor go once no read uses them.
final class TapFile: TapSource {
    static let cacheBlocks = 6
    static let pieceBytes = 1 << 20

    let path: String
    let length: Int64
    private let fd: Int32
    private let verifier: BlockVerifier
    private let blockSize: Int
    private let blockCount: Int
    /// Guards the cache, the reads in flight and the end; reads wait on it for a block being read ahead.
    private let gate = NSCondition()
    private var cache: [(block: Int, buf: UnsafeMutableRawPointer, length: Int)] = []
    private var inFlight = Set<Int>()
    private var users = 0
    private var disposed = false
    private var released = false
    private let counterLock = NSLock()
    private var fromDisk: Int64 = 0
    private let ahead = DispatchQueue(label: "offload.tap.readahead")

    /// Throws TapRefused when the file is (still) in memory, IOError when it cannot be opened.
    init(path: String, verifier: BlockVerifier) throws {
        guard CacheProbe.readsFromDisk(path) else { throw TapRefused() }
        self.path = path
        self.verifier = verifier
        blockSize = verifier.manifest.blockSize
        fd = try IO.openRead(path)
        var st = stat()
        guard fstat(fd, &st) == 0 else {
            let e = IOError("stat", path)
            Darwin.close(fd)
            throw e
        }
        length = Int64(st.st_size)
        blockCount = BlockManifest.countFor(length, blockSize)
        verifier.observeLength(length)
    }

    deinit { close() }

    /// Bytes read from the disk so far.
    var readFromDisk: Int64 { counterLock.lock(); defer { counterLock.unlock() }; return fromDisk }
    var activity: Int64 { readFromDisk }

    func read(_ offset: Int64, _ buffer: UnsafeMutableRawPointer, _ count: Int) throws -> Int {
        guard offset < length, count > 0 else { return 0 }
        gate.lock()
        if disposed { gate.unlock(); throw CancellationError() }
        users += 1
        defer {
            users -= 1
            gate.unlock()
            releaseIfDone()
        }
        var done = 0
        while done < count && offset + Int64(done) < length {
            if disposed { throw CancellationError() }
            let at = offset + Int64(done)
            let b = Int(at / Int64(blockSize))
            let (buf, n) = try block(b)
            readAhead(b + 1)
            let from = Int(at - Int64(b) * Int64(blockSize))
            if from >= n { break }   // the disk gave less than the file's length (it shrank meanwhile)
            let k = min(n - from, count - done)
            memcpy(buffer + done, buf + from, k)
            done += k
        }
        return done
    }

    /// Under `gate`: block `b` from the cache, after the read ahead of it, or read now.
    private func block(_ b: Int) throws -> (UnsafeMutableRawPointer, Int) {
        while inFlight.contains(b) && !disposed { gate.wait() }
        if disposed { throw CancellationError() }
        if let hit = cache.firstIndex(where: { $0.block == b }) {
            let c = cache.remove(at: hit)
            cache.append(c)   // most recently used last
            return (c.buf, c.length)
        }
        let buf = IO.allocateAligned(max(blockSize, 4096))
        inFlight.insert(b)
        gate.unlock()   // the disk read itself runs without the lock (a read ahead may finish meanwhile)
        let result = Result { try readFromDisk(b, buf) }
        gate.lock()
        inFlight.remove(b)
        gate.broadcast()
        switch result {
        case .failure(let e):
            free(buf)
            throw e
        case .success(let n):
            if disposed { free(buf); throw CancellationError() }
            insert(b, buf, n)
            return (buf, n)
        }
    }

    /// Under `gate`: the next block is read on the read-ahead queue unless it is there or on its way.
    private func readAhead(_ b: Int) {
        guard b < blockCount, !disposed, !inFlight.contains(b), !cache.contains(where: { $0.block == b }) else { return }
        inFlight.insert(b)
        users += 1
        ahead.async { [self] in
            let buf = IO.allocateAligned(max(blockSize, 4096))
            let result = Result { try readFromDisk(b, buf) }
            gate.lock()
            inFlight.remove(b)
            if case .success(let n) = result, !disposed { insert(b, buf, n) } else { free(buf) }
            users -= 1
            gate.broadcast()
            gate.unlock()
            releaseIfDone()
        }
    }

    /// Under `gate`: a block just read joins the cache (the least recently used one leaves).
    private func insert(_ b: Int, _ buf: UnsafeMutableRawPointer, _ n: Int) {
        if cache.count >= TapFile.cacheBlocks { free(cache.removeFirst().buf) }
        cache.append((b, buf, n))
    }

    /// Without `gate`: block `b` from the disk in pieces, then — whole — into the verifier.
    private func readFromDisk(_ b: Int, _ buf: UnsafeMutableRawPointer) throws -> Int {
        let start = Int64(b) * Int64(blockSize)
        var n = 0
        while n < blockSize {
            if isDisposed { throw CancellationError() }   // the lease ended: stop after this piece
            let want = min(TapFile.pieceBytes, blockSize - n)
            let got = try IO.pread(fd, buf + n, want, start + Int64(n), path: path)
            if got <= 0 { break }
            n += got
            counterLock.lock(); fromDisk += Int64(got); counterLock.unlock()
            if got < want { break }   // the end of the file
        }
        // Only a whole read reaches the verifier: every byte of the block (or of the shorter last one).
        verifier.feed(offset: start, buf, n)
        return n
    }

    private var isDisposed: Bool { gate.lock(); defer { gate.unlock() }; return disposed }

    /// Never waits for a read under way (it may hang on the disk).
    func close() {
        gate.lock()
        disposed = true
        gate.broadcast()
        gate.unlock()
        releaseIfDone()
    }

    /// After `close`: once no read uses the file, the blocks are freed and the descriptor closed — once.
    private func releaseIfDone() {
        gate.lock()
        defer { gate.unlock() }
        guard disposed, users == 0, inFlight.isEmpty, !released else { return }
        released = true
        for c in cache { free(c.buf) }
        cache = []
        Darwin.close(fd)
    }
}
