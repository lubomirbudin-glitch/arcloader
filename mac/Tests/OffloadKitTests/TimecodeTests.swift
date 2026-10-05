import XCTest
@testable import OffloadKit

final class TimecodeTests: XCTestCase {
    func testFps() {
        XCTAssertEqual(Timecode.fps("24000/1001")!, 23.976, accuracy: 0.001)
        XCTAssertEqual(Timecode.fps("25"), 25)
        XCTAssertEqual(Timecode.fps("25/1"), 25)
        XCTAssertNil(Timecode.fps("0/0"))
        XCTAssertNil(Timecode.fps("25/0"))
        XCTAssertNil(Timecode.fps(nil))
        XCTAssertNil(Timecode.fps(""))
        XCTAssertNil(Timecode.fps("x"))
    }

    func testIsValid() {
        XCTAssertTrue(Timecode.isValid("01:00:00:00"))
        XCTAssertTrue(Timecode.isValid("10:00:00;12"))
        XCTAssertFalse(Timecode.isValid("1:00:00:00"))
        XCTAssertFalse(Timecode.isValid(nil))
    }

    func testAddCountsFramesAndWrapsAtMidnight() {
        XCTAssertEqual(Timecode.add("01:00:00:00", frames: 99, fps: 25), "01:00:03:24")
        XCTAssertEqual(Timecode.add("01:00:00:00", frames: 50, fps: 25), "01:00:02:00")
        XCTAssertEqual(Timecode.add("23:59:59:24", frames: 1, fps: 25), "00:00:00:00")
        XCTAssertEqual(Timecode.add("10:00:00;00", frames: 30, fps: 29.97), "10:00:01;00")
    }
}
