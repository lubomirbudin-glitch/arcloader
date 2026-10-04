import Darwin
import Foundation

public struct IOError: Error, CustomStringConvertible {
    public let op: String
    public let path: String
    public let code: Int32

    public init(_ op: String, _ path: String, _ code: Int32 = errno) {
        self.op = op
        self.path = path
        self.code = code
    }

    public var description: String {
        "\(op) \(path): \(String(cString: strerror(code))) (errno \(code))"
    }
}

/// Thin wrappers over POSIX I/O. Everything bypasses the unified buffer cache (F_NOCACHE)
/// so that reads hit the media and verification never reads back what is still in RAM.
enum IO {
    static func openRead(_ path: String) throws -> Int32 {
        let fd = open(path, O_RDONLY)
        guard fd >= 0 else { throw IOError("open", path) }
        _ = fcntl(fd, F_NOCACHE, 1)
        return fd
    }

    static func openWrite(_ path: String) throws -> Int32 {
        let fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0o644)
        guard fd >= 0 else { throw IOError("open", path) }
        _ = fcntl(fd, F_NOCACHE, 1)
        return fd
    }

    /// Best effort: contiguous allocation reduces fragmentation on HDDs. Not every filesystem supports it.
    static func preallocate(_ fd: Int32, _ size: Int64) {
        guard size > 0 else { return }
        var store = fstore_t(fst_flags: UInt32(F_ALLOCATECONTIG), fst_posmode: F_PEOFPOSMODE,
                             fst_offset: 0, fst_length: off_t(size), fst_bytesalloc: 0)
        if fcntl(fd, F_PREALLOCATE, &store) == -1 {
            store.fst_flags = UInt32(F_ALLOCATEALL)
            _ = fcntl(fd, F_PREALLOCATE, &store)
        }
    }

    /// Reads until `count` bytes or EOF. Returns bytes read.
    static func pread(_ fd: Int32, _ buf: UnsafeMutableRawPointer, _ count: Int, _ offset: Int64, path: String) throws -> Int {
        var done = 0
        while done < count {
            let n = Darwin.pread(fd, buf + done, count - done, off_t(offset) + off_t(done))
            if n < 0 {
                if errno == EINTR { continue }
                throw IOError("read", path)
            }
            if n == 0 { break }
            done += n
        }
        return done
    }

    static func pwrite(_ fd: Int32, _ buf: UnsafeRawPointer, _ count: Int, _ offset: Int64, path: String) throws {
        var done = 0
        while done < count {
            let n = Darwin.pwrite(fd, buf + done, count - done, off_t(offset) + off_t(done))
            if n < 0 {
                if errno == EINTR { continue }
                throw IOError("write", path)
            }
            done += n
        }
    }

    /// F_FULLFSYNC also flushes the drive's own write cache; plain fsync on macOS does not.
    static func fullSync(_ fd: Int32, path: String) throws {
        if fcntl(fd, F_FULLFSYNC) == -1 {
            guard fsync(fd) == 0 else { throw IOError("fsync", path) }
        }
    }

    /// Write + F_FULLFSYNC, via a temp name so a crash never leaves a half-written report.
    static func writeFileDurably(_ path: String, _ data: Data) throws {
        let tmp = (path as NSString).deletingLastPathComponent + "/." + (path as NSString).lastPathComponent + ".tmp"
        let fd = try openWrite(tmp)
        do {
            if !data.isEmpty {
                try data.withUnsafeBytes { try pwrite(fd, $0.baseAddress!, data.count, 0, path: tmp) }
            }
            try fullSync(fd, path: tmp)
            close(fd)
        } catch {
            close(fd)
            unlink(tmp)
            throw error
        }
        guard rename(tmp, path) == 0 else { throw IOError("rename", path) }
    }

    static func setModificationTime(_ fd: Int32, _ mtime: timespec) {
        var times = [timespec(tv_sec: 0, tv_nsec: Int(UTIME_OMIT)), mtime]
        _ = futimens(fd, &times)
    }

    static func allocateAligned(_ size: Int) -> UnsafeMutableRawPointer {
        var p: UnsafeMutableRawPointer?
        guard posix_memalign(&p, Int(getpagesize()), size) == 0, let p else {
            fatalError("Out of memory allocating \(size) bytes")
        }
        return p
    }

    static func mkdirs(_ path: String) throws {
        var st = stat()
        if stat(path, &st) == 0 {
            guard st.st_mode & S_IFMT == S_IFDIR else { throw IOError("mkdir", path, EEXIST) }
            return
        }
        let parent = (path as NSString).deletingLastPathComponent
        if !parent.isEmpty && parent != path { try mkdirs(parent) }
        if mkdir(path, 0o755) != 0 && errno != EEXIST { throw IOError("mkdir", path) }
    }

    static func device(of path: String) -> dev_t? {
        var st = stat()
        return stat(path, &st) == 0 ? st.st_dev : nil
    }

    static func availableBytes(_ path: String) -> Int64? {
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return nil }
        return Int64(fs.f_bavail) * Int64(fs.f_bsize)
    }

    static func filesystemType(_ path: String) -> String {
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return "?" }
        return withUnsafeBytes(of: fs.f_fstypename) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
    }
}

public func hex64(_ v: UInt64) -> String { String(format: "%016llx", v) }

public func formatBytes(_ b: Int64) -> String {
    let units = ["B", "KB", "MB", "GB", "TB"]
    var v = Double(b)
    var i = 0
    while v >= 1000 && i < units.count - 1 { v /= 1000; i += 1 }
    return i == 0 ? "\(b) B" : String(format: "%.2f %@", v, units[i])
}

public func formatRate(_ bytes: Int64, _ seconds: Double) -> String {
    guard seconds > 0 else { return "-" }
    return String(format: "%.0f MB/s", Double(bytes) / seconds / 1_000_000)
}
