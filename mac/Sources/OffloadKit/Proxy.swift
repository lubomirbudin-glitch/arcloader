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

    var audioArgs: [String] {
        switch self {
        case .h264, .hevc: return ["-c:a", "aac", "-b:a", "192k"]
        case .proresProxy, .proresLT, .prores422, .prores422HQ: return ["-c:a", "pcm_s16le"]
        }
    }
}

public struct ProxySettings: Codable, Equatable {
    public var enabled = false
    public var height = 1080
    public var codec: ProxyCodec = .h264
    public var bitrateMbps = 10
    /// .cube 3D LUT applied after downscaling (cheap at proxy resolution).
    public var lutPath: String?
    public var burnFilename = true
    public var burnTimecode = true
    public var audio = true
    /// Skip the low-res files cameras record themselves (Sony SUB, PROXY folders).
    public var skipCameraProxies = true
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
        burnFilename = try c.decodeIfPresent(Bool.self, forKey: .burnFilename) ?? d.burnFilename
        burnTimecode = try c.decodeIfPresent(Bool.self, forKey: .burnTimecode) ?? d.burnTimecode
        audio = try c.decodeIfPresent(Bool.self, forKey: .audio) ?? d.audio
        skipCameraProxies = try c.decodeIfPresent(Bool.self, forKey: .skipCameraProxies) ?? d.skipCameraProxies
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
    /// Intermediate files to delete once ffmpeg is done.
    var cleanup: [String] = []

    func removeIntermediates() {
        for p in cleanup { try? FileManager.default.removeItem(atPath: p) }
    }
}

/// Per-format decoding for formats ffmpeg cannot read itself.
protocol RawBackend {
    var format: ClipFormat { get }
    var missingHint: String { get }
    func isAvailable(_ tools: ExternalTools) -> Bool
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo
    /// `singleFrameAt` (seconds) asks for just one frame — used for report thumbnails.
    func source(_ path: String, info: ClipInfo, settings: ProxySettings, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource
}

/// Where a report thumbnail is taken: frame number, its time, and whether it is the clip's last frame.
struct FramePosition: Equatable {
    let seconds: Double
    let frame: Int
    let fromEnd: Bool
}

public final class ProxyRenderer {
    public let settings: ProxySettings
    public let tools: ExternalTools
    let backends: [ClipFormat: RawBackend]
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

    public init(settings: ProxySettings, tools: ExternalTools) {
        self.settings = settings
        self.tools = tools
        backends = Dictionary(uniqueKeysWithValues: RawBackends.all.map { ($0.format, $0) })
        tempDir = NSTemporaryDirectory() + "offload-" + UUID().uuidString.prefix(8)
        try? IO.mkdirs(tempDir)
        // Filter graphs are picky about quoting; a copy at a plain path avoids every escaping problem.
        if let lut = settings.lutPath, !lut.isEmpty {
            try? FileManager.default.removeItem(atPath: lutCopy)
            try? FileManager.default.copyItem(atPath: lut, toPath: lutCopy)
        }
    }

    deinit { try? FileManager.default.removeItem(atPath: tempDir) }

    var lutCopy: String { tempDir + "/look.cube" }
    var hasLUT: Bool { FileManager.default.fileExists(atPath: lutCopy) }

    /// nil when the clip can be rendered, otherwise the reason it cannot.
    public func unsupportedReason(_ clip: Clip) -> String? {
        guard tools.ffmpeg != nil else { return "ffmpeg not found" }
        switch clip.format {
        case .video: return nil
        case .unknown: return "unknown format"
        default:
            guard let b = backends[clip.format] else { return "\(clip.format.label): no decoder available" }
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
            return try FFProbe.probe(ffprobe, path)
        }
        guard let b = backends[clip.format], b.isAvailable(tools) else {
            throw ToolError(tool: clip.format.label, status: -1, message: unsupportedReason(clip) ?? "")
        }
        return try b.probe(path, tools: tools)
    }

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
    static func usesHardwareDecode(_ codec: String?) -> Bool {
        let c = (codec ?? "").lowercased()
        return c.hasPrefix("h264") || c.hasPrefix("hevc")
    }

    /// The note at a clip whose ffmpeg stalled (texts shared with Windows `StallNote`).
    static func stallNote(_ seconds: TimeInterval, retriedInSoftware: Bool) -> String {
        "ffmpeg se zasekl (\(String(format: "%.0f", seconds)) s bez nového snímku)"
            + (retriedInSoftware ? " i bez hardwarového dekódování; proxy nevznikla" : "")
    }

