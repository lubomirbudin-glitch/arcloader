import Foundation

public struct UsbDevice: Codable, Equatable {
    public let instanceId: String
    public let vid: Int
    public let pid: Int
    public let name: String
    public let link: UsbLinkSpeed
    public let capable: UsbLinkSpeed
    public let hub: UsbHub?
}

/// supportsUsb3: nil when the registry could not say — never "USB 2.0 only" without proof.
public struct UsbPort: Codable, Equatable {
    public let number: Int
    public let supportsUsb3: Bool?
    public let userVisible: Bool
    public let typeC: Bool
    public let companionPort: Int
    public let device: UsbDevice?
}

public struct UsbHub: Codable, Equatable {
    public let instanceId: String
    public let name: String
    public let ports: [UsbPort]
}

public struct UsbController: Codable, Equatable {
    public let instanceId: String
    public let name: String
    public let rootHubs: [UsbHub]
}

/// One physical socket: a root hub lists it twice — a USB 2 port and its USB 3 twin; an external USB 3 hub is two hub
/// devices (USB 3 half + USB 2 half) with the same port numbers. `label` is the socket's unique default name
/// ("port 6", "port 7 › 2", "<řadič> · port 3"). Port of Windows `UsbTopology.cs`.
public struct UsbConnector: Codable, Equatable {
    public let id: String
    public let hubInstanceId: String
    public let controllerId: String
    public let controllerName: String
    public let number: Int
    public let usb2Port: Int
    public let usb3Port: Int
    public let supportsUsb3: Bool?
    public let typeC: Bool
    public let userVisible: Bool
    public let device: UsbDevice?
    public let label: String
    /// Other ids under which a drive may report this socket (the port on the other half).
    public var altIds: [String] = []

    public func has(_ connectorId: String) -> Bool { id == connectorId || altIds.contains(connectorId) }

    /// "USB 3", "USB-C", "jen USB 2.0", "USB-C jen USB 2.0", "" (unknown) + " (interní)".
    public var kind: String {
        let speed: String
        switch supportsUsb3 {
        case true?: speed = typeC ? "USB-C" : "USB 3"
        case false?: speed = typeC ? "USB-C jen USB 2.0" : "jen USB 2.0"
        case nil: speed = typeC ? "USB-C" : ""
        }
        return userVisible ? speed : (speed + " (interní)").trimmingCharacters(in: .whitespaces)
    }

    public var defaultLabel: String { kind.isEmpty ? label : "\(label) \(kind)" }
}

public struct UsbTopology: Codable, Equatable {
    public let controllers: [UsbController]

    public var connectors: [UsbConnector] {
        let short = controllers.map { PortLabels.shortController($0.name) }
        return controllers.enumerated().flatMap { i, c -> [UsbConnector] in
            // Only with more than one controller does a port need the controller in its name; same names get a number.
            let prefix: String
            if controllers.count <= 1 {
                prefix = ""
            } else if short.filter({ $0 == short[i] }).count > 1 {
                prefix = "\(short[i]) (\(short[...i].filter { $0 == short[i] }.count)) · "
            } else {
                prefix = "\(short[i]) · "
            }
            return c.rootHubs.flatMap { Connectors.build($0, controllerId: c.instanceId, controllerName: c.name, prefix: prefix) }
        }
    }
}

public enum Connectors {
    struct Slot {
        var usb2: UsbPort? = nil
        var usb2Hub: String? = nil
        var usb3: UsbPort? = nil
        var usb3Hub: String? = nil
        var unknown: UsbPort? = nil
        var unknownHub: String? = nil

        var ports: [UsbPort] { [usb2, usb3, unknown].compactMap { $0 } }
        var number: Int { usb2?.number ?? usb3?.number ?? unknown!.number }
    }

    /// The sockets of a root hub (pairs its USB 2 / USB 3 companions) and of every hub below it.
    public static func build(_ hub: UsbHub, controllerId: String, controllerName: String, prefix: String = "") -> [UsbConnector] {
        build(hub, twin: nil, controllerId: controllerId, controllerName: controllerName, labelStart: prefix + "port ")
    }

