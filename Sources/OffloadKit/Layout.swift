import Foundation

/// Destination layout: <root>/<YYYY-MM-DD>/<card name>/
public enum Layout {
    public static let reportDirName = "_REPORT"
    public static let proxyDirName = "_PROXY"

    public static func today() -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: Date())
    }

    /// Volume label when the source is a mounted card, otherwise the folder name.
    public static func defaultCardName(for source: String) -> String {
        let url = URL(fileURLWithPath: source).standardizedFileURL
        if let v = try? url.resourceValues(forKeys: [.volumeNameKey, .volumeURLKey]),
           let volURL = v.volume, volURL.standardizedFileURL.path == url.path,
           let name = v.volumeName, !name.isEmpty {
            return sanitize(name)
        }
        return sanitize(url.lastPathComponent)
    }

    public static func sanitize(_ s: String) -> String {
        let cleaned = s.map { "/:\\".contains($0) ? "_" : $0 }
        let out = String(cleaned).trimmingCharacters(in: .whitespacesAndNewlines)
        return out.isEmpty || out == "." || out == ".." ? "CARD" : out
    }

    /// Card folder name that is free on every destination (NAME, NAME_2, NAME_3 …),
    /// so a re-inserted card never overwrites an earlier offload.
    public static func uniqueCardFolder(roots: [String], date: String, name: String) -> String {
        var candidate = name
        var n = 2
        while roots.contains(where: { FileManager.default.fileExists(atPath: "\($0)/\(date)/\(candidate)") }) {
            candidate = "\(name)_\(n)"
            n += 1
        }
        return candidate
    }
}
