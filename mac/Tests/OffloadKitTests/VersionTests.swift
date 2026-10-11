import XCTest
@testable import OffloadKit

final class VersionTests: XCTestCase {
    func testVersionIsPlan6() {
        XCTAssertEqual(OffloadVersion.string, "0.7.0")
        // The JSON keeps tool "offload" (FORMATY); the footer names the product (ArcLoader, Windows 0.10.0).
        XCTAssertEqual(Reports.sample().tool, "offload")
        XCTAssertTrue(HTMLReport.render(Reports.sample()).contains("<footer>ArcLoader 0.7.0 · "))
    }
}
