import Foundation

public enum ClipFormat: String, Codable {
    case video      // anything ffmpeg decodes: ProRes, H.264/H.265, XAVC, XF-AVC, DNx, AVC-Intra…
    case braw       // Blackmagic RAW
    case r3d        // RED
    case arriraw    // ARRIRAW (.ari sequence or .mxf)
    case canonRaw   // Canon Cinema RAW Light
    case unknown

    public var label: String {
        switch self {
        case .video: return "Video"
        case .braw: return "Blackmagic RAW"
        case .r3d: return "RED R3D"
        case .arriraw: return "ARRIRAW"
        case .canonRaw: return "Canon RAW"
        case .unknown: return "?"
        }
    }
}

public struct Clip: Codable {
    public let name: String
    /// Primary file, relative to the card folder (for .ari sequences the folder).
    public let path: String
    public var format: ClipFormat
    public let segments: [String]
    public let bytes: Int64
    public let isCameraProxy: Bool
}

public struct ClipInfo: Codable {
    public var codec: String?
    public var width: Int?
    public var height: Int?
    /// Rational as ffmpeg likes it ("24000/1001").
    public var frameRate: String?
    public var duration: Double?
    public var frames: Int?
    public var timecode: String?
    public var audioChannels: Int = 0
    public var camera: String?
    /// Log curve the camera recorded in (e.g. "LogC4"), when the format reports it.
    public var logEncoding: String?
    /// Anamorphic squeeze of the lens (2.0 for 2x), when the format reports it.
    public var squeeze: Double?
    /// "tv" (legal) or "pc" (full) as ffprobe reports it; full-range sources need a pre-map before built-in LUTs.
    public var colorRange: String?
    /// Blackmagic RAW: gamma, gamut and colour science generation the clip was recorded in (from braw-decode --info);
    /// the LUT is chosen by them (LutResolver.braw). Not written when unknown.
    public var gamma: String?
    public var gamut: String?
    public var colorScienceGen: Int?
    /// The reel: ffprobe's `reel_name` / `tape_name` / `reel` tag after the probe, then ReelResolver's answer (tag, else
    /// the clip name, else the card folder) once the job has the clip. Report key `clips[].info.reel`, absent when unknown.
    public var reel: String?

    public var fpsLabel: String {
        guard let r = frameRate else { return "" }
        let parts = r.split(separator: "/").compactMap { Double($0) }
        let v = parts.count == 2 && parts[1] != 0 ? parts[0] / parts[1] : (parts.first ?? 0)
        return v == v.rounded() ? String(Int(v)) : String(format: "%.3f", v)
    }

    public var resolutionLabel: String {
        guard let w = width, let h = height else { return "" }
        return "\(w)×\(h)"
    }

    public var durationLabel: String {
        guard let d = duration else { return "" }
        let s = Int(d.rounded())
        return String(format: "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60)
    }
}

public enum ClipDetector {
    static let videoExtensions: Set<String> = ["mov", "mp4", "m4v", "mxf", "mts", "m2ts", "avi", "mkv"]

