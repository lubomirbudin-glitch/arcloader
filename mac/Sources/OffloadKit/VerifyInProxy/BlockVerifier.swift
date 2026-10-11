import Foundation

/// The verdict on one file × destination.
public enum BlockOutcome: Equatable {
    case verified
    /// The first block that does not match.
    case mismatch(block: Int)
    /// The file on the disk has another length.
    case sizeDiffers(onDisk: Int64)
    /// Blocks are still missing (a read cancelled or never done).
    case incomplete
}

/// A byte range of a file, `length` bytes from `offset`.
public struct ByteRange: Equatable {
    public var offset: Int64
    public var length: Int64

    public init(offset: Int64, length: Int64) {
        self.offset = offset
        self.length = length
    }
}

/// Spec 5 (BlockVerifier; Windows `BlockVerifier`): every byte read from a destination — by the proxy tap in any order and
/// with seeks, by the fill-in, by a background read — goes in through `feed`; a block is judged as soon as all its bytes
/// are in. A whole block is hashed straight away; pieces of a block are put together by their offset (at most
/// `maxPartials` blocks at a time — the oldest is dropped and stays missing). A verified block read again whole: another
/// answer makes it a mismatch and "unstable" (a disk that does not read the same twice is an I/O error, never hidden); a
/// piece of a judged block is ignored. A mismatching block that matches when read whole again is verified, but unstable
/// (a note in the report). Bytes past the manifest's size are ignored; `observeLength` with another length = `sizeDiffers`.
/// Thread-safe (the tap reads over several connections).
public final class BlockVerifier {
    public static let maxPartials = 8

    private enum State { case missing, partial, verified, mismatch }

    private final class Partial {
        var data: [UInt8]
        var covered: [(start: Int, end: Int)] = []
        var lastUse = 0
        init(_ n: Int) { data = [UInt8](repeating: 0, count: n) }
    }

    public let manifest: BlockManifest
    private let lock = NSLock()
    private var state: [State]
    private var diskHashes: [UInt64?]
    private var partials: [Int: Partial] = [:]
    private var unstable = Set<Int>()
    private var tick = 0
    private var verified: Int64 = 0
    private var fed: Int64 = 0
    private var sizeOnDisk: Int64?
    private var observed = false

    public init(_ m: BlockManifest) {
        manifest = m
        state = Array(repeating: .missing, count: m.count)
        diskHashes = Array(repeating: nil, count: m.count)
    }

    /// Bytes of verified blocks so far.
    public var verifiedBytes: Int64 { locked { verified } }
    /// Every byte handed in so far, read again or not.
    public var fedBytes: Int64 { locked { fed } }
    /// Blocks that gave two different answers on two reads, in order.
    public var unstableBlocks: [Int] { locked { unstable.sorted() } }
    /// A reader has told the file's length on the disk (an empty file is verified only by that).
    public var lengthObserved: Bool { locked { observed } }

    /// The XXH3-64 the disk gave for a mismatching block, else nil.
    public func diskHash(_ block: Int) -> UInt64? {
        locked { state.indices.contains(block) && state[block] == .mismatch ? diskHashes[block] : nil }
    }

    /// The length of the file on the disk, as a reader found it: another length than the card's is a mismatch.
    public func observeLength(_ length: Int64) {
        locked {
            observed = true
            if length != manifest.size { sizeOnDisk = length }
        }
    }

    public func feed(offset: Int64, _ p: UnsafeRawPointer, _ n: Int) {
        guard offset >= 0, n > 0 else { return }
        locked {
            fed += Int64(n)
            let end = min(offset + Int64(n), manifest.size)
            var at = offset
            while at < end {
                let b = Int(at / Int64(manifest.blockSize))
                let start = manifest.offset(b), blockEnd = start + Int64(manifest.length(b))
                let upTo = min(end, blockEnd)
                let piece = p + Int(at - offset)
                let len = Int(upTo - at)
                if at == start && upTo == blockEnd {
                    whole(b, BlockHash.xxh3(piece, len))
                } else {
                    self.piece(b, Int(at - start), piece, len)
                }
                at = upTo
            }
        }
    }

    /// Under the lock.
    private func whole(_ b: Int, _ got: UInt64) {
        partials[b] = nil
        let same = got == manifest.blocks[b]
        switch state[b] {
        case .verified where !same:
            unstable.insert(b)
            set(b, .mismatch, got)
        case .verified:
            break
        case .mismatch where same:
            unstable.insert(b)
            set(b, .verified, nil)
        case .mismatch:
            diskHashes[b] = got
        default:
            set(b, same ? .verified : .mismatch, same ? nil : got)
        }
    }

    /// Under the lock.
    private func piece(_ b: Int, _ at: Int, _ p: UnsafeRawPointer, _ n: Int) {
        if state[b] == .verified || state[b] == .mismatch { return }   // a whole read decides those
        let part: Partial
        if let have = partials[b] {
            part = have
        } else {
            if partials.count >= BlockVerifier.maxPartials, let oldest = partials.min(by: { $0.value.lastUse < $1.value.lastUse })?.key {
                partials[oldest] = nil
                state[oldest] = .missing
            }
            part = Partial(manifest.length(b))
            partials[b] = part
            state[b] = .partial
        }
        tick += 1
        part.lastUse = tick
        part.data.withUnsafeMutableBytes { dst in _ = memcpy(dst.baseAddress! + at, p, n) }
        part.covered = BlockVerifier.merge(part.covered + [(at, at + n)])
        if part.covered.count == 1, part.covered[0].start == 0, part.covered[0].end == part.data.count {
            let h = part.data.withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, $0.count) }
            whole(b, h)
        }
    }

    static func merge(_ ranges: [(start: Int, end: Int)]) -> [(start: Int, end: Int)] {
        var out: [(start: Int, end: Int)] = []
        for r in ranges.sorted(by: { $0.start < $1.start }) {
            if let last = out.last, r.start <= last.end {
                out[out.count - 1].end = max(last.end, r.end)
            } else {
                out.append(r)
            }
        }
        return out
    }

    /// Under the lock.
    private func set(_ b: Int, _ s: State, _ hash: UInt64?) {
        if state[b] == .verified { verified -= Int64(manifest.length(b)) }
        state[b] = s
        diskHashes[b] = hash
        if s == .verified { verified += Int64(manifest.length(b)) }
    }

    /// The blocks not verified yet (missing, put together only in part, or mismatching), adjacent ones joined — what the
    /// fill-in reads (spec 6: nothing is read whole again).
    public func missingRanges() -> [ByteRange] {
        locked {
            var out: [ByteRange] = []
            for b in state.indices where state[b] != .verified {
                let off = manifest.offset(b), len = Int64(manifest.length(b))
                if let last = out.last, last.offset + last.length == off {
                    out[out.count - 1].length += len
                } else {
                    out.append(ByteRange(offset: off, length: len))
                }
            }
            return out
        }
    }

    /// `sizeDiffers` when a reader found another length; the first mismatching block; `verified` when every block matched;
    /// else `incomplete`.
    public func finish() -> BlockOutcome {
        locked {
            if let other = sizeOnDisk { return .sizeDiffers(onDisk: other) }
            if let b = state.firstIndex(of: .mismatch) { return .mismatch(block: b) }
            return state.allSatisfy { $0 == .verified } ? .verified : .incomplete
        }
    }

    private func locked<T>(_ f: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return f()
    }
}
