import Darwin
import Foundation

/// Spec D.23.1: on macOS `F_NOCACHE` is only a hint and there is no public API that drops a file from the unified buffer
/// cache. Verify-in-proxy therefore never takes a read as proof unless `mincore` shows that the file had no page in memory
/// when it was read — and only on a Mac where `mincore` sees the cache at all (`blind()`); everything else is read back
/// the classic way, with a note (Review focus 9).
public enum CacheProbe {
    /// Pages of `path` in memory (`mincore` over a read-only shared mapping, in windows of 1 GiB), or nil when it cannot be
    /// told. Mapping a file does not fault its pages in, so the look itself changes nothing. An empty file is (0, 0).
    public static func residentPages(_ path: String) -> (resident: Int, total: Int)? {
        let fd = open(path, O_RDONLY)
        guard fd >= 0 else { return nil }
        defer { close(fd) }
        var st = stat()
        guard fstat(fd, &st) == 0 else { return nil }
        let size = Int(st.st_size), page = Int(getpagesize())
        if size == 0 { return (0, 0) }
        var resident = 0, total = 0, off = 0
        let window = 1 << 30
        while off < size {
            let len = min(window, size - off)
            guard let m = mmap(nil, len, PROT_READ, MAP_SHARED, fd, off_t(off)), m != MAP_FAILED else { return nil }
            defer { munmap(m, len) }
            let pages = (len + page - 1) / page
            var vec = [CChar](repeating: 0, count: pages)
            guard mincore(m, len, &vec) == 0 else { return nil }
            resident += vec.reduce(0) { $0 + (Int32($1) & MINCORE_INCORE != 0 ? 1 : 0) }
            total += pages
            off += len
        }
        return (resident, total)
    }

    /// Whether any page of `path` is in memory; nil when it cannot be told.
    public static func isResident(_ path: String) -> Bool? { residentPages(path).map { $0.resident > 0 } }

    /// Best effort: drops the file's clean cached pages (`msync(MS_INVALIDATE)` on a shared mapping — spec D.23.1; measured
    /// 2026-10-11 on APFS: a file written or read normally has 0 pages resident afterwards). Never proof on its own:
    /// `residentPages` decides.
    @discardableResult
    public static func evict(_ path: String) -> Bool {
        let fd = open(path, O_RDONLY)
        guard fd >= 0 else { return false }
        defer { close(fd) }
        var st = stat()
        guard fstat(fd, &st) == 0 else { return false }
        let size = Int(st.st_size)
        if size == 0 { return true }
        var off = 0, ok = true
        let window = 1 << 30
        while off < size {
            let len = min(window, size - off)
            guard let m = mmap(nil, len, PROT_READ, MAP_SHARED, fd, off_t(off)), m != MAP_FAILED else { return false }
            if msync(m, len, MS_INVALIDATE) != 0 { ok = false }
            munmap(m, len)
            off += len
        }
        return ok
    }

    /// Tests only (internal): replaces `evict` (a test that keeps a file in the cache on purpose).
    static var evictHook: ((String) -> Bool)?

    /// `evict`, or the test's hook.
    static func tryEvict(_ path: String) -> Bool { evictHook?(path) ?? evict(path) }

    /// The file is not in memory now — after one eviction when it was. False when it stays resident or cannot be told:
    /// a read of it may come from memory, so it is never proof (spec D.23.1 rule 3).
    static func readsFromDisk(_ path: String) -> Bool {
        guard let first = isResident(path) else { return false }
        if !first { return true }
        _ = tryEvict(path)
        return isResident(path) == false
    }

    private static let blindOnce: Bool = {
        let p = NSTemporaryDirectory() + ".offload-mincore-\(getpid())-\(UUID().uuidString.prefix(8))"
        defer { unlink(p) }
        guard (try? Data(repeating: 1, count: 1 << 20).write(to: URL(fileURLWithPath: p))) != nil,
              let r = residentPages(p) else { return true }
        return r.resident == 0
    }()

    /// `mincore` does not see cached pages on this Mac (a file just written the normal way shows none): nothing it says is
    /// proof, verify-in-proxy is not used (spec D.27 row 3). Asked once per run of the app.
    public static func blind() -> Bool { blindOnce }

    /// Tests only (internal): the size of the probe file.
    static let probeBytes = 4 << 20

    /// Nil when the destination of `cardPath` may be verified from the reads of the proxies (and of the fill-in); otherwise
    /// why it is read back the classic way (the report's `verifyNote`). Once per job and destination: a network volume
    /// never; a probe `.offload-cachecheck-<pid>-<uuid>` of random data written in `<card>/_REPORT` with F_NOCACHE and
    /// F_FULLFSYNC must leave no page in memory (Windows UnbufferedCheck). The probe is always removed.
    public static func problem(cardPath: String) -> String? {
        if blind() { return VIPMsg.classicBlind }
        if !VolumeFacts.isLocal(cardPath) { return VIPMsg.classicNetwork }
        let dir = cardPath + "/" + Layout.reportDirName
        let probe = dir + "/.offload-cachecheck-\(getpid())-\(UUID().uuidString)"
        defer { unlink(probe) }
        do {
            // The card folder is never made here (a disk gone meanwhile); only its `_REPORT`.
            guard FileManager.default.fileExists(atPath: cardPath) else { throw IOError("open", cardPath, ENOENT) }
            try IO.mkdirs(dir)
            let fd = try IO.openWrite(probe)
            let n = probeBytes
            let buf = IO.allocateAligned(n)
            defer { free(buf) }
            arc4random_buf(buf, n)
            do {
                try IO.pwrite(fd, buf, n, 0, path: probe)
                try IO.fullSync(fd, path: probe)
            } catch {
                close(fd)
                throw error
            }
            close(fd)
            guard let r = residentPages(probe) else { return VIPMsg.classicCacheCheckFailed("mincore") }
            return r.resident == 0 ? nil : VIPMsg.classicCacheCheck
        } catch {
            return VIPMsg.classicCacheCheckFailed("\(error)")
        }
    }
}
