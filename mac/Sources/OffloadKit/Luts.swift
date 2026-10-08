import Foundation

/// One built-in LUT from shared/luts/luts.json. `path` is filled in when the catalog is loaded.
public struct BuiltinLut: Codable, Equatable {
    public var id: String
    public var name: String
    public var maker: String
    public var file: String
    public var curve: String
    public var gamut: String
    /// Full path of the .cube (not in luts.json).
    public var path = ""

    enum CodingKeys: String, CodingKey { case id, name, maker, file, curve, gamut }

    /// Lenient like Windows: a missing field is empty; an entry without id or file is dropped by `LutCatalog.load`.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func text(_ k: CodingKeys) -> String { (try? c.decodeIfPresent(String.self, forKey: k)) ?? "" }
        id = text(.id)
        name = text(.name)
        maker = text(.maker)
        file = text(.file)
        curve = text(.curve)
        gamut = text(.gamut)
    }
}

/// The built-in LUTs shipped in Offload.app/Contents/Resources/luts or, in a development build, the repo's shared/luts.
public struct LutCatalog {
    public let folder: String?
    public let luts: [BuiltinLut]

    public init(folder: String?, luts: [BuiltinLut]) {
        self.folder = folder
        self.luts = luts
    }

    public func find(_ id: String?) -> BuiltinLut? {
        guard let id else { return nil }
        return luts.first { $0.id == id }
    }

    /// Loaded once, on first use (a few small local files in the app bundle). Never fails: no folder → empty catalog.
    public static let shared = LutCatalog.load(defaultFolder).withUserOverrides()

    /// The user's own file for a camera log wins over the built-in approximation — e.g. the official ARRI LUT saved as
    /// `~/Library/Application Support/Offload/luts/arri-logc4-awg4.cube` (vendor LUTs may not be bundled with the app,
    /// but anyone may use them on their own Mac). Applied to the app's/CLI's catalog only, never in `load`.
    public func withUserOverrides(_ folder: String = LutCatalog.userLutFolder) -> LutCatalog {
        let luts = self.luts.map { l -> BuiltinLut in
            var l = l
            let own = folder + "/" + l.id + ".cube"
            if FileManager.default.fileExists(atPath: own), LutCatalog.isValidCube(own) {
                l.path = own
                l.name += " (vlastní soubor)"
            }
            return l
        }
        return LutCatalog(folder: self.folder, luts: luts)
    }

    public static var defaultFolder: String? {
        findFolder(executable: Bundle.main.executableURL?.resolvingSymlinksInPath(), resourcePath: Bundle.main.resourcePath)
    }

    /// The app bundle's Resources/luts; next to the executable (luts/, or ../luts/ for the CLI in Contents/Resources/bin);
    /// and only for a SwiftPM build (the executable under ".build/") the repo's shared/luts — an installed copy never
    /// picks up a repository it happens to sit in.
    static func findFolder(executable: URL?, resourcePath: String?) -> String? {
        func ok(_ dir: String) -> Bool { FileManager.default.fileExists(atPath: dir + "/luts.json") }
        var candidates: [String] = []
        if let resourcePath { candidates.append(resourcePath + "/luts") }
        if let executable {
            let dir = executable.deletingLastPathComponent()
            candidates.append(dir.appendingPathComponent("luts").path)
            candidates.append(dir.deletingLastPathComponent().appendingPathComponent("luts").path)
        }
        if let hit = candidates.first(where: ok) { return hit }
        guard let executable, executable.path.contains("/.build/") else { return nil }
        var d = executable.deletingLastPathComponent()
        for _ in 0..<8 {
            d = d.deletingLastPathComponent()
            let repo = d.appendingPathComponent("shared/luts").path
            if ok(repo) { return repo }
        }
        return nil
    }

    /// Never throws: a missing or broken folder gives an empty catalog; entries whose file is missing are left out.
    public static func load(_ folder: String?) -> LutCatalog {
        guard let folder, let data = FileManager.default.contents(atPath: folder + "/luts.json"),
              let list = try? JSONDecoder().decode([BuiltinLut].self, from: data) else {
            return LutCatalog(folder: nil, luts: [])
        }
        let ok = list
            .filter { !$0.id.trimmingCharacters(in: .whitespaces).isEmpty && !$0.file.trimmingCharacters(in: .whitespaces).isEmpty }
            .map { l -> BuiltinLut in
                var l = l
                l.path = folder + "/" + l.file
                return l
            }
            .filter { FileManager.default.fileExists(atPath: $0.path) }
        return LutCatalog(folder: ok.isEmpty ? nil : folder, luts: ok)
    }

    /// `~/Library/Application Support/Offload/luts` — `<id>.cube` there replaces the built-in LUT with that id.
    public static var userLutFolder: String {
        (NSHomeDirectory() as NSString).appendingPathComponent("Library/Application Support/Offload/luts")
    }

