import XCTest
@testable import OffloadKit

final class HwTestTests: XCTestCase {
    func testRolesCardsThenDestinations() {
        let r = HwTest.roles(cards: ["/Volumes/E"], destinations: ["/Volumes/D/a", "/Volumes/F"])
        XCTAssertEqual(r.map(\.role), ["Karta", "Cíl 1", "Cíl 2"])
        XCTAssertEqual(r.map(\.root), ["/Volumes/E", "/Volumes/D/a", "/Volumes/F"])
        XCTAssertEqual(HwTest.roles(cards: ["/Volumes/E", "/Volumes/G"], destinations: []).map(\.role), ["Karta 1", "Karta 2"])
    }

    func testRolesByDriveUseTheVolumeName() {
        let r = HwTest.roles(cards: ["/Volumes/G"], destinations: ["/Volumes/offload-test-E", "/Volumes/offload-test-D"], rolesByDrive: true)
        XCTAssertEqual(r.map(\.role), ["Karta", "offload-test-E", "offload-test-D"])
    }

    func testQuickTestNoMeasurementNothingWritten() {
        let dir = tempDir()
        let dest = dir.sub("dest")
        let r = HwTest.run(cards: [dir.sub("card")], destinations: [dest], measure: false)
        XCTAssertEqual(r.drives.count, 2)
        XCTAssertEqual(r.measurements, [])
        XCTAssertEqual(TestFS.files(in: dest), [])
    }

