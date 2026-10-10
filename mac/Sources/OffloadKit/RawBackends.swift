import Foundation

/// Vendor decoders for formats ffmpeg cannot read. When a LUT is applied they hand ffmpeg the camera's log
/// (`RawLook.log`; BRAW always decodes in the clip's gamma), so the same LUT / scale / burn-in chain fits every
/// camera; without a LUT, RED delivers its own Rec.709 look and ARRI the clip's embedded look.
enum RawBackends {
    static let all: [RawBackend] = [BRAWBackend(), R3DBackend(), ARRIRAWBackend(), CanonRawBackend()]

    /// ffmpeg wants a rational frame rate; cameras report floats.
    static func rational(_ fps: Double) -> String {
        for (v, r) in [(23.976, "24000/1001"), (29.97, "30000/1001"), (47.952, "48000/1001"), (59.94, "60000/1001"), (119.88, "120000/1001")]
            where abs(fps - v) < 0.01 { return r }
        return fps == fps.rounded() ? String(Int(fps)) : String(format: "%.3f", fps)
    }

    static func newestFile(in dir: String, ext: String) -> String? {
        let fm = FileManager.default
        let items = (try? fm.contentsOfDirectory(atPath: dir)) ?? []
        return items.filter { ($0 as NSString).pathExtension.lowercased() == ext }
            .map { dir + "/" + $0 }
            .max { ((try? fm.attributesOfItem(atPath: $0)[.modificationDate] as? Date) ?? .distantPast)
                < ((try? fm.attributesOfItem(atPath: $1)[.modificationDate] as? Date) ?? .distantPast) }
    }

    static func freshDir(_ tempDir: String, _ name: String) throws -> String {
        let d = tempDir + "/" + name + "-" + UUID().uuidString.prefix(6)
        try IO.mkdirs(d)
        return d
    }
}

// MARK: - Blackmagic RAW (braw-decode helper → raw RGB48 frames on a pipe)

/// A RAW decoder's error turned into a note for the clip (Windows `RawDecoderException`). `description` is the note;
/// the decoder's own error, with its stderr (e.g. the library it loaded), stays in `decoderError` for the job's log.
struct RawDecoderError: Error, CustomStringConvertible {
    let note: String
    let decoderError: ToolError

    var description: String { note }
}

struct BRAWBackend: RawBackend {
    let format = ClipFormat.braw
    let missingHint = "Blackmagic RAW: braw-decode or the Blackmagic RAW library is missing (SDK, Player or DaVinci Resolve)"

    /// How long `braw-decode --info` may take: opening a clip is quick; a hung SDK must never hang the job (the clip
    /// then gets a note instead).
    static let defaultInfoTimeout: TimeInterval = 60

    /// Decoder folder → library path. Injectable so tests do not depend on what is installed on the Mac.
    var findLibrary: (String) -> String? = { BrawLibrary.find(decoderDir: $0) }
    /// Tests shorten it.
    var infoTimeout: TimeInterval = BRAWBackend.defaultInfoTimeout

    /// braw-decode alone is not enough: without BlackmagicRawAPI.framework it can only fail (exit 3).
    func isAvailable(_ tools: ExternalTools) -> Bool {
        guard let exe = tools.brawDecoder else { return false }
        return findLibrary(BRAWBackend.decoderDir(exe)) != nil
    }

    /// The folder braw-decode looks in first ("@exe": its real path, symlinks resolved).
    static func decoderDir(_ exe: String) -> String {
        URL(fileURLWithPath: exe).resolvingSymlinksInPath().deletingLastPathComponent().path
    }

    /// One line of `braw-decode --info`. gamma / gamut / colorScienceGen only when the SDK reports them (braw-decode
    /// since task 27, and Windows); an older decoder leaves them out.
    struct Info: Decodable {
        let width: Int, height: Int, sourceWidth: Int, sourceHeight: Int
        let fps: Double, frames: Int, timecode: String, camera: String
        let gamma: String?, gamut: String?, colorScienceGen: Int?
    }

