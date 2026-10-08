import CommonCrypto
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

/// The checksums a copy computes (spec 3.5, R16): xxh64 always, MD5 only in addition — never MD5 alone, so every
/// backup stays verifiable by older `offload verify` (Mac and Windows).
public struct HashKind: OptionSet, Equatable, Codable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let xxh64 = HashKind(rawValue: 1)
    public static let md5 = HashKind(rawValue: 2)
    public static let both: HashKind = [.xxh64, .md5]
}

public enum HashKinds {
    /// R16: xxh64 is always in.
    public static func normalize(_ kinds: HashKind) -> HashKind { kinds.union(.xxh64) }

    /// The ASC MHL / report names, in that order: ["xxh64"] or ["xxh64", "md5"].
    public static func names(_ kinds: HashKind) -> [String] { normalize(kinds).contains(.md5) ? ["xxh64", "md5"] : ["xxh64"] }

    /// From a report's hash names ("md5" anywhere → both); anything else is xxh64.
    public static func fromNames(_ names: [String]?) -> HashKind {
        (names ?? []).contains { $0.caseInsensitiveCompare("md5") == .orderedSame } ? .both : .xxh64
    }

    /// settings `hash` and CLI --hash: "xxh64" | "both".
    public static func wire(_ kinds: HashKind) -> String { normalize(kinds).contains(.md5) ? "both" : "xxh64" }

    public static func parseWire(_ s: String?) -> HashKind? {
        switch s {
        case "xxh64": return .xxh64
        case "both": return .both
        default: return nil
        }
    }

    /// The setting as the copy uses it: anything this version does not know (a newer value) is xxh64.
    public static func fromSetting(_ s: String?) -> HashKind { parseWire(s) ?? .xxh64 }

    /// For people: "xxHash64", "xxHash64 + MD5".
    public static func label(_ kinds: HashKind) -> String { normalize(kinds).contains(.md5) ? "xxHash64 + MD5" : "xxHash64" }
}

/// The checksums of one file; nil = not computed (the file failed, or MD5 was not asked for).
public struct FileHashes: Equatable {
    public var xxh64: UInt64?
    public var md5: [UInt8]?

    public init(xxh64: UInt64? = nil, md5: [UInt8]? = nil) {
        self.xxh64 = xxh64
        self.md5 = md5
    }

    public var xxh64Hex: String? { xxh64.map(hex64) }
    public var md5Hex: String? { md5.map(hexBytes) }
}

public func hexBytes(_ bytes: [UInt8]) -> String { bytes.map { String(format: "%02x", $0) }.joined() }

/// One running hash of a file, fed block by block (ASC MHL hash types).
public protocol FileHash: AnyObject {
    func update(_ p: UnsafeRawPointer, _ n: Int)
    /// The value as ASC MHL writes it (lower-case hex of the canonical digest; c4 is base58).
    func finish() -> String
}

/// The ASC MHL hash types (R9): xxh64, md5, sha1, xxh3, xxh128, c4. Values are lower-case hex of the canonical
/// (big-endian) digest, as ascmhl writes them; c4 is "c4" + base58(SHA-512) like `MHL.c4`.
public enum HashAlgo {
    public static let names = ["xxh64", "md5", "sha1", "xxh3", "xxh128", "c4"]

    public static func isKnown(_ name: String) -> Bool { names.contains(name) }

    /// nil for a hash type this version does not know.
    public static func create(_ name: String) -> FileHash? {
        switch name {
        case "xxh64": return XXH64Hash()
        case "xxh3": return XXH3Hash()
        case "xxh128": return XXH128Hash()
        case "md5": return DigestHash(.md5)
        case "sha1": return DigestHash(.sha1)
        case "c4": return DigestHash(.sha512, c4: true)
        default: return nil
        }
    }

    /// The whole of `data` at once (tests, small files).
    public static func of(_ name: String, _ data: Data) -> String? {
        guard let h = create(name) else { return nil }
        data.withUnsafeBytes { if let p = $0.baseAddress { h.update(p, data.count) } }
        return h.finish()
    }

    /// Hex compares in any case; a c4 id is base58, where case matters.
    public static func same(_ name: String, _ a: String, _ b: String) -> Bool {
        let x = a.trimmingCharacters(in: .whitespacesAndNewlines), y = b.trimmingCharacters(in: .whitespacesAndNewlines)
        return name == "c4" ? x == y : x.caseInsensitiveCompare(y) == .orderedSame
    }

    /// For people: "xxHash64", "MD5", "SHA-1", "xxHash3", "xxHash128", "C4".
    public static func label(_ name: String) -> String {
        switch name {
        case "xxh64": return "xxHash64"
        case "md5": return "MD5"
        case "sha1": return "SHA-1"
        case "xxh3": return "xxHash3"
        case "xxh128": return "xxHash128"
        case "c4": return "C4"
        default: return name
        }
    }
}

final class XXH64Hash: FileHash {
    private let s = XXH64Stream()
    func update(_ p: UnsafeRawPointer, _ n: Int) { s.update(p, n) }
    func finish() -> String { hex64(s.digest()) }
}

final class XXH3Hash: FileHash {
    private let state: OpaquePointer = XXH3_createState()

    init() { XXH3_64bits_reset(state) }
    deinit { XXH3_freeState(state) }