    private func frameSource(_ clip: Clip, cardPath: String, info: ClipInfo, singleFrameAt: Double?, hwaccel: Bool) throws -> FrameSource {
        let path = cardPath + "/" + clip.path
        if clip.format == .video {
            // VideoToolbox only for H.264/HEVC proxies (usesHardwareDecode); thumbnails always decode in software.
            return FrameSource(inputArgs: hwaccel ? ["-hwaccel", "videotoolbox", "-i", path] : ["-i", path])
        }
        guard let b = backends[clip.format] else { throw ToolError(tool: clip.format.label, status: -1, message: "no decoder") }
        return try b.source(path, info: info, settings: settings, tools: tools, tempDir: tempDir, singleFrameAt: singleFrameAt)
    }

    /// First, middle and last frame as small JPEGs with captions. When `isCancelled` turns true the clip simply
    /// gets no thumbnails. One bad position does not cost the others; if none works, the last error is thrown.
    public func thumbnails(_ clip: Clip, cardPath: String, info: ClipInfo, applyLUT: Bool = true,
                           isCancelled: () -> Bool = { false }) throws -> [Thumbnail] {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "ffmpeg not found") }
        let filter = FilterGraph.thumbnail(lutFile: hasLUT && applyLUT ? lutCopy : nil, premap: false)
        let video = clip.format == .video
        var result: [Thumbnail] = []
        var lastError: Error?
        for pos in ProxyRenderer.framePositions(info) {
            if isCancelled() { return [] }
            let out = tempDir + "/thumb-\(UUID().uuidString).jpg"
            defer { unlink(out) }
            do {
                let src = try frameSource(clip, cardPath: cardPath, info: info, singleFrameAt: pos.seconds, hwaccel: false)
                defer { src.removeIntermediates() }
                var args = ["-hide_banner", "-loglevel", "error", "-y"]
                if video { args += ["-ss", ProxyRenderer.seekSeconds(info, pos)] }
                args += src.inputArgs
                // Last frame: decode the final second and keep overwriting the image — the last write is the last frame.
                args += video && pos.fromEnd ? ["-map", "0:v:0", "-vf", filter, "-update", "1"] : ["-frames:v", "1", "-vf", filter]
                args += ["-q:v", "5", out]
                try runFFmpeg(ffmpeg, args, feeder: src.feeder, duration: nil, progress: nil, isCancelled: isCancelled)
                guard FileManager.default.fileExists(atPath: out) else {
                    throw ToolError(tool: "ffmpeg", status: 0, message: "no frame")
                }
                result.append(Thumbnail(jpeg: try Data(contentsOf: URL(fileURLWithPath: out)), label: ProxyRenderer.label(info, pos)))
            } catch {
                if isCancelled() { return [] }
                lastError = error
            }
        }
        if result.isEmpty, let lastError { throw lastError }
        return result
    }

    /// Renders the proxy. A render that stalls (no progress for `stallTimeout`) is killed. With hardware decoding the clip
    /// is then retried once in software — as is one that failed with it; without, the stall fails the proxy. Cancelling
    /// never retries. Notes and log lines as on Windows.
    public func render(_ clip: Clip, cardPath: String, info: ClipInfo, output: String,
                       progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool) throws {
        guard let ffmpeg = tools.ffmpeg else { throw ToolError(tool: "ffmpeg", status: -1, message: "not found") }
        let hw = clip.format == .video && ProxyRenderer.usesHardwareDecode(info.codec)
        let first: Error
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, hwaccel: hw, output: output,
                           progress: progress, isCancelled: isCancelled)
            return
        } catch let e as FFmpegStall where !hw && !isCancelled() {
            throw FFmpegStall(note: e.note + "; proxy nevznikla")
        } catch where hw && (error is FFmpegStall || error is ToolError) && !isCancelled() {
            first = error   // hardware decoding stalled or failed: once more in software
        }
        if isCancelled() { throw ToolError(tool: "ffmpeg", status: -1, message: "cancelled") }
        log?("proxy \(clip.name): with hardware decoding: \(first) — retrying in software")
        do {
            try renderOnce(ffmpeg, clip, cardPath: cardPath, info: info, hwaccel: false, output: output,
                           progress: progress, isCancelled: isCancelled)
            log?("proxy \(clip.name): software decoding succeeded")
        } catch where (error is FFmpegStall || error is ToolError) && !isCancelled() {
            log?("proxy \(clip.name): software decoding failed too: \(error)")
            switch (first, error) {
            case (is FFmpegStall, is FFmpegStall):
                throw ProxyRetryFailed(note: ProxyRenderer.stallNote(stallTimeout, retriedInSoftware: true))
            case (is FFmpegStall, _):
                throw ProxyRetryFailed(note: "\(first); bez hardwarového dekódování selhal: \(error)")
            default:
                throw ProxyRetryFailed(note: "\(error); s hardwarovým dekódováním předtím: \(first)")
            }
        }
    }

    private func renderOnce(_ ffmpeg: String, _ clip: Clip, cardPath: String, info: ClipInfo, hwaccel: Bool, output: String,
                            progress: @escaping (Double) -> Void, isCancelled: @escaping () -> Bool) throws {
        let src = try frameSource(clip, cardPath: cardPath, info: info, singleFrameAt: nil, hwaccel: hwaccel)
        defer { src.removeIntermediates() }
        let outHeight = min(settings.height, info.height ?? settings.height) & ~1
        let vf = FilterGraph.proxy(settings, outHeight: outHeight, alreadyCropped: src.alreadyCropped,
                                   lutFile: hasLUT ? lutCopy : nil, premap: false,
                                   burnIn: burnIn(clip: clip, info: info, height: outHeight))
        var args = ["-hide_banner", "-loglevel", "error", "-y", "-nostats", "-progress", "pipe:1"] + src.inputArgs
        args += ["-map", "0:v:0"]
        if settings.audio && info.audioChannels > 0 && src.audioFromInput {
            args += ["-map", "0:a:0?"] + settings.codec.audioArgs
        }
        args += ["-vf", vf] + settings.codec.encoderArgs(bitrateMbps: settings.bitrateMbps) + FilterGraph.rec709Args
        if let tc = info.timecode, Timecode.isValid(tc) { args += ["-timecode", tc] }

        let dir = (output as NSString).deletingLastPathComponent
        try IO.mkdirs(dir)
        let part = dir + "/." + (output as NSString).lastPathComponent + ".part"
        args += ["-f", "mov", part]
        do {
            try runFFmpeg(ffmpeg, args, feeder: src.feeder, duration: info.duration, progress: progress,
                          isCancelled: isCancelled, stallAfter: stallTimeout)
            guard rename(part, output) == 0 else { throw IOError("rename", output) }
        } catch {
            unlink(part)
            throw error
        }
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
            let tc = info.timecode.flatMap { isValidTimecode($0) ? $0 : nil } ?? "00:00:00:00"
            let escaped = tc.replacingOccurrences(of: ":", with: "\\:").replacingOccurrences(of: ";", with: "\\;")
            f.append("drawtext=\(common):timecode='\(escaped)':rate=\(rate):x=w-tw-\(margin)")
        }
        return f
    }

    func isValidTimecode(_ tc: String) -> Bool { Timecode.isValid(tc) }

    /// `stallAfter` (renders only, with -progress pipe:1 in the args): ffmpeg is killed and `FFmpegStall` thrown when it
    /// has reported no new frame or output time and taken no data from the feeder for that long (3× before the first
    /// progress). Cancelling is checked first and wins.
    func runFFmpeg(_ ffmpeg: String, _ args: [String], feeder: Process?, duration: Double?,
                   progress: ((Double) -> Void)?, isCancelled: () -> Bool, stallAfter: TimeInterval? = nil) throws {
        let p = ToolProcess(ffmpeg, args)
        p.captureStderr()
        let watch = ProgressWatch()
        var pump: Thread?
        if let feeder {
            // The decoder's frames go through a pump: every chunk ffmpeg takes is progress (a slow RAW decoder is not stuck).
            let fromFeeder = Pipe(), toFFmpeg = Pipe()
            feeder.standardOutput = fromFeeder
            feeder.standardError = FileHandle.nullDevice
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
                        if w <= 0 { break pumping }   // ffmpeg is gone (EPIPE, no signal thanks to F_SETNOSIGPIPE)
                        sent += w
                    }
                    watch.dataFlowed()
                }
                try? toFFmpeg.fileHandleForWriting.close()
            }
        } else {
            p.process.standardInput = FileHandle.nullDevice
        }
        p.onStdoutLine { line in
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
        while p.process.isRunning {
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
        if let feeder, feeder.isRunning { feeder.terminate() }
        feeder?.waitUntilExit()
        guard p.process.terminationStatus == 0 else {
            throw ToolError(tool: "ffmpeg", status: p.process.terminationStatus, message: p.stderrText)
        }
        if let feeder, feeder.terminationStatus != 0, feeder.terminationReason == .exit {
            throw ToolError(tool: (feeder.executableURL?.lastPathComponent ?? "decoder"), status: feeder.terminationStatus, message: "")
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