    static func parseInfo(_ json: String) throws -> Info {
        try JSONDecoder().decode(Info.self, from: Data(json.utf8))
    }

    static func clipInfo(_ i: Info) -> ClipInfo {
        var c = ClipInfo()
        c.codec = "Blackmagic RAW"
        c.width = i.sourceWidth
        c.height = i.sourceHeight
        c.frameRate = RawBackends.rational(i.fps)
        c.frames = i.frames
        c.duration = i.fps > 0 ? Double(i.frames) / i.fps : nil
        c.timecode = i.timecode.isEmpty ? nil : i.timecode
        c.camera = i.camera.isEmpty ? nil : i.camera
        c.gamma = i.gamma.flatMap { $0.isEmpty ? nil : $0 }
        c.gamut = i.gamut.flatMap { $0.isEmpty ? nil : $0 }
        c.colorScienceGen = i.colorScienceGen
        return c
    }

    private func info(_ path: String, scale: String, tools: ExternalTools) throws -> Info {
        let out: String
        do {
            out = try ToolProcess.run(tools.brawDecoder!, ["--info", "--scale", scale, path], timeout: infoTimeout)
        } catch let e as ToolError {
            throw explain(e, tools: tools)
        }
        return try BRAWBackend.parseInfo(out)
    }

    /// braw-decode's exit codes 3 (library unusable), 4 (clip cannot be opened) and 5 (a frame failed) as a note for
    /// the clip; the original error, with braw-decode's stderr, stays inside it for the log. Anything else (a timeout,
    /// a usage error) is returned as it is. Texts as Windows `BrawBackend.Explain`.
    func explain(_ e: ToolError, tools: ExternalTools) -> Error {
        let lib = tools.brawDecoder.flatMap { findLibrary(BRAWBackend.decoderDir($0)) } ?? BrawLibrary.frameworkName
        let note: String
        switch e.status {
        case 3: note = "Blackmagic RAW: cannot use the library \(lib) — install the current Blackmagic RAW Player"
        case 4: note = "Blackmagic RAW: cannot open the clip — it is damaged, or the library \(lib) is older than the camera (install the current Blackmagic RAW Player)"
        case 5: note = "Blackmagic RAW: could not decode a frame of the clip (\(BRAWBackend.lastLine(e.message)))"
        default: return e
        }
        return RawDecoderError(note: note, decoderError: e)
    }

    /// braw-decode's last stderr line without its "braw-decode: " prefix.
    static func lastLine(_ stderr: String) -> String {
        let line = stderr.split(separator: "\n").map { $0.trimmingCharacters(in: .whitespaces) }.last { !$0.isEmpty } ?? ""
        return line.hasPrefix("braw-decode: ") ? String(line.dropFirst("braw-decode: ".count)) : line
    }

    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        BRAWBackend.clipInfo(try info(path, scale: "eighth", tools: tools))
    }

    /// Smallest decode scale that still covers the target height — decoding less is much faster.
    private func scale(sourceHeight: Int, target: Int) -> String {
        for (name, div) in [("eighth", 8), ("quarter", 4), ("half", 2)] where sourceHeight / div >= target { return name }
        return "full"
    }

    /// The frame braw-decode starts at for a thumbnail `seconds` into the clip. Rounded: `seconds` comes from the
    /// exact rational rate (24000/1001), braw-decode reports a rounded fps (23.976) — truncating would land a frame early.
    static func startFrame(seconds: Double, fps: Double, frames: Int) -> Int {
        let rate = fps > 0 ? fps : 25
        return min(max(0, frames - 1), max(0, Int((seconds * rate).rounded())))
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        let target = singleFrameAt == nil ? settings.height : 180
        let s = scale(sourceHeight: c.height ?? 2160, target: target)
        let dims = try info(path, scale: s, tools: tools)
        var args = ["--scale", s]
        if let t = singleFrameAt {
            args += ["--start", String(BRAWBackend.startFrame(seconds: t, fps: dims.fps, frames: dims.frames)), "--count", "1"]
        }
        let feeder = Process()
        feeder.qualityOfService = .userInitiated
        feeder.executableURL = URL(fileURLWithPath: tools.brawDecoder!)
        feeder.arguments = args + [path]
        // braw-decode hands over full-range RGB (RGBU16) in the clip's own gamma: a built-in LUT needs the pre-map first.
        return FrameSource(inputArgs: ["-f", "rawvideo", "-pix_fmt", "rgb48le", "-s", "\(dims.width)x\(dims.height)",
                                       "-r", c.frameRate ?? "25", "-i", "-"],
                           feeder: feeder, audioFromInput: false, fullRange: true,
                           explainFeederError: { self.explain($0, tools: tools) })
    }
}

