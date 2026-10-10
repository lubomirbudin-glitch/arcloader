import Foundation

public enum AdviceKind: Int, Codable, Comparable {
    case warning, info, tip

    public static func < (a: AdviceKind, b: AdviceKind) -> Bool { a.rawValue < b.rawValue }
}

public struct Advice: Codable, Equatable {
    public let kind: AdviceKind
    public let text: String
}

/// A drive in its role for this offload ("Card", "Destination 1" …, or a volume name when listing every drive).
public struct RoleLink: Codable, Equatable {
    public let role: String
    public let link: DriveLink

    public init(role: String, link: DriveLink) {
        self.role = role
        self.link = link
    }

    public var isCard: Bool { RoleLink.isCardRole(role) }

    /// The role of the card ("Card", or "Card 2" with several cards).
    public static let card = "Card"

    /// The role of destination `n` (1-based): "Destination 1".
    public static func destination(_ n: Int) -> String { "Destination \(n)" }

    /// "Card", "Card 2" — and "Karta…" of a hardware test saved before the Mac went English.
    public static func isCardRole(_ role: String) -> Bool { role.hasPrefix(card) || role.hasPrefix("Karta") }
}

/// Groups in order of first appearance (like LINQ GroupBy).
func orderedGroups<T>(_ items: [T], by key: (T) -> String) -> [[T]] {
    var keys: [String] = []
    var groups: [String: [T]] = [:]
    for item in items {
        let k = key(item)
        if groups[k] == nil { keys.append(k) }
        groups[k, default: []].append(item)
    }
    return keys.map { groups[$0]! }
}

public enum Advisor {
    public static func advise(_ drives: [RoleLink], connectors: [UsbConnector], measuredMBps: [String: Double]? = nil,
                              portNames: [String: String]? = nil) -> [Advice] {
        var advice: [Advice] = []
        func who(_ r: RoleLink) -> String { "\(r.role) \(r.link.product)".trimmingCharacters(in: .whitespaces) }
        let freeFastPorts = connectors.filter { $0.userVisible && $0.supportsUsb3 == true && $0.device == nil }
        let freeFast = freeFastPorts.map { PortLabels.withKind($0, portNames) }
        // Bare names: the sentence already says "USB 2.0 only". Ports of unknown kind are in neither list.
        let slowOnly = connectors.filter { $0.userVisible && $0.supportsUsb3 == false }.map { PortLabels.name($0, portNames) }

        for r in drives {
            guard let u = r.link.usb, u.capable > u.link else { continue }
            // Never send the drive to the socket it is already in.
            let elsewhere = freeFastPorts.filter { !$0.has(u.connectorId) }.map { PortLabels.withKind($0, portNames) }
            let whereText = elsewhere.isEmpty ? "" : ": " + elsewhere.joined(separator: ", ")
            let port = PortLabels.of(u, connectors, portNames)
            let text: String
            switch u.connectorUsb3 {
            case false?:
                text = "\(who(r)): supports \(UsbSpeeds.label(u.capable)), but is in a USB 2.0 only port (\(port)) — move it to a USB 3 port\(whereText)."
            case true?:
                text = "\(who(r)): supports \(UsbSpeeds.label(u.capable)), but the link runs at \(UsbSpeeds.label(u.link)) — probably a USB 2.0 cable or a bad contact; try another cable or port."
            case nil:
                text = "\(who(r)): supports \(UsbSpeeds.label(u.capable)), but the link runs at \(UsbSpeeds.label(u.link)) (\(port)) — try another cable or a USB 3 port\(whereText)."
            }
            advice.append(Advice(kind: .warning, text: text))
        }

        // Shared controller: grouped by the controller's identity, not its name.
        let usbDrives = drives.filter { $0.link.usb != nil }
        for group in orderedGroups(usbDrives, by: { $0.link.usb!.controllerId.lowercased() }) where group.count >= 2 {
            let key = group[0].link.usb!.controllerId.lowercased()
            advice.append(Advice(kind: .info,
                                 text: "\(group.map(\.role).joined(separator: ", ")) share one controller (\(group[0].link.usb!.controllerName)) — they split its bandwidth."))
            // Reader and disks on one controller while another controller has a free fast socket: split them.
            if group.contains(where: \.isCard) && group.contains(where: { !$0.isCard }) {
                let other = freeFastPorts.filter { $0.controllerId.lowercased() != key }.map { PortLabels.withKind($0, portNames) }
                if !other.isEmpty {
                    advice.append(Advice(kind: .tip,
                                         text: "Put the reader and the destination drives on different controllers — free fast ports on another controller: \(other.joined(separator: ", "))."))
                }
            }
        }

        // Shared external hub / dock: the roles share its one uplink. Nearest hub first, each set of roles once.
        var hubs: [(hub: UsbHubRef, depth: Int, roles: [String])] = []
        for r in usbDrives {
            for (depth, h) in r.link.usb!.hubs.enumerated() {
                if let i = hubs.firstIndex(where: { $0.hub.instanceId.lowercased() == h.instanceId.lowercased() }) {
                    hubs[i].depth = min(hubs[i].depth, depth)
                    if !hubs[i].roles.contains(r.role) { hubs[i].roles.append(r.role) }
                } else {
                    hubs.append((h, depth, [r.role]))
                }
            }
        }
        var reported = Set<String>()
        let shared = hubs.enumerated().filter { $0.element.roles.count >= 2 }
            .sorted { ($0.element.depth, $0.offset) < ($1.element.depth, $1.offset) }.map(\.element)
        for h in shared {
            let whoText = h.roles.joined(separator: ", ")
            if reported.insert(whoText).inserted {
                advice.append(Advice(kind: .info, text: "\(whoText) share one hub (\(h.hub.name)) — they split its link."))
            }
        }

        if let measured = measuredMBps, !measured.isEmpty {
            let withPace = drives.filter { measured[$0.role] != nil }
            if let slowest = withPace.min(by: { measured[$0.role]! < measured[$1.role]! }) {
                advice.append(Advice(kind: .info,
                                     text: "Estimated offload speed with this setup: \(mb(measured[slowest.role]!)) MB/s (limited by \(who(slowest)))."))
            }
            for r in withPace {
                guard let u = r.link.usb, u.link >= .superSpeed else { continue }
                let ceiling = UsbSpeeds.practicalMBps(u.link)
                if measured[r.role]! < ceiling / 2 {
                    advice.append(Advice(kind: .info,
                                         text: "\(who(r)): the link can do ~\(mb(ceiling)) MB/s, the drive itself gives \(mb(measured[r.role]!)) MB/s — the drive is the limit, not the port."))
                }
            }
        }

        if !freeFast.isEmpty { advice.append(Advice(kind: .tip, text: "Free fast ports: \(freeFast.joined(separator: ", ")).")) }
        if !slowOnly.isEmpty {
            advice.append(Advice(kind: .tip, text: "USB 2.0 only ports — do not use them for offloads (fine for a keyboard or mouse): \(slowOnly.joined(separator: ", "))."))
        }
        return stable(advice)
    }

    /// Warnings first, then info, then tips — keeping the order within each kind.
    static func stable(_ advice: [Advice]) -> [Advice] {
        advice.enumerated().sorted { ($0.element.kind.rawValue, $0.offset) < ($1.element.kind.rawValue, $1.offset) }.map(\.element)
    }

    static func mb(_ v: Double) -> String { String(format: "%.0f", v) }
}
