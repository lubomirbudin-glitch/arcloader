import Darwin
import Foundation

public struct SourceFile {
    public let relPath: String
    public let size: Int64
    public let mtime: timespec
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

    public static func scan(_ root: String) throws -> SourceScan {
        guard let en = FileManager.default.enumerator(atPath: root) else {
            throw IOError("scan", root, ENOENT)
        }
        var files: [SourceFile] = []
        var dirs: [String] = []
        var skipped: [String] = []
        while let rel = en.nextObject() as? String {
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
