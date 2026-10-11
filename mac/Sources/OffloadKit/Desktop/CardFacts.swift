import Foundation

/// "ALEXA 35 · LogC4" (UI návrh, nová věc 6): the camera and its log curve, from one clip of the card. Port of Windows
/// `CameraLabel`.
public struct CameraLabel: Equatable {
    public let camera: String?
    public let curve: String?

    public init(camera: String?, curve: String?) { self.camera = camera; self.curve = curve }

    /// "ALEXA 35 · LogC4", "ALEXA 35", "S-Log3", or nil when nothing is known.
    public var text: String? {
        let parts = [camera, curve].compactMap { $0?.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    static let makers = ["Blackmagic Design", "Blackmagic", "ARRI", "Sony", "Canon", "Panasonic", "FUJIFILM", "Fujifilm", "DJI", "RED"]

    /// The model without its maker when the model says enough on its own: "ARRI ALEXA 35" → "ALEXA 35".
    public static func model(_ camera: String?) -> String? {
        guard let c = camera?.trimmingCharacters(in: .whitespaces), !c.isEmpty else { return nil }
        for m in makers where c.lowercased().hasPrefix(m.lowercased() + " ") && c.count - m.count > 3 {
            return String(c.dropFirst(m.count + 1)).trimmingCharacters(in: .whitespaces)
        }
        return c
    }

    /// The curve of a built-in LUT as people say it: "ARRI LogC4" → "LogC4", "Blackmagic Film Generation 5" →
    /// "Film Gen 5", "Canon Log 3" → "C-Log3".
    public static func shortCurve(_ curve: String) -> String {
        var c = curve.trimmingCharacters(in: .whitespaces)
        if c.hasPrefix("ARRI ") { c = String(c.dropFirst("ARRI ".count)) }
        c = c.replacingOccurrences(of: "Blackmagic Film Generation ", with: "Film Gen ")
        if c.hasPrefix("Canon Log ") { c = "C-Log" + c.dropFirst("Canon Log ".count) }
        return c
    }
}

/// The camera label of a card when it is put in (off the main thread, reading only): the first clip of the card that is
/// not a camera proxy. A video clip is asked with ffprobe (at most 5 s) and the automatic LUT choice names its curve; a
/// RAW clip is named by its format (its decoder is too slow for a card that was just put in). Port of Windows
/// `CameraHints`.
public enum CameraHints {
    public static let probeTimeout: TimeInterval = 5

    /// `probe`: ffprobe of a file (tests pass a fake); nil = FFProbe with probeTimeout when the tools have ffprobe.
    public static func probe(_ insight: CardInsight, tools: ExternalTools, catalog: LutCatalog,
                             probe: ((String) throws -> ClipInfo)? = nil) -> CameraLabel? {
        guard insight.error == nil, let clip = insight.clips.first(where: { !$0.isCameraProxy }) else { return nil }
        let raw: String? = switch clip.format {
        case .braw: "Blackmagic RAW"
        case .r3d: "RED"
        case .arriraw: "ARRIRAW"
        case .canonRaw: "Canon RAW"
        default: nil
        }
        if let raw { return CameraLabel(camera: raw, curve: nil) }
        guard clip.format == .video else { return nil }
        let path = insight.source + "/" + clip.path
        let run = probe ?? tools.ffprobe.map { ffprobe in { p in try FFProbe.probe(ffprobe, p, timeout: probeTimeout) } }
        let info = try? run?(path)   // a clip ffprobe cannot read: the label just stays empty
        var auto = ProxySettings()
        auto.lutMode = .auto
        let lut = LutResolver.resolve(clip, info: info, clipFullPath: path, settings: auto, catalog: catalog)
        let curve = lut.id.flatMap { catalog.find($0)?.curve }.map(CameraLabel.shortCurve)
        let label = CameraLabel(camera: CameraLabel.model(info?.camera), curve: curve)
        return label.text == nil ? nil : label
    }
}

/// "estimate ~0:08 incl. proxies" (UI návrh, nová věc 10): how long a start takes, from what the jobs of this session
/// really took — the copy and read-back speed of the last job big enough to measure, and the time stage 2 took per
/// second of clip with the same stage-2 settings. Memory only (a new session starts without an estimate). When any part
/// a start needs is not known the estimate is nil and the app shows only the size: a guess would be worse than nothing.
/// Port of Windows `RateMemory`.
public final class RateMemory {
    /// Jobs smaller than this are too short to measure a speed (the start-up cost would dominate).
    public static let minBytes: Int64 = 200_000_000

    public private(set) var copyRate: Double?
    public private(set) var verifyRate: Double?
    private var post: [String: Double] = [:]

    public init() {}

    /// What stage 2 costs depends on what it does: analysis only, or proxies of one codec, height and bitrate.
    public static func postKey(analyze: Bool, proxy: ProxySettings) -> String? {
        proxy.enabled ? "proxy|\(proxy.codec.rawValue)|\(proxy.height)|\(proxy.bitrateMbps)" : analyze ? "analysis" : nil
    }

    /// A finished job: `stage2` is how long its stage 2 ran without pauses (JobProgress.stage2Elapsed), nil without one;
    /// `done`: it ended Done. A cancelled job teaches nothing, and a stage 2 that did not end Done would teach a far too
    /// low cost. A repair covers only some files and disks, so it teaches nothing either.
    public func learn(_ r: OffloadReport, analyze: Bool, proxy: ProxySettings, stage2: TimeInterval?, cancelled: Bool = false,
                      done: Bool = true) {
        if cancelled || r.isRepair { return }
        if r.totalBytes >= RateMemory.minBytes && r.copySeconds > 0 && r.success { copyRate = Double(r.totalBytes) / r.copySeconds }
        if r.totalBytes >= RateMemory.minBytes && r.verified && r.verifySeconds > 0 { verifyRate = Double(r.totalBytes) / r.verifySeconds }
        let clipSeconds = r.clips.filter { !$0.clip.isCameraProxy }.reduce(0) { $0 + ($1.info?.duration ?? 0) }
        if done, let t = stage2, t > 0, clipSeconds > 0, let key = RateMemory.postKey(analyze: analyze, proxy: proxy) {
            post[key] = t / clipSeconds
        }
    }

    /// The seconds of footage on a card before it is read: from the clips' sizes at a careful (low) data rate, as the
    /// space check estimates them — the estimate leans long.
    public static func clipSeconds(_ clips: [Clip]) -> Double {
        clips.filter { !$0.isCameraProxy }.reduce(0) { $0 + Double($1.bytes) * 8 / (SpaceEstimator.sourceMbps($1) * 1e6) }
    }

    /// The whole job before it starts, or nil when a part is not known.
    public func estimate(bytes: Int64, clips: [Clip], verify: Bool, analyze: Bool, proxy: ProxySettings) -> TimeInterval? {
        guard let c = copyRate else { return nil }
        var seconds = Double(bytes) / c
        if verify {
            guard let v = verifyRate else { return nil }
            seconds += Double(bytes) / v
        }
        if let key = RateMemory.postKey(analyze: analyze, proxy: proxy), !clips.isEmpty {
            guard let perSecond = post[key] else { return nil }
            seconds += RateMemory.clipSeconds(clips) * perSecond
        }
        return seconds
    }

    /// The right side of the start bar: "estimate ~8:00 incl. proxies", "estimate ~2:10", or nil (only the size shows).
    public static func startText(_ estimate: TimeInterval?, proxies: Bool) -> String? {
        estimate.map { "estimate ~\(JobProgress.etaText($0))\(proxies ? " incl. proxies" : "")" }
    }
}
