import XCTest
@testable import OffloadKit

final class AdvisorTests: XCTestCase {
    let hc = "Intel(R) USB 3.20 eXtensible Host Controller"
    let hcId = "HC-INTEL"

    func usb(_ role: String, _ product: String, _ link: UsbLinkSpeed, _ cap: UsbLinkSpeed, _ usb3: Bool?, _ port: Int,
             controller: String? = nil, controllerId: String? = nil, hubs: [UsbHubRef] = []) -> RoleLink {
        RoleLink(role: role, link: DriveLink(root: "/Volumes/\(role)", bus: .usb, product: product,
                                             usb: UsbLink(controllerId: controllerId ?? hcId, controllerName: controller ?? hc,
                                                          connectorId: "ROOT#\(port)", connectorPort: port, port: port, link: link,
                                                          capable: cap, connectorUsb3: usb3, uasp: false, hubs: hubs)))
    }

    func port(_ usb2: Int, _ usb3: Int, typeC: Bool = false, visible: Bool = true, device: UsbDevice? = nil,
              controllerId: String? = nil, label: String = "") -> UsbConnector {
        UsbConnector(id: "ROOT#\(usb2)", hubInstanceId: "ROOT", controllerId: controllerId ?? hcId, controllerName: hc,
                     number: usb2, usb2Port: usb2, usb3Port: usb3, supportsUsb3: usb3 > 0, typeC: typeC, userVisible: visible,
                     device: device, label: label.isEmpty ? "port \(usb2)" : label)
    }

    var ports: [UsbConnector] {
        [port(9, 0), port(5, 20), port(11, 26, typeC: true),
         port(1, 17, device: UsbDevice(instanceId: "x", vid: 1, pid: 1, name: "Keyboard", link: .full, capable: .full, hub: nil)),
         port(14, 0, visible: false)]
    }

    func testCapableDeviceOnUsb2OnlyPort() throws {
        let a = Advisor.advise([usb("Destination 1", "WD Elements", .high, .superSpeed, false, 9)], connectors: ports)
        let w = try XCTUnwrap(a.first { $0.kind == .warning })
        XCTAssertEqual(a.filter { $0.kind == .warning }.count, 1)
        XCTAssertTrue(w.text.contains("Destination 1 WD Elements"))
        XCTAssertTrue(w.text.contains("supports USB 3 (5 Gb/s)"))
        XCTAssertTrue(w.text.contains("USB 2.0 only port (port 9)"))
        XCTAssertTrue(w.text.contains("port 5 USB 3"))
        XCTAssertTrue(w.text.contains("port 11 USB-C"))
    }

    func testWarningUsesSocketLabelAndNames() {
        let a = Advisor.advise([usb("Destination 1", "WD", .high, .superSpeed, false, 9)], connectors: ports, portNames: ["ROOT#9": "zadní dole"])
        XCTAssertTrue(a.first { $0.kind == .warning }!.text.contains("USB 2.0 only port (zadní dole)"))
    }

    func testCapableDeviceSlowLinkOnUsb3Connector() {
        let w = Advisor.advise([usb("Card", "SanDisk reader", .high, .superSpeed, true, 5)], connectors: ports).first { $0.kind == .warning }!
        XCTAssertEqual(w.text, "Card SanDisk reader: supports USB 3 (5 Gb/s), but the link runs at USB 2.0 (480 Mb/s) — "
                       + "probably a USB 2.0 cable or a bad contact; try another cable or port.")
        XCTAssertFalse(w.text.contains("USB 2.0 only"))
    }

    func testUnknownSocketKindNoUsb2Claim() {
        let w = Advisor.advise([usb("Destination 1", "SSD", .high, .superSpeed, nil, 5)], connectors: ports).first { $0.kind == .warning }!
        XCTAssertFalse(w.text.contains("USB 2.0 only"))
        XCTAssertTrue(w.text.contains("USB 3 port"))
        XCTAssertFalse(w.text.contains("port 5 USB 3"))   // never "move to the socket you are in"
    }