    // hub: one hub, or (when twin is set) the USB 2 half of an external USB 3 hub whose USB 3 half is `twin`.
    static func build(_ hub: UsbHub, twin: UsbHub?, controllerId: String, controllerName: String, labelStart: String) -> [UsbConnector] {
        var result: [UsbConnector] = []
        for slot in twin.map({ pairAcrossHalves(hub, $0) }) ?? pairWithinHub(hub) {
            let number = slot.number
            let mainHub = slot.usb2Hub ?? slot.usb3Hub ?? slot.unknownHub!
            var ids: [String] = []
            for (h, p) in [(slot.usb2Hub, slot.usb2), (slot.usb3Hub, slot.usb3), (slot.unknownHub, slot.unknown)] {
                if let h, let p, !ids.contains("\(h)#\(p.number)") { ids.append("\(h)#\(p.number)") }
            }
            let id = "\(mainHub)#\(number)"
            let usb3: Bool? = slot.usb3 != nil ? true : (slot.unknown != nil ? slot.unknown!.supportsUsb3 : false)
            let usb3Device = slot.usb3?.device
            let usb2Device = slot.usb2?.device ?? slot.unknown?.device
            let label = labelStart + String(number)
            var c = UsbConnector(id: id, hubInstanceId: mainHub, controllerId: controllerId, controllerName: controllerName,
                                 number: number, usb2Port: slot.usb2?.number ?? 0, usb3Port: slot.usb3?.number ?? 0,
                                 supportsUsb3: usb3, typeC: slot.ports.contains { $0.typeC },
                                 userVisible: slot.ports.contains { $0.userVisible }, device: usb3Device ?? usb2Device, label: label)
            c.altIds = ids.filter { $0 != id }
            result.append(c)
            // A USB 3 hub shows up on both halves of the socket: walk both, so USB 2 devices behind it don't vanish.
            let childStart = label + " › "
            if let h2 = usb2Device?.hub, let h3 = usb3Device?.hub {
                result += build(h2, twin: h3, controllerId: controllerId, controllerName: controllerName, labelStart: childStart)
            } else if let child = usb3Device?.hub ?? usb2Device?.hub {
                result += build(child, twin: nil, controllerId: controllerId, controllerName: controllerName, labelStart: childStart)
            }
        }
        return result
    }

    // Root hub: USB 2 port n and its USB 3 companion m are one socket. Ports of unknown kind pair only when both
    // name each other as companions (then the socket must be USB 3: only USB 3 sockets have companions).
    static func pairWithinHub(_ hub: UsbHub) -> [Slot] {
        var slots: [Slot] = []
        var done = Set<Int>()
        let ordered = hub.ports.sorted { ($0.supportsUsb3 == true ? 1 : 0, $0.number) < ($1.supportsUsb3 == true ? 1 : 0, $1.number) }
        for port in ordered {
            guard done.insert(port.number).inserted else { continue }
            var twin: UsbPort?
            if port.companionPort > 0 && port.companionPort != port.number {
                twin = hub.ports.first { $0.number == port.companionPort && !done.contains($0.number) && isTwin(port, $0) }
            }
            if let t = twin { done.insert(t.number) }
            if twin == nil && port.supportsUsb3 == nil {
                slots.append(Slot(unknown: port, unknownHub: hub.instanceId))
                continue
            }
            let usb2: UsbPort?, usb3: UsbPort?
            if let t = twin {
                (usb2, usb3) = order(port, t)
            } else if port.supportsUsb3 == true {
                (usb2, usb3) = (nil, port)
            } else {
                (usb2, usb3) = (port, nil)
            }
            slots.append(Slot(usb2: usb2, usb2Hub: usb2 == nil ? nil : hub.instanceId,
                              usb3: usb3, usb3Hub: usb3 == nil ? nil : hub.instanceId))
        }
        return slots.sorted { $0.number < $1.number }
    }

    static func isTwin(_ a: UsbPort, _ b: UsbPort) -> Bool {
        if let x = a.supportsUsb3, let y = b.supportsUsb3 { return x != y }
        return b.companionPort == a.number
    }

    static func order(_ a: UsbPort, _ b: UsbPort) -> (UsbPort?, UsbPort?) {
        if a.supportsUsb3 == true || b.supportsUsb3 == false { return (b, a) }
        if b.supportsUsb3 == true || a.supportsUsb3 == false { return (a, b) }
        return a.number < b.number ? (a, b) : (b, a)
    }

    // External USB 3 hub: port n of the USB 2 half and port n of the USB 3 half are the same socket.
    static func pairAcrossHalves(_ usb2Half: UsbHub, _ usb3Half: UsbHub) -> [Slot] {
        let numbers = Set(usb2Half.ports.map(\.number)).union(usb3Half.ports.map(\.number)).sorted()
        return numbers.map { n in
            let p2 = usb2Half.ports.first { $0.number == n }
            let p3 = usb3Half.ports.first { $0.number == n }
            return Slot(usb2: p2, usb2Hub: p2 == nil ? nil : usb2Half.instanceId, usb3: p3, usb3Hub: p3 == nil ? nil : usb3Half.instanceId)
        }
    }
}
