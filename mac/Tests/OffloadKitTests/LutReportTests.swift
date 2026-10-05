import XCTest
@testable import OffloadKit

final class LutReportTests: XCTestCase {
    private func report(lut: LutUse?, proxy: ProxySettings? = nil) -> OffloadReport {
        var r = Reports.sample()
        var e = ClipEntry(clip: Clip(name: "C0001", path: "C0001.MP4", format: .video, segments: ["C0001.MP4"], bytes: 1, isCameraProxy: false))
        e.lut = lut
        r.clips = [e]
        r.proxy = proxy
        return r
    }

    func testClipShowsLutAndJSONKey() throws {
        let r = report(lut: LutUse(source: "auto", id: "sony-slog3-sgamut3cine", name: "Sony S-Log3 / S-Gamut3.Cine → Rec.709",
                                   reason: "Sony XML: s-log3-cine / S-Gamut3Cine", file: "/x/lut.cube"))
        XCTAssertTrue(HTMLReport.render(r).contains(
            "<div class=\"note\">LUT: Sony S-Log3 / S-Gamut3.Cine → Rec.709 (Sony XML: s-log3-cine / S-Gamut3Cine)</div>"))
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: try r.jsonData()) as? [String: Any])
        let clip = try XCTUnwrap((obj["clips"] as? [[String: Any]])?.first)
        let lut = try XCTUnwrap(clip["lut"] as? [String: Any])
        XCTAssertEqual(lut.keys.sorted(), ["id", "name", "reason", "source"])
        XCTAssertEqual(lut["source"] as? String, "auto")
    }

    func testNoLutReasonShownUnlessSwitchedOff() throws {
        XCTAssertTrue(HTMLReport.render(report(lut: .off("kamera nerozpoznána"))).contains("<div class=\"note\">LUT: bez LUT (kamera nerozpoznána)</div>"))
        XCTAssertFalse(HTMLReport.render(report(lut: .off("vypnuto v nastavení"))).contains("LUT: bez LUT"))
        XCTAssertFalse(HTMLReport.render(report(lut: nil)).contains("class=\"note\""))
        let none = try XCTUnwrap(try JSONSerialization.jsonObject(with: try report(lut: .off("x")).jsonData()) as? [String: Any])
        let lut = try XCTUnwrap(((none["clips"] as? [[String: Any]])?.first)?["lut"] as? [String: Any])
        XCTAssertNil(lut["id"])   // omitted, like Windows (null is not written)
    }

    func testProxyLineSaysHowTheLutIsChosen() {
        var p = ProxySettings()
        p.enabled = true
        p.frameAspect = 1.66
        p.lutMode = .auto
        XCTAssertTrue(HTMLReport.render(report(lut: nil, proxy: p)).contains("Proxy: H.264, 1080p, výřez 1.66, LUT: automaticky podle kamery"))
        p.lutMode = .builtin
        p.lutBuiltin = "dji-dlog-dgamut"
        // The name comes from LutCatalog.shared — empty under xctest, so the id shows (the app shows the name).
        let builtin = HTMLReport.render(report(lut: nil, proxy: p))
        XCTAssertTrue(builtin.contains("LUT: dji-dlog-dgamut") || builtin.contains("LUT: DJI D-Log / D-Gamut → Rec.709"))
        p.lutMode = .custom
        p.lutPath = "/Users/x/show.cube"
        XCTAssertTrue(HTMLReport.render(report(lut: nil, proxy: p)).contains("LUT: show.cube"))
        p.lutMode = .off
        XCTAssertTrue(HTMLReport.render(report(lut: nil, proxy: p)).contains("LUT: bez LUT"))
    }

    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    // The LUT is chosen per clip (the Sony XML read from the destination copy) and recorded in the report.
    func testJobRecordsLutPerClip() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0001.MP4", seconds: 2, timecode: "01:00:00:00")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0002.MP4", seconds: 2, timecode: "01:00:10:00")
        try #"<NonRealTimeMeta><Item name="CaptureGammaEquation" value="s-log3-cine"/><Item name="CaptureColorPrimaries" value="S-Gamut3Cine"/></NonRealTimeMeta>"#
            .write(toFile: card + "/PRIVATE/M4ROOT/CLIP/C0001M01.XML", atomically: true, encoding: .utf8)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.date = "2026-10-05"
        config.proxy.enabled = true
        config.proxy.height = 360
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: options(), luts: TestMedia.luts)
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        let c1 = try XCTUnwrap(report.clips.first { $0.clip.name == "C0001" })
        XCTAssertEqual(c1.lut?.source, "auto")
        XCTAssertEqual(c1.lut?.id, "sony-slog3-sgamut3cine")
        XCTAssertEqual(c1.proxyStatus, .done)
        XCTAssertEqual(c1.thumbnails.count, 3)
        let c2 = try XCTUnwrap(report.clips.first { $0.clip.name == "C0002" })
        XCTAssertEqual(c2.lut?.source, "none")
        XCTAssertEqual(c2.lut?.reason, "kamera nerozpoznána")
        let json = try String(contentsOfFile: try XCTUnwrap(job.plan).cardPaths[0] + "/_REPORT/offload_report.json", encoding: .utf8)
        XCTAssertTrue(json.contains("\"lut\""))
    }
}
