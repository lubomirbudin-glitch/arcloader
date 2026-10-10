import Foundation

/// The USB topology from the IORegistry (IOService plane): controllers → ports → devices → hubs → …
/// Never throws: what cannot be read is left out. All registry names are in `Keys` (verify with ioreg, spec 9).
public enum UsbScanner {
    enum Keys {
        static let controllerClasses = ["AppleUSBXHCI", "AppleUSBHostController"]
        static let portClass = "AppleUSBHostPort"
        static let deviceClasses = ["IOUSBHostDevice", "IOUSBDevice"]
        static let portNumber = "port"
        /// The property the HS and SS halves of one socket share (Task 14 step 1 decides; more candidates allowed).
        /// Intel MacBook Pro 2019 (macOS 26): only the USB 3 halves have it — it pairs nothing there; `twinKeys` do.
        static let pairKeys = ["UsbCPortNumber"]
        /// Registry path of the other half of the socket: on a USB 2 port its USB 3 twin, on a USB 3 port its USB 2
        /// twin. Verified on an Intel MacBook Pro 2019: the USB 2 halves of all four USB-C sockets sit on the PCH
        /// controller XHC1, the USB 3 halves on the Thunderbolt controllers XHC2/XHC3 — twins across controllers.
        static let twinKeys = ["UsbProtocolCompanion (3.x)", "UsbProtocolCompanion (2.0)", "UsbProtocolCompanion (1.x)"]
        /// ACPI _UPC connector type: 9/10/11 = USB-C, 255 = internal / proprietary.
        static let connectorType = "UsbConnector"
        static let productKeys = ["USB Product Name", "kUSBProductString"]
        static let linkSpeed = "UsbLinkSpeed"
        static let deviceSpeed = "Device Speed"
        static let bcdUSB = "bcdUSB"
        static let deviceClass = "bDeviceClass"
        static let locationID = "locationID"
        static let vendorID = "idVendor"
        static let productID = "idProduct"
        static let uasClassFragment = "IOUSBAttachedSCSI"
    }

    /// One port as the registry shows it: number, kind (nil = unknown) and the key the two halves of one socket share.
    struct RawPort: Equatable {
        let number: Int
        let usb3: Bool?
        let pairKey: Int?
    }

