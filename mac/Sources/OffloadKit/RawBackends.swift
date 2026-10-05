import Foundation

/// Vendor decoders for formats ffmpeg cannot read. All of them hand ffmpeg footage in the camera's
/// log encoding, so the same LUT / scale / burn-in chain applies to every camera.
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

struct BRAWBackend: RawBackend {
    let format = ClipFormat.braw
    let missingHint = "Blackmagic RAW: chybí braw-decode nebo Blackmagic RAW Player/SDK"

    func isAvailable(_ tools: ExternalTools) -> Bool { tools.brawDecoder != nil }

    private struct Info: Decodable {
        let width: Int, height: Int, sourceWidth: Int, sourceHeight: Int
        let fps: Double, frames: Int, timecode: String, camera: String
    }

    private func info(_ path: String, scale: String, tools: ExternalTools) throws -> Info {
        let out = try ToolProcess.run(tools.brawDecoder!, ["--info", "--scale", scale, path])
        return try JSONDecoder().decode(Info.self, from: Data(out.utf8))
    }

    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        let i = try info(path, scale: "eighth", tools: tools)
        var c = ClipInfo()
        c.codec = "Blackmagic RAW"
        c.width = i.sourceWidth
        c.height = i.sourceHeight
        c.frameRate = RawBackends.rational(i.fps)
        c.frames = i.frames
        c.duration = i.fps > 0 ? Double(i.frames) / i.fps : nil
        c.timecode = i.timecode.isEmpty ? nil : i.timecode
        c.camera = i.camera.isEmpty ? nil : i.camera
        return c
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

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        let target = singleFrameAt == nil ? settings.height : 180
        let s = scale(sourceHeight: c.height ?? 2160, target: target)
        let dims = try info(path, scale: s, tools: tools)
        var args = ["--scale", s]
        if let t = singleFrameAt {
            args += ["--start", String(BRAWBackend.startFrame(seconds: t, fps: dims.fps, frames: dims.frames)), "--count", "1"]
        }
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: tools.brawDecoder!)
        feeder.arguments = args + [path]
        return FrameSource(inputArgs: ["-f", "rawvideo", "-pix_fmt", "rgb48le", "-s", "\(dims.width)x\(dims.height)",
                                       "-r", c.frameRate ?? "25", "-i", "-"],
                           feeder: feeder, audioFromInput: false)
    }
}

// MARK: - RED (REDline → ProRes LT intermediate)

struct R3DBackend: RawBackend {
    let format = ClipFormat.r3d
    let missingHint = "RED R3D: nainstalujte REDCINE-X PRO (REDline)"

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

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        guard singleFrameAt == nil else {
            // Thumbnails come from the finished proxy instead (one REDline run per clip is enough).
            throw ToolError(tool: "REDline", status: -1, message: "thumbnail from proxy")
        }
        let dir = try RawBackends.freshDir(tempDir, "r3d")
        let h = c.height ?? 4320
        let res = h / 4 >= settings.height ? "4" : (h / 2 >= settings.height ? "3" : "1")
        var args = ["--i", path, "--outDir", dir, "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", res, "--useMeta"]
        if settings.lutPath != nil {
            // The LUT expects log: IPP2, REDWideGamutRGB / Log3G10.
            args += ["--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"]
        }
        try ToolProcess.run(tools.redline!, args)
        guard let mov = RawBackends.newestFile(in: dir, ext: "mov") else {
            throw ToolError(tool: "REDline", status: -1, message: "no output in \(dir)")
        }
        return FrameSource(inputArgs: ["-i", mov], cleanup: [dir])
    }
}

// MARK: - ARRIRAW (ARRI Reference Tool CLI → ProRes LT MXF intermediate)

struct ARRIRAWBackend: RawBackend {
    let format = ClipFormat.arriraw
    let missingHint = "ARRIRAW: nainstalujte ARRI Reference Tool (art-cmd)"

    func isAvailable(_ tools: ExternalTools) -> Bool { tools.artCmd != nil }

