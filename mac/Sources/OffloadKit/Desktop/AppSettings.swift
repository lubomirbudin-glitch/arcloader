import Foundation

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

    public init() {}
}

/// Reading settings never fails and never loses the good parts (port of Windows `SettingsStore.Load`):
/// each top-level section is read on its own; a section that does not parse keeps its defaults.
public enum SettingsStore {
    public static let proxyHeights = [540, 720, 1080, 2160]
    public static let defaultsKey = "settings"

    public static func decode(_ data: Data?, catalog: LutCatalog = .shared) -> AppSettings {
        var s = AppSettings()
        guard let data, let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return s }
        if let roots = root["destRoots"] as? [Any] { s.destRoots = roots.compactMap { $0 as? String } }
        if let v = bool(root["verify"]) { s.verify = v }
        if let v = bool(root["analyzeClips"]) { s.analyzeClips = v }
        if let p = section(root["proxy"], as: ProxySettings.self) { s.proxy = p }
        if let t = section(root["tools"], as: ExternalTools.self) { s.tools = t }
        if let names = root["portNames"] as? [String: Any] { s.portNames = names.compactMapValues { $0 as? String } }
        if let v = bool(root["autoDestinations"]) { s.autoDestinations = v }
        if let v = root["ignoredVolumes"] as? [Any] { s.ignoredVolumes = v.compactMap { $0 as? String } }
        return normalize(s, catalog: catalog)
    }

    public static func encode(_ s: AppSettings) -> Data? { try? JSONEncoder().encode(s) }

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

    /// Why "Spustit offload" must stay disabled, or nil. `lutExists` nil = not checked yet (does not block).
    public static func blocking(_ p: ProxySettings, analyzeClips: Bool, lutExists: Bool?) -> String? {
        guard let lut = lutInUse(p, analyzeClips: analyzeClips), lutExists == false else { return nil }
        return "LUT nenalezen: \((lut as NSString).lastPathComponent) — vyberte jiný nebo ho odeberte."
    }

    /// A warning that does not block (copy and verification still run), or nil.
    public static func warning(_ p: ProxySettings, analyzeClips: Bool, resolved: ExternalTools) -> String? {
        (p.enabled || analyzeClips) && (resolved.ffmpeg == nil || resolved.ffprobe == nil)
            ? "ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou." : nil
    }

    /// The extra proxy folder the next start copies into (only when proxies are rendered), or nil.
    public static func extraFolderInUse(_ p: ProxySettings) -> String? { p.enabled ? p.extraFolder : nil }

    /// Why the start must wait: the proxy folder is set but missing (a disconnected disk).
    /// `folderExists` nil = not checked yet (does not block). The app passes its background answer.
    public static func extraFolderBlocking(_ p: ProxySettings, folderExists: Bool?) -> String? {
        guard let f = extraFolderInUse(p), folderExists == false else { return nil }
        return "Složka pro proxy není dostupná: \(f)"
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