    /// A 3D .cube whose data rows match its LUT_3D_SIZE.
    public static func isValidCube(_ path: String) -> Bool {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return false }
        var size = 0, rows = 0
        for raw in text.split(whereSeparator: \.isNewline) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("LUT_3D_SIZE") {
                guard let n = Int(line.dropFirst("LUT_3D_SIZE".count).trimmingCharacters(in: .whitespaces)) else { return false }
                size = n
            } else if let c = line.first, c.isASCII, c.isNumber || c == "-" || c == "." {
                rows += 1
            }
        }
        return size >= 2 && rows == size * size * size
    }
}

/// Which LUT source applies (`proxy.lutMode`). JSON values "auto", "builtin", "custom", "none" — shared with Windows.
/// `off` (not `none`): a `LutMode?` compared with `.none` would silently mean Optional.none.
public enum LutMode: String, Codable, CaseIterable {
    case auto, builtin, custom
    case off = "none"
}

/// The LUT a clip got, and why — goes into the report (key "lut"). `file` is the .cube to apply (never serialised).
public struct LutUse: Codable, Equatable {
    public let source: String
    public let id: String?
    public let name: String
    public let reason: String
    public var file: String?

    enum CodingKeys: String, CodingKey { case source, id, name, reason }

    public init(source: String, id: String?, name: String, reason: String, file: String? = nil) {
        self.source = source
        self.id = id
        self.name = name
        self.reason = reason
        self.file = file
    }

    public var isBuiltin: Bool { id != nil }

    /// No LUT, with the reason for the report (source "none").
    public static func off(_ reason: String) -> LutUse { LutUse(source: "none", id: nil, name: "bez LUT", reason: reason) }
}

/// Sony XAVC sidecar "<clip>M01.XML" (NonRealTimeMeta): the gamma and primaries the camera recorded in.
public enum SonyXML {
    public static func read(_ clipFullPath: String) -> (gamma: String?, primaries: String?)? {
        let clip = URL(fileURLWithPath: clipFullPath)
        let xml = clip.deletingLastPathComponent().appendingPathComponent(clip.deletingPathExtension().lastPathComponent + "M01.XML")
        guard FileManager.default.fileExists(atPath: xml.path), let parser = XMLParser(contentsOf: xml) else { return nil }
        let items = ItemCollector()
        parser.delegate = items
        guard parser.parse() else { return nil }
        return ((items.values["CaptureGammaEquation"] ?? nil), (items.values["CaptureColorPrimaries"] ?? nil))
    }

    /// The first element carrying each `name` attribute, and its `value` attribute (namespace-agnostic).
    private final class ItemCollector: NSObject, XMLParserDelegate {
        var values: [String: String?] = [:]

        func parser(_ parser: XMLParser, didStartElement elementName: String, namespaceURI: String?,
                    qualifiedName qName: String?, attributes attributeDict: [String: String] = [:]) {
            if let name = attributeDict["name"], values.index(forKey: name) == nil { values[name] = .some(attributeDict["value"]) }
        }
    }
}

/// Which LUT a clip gets (LUT design §3; port of Windows `LutResolver`). Pure rules over metadata; never throws.
public enum LutResolver {
    /// The stored mode, or for older settings: a non-empty lutPath means custom, otherwise automatic.
    public static func effectiveMode(_ s: ProxySettings) -> LutMode {
        s.lutMode ?? ((s.lutPath ?? "").isEmpty ? .auto : .custom)
    }

    public static func resolve(_ clip: Clip, info: ClipInfo?, clipFullPath: String, settings s: ProxySettings,
                               catalog: LutCatalog) -> LutUse {
        switch effectiveMode(s) {
        case .off:
            return .off("vypnuto v nastavení")
        case .custom:
            guard let path = s.lutPath, !path.isEmpty else { return .off("vlastní LUT není vybraný") }
            return LutUse(source: "custom", id: nil, name: (path as NSString).lastPathComponent, reason: "vlastní LUT", file: path)
        case .builtin:
            return builtin(catalog, s.lutBuiltin, source: "builtin", reason: "vybraný v nastavení") ?? .off("vestavěný LUT nenalezen")
        case .auto:
            break
        }
        if catalog.luts.isEmpty { return .off("vestavěné LUTy chybí") }
        let (id, reason) = detect(clip, info: info, clipFullPath: clipFullPath)
        if id == "" { return .off(reason) }   // recognised, but not log footage
        if let id { return builtin(catalog, id, source: "auto", reason: reason) ?? .off("vestavěný LUT nenalezen") }
        return builtin(catalog, s.lutFallback, source: "fallback", reason: "kamera nerozpoznána — záložní LUT")
            ?? .off("kamera nerozpoznána")
    }

    private static func builtin(_ catalog: LutCatalog, _ id: String?, source: String, reason: String) -> LutUse? {
        guard let lut = catalog.find(id) else { return nil }
        return LutUse(source: source, id: lut.id, name: lut.name, reason: reason, file: lut.path)
    }