    public static func scan() -> UsbTopology {
        var controllers: [UsbController] = []
        var seen = Set<String>()
        for cls in Keys.controllerClasses where controllers.isEmpty {   // the base class only as a fallback
            for c in IOEntry.matching(cls) {
                let id = c.path
                guard !id.isEmpty, seen.insert(id).inserted else { continue }
                let name = controllerName(c)
                let root = readHub(c, id: id, name: name, depth: 0)
                // A controller whose every port joined a twin on another controller has no socket of its own.
                guard !root.ports.isEmpty else { continue }
                controllers.append(UsbController(instanceId: id, name: name, rootHubs: [root]))
            }
        }
        return UsbTopology(controllers: controllers.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending })
    }

    /// Companion numbers from the shared key; a hub/controller with exactly one USB 2 and one USB 3 port (Apple Silicon
    /// `usb-drd*`: one USB-C socket per controller) pairs them even without a key. A USB 2 port still without a twin
    /// on a hub that also has USB 3 ports is of unknown kind — never claim "jen USB 2.0" without proof.
    /// Port numbers must be unique here (`rawPorts` makes them so).
    static func pair(_ ports: [RawPort]) -> [(number: Int, usb3: Bool?, companion: Int)] {
        let hasUsb3 = ports.contains { $0.usb3 == true }
        let usb2Ports = ports.filter { $0.usb3 == false }, usb3Ports = ports.filter { $0.usb3 == true }
        let single: (usb2: Int, usb3: Int)? = usb2Ports.count == 1 && usb3Ports.count == 1
            ? (usb2Ports[0].number, usb3Ports[0].number) : nil
        return ports.map { p in
            var companion = p.pairKey.flatMap { key in
                ports.first { $0.number != p.number && $0.pairKey == key && $0.usb3 != nil && $0.usb3 != p.usb3 }
            }?.number ?? 0
            if companion == 0, let single {
                if p.number == single.usb2 {
                    companion = single.usb3
                } else if p.number == single.usb3 {
                    companion = single.usb2
                }
            }
            var usb3 = p.usb3
            if companion == 0 && p.usb3 == false && hasUsb3 { usb3 = nil }
            return (p.number, usb3, companion)
        }
    }

    /// Port numbers made unique within one hub/controller: a repeated number (e.g. HS and SS port both "port" = 1)
    /// gets +1000 per repeat. Only ids and pairing see it; labels use the USB 2 twin's number.
    static func uniqueNumbers(_ numbers: [Int]) -> [Int] {
        var seen = Set<Int>()
        return numbers.map { n in
            var m = n
            while !seen.insert(m).inserted { m += 1000 }
            return m
        }
    }

    static func isPort(_ e: IOEntry) -> Bool { e.conforms(to: Keys.portClass) || e.className.hasSuffix("Port") }
    static func isDevice(_ e: IOEntry) -> Bool { Keys.deviceClasses.contains { e.conforms(to: $0) } }
    static func isController(_ e: IOEntry) -> Bool { Keys.controllerClasses.contains { e.conforms(to: $0) } }

    /// USB 3 from the port class or name (AppleUSB30XHCIPort, AppleUSB30HubPort, "SS01", "…-ss@…"), USB 2 likewise; else nil.
    static func portKind(_ e: IOEntry) -> Bool? {
        let cls = e.className, name = e.name.lowercased()
        if cls.contains("USB30") || cls.contains("USB31") || name.hasPrefix("ss") || name.contains("-ss") { return true }
        if cls.contains("USB20") || name.hasPrefix("hs") || name.contains("-hs") { return false }
        return nil
    }

    /// The ports of a controller or hub device: its children that are ports, and the ports one level down under a
    /// child that is neither port nor device (a hub's ports hang under its driver, e.g. AppleUSB30Hub — verified).
    /// A controller's root hub also takes the USB 2 twins (on another controller) of its USB 3 ports and gives away
    /// its USB 2 ports whose USB 3 twin is on another controller: one socket, listed once, under its USB 3 controller.
    /// USB 2 ports come first, so they keep their number when `uniqueNumbers` renumbers a repeat.
    static func portEntries(of owner: IOEntry) -> [IOEntry] {
        var out: [IOEntry] = []
        for child in owner.children {
            if isPort(child) {
                out.append(child)
            } else if !isDevice(child) {
                out += child.children.filter(isPort)
            }
        }
        if isController(owner) {
            let me = owner.entryId
            out = out.filter { p in !(portKind(p) == false && foreignTwin(p, of: me) != nil) }
            out += out.filter { portKind($0) == true }.compactMap { p in
                foreignTwin(p, of: me).flatMap { t in portKind(t) == false ? t : nil }
            }
        }
        return usb2First(out, kind: portKind)
    }

    /// Stable order: USB 2 ports, then ports of unknown kind, then USB 3 ports.
    static func usb2First<T>(_ items: [T], kind: (T) -> Bool?) -> [T] {
        func rank(_ k: Bool?) -> Int { k == false ? 0 : (k == nil ? 1 : 2) }
        return items.enumerated().sorted { (rank(kind($0.element)), $0.offset) < (rank(kind($1.element)), $1.offset) }.map(\.element)
    }

    /// The other half of a socket, when both name each other (`Keys.twinKeys`).
    static func twin(_ port: IOEntry) -> IOEntry? {
        guard let path = Keys.twinKeys.lazy.compactMap({ port.string($0) }).first(where: { !$0.isEmpty }),
              let t = IOEntry.fromPath(path), isPort(t), t.entryId != port.entryId,
              let back = Keys.twinKeys.lazy.compactMap({ t.string($0) }).first(where: { !$0.isEmpty }),
              IOEntry.fromPath(back)?.entryId == port.entryId else { return nil }
        return t
    }

    /// The twin of a root port when it sits directly on another USB controller than `controllerId`.
    static func foreignTwin(_ port: IOEntry, of controllerId: UInt64) -> IOEntry? {
        guard let t = twin(port), let c = t.parent, isController(c), c.entryId != controllerId else { return nil }
        return t
    }

    /// The owner a port is listed under — its device or controller, or the controller its USB 3 twin is on.
    static func listedOwner(of port: IOEntry) -> IOEntry? {
        guard let owner = port.ancestors.dropFirst().first(where: { isDevice($0) || isController($0) }) else { return nil }
        if isController(owner), portKind(port) == false, let t = foreignTwin(port, of: owner.entryId), let c = t.parent {
            return c
        }
        return owner
    }

    /// The other half of the socket a port is in, paired as the port list pairs it: the companion in its own hub or
    /// controller (`pair`), or — on a half of an external USB 3 hub (two hub devices, no companion data on their
    /// ports, verified) — the port with the same number on the other half (`Connectors.pairAcrossHalves`).
    static func twinPort(of port: IOEntry, depth: Int = 0) -> IOEntry? {
        guard depth < 8, let owner = listedOwner(of: port) else { return nil }
        let raws = rawPorts(of: owner)
        guard let me = raws.first(where: { $0.entry.entryId == port.entryId }) else { return nil }
        if let c = pair(raws.map(\.raw)).first(where: { $0.number == me.raw.number })?.companion, c > 0 {
            return raws.first { $0.raw.number == c }?.entry
        }
        guard isDevice(owner), let hubPort = owner.parent, isPort(hubPort), let other = twinPort(of: hubPort, depth: depth + 1),
              let otherHub = other.children.first(where: isDevice), otherHub.int(Keys.deviceClass) == 9 else { return nil }
        return rawPorts(of: otherHub).first { $0.raw.number == me.raw.number }?.entry
    }

    /// Both the scan and `DriveLinks.usbLink` number ports through here, so ids and pairing always agree.
    /// Twins (`twin`) share a key made from the lower registry id; otherwise `Keys.pairKeys`.
    static func rawPorts(of owner: IOEntry) -> [(entry: IOEntry, raw: RawPort)] {
        let entries = portEntries(of: owner)
        let numbers = uniqueNumbers(entries.enumerated().map { i, e in e.int(Keys.portNumber) ?? (i + 1) })
        return entries.enumerated().map { i, e in
            let twinKey = twin(e).map { Int(truncatingIfNeeded: min($0.entryId, e.entryId)) }
            return (e, RawPort(number: numbers[i], usb3: portKind(e),
                               pairKey: twinKey ?? Keys.pairKeys.lazy.compactMap { e.int($0) }.first))
        }
    }

    static func readHub(_ owner: IOEntry, id: String, name: String, depth: Int) -> UsbHub {
        let raws = rawPorts(of: owner)
        let paired = pair(raws.map(\.raw))
        var ports: [UsbPort] = []
        for (i, item) in raws.enumerated() {
            let connector = item.entry.int(Keys.connectorType)
            let device = item.entry.children.first(where: isDevice).map { readDevice($0, depth: depth) }
            // A device already running at SuperSpeed proves the socket can do USB 3.
            let usb3 = device.map { $0.link >= .superSpeed } == true ? true : paired[i].usb3
            ports.append(UsbPort(number: paired[i].number, supportsUsb3: usb3, userVisible: connector != 255,
                                 typeC: [9, 10, 11].contains(connector ?? -1), companionPort: paired[i].companion, device: device))
        }
        return UsbHub(instanceId: id, name: name, ports: ports)
    }

    static func readDevice(_ e: IOEntry, depth: Int) -> UsbDevice {
        let link = linkSpeed(e)
        var hub: UsbHub?
        if e.int(Keys.deviceClass) == 9, depth < 6 {
            hub = readHub(e, id: deviceId(e), name: productName(e), depth: depth + 1)
        }
        return UsbDevice(instanceId: deviceId(e), vid: e.int(Keys.vendorID) ?? 0, pid: e.int(Keys.productID) ?? 0,
                         name: productName(e), link: link, capable: UsbSpeeds.capability(bcdUSB: e.int(Keys.bcdUSB), link: link), hub: hub)
    }

    static func linkSpeed(_ e: IOEntry) -> UsbLinkSpeed {
        UsbSpeeds.link(linkBitsPerSecond: e.uint64(Keys.linkSpeed), deviceSpeed: e.int(Keys.deviceSpeed))
    }

    /// "loc:<locationID>" — stable for one socket position; the registry path when locationID is missing.
    static func deviceId(_ e: IOEntry) -> String {
        if let loc = e.int(Keys.locationID) { return String(format: "loc:%08x", loc) }
        return e.path
    }

    static func productName(_ e: IOEntry) -> String {
        for key in Keys.productKeys {
            if let s = e.string(key), !s.isEmpty { return s }
        }
        return e.name
    }

    /// "USB controller XHC1", "USB controller usb-drd1", "… (Thunderbolt)" only for a PCI controller behind a Thunderbolt
    /// tunnel (a dock, an Intel TB controller) — built-in Apple Silicon controllers are not PCI devices (verify, step 1h).
    static func controllerName(_ c: IOEntry) -> String {
        let above = c.ancestors
        let pci = above.contains { $0.conforms(to: "IOPCIDevice") }
        let thunderbolt = pci && above.dropFirst().contains { $0.className.contains("Thunderbolt") }
        return "USB controller \(c.name)" + (thunderbolt ? " (Thunderbolt)" : "")
    }
}