// MARK: - RED (REDline → ProRes LT intermediate)

struct R3DBackend: RawBackend {
    let format = ClipFormat.r3d
    let missingHint = "RED R3D: install REDCINE-X PRO (REDline)"

    func isAvailable(_ tools: ExternalTools) -> Bool { tools.redline != nil }

    /// `--printMeta 1` prints "Key: value" lines; the key names differ between versions, so match loosely.
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        let out = try ToolProcess.run(tools.redline!, ["--i", path, "--printMeta", "1"])
        var meta: [String: String] = [:]
        for line in out.split(separator: "\n") {
            guard let colon = line.firstIndex(of: ":") else { continue }
            let k = line[..<colon].trimmingCharacters(in: .whitespaces).lowercased()
            let v = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            if meta[k] == nil { meta[k] = v }
        }
        func find(_ needles: [String]) -> String? {
            for n in needles { if let hit = meta.first(where: { $0.key.contains(n) })?.value, !hit.isEmpty { return hit } }
            return nil
        }
        var c = ClipInfo()
        c.codec = "REDCODE RAW"
        c.width = find(["frame width", "width"]).flatMap { Int($0.filter(\.isNumber)) }
        c.height = find(["frame height", "height"]).flatMap { Int($0.filter(\.isNumber)) }
        if let fps = find(["record fps", "timebase", "fps", "frame rate"]).flatMap({ Double($0.split(separator: " ").first ?? "") }) {
            c.frameRate = RawBackends.rational(fps)
            if let n = find(["total frames", "frame count", "frames"]).flatMap({ Int($0.filter(\.isNumber)) }) {
                c.frames = n
                c.duration = Double(n) / fps
            }
        }
        c.timecode = find(["abs tc", "absolute timecode", "timecode", "edge tc"])
        c.camera = find(["camera model", "camera"])
        return c
    }

    /// REDline arguments: the smallest decode resolution that covers the proxy height; log (IPP2, REDWideGamutRGB /
    /// Log3G10) only when a LUT follows, otherwise RED's own Rec.709 look.
    static func args(input: String, outDir: String, info c: ClipInfo, settings: ProxySettings, look: RawLook) -> [String] {
        let h = c.height ?? 4320
        let res = h / 4 >= settings.height ? "4" : (h / 2 >= settings.height ? "3" : "1")
        var args = ["--i", input, "--outDir", outDir, "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", res, "--useMeta"]
        if look.log { args += ["--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"] }
        return args
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        guard singleFrameAt == nil else {
            // Thumbnails come from the finished proxy instead (one REDline run per clip is enough).
            throw ToolError(tool: "REDline", status: -1, message: "thumbnail from proxy")
        }
        let dir = try RawBackends.freshDir(tempDir, "r3d")
        try ToolProcess.run(tools.redline!, R3DBackend.args(input: path, outDir: dir, info: c, settings: settings, look: look))
        guard let mov = RawBackends.newestFile(in: dir, ext: "mov") else {
            throw ToolError(tool: "REDline", status: -1, message: "no output in \(dir)")
        }
        return FrameSource(inputArgs: ["-i", mov], cleanup: [dir])
    }
}

// MARK: - ARRIRAW (ARRI Reference Tool CLI → ProRes LT MXF intermediate)

