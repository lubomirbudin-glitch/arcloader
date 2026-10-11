import XCTest
@testable import OffloadKit

/// Task 70: the decoder's content check (spec 7, Windows `ContentWatch`) — every group of words, the proxy's own AAC
/// encoder not counted, a short clip, a retry forgets the first attempt, the JSON shape.
final class ContentCheckTests: XCTestCase {
    func info(frames: Int? = 100, tc: String? = "01:00:00:00", rate: String? = "25") -> ClipInfo {
        var i = ClipInfo()
        i.frames = frames
        i.timecode = tc
        i.frameRate = rate
        return i
    }

    func testOkWithoutLines() {
        let c = ContentCheck()
        c.progress("frame=100")
        let v = c.verdict(info())
        XCTAssertFalse(v.isWarn)
        XCTAssertEqual(v.state, "ok")
        XCTAssertEqual(v.framesDecoded, 100)
        XCTAssertEqual(v.framesExpected, 100)
        XCTAssertNil(v.kind)
    }

    func testEveryGroupOfWords() {
        let cases: [(String, String)] = [
            ("[h264 @ 0x7f8] corrupt decoded frame in stream 0", "decode"),
            ("[h264 @ 0x7f8] error while decoding MB 3 4, bytestream -5", "decode"),
            ("[hevc @ 0x7f8] Invalid NAL unit size (1234 > 99).", "decode"),
            ("[mov,mp4,m4a @ 0x7f8] Packet corrupt (stream = 0, dts = 10).", "decode"),
            ("[vist#0:0/h264 @ 0x7f8] Error submitting packet to decoder: Invalid data found when processing input", "decode"),
            ("[aac @ 0x7f8] Number of bands (47) exceeds limit (40).", "audio"),
            ("[pcm_s24le @ 0x7f8] Invalid PCM packet, data has size 5 but at least a size of 6 was expected", "audio"),
            ("[aist#0:1/aac @ 0x7f8] Error submitting packet to decoder: Invalid data found when processing input", "audio"),
            ("[mp4 @ 0x7f8] Application provided invalid, non monotonically increasing dts to muxer", "timestamps"),
            ("[mxf @ 0x7f8] Invalid timestamp", "timestamps"),
        ]
        for (line, kind) in cases {
            let c = ContentCheck()
            c.progress("frame=50")
            c.stderr(line)
            c.progress("frame=100")
            let v = c.verdict(info())
            XCTAssertEqual(v.kind, kind, line)
            XCTAssertEqual(v.state, "warn", line)
            XCTAssertEqual(v.frame, 50, "the frame when the decoder said it")
            XCTAssertEqual(v.timecode, "01:00:02:00", line)
            XCTAssertEqual(v.message, line.trimmingCharacters(in: .whitespaces))
        }
    }

    func testOwnAacEncoderAndOtherNotesNotCounted() {
        let c = ContentCheck()
        c.stderr("[aac @ 0x7f8] Too many bits 8832.000000 > 6144 per frame requested, clamping to max")
        c.stderr("[swscaler @ 0x7f8] deprecated pixel format used, make sure you did set range correctly")
        c.stderr("[h264_videotoolbox @ 0x7f8] Color range not set for yuv420p. Using MPEG range.")
        c.progress("frame=100")
        XCTAssertFalse(c.verdict(info()).isWarn)
    }

    func testFewerFramesIsEndMoreIsFrames() {
        let a = ContentCheck()
        a.progress("frame=60")
        let end = a.verdict(info())
        XCTAssertEqual(end.kind, "end")
        XCTAssertEqual(end.message, "decoded 60 of 100 frames")
        XCTAssertEqual(end.frame, 60)
        let b = ContentCheck()
        b.progress("frame=130")
        XCTAssertEqual(b.verdict(info()).kind, "frames")
        let c = ContentCheck()
        c.progress("frame=99")
        XCTAssertFalse(c.verdict(info()).isWarn, "one frame either way is not a defect")
        let d = ContentCheck()
        d.progress("frame=10")
        XCTAssertFalse(d.verdict(info(frames: nil)).isWarn, "a container without nb_frames is not counted")
    }

    func testFirstDefectWinsAndRetryForgets() {
        let c = ContentCheck()
        c.stderr("[h264 @ 1] concealing 10 DC, 10 AC, 10 MV errors in P frame")
        c.stderr("[mxf @ 1] Invalid timestamp")
        XCTAssertEqual(c.verdict(info(frames: nil)).kind, "decode")
        c.reset()
        c.progress("frame=100")
        XCTAssertFalse(c.verdict(info()).isWarn, "the software retry's verdict only")
    }