    /// `art-cmd export` writes the clip's metadata as JSON: lists of metadata sets per clip, plus one entry per frame.
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo {
        let json = NSTemporaryDirectory() + "art-meta-\(UUID().uuidString).json"
        defer { try? FileManager.default.removeItem(atPath: json) }
        // art-cmd writes art.log into the working directory — never let that be the card or a destination.
        try ToolProcess.run(tools.artCmd!, ["export", "--input", path, "--output", json, "--skip-audio", "--skip-look"],
                            cwd: NSTemporaryDirectory())
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
        c.frames = int("clipDuration")
        let parts = (c.frameRate ?? "").split(separator: "/").compactMap { Double($0) }
        let fps = parts.count == 2 ? parts[0] / parts[1] : parts.first
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

    /// The LUT decides which log it needs when its name says so; otherwise the clip's own log encoding
    /// from metadata (ALEXA 35 family → LogC4, older ALEXAs → LogC3).
    func colorSpace(for lut: String?, info: ClipInfo) -> String {
        let name = ((lut ?? "") as NSString).lastPathComponent.lowercased()
        if name.contains("logc4") { return "AWG4/D65/LogC4" }
        if name.contains("logc3") || name.contains("logc ") || name.contains("logc_") || name.contains("logc-") { return "AWG3/D65/LogC3" }
        if let log = info.logEncoding?.lowercased() { return log.contains("4") ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3" }
        return (info.camera ?? "").lowercased().contains("35") ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3"
    }

    func source(_ path: String, info c: ClipInfo, settings: ProxySettings, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        guard singleFrameAt == nil else { throw ToolError(tool: "art-cmd", status: -1, message: "thumbnail from proxy") }
        let dir = try RawBackends.freshDir(tempDir, "ari")
        let out = dir + "/intermediate.mxf"
        var args = ["process", "--input", path, "--video-codec", "prores422lt", "--deflip", "--output", out]
        var cropped = false
        let sq = c.squeeze ?? 1
        // Frame lines are defined on the desqueezed picture: compute the crop there, then map it back to sensor pixels.
        if let w = c.width, let h = c.height,
           let fc = settings.frameCrop(width: Int(Double(w) * sq), height: h) {
            let crop = (x: Int(Double(fc.x) / sq) & ~1, y: fc.y, w: Int(Double(fc.w) / sq) & ~1, h: fc.h)
            // Crop on the sensor image before debayer/scaling: full detail inside the frame lines.
            // Size and desqueeze go into the same file (art-cmd refuses --desqueeze / --output-height with it,
            // and --output-height would stretch the crop back to the sensor's aspect).
            let outH = settings.height & ~1  // same height for every proxy, whatever the crop
            let outW = Int((Double(outH) * Double(crop.w) * sq / Double(crop.h) / 2).rounded()) * 2
            let params: [String: Any] = [
                "inputCrop": ["displayXOffsetOverride": crop.x, "displayYOffsetOverride": crop.y,
                              "displayWidthOverride": crop.w, "displayHeightOverride": crop.h],
                "outputScaling": ["targetWidth": outW, "targetHeight": outH],
            ]
            let file = dir + "/parameters.json"
            try JSONSerialization.data(withJSONObject: params).write(to: URL(fileURLWithPath: file))
            args += ["--parameters", file]
            cropped = true
        } else {
            args += ["--desqueeze", "--output-height", String(settings.height)]
        }
        if settings.lutPath != nil {
            args += ["--target-colorspace", colorSpace(for: settings.lutPath, info: c)]
        } else {
            args += ["--target-colorspace", "Rec.709/D65/BT.1886", "--embedded-look"]
        }
        try ToolProcess.run(tools.artCmd!, args, cwd: dir)
        guard FileManager.default.fileExists(atPath: out) else { throw ToolError(tool: "art-cmd", status: -1, message: "no output") }
        return FrameSource(inputArgs: ["-i", out], alreadyCropped: cropped, cleanup: [dir])
    }
}

// MARK: - Canon Cinema RAW Light — no command-line decoder exists for macOS (Intel)

struct CanonRawBackend: RawBackend {
    let format = ClipFormat.canonRaw
    let missingHint = "Canon Cinema RAW Light: pro macOS neexistuje dekodér pro příkazovou řádku — zkopírováno a ověřeno, proxy přeskočeno"

    func isAvailable(_ tools: ExternalTools) -> Bool { false }
    func probe(_ path: String, tools: ExternalTools) throws -> ClipInfo { throw ToolError(tool: "Canon RAW", status: -1, message: missingHint) }
    func source(_ path: String, info: ClipInfo, settings: ProxySettings, tools: ExternalTools,
                tempDir: String, singleFrameAt: Double?) throws -> FrameSource {
        throw ToolError(tool: "Canon RAW", status: -1, message: missingHint)
    }
}
