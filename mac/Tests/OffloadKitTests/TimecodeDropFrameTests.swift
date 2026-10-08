import XCTest
@testable import OffloadKit

/// Windows `TimecodeEncoderTests.Add` / `Length_NominalFrames` (plan 7, review focus 1): drop-frame counts as the
/// camera does, a clip list's duration is nominal frames.
final class TimecodeDropFrameTests: XCTestCase {
    func testAddDropFrameAsTheCameraCounts() {
        let cases: [(String, Int, Double, String)] = [
            ("01:00:00:00", 0, 25, "01:00:00:00"),
            ("01:00:00:00", 50, 25, "01:00:02:00"),
            ("01:00:00:00", 99, 25, "01:00:03:24"),
            ("23:59:59:24", 1, 25, "00:00:00:00"),
            ("10:00:00;00", 30, 29.97, "10:00:01;00"),
            // No ;00/;01 (;00–;03 at 59.94) at a minute but every tenth.
            ("00:00:59;29", 1, 29.97, "00:01:00;02"),
            ("00:09:59;29", 1, 29.97, "00:10:00;00"),
            ("01:00:00;00", 17982, 29.97, "01:10:00;00"),
            ("23:59:59;29", 1, 29.97, "00:00:00;00"),
            ("00:00:59;59", 1, 59.94, "00:01:00;04"),
            ("00:00:59;29", 1, 30, "00:01:00;00"),   // ";" at exactly 30 fps: no frames dropped
        ]
        for (tc, frames, fps, expected) in cases {
            XCTAssertEqual(Timecode.add(tc, frames: frames, fps: fps), expected, "\(tc) + \(frames) @ \(fps)")
        }
    }

    func testLengthInNominalFrames() {
        XCTAssertEqual(Timecode.length(frames: 310, fps: 25), "00:00:12:10")
        XCTAssertEqual(Timecode.length(frames: 48, fps: 23.976), "00:00:02:00")
        XCTAssertEqual(Timecode.length(frames: 2, fps: 29.97), "00:00:00:02")
        XCTAssertEqual(Timecode.length(frames: -5, fps: 25), "00:00:00:00")
    }

    func testDropFrameOnlyWithSemicolonAtNtscRates() {
        XCTAssertTrue(Timecode.isDropFrame("00:00:00;00", fps: 29.97))
        XCTAssertTrue(Timecode.isDropFrame("00:00:00;00", fps: 59.94))
        XCTAssertFalse(Timecode.isDropFrame("00:00:00:00", fps: 29.97))
        XCTAssertFalse(Timecode.isDropFrame("00:00:00;00", fps: 30))
        XCTAssertFalse(Timecode.isDropFrame("00:00:00;00", fps: 25))
        XCTAssertEqual(Timecode.nominal(23.976), 24)
        XCTAssertEqual(Timecode.nominal(0.2), 1)
    }
}
