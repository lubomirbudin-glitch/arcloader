import XCTest
@testable import OffloadKit

final class LayoutTests: XCTestCase {
    func testSanitize() {
        XCTAssertEqual(Layout.sanitize("Den 1: hlavní"), "Den 1_ hlavní")
        XCTAssertEqual(Layout.sanitize("a/b\\c"), "a_b_c")
        XCTAssertEqual(Layout.sanitize("  "), "CARD")
        XCTAssertEqual(Layout.sanitize(".."), "CARD")
    }

    func testUniqueCardFolderNeverReusesAnExistingOne() {
        let dir = tempDir()
        let d1 = dir.sub("d1"), d2 = dir.sub("d2")
        XCTAssertEqual(Layout.uniqueCardFolder(roots: [d1, d2], date: "2026-10-05", name: "A001"), "A001")
        dir.sub("d2", "2026-10-05", "A001")
        XCTAssertEqual(Layout.uniqueCardFolder(roots: [d1, d2], date: "2026-10-05", name: "A001"), "A001_2")
    }

    func testFormatting() {
        XCTAssertEqual(formatBytes(999), "999 B")
        XCTAssertEqual(formatBytes(1000), "1.00 KB")
        XCTAssertEqual(formatBytes(2_500_000_000), "2.50 GB")
        XCTAssertEqual(formatRate(1_000_000_000, 10), "100 MB/s")
        XCTAssertEqual(formatRate(1, 0), "-")
        XCTAssertEqual(hex64(0xabc), "0000000000000abc")
    }

    func testRandomFilesAreDeterministic() {
        let dir = tempDir()
        let a = TestFS.writeRandom(dir.path + "/x/a.bin", size: 1001, seed: 7)
        let b = TestFS.writeRandom(dir.path + "/x/b.bin", size: 1001, seed: 7)
        XCTAssertEqual(a, b)
        XCTAssertEqual(TestFS.files(in: dir.path), ["x/a.bin", "x/b.bin"])
    }
}
