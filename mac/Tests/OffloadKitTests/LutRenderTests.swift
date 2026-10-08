import XCTest
@testable import OffloadKit

final class LutRenderTests: XCTestCase {
    private func builtin(_ id: String) -> LutUse { LutUse(source: "auto", id: id, name: "n", reason: "r", file: "/t/\(id).cube") }

    // Review focus 7: the pre-map only for a built-in LUT on a full-range source (BRAW RGB, video tagged pc).
    func testNeedsPreMapOnlyBuiltinLutOnFullRangeSource() {
        let braw = FrameSource(inputArgs: ["-i", "-"], fullRange: true)
        let video = FrameSource(inputArgs: ["-i", "a.mov"])
        var pc = ClipInfo()
        pc.colorRange = "pc"
        var tv = ClipInfo()
        tv.colorRange = "tv"
        XCTAssertTrue(ProxyRenderer.needsPreMap(builtin("bmd-film-gen5"), source: braw, info: ClipInfo()))
        XCTAssertTrue(ProxyRenderer.needsPreMap(builtin("sony-slog3-sgamut3"), source: video, info: pc))
        XCTAssertFalse(ProxyRenderer.needsPreMap(builtin("sony-slog3-sgamut3"), source: video, info: tv))
        XCTAssertFalse(ProxyRenderer.needsPreMap(builtin("sony-slog3-sgamut3"), source: video, info: ClipInfo()))
        XCTAssertFalse(ProxyRenderer.needsPreMap(TestMedia.customLut("/t/look.cube"), source: braw, info: ClipInfo()))   // custom: as is
        XCTAssertFalse(ProxyRenderer.needsPreMap(nil, source: braw, info: pc))
    }

