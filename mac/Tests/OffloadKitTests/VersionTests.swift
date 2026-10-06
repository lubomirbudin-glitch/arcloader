import XCTest
@testable import OffloadKit

final class VersionTests: XCTestCase {
    func testVersionIsTheParityRelease() {
        XCTAssertEqual(OffloadVersion.string, "0.2.0")
        XCTAssertTrue(HTMLReport.render(Reports.sample()).contains("offload 0.2.0"))
    }
}
