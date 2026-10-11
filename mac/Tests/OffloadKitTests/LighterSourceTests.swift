import XCTest
@testable import OffloadKit

/// Task 54 (Windows 0.5.1): no H.264/HEVC proxy when the original is lighter (`proxy.skipLighterSources`, CLI
/// --proxy-always) — the rule, the note, settings, the report, the space check and a whole job.
final class LighterSourceTests: XCTestCase {
    private let clip = Clip(name: "1", path: "idle/1.mp4", format: .video, segments: ["idle/1.mp4"], bytes: 1, isCameraProxy: false)

    private func info(_ codec: String = "h264 High", w: Int? = 1920, h: Int? = 1080, mbps: Double? = 5.2) -> ClipInfo {
        var i = ClipInfo()
        i.codec = codec
        i.width = w
        i.height = h
        i.videoBitrate = mbps.map { Int64($0 * 1e6) }
        return i
    }

    private func settings(_ codec: ProxyCodec = .h264, height: Int = 1080, mbps: Int = 10) -> ProxySettings {
        var p = ProxySettings()
        p.enabled = true
        p.codec = codec
        p.height = height
        p.bitrateMbps = mbps
        return p
    }

    func testLighterH264OriginalGetsNoProxyWithTheWindowsNote() {
        XCTAssertEqual(LighterSource.note(clip, info: info(), settings: settings()), "no proxy — the original is lighter (5.2 Mb/s ≤ 10 Mb/s)")
        XCTAssertEqual(LighterSource.note(clip, info: info(mbps: 10), settings: settings()), "no proxy — the original is lighter (10 Mb/s ≤ 10 Mb/s)")
        XCTAssertEqual(LighterSource.note(clip, info: info("hevc Main 10", w: 1280, h: 720, mbps: 1.34), settings: settings(.hevc)),
                       "no proxy — the original is lighter (1.3 Mb/s ≤ 10 Mb/s)")
        // A LUT that is "none" (camera not recognised) is no look: still lighter.
        XCTAssertNotNil(LighterSource.note(clip, info: info(), settings: settings(), lut: .off("camera not recognised")))
        // Burn-in alone does not count.
        var burn = settings()
        burn.burnFilename = true
        burn.burnTimecode = true
        XCTAssertNotNil(LighterSource.note(clip, info: info(), settings: burn))
    }

    func testEveryOtherCaseGetsAProxy() {
        XCTAssertNil(LighterSource.note(clip, info: info(), settings: settings(.proresProxy)), "a ProRes proxy is always rendered")
        XCTAssertNil(LighterSource.note(clip, info: info(), settings: settings(.proresLT)))
        XCTAssertNil(LighterSource.note(clip, info: info(mbps: 10.5), settings: settings()), "heavier than the proxy")
        XCTAssertNil(LighterSource.note(clip, info: info(w: 3840, h: 2160, mbps: 4), settings: settings()), "4K long-GOP: always a proxy")
        XCTAssertNil(LighterSource.note(clip, info: info(w: 2048, h: 1080, mbps: 4), settings: settings()), "wider than 16:9 of the proxy")
        XCTAssertNil(LighterSource.note(clip, info: info(mbps: nil), settings: settings()), "rate not known")
        XCTAssertNil(LighterSource.note(clip, info: info(w: nil, h: nil), settings: settings()), "size not known")
        XCTAssertNil(LighterSource.note(clip, info: info("prores HQ", mbps: 5), settings: settings()), "ProRes original")
        XCTAssertNil(LighterSource.note(clip, info: info("mpeg2video 4:2:2", mbps: 5), settings: settings()))
        XCTAssertNil(LighterSource.note(clip, info: info("mjpeg", mbps: 5), settings: settings()))
        var braw = clip
        braw.format = .braw
        XCTAssertNil(LighterSource.note(braw, info: info(), settings: settings()), "RAW always")
        let lut = LutUse(source: "auto", id: "sony-slog3", name: "S-Log3", reason: "Sony")
        XCTAssertNil(LighterSource.note(clip, info: info(), settings: settings(), lut: lut), "with a LUT the proxy is the look")
        var crop = settings()
        crop.frameAspect = 1.85
        XCTAssertNil(LighterSource.note(clip, info: info(), settings: crop), "frame-lines crop")
        var scale = settings()
        scale.frameScale = 0.9
        XCTAssertNil(LighterSource.note(clip, info: info(), settings: scale))
        var always = settings()
        always.skipLighterSources = false
        XCTAssertNil(LighterSource.note(clip, info: info(), settings: always), "--proxy-always")
        XCTAssertNil(LighterSource.note(clip, info: info(w: 1280, h: 720, mbps: 2), settings: settings(height: 540)), "bigger than a 540p proxy")
    }

    func testOriginalLighterReadsBothLanguages() {
        var e = ClipEntry(clip: clip)
        e.proxyStatus = .skipped
        e.proxyNote = Msg.proxyLighter(5.2, 10)
        XCTAssertTrue(e.originalLighter)
        e.proxyNote = "bez proxy — originál je lehčí (5,2 Mb/s ≤ 10 Mb/s)"   // Windows before 0.9.0
        XCTAssertTrue(e.originalLighter)
        e.proxyNote = "proxies skipped: not enough space"
        XCTAssertFalse(e.originalLighter)
        e.proxyNote = nil
        XCTAssertFalse(e.originalLighter)
        e.proxyStatus = .done
        e.proxyNote = Msg.proxyLighter(5.2, 10)
        XCTAssertFalse(e.originalLighter)
    }

