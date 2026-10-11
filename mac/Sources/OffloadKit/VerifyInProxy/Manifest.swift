import Darwin
import Foundation

/// What the card gave for one file (spec 2, 4.5): its size and the XXH3-64 of every block (`blockSize` bytes, the last one
/// shorter; none for an empty file). What a BlockVerifier checks a destination's copy against.
public struct BlockManifest: Equatable {
    public let size: Int64
    public let blockSize: Int
    public let blocks: [UInt64]

    /// The caller gives blocks that fit (`make` checks); the tests build it directly.
    public init(size: Int64, blockSize: Int, blocks: [UInt64]) {
        self.size = size
        self.blockSize = blockSize
        self.blocks = blocks
    }

    /// Nil when the blocks do not fit the size (a file not read to its end, a manifest line of another block size).
    public static func make(size: Int64, blockSize: Int, blocks: [UInt64]) -> BlockManifest? {
        guard size >= 0, blockSize > 0, blocks.count == countFor(size, blockSize) else { return nil }
        return BlockManifest(size: size, blockSize: blockSize, blocks: blocks)
    }

    /// How many blocks `size` bytes make (0 for an empty file).
    public static func countFor(_ size: Int64, _ blockSize: Int) -> Int { Int((size + Int64(blockSize) - 1) / Int64(blockSize)) }

    public var count: Int { blocks.count }
    public func offset(_ block: Int) -> Int64 { Int64(block) * Int64(blockSize) }
    public func length(_ block: Int) -> Int { Int(min(Int64(blockSize), size - offset(block))) }
}

/// One line of `<card>/_REPORT/offload-manifest.jsonl` (FORMATY „Blokový manifest“; Windows `ManifestRecord`). `file`: a
/// file in its place on this destination, with the card's hashes and the XXH3-64 of every block. `part`: blocks `first` …
/// of a file still being written under its `.offload-part` name — on the medium (Mac: after F_FULLFSYNC of the file) —
/// the checkpoint a later append resumes from. `gen` is the ASC MHL generation the copy wrote.
public struct ManifestRecord: Codable, Equatable {
    public static let fileKind = "file"
    public static let partKind = "part"

    public var v = 1
    public var kind = ManifestRecord.fileKind
    public var gen = 1
    public var path = ""
    public var size: Int64 = 0
    /// UTC to the second (the JSON has no fraction).
    public var modified = Date(timeIntervalSince1970: 0)
    public var xxh64: String?
    public var xxh128: String?
    public var md5: String?
    public var blockSize = 0
    /// `part` only: the index of the first block in `blocks` (not written when 0).
    public var first = 0
    /// XXH3-64 of each block, 16 lower-case hex digits.
    public var blocks: [String] = []

    public init(kind: String = ManifestRecord.fileKind, gen: Int, path: String, size: Int64, modified: Date, xxh64: String? = nil,
                xxh128: String? = nil, md5: String? = nil, blockSize: Int, first: Int = 0, blocks: [String]) {
        self.kind = kind; self.gen = gen; self.path = path; self.size = size
        self.modified = Date(timeIntervalSince1970: floor(modified.timeIntervalSince1970))
        self.xxh64 = xxh64; self.xxh128 = xxh128; self.md5 = md5; self.blockSize = blockSize; self.first = first
        self.blocks = blocks
    }

    enum CodingKeys: String, CodingKey { case v, kind, gen, path, size, modified, xxh64, xxh128, md5, blockSize, first, blocks }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        v = try c.decodeIfPresent(Int.self, forKey: .v) ?? 0
        kind = try c.decodeIfPresent(String.self, forKey: .kind) ?? ManifestRecord.fileKind
        gen = try c.decodeIfPresent(Int.self, forKey: .gen) ?? 1
        path = try c.decodeIfPresent(String.self, forKey: .path) ?? ""
        size = try c.decodeIfPresent(Int64.self, forKey: .size) ?? 0
        modified = try c.decodeIfPresent(Date.self, forKey: .modified) ?? Date(timeIntervalSince1970: 0)
        xxh64 = try c.decodeIfPresent(String.self, forKey: .xxh64)
        xxh128 = try c.decodeIfPresent(String.self, forKey: .xxh128)
        md5 = try c.decodeIfPresent(String.self, forKey: .md5)
        blockSize = try c.decodeIfPresent(Int.self, forKey: .blockSize) ?? 0
        first = try c.decodeIfPresent(Int.self, forKey: .first) ?? 0
        blocks = try c.decodeIfPresent([String].self, forKey: .blocks) ?? []
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(v, forKey: .v); try c.encode(kind, forKey: .kind); try c.encode(gen, forKey: .gen)
        try c.encode(path, forKey: .path); try c.encode(size, forKey: .size); try c.encode(modified, forKey: .modified)
        try c.encodeIfPresent(xxh64, forKey: .xxh64); try c.encodeIfPresent(xxh128, forKey: .xxh128)
        try c.encodeIfPresent(md5, forKey: .md5)
        try c.encode(blockSize, forKey: .blockSize)
        if first != 0 { try c.encode(first, forKey: .first) }
        try c.encode(blocks, forKey: .blocks)
    }

    public static func hex(_ blocks: [UInt64]) -> [String] { blocks.map(BlockHash.hex) }

    /// The blocks as numbers; nil when one is not 16 hex digits.
    public func blockValues() -> [UInt64]? {
        var out: [UInt64] = []
        out.reserveCapacity(blocks.count)
        for b in blocks {
            guard b.count == 16, let v = UInt64(b, radix: 16) else { return nil }
            out.append(v)
        }
        return out
    }

    /// A `file` record as what a destination's copy is checked against; nil when it does not hold together.
    public func toManifest() -> BlockManifest? {
        guard kind == ManifestRecord.fileKind, let values = blockValues() else { return nil }
        return BlockManifest.make(size: size, blockSize: blockSize, blocks: values)
    }

    /// The xxh64 as a number; nil when it is not readable.
    public var xxh64Value: UInt64? {
        guard let x = xxh64, x.count == 16 else { return nil }
        return UInt64(x, radix: 16)
    }
}

