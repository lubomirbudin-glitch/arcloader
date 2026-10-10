import XCTest
@testable import OffloadKit

final class FileTimesTests: XCTestCase {
    let t = Date(timeIntervalSince1970: 1_791_000_000)

    func testFatShiftByWholeHoursIsSame() {
        XCTAssertTrue(FileTimes.storesLocalTime("msdos"))
        XCTAssertTrue(FileTimes.storesLocalTime("FAT32"))
        XCTAssertTrue(FileTimes.same(t, t.addingTimeInterval(3600), localTime: true))
        XCTAssertTrue(FileTimes.same(t, t.addingTimeInterval(-14 * 3600), localTime: true))
        XCTAssertFalse(FileTimes.same(t, t.addingTimeInterval(15 * 3600), localTime: true))
        XCTAssertFalse(FileTimes.same(t, t.addingTimeInterval(3601), localTime: true))
    }

    func testExfatShiftIsChange() {
        XCTAssertFalse(FileTimes.storesLocalTime("exfat"))
        XCTAssertFalse(FileTimes.same(t, t.addingTimeInterval(3600), localTime: false))
    }

    func testCloseAllowsTwoSecondsAndOnFatWholeHours() {
        XCTAssertTrue(FileTimes.close(t, t.addingTimeInterval(1.9), localTime: false))
        XCTAssertFalse(FileTimes.close(t, t.addingTimeInterval(2.5), localTime: false))
        XCTAssertTrue(FileTimes.close(t, t.addingTimeInterval(3600 + 1), localTime: true))
    }

    func testDotNetTicks() {
        XCTAssertEqual(FileTimes.ticks(Date(timeIntervalSince1970: 0)), 621_355_968_000_000_000)
        XCTAssertEqual(FileTimes.ticks(Date(timeIntervalSince1970: 1.5)), 621_355_968_015_000_000)
        XCTAssertEqual(FileTimes.date(ticks: 621_355_968_015_000_000).timeIntervalSince1970, 1.5, accuracy: 1e-6)
    }
}
