import XCTest
@testable import OffloadKit

final class UsbSpeedsTests: XCTestCase {
    func testLinkFromUsbLinkSpeedElseDeviceSpeed() {
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 5_000_000_000, deviceSpeed: 2), .superSpeed)   // UsbLinkSpeed wins
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 10_000_000_000, deviceSpeed: nil), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 20_000_000_000, deviceSpeed: nil), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 480_000_000, deviceSpeed: nil), .high)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 12_000_000, deviceSpeed: nil), .full)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 1_500_000, deviceSpeed: nil), .low)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: 0, deviceSpeed: 3), .superSpeed)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 0), .low)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 1), .full)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 2), .high)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 4), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 5), .superPlus)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: 9), .unknown)
        XCTAssertEqual(UsbSpeeds.link(linkBitsPerSecond: nil, deviceSpeed: nil), .unknown)
    }

    func testCapabilityFromBcdUsbNeverBelowTheLink() {
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0320, link: .high), .superSpeed)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0300, link: .superPlus), .superPlus)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0200, link: .high), .high)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: 0x0110, link: .full), .full)
        XCTAssertEqual(UsbSpeeds.capability(bcdUSB: nil, link: .high), .high)
    }

    func testLabelsAndCeilings() {
        XCTAssertEqual(UsbSpeeds.label(.high), "USB 2.0 (480 Mb/s)")
        XCTAssertEqual(UsbSpeeds.label(.superSpeed), "USB 3 (5 Gb/s)")
        XCTAssertEqual(UsbSpeeds.label(.superPlus), "USB 3 (10 Gb/s or more)")
        XCTAssertEqual(UsbSpeeds.label(.unknown), "unknown")
        XCTAssertEqual(UsbSpeeds.practicalMBps(.high), 40)
        XCTAssertEqual(UsbSpeeds.practicalMBps(.superSpeed), 450)
        XCTAssertEqual(UsbSpeeds.practicalMBps(.superPlus), 1000)
        XCTAssertEqual(UsbSpeeds.practicalMBps(.unknown), 0)
        XCTAssertLessThan(UsbLinkSpeed.high, UsbLinkSpeed.superSpeed)
    }
}
