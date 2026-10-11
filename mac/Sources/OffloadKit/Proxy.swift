import Darwin
import Foundation

public enum ProxyCodec: String, Codable, CaseIterable, Identifiable {
    case h264, hevc, proresProxy, proresLT, prores422, prores422HQ

    public var id: String { rawValue }

    public var label: String {
        switch self {
        case .h264: return "H.264"
        case .hevc: return "H.265 / HEVC"
        case .proresProxy: return "ProRes 422 Proxy"
        case .proresLT: return "ProRes 422 LT"
        case .prores422: return "ProRes 422"
        case .prores422HQ: return "ProRes 422 HQ"
        }
    }

    var pixelFormat: String {
        switch self {
        case .h264, .hevc: return "yuv420p"
        case .proresProxy, .proresLT, .prores422, .prores422HQ: return "yuv422p10le"
        }
    }

    func encoderArgs(bitrateMbps: Int) -> [String] {
        switch self {
        case .h264:
            return ["-c:v", "h264_videotoolbox", "-allow_sw", "1", "-profile:v", "high",
                    "-b:v", "\(bitrateMbps)M", "-maxrate", "\(bitrateMbps * 3 / 2)M"]
        case .hevc:
            return ["-c:v", "hevc_videotoolbox", "-allow_sw", "1", "-tag:v", "hvc1",
                    "-b:v", "\(bitrateMbps)M", "-maxrate", "\(bitrateMbps * 3 / 2)M"]
        case .proresProxy:
            return ["-c:v", "prores_ks", "-profile:v", "0", "-vendor", "apl0"]
        case .proresLT:
            return ["-c:v", "prores_ks", "-profile:v", "1", "-vendor", "apl0"]
        case .prores422:
            return ["-c:v", "prores_ks", "-profile:v", "2", "-vendor", "apl0"]
        case .prores422HQ:
            return ["-c:v", "prores_ks", "-profile:v", "3", "-vendor", "apl0"]
        }
    }

    /// Every audio stream of the source becomes a track of the proxy (Windows R14, `Encoders.AudioArgs`): H.264/HEVC AAC
    /// at 64 kb/s a channel of each stream (128–512 kb/s), ProRes 16-bit PCM. `streamChannels` empty (unknown) = 192k.
    func audioArgs(_ streamChannels: [Int]) -> [String] {
        switch self {
        case .h264, .hevc:
            var args = ["-c:a", "aac"]
            if streamChannels.isEmpty { args += ["-b:a", "192k"] }
            for (i, ch) in streamChannels.enumerated() { args += ["-b:a:\(i)", "\(SpaceEstimator.aacKbps(ch))k"] }
            return args
        case .proresProxy, .proresLT, .prores422, .prores422HQ: return ["-c:a", "pcm_s16le"]
        }
    }
}

public struct ProxySettings: Codable, Equatable {
    public var enabled = false
    public var height = 1080
    public var codec: ProxyCodec = .h264
    public var bitrateMbps = 10
    /// The user's own .cube (mode custom), applied after downscaling (cheap at proxy resolution).
    public var lutPath: String?
    /// Which LUT source applies; nil = not stored (older settings): a lutPath means custom, otherwise automatic.
    public var lutMode: LutMode?
    /// Built-in LUT id used in builtin mode.
    public var lutBuiltin: String?
    /// Built-in LUT id used in auto mode when the camera is not recognised (nil = no LUT).
    public var lutFallback: String?
    public var burnFilename = true
    public var burnTimecode = true
    public var audio = true
    /// Skip the low-res files cameras record themselves (Sony SUB, PROXY folders).
    public var skipCameraProxies = true
    /// Task 54 (Windows 0.5.1): no H.264/HEVC proxy for an H.264/HEVC original that is lighter than the proxy would be
    /// (`LighterSource`); CLI --proxy-always turns it off. Absent in older settings and reports = true.
    public var skipLighterSources = true
    /// Proxies are rendered once, onto the first destination, then copied to the others.
    public var copyToAllDestinations = true
    /// Optional: every proxy is also copied to `<folder>/<date>/<card>/` (never overwriting). nil = off (key absent).
    public var extraFolder: String?
    /// Crop to the frame lines: aspect ratio of the frame (e.g. 1.66) and how much of the image
    /// it covers (0.9 = 90 %, i.e. 10 % overscan around it). nil = full image.
    public var frameAspect: Double?
    public var frameScale: Double = 1.0

