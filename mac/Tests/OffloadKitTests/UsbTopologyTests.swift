import XCTest
@testable import OffloadKit

final class UsbTopologyTests: XCTestCase {
    func testPairUsb2AndUsb3Ports() throws {
        let c = build(root(onPort17: dev("disk", .superSpeed, .superSpeed)))
        XCTAssertEqual(c.count, 3)
        let a = try XCTUnwrap(c.first { $0.usb2Port == 1 })
        XCTAssertEqual(a.usb3Port, 17)
        XCTAssertEqual(a.supportsUsb3, true)
        XCTAssertEqual(a.device?.name, "disk")
        XCTAssertEqual(a.id, "ROOT#1")
        XCTAssertTrue(a.has("ROOT#17"))
        XCTAssertEqual(a.controllerId, "HC")
        XCTAssertEqual(a.label, "port 1")
        XCTAssertEqual(a.defaultLabel, "port 1 USB 3")
        let typeC = try XCTUnwrap(c.first { $0.usb2Port == 2 })
        XCTAssertTrue(typeC.typeC)
        XCTAssertEqual(typeC.defaultLabel, "port 2 USB-C")
        let usb2Only = try XCTUnwrap(c.first { $0.usb2Port == 3 })
        XCTAssertEqual(usb2Only.supportsUsb3, false)
        XCTAssertEqual(usb2Only.defaultLabel, "port 3 jen USB 2.0")
    }

    func testDeviceOnUsb2HalfOfUsb3Connector() throws {
        let a = try XCTUnwrap(build(root(onPort1: dev("disk", .high, .superSpeed))).first { $0.usb2Port == 1 })
        XCTAssertEqual(a.supportsUsb3, true)
        XCTAssertEqual(a.device?.link, .high)
    }

    func testTolerateMissingCompanionAndDevice() throws {
        let c = build(UsbHub(instanceId: "H", name: "hub", ports: [p(1, false, companion: 99), p(20, true, visible: false)]))
        XCTAssertEqual(c.count, 2)
        XCTAssertEqual(c.first { $0.usb2Port == 1 }?.usb3Port, 0)
        XCTAssertEqual(c.first { $0.usb3Port == 20 }?.defaultLabel, "port 20 USB 3 (interní)")
    }

    func testRecurseIntoExternalHubs() {
        let ext = UsbHub(instanceId: "EXT", name: "USB2.0 Hub", ports: [p(1, false, dev("reader", .high, .high))])
        let c = build(root(onPort3: dev("hub", .high, .high, hub: ext)))
        XCTAssertTrue(c.contains { $0.hubInstanceId == "EXT" && $0.device?.name == "reader" })
    }

    // Windows final review 1: hub ports are named by their path, never "port 1" again.
    func testHubPortsDoNotCollideWithRootPorts() {
        let ext = UsbHub(instanceId: "EXT", name: "USB2.0 Hub", ports: [
            p(1, false, dev("headset", .full, .full)), p(2, false), p(3, false),
        ])
        let c = build(root(onPort3: dev("hub", .high, .high, hub: ext)))
        XCTAssertEqual(Set(c.map(\.label)).count, c.count)
        XCTAssertEqual(c.first { $0.id == "EXT#1" }?.label, "port 3 › 1")
        XCTAssertEqual(c.first { $0.id == "EXT#2" }?.defaultLabel, "port 3 › 2 jen USB 2.0")
        XCTAssertEqual(c.first { $0.id == "ROOT#1" }?.label, "port 1")
    }

    // Windows final review 1 + 4: the controller goes into the name only with more than one; same names get numbers.
    func testControllerPrefixOnlyWithSeveralControllers() {
        func r(_ id: String) -> UsbHub { UsbHub(instanceId: id, name: "Root", ports: [p(1, false)]) }
        let one = UsbTopology(controllers: [UsbController(instanceId: "A", name: "USB řadič XHC1", rootHubs: [r("RA")])])
        XCTAssertEqual(one.connectors.map(\.label), ["port 1"])
        let two = UsbTopology(controllers: [UsbController(instanceId: "A", name: "USB řadič usb-drd0", rootHubs: [r("RA")]),
                                            UsbController(instanceId: "B", name: "USB řadič usb-drd1", rootHubs: [r("RB")])])
        XCTAssertEqual(two.connectors.map(\.label), ["USB řadič usb-drd0 · port 1", "USB řadič usb-drd1 · port 1"])
        XCTAssertEqual(two.connectors.map(\.controllerId), ["A", "B"])
        let amd = "AMD USB 3.10 eXtensible Host Controller - 1.10 (Microsoft)"
        let same = UsbTopology(controllers: [UsbController(instanceId: "A", name: amd, rootHubs: [r("RA")]),
                                             UsbController(instanceId: "B", name: amd, rootHubs: [r("RB")])])
        XCTAssertEqual(same.connectors.map(\.label), ["AMD USB 3.10 xHCI (1) · port 1", "AMD USB 3.10 xHCI (2) · port 1"])
    }

    // Windows final review 8: unknown kind → no speed claim; mutual companions still make a USB 3 socket.
    func testUnknownPortKind() throws {
        let c = build(UsbHub(instanceId: "ROOT", name: "Root", ports: [p(1, nil, companion: 5), p(2, nil), p(5, nil, companion: 1)]))
        XCTAssertEqual(c.count, 2)
        let paired = try XCTUnwrap(c.first { $0.number == 1 })
        XCTAssertEqual(paired.supportsUsb3, true)
        XCTAssertEqual(paired.usb3Port, 5)
        let unknown = try XCTUnwrap(c.first { $0.number == 2 })
        XCTAssertNil(unknown.supportsUsb3)
        XCTAssertEqual(unknown.defaultLabel, "port 2")
    }

    func testDualHalfHubMergedByPortNumber() throws {
        let hubPorts = dualHalfHub().filter { $0.label.hasPrefix("port 1 › ") }
        XCTAssertEqual(hubPorts.count, 3)
        XCTAssertTrue(hubPorts.allSatisfy { $0.supportsUsb3 == true })
        XCTAssertEqual(hubPorts.first { $0.number == 1 }?.device?.name, "ssd")
        let reader = try XCTUnwrap(hubPorts.first { $0.number == 2 })
        XCTAssertEqual(reader.device?.name, "reader")
        XCTAssertTrue(reader.has("HS#2") && reader.has("SS#2"))
        XCTAssertNil(hubPorts.first { $0.number == 3 }?.device)
        XCTAssertEqual(hubPorts.first { $0.number == 3 }?.defaultLabel, "port 1 › 3 USB 3")
    }

    func testPortLabelsUseTheUsersNameUnderAnyId() throws {
        let c = try XCTUnwrap(build(root()).first { $0.usb2Port == 1 })
        XCTAssertEqual(PortLabels.name(c, ["ROOT#17": "přední levý"]), "přední levý")
        XCTAssertEqual(PortLabels.withKind(c, ["ROOT#1": " zadní "]), "zadní USB 3")
        XCTAssertEqual(PortLabels.withKind(c, ["ROOT#1": "  "]), "port 1 USB 3")
        XCTAssertEqual(PortLabels.find([c], "ROOT#17")?.id, "ROOT#1")
        XCTAssertEqual(PortLabels.shortController("Intel(R) USB 3.20 eXtensible Host Controller - 1.20 (Microsoft)"), "Intel USB 3.20 xHCI")
        XCTAssertEqual(PortLabels.shortController("USB řadič XHC1"), "USB řadič XHC1")
    }
}
