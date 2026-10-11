import Foundation

enum JobFiles {
    /// The proxy names (without extension, lower-cased) already under `_PROXY` (any depth) of these card folders — an
    /// append never gives a new proxy a name an earlier one has on any destination.
    static func proxyNames(_ cardPaths: [String]) -> Set<String> {
        var names = Set<String>()
        for p in cardPaths {
            guard let en = FileManager.default.enumerator(atPath: p + "/" + Layout.proxyDirName) else { continue }
            while let rel = en.nextObject() as? String {
                guard (rel as NSString).pathExtension.lowercased() == "mov" else { continue }
                names.insert(((rel as NSString).lastPathComponent as NSString).deletingPathExtension.lowercased())
            }
        }
        return names
    }
}