    func update(_ p: UnsafeRawPointer, _ n: Int) { XXH3_64bits_update(state, p, n) }
    func finish() -> String { hex64(XXH3_64bits_digest(state)) }
}

final class XXH128Hash: FileHash {
    private let state: OpaquePointer = XXH3_createState()

    init() { XXH3_128bits_reset(state) }
    deinit { XXH3_freeState(state) }

    func update(_ p: UnsafeRawPointer, _ n: Int) { XXH3_128bits_update(state, p, n) }

    /// Canonical form: the high 64 bits first, big-endian (XXH128_canonicalFromHash).
    func finish() -> String {
        let h = XXH3_128bits_digest(state)
        return hex64(h.high64) + hex64(h.low64)
    }
}

/// MD5, SHA-1 and SHA-512 (c4) from CommonCrypto. MD5 and SHA-1 are checksums here (ASC MHL "md5", "sha1"), never
/// security — CommonCrypto marks them deprecated, which only means a compiler warning.
final class DigestHash: FileHash {
    enum Kind { case md5, sha1, sha512 }
    private let kind: Kind
    private let c4: Bool
    private var md5 = CC_MD5_CTX()
    private var sha1 = CC_SHA1_CTX()
    private var sha512 = CC_SHA512_CTX()
    private var done = false

    init(_ kind: Kind, c4: Bool = false) {
        self.kind = kind
        self.c4 = c4
        switch kind {
        case .md5: CC_MD5_Init(&md5)
        case .sha1: CC_SHA1_Init(&sha1)
        case .sha512: CC_SHA512_Init(&sha512)
        }
    }

    func update(_ p: UnsafeRawPointer, _ n: Int) {
        var p = p, left = n
        while left > 0 {   // CC_LONG is 32 bits: at most 1 GiB at a time
            let k = min(left, 1 << 30)
            switch kind {
            case .md5: CC_MD5_Update(&md5, p, CC_LONG(k))
            case .sha1: CC_SHA1_Update(&sha1, p, CC_LONG(k))
            case .sha512: CC_SHA512_Update(&sha512, p, CC_LONG(k))
            }
            p += k
            left -= k
        }
    }

    func finish() -> String { c4 ? MHL.c4(sha512: finishBytes()) : hexBytes(finishBytes()) }

    /// The raw digest; the hash cannot be fed or finished again afterwards.
    func finishBytes() -> [UInt8] {
        guard !done else { return [] }
        done = true
        switch kind {
        case .md5:
            var out = [UInt8](repeating: 0, count: Int(CC_MD5_DIGEST_LENGTH))
            CC_MD5_Final(&out, &md5)
            return out
        case .sha1:
            var out = [UInt8](repeating: 0, count: Int(CC_SHA1_DIGEST_LENGTH))
            CC_SHA1_Final(&out, &sha1)
            return out
        case .sha512:
            var out = [UInt8](repeating: 0, count: Int(CC_SHA512_DIGEST_LENGTH))
            CC_SHA512_Final(&out, &sha512)
            return out
        }
    }
}

/// Hashes a whole file straight from the media (no page cache). `isCancelled` is checked before every block.
func hashFile(_ path: String, chunkSize: Int, buffer: UnsafeMutableRawPointer, progress: ((Int) -> Void)? = nil,
              isCancelled: () -> Bool = { false }) throws -> (hash: UInt64, size: Int64) {
    let xx = XXH64Stream()
    let size = try hashFile(path, chunkSize: chunkSize, buffer: buffer, feed: { xx.update($0, $1) },
                            progress: progress, isCancelled: isCancelled)
    return (xx.digest(), size)
}

/// Every hash in `kinds` (xxh64 always) in one read of the file — the read-back verification of a copy (spec 3.5).
func hashFile(_ path: String, kinds: HashKind, chunkSize: Int, buffer: UnsafeMutableRawPointer, progress: ((Int) -> Void)? = nil,
              isCancelled: () -> Bool = { false }) throws -> (hashes: FileHashes, size: Int64) {
    let xx = XXH64Stream()
    let md5 = HashKinds.normalize(kinds).contains(.md5) ? DigestHash(.md5) : nil
    let size = try hashFile(path, chunkSize: chunkSize, buffer: buffer, feed: { p, n in
        xx.update(p, n)
        md5?.update(p, n)
    }, progress: progress, isCancelled: isCancelled)
    return (FileHashes(xxh64: xx.digest(), md5: md5?.finishBytes()), size)
}

/// The one read loop: the file is read once, straight from the media, and every block goes to `feed`. Returns the size.
func hashFile(_ path: String, chunkSize: Int, buffer: UnsafeMutableRawPointer, feed: (UnsafeRawPointer, Int) -> Void,
              progress: ((Int) -> Void)? = nil, isCancelled: () -> Bool = { false }) throws -> Int64 {
    let fd = try IO.openRead(path)
    defer { close(fd) }
    var offset: Int64 = 0
    while true {
        if isCancelled() { throw CancellationError() }
        let n = try IO.pread(fd, buffer, chunkSize, offset, path: path)
        if n == 0 { break }
        feed(buffer, n)
        offset += Int64(n)
        progress?(n)
        if n < chunkSize { break }
    }
    return offset
}