    func testTimecodeWithoutTcIsFrame() {
        let c = ContentCheck()
        c.progress("frame=12")
        c.stderr("[h264 @ 1] decode_slice_header error")
        XCTAssertEqual(c.verdict(info(tc: nil)).timecode, "#12")
    }

    func testJsonShape() throws {
        var e = ClipEntry(clip: Clip(name: "A", path: "A.mov", format: .video, segments: ["A.mov"], bytes: 1, isCameraProxy: false))
        var c = ClipContent(state: ClipContent.warn)
        c.kind = "decode"; c.frame = 5; c.timecode = "01:00:00:05"; c.message = "m"; c.framesDecoded = 100; c.framesExpected = 100
        c.jpeg = Data([1, 2, 3])
        e.content = c
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys]
        let json = String(decoding: try enc.encode(e), as: UTF8.self)
        XCTAssertTrue(json.contains(#""content":{"frame":5,"framesDecoded":100,"framesExpected":100,"kind":"decode","message":"m","state":"warn","timecode":"01:00:00:05"}"#), json)
        XCTAssertFalse(json.contains("jpeg"), "the picture is in the HTML only")
        let back = try JSONDecoder().decode(ClipEntry.self, from: Data(json.utf8))
        XCTAssertEqual(back.content, c)
        let ok = String(decoding: try enc.encode(ClipContent()), as: UTF8.self)
        XCTAssertEqual(ok, #"{"state":"ok"}"#)
        XCTAssertEqual(ClipContent.kindText("end"), "clip ends early")
    }

    func testReportShowsDefects() {
        var r = OffloadReport(cardName: "A001", date: "2026-10-08", source: "/s", sourceFilesystem: "exfat", started: Date(),
                              copyFinished: Date(), finished: Date(), verified: true, totalFiles: 0, totalBytes: 0,
                              destinations: [], files: [], skipped: [])
        var e = ClipEntry(clip: Clip(name: "A001C001", path: "A001C001.mov", format: .video, segments: ["A001C001.mov"], bytes: 1, isCameraProxy: false))
        var c = ClipContent(state: ClipContent.warn)
        c.kind = "audio"; c.timecode = "01:00:00:05"; c.message = "[aac @ 1] Number of bands (47) exceeds limit (40)."
        e.content = c
        r.clips = [e]
        XCTAssertEqual(r.contentWarnings.count, 1)
        let html = HTMLReport.render(r)
        XCTAssertTrue(html.contains("Content defects (1)"))
        XCTAssertTrue(html.contains("corrupted audio"))
        XCTAssertTrue(html.contains(VIPMsg.contentWarnings(1)))
        XCTAssertTrue(r.summary.contains("Content warnings: 1 (A001C001 01:00:00:05)"))
        // verifyIncomplete: yellow UNFINISHED, never green VERIFIED.
        r.verifyIncomplete = true
        let unfinished = HTMLReport.render(r)
        XCTAssertTrue(unfinished.contains("badge warn\">UNFINISHED"))
        XCTAssertFalse(unfinished.contains(">VERIFIED<"))
    }

    // With ffmpeg: a damaged clip (a run of garbage in its media data) is a content warning, the copy is still verified.
    func testDamagedClipIsWarnedInAJob() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A005")
        let path = card + "/A005C001.mp4"
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=4",
                                     "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart", path])
        // Cut the end off: the clip ends early (fewer frames than the file says).
        let size = try XCTUnwrap(try FileManager.default.attributesOfItem(atPath: path)[.size] as? NSNumber).intValue
        let h = try XCTUnwrap(FileHandle(forUpdatingAtPath: path))
        try h.truncate(atOffset: UInt64(size * 2 / 3))
        try h.close()
        var cfg = JobConfig(source: card, destRoots: [dir.sub("d1")])
        cfg.proxy.enabled = true
        cfg.proxy.height = 360
        cfg.tools = TestMedia.tools
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        let job = OffloadJob(config: cfg, options: o)
        let r = try XCTUnwrap(job.run())
        let s = job.status()
        XCTAssertEqual(s.phase, .done, s.error ?? "")
        let c = try XCTUnwrap(r.clips.first?.content)
        XCTAssertTrue(c.isWarn, "\(c)")
        XCTAssertEqual(s.contentWarnings, 1)
        XCTAssertEqual(s.warning, VIPMsg.contentWarnings(1))
        XCTAssertTrue(s.safeToFormat, "a defect of the camera file never blocks Safe to format (the copy matches the card)")
        XCTAssertFalse(JobProgress.chime(s), "but no success chime — it needs a look")
        let chips = JobProgress.chips(s, contentConfirmed: false, contentWarnings: s.contentWarnings)
        XCTAssertEqual(chips.format.text, "Safe to format after checking defects")
        XCTAssertTrue(r.files.allSatisfy { $0.destinations == ["verified"] })
    }
}