    public static func detect(_ files: [SourceFile]) -> [Clip] {
        var clips: [Clip] = []
        var r3dGroups: [String: [SourceFile]] = [:]
        var ariGroups: [String: [SourceFile]] = [:]

        for f in files {
            let rel = f.relPath
            if rel.hasPrefix(Layout.reportDirName + "/") || rel.hasPrefix(Layout.proxyDirName + "/") { continue }
            let ext = (rel as NSString).pathExtension.lowercased()
            let base = ((rel as NSString).lastPathComponent as NSString).deletingPathExtension
            switch ext {
            case "r3d":
                // A001_C002_0101AB_001.R3D, _002.R3D … are one clip.
                let key = (rel as NSString).deletingLastPathComponent + "/" + base.replacingOccurrences(of: #"_\d{3}$"#, with: "", options: .regularExpression)
                r3dGroups[key, default: []].append(f)
            case "ari":
                ariGroups[(rel as NSString).deletingLastPathComponent, default: []].append(f)
            case "braw":
                clips.append(Clip(name: base, path: rel, format: .braw, segments: [rel], bytes: f.size, isCameraProxy: false))
            case "crm":
                clips.append(Clip(name: base, path: rel, format: .canonRaw, segments: [rel], bytes: f.size, isCameraProxy: false))
            case _ where videoExtensions.contains(ext):
                guard f.size > 0 else { continue }
                clips.append(Clip(name: base, path: rel, format: .video, segments: [rel], bytes: f.size, isCameraProxy: isCameraProxy(rel)))
            default:
                continue
            }
        }
        for (key, segs) in r3dGroups {
            let sorted = segs.sorted { $0.relPath < $1.relPath }
            clips.append(Clip(name: (key as NSString).lastPathComponent, path: sorted[0].relPath, format: .r3d,
                              segments: sorted.map(\.relPath), bytes: sorted.reduce(0) { $0 + $1.size }, isCameraProxy: false))
        }
        for (dir, frames) in ariGroups {
            let sorted = frames.sorted { $0.relPath < $1.relPath }
            clips.append(Clip(name: (dir as NSString).lastPathComponent, path: dir, format: .arriraw,
                              segments: sorted.map(\.relPath), bytes: sorted.reduce(0) { $0 + $1.size }, isCameraProxy: false))
        }
        return clips.sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
    }

    /// Low-res files the camera records next to the original (Sony SUB, Panasonic/Canon PROXY folders).
    static func isCameraProxy(_ rel: String) -> Bool {
        let comps = rel.uppercased().split(separator: "/")
        return comps.dropLast().contains { $0 == "SUB" || $0 == "PROXY" }
    }
}

enum FFProbe {
    static func probe(_ ffprobe: String, _ path: String) throws -> ClipInfo {
        let out = try ToolProcess.run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path])
        return try parse(Data(out.utf8))
    }

    static func parse(_ data: Data) throws -> ClipInfo {
        guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ToolError(tool: "ffprobe", status: 0, message: "unreadable output")
        }
        let streams = json["streams"] as? [[String: Any]] ?? []
        let format = json["format"] as? [String: Any] ?? [:]
        var info = ClipInfo()
        if let v = streams.first(where: { $0["codec_type"] as? String == "video" && ($0["disposition"] as? [String: Any])?["attached_pic"] as? Int != 1 }) {
            let name = v["codec_name"] as? String
            let profile = v["profile"] as? String
            info.codec = [name, profile].compactMap { $0 }.joined(separator: " ")
            // ffmpeg reports codecs it cannot decode (ARRIRAW, X-OCN …) as "unknown", 0×0.
            if name != nil && name != "unknown" {
                info.width = (v["width"] as? Int).flatMap { $0 > 0 ? $0 : nil }
                info.height = (v["height"] as? Int).flatMap { $0 > 0 ? $0 : nil }
            }
            if let r = v["avg_frame_rate"] as? String, r != "0/0" { info.frameRate = r } else { info.frameRate = v["r_frame_rate"] as? String }
            info.frames = (v["nb_frames"] as? String).flatMap(Int.init)
            info.timecode = (v["tags"] as? [String: Any])?["timecode"] as? String
            info.colorRange = v["color_range"] as? String
            if info.duration == nil { info.duration = (v["duration"] as? String).flatMap(Double.init) }
        }
        info.audioChannels = streams.filter { $0["codec_type"] as? String == "audio" }.reduce(0) { $0 + ($1["channels"] as? Int ?? 0) }
        if let d = (format["duration"] as? String).flatMap(Double.init) { info.duration = d }
        let ftags = format["tags"] as? [String: Any] ?? [:]
        if info.timecode == nil {
            info.timecode = (ftags["timecode"] as? String)
                ?? streams.lazy.compactMap { ($0["tags"] as? [String: Any])?["timecode"] as? String }.first
        }
        info.camera = camera(make: ftags["com.apple.quicktime.make"] as? String, model: ftags["com.apple.quicktime.model"] as? String)
            ?? (ftags["product_name"] as? String) ?? (ftags["company_name"] as? String)
        info.reel = reel(format: format, streams: streams)
        return info
    }

    /// Make and model together ("ARRI" + "ALEXA 35" → "ARRI ALEXA 35"), unless the model already starts with the make.
    static func camera(make: String?, model: String?) -> String? {
        let mk = make?.trimmingCharacters(in: .whitespaces), md = model?.trimmingCharacters(in: .whitespaces)
        let m = (mk?.isEmpty ?? true) ? nil : mk, n = (md?.isEmpty ?? true) ? nil : md
        guard let m, let n else { return m ?? n }
        return n.lowercased().hasPrefix(m.lowercased()) ? n : m + " " + n
    }
}