    public init() {}

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = ProxySettings()
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? d.enabled
        height = try c.decodeIfPresent(Int.self, forKey: .height) ?? d.height
        codec = (try? c.decodeIfPresent(ProxyCodec.self, forKey: .codec)) ?? d.codec
        bitrateMbps = try c.decodeIfPresent(Int.self, forKey: .bitrateMbps) ?? d.bitrateMbps
        lutPath = try c.decodeIfPresent(String.self, forKey: .lutPath)
        // An unknown mode (a newer version) reads as "not stored" — normalize then derives it like for old settings.
        lutMode = try? c.decodeIfPresent(LutMode.self, forKey: .lutMode)
        lutBuiltin = try c.decodeIfPresent(String.self, forKey: .lutBuiltin)
        lutFallback = try c.decodeIfPresent(String.self, forKey: .lutFallback)
        burnFilename = try c.decodeIfPresent(Bool.self, forKey: .burnFilename) ?? d.burnFilename
        burnTimecode = try c.decodeIfPresent(Bool.self, forKey: .burnTimecode) ?? d.burnTimecode
        audio = try c.decodeIfPresent(Bool.self, forKey: .audio) ?? d.audio
        skipCameraProxies = try c.decodeIfPresent(Bool.self, forKey: .skipCameraProxies) ?? d.skipCameraProxies
        skipLighterSources = try c.decodeIfPresent(Bool.self, forKey: .skipLighterSources) ?? d.skipLighterSources
        copyToAllDestinations = try c.decodeIfPresent(Bool.self, forKey: .copyToAllDestinations) ?? d.copyToAllDestinations
        extraFolder = try c.decodeIfPresent(String.self, forKey: .extraFolder)
        frameAspect = try c.decodeIfPresent(Double.self, forKey: .frameAspect)
        frameScale = try c.decodeIfPresent(Double.self, forKey: .frameScale) ?? d.frameScale
    }

    /// Centered crop rectangle (x, y, w, h) inside a w×h image, even-sized, or nil when no crop is set.
    public func frameCrop(width W: Int, height H: Int) -> (x: Int, y: Int, w: Int, h: Int)? {
        guard let aspect = frameAspect, aspect > 0, W > 0, H > 0 else { return nil }
        let scale = min(max(frameScale, 0.1), 1.0)
        var w = Double(W) * scale
        var h = w / aspect
        if h > Double(H) * scale {
            h = Double(H) * scale
            w = h * aspect
        }
        let cw = Int(w) & ~1, ch = Int(h) & ~1
        return ((W - cw) / 2 & ~1, (H - ch) / 2 & ~1, cw, ch)
    }

    public var frameLabel: String? {
        guard let a = frameAspect else { return nil }
        return String(format: "%.2f", a) + (frameScale < 1 ? String(format: " · %.0f %%", frameScale * 100) : "")
    }
}

/// How frames reach ffmpeg: straight from a file, or piped from a vendor RAW decoder.
struct FrameSource {
    var inputArgs: [String]
    var feeder: Process?
    var audioFromInput = true
    /// The decoder already cropped to the frame lines (ARRI does it before debayering).
    var alreadyCropped = false
    /// Full-range RGB (braw-decode) — a built-in LUT then needs `FilterGraph.preMap` first.
    var fullRange = false
    /// Intermediate files to delete once ffmpeg is done.
    var cleanup: [String] = []
    /// Turns the feeder's own failure (it ended non-zero while ffmpeg still read) into the clip's note — a
    /// `RawDecoderError`; nil = the feeder's ToolError as it is (Windows `FrameSource.ExplainFeederError`).
    var explainFeederError: ((ToolError) -> Error)?

    func removeIntermediates() {
        for p in cleanup { try? FileManager.default.removeItem(atPath: p) }
    }
}

/// What a RAW decoder hands ffmpeg: camera log (a LUT follows) or its own Rec.709 look. `arriColorSpace` is the ARRI
/// target space matching the LUT (nil = from the clip's metadata). BRAW always decodes in the clip's gamma; Canon has
/// no decoder.
struct RawLook: Equatable {
    let log: Bool
    let arriColorSpace: String?
}

/// Per-format decoding for formats ffmpeg cannot read itself.
protocol RawBackend {
    var format: ClipFormat { get }
    var missingHint: String { get }
    func isAvailable(_ tools: ExternalTools) -> Bool
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo
    /// `singleFrameAt` (seconds) asks for just one frame — used for report thumbnails.
    func source(_ path: String, info: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource
}

/// Where a report thumbnail is taken: frame number, its time, and whether it is the clip's last frame.
struct FramePosition: Equatable {
    let seconds: Double
    let frame: Int
    let fromEnd: Bool
}

/// Probes clips, makes the report thumbnails (first, middle, last frame) and renders proxies — each clip with its own
/// LUT (`LutUse`). Works in its own temp folder. One renderer serves one job, on one thread.
public final class ProxyRenderer {
    public let settings: ProxySettings
    public let tools: ExternalTools
    /// One backend per RAW format. Tests replace one (e.g. BRAW with a fake library search or a short --info timeout).
    var backends: [ClipFormat: RawBackend]
    let tempDir: String
    /// A proxy render with no progress for this long is stuck (3× as long before the first progress): ffmpeg is killed
    /// and, when it decoded in hardware, the clip is retried once in software.
    static let defaultStallTimeout: TimeInterval = 60
    /// How long a killed ffmpeg is waited for: a process stuck in a GPU driver may never end — the job must go on.
    static let killWait: TimeInterval = 10
    /// Tests shorten it.
    var stallTimeout: TimeInterval = ProxyRenderer.defaultStallTimeout
    /// Where retries and unkillable processes are reported (the job's log).
    var log: ((String) -> Void)?
    /// The custom LUT (custom mode) was copied to `tempDir/look.cube`.
    private let hasCustomLUT: Bool
    /// Built-in LUT ids already copied into `tempDir`.
    private var builtinLUTs: Set<String> = []

