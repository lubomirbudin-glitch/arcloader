import XCTest
@testable import OffloadKit

final class ChainViewTests: XCTestCase {
    func usb(_ role: String, _ product: String, _ link: UsbLinkSpeed, controllerId: String = "HC") -> RoleLink {
        RoleLink(role: role, link: DriveLink(root: "/Volumes/x", bus: .usb, product: product,
                                             usb: UsbLink(controllerId: controllerId, controllerName: "USB řadič XHC1", connectorId: "R#5",
                                                          connectorPort: 5, port: 5, link: link, capable: link, connectorUsb3: true, uasp: false)))
    }

    func sata(_ role: String) -> RoleLink { RoleLink(role: role, link: DriveLink(root: "/", bus: .sata, product: "ST1000", usb: nil)) }

    func testBuildUtilizationAndConnection() {
        let c = ChainView.build([usb("Karta", "reader", .superSpeed), sata("Cíl 1")], mbps: [225, 98])
        XCTAssertEqual(c[0].utilization!, 0.5, accuracy: 0.01)
        XCTAssertEqual(c[0].connection, "USB 3 (5 Gb/s), port 5")
        XCTAssertNil(c[1].utilization)
        XCTAssertEqual(c[1].connection, "SATA")
    }

    func testDescribeBrakeAndFullLink() {
        let chain = ChainView.build([usb("Karta", "reader", .high), sata("Cíl 1")], mbps: [38, 38])
        XCTAssertEqual(ChainView.describe(Brake(element: 1, mbps: 38, utilization: nil), chain), "Cíl 1 ST1000")
        XCTAssertEqual(ChainView.describe(Brake(element: 0, mbps: 38, utilization: 0.95), chain), "Karta reader — linka je plná (USB 2.0 (480 Mb/s))")
        XCTAssertEqual(ChainView.describe(Brake(element: 0, mbps: 20, utilization: 0.5), chain), "Karta reader")
        XCTAssertEqual(ChainView.describe(Brake(element: 7, mbps: 20, utilization: nil), chain), "")
    }

    func testLinesIncludeControllerSum() {
        let chain = ChainView.build([usb("Karta", "reader", .superSpeed), usb("Cíl 1", "SSD", .superSpeed)], mbps: [300, 290])
        let lines = ChainView.lines(chain, brake: Brake(element: 1, mbps: 290, utilization: 0.64))
        XCTAssertTrue(lines.contains { $0.hasPrefix("Karta") && $0.contains("300 MB/s") && $0.contains("67 % linky") })
        XCTAssertTrue(lines.contains("Řadič USB řadič XHC1: 590 MB/s"))
        XCTAssertTrue(lines.contains("Úzké hrdlo: Cíl 1 SSD"))
    }

    func testSameNamedControllersNotSummed() {
        let chain = ChainView.build([usb("Karta", "reader", .superSpeed, controllerId: "A"), usb("Cíl 1", "SSD", .superSpeed, controllerId: "B")],
                                    mbps: [300, 290])
        XCTAssertFalse(ChainView.lines(chain, brake: nil).contains { $0.hasPrefix("Řadič") })
    }
}
