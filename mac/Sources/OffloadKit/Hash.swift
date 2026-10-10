import CXXHash
import Darwin
import Foundation

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

/// Hashes a whole file straight from the media (no page cache) with every kind asked for (xxh64 and xxh128 always,
/// MD5 with `.both` — `MultiHasher`). `isCancelled` is checked before every block.
func hashFile(_ path: String, chunkSize: Int, buffer: UnsafeMutableRawPointer, kinds: HashKinds = .xxh64,
              progress: ((Int) -> Void)? = nil, isCancelled: () -> Bool = { false }) throws -> (hashes: FileHashes, size: Int64) {
    let fd = try IO.openRead(path)
    defer { close(fd) }
    let h = MultiHasher(kinds: kinds, blockSize: chunkSize)
    var offset: Int64 = 0
    while true {
        if isCancelled() { throw CancellationError() }
        let n = try IO.pread(fd, buffer, chunkSize, offset, path: path)
        if n == 0 { break }
        h.update(buffer, n)
        offset += Int64(n)
        progress?(n)
    }
    return (h.finish().hashes, offset)
}
