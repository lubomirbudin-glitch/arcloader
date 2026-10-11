import Foundation

/// The card volume at the time of a backup (FORMATY „Místní soubory“). Stored for the history match (R1: `fsSerial` +
/// `capacity`); `identity` is never required to match.
public struct HistoryCard: Codable, Equatable {
    public var identity: String?
    public var fsSerial: String
    public var capacity: Int64
    public var label: String?
    public var filesystem: String?

    public init(identity: String?, fsSerial: String, capacity: Int64, label: String?, filesystem: String?) {
        self.identity = identity; self.fsSerial = fsSerial; self.capacity = capacity; self.label = label
        self.filesystem = filesystem
    }
}

public struct HistoryDestination: Codable, Equatable {
    public var root: String
    public var cardPath: String

    public init(root: String, cardPath: String) { self.root = root; self.cardPath = cardPath }
}

/// One copy into the card folder: full copy (seq 1), append or repair (seq = its ASC MHL generation).
public struct HistoryGeneration: Codable, Equatable {
    public var seq: Int
    public var kind: String          // "full" | "append" | "repair"
    public var finished: Date
    public var files: Int
    public var bytes: Int64
    public var verified: Bool

    public init(seq: Int, kind: String, finished: Date, files: Int, bytes: Int64, verified: Bool) {
        self.seq = seq; self.kind = kind; self.finished = finished; self.files = files; self.bytes = bytes
        self.verified = verified
    }
}

/// One backed-up card in `history.json` — the keys of FORMATY „Místní soubory“, shared with Windows.
public struct HistoryEntry: Codable, Equatable {
    public var id: String
    public var card: HistoryCard?
    public var source: String
    public var cardName: String
    public var date: String
    public var project: String?
    public var finished: Date
    public var success = false
    public var verified = false
    public var failedFiles: Int?
    public var hashAlgorithms = ["xxh64"]
    public var files = 0
    public var bytes: Int64 = 0
    public var fingerprint = ""
    public var shape = ""
    public var fileList: String?
    public var destinations: [HistoryDestination] = []
    public var generations: [HistoryGeneration] = []
    public var reels: [String] = []
    /// A full copy that started and has not ended (R6): its card offers "Finish interrupted backup". Absent when false.
    public var unfinished = false

    public init(id: String, source: String, cardName: String, date: String, finished: Date) {
        self.id = id; self.source = source; self.cardName = cardName; self.date = date; self.finished = finished
    }

    enum CodingKeys: String, CodingKey {
        case id, card, source, cardName, date, project, finished, success, verified, failedFiles, hashAlgorithms, files, bytes
        case fingerprint, shape, fileList, destinations, generations, reels, unfinished
    }

    /// Tolerant: every key but `id` and `finished` may be missing (an older or a newer writer).
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        card = try? c.decodeIfPresent(HistoryCard.self, forKey: .card)
        source = try c.decodeIfPresent(String.self, forKey: .source) ?? ""
        cardName = try c.decodeIfPresent(String.self, forKey: .cardName) ?? ""
        date = try c.decodeIfPresent(String.self, forKey: .date) ?? ""
        project = try c.decodeIfPresent(String.self, forKey: .project)
        finished = try c.decode(Date.self, forKey: .finished)
        success = try c.decodeIfPresent(Bool.self, forKey: .success) ?? false
        verified = try c.decodeIfPresent(Bool.self, forKey: .verified) ?? false
        failedFiles = try c.decodeIfPresent(Int.self, forKey: .failedFiles)
        hashAlgorithms = try c.decodeIfPresent([String].self, forKey: .hashAlgorithms) ?? ["xxh64"]
        files = try c.decodeIfPresent(Int.self, forKey: .files) ?? 0
        bytes = try c.decodeIfPresent(Int64.self, forKey: .bytes) ?? 0
        fingerprint = try c.decodeIfPresent(String.self, forKey: .fingerprint) ?? ""
        shape = try c.decodeIfPresent(String.self, forKey: .shape) ?? ""
        fileList = try c.decodeIfPresent(String.self, forKey: .fileList)
        destinations = try c.decodeIfPresent([HistoryDestination].self, forKey: .destinations) ?? []
        generations = try c.decodeIfPresent([HistoryGeneration].self, forKey: .generations) ?? []
        reels = try c.decodeIfPresent([String].self, forKey: .reels) ?? []
        unfinished = try c.decodeIfPresent(Bool.self, forKey: .unfinished) ?? false
    }

    public func encode(to e: Encoder) throws {
        var c = e.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(card, forKey: .card)          // null for a folder, as Windows
        try c.encode(source, forKey: .source)
        try c.encode(cardName, forKey: .cardName)
        try c.encode(date, forKey: .date)
        try c.encode(project, forKey: .project)
        try c.encode(finished, forKey: .finished)
        try c.encode(success, forKey: .success)
        try c.encode(verified, forKey: .verified)
        if !success { try c.encode(failedFiles ?? 0, forKey: .failedFiles) }
        try c.encode(hashAlgorithms, forKey: .hashAlgorithms)
        try c.encode(files, forKey: .files)
        try c.encode(bytes, forKey: .bytes)
        try c.encode(fingerprint, forKey: .fingerprint)
        try c.encode(shape, forKey: .shape)
        // Always derived from the id: a hand-edited entry must not point anywhere else.
        try c.encode("history/\(id).files.gz", forKey: .fileList)
        try c.encode(destinations, forKey: .destinations)
        try c.encode(generations, forKey: .generations)
        try c.encode(reels, forKey: .reels)
        if unfinished { try c.encode(true, forKey: .unfinished) }
    }
}

public enum HistoryWrite: Equatable { case written, unchanged, busy, failed }