    func testSettingDefaultsOnAndIsStored() throws {
        XCTAssertTrue(ProxySettings().skipLighterSources)
        XCTAssertTrue(try JSONDecoder().decode(ProxySettings.self, from: Data("{}".utf8)).skipLighterSources, "absent = true")
        XCTAssertFalse(try JSONDecoder().decode(ProxySettings.self, from: Data(#"{"skipLighterSources":false}"#.utf8)).skipLighterSources)
        var p = ProxySettings()
        p.skipLighterSources = false
        let json = String(decoding: try JSONEncoder().encode(p), as: UTF8.self)
        XCTAssertTrue(json.contains("\"skipLighterSources\":false"))
        var s = AppSettings()
        s.proxy.skipLighterSources = false
        XCTAssertEqual(SettingsStore.decode(SettingsStore.encode(s)).proxy.skipLighterSources, false)
    }

    func testSpaceLeavesTheLighterOriginalOut() {
        let p = settings()
        let i = info()
        var withDuration = i
        withDuration.duration = 600
        let all = SpaceEstimator.proxyBytes([clip], p, renderable: { _ in true }, info: { _ in withDuration })
        XCTAssertGreaterThan(all, 0)
        XCTAssertEqual(SpaceEstimator.proxyBytes([clip], p, renderable: { _ in true }, info: { _ in withDuration },
                                                 noProxy: { LighterSource.note($0, info: withDuration, settings: p) != nil }), 0)
    }

    func testHTMLShowsNoProxyInGrey() {
        var e = ClipEntry(clip: clip)
        e.proxyStatus = .skipped
        e.proxyNote = Msg.proxyLighter(5.2, 10)
        XCTAssertEqual(HTMLReport.proxyLabel(e).0, "muted")
        XCTAssertEqual(HTMLReport.proxyLabel(e).1, "no proxy")
        var r = Reports.sample()
        r.clips = [e]
        let html = HTMLReport.render(r)
        XCTAssertTrue(html.contains("<div class=\"note\">no proxy — the original is lighter (5.2 Mb/s ≤ 10 Mb/s)</div>"))
        XCTAssertFalse(html.contains("<div class=\"path err\">no proxy"), "not an error")
        XCTAssertTrue(html.contains("<td class=\"muted\">no proxy</td>"))
    }

    // MARK: With ffmpeg

    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    private func run(_ dir: TempDir, configure: (inout JobConfig) -> Void) throws -> (OffloadJob, OffloadReport) {
        let card = dir.sub("A001")
        if !FileManager.default.fileExists(atPath: card + "/idle/1.mp4") {
            try TestMedia.makeClip(card + "/idle/1.mp4", seconds: 1, width: 640, height: 360)
        }
        var c = JobConfig(source: card, destRoots: [dir.sub("d1")])
        c.date = "2026-10-08"
        c.proxy.enabled = true
        c.proxy.height = 1080
        c.proxy.bitrateMbps = 20
        c.proxy.lutMode = .auto   // the test pattern has no camera: no LUT
        c.tools = TestMedia.tools
        c.dayReport = false
        configure(&c)
        let job = OffloadJob(config: c, options: options())
        let report = try XCTUnwrap(job.run())
        return (job, report)
    }

    func testJobSkipsTheLighterOriginalKeepsThumbnails() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let (job, report) = try run(dir) { _ in }
        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertTrue(s.safeToFormat)
        XCTAssertEqual(s.proxiesFailed, 0)
        XCTAssertNil(s.warning)
        // Verified during the proxy render although no proxy read the clip: the fill-in read verifies it on the primary.
        XCTAssertTrue(s.verifyInProxy)
        XCTAssertTrue(report.verified)
        XCTAssertTrue(report.files.allSatisfy { $0.destinations == ["verified"] })
        let c = try XCTUnwrap(report.clips.first)
        XCTAssertEqual(c.proxyStatus, .skipped)
        XCTAssertTrue(c.originalLighter, c.proxyNote ?? "nil")
        XCTAssertTrue(c.proxyNote?.hasSuffix("≤ 20 Mb/s)") == true, c.proxyNote ?? "nil")
        XCTAssertNil(c.proxyPath)
        XCTAssertEqual(c.thumbnails.count, 3, "thumbnails still made")
        let cardPath = try XCTUnwrap(job.plan).cardPaths[0]
        XCTAssertEqual(TestFS.files(in: cardPath + "/_PROXY"), [])
        XCTAssertTrue(JobProgress.summaryText(report).hasSuffix("proxy 0, 1 without proxy (lighter original)"), JobProgress.summaryText(report))
        let stored = try OffloadReport.read(cardPath + "/_REPORT/offload_report.json")
        XCTAssertEqual(stored.proxy?.skipLighterSources, true)
        XCTAssertTrue(stored.clips.first?.originalLighter == true)
    }

    func testProxyAlwaysProResAndLUTStillRender() throws {
        _ = try TestMedia.requireFFmpeg()
        let lut = tempDir().path + "/identity.cube"
        TestMedia.identityLUT(lut)
        let cases: [(String, (inout JobConfig) -> Void)] = [
            ("--proxy-always", { $0.proxy.skipLighterSources = false }),
            ("ProRes proxy", { $0.proxy.codec = .proresProxy }),
            ("custom LUT", { $0.proxy.lutMode = .custom; $0.proxy.lutPath = lut }),
            ("frame lines", { $0.proxy.frameAspect = 1.85 }),
        ]
        for (what, configure) in cases {
            let dir = tempDir()
            let (job, report) = try run(dir, configure: configure)
            XCTAssertEqual(job.status().phase, .done, what)
            let c = try XCTUnwrap(report.clips.first)
            XCTAssertEqual(c.proxyStatus, .done, what)
            XCTAssertEqual(c.proxyPath, "_PROXY/idle/1.mov", what)
        }
    }
}
