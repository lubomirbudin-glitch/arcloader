import CXXHash
import Darwin

/// Streaming XXH64 — the hash most widely accepted by DIT tools and ASC MHL ("xxh64").
final class XXH64Stream {
    private let state: OpaquePointer

    init() {
        state = XXH64_createState()
        XXH64_reset(state, 0)
    }

    deinit { XXH64_freeState(state) }

    func update(_ p: UnsafeRawPointer, _ n: Int) { XXH64_update(state, p, n) }

    func digest() -> UInt64 { XXH64_digest(state) }
}

/// Hashes a whole file straight from the media (no page cache). `isCancelled` is checked before every block.
func hashFile(_ path: String, chunkSize: Int, buffer: UnsafeMutableRawPointer, progress: ((Int) -> Void)? = nil,
              isCancelled: () -> Bool = { false }) throws -> (hash: UInt64, size: Int64) {
    let fd = try IO.openRead(path)
    defer { close(fd) }
    let h = XXH64Stream()
    var offset: Int64 = 0
    while true {
        if isCancelled() { throw CancellationError() }
        let n = try IO.pread(fd, buffer, chunkSize, offset, path: path)
        if n == 0 { break }
        h.update(buffer, n)
        offset += Int64(n)
        progress?(n)
    }
    return (h.digest(), offset)
}
