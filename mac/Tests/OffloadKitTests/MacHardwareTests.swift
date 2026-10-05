import XCTest
@testable import OffloadKit

final class MacHardwareTests: XCTestCase {
    // Pure: how the HS/SS halves of one socket are paired, and the safe fallback without a pairing key.
    func testPairByKeyAndUnknownWithoutIt() {
        let paired = UsbScanner.pair([
            .init(number: 1, usb3: false, pairKey: 7), .init(number: 2, usb3: false, pairKey: nil),
            .init(number: 17, usb3: true, pairKey: 7), .init(number: 18, usb3: true, pairKey: nil),
        ])
        XCTAssertEqual(paired.map(\.number), [1, 2, 17, 18])
        XCTAssertEqual(paired.map(\.companion), [17, 0, 1, 0])
        XCTAssertEqual(paired[0].usb3, false)
        XCTAssertNil(paired[1].usb3, "a USB 2 port without a twin next to USB 3 ports is of unknown kind")
        XCTAssertEqual(paired[3].usb3, true)
    }

    // Apple Silicon: one HS and one SS port per usb-drd controller, no pairing key → still one socket.
    func testSingleHsAndSsPortAreOneSocketWithoutAKey() {
        let paired = UsbScanner.pair([.init(number: 1, usb3: false, pairKey: nil), .init(number: 1001, usb3: true, pairKey: nil)])
        XCTAssertEqual(paired.map(\.companion), [1001, 1])
        XCTAssertEqual(paired.map(\.usb3), [false, true])
        let hub = UsbHub(instanceId: "C", name: "C", ports: paired.map {
            UsbPort(number: $0.number, supportsUsb3: $0.usb3, userVisible: true, typeC: true, companionPort: $0.companion, device: nil)
        })
        XCTAssertEqual(Connectors.build(hub, controllerId: "C", controllerName: "USB řadič usb-drd0").map(\.defaultLabel), ["port 1 USB-C"])
    }

    func testRepeatedPortNumbersAreMadeUnique() {
        XCTAssertEqual(UsbScanner.uniqueNumbers([1, 1, 2, 1]), [1, 1001, 2, 2001])
        XCTAssertEqual(UsbScanner.uniqueNumbers([3, 17]), [3, 17])
    }

    // USB 2 ports first: a repeated number is taken from the USB 3 port, so the label keeps the USB 2 twin's number.
    func testUsb2PortsAreNumberedFirst() {
        let kinds: [Bool?] = [true, nil, false, true, false]
        XCTAssertEqual(UsbScanner.usb2First(Array(kinds.indices), kind: { kinds[$0] }), [2, 4, 1, 0, 3])
    }

    // Intel Macs with Thunderbolt 3 (verified on a MacBook Pro 2019): the USB 2 half of each USB-C socket sits on XHC1,
    // the USB 3 half on a Thunderbolt controller (XHC2/XHC3). One socket, USB 3, found under either half's id.
    func testCrossControllerTwinsAreOneUsb3Socket() throws {
        let connectors = UsbScanner.scan().connectors
        var checked = 0, seen = Set<UInt64>()
        for c in UsbScanner.Keys.controllerClasses.flatMap(IOEntry.matching) where seen.insert(c.entryId).inserted {
            for p in c.children.filter(UsbScanner.isPort) where UsbScanner.portKind(p) == false {
                guard let t = UsbScanner.foreignTwin(p, of: c.entryId) else { continue }
                checked += 1
                let ids = [p, t].compactMap { port -> String? in
                    guard let owner = UsbScanner.listedOwner(of: port) else { return nil }
                    let number = UsbScanner.rawPorts(of: owner).first { $0.entry.entryId == port.entryId }?.raw.number
                    return number.map { "\(owner.path)#\($0)" }
                }
                guard ids.count == 2 else {
                    XCTFail("\(p.path): both halves must be listed")
                    continue
                }
                let socket = try XCTUnwrap(PortLabels.find(connectors, ids[0]), "\(p.path): socket missing")
                XCTAssertTrue(socket.has(ids[1]), "\(socket.label): the USB 3 half is a separate socket")
                XCTAssertEqual(socket.supportsUsb3, true, "\(socket.label): a USB-C socket with a USB 3 twin is not USB 2 only")
            }
        }
        guard checked > 0 else { throw XCTSkip("no USB 2 port with its USB 3 twin on another controller on this Mac") }
    }

