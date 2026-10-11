import CXXHash
import CryptoKit
import Foundation

/// The checksums a copy computes (spec 3.5 of the Windows design, R16): xxh64 always, MD5 only on top of it.
public struct HashKinds: OptionSet, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue | 1 }   // xxh64 is never left out
    public static let xxh64 = HashKinds(rawValue: 1)
    public static let md5 = HashKinds(rawValue: 3)
    public static let both = HashKinds(rawValue: 3)

    /// Report `hashAlgorithms` / history `hashAlgorithms`: ["xxh64"] or ["xxh64", "md5"].
    public var names: [String] { contains(.md5) ? ["xxh64", "md5"] : ["xxh64"] }

    /// From a report's `hashAlgorithms` (missing = xxh64 only).
    public static func from(names: [String]?) -> HashKinds {
        (names ?? []).contains { $0.lowercased() == "md5" } ? .both : .xxh64
    }

    /// From settings `hash`: "both" = xxh64 + MD5; anything else (also a value of a newer version) = xxh64.
    public static func fromSetting(_ s: String?) -> HashKinds { s == "both" ? .both : .xxh64 }
}

/// What a file's hashes are, as the report and the MHL record them.
public struct FileHashes: Equatable {
    public var xxh64: UInt64?
    public var md5: Data?
    /// XXH3-128, 32 lower-case hex digits, high 64 bits first (canonical form).
    public var xxh128: String?

    public init(xxh64: UInt64? = nil, md5: Data? = nil, xxh128: String? = nil) {
        self.xxh64 = xxh64
        self.md5 = md5
        self.xxh128 = xxh128
    }

    public var xxh64Hex: String? { xxh64.map(hex64) }
    public var md5Hex: String? { md5.map(hexBytes) }
}

func hexBytes(_ d: Data) -> String { d.map { String(format: "%02x", $0) }.joined() }

func bytesFromHex(_ s: String) -> Data? {
    guard s.count % 2 == 0 else { return nil }
    var out = Data(capacity: s.count / 2)
    var i = s.startIndex
    while i < s.endIndex {
        let j = s.index(i, offsetBy: 2)
        guard let b = UInt8(s[i..<j], radix: 16) else { return nil }
        out.append(b)
        i = j
    }
    return out
}

/// Streaming XXH3-128 (ASC MHL "xxh128").
final class XXH128Stream {
    private let state: OpaquePointer

    init() {
        state = XXH3_createState()
        XXH3_128bits_reset(state)
    }

    deinit { XXH3_freeState(state) }

    func update(_ p: UnsafeRawPointer, _ n: Int) { XXH3_128bits_update(state, p, n) }

    func hex() -> String {
        let h = XXH3_128bits_digest(state)
        return String(format: "%016llx%016llx", h.high64, h.low64)
    }
}

/// XXH3-64 of one block (the block manifest, verify-in-proxy).
enum BlockHash {
    static func xxh3(_ p: UnsafeRawPointer, _ n: Int) -> UInt64 { XXH3_64bits(p, n) }
    static func hex(_ v: UInt64) -> String { String(format: "%016llx", v) }
}

/// Every hash of one file in one pass: xxh64, xxh128, MD5 (when asked) and the XXH3-64 of every `blockSize` block.
/// Pieces may be fed in any sizes, in order.
final class MultiHasher {
    private let kinds: HashKinds
    private let blockSize: Int
    private let x64 = XXH64Stream()
    private let x128: XXH128Stream?
    private var md5: Insecure.MD5?
    private var block: UnsafeMutableRawPointer
    private var inBlock = 0
    private(set) var blocks: [UInt64] = []

    /// `xxh128: false` leaves XXH3-128 out (the copy needs only xxh64 and the block hashes).
    init(kinds: HashKinds, blockSize: Int, xxh128: Bool = true) {
        x128 = xxh128 ? XXH128Stream() : nil
        precondition(blockSize > 0)
        self.kinds = kinds
        self.blockSize = blockSize
        block = UnsafeMutableRawPointer.allocate(byteCount: blockSize, alignment: 16)
        if kinds.contains(.md5) { md5 = Insecure.MD5() }
    }

