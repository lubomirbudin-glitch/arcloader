import Darwin
import Foundation

/// Reading a destination's copy straight from the disk for a BlockVerifier (Windows `BlockReads`): the fill-in after a
/// proxy (only what the tap did not read), a background read and the classic read-back (all of it, a fresh verifier).
/// Only whole blocks reach the verifier. Spec D.23.1 rule 3: before the read the file's pages in memory are looked at
/// (`mincore`); resident ones are evicted once, and a file that stays resident (or cannot be looked at) is still read —
/// F_NOCACHE, as Offload always read back — but `throughCache` says so: the report notes it and the job is never green
/// on it (Mac counterpart of Windows ruling I3).
public enum BlockReads {
    /// Tests only (internal): called before every block read of the file at the key (its path).
    static var readHooks: [String: () -> Void] = [:]
    private static let hooksLock = NSLock()

    static func hook(_ path: String) -> (() -> Void)? {
        hooksLock.lock(); defer { hooksLock.unlock() }
        return readHooks.isEmpty ? nil : readHooks[path]
    }

    static func setHook(_ path: String, _ f: (() -> Void)?) {
        hooksLock.lock(); defer { hooksLock.unlock() }
        readHooks[path] = f
    }

    /// Reads every block the verifier still lacks, then once more every block that did not match (matching now is an
    /// unstable read, not a failure — the verifier records it); returns the verdict. `beforeBlock`: false stops
    /// (CancellationError), as `isCancelled` does — checked before every block. `progress`: bytes read. IOError when the
    /// file cannot be opened or read (a disk unplugged, the file gone) — never a verdict without the whole read.
    public static func complete(_ path: String, _ v: BlockVerifier, beforeBlock: (() -> Bool)? = nil, isCancelled: () -> Bool,
                                progress: ((Int) -> Void)?) throws -> (outcome: BlockOutcome, throughCache: Bool) {
        // Verified already by this session's reads (the tap, the pre-read): nothing to open again.
        if v.lengthObserved && v.finish() == .verified { return (.verified, false) }
        let fromDisk = CacheProbe.readsFromDisk(path)
        let fd = try IO.openRead(path)
        defer { close(fd) }
        var st = stat()
        guard fstat(fd, &st) == 0 else { throw IOError("stat", path) }
        v.observeLength(Int64(st.st_size))
        let size = v.manifest.blockSize
        let buf = IO.allocateAligned(max(size, 4096))
        defer { free(buf) }
        var read = false
        for pass in 0..<2 {
            for range in v.missingRanges() {
                var off = range.offset
                while off < range.offset + range.length {
                    if isCancelled() || beforeBlock?() == false { throw CancellationError() }
                    hook(path)?()
                    let n = try IO.pread(fd, buf, size, off, path: path)
                    if n <= 0 { break }   // shorter on the disk: observeLength already made it a mismatch
                    v.feed(offset: off, buf, n)
                    read = true
                    progress?(n)
                    off += Int64(size)
                }
            }
            let now = v.finish()
            if pass == 1 { break }
            guard case .mismatch = now else { return (now, read && !fromDisk) }
        }
        return (v.finish(), read && !fromDisk)
    }

    /// The destination's result from a verdict (English, as every file error in the report JSON): a mismatch names the
    /// block and both hashes, a size difference both sizes; `incomplete` leaves the copy "written".
    public static func result(_ v: BlockVerifier, _ o: BlockOutcome) -> DestFileResult {
        switch o {
        case .verified:
            return DestFileResult(state: .verified, error: nil)
        case .sizeDiffers(let disk):
            return DestFileResult(state: .failed, error: "size mismatch: \(disk) != \(v.manifest.size)")
        case .mismatch(let b):
            return DestFileResult(state: .failed,
                                  error: "HASH MISMATCH: block \(b) dest \(hex64(v.diskHash(b) ?? 0)) != source \(hex64(v.manifest.blocks[b]))")
        case .incomplete:
            return DestFileResult(state: .written, error: nil)
        }
    }
}
