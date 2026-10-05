import XCTest
@testable import OffloadKit

final class LutResolverTests: XCTestCase {
    private let cat = TestMedia.luts

    private func videoClip(_ path: String = "XDROOT/Clip/C0001.MP4") -> Clip {
        Clip(name: ((path as NSString).lastPathComponent as NSString).deletingPathExtension, path: path, format: .video,
             segments: [path], bytes: 1, isCameraProxy: false)
    }

    private func rawClip(_ format: ClipFormat, _ path: String) -> Clip {
        Clip(name: "R", path: path, format: format, segments: [path], bytes: 1, isCameraProxy: false)
    }

    private func info(camera: String? = nil, log: String? = nil) -> ClipInfo {
        var i = ClipInfo()
        i.camera = camera
        i.logEncoding = log
        return i
    }

    /// A clip file and its Sony sidecar "<clip>M01.XML" (NonRealTimeMeta, with the namespace a camera writes).
    private func sonyXML(_ dir: TempDir, _ clipRel: String, gamma: String, primaries: String) -> String {
        let clip = dir.path + "/" + clipRel
        try! FileManager.default.createDirectory(atPath: (clip as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try! "x".write(toFile: clip, atomically: true, encoding: .utf8)
        try! """
        <?xml version="1.0" encoding="UTF-8"?>
        <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
          <AcquisitionRecord><Group name="CameraUnitMetadataSet">
            <Item name="CaptureGammaEquation" value="\(gamma)"/>
            <Item name="CaptureColorPrimaries" value="\(primaries)"/>
          </Group></AcquisitionRecord>
        </NonRealTimeMeta>
        """.write(toFile: (clip as NSString).deletingPathExtension + "M01.XML", atomically: true, encoding: .utf8)
        return clip
    }

    private func auto(fallback: String? = nil) -> ProxySettings {
        var s = ProxySettings()
        s.enabled = true
        s.lutMode = .auto
        s.lutFallback = fallback
        return s
    }

    func testSonySLog3FromXML() {
        for (gamma, primaries, id) in [("s-log3-cine", "S-Gamut3Cine", "sony-slog3-sgamut3cine"),
                                       ("s-log3", "S-Gamut3", "sony-slog3-sgamut3"),
                                       ("s-log3-cine", "", "sony-slog3-sgamut3cine")] {
            let dir = tempDir()
            let clip = sonyXML(dir, "XDROOT/Clip/C0001.MP4", gamma: gamma, primaries: primaries)
            let use = LutResolver.resolve(videoClip(), info: ClipInfo(), clipFullPath: clip, settings: auto(), catalog: cat)
            XCTAssertEqual(use.source, "auto", gamma)
            XCTAssertEqual(use.id, id, gamma)
            XCTAssertTrue(use.reason.contains(gamma), use.reason)
            XCTAssertFalse(use.reason.hasSuffix("/"), use.reason)
            XCTAssertTrue(FileManager.default.fileExists(atPath: use.file ?? ""), id)
        }
    }

    // Review focus 7: a clip the camera recorded in Rec.709 never gets a log LUT, not even the fallback.
    func testSonyRec709NoLut() {
        let dir = tempDir()
        let clip = sonyXML(dir, "PRIVATE/M4ROOT/CLIP/C0002.MP4", gamma: "rec709", primaries: "rec709")
        let use = LutResolver.resolve(videoClip("PRIVATE/M4ROOT/CLIP/C0002.MP4"), info: ClipInfo(), clipFullPath: clip,
                                      settings: auto(fallback: "sony-slog3-sgamut3cine"), catalog: cat)
        XCTAssertEqual(use.source, "none")
        XCTAssertNil(use.file)
        XCTAssertTrue(use.reason.contains("rec709"), use.reason)
    }

    // S-Log2 / S-Log is log, just without a built-in LUT — only the rest is "not log".
    func testSonyOtherGammaNoLutWithHonestReason() {
        for (gamma, reason) in [("s-log2", "Sony XML: s-log2 (bez vestavěného LUTu)"), ("S-Log", "Sony XML: S-Log (bez vestavěného LUTu)"),
                                ("hlg2", "Sony XML: hlg2 (není log)"), ("s-cinetone", "Sony XML: s-cinetone (není log)")] {
            let dir = tempDir()
            let clip = sonyXML(dir, "XDROOT/Clip/C0003.MP4", gamma: gamma, primaries: "rec709")
            let use = LutResolver.resolve(videoClip(), info: ClipInfo(), clipFullPath: clip,
                                          settings: auto(fallback: "sony-slog3-sgamut3cine"), catalog: cat)
            XCTAssertEqual(use.source, "none", gamma)
            XCTAssertEqual(use.reason, reason)
        }
    }

    func testSonyUpperCaseAndHyphenatedPrimaries() {
        let dir = tempDir()
        let clip = sonyXML(dir, "XDROOT/Clip/C0004.MP4", gamma: "S-LOG3-CINE", primaries: "s-gamut3-cine")
        XCTAssertEqual(LutResolver.resolve(videoClip(), info: ClipInfo(), clipFullPath: clip, settings: auto(), catalog: cat).id,
                       "sony-slog3-sgamut3cine")
    }

    func testSonyXMLMissingOrBroken() throws {
        let dir = tempDir()
        XCTAssertNil(SonyXML.read(dir.path + "/C0001.MP4"))
        try "<NonRealTimeMeta><Item".write(toFile: dir.path + "/C0001M01.XML", atomically: true, encoding: .utf8)
        XCTAssertNil(SonyXML.read(dir.path + "/C0001.MP4"))
    }

    func testArriFromCameraModel() {
        for (camera, id) in [("ARRI ALEXA 35", "arri-logc4-awg4"), ("ALEXA Mini LF", "arri-logc3-awg3"), ("AMIRA", "arri-logc3-awg3"),
                             ("ALEXA Mini LF 35mm", "arri-logc3-awg3"), ("ALEXA35", "arri-logc4-awg4")] {
            let use = LutResolver.resolve(videoClip("A001C001.mov"), info: info(camera: camera), clipFullPath: "/nonexistent/A001C001.mov",
                                          settings: auto(), catalog: cat)
            XCTAssertEqual(use.id, id, camera)
            let raw = LutResolver.resolve(rawClip(.arriraw, "A/A.ari"), info: info(camera: camera), clipFullPath: "/nonexistent/A",
                                          settings: auto(), catalog: cat)
            XCTAssertEqual(raw.id, id, "ARRIRAW " + camera)
        }
    }

    func testArrirawUsesLogEncoding() {
        let clip = rawClip(.arriraw, "A1/A1.ari")
        XCTAssertEqual(LutResolver.resolve(clip, info: info(log: "LogC4"), clipFullPath: "/x/A1", settings: auto(), catalog: cat).id, "arri-logc4-awg4")
        XCTAssertEqual(LutResolver.resolve(clip, info: info(camera: "ALEXA Mini"), clipFullPath: "/x/A1", settings: auto(), catalog: cat).id, "arri-logc3-awg3")
    }

    func testArriColorSpace() {
        let cases: [(String?, String?, String?, String)] = [
            ("ARRI_LogC4-to-Gamma24_Rec709.cube", nil, nil, "AWG4/D65/LogC4"),
            ("show_LogC3_709.cube", "LogC4", nil, "AWG3/D65/LogC3"),
            (nil, "LogC4", nil, "AWG4/D65/LogC4"),
            (nil, nil, "ALEXA Mini", "AWG3/D65/LogC3"),
            (nil, nil, "ALEXA 35", "AWG4/D65/LogC4"),
        ]
        for (lut, log, camera, expected) in cases {
            XCTAssertEqual(ARRIRAWBackend.colorSpace(lut: lut, info: info(camera: camera, log: log)), expected)
        }
    }

    func testBrawAndR3D() {
        XCTAssertEqual(LutResolver.resolve(rawClip(.braw, "B.braw"), info: nil, clipFullPath: "/x/B.braw", settings: auto(), catalog: cat).id,
                       "bmd-film-gen5")
        XCTAssertEqual(LutResolver.resolve(rawClip(.r3d, "R.RDC/R_001.R3D"), info: nil, clipFullPath: "/x/R", settings: auto(), catalog: cat).id,
                       "red-log3g10-rwg")
    }

    func testUnknownCameraUsesTheFallback() {
        let clip = videoClip("DCIM/100CANON/MVI_0001.MP4")
        let none = LutResolver.resolve(clip, info: info(camera: "Canon EOS R5"), clipFullPath: "/x/x.mp4", settings: auto(), catalog: cat)
        XCTAssertEqual(none.source, "none")
        XCTAssertEqual(none.reason, "kamera nerozpoznána")
        let fb = LutResolver.resolve(clip, info: ClipInfo(), clipFullPath: "/x/x.mp4", settings: auto(fallback: "canon-clog3-cinemagamut"), catalog: cat)
        XCTAssertEqual(fb.source, "fallback")
        XCTAssertEqual(fb.id, "canon-clog3-cinemagamut")
        XCTAssertEqual(fb.reason, "kamera nerozpoznána — záložní LUT")
    }

    func testModesBuiltinCustomOff() {
        let clip = videoClip()
        var b = ProxySettings()
        b.lutMode = .builtin
        b.lutBuiltin = "dji-dlog-dgamut"
        let builtin = LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: b, catalog: cat)
        XCTAssertEqual([builtin.source, builtin.id, builtin.reason], ["builtin", "dji-dlog-dgamut", "vybraný v nastavení"])
        var c = ProxySettings()
        c.lutMode = .custom
        c.lutPath = "/Users/x/luts/show.cube"
        let custom = LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: c, catalog: cat)
        XCTAssertEqual(custom, LutUse(source: "custom", id: nil, name: "show.cube", reason: "vlastní LUT", file: "/Users/x/luts/show.cube"))
        var n = ProxySettings()
        n.lutMode = .off
        let off = LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: n, catalog: cat)
        XCTAssertEqual([off.source, off.name, off.reason], ["none", "bez LUT", "vypnuto v nastavení"])
        // No mode stored (older settings): a lutPath means custom, otherwise automatic.
        var old = ProxySettings()
        old.lutPath = "/l.cube"
        XCTAssertEqual(LutResolver.resolve(clip, info: nil, clipFullPath: "/x", settings: old, catalog: cat).source, "custom")
        XCTAssertEqual(LutResolver.effectiveMode(ProxySettings()), .auto)
    }

    func testEmptyLutPathIsNotCustom() {
        var s = ProxySettings()
        s.lutPath = ""
        XCTAssertEqual(LutResolver.effectiveMode(s), .auto)
        s.lutMode = .custom
        XCTAssertEqual(LutResolver.resolve(videoClip(), info: nil, clipFullPath: "/x", settings: s, catalog: cat).reason,
                       "vlastní LUT není vybraný")
    }

    func testBuiltinIdMissingFromTheCatalog() {
        var s = ProxySettings()
        s.lutMode = .builtin
        s.lutBuiltin = "gone"
        XCTAssertEqual(LutResolver.resolve(videoClip(), info: nil, clipFullPath: "/x", settings: s, catalog: cat).reason,
                       "vestavěný LUT nenalezen")
    }

    func testAutoWithoutCatalogNoLut() {
        let dir = tempDir()
        let clip = sonyXML(dir, "XDROOT/Clip/C0001.MP4", gamma: "s-log3-cine", primaries: "S-Gamut3Cine")
        let use = LutResolver.resolve(videoClip(), info: nil, clipFullPath: clip, settings: auto(), catalog: LutCatalog.load(nil))
        XCTAssertEqual(use.source, "none")
        XCTAssertEqual(use.reason, "vestavěné LUTy chybí")
    }

    func testLutUseJSONHasNoFile() throws {
        var s = ProxySettings()
        s.lutMode = .builtin
        s.lutBuiltin = "dji-dlog-dgamut"
        let use = LutResolver.resolve(videoClip(), info: nil, clipFullPath: "/x", settings: s, catalog: cat)
        XCTAssertNotNil(use.file)
        let json = String(decoding: try JSONEncoder().encode(use), as: UTF8.self)
        XCTAssertFalse(json.contains("file"))
        XCTAssertFalse(json.contains(".cube"))
        XCTAssertTrue(json.contains(#""source":"builtin""#))
        let back = try JSONDecoder().decode(LutUse.self, from: Data(json.utf8))
        XCTAssertEqual(back.id, "dji-dlog-dgamut")
        XCTAssertNil(back.file)
    }

    func testFFProbeReadsColorRangeAndMakePlusModel() throws {
        let range = try FFProbe.parse(Data(#"{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"color_range":"pc"}],"format":{}}"#.utf8))
        XCTAssertEqual(range.colorRange, "pc")
        let arri = try FFProbe.parse(Data(#"{"streams":[{"codec_type":"video","codec_name":"prores"}],"format":{"tags":{"com.apple.quicktime.make":"ARRI","com.apple.quicktime.model":"ALEXA 35"}}}"#.utf8))
        XCTAssertEqual(arri.camera, "ARRI ALEXA 35")
        XCTAssertEqual(FFProbe.camera(make: "ARRI", model: "ARRI ALEXA 35"), "ARRI ALEXA 35")
        XCTAssertEqual(FFProbe.camera(make: " Sony ", model: nil), "Sony")
        XCTAssertEqual(FFProbe.camera(make: "", model: "ILME-FX6"), "ILME-FX6")
        XCTAssertNil(FFProbe.camera(make: nil, model: "  "))
    }
}
