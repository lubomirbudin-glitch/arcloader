import Foundation

/// How a card folder's `_PROXY` is laid out (FORMATY „Proxy ve složkách jako na kartě“): `mirror` (Windows from 0.5.1,
/// the Mac after task 54) = the clip's folder on the card is kept, `_PROXY/idle/1.mov`; `flat` (Windows 0.5.0 and older,
/// the Mac before) = every proxy at the top, `_PROXY/1.mov`, `1_2.mov` ….
public enum ProxyTree: Equatable {
    case mirror, flat
}

/// Where a clip's proxy goes (relative to the card folder, with `/`). Cards often repeat file names in different folders
/// (idle/1.mp4, listening/1.mp4): mirrored, each keeps its name, so the editor relinks it by name. A name clash is then
/// possible only inside one folder (A.mov + A.mxf) — the second gets "_2" and `Msg.proxyRenamed`. Port of Windows
/// `ProxyPaths`.
public enum ProxyPaths {
    /// The clip's folder on the card ("" at the root) — the folder of its primary file; for an .ari sequence (whose path
    /// is the sequence folder) its parent, so the proxy sits beside the sequence and is named after it.
    public static func folder(_ clip: Clip) -> String {
        var path = clip.path
        while path.hasSuffix("/") { path.removeLast() }
        guard let slash = path.lastIndex(of: "/") else { return "" }
        return String(path[..<slash])
    }

    /// `_PROXY/<folder>/<name>.mov` (`_PROXY/<name>.mov` for "").
    public static func relative(folder: String, name: String) -> String {
        Layout.proxyDirName + "/" + (folder.isEmpty ? "" : folder + "/") + name + ".mov"
    }

    /// The key of a proxy in `used`: its path below `_PROXY` without `.mov`, lower-cased ("idle/1"; flat "1") — names are
    /// compared regardless of case, as APFS (by default), exFAT and NTFS do.
    static func key(folder: String, name: String) -> String {
        (folder.isEmpty ? name : folder + "/" + name).lowercased()
    }

    /// The proxy of `clip` in this layout: the first free name (`<name>`, `<name>_2` …) in its folder, taken into `used`.
    /// Returns the relative path and the name chosen.
    public static func pick(_ clip: Clip, tree: ProxyTree, used: inout Set<String>) -> (path: String, name: String) {
        let dir = tree == .mirror ? folder(clip) : ""
        var name = clip.name
        var n = 2
        while !used.insert(key(folder: dir, name: name)).inserted {
            name = "\(clip.name)_\(n)"
            n += 1
        }
        return (relative(folder: dir, name: name), name)
    }

    /// The layout of an earlier backup from its clips (every generation's report): flat when a clip from a subfolder of
    /// the card has its proxy at the top of `_PROXY` and none has one in its folder; otherwise (also no proxies at all, or
    /// clips only at the card's root, where both layouts agree) mirror. An append keeps the backup's layout — the proxies
    /// already there are never moved.
    public static func detect(_ earlier: [ClipEntry]) -> ProxyTree {
        var flat = false
        let prefix = Layout.proxyDirName.lowercased() + "/"
        for e in earlier {
            guard let p = e.proxyPath, !folder(e.clip).isEmpty else { continue }
            let below = p.lowercased().hasPrefix(prefix) ? String(p.dropFirst(prefix.count)) : p
            if below.contains("/") { return .mirror }
            flat = true
        }
        return flat ? .flat : .mirror
    }

    /// The layout of the backup in these card folders (their reports, every generation; reads the disk).
    public static func of(_ cardPaths: [String]) -> ProxyTree {
        detect(cardPaths.flatMap { CardRecord.load($0).clips })
    }

    /// Every proxy already in `_PROXY` of these card folders, as `pick`'s keys ("idle/1", "c0001"); temporary `.part`
    /// files and folders that cannot be read are left out.
    public static func existing(_ cardPaths: [String]) -> Set<String> {
        var keys = Set<String>()
        for p in cardPaths {
            // A destination that went away: nothing is rendered or copied there now either.
            guard let en = FileManager.default.enumerator(atPath: p + "/" + Layout.proxyDirName) else { continue }
            while let rel = en.nextObject() as? String {
                guard (rel as NSString).pathExtension.lowercased() == "mov" else { continue }
                keys.insert((rel as NSString).deletingPathExtension.lowercased())
            }
        }
        return keys
    }
}
