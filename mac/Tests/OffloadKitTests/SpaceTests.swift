import XCTest
@testable import OffloadKit

final class SpaceTests: XCTestCase {
    func clip(_ ext: String, bytes: Int64, format: ClipFormat = .video, proxy: Bool = false) -> Clip {
        Clip(name: "A001C001", path: "CLIP/A001C001.\(ext)", format: format, segments: [], bytes: bytes, isCameraProxy: proxy)
    }

    func testFormulas() {
        XCTAssertEqual(SpaceEstimator.cardBytes(1000, files: 2), 1000 + 2 * 65536 + 64 << 20)
        XCTAssertEqual(SpaceEstimator.reportBytes(clips: 3), (32 << 20) + 3 * (1 << 20))
        var p = ProxySettings()
        p.enabled = true; p.codec = .h264; p.bitrateMbps = 10; p.height = 1080
        XCTAssertEqual(SpaceEstimator.proxyMbps(p, audioStreams: [2], height: 1080), 10 * 1.1 + 0.128, accuracy: 1e-9)
        p.codec = .proresProxy
        XCTAssertEqual(SpaceEstimator.proxyMbps(p, audioStreams: [2], height: 2160), 45 * 4 + 0.768 * 2, accuracy: 1e-9)
        XCTAssertEqual(SpaceEstimator.sourceMbps(clip("mxf", bytes: 1)), 100)
        XCTAssertEqual(SpaceEstimator.sourceMbps(clip("braw", bytes: 1, format: .braw)), 150)
        XCTAssertEqual(SpaceEstimator.sourceMbps(clip("mov", bytes: 1)), 50)
    }

    func testProxyBytesSkipsCameraProxiesAndUndecodable() {
        var p = ProxySettings()
        p.enabled = true; p.codec = .h264; p.bitrateMbps = 10; p.skipCameraProxies = true
        let one = SpaceEstimator.proxyBytes([clip("mov", bytes: 50_000_000 / 8 * 60)], p, renderable: { _ in true })
        XCTAssertEqual(Double(one), 60 * (11 + 0.128) * 1e6 / 8, accuracy: 2)
        XCTAssertEqual(SpaceEstimator.proxyBytes([clip("mov", bytes: 1 << 30, proxy: true)], p, renderable: { _ in true }), 0)
        XCTAssertEqual(SpaceEstimator.proxyBytes([clip("r3d", bytes: 1 << 30, format: .r3d)], p, renderable: { _ in false }), 0)
        // After the analysis: the real duration.
        var info = ClipInfo()
        info.duration = 10
        let analysed = SpaceEstimator.proxyBytes([clip("mov", bytes: 1 << 30)], p, renderable: { _ in true }, info: { _ in info })
        XCTAssertEqual(Double(analysed), 10 * 11 * 1e6 / 8, accuracy: 2, "no audio channels probed: no audio")
        p.enabled = false
        XCTAssertEqual(SpaceEstimator.proxyBytes([clip("mov", bytes: 1 << 30)], p, renderable: { _ in true }), 0)
    }

    func testPlannerSumsPerVolumeBlocksAndWarns() {
        let a = VolumeDemand(volume: "V1", label: "Zaloha (RAID)", need: SpaceNeed(card: 60, proxy: 0, report: 0))
        let b = VolumeDemand(volume: "V1", label: "Zaloha2 (RAID)", need: SpaceNeed(card: 50, proxy: 0, report: 0))
        XCTAssertEqual(SpacePlanner.check([a, b], free: ["V1": 100]).first?.severity, .blocking)
        XCTAssertEqual(SpacePlanner.check([a, b], free: ["V1": 100]).first?.label, "Zaloha (RAID), Zaloha2 (RAID)")
        XCTAssertEqual(SpacePlanner.check([a], free: ["V1": 62]).first?.severity, .warning, "2 left of 60 is under 5 %")
        XCTAssertTrue(SpacePlanner.check([a], free: ["V1": 64]).isEmpty)
        XCTAssertTrue(SpacePlanner.check([a], free: [:]).isEmpty, "unknown free space never blocks")
    }

    func testBlockingText() {
        let need = SpaceNeed(card: 380_000_000_000, proxy: 30_000_000_000, report: 2_000_000_000)
        let p = SpacePlanner.check([VolumeDemand(volume: "V", label: "Zaloha (RAID)", need: need)], free: ["V": 398_000_000_000])
        XCTAssertEqual(p.first?.text, "Not enough space on Zaloha (RAID): needs 412 GB (cards 380 GB, proxy ~30 GB, report 2 GB), free 398 GB.")
    }

    func testEngineBlocksBeforeWritingAndAddsVolumes() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        _ = TestFS.writeRandom(card + "/A001C001.mov", size: 1000, seed: 1)
        var o = OffloadOptions()
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let e = OffloadEngine(options: o)
        let card1 = SpaceEstimator.cardBytes(1000, files: 1)
        // Two destinations on one volume: each fits alone, together they do not.
        e.testFreeSpace = { _ in card1 + card1 / 2 }
        XCTAssertThrowsError(try e.prepare(source: card, destRoots: [dir.sub("D"), dir.sub("E")], cardName: "A001", date: "2026-10-08")) {
            XCTAssertEqual(($0 as? OffloadError)?.code, .noSpace)
        }
        XCTAssertEqual(TestFS.files(in: dir.sub("D")), [], "nothing written")
        // One destination with less than 5 % left: a warning, not a refusal.
        e.testFreeSpace = { _ in card1 + card1 / 100 }
        _ = try e.prepare(source: card, destRoots: [dir.sub("D")], cardName: "A001", date: "2026-10-08")
        XCTAssertEqual(e.spaceWarnings.count, 1)
        XCTAssertTrue(e.spaceWarnings[0].hasSuffix("under 5% headroom, the proxy estimate is approximate."), e.spaceWarnings[0])
        XCTAssertEqual(e.spaceLines.first?.need.card, card1)
    }

    // Spec 2.5: recounted before the render from the real lengths — no room: no proxies, the copy stays verified.
    func testProxiesSkippedWhenTheyNoLongerFit() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/C0001.mov", seconds: 1)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.proxy.enabled = true
        config.proxy.height = 360
        config.tools = TestMedia.tools
        var o = OffloadOptions()
        o.allowSourceVolume = true
        let job = OffloadJob(config: config, options: o)
        XCTAssertTrue(job.runCopy())
        job.engine.testFreeSpace = { _ in 1000 }
        job.runPost()
        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertTrue(s.safeToFormat, "the copy stays verified")
        XCTAssertTrue(s.warning?.hasPrefix("Proxies were not rendered: not enough space on ") == true, s.warning ?? "nil")
        XCTAssertEqual(job.report?.clips.first?.proxyStatus, .skipped)
        XCTAssertFalse(FileManager.default.fileExists(atPath: try XCTUnwrap(job.plan).cardPaths[0] + "/_PROXY"))
    }

    func testProxyNeedCountsOnTheFirstDestinationAndTheFolder() throws {
        let dir = tempDir()
        let extra = SpaceExtra(proxy: 100, report: 10, proxyOnAllDestinations: false, proxyFolder: dir.sub("proxies"))
        let places = SpacePlanner.places([dir.sub("D"), dir.sub("E")], extra: extra, card: 1000, label: { ($0 as NSString).lastPathComponent })
        XCTAssertEqual(places.map(\.need), [SpaceNeed(card: 1000, proxy: 100, report: 10), SpaceNeed(card: 1000, proxy: 0, report: 10),
                                            SpaceNeed(card: 0, proxy: 100, report: 0)])
    }
}