    public init(settings: ProxySettings, tools: ExternalTools) {
        self.settings = settings
        self.tools = tools
        backends = Dictionary(uniqueKeysWithValues: RawBackends.all.map { ($0.format, $0) })
        let dir = NSTemporaryDirectory() + "offload-" + UUID().uuidString.prefix(8)
        tempDir = dir
        try? IO.mkdirs(dir)
        // Filter graphs are picky about quoting: LUTs are used from a plain path in the temp folder. The custom LUT is
        // copied up front, built-in ones per clip (prepareLUT).
        var custom = false
        if LutResolver.effectiveMode(settings) == .custom, let lut = settings.lutPath, !lut.isEmpty,
           FileManager.default.fileExists(atPath: lut) {
            custom = (try? FileManager.default.copyItem(atPath: lut, toPath: dir + "/look.cube")) != nil
        }
        hasCustomLUT = custom
    }

    deinit { try? FileManager.default.removeItem(atPath: tempDir) }

    /// nil when the clip can be rendered, otherwise the reason it cannot.
    public func unsupportedReason(_ clip: Clip) -> String? {
        guard tools.ffmpeg != nil else { return Msg.ffmpegNotFound }
        switch clip.format {
        case .video: return nil
        case .unknown: return Msg.unknownFormat
        default:
            guard let b = backends[clip.format] else { return Msg.noDecoder(clip.format.label) }
            return b.isAvailable(tools) ? nil : b.missingHint
        }
    }

    /// MXF is a container for many codecs; ARRIRAW/ARRICORE inside it only the ARRI tool can read.
    /// Returns the clip reclassified as ARRIRAW when ffmpeg cannot decode it but art-cmd can.
    public func reclassifyUndecodableMXF(_ clip: Clip, cardPath: String) -> (Clip, ClipInfo)? {
        guard clip.format == .video, (clip.path as NSString).pathExtension.lowercased() == "mxf",
              let arri = backends[.arriraw], arri.isAvailable(tools),
              let info = try? arri.probe(cardPath + "/" + clip.path, tools: tools), info.width != nil else { return nil }
        var c = clip
        c.format = .arriraw
        return (c, info)
    }

    public func probe(_ clip: Clip, cardPath: String) throws -> ClipInfo {
        let path = cardPath + "/" + clip.path
        if clip.format == .video {
            guard let ffprobe = tools.ffprobe else { throw ToolError(tool: "ffprobe", status: -1, message: "ffprobe not found") }
            var info = try FFProbe.probe(ffprobe, path)
            // S&Q: the capture rate is only in Sony's XML; other video says nothing — capture = playback.
            if info.captureFrameRate == nil { info.captureFrameRate = SonyXML.captureFrameRate(path) }
            return info
        }
        guard let b = backends[clip.format], b.isAvailable(tools) else {
            throw ToolError(tool: clip.format.label, status: -1, message: unsupportedReason(clip) ?? "")
        }
        return try logged { try b.probe(path, tools: tools) }
    }

    // MARK: Thumbnail positions

    /// First, middle and last frame; only the first when the length is unknown or the clip has one frame.
    static func framePositions(_ info: ClipInfo) -> [FramePosition] {
        let first = FramePosition(seconds: 0, frame: 0, fromEnd: false)
        guard let duration = info.duration, duration > 0, let fps = Timecode.fps(info.frameRate) else { return [first] }
        let frames = info.frames ?? Int((duration * fps).rounded())
        guard frames > 1 else { return [first] }
        let middle = frames / 2, last = frames - 1
        var all = [first]
        if middle > 0 { all.append(FramePosition(seconds: Double(middle) / fps, frame: middle, fromEnd: false)) }
        if last > middle { all.append(FramePosition(seconds: Double(last) / fps, frame: last, fromEnd: true)) }
        return all
    }

    /// Caption under a thumbnail: the source timecode of that frame, else its number.
    static func label(_ info: ClipInfo, _ p: FramePosition) -> String {
        if let tc = info.timecode, Timecode.isValid(tc), let fps = Timecode.fps(info.frameRate) {
            return Timecode.add(tc, frames: p.frame, fps: fps)
        }
        return "#\(p.frame)"
    }

    /// Where ffmpeg's input seek (-ss before -i) lands for a thumbnail position. A frame in the middle is sought
    /// half a frame early: its exact time rounded up would skip to the next frame (23.976, 24, 29.97 fps).
    /// The last frame is sought a second before it and decoded to the end of the video stream — never relative
    /// to the container end, which is later when the audio runs longer than the picture.
    static func seekSeconds(_ info: ClipInfo, _ p: FramePosition) -> String {
        var at = p.seconds
        if p.fromEnd {
            at = max(0, p.seconds - 1)
        } else if p.frame > 0, let fps = Timecode.fps(info.frameRate) {
            at = (Double(p.frame) - 0.5) / fps
        }
        return String(format: "%.6f", at)
    }

