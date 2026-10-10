import Darwin
import Foundation

/// Measures what the hardware can really do, so the engine settings rest on numbers, not guesses.
public enum Bench {
    public struct Line {
        public let label: String
        public let bytes: Int64
        public let seconds: Double
        public var rate: String { formatRate(bytes, seconds) }
    }

    /// Sequential read of existing files (largest first) at several block sizes, bypassing the page cache.
    /// `isCancelled`: checked before every block — a cancelled read throws `CancellationError`.
    public static func read(path: String, limit: Int64, blockSizes: [Int], isCancelled: () -> Bool = { false }) throws -> [Line] {
        var targets: [(String, Int64)] = []
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir) else { throw IOError("open", path, ENOENT) }
        if isDir.boolValue {
            let scan = try Scanner.scan(path)
            targets = scan.files.sorted { $0.size > $1.size }.map { (path + "/" + $0.relPath, $0.size) }
        } else {
            var st = stat()
            stat(path, &st)
            targets = [(path, Int64(st.st_size))]
        }
        guard !targets.isEmpty else { throw OffloadError.preflight(Msg.noFilesToRead(path), code: .noFilesToRead) }

        var lines: [Line] = []
        for bs in blockSizes {
            let buf = IO.allocateAligned(bs)
            defer { free(buf) }
            var total: Int64 = 0
            let t0 = Date()
            outer: for (file, _) in targets {
                let fd = try IO.openRead(file)
                defer { close(fd) }
                var off: Int64 = 0
                while total < limit {
                    if isCancelled() { throw CancellationError() }
                    let n = try IO.pread(fd, buf, bs, off, path: file)
                    if n == 0 { break }
                    off += Int64(n)
                    total += Int64(n)
                    if n < bs { break }
                }
                if total >= limit { break outer }
            }
            lines.append(Line(label: "read  \(blockLabel(bs))", bytes: total, seconds: Date().timeIntervalSince(t0)))
        }
        return lines
    }

    /// Writes a temporary file (no cache, flushed to the media), reads it back, deletes it — also when cancelled.
    /// `isCancelled`: checked before every block of the write and of the read-back (`CancellationError`).
    public static func write(dir: String, size: Int64, blockSize: Int, isCancelled: () -> Bool = { false }) throws -> [Line] {
        if isCancelled() { throw CancellationError() }
        let path = dir + "/.offload-bench-\(getpid())"
        defer { unlink(path) }
        let buf = IO.allocateAligned(blockSize)
        defer { free(buf) }
        arc4random_buf(buf, blockSize)

        let t0 = Date()
        let wfd = try IO.openWrite(path)
        do {
            IO.preallocate(wfd, size)
            var off: Int64 = 0
            while off < size {
                if isCancelled() { throw CancellationError() }
                let n = Int(min(Int64(blockSize), size - off))
                try IO.pwrite(wfd, buf, n, off, path: path)
                off += Int64(n)
            }
            try IO.fullSync(wfd, path: path)
            close(wfd)
        } catch {
            close(wfd)
            throw error
        }
        let w = Line(label: "write \(blockLabel(blockSize))", bytes: size, seconds: Date().timeIntervalSince(t0))

        let t1 = Date()
        let r = try hashFile(path, chunkSize: blockSize, buffer: buf, isCancelled: isCancelled)
        let rl = Line(label: "read  \(blockLabel(blockSize)) + xxh64", bytes: r.size, seconds: Date().timeIntervalSince(t1))
        return [w, rl]
    }

    /// Raw hashing throughput from memory — must be far above any disk, otherwise it is the bottleneck.
    public static func hash(size: Int) -> Line {
        let buf = IO.allocateAligned(size)
        defer { free(buf) }
        arc4random_buf(buf, size)
        let t0 = Date()
        let h = XXH64Stream()
        for _ in 0..<8 { h.update(buf, size) }
        _ = h.digest()
        return Line(label: "xxh64 in RAM", bytes: Int64(size) * 8, seconds: Date().timeIntervalSince(t0))
    }

    static func blockLabel(_ bs: Int) -> String {
        bs >= 1 << 20 ? "\(bs >> 20) MB" : "\(bs >> 10) KB"
    }
}