    /// BRAW decodes in the gamma the clip was shot in: only "Blackmagic Design Film" Gen 5 matches the built-in LUT.
    /// Video / Extended Video / Custom / HDR gammas are display-ready already; a decoder without the gamma in --info keeps
    /// the old assumption (Film Gen 5). Film Gen 4 has no built-in LUT (provisional, as Windows). Port of `LutResolver.Braw`.
    static func braw(_ info: ClipInfo?) -> (id: String?, reason: String) {
        guard let gamma = info?.gamma, !gamma.isEmpty else {
            return ("bmd-film-gen5", "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)")
        }
        let gen = info?.colorScienceGen
        let label = gen.map { "\(gamma) Gen \($0)" } ?? gamma
        if gamma.caseInsensitiveCompare("Blackmagic Design Film") != .orderedSame {
            return ("", "Blackmagic RAW: \(label) (není log)")
        }
        if let gen, gen < 5 { return ("", "Blackmagic RAW: \(label) (bez vestavěného LUTu)") }
        return ("bmd-film-gen5", "Blackmagic RAW: \(label)")
    }

    /// (id, reason): id nil = not recognised, "" = recognised as non-log (no LUT).
    static func detect(_ clip: Clip, info: ClipInfo?, clipFullPath: String) -> (id: String?, reason: String) {
        switch clip.format {
        case .braw:
            return braw(info)
        case .r3d:
            return ("red-log3g10-rwg", "RED R3D (IPP2 Log3G10)")
        case .arriraw:
            return ARRIRAWBackend.colorSpace(lut: nil, info: info ?? ClipInfo()).contains("LogC4")
                ? ("arri-logc4-awg4", "ARRIRAW (LogC4)") : ("arri-logc3-awg3", "ARRIRAW (LogC3)")
        default:
            break
        }
        if let xml = SonyXML.read(clipFullPath) {
            let gamma = xml.gamma ?? "", primaries = xml.primaries ?? ""
            let g = gamma.lowercased(), p = primaries.lowercased()
            if g.hasPrefix("s-log3") {
                let cine = p.contains("cine") || (p.isEmpty && g.hasSuffix("cine"))
                var reason = "Sony XML: \(gamma) / \(primaries)"
                while reason.hasSuffix(" ") || reason.hasSuffix("/") { reason.removeLast() }
                return (cine ? "sony-slog3-sgamut3cine" : "sony-slog3-sgamut3", reason)
            }
            // S-Log2 / S-Log is log too, there is just no built-in LUT for it; the rest (rec709, hlg, s-cinetone) is not log.
            if g.hasPrefix("s-log") { return ("", "Sony XML: \(gamma) (bez vestavěného LUTu)") }
            if !g.isEmpty { return ("", "Sony XML: \(gamma) (není log)") }
        }
        let camera = (info?.camera ?? "").uppercased()
        if camera.contains("ALEXA") || camera.contains("AMIRA") {
            return ARRIRAWBackend.isAlexa35(camera)
                ? ("arri-logc4-awg4", "ARRI kamera (předpoklad LogC4)") : ("arri-logc3-awg3", "ARRI kamera (předpoklad LogC3)")
        }
        return (nil, "kamera nerozpoznána")
    }
}

/// `--lut` / `--lut-fallback` of the CLI: auto, none, a built-in id, or a path to a .cube (anything ending in ".cube"
/// or containing a path separator).
public enum LutOption {
    public struct Parsed: Equatable {
        public var mode: LutMode
        public var builtin: String?
        public var path: String?
        public var fallback: String?
        public var error: String?
    }

    public static func parse(_ value: String?, fallback: String?, catalog: LutCatalog) -> Parsed {
        let ids = catalog.luts.map(\.id).joined(separator: ", ")
        var fb: String?
        if let fallback, fallback.lowercased() != "none" {
            guard catalog.find(fallback) != nil else {
                return Parsed(mode: .auto, error: "Unknown fallback LUT '\(fallback)'. Built-in: \(ids)")
            }
            fb = fallback
        }
        guard let value, value.lowercased() != "auto" else { return Parsed(mode: .auto, fallback: fb) }
        // An empty value ("--lut ''") is an explicit "no LUT", like none.
        if value.lowercased() == "none" || value.trimmingCharacters(in: .whitespaces).isEmpty { return Parsed(mode: .off, fallback: fb) }
        if value.lowercased().hasSuffix(".cube") || value.contains("/") || value.contains("\\") {
            return Parsed(mode: .custom, path: value, fallback: fb)
        }
        return catalog.find(value) != nil
            ? Parsed(mode: .builtin, builtin: value, fallback: fb)
            : Parsed(mode: .auto, fallback: fb, error: "Unknown LUT '\(value)'. Use auto, none, a .cube path or one of: \(ids)")
    }
}