    /// Hardware decoding only where it helps and is safe: H.264 and HEVC (`ClipInfo.codec` starts with the ffprobe
    /// codec name). Everything else (FFV1, ProRes, MJPEG, DNx …) decodes in software — same rule as Windows, where
    /// ffmpeg 8.1 with -hwaccel hung forever on FFV1.
    /// Hardware DEcoding (VideoToolbox) is off by default: on Intel Macs it returned green frames for some H.264/HEVC
    /// clips instead of failing, so ffmpeg never noticed (reported 2026-10-08). Proxies still ENcode with VideoToolbox.
    /// The switch stays for tests of the software-retry path.
    public static var hardwareDecodeEnabled = false

    static func usesHardwareDecode(_ codec: String?) -> Bool {
        guard hardwareDecodeEnabled else { return false }
        let c = (codec ?? "").lowercased()
        return c.hasPrefix("h264") || c.hasPrefix("hevc")
    }

    /// The note at a clip whose ffmpeg stalled (texts shared with Windows `StallNote`).
    static func stallNote(_ seconds: TimeInterval, retriedInSoftware: Bool) -> String {
        "ffmpeg stalled (\(String(format: "%.0f", seconds)) s without a new frame)"
            + (retriedInSoftware ? ", also with software decoding; proxy not created" : "")
    }

    // MARK: LUT per clip

    /// The .cube for this clip as a plain path in the temp folder, or nil for no LUT. The custom LUT was copied in init
    /// (nil when it was missing); a built-in one is copied on first use, once per id.
    func prepareLUT(_ lut: LutUse?) throws -> String? {
        guard let lut, let file = lut.file, !file.isEmpty else { return nil }   // an empty path is no LUT
        guard let id = lut.id else { return hasCustomLUT ? tempDir + "/look.cube" : nil }
        let copy = tempDir + "/lut-\(id).cube"
        if !builtinLUTs.contains(id) {
            try? FileManager.default.removeItem(atPath: copy)
            try FileManager.default.copyItem(atPath: file, toPath: copy)
            builtinLUTs.insert(id)
        }
        return copy
    }

    /// RAW decoders hand over log whenever a LUT follows; ARRI's target space must match a built-in ARRI LUT.
    static func lookFor(_ lut: LutUse?, info: ClipInfo) -> RawLook {
        guard let lut, let file = lut.file, !file.isEmpty else { return RawLook(log: false, arriColorSpace: nil) }
        switch lut.id {
        case "arri-logc4-awg4"?: return RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4")
        case "arri-logc3-awg3"?: return RawLook(log: true, arriColorSpace: "AWG3/D65/LogC3")
        case nil: return RawLook(log: true, arriColorSpace: ARRIRAWBackend.colorSpace(lut: file, info: info))
        default: return RawLook(log: true, arriColorSpace: nil)
        }
    }

    /// Built-in LUTs expect legal-range input: full-range sources (BRAW, video tagged "pc") are pre-mapped first.
    /// A custom .cube is applied as it is.
    static func needsPreMap(_ lut: LutUse?, source: FrameSource, info: ClipInfo) -> Bool {
        lut?.isBuiltin == true && (source.fullRange || info.colorRange == "pc")
    }

    private func frameSource(_ clip: Clip, cardPath: String, info: ClipInfo, look: RawLook, singleFrameAt: Double?,
                             hwaccel: Bool, inputURL: String? = nil) throws -> FrameSource {
        let path = cardPath + "/" + clip.path
        if clip.format == .video {
            // Verify-in-proxy (task 68): the clip through Offload's tap; -short_seek_size keeps ffmpeg from reading a MOV
            // with moov at the end or an MXF twice (measured on Windows with ffmpeg 8.1).
            let input = inputURL.map { ["-short_seek_size", "65536", "-i", $0] } ?? ["-i", path]
            // VideoToolbox only for H.264/HEVC proxies (usesHardwareDecode); thumbnails always decode in software.
            return FrameSource(inputArgs: hwaccel ? ["-hwaccel", "videotoolbox"] + input : input)
        }
        guard let b = backends[clip.format] else { throw ToolError(tool: clip.format.label, status: -1, message: "no decoder") }
        return try logged { try b.source(path, info: info, settings: settings, look: look, tools: tools, tempDir: tempDir, singleFrameAt: singleFrameAt) }
    }

    /// A decoder error turned into a note for the clip (`RawDecoderError`): the decoder's own words — its stderr, e.g.
    /// the library it loaded — go to the job's log, the note to the clip (Windows `Logged`).
    private func logged<T>(_ decode: () throws -> T) throws -> T {
        do {
            return try decode()
        } catch let e as RawDecoderError {
            log?("\(e.decoderError)")
            throw e
        }
    }

    // MARK: Thumbnails and proxies