    deinit { block.deallocate() }

    func update(_ p: UnsafeRawPointer, _ n: Int) {
        guard n > 0 else { return }
        x64.update(p, n)
        x128?.update(p, n)
        md5?.update(bufferPointer: UnsafeRawBufferPointer(start: p, count: n))
        var done = 0
        while done < n {
            if inBlock == 0 && n - done >= blockSize {
                blocks.append(BlockHash.xxh3(p + done, blockSize))   // a whole block straight from the caller's buffer
                done += blockSize
                continue
            }
            let take = min(blockSize - inBlock, n - done)
            block.advanced(by: inBlock).copyMemory(from: p + done, byteCount: take)
            inBlock += take
            done += take
            if inBlock == blockSize {
                blocks.append(BlockHash.xxh3(block, blockSize))
                inBlock = 0
            }
        }
    }

    func finish() -> (hashes: FileHashes, blocks: [UInt64]) {
        if inBlock > 0 {
            blocks.append(BlockHash.xxh3(block, inBlock))
            inBlock = 0
        }
        let m = md5.map { Data($0.finalize()) }
        return (FileHashes(xxh64: x64.digest(), md5: m, xxh128: x128?.hex()), blocks)
    }
}

/// One running hash of a file for "Verify backup" (every ASC MHL hash type Windows verifies, R9).
protocol RunningHash: AnyObject {
    func update(_ p: UnsafeRawPointer, _ n: Int)
    func value() -> String
}

public enum HashAlgo {
    public static let known = ["xxh64", "md5", "sha1", "xxh3", "xxh128", "c4"]

    final class X64: RunningHash {
        let s = XXH64Stream()
        func update(_ p: UnsafeRawPointer, _ n: Int) { s.update(p, n) }
        func value() -> String { hex64(s.digest()) }
    }

    final class X3: RunningHash {
        private let state: OpaquePointer = XXH3_createState()
        init() { XXH3_64bits_reset(state) }
        deinit { XXH3_freeState(state) }
        func update(_ p: UnsafeRawPointer, _ n: Int) { XXH3_64bits_update(state, p, n) }
        func value() -> String { BlockHash.hex(XXH3_64bits_digest(state)) }
    }

    final class X128: RunningHash {
        let s = XXH128Stream()
        func update(_ p: UnsafeRawPointer, _ n: Int) { s.update(p, n) }
        func value() -> String { s.hex() }
    }

    final class Kit<H: HashFunction>: RunningHash {
        var h = H()
        let finish: (H.Digest) -> String
        init(_ finish: @escaping (H.Digest) -> String) { self.finish = finish }
        func update(_ p: UnsafeRawPointer, _ n: Int) { h.update(bufferPointer: UnsafeRawBufferPointer(start: p, count: n)) }
        func value() -> String { finish(h.finalize()) }
    }

    static func make(_ name: String) -> RunningHash? {
        switch name.lowercased() {
        case "xxh64": return X64()
        case "xxh3": return X3()
        case "xxh128": return X128()
        case "md5": return Kit<Insecure.MD5> { hexBytes(Data($0)) }
        case "sha1": return Kit<Insecure.SHA1> { hexBytes(Data($0)) }
        case "c4": return Kit<SHA512> { MHL.c4FromSHA512(Array($0)) }
        default: return nil
        }
    }

    /// Hex compares in any case; a c4 id is base58, where case matters.
    public static func equal(_ algorithm: String, _ a: String, _ b: String) -> Bool {
        algorithm.lowercased() == "c4" ? a == b : a.lowercased() == b.lowercased()
    }

    /// For people: "xxHash64", "MD5", "SHA-1", "xxHash3", "xxHash128", "C4".
    public static func label(_ name: String) -> String {
        ["xxh64": "xxHash64", "md5": "MD5", "sha1": "SHA-1", "xxh3": "xxHash3", "xxh128": "xxHash128", "c4": "C4"][name.lowercased()] ?? name
    }
}
