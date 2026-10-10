import Foundation

/// "Card name from clips" (Windows plan 6: "Název karty z klipů"): suggest the card name from the clip names.
public struct NamingSettings: Codable, Equatable {
    public var suggestFromClips = true
    public init() {}

    /// A missing key keeps its default (a section written by an older or newer version).
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        suggestFromClips = try c.decodeIfPresent(Bool.self, forKey: .suggestFromClips) ?? true
    }
}

/// Outputs for production and editing (Windows plan 7): day report, clip lists.
public struct OutputSettings: Codable, Equatable {
    public var dayReport = true
    public var clipListCsv = true
    public var clipListAle = false
    public init() {}

    /// A missing key keeps its default (a section written by an older or newer version).
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        dayReport = try c.decodeIfPresent(Bool.self, forKey: .dayReport) ?? true
        clipListCsv = try c.decodeIfPresent(Bool.self, forKey: .clipListCsv) ?? true
        clipListAle = try c.decodeIfPresent(Bool.self, forKey: .clipListAle) ?? false
    }
}

/// The app's settings (UserDefaults key "settings", JSON). Same keys as Windows settings.json.
/// `portNames`: the user's names for USB sockets (`UsbConnector.id` → name).
public struct AppSettings: Codable, Equatable {
    public var destRoots: [String] = []
    public var verify = true
    public var analyzeClips = true
    public var proxy = ProxySettings()
    public var tools = ExternalTools()
    public var portNames: [String: String] = [:]
    /// Connected external drives join the destinations by themselves, unplugged ones leave (2026-10-09).
    public var autoDestinations = true
    /// Drives the user removed from the destinations by hand — never added automatically again (identity or name).
    public var ignoredVolumes: [String] = []
    public var naming = NamingSettings()
    /// "xxh64" or "both" (xxh64 + MD5); any other value (a newer version's) is kept and reads as "xxh64".
    public var hash = "xxh64"
    public var outputs = OutputSettings()

    /// The JSON object last read (unknown keys of newer versions); written back under the known keys on save.
    public var raw: [String: Any] = [:]
    /// Sections this version could not read: saved back exactly as they were.
    public var unreadable: Set<String> = []

    enum CodingKeys: String, CodingKey {
        case destRoots, verify, analyzeClips, proxy, tools, portNames, autoDestinations, ignoredVolumes, naming, hash, outputs
    }

    public init() {}

    public static func == (a: AppSettings, b: AppSettings) -> Bool {
        a.destRoots == b.destRoots && a.verify == b.verify && a.analyzeClips == b.analyzeClips && a.proxy == b.proxy
            && a.tools == b.tools && a.portNames == b.portNames && a.autoDestinations == b.autoDestinations
            && a.ignoredVolumes == b.ignoredVolumes && a.naming == b.naming && a.hash == b.hash && a.outputs == b.outputs
    }
}

/// Reading settings never fails and never loses the good parts (port of Windows `SettingsStore.Load`):
/// each top-level section is read on its own; a section that does not parse keeps its defaults.
public enum SettingsStore {
    public static let proxyHeights = [540, 720, 1080, 2160]
    public static let defaultsKey = "settings"

