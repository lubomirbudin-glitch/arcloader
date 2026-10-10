import XCTest
@testable import OffloadKit

final class StageTwoTests: XCTestCase {
    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    private func proxySettings(_ codec: ProxyCodec = .h264) -> ProxySettings {
        var p = ProxySettings()
        p.enabled = true
        p.height = 360
        p.codec = codec
        return p
    }

    private func sonyCard(_ dir: TempDir) throws -> String {
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0001.MP4", seconds: 2, timecode: "01:00:00:00")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/CLIP/C0002.MP4", seconds: 2, timecode: "01:00:10:00")
        try TestMedia.makeClip(card + "/PRIVATE/M4ROOT/SUB/C0001S03.MP4", seconds: 2, width: 640, height: 360)
        try "<x/>".write(toFile: card + "/PRIVATE/M4ROOT/MEDIAPRO.XML", atomically: true, encoding: .utf8)
        return card
    }

    func testProxiesAndThreeThumbnailsOnEveryDestination() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        var config = JobConfig(source: try sonyCard(dir), destRoots: [dir.sub("d1"), dir.sub("d2")])
        config.date = "2026-10-05"
        config.proxy = proxySettings()
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: options())
        let report = try XCTUnwrap(job.run())
        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertTrue(s.safeToEject)
        XCTAssertEqual(s.proxiesDone, 2)
        XCTAssertEqual(s.proxiesFailed, 0)
        XCTAssertEqual(report.clips.count, 3)
        for c in report.clips { XCTAssertEqual(c.thumbnails.count, 3, c.clip.name) }
        XCTAssertEqual(report.clips.first { $0.clip.isCameraProxy }?.proxyStatus, .skipped)
        for cardPath in try XCTUnwrap(job.plan).cardPaths {
            XCTAssertTrue(FileManager.default.fileExists(atPath: cardPath + "/_PROXY/C0001.mov"))
            XCTAssertTrue(FileManager.default.fileExists(atPath: cardPath + "/_PROXY/C0002.mov"))
            let html = try String(contentsOfFile: cardPath + "/_REPORT/A001_report.html", encoding: .utf8)
            XCTAssertTrue(html.contains("<figcaption>01:00:10:00</figcaption>"))
        }
        // The hash list only covers the card's files, not proxies.
        let list = try String(contentsOfFile: try XCTUnwrap(job.plan).cardPaths[0] + "/_REPORT/A001.xxh64", encoding: .utf8)
        XCTAssertFalse(list.contains("_PROXY"))
    }

    // Windows A7: a failed copy to another destination keeps the rendered proxy.
    func testProxyCopyFailsRenderedProxyKept() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/C0001.mov", seconds: 1)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        config.proxy = proxySettings()
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: options())
        XCTAssertTrue(job.runCopy())
        let paths = try XCTUnwrap(job.plan).cardPaths
        try FileManager.default.createDirectory(atPath: paths[1] + "/_PROXY/C0001.mov", withIntermediateDirectories: true)  // blocks the copy on d2
        var log: [String] = []
        job.runPost { log.append($0) }

        let s = job.status()
        XCTAssertEqual(s.phase, .done)
        XCTAssertEqual(s.proxiesDone, 1)
        XCTAssertEqual(s.proxiesFailed, 0)
        let clip = try XCTUnwrap(job.report?.clips.first)
        XCTAssertEqual(clip.proxyStatus, .done)
        XCTAssertEqual(clip.proxyPath, "_PROXY/C0001.mov")
        XCTAssertTrue(clip.proxyNote?.hasPrefix("copy to \(paths[1]) failed: ") == true, clip.proxyNote ?? "nil")
        XCTAssertTrue(log.contains { $0.contains("C0001") && $0.contains(paths[1]) })
        XCTAssertTrue(FileManager.default.fileExists(atPath: paths[0] + "/_PROXY/C0001.mov"))
        XCTAssertEqual(TestFS.files(in: paths[1] + "/_PROXY").filter { $0.hasSuffix(".part") }, [])
    }

    func testNoVerifyProxiesFromWrittenCopy() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try TestMedia.makeClip(card + "/clip.mov", seconds: 1)
        var o = options()
        o.verify = false
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.verify = false
        config.proxy = proxySettings()
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: o)
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertFalse(job.status().safeToEject)
        XCTAssertEqual(report.clips.first?.proxyStatus, .done)
    }

    // Windows C1: HEVC proxies are tagged hvc1 (QuickTime / Resolve need it).
    func testHevcProxyIsTaggedHvc1() throws {
        let (_, ffprobe) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/a.mov", seconds: 1)
        let r = ProxyRenderer(settings: proxySettings(.hevc), tools: TestMedia.tools)
        let clip = Clip(name: "a", path: "a.mov", format: .video, segments: ["a.mov"], bytes: 1, isCameraProxy: false)
        let out = card + "/_PROXY/a.mov"
        try r.render(clip, cardPath: card, info: try r.probe(clip, cardPath: card), output: out, progress: { _ in }, isCancelled: { false })
        let tag = try ToolProcess.run(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_entries",
                                                "stream=codec_tag_string", "-of", "csv=p=0", out])
        // ffprobe 8+ lists the video stream twice when the MOV has a timecode track: once in the stream group
        // that links it to the tmcd track, once in "streams". Every listing must say hvc1.
        let tags = tag.split(whereSeparator: \.isNewline).map(String.init)
        XCTAssertFalse(tags.isEmpty)
        XCTAssertEqual(Set(tags), ["hvc1"], tag)
    }
}