struct ARRIRAWBackend: RawBackend {
    let format = ClipFormat.arriraw
    let missingHint = "ARRIRAW: install ARRI Reference Tool (art-cmd)"

    func isAvailable(_ tools: ExternalTools) -> Bool { tools.artCmd != nil }

    /// `art-cmd export` writes the clip's metadata as JSON: lists of metadata sets per clip, plus one entry per frame.
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        let json = NSTemporaryDirectory() + "art-meta-\(UUID().uuidString).json"
        defer { try? FileManager.default.removeItem(atPath: json) }
        // art-cmd writes art.log into the working directory — never let that be the card or a destination.
        // Only the FIRST frame: a full export walks the metadata of every frame, i.e. reads the whole clip (minutes on a
        // 120 GB ARRIRAW 4.6K clip); clip metadata and the start timecode are all there in frame 0. The frame count then
        // comes from the container (ffprobe) or, for an .ari sequence, from the number of frame files.
        try ToolProcess.run(tools.artCmd!, ["export", "--input", path, "--output", json, "--skip-audio", "--skip-look",
                                            "--start", "0", "--duration", "1"],
                            cwd: NSTemporaryDirectory(), timeout: 180)
        guard var root = try JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: json))) as? [String: Any] else {
            throw ToolError(tool: "art-cmd", status: -1, message: "unreadable metadata")
        }
        // Only the first frame matters (start timecode); the rest would just slow the walk down.
        if var fb = root["frameBasedMetadata"] as? [String: Any], let frames = fb["frames"] as? [Any] {
            fb["frames"] = Array(frames.prefix(1))
            root["frameBasedMetadata"] = fb
        }
        var flat: [String: Any] = [:]
        func walk(_ v: Any, _ prefix: String) {
            if let d = v as? [String: Any] { for (k, x) in d { walk(x, prefix + k + ".") } }
            else if let a = v as? [Any] { for x in a { walk(x, prefix) } }
            else if flat[String(prefix.dropLast())] == nil { flat[String(prefix.dropLast())] = v }
        }
        walk(root, "")
        func value(_ suffix: String) -> Any? { flat.first { $0.key.hasSuffix(suffix) }?.value }
        func int(_ suffix: String) -> Int? { (value(suffix) as? NSNumber)?.intValue ?? (value(suffix) as? String).flatMap { Int($0) } }

        var c = ClipInfo()
        c.codec = (value("videoCodec") as? String) ?? "ARRIRAW"
        c.width = int("storedSize.width") ?? int("displayRect.width")
        c.height = int("storedSize.height") ?? int("displayRect.height")
        if let rate = value("nativePictureEssenceDescriptor.file.sampleRate") as? String {
            c.frameRate = rate.hasSuffix("/1") ? String(rate.dropLast(2)) : rate
        }
        let parts = (c.frameRate ?? "").split(separator: "/").compactMap { Double($0) }
        let fps = parts.count == 2 ? parts[0] / parts[1] : parts.first
        // With --duration 1 art-cmd reports clipDuration = 1; trust it only when it is larger.
        if let n = int("clipDuration"), n > 1 { c.frames = n }
        else { c.frames = ARRIRAWBackend.frameCount(path, fps: fps, tools: tools) }
        if let n = c.frames, let fps, fps > 0 { c.duration = Double(n) / fps }
        c.timecode = value("frames.timecode") as? String
        c.camera = value("cameraModel") as? String
        c.logEncoding = value("sceneColorEncodingTransferCurve") as? String
        // Lens squeeze; ARRI writes it as a factor or in thousandths depending on the field.
        if let raw = flat.first(where: { $0.key.lowercased().contains("squeeze") && ($0.value as? NSNumber) != nil })?.value as? NSNumber {
            let v = raw.doubleValue > 10 ? raw.doubleValue / 1000 : raw.doubleValue
            if v > 1.01 { c.squeeze = v }
        }
        c.audioChannels = flat.keys.contains { $0.hasSuffix("nativeSoundEssenceDescriptorList.blockAlign") } ? 2 : 0
        return c
    }

    /// Frame count without reading the clip: an .ari/.arx sequence folder counts its frame files; an MXF takes the
    /// video stream's duration from ffprobe (the MXF index — fast) as `duration_ts` (time base 1/fps) or duration × fps.
    static func frameCount(_ path: String, fps: Double?, tools: ExternalTools) -> Int? {
        var isDir: ObjCBool = false
        if FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue {
            let n = ((try? FileManager.default.contentsOfDirectory(atPath: path)) ?? [])
                .filter { ["ari", "arx"].contains(($0 as NSString).pathExtension.lowercased()) }.count
            return n > 0 ? n : nil
        }
        guard let ffprobe = tools.ffprobe,
              let out = try? ToolProcess.run(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_entries",
                                                        "stream=duration_ts,time_base,duration:format=duration",
                                                        "-of", "json", path], timeout: 60),
              let root = try? JSONSerialization.jsonObject(with: Data(out.utf8)) as? [String: Any] else { return nil }
        let stream = (root["streams"] as? [[String: Any]])?.first ?? [:]
        let tb = (stream["time_base"] as? String).map { $0.split(separator: "/").compactMap { Double($0) } } ?? []
        if let ts = (stream["duration_ts"] as? NSNumber)?.intValue ?? (stream["duration_ts"] as? String).flatMap({ Int($0) }),
           tb.count == 2, let fps, fps > 0, abs(tb[1] / tb[0] - fps) < 0.01, ts > 0 {
            return ts
        }
        let seconds = (stream["duration"] as? String).flatMap(Double.init)
            ?? ((root["format"] as? [String: Any])?["duration"] as? String).flatMap(Double.init)
        guard let seconds, let fps, fps > 0, seconds > 0 else { return nil }
        return Int((seconds * fps).rounded())
    }

    /// The LUT decides which log it needs when its name says so; otherwise the clip's own log encoding
    /// from metadata, then the camera (ALEXA 35 family → LogC4, older ALEXAs → LogC3).
    static func colorSpace(lut: String?, info: ClipInfo) -> String {
        let name = ((lut ?? "") as NSString).lastPathComponent.lowercased()
        if name.contains("logc4") { return "AWG4/D65/LogC4" }
        if name.contains("logc3") || name.contains("logc ") || name.contains("logc_") || name.contains("logc-") { return "AWG3/D65/LogC3" }
        if let log = info.logEncoding?.lowercased() { return log.contains("4") ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3" }
        return isAlexa35(info.camera) ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3"
    }

    /// "ALEXA 35" / "ALEXA35" as a whole word — not "ALEXA Mini LF 35mm" or a serial number.
    static func isAlexa35(_ camera: String?) -> Bool {
        guard let camera else { return false }
        return camera.range(of: #"\bALEXA\s*35\b"#, options: [.regularExpression, .caseInsensitive]) != nil
    }

    /// art-cmd arguments and, for a frame-lines crop, the parameters JSON (written to `parametersFile`).
    /// Frame lines are defined on the desqueezed picture: the crop is computed there, then mapped back to sensor pixels
    /// and done before debayer/scaling (full detail inside the frame lines). Size and desqueeze go into the same file
    /// (art-cmd refuses --desqueeze / --output-height with it, and --output-height would stretch the crop back).
    static func args(input: String, output: String, info c: ClipInfo, settings: ProxySettings, look: RawLook,
                     parametersFile: String) -> (args: [String], parameters: Data?, cropped: Bool) {
        var args = ["process", "--input", input, "--video-codec", "prores422lt", "--deflip", "--output", output]
        var parameters: Data?
        let sq = c.squeeze ?? 1
        if let w = c.width, let h = c.height, let fc = settings.frameCrop(width: Int(Double(w) * sq), height: h) {
            let crop = (x: Int(Double(fc.x) / sq) & ~1, y: fc.y, w: Int(Double(fc.w) / sq) & ~1, h: fc.h)
            let outH = settings.height & ~1  // same height for every proxy, whatever the crop
            let outW = Int((Double(outH) * Double(crop.w) * sq / Double(crop.h) / 2).rounded()) * 2
            let params: [String: Any] = [
                "inputCrop": ["displayXOffsetOverride": crop.x, "displayYOffsetOverride": crop.y,
                              "displayWidthOverride": crop.w, "displayHeightOverride": crop.h],
                "outputScaling": ["targetWidth": outW, "targetHeight": outH],
            ]
            parameters = try? JSONSerialization.data(withJSONObject: params)
        }
        if parameters != nil {
            args += ["--parameters", parametersFile]
        } else {
            // --desqueeze only for anamorphic lenses: it halves art-cmd's speed and changes nothing for spherical ones.
            if (c.squeeze ?? 1) > 1.01 { args.append("--desqueeze") }
            args += ["--output-height", String(settings.height)]
        }
        if look.log {
            args += ["--target-colorspace", look.arriColorSpace ?? colorSpace(lut: nil, info: c)]
        } else {
            // The camera's embedded look (ARRI's display transform to Rec.709; plain LogC when the clip has no look).
            // Never with --target-colorspace: art-cmd refuses that for looks without DRT LUTs ("target-colorspace
            // argument is only valid for embedded looks with DRT LUTs") — and exits 0 without writing anything.
            args += ["--embedded-look"]
        }
        return (args, parameters, parameters != nil)
    }

    /// art-cmd's log lines that explain a failure, on one line: its [error]/[critical] lines (else the last three),
    /// without the timestamp and logger prefix.
    static func logTail(_ output: String) -> String {
        let lines = output.split(whereSeparator: \.isNewline).map { raw -> String in
            let s = String(raw).trimmingCharacters(in: .whitespaces)
            let bare = s.replacingOccurrences(of: #"^\[[^\]]*\]\s*\[[^\]]*\]\s*"#, with: "", options: .regularExpression)
            return String(bare.prefix(300))
        }.filter { !$0.isEmpty }
        let errors = lines.filter { $0.hasPrefix("[error]") || $0.hasPrefix("[critical]") }
        return (errors.isEmpty ? Array(lines.suffix(3)) : errors).joined(separator: " | ")
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        guard singleFrameAt == nil else { throw ToolError(tool: "art-cmd", status: -1, message: "thumbnail from proxy") }
        let dir = try RawBackends.freshDir(tempDir, "ari")
        let out = dir + "/intermediate.mxf"
        let file = dir + "/parameters.json"
        let a = ARRIRAWBackend.args(input: path, output: out, info: c, settings: settings, look: look, parametersFile: file)
        if let json = a.parameters { try json.write(to: URL(fileURLWithPath: file)) }
        // art-cmd logs (errors too) to stdout and may exit 0 without an output file: both cases quote its own lines.
        let run = try ToolProcess.runCombined(tools.artCmd!, a.args, cwd: dir)
        let log = ARRIRAWBackend.logTail(run.output)
        guard run.status == 0 else { throw ToolError(tool: "art-cmd", status: run.status, message: log) }
        guard FileManager.default.fileExists(atPath: out) else {
            throw ToolError(tool: "art-cmd", status: 0, message: "no output file" + (log.isEmpty ? "" : ": " + log))
        }
        return FrameSource(inputArgs: ["-i", out], alreadyCropped: a.cropped, cleanup: [dir])
    }
}

// MARK: - Canon Cinema RAW Light — no command-line decoder exists for macOS

struct CanonRawBackend: RawBackend {
    let format = ClipFormat.canonRaw
    let missingHint = "Canon Cinema RAW Light: there is no command-line decoder for macOS — copied and verified, proxy skipped"

    func isAvailable(_ tools: ExternalTools) -> Bool { false }
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo { throw ToolError(tool: "Canon RAW", status: -1, message: missingHint) }
    func source(_ path: String, info: ClipInfo, settings: ProxySettings, look: RawLook, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        throw ToolError(tool: "Canon RAW", status: -1, message: missingHint)
    }
}