    // Without a USB disk: every USB device the registry shows (hubs, readers, mice) is placed by `usbLink` — the code
    // that places a disk — into a socket of the port list, so a drive's socket and the list always agree.
    func testEveryUsbDeviceLinksToASocketInThePortList() throws {
        let connectors = UsbScanner.scan().connectors
        let scanned = Set(UsbScanner.Keys.controllerClasses.lazy.map(IOEntry.matching).first { !$0.isEmpty }?.map(\.path) ?? [])
        var checked = 0, seen = Set<UInt64>()
        for d in UsbScanner.Keys.deviceClasses.flatMap(IOEntry.matching) where seen.insert(d.entryId).inserted {
            guard let u = DriveLinks.usbLink(d.ancestors), scanned.contains(u.controllerId) else { continue }
            checked += 1
            let socket = try XCTUnwrap(PortLabels.find(connectors, u.connectorId), "\(UsbScanner.productName(d)): \(u.connectorId) not in the port list")
            print("device:", UsbScanner.productName(d), "→", PortLabels.withKind(socket), "| link", UsbSpeeds.label(u.link),
                  "| hubs", u.hubs.map(\.name), "| usb3 socket", u.connectorUsb3 as Any)
            XCTAssertNotNil(socket.device, "\(socket.label): the socket shows no device")
            XCTAssertNotEqual(u.link, .unknown)
            XCTAssertEqual(PortLabels.of(u, connectors), socket.label)
            if let usb3 = u.connectorUsb3 { XCTAssertEqual(socket.supportsUsb3, usb3, "\(socket.label): drive line and port list disagree") }
        }
        guard checked > 0 else { throw XCTSkip("no USB device on a scanned controller") }
    }

    func testUsb2OnlyHubStaysUsb2() {
        let paired = UsbScanner.pair([.init(number: 1, usb3: false, pairKey: nil), .init(number: 2, usb3: false, pairKey: nil)])
        XCTAssertEqual(paired.map(\.usb3), [false, false])
    }

    func testPairedRootFeedsTheConnectorBuilder() {
        let paired = UsbScanner.pair([.init(number: 1, usb3: false, pairKey: 7), .init(number: 2, usb3: false, pairKey: nil),
                                      .init(number: 17, usb3: true, pairKey: 7)])
        let hub = UsbHub(instanceId: "C", name: "C", ports: paired.map {
            UsbPort(number: $0.number, supportsUsb3: $0.usb3, userVisible: true, typeC: false, companionPort: $0.companion, device: nil)
        })
        let c = Connectors.build(hub, controllerId: "C", controllerName: "USB řadič XHC1")
        XCTAssertEqual(c.map(\.defaultLabel), ["port 1 USB 3", "port 2"])
        XCTAssertFalse(Advisor.advise([], connectors: c).contains { $0.text.contains("jen USB 2.0") })
    }

    func testBusFromProtocolAndClasses() {
        XCTAssertEqual(DriveLinks.busFrom(protocol: "USB", classes: [], hasUsbDevice: true), .usb)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "PCI-Express", classes: ["IOMedia", "IONVMeController", "AppleThunderboltPCIDownAdapter"], hasUsbDevice: false), .thunderbolt)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "Apple Fabric", classes: ["IOMedia", "AppleANS3NVMeController"], hasUsbDevice: false), .nvme)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "PCI-Express", classes: ["IOMedia", "IONVMeController"], hasUsbDevice: false), .nvme)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "SATA", classes: [], hasUsbDevice: false), .sata)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "Secure Digital", classes: [], hasUsbDevice: false), .sd)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "Disk Image", classes: [], hasUsbDevice: false), .virtual)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "USB", classes: [], hasUsbDevice: false), .usb)   // USB without a port found
        XCTAssertEqual(DriveLinks.busFrom(protocol: nil, classes: [], hasUsbDevice: false), .unknown)
        XCTAssertEqual(DriveLinks.busFrom(protocol: "FireWire", classes: [], hasUsbDevice: false), .other)
    }

    // Smoke tests on the real Mac: never crash, sensible answers.
    func testScanNeverCrashesAndLabelsAreUnique() {
        let connectors = UsbScanner.scan().connectors
        XCTAssertEqual(Set(connectors.map(\.label)).count, connectors.count)
        for c in connectors { print("USB", c.controllerName, "|", c.defaultLabel, "|", c.device.map { "\($0.name) \(UsbSpeeds.label($0.link))" } ?? "volný") }
    }

    func testStartupVolumeIsInternalWithAProduct() {
        let d = DriveLinks.resolve("/")
        print("startup volume:", d.describe())
        XCTAssertNotEqual(d.bus, .usb)
        XCTAssertNotEqual(d.bus, .network)
        XCTAssertFalse(d.product.isEmpty, "DiskArbitration model of the startup disk")
    }

    func testMissingPathIsUnknownNotACrash() {
        XCTAssertEqual(DriveLinks.resolve("/no/such/path-\(UUID().uuidString)").bus, .unknown)
    }

    func testUsbVolumesResolveToASocket() throws {
        let volumes = (FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: [.skipHiddenVolumes]) ?? [])
            .map(\.path).filter { $0.hasPrefix("/Volumes/") }
        let usb = volumes.map(DriveLinks.resolve).filter { $0.bus == .usb }
        guard !usb.isEmpty else { throw XCTSkip("no USB volume connected") }
        let connectors = UsbScanner.scan().connectors
        for d in usb {
            print("USB volume:", d.root, "→", d.describe(connectors), "uasp:", d.usb?.uasp as Any)
            let u = try XCTUnwrap(d.usb, "\(d.root): USB volume without a port")
            XCTAssertNotEqual(u.link, .unknown)
            XCTAssertNotNil(PortLabels.find(connectors, u.connectorId), "\(d.root): the drive's socket is not in the port list")
        }
    }
}
