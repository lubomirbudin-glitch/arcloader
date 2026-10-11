import Darwin
import Foundation

/// history.json + history/<id>.files.gz — local to this Mac, never sent anywhere (FORMATY „Místní soubory“). Never
/// throws: a broken history must never stop an offload. Every read-modify-write holds the history lock (flock, task 37).
public final class HistoryStore {
    public static let currentVersion = 1
    public let folder: String
    let lockTimeout: TimeInterval
    let maxEntries: Int
    public var log: ((String) -> Void)?

    public init(folder: String = AppSupport.folder, lockTimeout: TimeInterval = 5, maxEntries: Int = 5000) {
        self.folder = folder
        self.lockTimeout = lockTimeout
        self.maxEntries = maxEntries
    }

    var file: String { folder + "/history.json" }
    /// Always derived from the id, never from the stored `fileList`.
    func listPath(_ id: String) -> String { folder + "/history/" + id + ".files.gz" }

    /// "20261006T081500Z-3f2a9c1b": the end of the copy in UTC and 8 random hex digits.
    public static func newId(finished: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyyMMdd'T'HHmmss'Z'"
        return f.string(from: finished) + "-" + String(format: "%08x", UInt32.random(in: 0...UInt32.max))
    }

    struct Root: Codable {
        var version: Int
        var entries: [HistoryEntry]
    }

    enum Read { case ok([HistoryEntry]), newer([HistoryEntry]), corrupt }

    func read() -> Read {
        guard let data = FileManager.default.contents(atPath: file) else { return .ok([]) }
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        if let v = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["version"] as? Int, v > Self.currentVersion {
            return .newer((try? dec.decode(Root.self, from: data))?.entries ?? [])
        }
        guard let root = try? dec.decode(Root.self, from: data) else { return .corrupt }
        return .ok(root.entries)
    }

    /// Only under the lock: the damaged file keeps its bytes under another name, the history starts empty.
    func setAsideCorrupt() {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyyMMdd-HHmmss"
        let to = file + ".corrupt-" + f.string(from: Date())
        if rename(file, to) == 0 { log?("history: damaged history.json set aside as \(to)") }
    }

    /// Every entry, newest `finished` first.
    public func load() -> [HistoryEntry] {
        guard let held = FileLock.lock(Locks.history(folder), timeout: lockTimeout) else { return peek() }
        defer { held.release() }
        switch read() {
        case .ok(let e), .newer(let e): return e.sorted { $0.finished > $1.finished }
        case .corrupt:
            setAsideCorrupt()
            return []
        }
    }

    /// Without the lock (it stayed busy): what can be read now.
    func peek() -> [HistoryEntry] {
        switch read() {
        case .ok(let e), .newer(let e): return e.sorted { $0.finished > $1.finished }
        case .corrupt: return []
        }
    }

    /// Load, modify and save under ONE hold of the lock. `modify` gets the entry with this id (nil when none) and
    /// returns the entry to store (same id) and optionally its new file list — or nil to change nothing.
    @discardableResult
    public func update(id: String, timeout: TimeInterval? = nil, _ modify: (HistoryEntry?) -> (HistoryEntry, [SourceFile]?)?) -> HistoryWrite {
        guard let held = FileLock.lock(Locks.history(folder), timeout: timeout ?? lockTimeout) else { return .busy }
        defer { held.release() }
        var entries: [HistoryEntry]
        switch read() {
        case .newer:
            log?("history: written by a newer version of \(Product.name) — read only")
            return .failed
        case .corrupt:
            setAsideCorrupt()
            entries = []
        case .ok(let e):
            entries = e
        }
        let index = entries.firstIndex { $0.id == id }
        guard let result = modify(index.map { entries[$0] }), result.0.id == id else { return .unchanged }
        let (entry, files) = result
        if let index { entries[index] = entry } else { entries.append(entry) }
        do {
            if let files { try FileListStore.write(listPath(id), files) }
            entries.sort { $0.finished > $1.finished }
            if entries.count > maxEntries {
                for old in entries[maxEntries...] { unlink(listPath(old.id)) }
                entries = Array(entries[..<maxEntries])
            }
            try save(entries)
            return .written
        } catch {
            log?("history: \(error)")
            return .failed
        }
    }

    @discardableResult
    public func remove(id: String, timeout: TimeInterval? = nil) -> HistoryWrite {
        guard let held = FileLock.lock(Locks.history(folder), timeout: timeout ?? lockTimeout) else { return .busy }
        defer { held.release() }
        guard case .ok(var entries) = read(), let i = entries.firstIndex(where: { $0.id == id }) else { return .unchanged }
        entries.remove(at: i)
        unlink(listPath(id))
        do {
            try save(entries)
            return .written
        } catch {
            log?("history: \(error)")
            return .failed
        }
    }

    /// UTF-8 without BOM, indented, sorted keys, ISO 8601 UTC dates to the second; a temporary name, then a rename.
    func save(_ entries: [HistoryEntry]) throws {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        enc.dateEncodingStrategy = .iso8601
        try IO.mkdirs(folder)
        try IO.writeFileDurably(file, enc.encode(Root(version: Self.currentVersion, entries: entries)))
    }

    public func readFileList(_ e: HistoryEntry) -> [SourceFile]? { FileListStore.read(listPath(e.id)) }

    /// The newest entry this card may be: a volume root by file-system serial + capacity (R1), a folder by its shape.
    public func findForCard(_ key: CardKey?, shape: String, in entries: [HistoryEntry]? = nil) -> HistoryEntry? {
        HistoryStore.findForCard(entries ?? load(), key, shape: shape)
    }

    /// `entries` newest first (as `load` gives them).
    public static func findForCard(_ entries: [HistoryEntry], _ key: CardKey?, shape: String) -> HistoryEntry? {
        entries.first { e in
            if let key { return e.card?.fsSerial == key.fsSerial && e.card?.capacity == key.capacity }
            return e.card == nil && e.shape == shape
        }
    }
}