    /// First, middle and last frame as small JPEGs with captions, with the clip's LUT so log footage is readable.
    /// `lut` nil = no LUT (thumbnails taken from a finished proxy, which already has it). When `isCancelled` turns
    /// true the clip simply gets no thumbnails. One bad position does not cost the others — except a hung RAW decoder
    /// (a stall, or `braw-decode --info` timing out), which would hang at the next positions too: the rest is skipped and
    /// the log says so. If none works, the last error is thrown.
    public func thumbnails(_ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse? = nil,
                           isCancelled: () -> Bool = { false }) throws -> [Thumbnail] {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "ffmpeg not found") }
        let lutFile = try prepareLUT(lut)
        let look = ProxyRenderer.lookFor(lutFile != nil ? lut : nil, info: info)   // log only when a LUT really follows
        let video = clip.format == .video
        var result: [Thumbnail] = []
        var lastError: Error?
        for pos in ProxyRenderer.framePositions(info) {
            if isCancelled() { return [] }
            let out = tempDir + "/thumb-\(UUID().uuidString).jpg"
            defer { unlink(out) }
            var src: FrameSource?
            do {
                let s = try frameSource(clip, cardPath: cardPath, info: info, look: look, singleFrameAt: pos.seconds, hwaccel: false)
                src = s
                defer { s.removeIntermediates() }
                // The pre-map depends on the source (BRAW, full-range video).
                let filter = FilterGraph.thumbnail(lutFile: lutFile, premap: ProxyRenderer.needsPreMap(lut, source: s, info: info))
                var args = ["-hide_banner", "-loglevel", "error", "-y"]
                if video { args += ["-ss", ProxyRenderer.seekSeconds(info, pos)] }
                args += s.inputArgs
                // Last frame: decode the final second and keep overwriting the image — the last write is the last frame.
                args += video && pos.fromEnd ? ["-map", "0:v:0", "-vf", filter, "-update", "1"] : ["-frames:v", "1", "-vf", filter]
                args += ["-q:v", "5", out]
                // A RAW decoder piped in (BRAW) runs under the stall watchdog: the data it hands ffmpeg is the progress, and
                // a hung SDK must not hang the job. A file input prints no progress here, so it is left alone (as Windows).
                try runFFmpeg(ffmpeg, args, feeder: s.feeder, explainFeederError: s.explainFeederError, duration: nil,
                              progress: nil, isCancelled: isCancelled, stallAfter: s.feeder != nil ? stallTimeout : nil)
                guard FileManager.default.fileExists(atPath: out) else {
                    throw ToolError(tool: "ffmpeg", status: 0, message: "no frame")
                }
                result.append(Thumbnail(jpeg: try Data(contentsOf: URL(fileURLWithPath: out)), label: ProxyRenderer.label(info, pos)))
            } catch let e as FFmpegStall where src?.feeder != nil {
                if isCancelled() { return [] }
                // A hung decoder would hang at the next positions too (3 × the grace per clip): stop here.
                let stall = FFmpegStall(note: "decoder or " + e.note)
                lastError = stall
                log?("thumbnails \(clip.name): \(stall); the remaining frames are skipped")
                break
            } catch let e as ToolError where src == nil && e.status == -1 && isBrawDecoder(e.tool) {
                if isCancelled() { return [] }
                // `braw-decode --info` timed out: it would at the next positions too.
                lastError = e
                log?("thumbnails \(clip.name): \(e); the remaining frames are skipped")
                break
            } catch {
                if isCancelled() { return [] }
                lastError = error
            }
        }
        if result.isEmpty, let lastError { throw lastError }
        return result
    }

    /// Task 70: a small picture of the place of a content defect (JPEG, 180 px high) from the clip on the destination; nil
    /// when it cannot be made (RAW, no ffmpeg) — the report then shows the defect without a picture.
    public func snapshot(_ clip: Clip, cardPath: String, info: ClipInfo, frame: Int64) -> Data? {
        guard let ffmpeg = tools.ffmpeg, clip.format == .video else { return nil }
        let out = tempDir + "/defect-\(UUID().uuidString.prefix(8)).jpg"
        defer { unlink(out) }
        let seconds = Timecode.fps(info.frameRate).map { $0 > 0 ? Double(frame) / $0 : 0 } ?? 0
        let at = String(format: "%.6f", max(0, seconds - 0.5))
        do {
            try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-ss", at, "-i", cardPath + "/" + clip.path,
                                         "-frames:v", "1", "-vf", "scale=-2:180", "-q:v", "5", out])
        } catch {
            return nil
        }
        return FileManager.default.contents(atPath: out)
    }

    /// A ToolError raised by braw-decode (ToolProcess names errors by the executable's file name).
    private func isBrawDecoder(_ tool: String) -> Bool {
        tools.brawDecoder.map { ($0 as NSString).lastPathComponent == tool } ?? false
    }

    /// Renders the proxy. A render that stalls (no progress for `stallTimeout`) is killed. With hardware decoding the clip
    /// is then retried once in software — as is one that failed with it; without, the stall fails the proxy. Cancelling
    /// never retries. Notes and log lines as on Windows.
    /// `inputURL`: verify-in-proxy's tap for the clip (mode A) — ffmpeg reads it from there instead of the file; `activity`:
    /// the tap's reads, which the stall watchdog takes as progress (a slow destination does not kill the proxy).
    /// `content` (task 70): watches the decoder's warnings and frame count for the content check (-loglevel warning).
    public func render(_ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse? = nil, output: String,
                       progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool,
                       inputURL: String? = nil, activity: (() -> Int64)? = nil, content: ContentCheck? = nil) throws {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "not found") }
        let lutFile = try prepareLUT(lut)
        let look = ProxyRenderer.lookFor(lutFile != nil ? lut : nil, info: info)
        let hw = clip.format == .video && ProxyRenderer.usesHardwareDecode(info.codec)
        let first: Error
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, lut: lut, lutFile: lutFile, look: look, hwaccel: hw,
                           output: output, progress: progress, isCancelled: isCancelled, inputURL: inputURL, activity: activity,
                           content: content)
            return
        } catch let e as FFmpegStall where !hw && !isCancelled() {
            throw FFmpegStall(note: e.note + "; proxy not created")
        } catch where hw && (error is FFmpegStall || error is ToolError) && !isCancelled() {
            first = error   // hardware decoding stalled or failed: once more in software
        }
        if isCancelled() { throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled") }
        content?.reset()   // what the hardware decoder said is not the file's verdict
        log?("proxy \(clip.name): with hardware decoding: \(first) — retrying in software")
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, lut: lut, lutFile: lutFile, look: look, hwaccel: false,
                           output: output, progress: progress, isCancelled: isCancelled, inputURL: inputURL, activity: activity,
                           content: content)
            log?("proxy \(clip.name): software decoding succeeded")
        } catch where (error is FFmpegStall || error is ToolError) && !isCancelled() {
            log?("proxy \(clip.name): software decoding failed too: \(error)")
            switch (first, error) {
            case (is FFmpegStall, is FFmpegStall):
                throw ProxyRetryFailed(note: ProxyRenderer.stallNote(stallTimeout, retriedInSoftware: true))
            case (is FFmpegStall, _):
                throw ProxyRetryFailed(note: "\(first); software decoding failed: \(error)")
            default:
                throw ProxyRetryFailed(note: "\(error); earlier with hardware decoding: \(first)")
            }
        }
    }

    private func renderOnce(_ ffmpeg: String, _ clip: Clip, cardPath: String, info: ClipInfo, lut: LutUse?, lutFile: String?,
                            look: RawLook, hwaccel: Bool, output: String,
                            progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool,
                            inputURL: String? = nil, activity: (() -> Int64)? = nil, content: ContentCheck? = nil) throws {
        // ARRIRAW: art-cmd first writes a ProRes LT intermediate and reports no progress itself — estimate it from the
        // intermediate's size (0–80 %); ffmpeg's own progress covers the rest.
        let arriMonitor = clip.format == .arriraw ? intermediateProgress(info: info, progress: progress) : nil
        let src: FrameSource
        do {
            src = try frameSource(clip, cardPath: cardPath, info: info, look: look, singleFrameAt: nil, hwaccel: hwaccel,
                                  inputURL: inputURL)
        } catch {
            arriMonitor?.cancel()
            throw error
        }
        arriMonitor?.cancel()
        defer { src.removeIntermediates() }
        let progress: (Double) -> Void = clip.format == .arriraw ? { progress(0.8 + 0.2 * $0) } : progress
        let outHeight = min(settings.height, info.height ?? settings.height) & ~1
        let vf = FilterGraph.proxy(settings, outHeight: outHeight, alreadyCropped: src.alreadyCropped, lutFile: lutFile,
                                   premap: ProxyRenderer.needsPreMap(lut, source: src, info: info),
                                   burnIn: burnIn(clip: clip, info: info, height: outHeight))
        // Warning level with the content check: "corrupt decoded frame" is a warning (Windows measurement, ffmpeg 8.1).
        var args = ["-hide_banner", "-loglevel", content != nil ? "warning" : "error", "-y", "-nostats", "-progress", "pipe:1"] + src.inputArgs
        args += ["-map", "0:v:0"]
        if settings.audio && info.audioChannels > 0 && src.audioFromInput {
            args += ["-map", "0:a?"] + settings.codec.audioArgs(info.audioStreams)   // every audio stream (Windows R14)
        }
        args += ["-vf", vf] + settings.codec.encoderArgs(bitrateMbps: settings.bitrateMbps) + FilterGraph.rec709Args
        if let tc = info.timecode, Timecode.isValid(tc) {
            args += ["-timecode", tc]
            // The MOV muxer writes it into the timecode track (tmcd) as its source reference: Premiere reads it as Tape
            // Name, Resolve as Reel (ffprobe shows `reel_name` on the data stream). Without TC there is no tmcd.
            if let reel = info.reel, !reel.isEmpty { args += ["-metadata:s:v:0", "reel_name=\(reel)"] }
        }

        let dir = (output as NSString).deletingLastPathComponent
        try IO.mkdirs(dir)
        let part = dir + "/." + (output as NSString).lastPathComponent + ".part"
        args += ["-f", "mov", part]
        do {
            try runFFmpeg(ffmpeg, args, feeder: src.feeder, explainFeederError: src.explainFeederError, duration: info.duration,
                          progress: progress, isCancelled: isCancelled, stallAfter: stallTimeout, activity: activity, content: content)
            // Never over a proxy that is already there (an append writes next to earlier proxies; a name taken meanwhile).
            do {
                try ProxyCopy.renameNoReplace(part, output)
            } catch is ProxyCopy.Taken {
                throw IOError(message: "proxy name taken meanwhile: \((output as NSString).lastPathComponent)")
            }
        } catch {
            unlink(part)
            throw error
        }
    }

    /// Polls the size of the ARRI intermediate (`<tempDir>/ari-*/intermediate.mxf`) once a second and reports it as
    /// 0–0.8 of the clip's progress. ProRes 422 LT is ≈ 0.21 bytes per pixel and frame — an estimate, capped at 95 %.
    func intermediateProgress(info: ClipInfo, progress: @escaping (Double) -> Void) -> DispatchSourceTimer? {
        guard let frames = info.frames, frames > 0, let w = info.width, let h = info.height, h > 0 else { return nil }
        let outH = Double(settings.height)
        let aspect = settings.frameAspect ?? (Double(w) * (info.squeeze ?? 1) / Double(h))
        let expected = Double(frames) * outH * aspect * outH * 0.21
        let root = tempDir
        let timer = DispatchSource.makeTimerSource(queue: .global(qos: .utility))
        timer.schedule(deadline: .now() + 1, repeating: 1)
        timer.setEventHandler {
            let fm = FileManager.default
            let dirs = ((try? fm.contentsOfDirectory(atPath: root)) ?? []).filter { $0.hasPrefix("ari-") }
            var size: Double = 0
            for d in dirs {
                if let n = (try? fm.attributesOfItem(atPath: root + "/" + d + "/intermediate.mxf"))?[.size] as? NSNumber {
                    size = max(size, n.doubleValue)
                }
            }
            progress(min(0.95, size / expected) * 0.8)
        }
        timer.resume()
        return timer
    }

    /// Bottom bar: clip name left, source timecode right.
    func burnIn(clip: Clip, info: ClipInfo, height: Int) -> [String] {
        guard settings.burnFilename || settings.burnTimecode else { return [] }
        let font = ["/System/Library/Fonts/Menlo.ttc", "/System/Library/Fonts/Monaco.ttf"].first { FileManager.default.fileExists(atPath: $0) }
        guard let font else { return [] }
        let size = max(12, height * 3 / 100)
        let margin = max(8, height * 2 / 100)
        var f = ["drawbox=x=0:y=ih-\(size * 2):w=iw:h=\(size * 2):color=black@0.55:t=fill"]
        let common = "fontfile=\(font):fontcolor=white:fontsize=\(size):y=h-\(size * 3 / 2)"
        if settings.burnFilename {
            let label = tempDir + "/label-\(UUID().uuidString.prefix(8)).txt"
            try? Data(clip.name.utf8).write(to: URL(fileURLWithPath: label))
            // expansion=none: the name is literal text — a "%" in it would otherwise be a drawtext expansion.
            f.append("drawtext=\(common):textfile=\(label):expansion=none:x=\(margin)")
        }
        if settings.burnTimecode, let rate = info.frameRate {
            let tc = info.timecode.flatMap { Timecode.isValid($0) ? $0 : nil } ?? "00:00:00:00"
            let escaped = tc.replacingOccurrences(of: ":", with: "\\:").replacingOccurrences(of: ";", with: "\\;")
            f.append("drawtext=\(common):timecode='\(escaped)':rate=\(rate):x=w-tw-\(margin)")
        }
        return f
    }

    /// `stallAfter`: ffmpeg is killed and `FFmpegStall` thrown when it has reported no new frame or output time and taken
    /// no data from the feeder for that long (3× before the first progress) — every proxy, and thumbnails with a feeder.
    /// Cancelling is checked first and wins. A feeder (RAW decoder) is judged as on Windows: one that ended non-zero on
    /// its own while ffmpeg still read is the clip's error, before ffmpeg's (`explainFeederError` may turn it into the
    /// note); one that was terminated, or whose stdout the pump closed because ffmpeg had stopped reading, is not.
    func runFFmpeg(_ ffmpeg: String, _ args: [String], feeder: Process?, explainFeederError: ((ToolError) -> Error)? = nil,
                   duration: Double?, progress: ((Double) -> Void)?, isCancelled: () -> Bool,
                   stallAfter: TimeInterval? = nil, activity: (() -> Int64)? = nil, content: ContentCheck? = nil) throws {
        let p = ToolProcess(ffmpeg, args)
        p.captureStderr(onLine: content.map { c in { c.stderr($0) } })
        let watch = ProgressWatch()
        // Set by the pump when ffmpeg stopped taking data and the feeder's stdout was closed under it.
        let feederCut = Flag()
        var feederStderr: StderrTail?
        var pump: Thread?
        if let feeder {
            // The decoder's frames go through a pump: every chunk ffmpeg takes is progress (a slow RAW decoder is not stuck).
            let fromFeeder = Pipe(), toFFmpeg = Pipe()
            feeder.standardOutput = fromFeeder
            // Its stderr names the cause when it fails on its own (braw-decode: the clip and the library it loaded).
            feederStderr = StderrTail(feeder)
            p.process.standardInput = toFFmpeg
            // A write after ffmpeg died must fail with EPIPE, not take the app down with SIGPIPE.
            _ = fcntl(toFFmpeg.fileHandleForWriting.fileDescriptor, F_SETNOSIGPIPE, 1)
            pump = Thread {
                // Plain read(2)/write(2): read returns whatever the decoder has written so far. FileHandle.read(upToCount:)
                // on a pipe waits for the whole count (or EOF) — a slow decoder would look stuck.
                let inFD = fromFeeder.fileHandleForReading.fileDescriptor
                let outFD = toFFmpeg.fileHandleForWriting.fileDescriptor
                let size = 4 << 20
                let buffer = UnsafeMutableRawPointer.allocate(byteCount: size, alignment: 16)
                defer { buffer.deallocate() }
                pumping: while true {
                    let n = Darwin.read(inFD, buffer, size)
                    if n < 0 && errno == EINTR { continue }
                    if n <= 0 { break }   // the decoder is done (EOF) or gone
                    var sent = 0
                    while sent < n {
                        let w = Darwin.write(outFD, buffer + sent, n - sent)
                        if w < 0 && errno == EINTR { continue }
                        if w <= 0 {
                            // ffmpeg went away first (a thumbnail needs one frame, or it failed). Marked before the
                            // decoder's end is closed below, so its exit code is known to be a consequence.
                            feederCut.set()
                            break pumping
                        }
                        sent += w
                    }
                    watch.dataFlowed()
                }
                try? toFFmpeg.fileHandleForWriting.close()
                // Close the decoder's end too: a decoder still writing sees the broken pipe and stops by itself
                // (braw-decode exits 0) instead of blocking on a full pipe until it is terminated.
                try? fromFeeder.fileHandleForReading.close()
            }
        } else {
            p.process.standardInput = FileHandle.nullDevice
        }
        p.onStdoutLine { line in
            content?.progress(line)
            watch.advanced(line)
            guard let progress, let d = duration, d > 0, line.hasPrefix("out_time_us="),
                  let us = Double(line.dropFirst("out_time_us=".count)) else { return }
            progress(min(1, us / 1_000_000 / d))
        }
        try feeder?.run()
        do {
            try p.process.run()
        } catch {
            feeder?.terminate()
            throw error
        }
        pump?.start()
        var lastActivity = activity?() ?? 0
        while p.process.isRunning {
            // The tap read from the disk (verify-in-proxy): a slow destination is alive, a hung one is not.
            if let a = activity?(), a != lastActivity {
                lastActivity = a
                watch.dataFlowed()
            }
            if isCancelled() {
                stop(p, feeder)
                throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled")
            }
            if let limit = stallAfter, watch.isStalled(limit: limit) {
                stop(p, feeder)
                throw FFmpegStall(note: ProxyRenderer.stallNote(limit, retriedInSoftware: false))
            }
            usleep(200_000)
        }
        if let feeder {
            // ffmpeg is done. A decoder still writing gets the closed pipe from the pump and ends by itself; one that has
            // not ended within 2 s (decoding a frame nobody wants) is terminated — its exit code then says nothing.
            let deadline = Date().addingTimeInterval(2)
            while feeder.isRunning && Date() < deadline { usleep(20_000) }
            if feeder.isRunning { feeder.terminate() }
            feeder.waitUntilExit()
            if feeder.terminationReason == .exit, feeder.terminationStatus != 0, !feederCut.isSet {
                let error = ToolError(tool: feeder.executableURL?.lastPathComponent ?? "decoder", status: feeder.terminationStatus,
                                      message: feederStderr?.drain() ?? "")
                let explained = explainFeederError?(error) ?? error
                if explained is RawDecoderError { log?("\(error)") }   // the decoder's own words for the log
                throw explained
            }
        }
        guard p.process.terminationStatus == 0 else {
            // drain: ffmpeg has ended, the whole stderr tail (the handler may lag behind the exit — task 29)
            throw ToolError(tool: "ffmpeg", status: p.process.terminationStatus, message: p.drainStderr())
        }
    }

    /// SIGTERM, at most `killWait` for ffmpeg to end, then SIGKILL — never an unbounded wait (a process stuck in a GPU
    /// driver may survive even that; the job goes on without it and the log says so).
    private func stop(_ p: ToolProcess, _ feeder: Process?) {
        let pid = p.process.processIdentifier
        p.process.terminate()
        if let feeder, feeder.isRunning { feeder.terminate() }
        let deadline = Date().addingTimeInterval(ProxyRenderer.killWait)
        while p.process.isRunning && Date() < deadline { usleep(50_000) }
        guard p.process.isRunning else { return }
        kill(pid, SIGKILL)
        if let feeder, feeder.isRunning { kill(feeder.processIdentifier, SIGKILL) }
        usleep(500_000)
        if p.process.isRunning { log?("ffmpeg (pid \(pid)) did not end \(Int(ProxyRenderer.killWait)) s after kill") }
    }
}