    func testSharedController() {
        let a = Advisor.advise([usb("Card", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Destination 1", "SSD", .superSpeed, .superSpeed, true, 11)], connectors: ports)
        XCTAssertTrue(a.contains { $0.kind == .info && $0.text.contains("Card, Destination 1") && $0.text.contains("share one controller") })
        XCTAssertFalse(a.contains { $0.text.contains("different controllers") })
    }

    func testSameNamedControllersNotShared() {
        let a = Advisor.advise([usb("Card", "reader", .superSpeed, .superSpeed, true, 5, controller: "USB controller", controllerId: "A"),
                                usb("Destination 1", "SSD", .superSpeed, .superSpeed, true, 11, controller: "USB controller", controllerId: "B")],
                               connectors: ports)
        XCTAssertFalse(a.contains { $0.text.contains("share one controller") })
    }

    func testSplitAcrossControllers() throws {
        let more = ports + [port(3, 4, controllerId: "ASMEDIA", label: "ASMedia xHCI · port 3")]
        let a = Advisor.advise([usb("Card", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Destination 1", "SSD", .superSpeed, .superSpeed, true, 11)], connectors: more)
        let tip = try XCTUnwrap(a.first { $0.text.contains("different controllers") })
        XCTAssertEqual(tip.kind, .tip)
        XCTAssertTrue(tip.text.contains("ASMedia xHCI · port 3 USB 3"))
        XCTAssertFalse(tip.text.contains("port 5"))
    }

    func testSharedHub() throws {
        let dock = UsbHubRef(instanceId: "loc:14100000", name: "USB3.0 Hub")
        let a = Advisor.advise([usb("Card", "reader", .superSpeed, .superSpeed, true, 1, hubs: [dock]),
                                usb("Destination 1", "SSD", .superSpeed, .superSpeed, true, 2, hubs: [UsbHubRef(instanceId: "loc:14110000", name: "Hub"), dock]),
                                usb("Destination 2", "HDD", .superSpeed, .superSpeed, true, 11)], connectors: ports)
        let info = try XCTUnwrap(a.first { $0.text.contains("share one hub") })
        XCTAssertEqual(a.filter { $0.text.contains("share one hub") }.count, 1)
        XCTAssertEqual(info.kind, .info)
        XCTAssertEqual(info.text, "Card, Destination 1 share one hub (USB3.0 Hub) — they split its link.")
    }

    func testPortListsAndNames() {
        let a = Advisor.advise([], connectors: ports, portNames: ["ROOT#5": "přední levý"])
        XCTAssertTrue(a.contains { $0.kind == .tip && $0.text.hasPrefix("Free fast ports: ") && $0.text.contains("přední levý") && $0.text.contains("port 11 USB-C") })
        XCTAssertTrue(a.contains { $0.kind == .tip && $0.text.hasSuffix("mouse): port 9.") })
    }

    func testMeasuredEstimateAndSlowDisk() {
        let a = Advisor.advise([usb("Card", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Destination 1", "WD Elements", .superSpeed, .superSpeed, true, 11),
                                RoleLink(role: "Destination 2", link: DriveLink(root: "/", bus: .sata, product: "ST1000LM035", usb: nil))],
                               connectors: ports, measuredMBps: ["Card": 300, "Destination 1": 26, "Destination 2": 98])
        XCTAssertTrue(a.contains { $0.kind == .info && $0.text == "Estimated offload speed with this setup: 26 MB/s (limited by Destination 1 WD Elements)." })
        XCTAssertTrue(a.contains { $0.text.contains("Destination 1 WD Elements") && $0.text.contains("the drive is the limit, not the port") })
    }

    func testOrderWarningsFirst() {
        let a = Advisor.advise([usb("Destination 1", "WD", .high, .superSpeed, false, 9)], connectors: ports)
        XCTAssertEqual(a.first?.kind, .warning)
        XCTAssertEqual(a.map(\.kind), a.map(\.kind).sorted())
    }

    // From the topology fixtures: unknown kind is listed nowhere; a reader at USB 2.0 behind a merged USB 3 hub socket
    // gets "cable", never "move to <the same port>".
    func testTopologyFixturesThroughTheAdvisor() throws {
        let unknown = Connectors.build(UsbHub(instanceId: "ROOT", name: "Root", ports: [p(1, nil, companion: 5), p(2, nil), p(5, nil, companion: 1)]),
                                       controllerId: "HC", controllerName: "USB controller XHC1")
        XCTAssertFalse(Advisor.advise([], connectors: unknown).contains { $0.text.contains("port 2") })

        let c = dualHalfHub()
        let link = DriveLink(root: "/Volumes/F", bus: .usb, product: "reader",
                             usb: UsbLink(controllerId: "HC", controllerName: "USB controller XHC1", connectorId: "HS#2", connectorPort: 2, port: 2,
                                          link: .high, capable: .superSpeed, connectorUsb3: true, uasp: false))
        let w = try XCTUnwrap(Advisor.advise([RoleLink(role: "Card", link: link)], connectors: c).first { $0.kind == .warning })
        XCTAssertTrue(w.text.contains("cable"))
        XCTAssertFalse(w.text.contains("port 1 › 2"))
        XCTAssertEqual(link.describe(c), "reader — USB 2.0 (480 Mb/s), port 1 › 2")
    }
}