/// A checkpoint put together from a file's `part` records: its first `blocks.count` blocks are on the disk.
public struct PartCheckpoint: Equatable {
    public var path: String
    public var size: Int64
    public var modified: Date
    public var blockSize: Int
    public var blocks: [UInt64]
}

/// `<card>/_REPORT/offload-manifest.jsonl` — only ever appended to: lines are written and F_FULLFSYNC'd, nothing in the
/// file is rewritten. A line left half-written by a crash is skipped when read, and the next append starts on a new line.
public enum ManifestFile {
    public static let fileName = "offload-manifest.jsonl"

    public static func path(in cardPath: String) -> String { cardPath + "/" + Layout.reportDirName + "/" + fileName }

    private static let locksLock = NSLock()
    private static var locks: [String: NSLock] = [:]

    private static func lock(for path: String) -> NSLock {
        locksLock.lock(); defer { locksLock.unlock() }
        let key = path.lowercased()
        if let l = locks[key] { return l }
        let l = NSLock()
        locks[key] = l
        return l
    }

    static func line(_ r: ManifestRecord) throws -> Data {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        enc.dateEncodingStrategy = .iso8601
        return try enc.encode(r) + Data("\n".utf8)
    }

    /// Appends records, one line each, with one F_FULLFSYNC (many small files finish together). `_REPORT` is made when
    /// missing — never the card folder itself (a folder gone with its disk is not made anew). Throws IOError.
    public static func append(_ cardPath: String, _ records: [ManifestRecord]) throws {
        guard !records.isEmpty else { return }
        var data = Data()
        for r in records { data += try line(r) }
        guard FileManager.default.fileExists(atPath: cardPath) else { throw IOError("open", cardPath, ENOENT) }
        let dir = cardPath + "/" + Layout.reportDirName
        try IO.mkdirs(dir)
        let p = path(in: cardPath)
        let l = lock(for: p)
        l.lock(); defer { l.unlock() }
        let fd = Darwin.open(p, O_RDWR | O_APPEND | O_CREAT, 0o644)
        guard fd >= 0 else { throw IOError("open", p) }
        defer { close(fd) }
        var st = stat()
        guard fstat(fd, &st) == 0 else { throw IOError("stat", p) }
        if st.st_size > 0 {
            var last: UInt8 = 0
            if Darwin.pread(fd, &last, 1, st.st_size - 1) == 1, last != UInt8(ascii: "\n") {
                data = Data("\n".utf8) + data   // a torn last line stays alone on its line
            }
        }
        try data.withUnsafeBytes { buf in
            var done = 0
            while done < buf.count {
                let n = Darwin.write(fd, buf.baseAddress! + done, buf.count - done)
                if n < 0 {
                    if errno == EINTR { continue }
                    throw IOError("write", p)
                }
                done += n
            }
        }
        try IO.fullSync(fd, path: p)
    }

    /// Every record that can be read, in the order written; none when there is no manifest. Lines that do not parse,
    /// another `v` or an empty `path` are skipped.
    public static func read(_ cardPath: String) -> [ManifestRecord] {
        guard let data = FileManager.default.contents(atPath: path(in: cardPath)) else { return [] }
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        var out: [ManifestRecord] = []
        for line in data.split(separator: UInt8(ascii: "\n"), omittingEmptySubsequences: true) {
            guard let r = try? dec.decode(ManifestRecord.self, from: Data(line)), r.v == 1, !r.path.isEmpty else { continue }
            out.append(r)
        }
        return out
    }

    /// The newest `file` record of every path of generation `generation` (key: the path lower-cased).
    public static func files(_ records: [ManifestRecord], generation: Int) -> [String: ManifestRecord] {
        var out: [String: ManifestRecord] = [:]
        for r in records where r.kind == ManifestRecord.fileKind && r.gen == generation { out[r.path.lowercased()] = r }
        return out
    }

    /// The checkpoint of every path of generation `generation` from its `part` records (key: the path lower-cased): a record
    /// with `first` 0 starts a new attempt, one that continues the blocks so far extends it, a gap is ignored. A path
    /// whose size, time or block size changes starts again with the record that changed it (only when `first` is 0).
    public static func parts(_ records: [ManifestRecord], generation: Int) -> [String: PartCheckpoint] {
        var out: [String: PartCheckpoint] = [:]
        for r in records where r.kind == ManifestRecord.partKind && r.gen == generation && r.blockSize > 0 {
            guard let values = r.blockValues() else { continue }
            let key = r.path.lowercased()
            let had = out[key]
            let same = had.map { $0.size == r.size && $0.modified == r.modified && $0.blockSize == r.blockSize } ?? false
            if r.first == 0 {
                out[key] = PartCheckpoint(path: r.path, size: r.size, modified: r.modified, blockSize: r.blockSize, blocks: values)
            } else if same, var h = had, r.first == h.blocks.count {
                h.blocks += values
                out[key] = h
            }
        }
        return out
    }
}
