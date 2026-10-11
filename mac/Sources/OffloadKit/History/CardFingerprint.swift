import CryptoKit
import Foundation

enum SHA256Hex {
    static func of(_ d: Data) -> String { SHA256.hash(data: d).map { String(format: "%02x", $0) }.joined() }
}

/// What a card looked like (Windows R2): SHA-256 over "relpath\tsize\tmtimeTicksUtc\n" (fingerprint) and
/// "relpath\tsize\n" (shape), the files sorted ordinally (UTF-16 code units, as .NET `StringComparer.Ordinal`).
public enum CardFingerprint {
    static func ordinal(_ a: String, _ b: String) -> Bool { a.utf16.lexicographicallyPrecedes(b.utf16) }

    public static func compute(_ files: [SourceFile]) -> (fingerprint: String, shape: String) {
        var full = SHA256(), shape = SHA256()
        for f in files.sorted(by: { ordinal($0.relPath, $1.relPath) }) {
            shape.update(data: Data("\(f.relPath)\t\(f.size)\n".utf8))
            full.update(data: Data("\(f.relPath)\t\(f.size)\t\(f.ticks)\n".utf8))
        }
        let hex = { (d: SHA256.Digest) in d.map { String(format: "%02x", $0) }.joined() }
        return (hex(full.finalize()), hex(shape.finalize()))
    }
}

/// gzip (RFC 1952) around Foundation's raw DEFLATE (`NSData.compressed(using: .zlib)` is RFC 1951 without a zlib header).
enum Gzip {
    static let table: [UInt32] = (0..<256).map { n -> UInt32 in
        var c = UInt32(n)
        for _ in 0..<8 { c = c & 1 != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        return c
    }

    static func crc32(_ d: Data) -> UInt32 {
        var c: UInt32 = 0xFFFF_FFFF
        for b in d { c = table[Int((c ^ UInt32(b)) & 0xFF)] ^ (c >> 8) }
        return c ^ 0xFFFF_FFFF
    }

    static func le32(_ v: UInt32) -> Data { Data((0..<4).map { UInt8((v >> (8 * UInt32($0))) & 0xFF) }) }

    /// No flags, no name, mtime 0, OS 255 (unknown) — what `gzip -n` and .NET GZipStream read.
    static func compress(_ d: Data) -> Data {
        let body: Data
        if d.isEmpty {
            body = Data([0x03, 0x00])   // a final empty fixed-Huffman block
        } else {
            body = (try? (d as NSData).compressed(using: .zlib) as Data) ?? Data()
        }
        return Data([0x1F, 0x8B, 0x08, 0, 0, 0, 0, 0, 0, 0xFF]) + body + le32(crc32(d)) + le32(UInt32(truncatingIfNeeded: d.count))
    }

    /// Nil when it is not gzip, the CRC or the length do not match, or the body does not inflate. The optional header
    /// fields (FEXTRA, FNAME, FCOMMENT, FHCRC) are skipped.
    static func decompress(_ g: Data) -> Data? {
        let b = [UInt8](g)
        guard b.count >= 18, b[0] == 0x1F, b[1] == 0x8B, b[2] == 8 else { return nil }
        let flags = b[3]
        var i = 10
        if flags & 4 != 0 {
            guard i + 2 <= b.count else { return nil }
            i += 2 + (Int(b[i]) | Int(b[i + 1]) << 8)
        }
        if flags & 8 != 0 { while i < b.count && b[i] != 0 { i += 1 }; i += 1 }
        if flags & 16 != 0 { while i < b.count && b[i] != 0 { i += 1 }; i += 1 }
        if flags & 2 != 0 { i += 2 }
        guard i <= b.count - 8 else { return nil }
        let crc = UInt32(b[b.count - 8]) | UInt32(b[b.count - 7]) << 8 | UInt32(b[b.count - 6]) << 16 | UInt32(b[b.count - 5]) << 24
        let size = UInt32(b[b.count - 4]) | UInt32(b[b.count - 3]) << 8 | UInt32(b[b.count - 2]) << 16 | UInt32(b[b.count - 1]) << 24
        let body = Data(b[i..<(b.count - 8)])
        let out: Data
        if size == 0 {
            out = Data()
        } else {
            guard let inflated = try? (body as NSData).decompressed(using: .zlib) as Data else { return nil }
            out = inflated
        }
        return crc == crc32(out) && size == UInt32(truncatingIfNeeded: out.count) ? out : nil
    }
}

/// `history/<id>.files.gz`: gzip of UTF-8 lines "relpath\tsize\tmtimeTicksUtc\n" (FORMATY „Místní soubory“).
public enum FileListStore {
    public static func write(_ path: String, _ files: [SourceFile]) throws {
        let text = files.map { "\($0.relPath)\t\($0.size)\t\($0.ticks)\n" }.joined()
        try IO.mkdirs((path as NSString).deletingLastPathComponent)
        try IO.writeFileDurably(path, Gzip.compress(Data(text.utf8)))
    }

    /// Nil when missing or damaged — that only costs the "new files" count, never the history line.
    public static func read(_ path: String) -> [SourceFile]? {
        guard let g = FileManager.default.contents(atPath: path), let d = Gzip.decompress(g) else { return nil }
        var out: [SourceFile] = []
        for line in OffloadReport.listLines(d) {
            let p = line.split(separator: "\t", omittingEmptySubsequences: false)
            guard p.count == 3, let size = Int64(p[1]), let ticks = Int64(p[2]) else { return nil }
            let unix = ticks - 621_355_968_000_000_000
            var sec = unix / 10_000_000, rest = unix % 10_000_000
            if rest < 0 { sec -= 1; rest += 10_000_000 }
            out.append(SourceFile(relPath: String(p[0]), size: size, mtime: timespec(tv_sec: Int(sec), tv_nsec: Int(rest) * 100)))
        }
        return out
    }
}
