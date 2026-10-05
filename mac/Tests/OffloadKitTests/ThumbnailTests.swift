import XCTest
@testable import OffloadKit

final class ThumbnailTests: XCTestCase {
    private func videoClip(_ rel: String) -> Clip {
        Clip(name: ((rel as NSString).lastPathComponent as NSString).deletingPathExtension, path: rel, format: .video,
             segments: [rel], bytes: 1, isCameraProxy: false)
    }

    /// Mean grey level of a JPEG, decoded by ffmpeg.
    private func meanGrey(_ ffmpeg: String, _ jpeg: String, _ dir: String) throws -> Double {
        let raw = dir + "/grey-\(UUID().uuidString).raw"
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", jpeg,
                                     "-f", "rawvideo", "-pix_fmt", "gray", raw])
        let bytes = try Data(contentsOf: URL(fileURLWithPath: raw))
        return bytes.reduce(0.0) { $0 + Double($1) } / Double(max(bytes.count, 1))
    }

    func testThreeJpegsWithTimecodeLabels() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/CLIP/C0001.mov", seconds: 4, timecode: "01:00:00:00")
        TestMedia.identityLUT(dir.path + "/look.cube")
        var s = ProxySettings()
        s.lutPath = dir.path + "/look.cube"
        let r = ProxyRenderer(settings: s, tools: TestMedia.tools)
        let clip = videoClip("CLIP/C0001.mov")
        let thumbs = try r.thumbnails(clip, cardPath: card, info: try r.probe(clip, cardPath: card))
        XCTAssertEqual(thumbs.count, 3)
        for t in thumbs { XCTAssertEqual(Array(t.jpeg.prefix(2)), [0xFF, 0xD8]) }
        XCTAssertEqual(thumbs.map(\.label), ["01:00:00:00", "01:00:02:00", "01:00:03:24"])
    }

    // Review focus 4 (Windows A1): the middle thumbnail is exactly the frame its caption names.
    func testMiddleFrameMatchesCaption24fps() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        let file = card + "/ramp.mov"
        // Every frame is one flat grey level, 8 steps above the previous one: a frame off is plain to see.
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                                     "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24",
                                     "-vf", "geq=lum='mod(N*8,256)':cb=128:cr=128", "-frames:v", "50",
                                     "-c:v", "libx264", "-pix_fmt", "yuv420p", file])
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("ramp.mov")
        let info = try r.probe(clip, cardPath: card)
        XCTAssertEqual(ProxyRenderer.framePositions(info)[1].frame, 25)
        let thumbs = try r.thumbnails(clip, cardPath: card, info: info)
        XCTAssertEqual(thumbs.count, 3)
        let thumb = dir.path + "/thumb.jpg"
        try thumbs[1].jpeg.write(to: URL(fileURLWithPath: thumb))
        let actual = try meanGrey(ffmpeg, thumb, dir.path)

        // Decode-exact reference: the frame picked by number, through the same thumbnail filter.
        func reference(_ n: Int) throws -> Double {
            let jpg = dir.path + "/ref-\(n).jpg"
            try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file,
                                         "-vf", "select='eq(n,\(n))',\(r.thumbnailFilter(applyLUT: false))",
                                         "-frames:v", "1", "-q:v", "5", jpg])
            return try meanGrey(ffmpeg, jpg, dir.path)
        }
        let exact = try reference(25), next = try reference(26), previous = try reference(24)
        XCTAssertLessThan(abs(actual - exact), 2,
                          "thumbnail grey \(actual), frame 25 \(exact), frame 26 \(next), frame 24 \(previous)")
    }

    // Review focus 4 (Windows A3): audio longer than the picture — still three, the last one is the last frame.
    func testAudioLongerThanVideoStillThree() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                                     "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=4", "-f", "lavfi", "-i", "sine=d=6",
                                     "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", card + "/long-audio.mov"])
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("long-audio.mov")
        let info = try r.probe(clip, cardPath: card)
        let thumbs = try r.thumbnails(clip, cardPath: card, info: info)
        XCTAssertEqual(thumbs.count, 3)
        XCTAssertEqual(thumbs[2].label, ProxyRenderer.label(info, ProxyRenderer.framePositions(info)[2]))
    }

    func testSingleFrameClip() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/one.mov", seconds: 0.04, audio: false)
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("one.mov")
        XCTAssertEqual(try r.thumbnails(clip, cardPath: card, info: try r.probe(clip, cardPath: card)).count, 1)
    }

    // Windows A4: cancelled → no thumbnails and no error.
    func testCancelledNoneAndNoError() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/a.mov", seconds: 2)
        let r = ProxyRenderer(settings: ProxySettings(), tools: TestMedia.tools)
        let clip = videoClip("a.mov")
        let info = try r.probe(clip, cardPath: card)
        XCTAssertEqual(try r.thumbnails(clip, cardPath: card, info: info, isCancelled: { true }), [])
    }

    func testNoFFmpegThrows() {
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        XCTAssertThrowsError(try r.thumbnails(videoClip("a.mov"), cardPath: "/nonexistent", info: ClipInfo()))
    }

    // Spec 7: braw-decode's fps is rounded (23.976) while positions use the exact rate — round, never truncate.
    func testBrawStartFrameIsRounded() {
        let exact = 24000.0 / 1001
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 25 / exact, fps: 23.976, frames: 50), 25)
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 49 / exact, fps: 23.976, frames: 50), 49)
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 100, fps: 25, frames: 50), 49)
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 1, fps: 0, frames: 50), 25)   // unknown fps → 25
        XCTAssertEqual(BRAWBackend.startFrame(seconds: 1, fps: 25, frames: 0), 0)
    }
}
