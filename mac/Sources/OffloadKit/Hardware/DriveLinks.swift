import Foundation

public enum StorageBus: String, Codable {
    case unknown, usb, sata, nvme, sd, mmc, scsi, sas, raid, virtual, network, thunderbolt, other
}

public enum StorageBuses {
    /// Czech name of a connection without a USB link.
    public static func label(_ bus: StorageBus) -> String {
        switch bus {
        case .nvme: return "NVMe"
        case .sata: return "SATA"
        case .sd, .mmc: return "SD čtečka"
        case .virtual: return "virtuální disk"
        case .network: return "síťový disk"
        case .usb: return "USB (port nezjištěn)"
        case .scsi: return "SCSI"
        case .sas: return "SAS"
        case .raid: return "RAID"
        case .thunderbolt: return "Thunderbolt"
        case .other: return "jiné připojení"
        case .unknown: return "neznámé připojení"
        }
    }
}

/// A hub between a device and its controller (a dock, an external hub, a monitor's hub).
public struct UsbHubRef: Codable, Equatable {
    public let instanceId: String
    public let name: String
}

/// Where a USB drive sits. `connectorId` matches `UsbConnector.id` or one of its `altIds`; `connectorPort` is the
/// socket's number for the fallback label; `port` the raw port of the half the device is on.
/// `connectorUsb3`: nil = unknown. `hubs`: from the device up to the controller, nearest first; empty = straight in.
public struct UsbLink: Codable, Equatable {
    public let controllerId: String
    public let controllerName: String
    public let connectorId: String
    public let connectorPort: Int
    public let port: Int
    public let link: UsbLinkSpeed
    public let capable: UsbLinkSpeed
    public let connectorUsb3: Bool?
    public let uasp: Bool
    public var hubs: [UsbHubRef] = []
}

public struct DriveLink: Codable, Equatable {
    public let root: String
    public let bus: StorageBus
    public let product: String
    public let usb: UsbLink?

    /// "USB 3 (5 Gb/s), port 6" or "SATA" — the socket named through `PortLabels`, like the port list.
    public func connection(_ connectors: [UsbConnector]? = nil, _ names: [String: String]? = nil) -> String {
        if let u = usb { return "\(UsbSpeeds.label(u.link)), \(PortLabels.of(u, connectors, names))" }
        return StorageBuses.label(bus)
    }

    /// "WD Elements 25A2 — USB 3 (5 Gb/s), port 6"; without a product just the connection.
    public func describe(_ connectors: [UsbConnector]? = nil, _ names: [String: String]? = nil) -> String {
        product.isEmpty ? connection(connectors, names) : "\(product) — \(connection(connectors, names))"
    }
}

public enum DriveLinks {
    /// A network location: the share as the product, else the mount point.
    static func network(_ root: String, _ share: String?) -> DriveLink {
        let s = share?.trimmingCharacters(in: .whitespaces) ?? ""
        return DriveLink(root: root, bus: .network, product: s.isEmpty ? root : s, usb: nil)
    }
}

extension PortLabels {
    /// The name of the socket a drive is plugged into; without the topology, "port N" from the link itself.
    public static func of(_ u: UsbLink, _ connectors: [UsbConnector]?, _ names: [String: String]? = nil) -> String {
        if let c = find(connectors, u.connectorId) { return name(c, names) }
        return "port \(u.connectorPort)"
    }

    /// One row of the port list: "port 6 USB 3   Elements 25A2 — USB 3 (5 Gb/s)  ← SSD_A (Cíl 1)".
    public static func listRow(_ c: UsbConnector, drives: [RoleLink], names: [String: String]? = nil, labelWidth: Int = 24) -> String {
        let device = c.device.map { "\($0.name) — \(UsbSpeeds.label($0.link))" } ?? "volný"
        let here = drives.filter { d in d.link.usb.map { c.has($0.connectorId) } ?? false }.map { d -> String in
            let volume = volumeLabel(d.link.root)
            return d.role == volume ? volume : "\(volume) (\(d.role))"
        }
        let tag = here.isEmpty ? "" : "  ← " + here.joined(separator: ", ")
        return "\(padRight(withKind(c, names), labelWidth)) \(device)\(tag)"
    }

    /// "/Volumes/SSD_A" → "SSD_A"; "/" stays "/".
    static func volumeLabel(_ root: String) -> String { root == "/" ? "/" : (root as NSString).lastPathComponent }
}