    /// Each known key is read on its own; one of a wrong type keeps its default in memory and goes to `unreadable`
    /// (saved back as it was). A null is the same as a missing key.
    public static func decode(_ data: Data?, catalog: LutCatalog = .shared) -> AppSettings {
        var s = AppSettings()
        guard let data, let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return s }
        s.raw = root
        func read(_ key: String, _ apply: (Any) -> Bool) {
            guard let v = root[key], !(v is NSNull) else { return }
            if !apply(v) { s.unreadable.insert(key) }
        }
        read("destRoots") { v in
            guard let roots = v as? [Any] else { return false }
            s.destRoots = roots.compactMap { $0 as? String }
            return true
        }
        read("verify") { v in bool(v).map { s.verify = $0 } != nil }
        read("analyzeClips") { v in bool(v).map { s.analyzeClips = $0 } != nil }
        read("proxy") { v in section(v, as: ProxySettings.self).map { s.proxy = $0 } != nil }
        read("tools") { v in section(v, as: ExternalTools.self).map { s.tools = $0 } != nil }
        read("portNames") { v in
            guard let names = v as? [String: Any] else { return false }
            s.portNames = names.compactMapValues { $0 as? String }
            return true
        }
        read("autoDestinations") { v in bool(v).map { s.autoDestinations = $0 } != nil }
        read("ignoredVolumes") { v in
            guard let list = v as? [Any] else { return false }
            s.ignoredVolumes = list.compactMap { $0 as? String }
            return true
        }
        read("naming") { v in section(v, as: NamingSettings.self).map { s.naming = $0 } != nil }
        read("hash") { v in
            guard let h = v as? String else { return false }
            s.hash = h.isEmpty ? "xxh64" : h
            return true
        }
        read("outputs") { v in section(v, as: OutputSettings.self).map { s.outputs = $0 } != nil }
        return normalize(s, catalog: catalog)
    }

    /// Sections whose keys of a newer version are kept inside them on save (FORMATY „settings.json“): the keys this
    /// version knows (its properties) come from the encoding — a cleared optional stays cleared —, any other key stays.
    static let mergedSections: [String: Set<String>] = [
        "proxy": keys(of: ProxySettings()), "tools": keys(of: ExternalTools()),
        "naming": keys(of: NamingSettings()), "outputs": keys(of: OutputSettings()),
    ]

    static func keys(of value: Any) -> Set<String> { Set(Mirror(reflecting: value).children.compactMap(\.label)) }

    /// Known keys over the last read JSON: unknown keys (top level and in every section) and unreadable sections stay.
    public static func encode(_ s: AppSettings) -> Data? {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys]
        guard let known = try? JSONSerialization.jsonObject(with: enc.encode(s)) as? [String: Any] else { return nil }
        var out = s.raw
        for (key, value) in known where !s.unreadable.contains(key) {
            if let section = value as? [String: Any], let knownKeys = mergedSections[key], let old = out[key] as? [String: Any] {
                out[key] = old.filter { !knownKeys.contains($0.key) }.merging(section) { _, new in new }
            } else {
                out[key] = value
            }
        }
        return try? JSONSerialization.data(withJSONObject: out, options: [.sortedKeys])
    }

    /// Only a real JSON boolean (not 1, not "yes").
    static func bool(_ v: Any?) -> Bool? {
        guard let n = v as? NSNumber, CFGetTypeID(n) == CFBooleanGetTypeID() else { return nil }
        return n.boolValue
    }

    static func section<T: Decodable>(_ v: Any?, as type: T.Type) -> T? {
        guard let dict = v as? [String: Any], let data = try? JSONSerialization.data(withJSONObject: dict) else { return nil }
        return try? JSONDecoder().decode(T.self, from: data)
    }

    static func normalize(_ input: AppSettings, catalog: LutCatalog = .shared) -> AppSettings {
        var s = input
        var roots: [String] = []
        for r in s.destRoots.map({ $0.trimmingCharacters(in: .whitespaces) }) where !r.isEmpty {
            if !roots.contains(where: { $0.caseInsensitiveCompare(r) == .orderedSame }) { roots.append(r) }
        }
        s.destRoots = roots
        var names: [String: String] = [:]
        for (id, name) in s.portNames {
            let i = id.trimmingCharacters(in: .whitespaces), n = name.trimmingCharacters(in: .whitespaces)
            if !i.isEmpty && !n.isEmpty { names[i] = n }
        }
        s.portNames = names
        if !proxyHeights.contains(s.proxy.height) { s.proxy.height = 1080 }
        s.proxy.bitrateMbps = min(max(s.proxy.bitrateMbps, 2), 50)
        s.proxy.frameScale = s.proxy.frameScale.isFinite ? min(max(s.proxy.frameScale, 0.5), 1.0) : 1.0
        if let a = s.proxy.frameAspect, !a.isFinite || a <= 0 { s.proxy.frameAspect = nil }
        if let l = s.proxy.lutPath, l.trimmingCharacters(in: .whitespaces).isEmpty { s.proxy.lutPath = nil }
        // Settings from before the built-in LUTs: a lutPath stays the user's own LUT, otherwise automatic.
        if s.proxy.lutMode == nil { s.proxy.lutMode = s.proxy.lutPath != nil ? .custom : .auto }
        if !catalog.luts.isEmpty {   // an empty catalog (luts folder missing) must not erase saved choices
            if catalog.find(s.proxy.lutBuiltin) == nil { s.proxy.lutBuiltin = nil }
            if catalog.find(s.proxy.lutFallback) == nil { s.proxy.lutFallback = nil }
            if s.proxy.lutMode == .builtin && s.proxy.lutBuiltin == nil { s.proxy.lutMode = .auto }
        }
        if let f = s.proxy.extraFolder?.trimmingCharacters(in: .whitespaces) { s.proxy.extraFolder = f.isEmpty ? nil : f }
        return s
    }
}

/// Checks before a start that the proxy/thumbnail settings can work.
public enum ProxyPreflight {
    /// The user's own .cube the next start needs (only in custom mode — built-in LUTs ship with the app), or nil.
    public static func lutInUse(_ p: ProxySettings, analyzeClips: Bool) -> String? {
        guard (p.enabled || analyzeClips) && LutResolver.effectiveMode(p) == .custom, let path = p.lutPath,
              !path.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }   // an empty path is no LUT
        return path
    }

    /// Why "Start offload" must stay disabled, or nil. `lutExists` nil = not checked yet (does not block).
    public static func blocking(_ p: ProxySettings, analyzeClips: Bool, lutExists: Bool?) -> String? {
        guard let lut = lutInUse(p, analyzeClips: analyzeClips), lutExists == false else { return nil }
        return "LUT not found: \((lut as NSString).lastPathComponent) — choose another one or remove it."
    }

    /// A warning that does not block (copy and verification still run), or nil.
    public static func warning(_ p: ProxySettings, analyzeClips: Bool, resolved: ExternalTools) -> String? {
        (p.enabled || analyzeClips) && (resolved.ffmpeg == nil || resolved.ffprobe == nil)
            ? "ffmpeg/ffprobe not found — thumbnails and proxies will be skipped, copy and verification will run." : nil
    }

    /// The extra proxy folder the next start copies into (only when proxies are rendered), or nil.
    public static func extraFolderInUse(_ p: ProxySettings) -> String? { p.enabled ? p.extraFolder : nil }

    /// Why the start must wait: the proxy folder is set but missing (a disconnected disk).
    /// `folderExists` nil = not checked yet (does not block). The app passes its background answer.
    public static func extraFolderBlocking(_ p: ProxySettings, folderExists: Bool?) -> String? {
        guard let f = extraFolderInUse(p), folderExists == false else { return nil }
        return "Folder for proxies is not available: \(f)"
    }

    /// The same, asking the disk now — the CLI only (the app never touches a disk on the main thread).
    public static func extraFolderBlocking(_ p: ProxySettings) -> String? {
        extraFolderBlocking(p, folderExists: extraFolderInUse(p).map(isDirectory))
    }

    public static func isDirectory(_ path: String) -> Bool {
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && isDir.boolValue
    }
}
