import Darwin
import Foundation

public struct SourceFile {
    public let relPath: String
    public let size: Int64
    public let mtime: timespec

    /// The modification time as a date (UTC).
    public var modifiedUtc: Date { Date(timeIntervalSince1970: Double(mtime.tv_sec) + Double(mtime.tv_nsec) / 1e9) }
    /// .NET ticks of `mtime`, exactly from the timespec (history `mtimeTicksUtc`, card fingerprint).
    public var ticks: Int64 { FileTimes.ticks(mtime) }
}

extension SourceFile {
    /// A file as a report or the history records it (the date to the nearest microsecond is enough there).
    public init(relPath: String, size: Int64, modified: Date) {
        let t = modified.timeIntervalSince1970
        var sec = t.rounded(.down)
        var nsec = ((t - sec) * 1e9).rounded()
        if nsec >= 1e9 { sec += 1; nsec -= 1e9 }
        self.init(relPath: relPath, size: size, mtime: timespec(tv_sec: Int(sec), tv_nsec: Int(nsec)))
    }
}

public struct SourceScan {
    public let root: String
    public let files: [SourceFile]
    public let dirs: [String]
    public let skipped: [String]
    public var totalBytes: Int64 { files.reduce(0) { $0 + $1.size } }
}

public enum Scanner {
    /// macOS housekeeping that is not camera data.
    static let junkDirs: Set<String> = [".Spotlight-V100", ".fseventsd", ".Trashes", ".TemporaryItems", ".DocumentRevisions-V100"]

    static func isJunkFile(_ name: String) -> Bool {
        name == ".DS_Store" || name.hasPrefix("._")
    }

    /// `isCancelled` is asked every 1 000 entries (a card swapped while it is inspected); then CancellationError.
    public static func scan(_ root: String, isCancelled: () -> Bool = { false }) throws -> SourceScan {
        guard let en = FileManager.default.enumerator(atPath: root) else {
            throw IOError("scan", root, ENOENT)
        }
        var files: [SourceFile] = []
        var dirs: [String] = []
        var skipped: [String] = []
        var seen = 0
        while let rel = en.nextObject() as? String {
            seen += 1
            if seen % 1000 == 0 && isCancelled() { throw CancellationError() }
            let name = (rel as NSString).lastPathComponent
            let full = root + "/" + rel
            var st = stat()
            guard lstat(full, &st) == 0 else { throw IOError("stat", full) }
            switch st.st_mode & S_IFMT {
            case S_IFDIR:
                if junkDirs.contains(name) {
                    en.skipDescendants()
                    skipped.append(rel)
                } else {
                    dirs.append(rel)
                }
            case S_IFREG:
                if isJunkFile(name) {
                    skipped.append(rel)
                } else {
                    files.append(SourceFile(relPath: rel, size: Int64(st.st_size), mtime: st.st_mtimespec))
                }
            default:
                skipped.append(rel)
            }
        }
        files.sort { $0.relPath < $1.relPath }
        dirs.sort()
        return SourceScan(root: root, files: files, dirs: dirs, skipped: skipped)
    }
}