    func testMeasureReadsCard() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/clip.bin", size: 4 << 20, seed: 1)
        let r = HwTest.run(cards: [card], destinations: [], measure: true, bytesPerDevice: 8 << 20)
        XCTAssertEqual(r.measurements.count, 1)
        let m = try XCTUnwrap(r.measurements.first)
        XCTAssertGreaterThan(m.readMBps ?? 0, 0)
        XCTAssertNil(m.writeMBps)
        XCTAssertEqual(TestFS.files(in: card), ["clip.bin"])
        XCTAssertTrue(r.advice.contains { $0.text.hasPrefix("Odhad rychlosti offloadu") })
    }

    func testMeasureWritesDestinationAndCleansUp() throws {
        let dir = tempDir()
        let dest = dir.sub("dest")
        let r = HwTest.run(cards: [], destinations: [dest], measure: true, bytesPerDevice: 8 << 20)
        let m = try XCTUnwrap(r.measurements.first)
        XCTAssertGreaterThan(m.writeMBps ?? 0, 0)
        XCTAssertGreaterThan(m.readMBps ?? 0, 0)
        XCTAssertEqual(TestFS.files(in: dest), [])
    }

    // Review focus 5: a destination on the card's volume is never written to — no measurement, a warning first.
    func testMeasureDestinationOnCardVolumeSkipped() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/clip.bin", size: 4 << 20, seed: 1)
        let dest = dir.sub("dest")
        XCTAssertTrue(HwTest.sameVolume(card, dest))
        let r = HwTest.run(cards: [card], destinations: [dest], measure: true, bytesPerDevice: 8 << 20)
        let m = Dictionary(uniqueKeysWithValues: r.measurements.map { ($0.role, $0) })
        XCTAssertGreaterThan(m["Karta"]?.readMBps ?? 0, 0)
        XCTAssertNil(m["Cíl 1"]?.readMBps)
        XCTAssertNil(m["Cíl 1"]?.writeMBps)
        XCTAssertEqual(TestFS.files(in: dest), [])
        let warnings = r.advice.filter { $0.kind == .warning && $0.text.contains("stejném svazku") }
        XCTAssertEqual(warnings.count, 1)
        XCTAssertTrue(warnings[0].text.hasPrefix("Cíl 1 je na stejném svazku jako Karta"))
        XCTAssertEqual(r.advice.first?.kind, .warning)
    }

    func testMeasureCancelledStopsBeforeNextDevice() {
        let dir = tempDir()
        let dest = dir.sub("dest")
        let r = HwTest.run(cards: [], destinations: [dest], measure: true, bytesPerDevice: 8 << 20, isCancelled: { true })
        XCTAssertEqual(r.measurements, [])
        XCTAssertEqual(TestFS.files(in: dest), [])
    }

    // Review focus 5: cancelling stops inside the 1 GB-style file, deletes it, and skips the remaining devices.
    func testMeasureCancelledDuringWriteStopsAtOnceNoBenchFileLeft() {
        final class Checks { var n = 0 }
        let checks = Checks()
        let dir = tempDir()
        let dest1 = dir.sub("dest1"), dest2 = dir.sub("dest2")
        let r = HwTest.run(cards: [], destinations: [dest1, dest2], measure: true, bytesPerDevice: 512 << 20, isCancelled: {
            checks.n += 1
            return checks.n > 5
        })
        XCTAssertEqual(r.measurements, [])
        XCTAssertEqual(TestFS.files(in: dest1), [])
        XCTAssertEqual(TestFS.files(in: dest2), [])
    }

    func testLoadMissingOrPartialIsNil() throws {
        let dir = tempDir()
        let path = dir.path + "/hwtest.json"
        XCTAssertNil(HwTest.load(path))
        try "{}".write(toFile: path, atomically: true, encoding: .utf8)
        XCTAssertNil(HwTest.load(path))
        try #"{"time":"2026-10-05T10:00:00Z","drives":[],"connectors":[],"measurements":[]}"#.write(toFile: path, atomically: true, encoding: .utf8)
        XCTAssertNil(HwTest.load(path))
    }

    func testSaveLoadRoundTripNestedHubsMeasurementsAndAdvice() throws {
        let dir = tempDir()
        let path = dir.path + "/sub/hwtest.json"
        let inner = UsbHub(instanceId: "loc:14110000", name: "Hub",
                           ports: [UsbPort(number: 1, supportsUsb3: true, userVisible: true, typeC: false, companionPort: 0, device: nil)])
        let dev = UsbDevice(instanceId: "loc:14100000", vid: 0x1234, pid: 0x5678, name: "Disk", link: .superSpeed, capable: .superPlus, hub: inner)
        var conn = UsbConnector(id: "C#1", hubInstanceId: "C", controllerId: "C", controllerName: "USB řadič XHC1", number: 1,
                                usb2Port: 1, usb3Port: 2, supportsUsb3: true, typeC: true, userVisible: true, device: dev, label: "port 1")
        conn.altIds = ["C#2"]
        let link = DriveLink(root: "/Volumes/E", bus: .usb, product: "SSD",
                             usb: UsbLink(controllerId: "C", controllerName: "USB řadič XHC1", connectorId: "C#1", connectorPort: 1, port: 2,
                                          link: .superSpeed, capable: .superPlus, connectorUsb3: nil, uasp: true,
                                          hubs: [UsbHubRef(instanceId: "loc:14100000", name: "Dock")]))
        let time = Date(timeIntervalSince1970: 1_791_201_600)
        let r = HwTestResult(time: time, drives: [RoleLink(role: "Karta", link: link)], connectors: [conn],
                             measurements: [DriveMeasurement(role: "Karta", root: "/Volumes/E", readMBps: 123.5, writeMBps: nil)],
                             advice: [Advice(kind: .info, text: "Ahoj č")])
        try HwTest.save(r, to: path)
        let back = try XCTUnwrap(HwTest.load(path))
        XCTAssertEqual(back.time, time)
        XCTAssertEqual(back.drives, r.drives)
        XCTAssertEqual(back.connectors, r.connectors)
        XCTAssertTrue(back.connectors[0].has("C#2"))
        XCTAssertEqual(back.measurements, r.measurements)
        XCTAssertEqual(back.advice, r.advice)
    }
}
