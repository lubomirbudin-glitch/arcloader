import XCTest
@testable import OffloadKit

final class FramePositionTests: XCTestCase {
    private func info(rate: String? = "25/1", duration: Double? = 4, frames: Int? = 100, tc: String? = nil) -> ClipInfo {
        var i = ClipInfo()
        i.frameRate = rate
        i.duration = duration
        i.frames = frames
        i.timecode = tc
        return i
    }

    func testFirstMiddleLast() {
        XCTAssertEqual(ProxyRenderer.framePositions(info()), [
            FramePosition(seconds: 0, frame: 0, fromEnd: false),
            FramePosition(seconds: 2, frame: 50, fromEnd: false),
            FramePosition(seconds: 3.96, frame: 99, fromEnd: true),
        ])
        let withTC = info(tc: "01:00:00:00")
        XCTAssertEqual(ProxyRenderer.framePositions(withTC).map { ProxyRenderer.label(withTC, $0) },
                       ["01:00:00:00", "01:00:02:00", "01:00:03:24"])
        let noTC = info()
        XCTAssertEqual(ProxyRenderer.label(noTC, ProxyRenderer.framePositions(noTC)[1]), "#50")
    }

    // Review focus 4: the middle frame is sought half a frame early, the last one a second before it.
    func testSeekSecondsMiddleHalfFrameEarlyLastOneSecondBefore() {
        let i = info(rate: "24000/1001", duration: 2.085417, frames: 50)
        XCTAssertEqual(ProxyRenderer.framePositions(i).map { ProxyRenderer.seekSeconds(i, $0) },
                       ["0.000000", "1.021854", "1.043708"])
        let short = info(rate: "25/1", duration: 0.12, frames: 3)
        XCTAssertEqual(ProxyRenderer.framePositions(short).map { ProxyRenderer.seekSeconds(short, $0) },
                       ["0.000000", "0.020000", "0.000000"])
    }

    func testUnknownLengthOrSingleFrameOnlyFirst() {
        XCTAssertEqual(ProxyRenderer.framePositions(ClipInfo()).count, 1)
        XCTAssertEqual(ProxyRenderer.framePositions(info(duration: 0.04, frames: 1)).count, 1)
        XCTAssertEqual(ProxyRenderer.framePositions(info(duration: 0.08, frames: 2)).map(\.frame), [0, 1])
        // Without nb_frames (MKV, some MXF): from duration × fps.
        XCTAssertEqual(ProxyRenderer.framePositions(info(duration: 4, frames: nil)).map(\.frame), [0, 50, 99])
    }

    // Spec 3.3 / Windows A2: "%" in a clip name is literal text, not a drawtext expansion.
    func testBurnInNameIsLiteral() {
        var s = ProxySettings()
        s.burnFilename = true
        s.burnTimecode = false
        let r = ProxyRenderer(settings: s, tools: ExternalTools())
        let clip = Clip(name: "Take 100% A", path: "a.mov", format: .video, segments: ["a.mov"], bytes: 1, isCameraProxy: false)
        let f = r.burnIn(clip: clip, info: ClipInfo(), height: 1080)
        XCTAssertTrue(f.contains { $0.hasPrefix("drawtext=") && $0.contains(":expansion=none:") }, "\(f)")
    }
}
