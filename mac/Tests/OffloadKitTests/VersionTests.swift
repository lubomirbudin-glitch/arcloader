import XCTest
@testable import OffloadKit

final class VersionTests: XCTestCase {
    func testVersionIsPlan6() {
        XCTAssertEqual(OffloadVersion.string, "0.7.0")
        XCTAssertTrue(HTMLReport.render(Reports.sample()).contains("offload 0.7.0"))
    }
}
