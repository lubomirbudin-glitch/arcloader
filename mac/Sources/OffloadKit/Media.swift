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

    public init(name: String, path: String, format: ClipFormat, segments: [String], bytes: Int64, isCameraProxy: Bool) {
        self.name = name; self.path = path; self.format = format; self.segments = segments; self.bytes = bytes
        self.isCameraProxy = isCameraProxy
    }
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
    /// The rate the camera recorded at, from the metadata of the clip's first frame (Windows 0.8.0): ARRIRAW `Sensor
    /// State.sensorSampleRate`, BRAW `sensorRate` (braw-decode --info), RED record fps, Sony M01.XML `captureFps`. Written
    /// like `frameRate` ("35", "120000/1001"). `frameRate` stays the file's playback rate, so `duration` (frames ÷
    /// frameRate) is the editing length. Report key `clips[].info.captureFrameRate`, absent when unknown — then
    /// capture = playback.
    public var captureFrameRate: String?
    /// When the camera recorded the clip (UTC), from its metadata: ffprobe `creation_time` (QuickTime, MXF, MP4). Report
    /// key `clips[].info.recorded` (Windows 0.8.0), absent when unknown (RAW decoders, older reports) — the summary report
    /// then orders the clips by their number. Read leniently: a value of another shape is unknown, never a report that
    /// cannot be read.
    public var recorded: Date?

    public init() {}

    enum CodingKeys: String, CodingKey {
        case codec, width, height, frameRate, duration, frames, timecode, audioChannels, camera, logEncoding, squeeze
        case colorRange, gamma, gamut, colorScienceGen, captureFrameRate, recorded
    }

    /// Tolerant like the report: a missing key is unknown; `recorded` of another shape (a number, another text) too.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        codec = try c.decodeIfPresent(String.self, forKey: .codec)
        width = try c.decodeIfPresent(Int.self, forKey: .width)
        height = try c.decodeIfPresent(Int.self, forKey: .height)
        frameRate = try c.decodeIfPresent(String.self, forKey: .frameRate)
        duration = try c.decodeIfPresent(Double.self, forKey: .duration)
        frames = try c.decodeIfPresent(Int.self, forKey: .frames)
        timecode = try c.decodeIfPresent(String.self, forKey: .timecode)
        audioChannels = try c.decodeIfPresent(Int.self, forKey: .audioChannels) ?? 0
        camera = try c.decodeIfPresent(String.self, forKey: .camera)
        logEncoding = try c.decodeIfPresent(String.self, forKey: .logEncoding)
        squeeze = try c.decodeIfPresent(Double.self, forKey: .squeeze)
        colorRange = try c.decodeIfPresent(String.self, forKey: .colorRange)
        gamma = try c.decodeIfPresent(String.self, forKey: .gamma)
        gamut = try c.decodeIfPresent(String.self, forKey: .gamut)
        colorScienceGen = try c.decodeIfPresent(Int.self, forKey: .colorScienceGen)
        captureFrameRate = try c.decodeIfPresent(String.self, forKey: .captureFrameRate)
        recorded = ClipInfo.plausible((try? c.decodeIfPresent(Date.self, forKey: .recorded)) ?? nil)
    }

    /// A record time a camera could have meant: from 2000 on and at most a day ahead of now. A camera without its clock
    /// set writes 1904-01-01 (QuickTime) or 1970-01-01 — unknown, never the first clapper (Windows review S-7).
    public static func plausible(_ t: Date?) -> Date? {
        guard let t else { return nil }
        let y2000 = Date(timeIntervalSince1970: 946_684_800)
        return t >= y2000 && t <= Date().addingTimeInterval(86_400) ? t : nil
    }

    /// The playback rate as people read it: "23.976", "25"; "" when unknown.
    public var fpsLabel: String { ClipInfo.label(frameRate) }

    /// "24000/1001" → "23.976", "25/1" → "25"; "" for nil or anything unusable.
    public static func label(_ rate: String?) -> String {
        guard let v = Timecode.fps(rate) else { return "" }
        return v == v.rounded() ? String(Int(v)) : String(format: "%.3f", v)
    }

    /// The capture rate is known and is not the playback rate (overcranked, undercranked, S&Q).
    public var captureDiffers: Bool {
        guard let c = Timecode.fps(captureFrameRate), let p = Timecode.fps(frameRate) else { return false }
        return abs(c - p) > 0.001
    }

    /// What a report shows for the clip's fps: "35 fps · playback 24" when the rates differ, else the playback rate
    /// ("25"); "48 fps" when only the capture rate is known; "" when neither is. Wording as Windows `FpsSummary`.
    public var fpsSummary: String {
        if captureDiffers { return "\(ClipInfo.label(captureFrameRate)) fps · playback \(fpsLabel)" }
        let capture = ClipInfo.label(captureFrameRate)
        if fpsLabel.isEmpty && !capture.isEmpty { return capture + " fps" }
        return fpsLabel
    }

    /// A rate from camera metadata as `frameRate` writes it: "35/1" → "35", "24000/1001" stays, "23.976" →
    /// "24000/1001"; thousandths (ARRI: 35000, 23976) are divided by 1000. Nil when it is no camera rate.
    /// Same as Windows `ClipInfo.NormalizeRate`.
    public static func normalizeRate(_ rate: String?) -> String? {
        guard let r = rate?.trimmingCharacters(in: .whitespaces), let raw = Timecode.fps(r), let fps = plausibleRate(raw) else { return nil }
        if fps == raw && r.hasSuffix("/1") && !r.dropLast(2).contains(".") { return String(r.dropLast(2)) }
        if fps == raw && r.hasSuffix("/1001") { return r }
        return RawBackends.rational(fps)
    }

    /// ARRI writes some rates in thousandths of a frame per second: above 1000 the value is divided by 1000
    /// (35000 → 35, 23976 → 23.976); nil when even then it is no camera rate (0 or above 1000 fps).
    static func plausibleRate(_ value: Double) -> Double? {
        let fps = value > 1000 ? value / 1000 : value
        return fps > 0 && fps <= 1000 ? fps : nil
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
    static func probe(_ ffprobe: String, _ path: String, timeout: TimeInterval = 120) throws -> ClipInfo {
        let out = try ToolProcess.run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path],
                                      timeout: timeout)
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
        // When the camera recorded it: `creation_time` of the container, else of a stream (the summary report's order).
        let tagSets = [ftags] + streams.map { $0["tags"] as? [String: Any] ?? [:] }
        info.recorded = tagSets.lazy.compactMap { tags in
            tags.first { $0.key.lowercased() == "creation_time" }.flatMap { ($0.value as? String).flatMap(recordedDate) }
        }.first
        return info
    }

    /// "2026-10-06T08:15:30.000000Z" (ffprobe `creation_time`) in UTC; nil when it is no date or not a plausible one.
    static func recordedDate(_ text: String) -> Date? {
        let t = text.trimmingCharacters(in: .whitespaces)
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        // ffprobe writes microseconds; ISO8601DateFormatter reads milliseconds — cut the fraction to three digits.
        let trimmed = t.replacingOccurrences(of: #"(\.\d{3})\d+"#, with: "$1", options: .regularExpression)
        var d = f.date(from: trimmed)
        if d == nil {
            f.formatOptions = [.withInternetDateTime]
            d = f.date(from: t)
        }
        if d == nil {
            // "2026-10-06 08:15:30" without a zone: UTC, as ffprobe means it.
            let g = DateFormatter()
            g.locale = Locale(identifier: "en_US_POSIX")
            g.timeZone = TimeZone(identifier: "UTC")
            g.dateFormat = "yyyy-MM-dd HH:mm:ss"
            d = g.date(from: t)
        }
        // Whole seconds, as the report writes dates.
        return ClipInfo.plausible(d.map { Date(timeIntervalSince1970: $0.timeIntervalSince1970.rounded(.down)) })
    }

    /// Make and model together ("ARRI" + "ALEXA 35" → "ARRI ALEXA 35"), unless the model already starts with the make.
    static func camera(make: String?, model: String?) -> String? {
        let mk = make?.trimmingCharacters(in: .whitespaces), md = model?.trimmingCharacters(in: .whitespaces)
        let m = (mk?.isEmpty ?? true) ? nil : mk, n = (md?.isEmpty ?? true) ? nil : md
        guard let m, let n else { return m ?? n }
        return n.lowercased().hasPrefix(m.lowercased()) ? n : m + " " + n
    }
}