    // RAW decoders hand over log only when a LUT follows; ARRI's target space follows a built-in ARRI LUT.
    func testLookForLogWhenAnyLutArriSpaceFromBuiltin() {
        var info = ClipInfo()
        info.camera = "ALEXA Mini"
        XCTAssertEqual(ProxyRenderer.lookFor(nil, info: info), RawLook(log: false, arriColorSpace: nil))
        XCTAssertEqual(ProxyRenderer.lookFor(.off("x"), info: info), RawLook(log: false, arriColorSpace: nil))
        XCTAssertEqual(ProxyRenderer.lookFor(builtin("arri-logc4-awg4"), info: info), RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"))
        XCTAssertEqual(ProxyRenderer.lookFor(builtin("arri-logc3-awg3"), info: info), RawLook(log: true, arriColorSpace: "AWG3/D65/LogC3"))
        XCTAssertEqual(ProxyRenderer.lookFor(builtin("red-log3g10-rwg"), info: info), RawLook(log: true, arriColorSpace: nil))
        XCTAssertEqual(ProxyRenderer.lookFor(TestMedia.customLut("/l/show_LogC4_709.cube"), info: info),
                       RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"))
        XCTAssertEqual(ProxyRenderer.lookFor(TestMedia.customLut("/l/look.cube"), info: info),
                       RawLook(log: true, arriColorSpace: "AWG3/D65/LogC3"))
    }

    func testR3DArgsPickResolutionAndLogOnlyWithLut() {
        var info = ClipInfo()
        info.height = 4320
        var s = ProxySettings()
        s.height = 1080
        XCTAssertEqual(R3DBackend.args(input: "/c/A.R3D", outDir: "/t/r3d", info: info, settings: s, look: RawLook(log: true, arriColorSpace: nil)),
                       ["--i", "/c/A.R3D", "--outDir", "/t/r3d", "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", "4",
                        "--useMeta", "--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"])
        XCTAssertFalse(R3DBackend.args(input: "a", outDir: "b", info: info, settings: s, look: RawLook(log: false, arriColorSpace: nil))
            .contains("--gammaCurve"))
        info.height = 2160
        XCTAssertTrue(R3DBackend.args(input: "a", outDir: "b", info: info, settings: s, look: RawLook(log: false, arriColorSpace: nil)).contains("3"))
        info.height = 1080
        XCTAssertTrue(R3DBackend.args(input: "a", outDir: "b", info: info, settings: s, look: RawLook(log: false, arriColorSpace: nil)).contains("1"))
    }

    func testArriArgsCropOnSensorRec709WithoutLut() throws {
        var info = ClipInfo()
        info.width = 4608
        info.height = 3164
        var s = ProxySettings()
        s.frameAspect = 1.66
        s.height = 1080
        let a = ARRIRAWBackend.args(input: "/c/c.mxf", output: "/t/i.mxf", info: info, settings: s,
                                    look: RawLook(log: false, arriColorSpace: nil), parametersFile: "/t/p.json")
        XCTAssertTrue(a.cropped)
        XCTAssertEqual(a.args, ["process", "--input", "/c/c.mxf", "--video-codec", "prores422lt", "--deflip", "--output", "/t/i.mxf",
                                "--parameters", "/t/p.json", "--embedded-look"])
        let p = try XCTUnwrap(try JSONSerialization.jsonObject(with: try XCTUnwrap(a.parameters)) as? [String: [String: Int]])
        XCTAssertEqual(p["inputCrop"], ["displayXOffsetOverride": 0, "displayYOffsetOverride": 194,
                                        "displayWidthOverride": 4608, "displayHeightOverride": 2774])
        XCTAssertEqual(p["outputScaling"], ["targetWidth": 1794, "targetHeight": 1080])
    }

    func testArriArgsWithoutCropDesqueezeAndLog() {
        var info = ClipInfo()
        info.camera = "ALEXA 35"
        var s = ProxySettings()
        s.height = 720
        let a = ARRIRAWBackend.args(input: "in", output: "out", info: info, settings: s,
                                    look: ProxyRenderer.lookFor(TestMedia.customLut("look.cube"), info: info), parametersFile: "p.json")
        XCTAssertFalse(a.cropped)
        XCTAssertNil(a.parameters)
        // spherical lens: no --desqueeze (it halves art-cmd's speed for nothing)
        XCTAssertEqual(a.args, ["process", "--input", "in", "--video-codec", "prores422lt", "--deflip", "--output", "out",
                                "--output-height", "720", "--target-colorspace", "AWG4/D65/LogC4"])
        var anamorphic = info
        anamorphic.squeeze = 2
        XCTAssertTrue(ARRIRAWBackend.args(input: "in", output: "out", info: anamorphic, settings: s,
                                          look: RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"), parametersFile: "p").args.contains("--desqueeze"))
        XCTAssertTrue(ARRIRAWBackend.args(input: "in", output: "out", info: ClipInfo(), settings: ProxySettings(),
                                          look: RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"), parametersFile: "p").args.contains("AWG4/D65/LogC4"))
    }

    /// Mean Y (8-bit code values as stored) of the first frame, via signalstats.
    private func meanLuma(_ ffmpeg: String, _ video: String, _ dir: String) throws -> Double {
        let stats = dir + "/yavg-\(UUID().uuidString).txt"
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", video,
                                     "-vf", "signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=\(stats)",
                                     "-frames:v", "1", "-f", "null", "-"])
        let text = try String(contentsOfFile: stats, encoding: .utf8)
        let line = try XCTUnwrap(text.split(whereSeparator: \.isNewline).first { $0.hasPrefix("lavfi.signalstats.YAVG=") })
        return try XCTUnwrap(Double(line.dropFirst("lavfi.signalstats.YAVG=".count)))
    }

    private func greyClip(_ ffmpeg: String, _ path: String, y: Int) throws {
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi",
                                     "-i", "color=c=0x696969:s=320x180:d=1:r=25", "-vf", "format=yuv420p,lutyuv=y=\(y):u=128:v=128",
                                     "-c:v", "libx264", "-color_range", "tv", path])
    }

    // Review focus 7 — real ffmpeg (H.264 source → VideoToolbox decode): an S-Log3 grey clip with its Sony XML renders a
    // proxy whose grey is ~0.42; a brighter level proves the LUT is really applied.
    func testSLog3GreyProxyIs042() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        let clipDir = dir.sub("card", "XDROOT", "Clip")
        // S-Log3 grey = 10-bit code 420 → 8-bit legal Y ≈ 105; neutral chroma.
        try greyClip(ffmpeg, clipDir + "/C0001.MP4", y: 105)
        try greyClip(ffmpeg, clipDir + "/C0002.MP4", y: 150)
        try #"<NonRealTimeMeta><Item name="CaptureGammaEquation" value="s-log3-cine"/><Item name="CaptureColorPrimaries" value="S-Gamut3Cine"/></NonRealTimeMeta>"#
            .write(toFile: clipDir + "/C0001M01.XML", atomically: true, encoding: .utf8)
        var s = ProxySettings()
        s.enabled = true
        s.height = 180
        s.lutMode = .auto
        s.burnFilename = false
        s.burnTimecode = false
        let r = ProxyRenderer(settings: s, tools: TestMedia.tools)
        func clip(_ name: String) -> Clip {
            Clip(name: name, path: "XDROOT/Clip/\(name).MP4", format: .video, segments: ["XDROOT/Clip/\(name).MP4"], bytes: 1, isCameraProxy: false)
        }
        let info = try r.probe(clip("C0001"), cardPath: card)
        let lut = LutResolver.resolve(clip("C0001"), info: info, clipFullPath: clipDir + "/C0001.MP4", settings: s, catalog: TestMedia.luts)
        XCTAssertEqual(lut.id, "sony-slog3-sgamut3cine")
        try r.render(clip("C0001"), cardPath: card, info: info, lut: lut, output: dir.path + "/p.mov", progress: { _ in }, isCancelled: { false })
        XCTAssertEqual((try meanLuma(ffmpeg, dir.path + "/p.mov", dir.path) - 16) / 219, 0.42, accuracy: 0.03)
        // Grey maps almost onto itself, so also a level the LUT clearly moves: Y=150 (≈ 0.61 without a LUT) comes out
        // well above 0.75 through it. Fails if the LUT is dropped.
        let bright = try r.probe(clip("C0002"), cardPath: card)
        try r.render(clip("C0002"), cardPath: card, info: bright, lut: lut, output: dir.path + "/p2.mov", progress: { _ in }, isCancelled: { false })
        XCTAssertGreaterThanOrEqual((try meanLuma(ffmpeg, dir.path + "/p2.mov", dir.path) - 16) / 219, 0.75)
        try r.render(clip("C0002"), cardPath: card, info: bright, lut: nil, output: dir.path + "/p3.mov", progress: { _ in }, isCancelled: { false })
        let plain = (try meanLuma(ffmpeg, dir.path + "/p3.mov", dir.path) - 16) / 219
        XCTAssertTrue((0.58...0.64).contains(plain), "\(plain)")
    }

    func testBuiltinLutIsCopiedOncePerIdAndCustomOnlyInCustomMode() throws {
        let dir = tempDir()
        let cube = dir.path + "/my look.cube"
        TestMedia.identityLUT(cube)
        var s = ProxySettings()
        s.lutPath = cube
        s.lutMode = .auto   // a saved custom path does not count outside custom mode
        let auto = ProxyRenderer(settings: s, tools: ExternalTools())
        XCTAssertNil(try auto.prepareLUT(TestMedia.customLut(cube)))
        let lut = try XCTUnwrap(TestMedia.luts.find("dji-dlog-dgamut"))
        let use = LutUse(source: "builtin", id: lut.id, name: lut.name, reason: "r", file: lut.path)
        let first = try XCTUnwrap(try auto.prepareLUT(use))
        XCTAssertEqual(first, auto.tempDir + "/lut-dji-dlog-dgamut.cube")
        XCTAssertEqual(try auto.prepareLUT(use), first)
        s.lutMode = .custom
        let custom = ProxyRenderer(settings: s, tools: ExternalTools())
        XCTAssertEqual(try custom.prepareLUT(TestMedia.customLut(cube)), custom.tempDir + "/look.cube")   // a plain path, no space
        XCTAssertNil(try custom.prepareLUT(nil))
        XCTAssertNil(try custom.prepareLUT(.off("x")))
    }
}
