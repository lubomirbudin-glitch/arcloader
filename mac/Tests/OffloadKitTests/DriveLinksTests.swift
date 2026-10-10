import XCTest
@testable import OffloadKit

final class DriveLinksTests: XCTestCase {
    func testNetworkAndBusLabels() {
        XCTAssertEqual(DriveLinks.network("/Volumes/media", "//nas/media").describe(), "//nas/media — network drive")
        XCTAssertEqual(DriveLinks.network("/Volumes/media", nil).describe(), "/Volumes/media — network drive")
        XCTAssertEqual(DriveLink(root: "/Volumes/Q", bus: .unknown, product: "", usb: nil).describe(), "unknown connection")
        XCTAssertEqual(DriveLink(root: "/Volumes/G", bus: .sd, product: "", usb: nil).describe(), "SD reader")
        XCTAssertEqual(DriveLink(root: "/Volumes/V", bus: .virtual, product: "Disk", usb: nil).describe(), "Disk — virtual disk")
        XCTAssertEqual(DriveLink(root: "/Volumes/T", bus: .thunderbolt, product: "OWC Atlas", usb: nil).describe(), "OWC Atlas — Thunderbolt")
        XCTAssertEqual(StorageBuses.label(.usb), "USB (port unknown)")
        // No raw English enum names reach the user.
        for bus in [StorageBus.usb, .sd, .mmc, .virtual, .network, .unknown, .other] {
            XCTAssertNotEqual(bus.rawValue.lowercased(), StorageBuses.label(bus).lowercased())
        }
    }

    // Windows final review 1: the drive line and the port list name one device's socket the same way.
    func testDriveLineAndPortListShareTheLabel() throws {
        let hub = UsbHub(instanceId: "ROOT", name: "Root", ports: [
            UsbPort(number: 6, supportsUsb3: false, userVisible: true, typeC: false, companionPort: 19, device: nil),
            UsbPort(number: 19, supportsUsb3: true, userVisible: true, typeC: false, companionPort: 6,
                    device: UsbDevice(instanceId: "loc:14600000", vid: 1, pid: 2, name: "Elements 25A2", link: .superSpeed, capable: .superSpeed, hub: nil)),
        ])
        let connectors = Connectors.build(hub, controllerId: "HC", controllerName: "USB controller XHC1")
        let socket = try XCTUnwrap(connectors.first)
        XCTAssertEqual(connectors.count, 1)
        let link = DriveLink(root: "/Volumes/E", bus: .usb, product: "WD Elements 25A2",
                             usb: UsbLink(controllerId: "HC", controllerName: "USB controller XHC1", connectorId: "ROOT#19", connectorPort: 6,
                                          port: 19, link: .superSpeed, capable: .superSpeed, connectorUsb3: true, uasp: true))
        XCTAssertEqual(link.describe(connectors), "WD Elements 25A2 — USB 3 (5 Gb/s), port 6")
        let row = PortLabels.listRow(socket, drives: [RoleLink(role: "E", link: link)])
        XCTAssertTrue(row.hasPrefix("port 6 USB 3"))
        XCTAssertTrue(row.hasSuffix("← E"))
        XCTAssertEqual(PortLabels.of(link.usb!, connectors), "port 6")
        // A user's name replaces the label everywhere, whichever half the drive reports.
        let names = ["ROOT#6": "přední levý"]
        XCTAssertEqual(link.describe(connectors, names), "WD Elements 25A2 — USB 3 (5 Gb/s), přední levý")
        XCTAssertTrue(PortLabels.listRow(socket, drives: [RoleLink(role: "Destination 1", link: link)], names: names).hasPrefix("přední levý USB 3"))
        XCTAssertTrue(PortLabels.listRow(socket, drives: [RoleLink(role: "Destination 1", link: link)], names: names).hasSuffix("← E (Destination 1)"))
        // Without the topology: the socket number the link carries, not the raw USB 3 port.
        XCTAssertEqual(link.describe(), "WD Elements 25A2 — USB 3 (5 Gb/s), port 6")
    }
}
