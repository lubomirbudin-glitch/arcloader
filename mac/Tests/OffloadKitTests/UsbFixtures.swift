import XCTest
@testable import OffloadKit

/// USB topology fixtures shared by several test classes (no test class instantiates another).
extension XCTestCase {
    func dev(_ name: String, _ link: UsbLinkSpeed, _ cap: UsbLinkSpeed, hub: UsbHub? = nil) -> UsbDevice {
        UsbDevice(instanceId: "USB-\(name)", vid: 1, pid: 2, name: name, link: link, capable: cap, hub: hub)
    }

    func p(_ n: Int, _ usb3: Bool?, visible: Bool = true, typeC: Bool = false, companion: Int = 0, _ device: UsbDevice? = nil) -> UsbPort {
        UsbPort(number: n, supportsUsb3: usb3, userVisible: visible, typeC: typeC, companionPort: companion, device: device)
    }

    /// Root hub: USB 2 ports 1…3, USB 3 ports 17…18 as companions of 1 and 2.
    func root(onPort1: UsbDevice? = nil, onPort17: UsbDevice? = nil, onPort3: UsbDevice? = nil) -> UsbHub {
        UsbHub(instanceId: "ROOT", name: "Root hub", ports: [
            p(1, false, companion: 17, onPort1), p(2, false, typeC: true, companion: 18), p(3, false, onPort3),
            p(17, true, companion: 1, onPort17), p(18, true, typeC: true, companion: 2),
        ])
    }

    func build(_ hub: UsbHub) -> [UsbConnector] { Connectors.build(hub, controllerId: "HC", controllerName: "USB řadič XHC1") }

    // Windows final review 5: an external USB 3 hub is two hub devices — merged by port number; nothing vanishes.
    func dualHalfHub() -> [UsbConnector] {
        let ss = UsbHub(instanceId: "SS", name: "USB3.0 Hub", ports: [
            p(1, true, companion: 1, dev("ssd", .superSpeed, .superSpeed)), p(2, true, companion: 2), p(3, true, companion: 3),
        ])
        let hs = UsbHub(instanceId: "HS", name: "USB2.0 Hub", ports: [
            p(1, false, companion: 1), p(2, false, companion: 2, dev("reader", .high, .superSpeed)), p(3, false, companion: 3),
        ])
        return build(root(onPort1: dev("hs-hub", .high, .high, hub: hs), onPort17: dev("ss-hub", .superSpeed, .superSpeed, hub: ss)))
    }
}
