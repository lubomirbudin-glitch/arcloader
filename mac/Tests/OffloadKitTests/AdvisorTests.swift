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
        let a = Advisor.advise([usb("Cíl 1", "WD Elements", .high, .superSpeed, false, 9)], connectors: ports)
        let w = try XCTUnwrap(a.first { $0.kind == .warning })
        XCTAssertEqual(a.filter { $0.kind == .warning }.count, 1)
        XCTAssertTrue(w.text.contains("Cíl 1 WD Elements"))
        XCTAssertTrue(w.text.contains("umí USB 3 (5 Gb/s)"))
        XCTAssertTrue(w.text.contains("jen USB 2.0 (port 9)"))
        XCTAssertTrue(w.text.contains("port 5 USB 3"))
        XCTAssertTrue(w.text.contains("port 11 USB-C"))
    }

    func testWarningUsesSocketLabelAndNames() {
        let a = Advisor.advise([usb("Cíl 1", "WD", .high, .superSpeed, false, 9)], connectors: ports, portNames: ["ROOT#9": "zadní dole"])
        XCTAssertTrue(a.first { $0.kind == .warning }!.text.contains("jen USB 2.0 (zadní dole)"))
    }

    func testCapableDeviceSlowLinkOnUsb3Connector() {
        let w = Advisor.advise([usb("Karta", "SanDisk reader", .high, .superSpeed, true, 5)], connectors: ports).first { $0.kind == .warning }!
        XCTAssertTrue(w.text.contains("kabel"))
        XCTAssertFalse(w.text.contains("jen USB 2.0"))
    }

    func testUnknownSocketKindNoUsb2Claim() {
        let w = Advisor.advise([usb("Cíl 1", "SSD", .high, .superSpeed, nil, 5)], connectors: ports).first { $0.kind == .warning }!
        XCTAssertFalse(w.text.contains("jen USB 2.0"))
        XCTAssertTrue(w.text.contains("USB 3 port"))
        XCTAssertFalse(w.text.contains("port 5 USB 3"))   // never "move to the socket you are in"
    }

    func testSharedController() {
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 11)], connectors: ports)
        XCTAssertTrue(a.contains { $0.kind == .info && $0.text.contains("Karta, Cíl 1") && $0.text.contains("jeden řadič") })
        XCTAssertFalse(a.contains { $0.text.contains("různé řadiče") })
    }

    func testSameNamedControllersNotShared() {
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5, controller: "USB řadič", controllerId: "A"),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 11, controller: "USB řadič", controllerId: "B")],
                               connectors: ports)
        XCTAssertFalse(a.contains { $0.text.contains("jeden řadič") })
    }

    func testSplitAcrossControllers() throws {
        let more = ports + [port(3, 4, controllerId: "ASMEDIA", label: "ASMedia xHCI · port 3")]
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 11)], connectors: more)
        let tip = try XCTUnwrap(a.first { $0.text.contains("různé řadiče") })
        XCTAssertEqual(tip.kind, .tip)
        XCTAssertTrue(tip.text.contains("ASMedia xHCI · port 3 USB 3"))
        XCTAssertFalse(tip.text.contains("port 5"))
    }

    func testSharedHub() throws {
        let dock = UsbHubRef(instanceId: "loc:14100000", name: "USB3.0 Hub")
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 1, hubs: [dock]),
                                usb("Cíl 1", "SSD", .superSpeed, .superSpeed, true, 2, hubs: [UsbHubRef(instanceId: "loc:14110000", name: "Hub"), dock]),
                                usb("Cíl 2", "HDD", .superSpeed, .superSpeed, true, 11)], connectors: ports)
        let info = try XCTUnwrap(a.first { $0.text.contains("jeden hub") })
        XCTAssertEqual(a.filter { $0.text.contains("jeden hub") }.count, 1)
        XCTAssertEqual(info.kind, .info)
        XCTAssertEqual(info.text, "Karta, Cíl 1 jdou přes jeden hub (USB3.0 Hub) — dělí se o jeho linku.")
    }

    func testPortListsAndNames() {
        let a = Advisor.advise([], connectors: ports, portNames: ["ROOT#5": "přední levý"])
        XCTAssertTrue(a.contains { $0.kind == .tip && $0.text.hasPrefix("Volné rychlé porty: ") && $0.text.contains("přední levý") && $0.text.contains("port 11 USB-C") })
        XCTAssertTrue(a.contains { $0.kind == .tip && $0.text.hasSuffix("myš): port 9.") })
    }

    func testMeasuredEstimateAndSlowDisk() {
        let a = Advisor.advise([usb("Karta", "reader", .superSpeed, .superSpeed, true, 5),
                                usb("Cíl 1", "WD Elements", .superSpeed, .superSpeed, true, 11),
                                RoleLink(role: "Cíl 2", link: DriveLink(root: "/", bus: .sata, product: "ST1000LM035", usb: nil))],
                               connectors: ports, measuredMBps: ["Karta": 300, "Cíl 1": 26, "Cíl 2": 98])
        XCTAssertTrue(a.contains { $0.kind == .info && $0.text == "Odhad rychlosti offloadu s tímto zapojením: 26 MB/s (brzdí Cíl 1 WD Elements)." })
        XCTAssertTrue(a.contains { $0.text.contains("Cíl 1 WD Elements") && $0.text.contains("brzdí disk, ne port") })
    }

    func testOrderWarningsFirst() {
        let a = Advisor.advise([usb("Cíl 1", "WD", .high, .superSpeed, false, 9)], connectors: ports)
        XCTAssertEqual(a.first?.kind, .warning)
        XCTAssertEqual(a.map(\.kind), a.map(\.kind).sorted())
    }

    // From the topology fixtures: unknown kind is listed nowhere; a reader at USB 2.0 behind a merged USB 3 hub socket
    // gets "cable", never "move to <the same port>".
    func testTopologyFixturesThroughTheAdvisor() throws {
        let unknown = Connectors.build(UsbHub(instanceId: "ROOT", name: "Root", ports: [p(1, nil, companion: 5), p(2, nil), p(5, nil, companion: 1)]),
                                       controllerId: "HC", controllerName: "USB řadič XHC1")
        XCTAssertFalse(Advisor.advise([], connectors: unknown).contains { $0.text.contains("port 2") })

        let c = dualHalfHub()
        let link = DriveLink(root: "/Volumes/F", bus: .usb, product: "reader",
                             usb: UsbLink(controllerId: "HC", controllerName: "USB řadič XHC1", connectorId: "HS#2", connectorPort: 2, port: 2,
                                          link: .high, capable: .superSpeed, connectorUsb3: true, uasp: false))
        let w = try XCTUnwrap(Advisor.advise([RoleLink(role: "Karta", link: link)], connectors: c).first { $0.kind == .warning })
        XCTAssertTrue(w.text.contains("kabel"))
        XCTAssertFalse(w.text.contains("port 1 › 2"))
        XCTAssertEqual(link.describe(c), "reader — USB 2.0 (480 Mb/s), port 1 › 2")
    }
}
