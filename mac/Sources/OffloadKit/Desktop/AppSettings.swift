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

    public init() {}
}

/// Reading settings never fails and never loses the good parts (port of Windows `SettingsStore.Load`):
/// each top-level section is read on its own; a section that does not parse keeps its defaults.
public enum SettingsStore {
    public static let proxyHeights = [540, 720, 1080, 2160]
    public static let defaultsKey = "settings"

    public static func decode(_ data: Data?) -> AppSettings {
        var s = AppSettings()
        guard let data, let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return s }
        if let roots = root["destRoots"] as? [Any] { s.destRoots = roots.compactMap { $0 as? String } }
        if let v = bool(root["verify"]) { s.verify = v }
        if let v = bool(root["analyzeClips"]) { s.analyzeClips = v }
        if let p = section(root["proxy"], as: ProxySettings.self) { s.proxy = p }
        if let t = section(root["tools"], as: ExternalTools.self) { s.tools = t }
        if let names = root["portNames"] as? [String: Any] { s.portNames = names.compactMapValues { $0 as? String } }
        return normalize(s)
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

    static func normalize(_ input: AppSettings) -> AppSettings {
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
        return s
    }
}

/// Checks before a start that the proxy/thumbnail settings can work.
public enum ProxyPreflight {
    /// The LUT the next start needs (proxies or thumbnails use it), or nil.
    public static func lutInUse(_ p: ProxySettings, analyzeClips: Bool) -> String? {
        (p.enabled || analyzeClips) ? p.lutPath : nil
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
}
