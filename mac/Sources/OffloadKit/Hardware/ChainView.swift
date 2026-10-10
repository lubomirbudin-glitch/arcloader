import Foundation

/// One link of the chain. `link`: the USB speed label ("USB 3 (5 Gb/s)") or nil for disks without a USB link.
public struct ChainElement: Equatable {
    public let role: String
    public let product: String
    public let connection: String
    public let link: String?
    public let linkMBps: Double?
    public let mbps: Double
    public let controllerId: String?
    public let controllerName: String?

    public var utilization: Double? {
        guard let l = linkMBps, l > 0 else { return nil }
        return mbps / l
    }
}

/// The chain card → reader/link → controller → disks with live throughput, and what is holding it back.
/// In the CLI only the brake shows (status line); the full view is the GUI job card.
public enum ChainView {
    public static func build(_ links: [RoleLink], mbps: [Double], connectors: [UsbConnector]? = nil,
                             portNames: [String: String]? = nil) -> [ChainElement] {
        links.enumerated().map { i, r in
            let u = r.link.usb
            return ChainElement(role: r.role, product: r.link.product, connection: r.link.connection(connectors, portNames),
                                link: u.map { UsbSpeeds.label($0.link) }, linkMBps: linkCeiling(r),
                                mbps: i < mbps.count ? mbps[i] : 0, controllerId: u?.controllerId, controllerName: u?.controllerName)
        }
    }

    /// Realistic MB/s ceiling of the element's USB link; nil without one (no "% of link").
    public static func linkCeiling(_ r: RoleLink) -> Double? {
        guard let u = r.link.usb else { return nil }
        let c = UsbSpeeds.practicalMBps(u.link)
        return c > 0 ? c : nil
    }

    /// "Destination 1 ST1000" or "Card reader — the link is full (USB 2.0 (480 Mb/s))".
    public static func describe(_ brake: Brake, _ chain: [ChainElement]) -> String {
        guard brake.element >= 0, brake.element < chain.count else { return "" }
        let e = chain[brake.element]
        let who = "\(e.role) \(e.product)".trimmingCharacters(in: .whitespaces)
        if brake.linkFull, let link = e.link { return "\(who) — the link is full (\(link))" }
        return who
    }

    /// The start of the bottleneck line in `lines` (the GUI colours that line).
    public static let bottleneckPrefix = "Bottleneck: "

    public static func lines(_ chain: [ChainElement], brake: Brake?) -> [String] {
        var lines = chain.map { e -> String in
            let util = e.utilization.map { "  " + String(format: "%.0f", $0 * 100) + "% of link" } ?? ""
            return "\(e.role) \(e.product) — \(e.connection)  \(String(format: "%.0f", e.mbps)) MB/s\(util)"
        }
        // Shared controller: by identity — two controllers may carry the same name.
        for g in orderedGroups(chain.filter { $0.controllerId != nil }, by: { $0.controllerId!.lowercased() }) where g.count >= 2 {
            lines.append("\(g[0].controllerName ?? "") (shared): \(String(format: "%.0f", g.reduce(0) { $0 + $1.mbps })) MB/s")
        }
        if let b = brake {
            let text = describe(b, chain)
            if !text.isEmpty { lines.append(bottleneckPrefix + text) }
        }
        return lines
    }
}
