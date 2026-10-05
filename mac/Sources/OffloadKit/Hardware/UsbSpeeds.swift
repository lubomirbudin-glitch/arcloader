import Foundation

public enum UsbLinkSpeed: Int, Codable, Comparable {
    case unknown, low, full, high, superSpeed, superPlus

    public static func < (a: UsbLinkSpeed, b: UsbLinkSpeed) -> Bool { a.rawValue < b.rawValue }
}

public enum UsbSpeeds {
    /// The speed the link really runs at, from an IOUSBHostDevice: "UsbLinkSpeed" (bit/s) when the system reports it,
    /// else "Device Speed" (kUSBDevicePropertySpeed: 0 low, 1 full, 2 high, 3 super, 4 SuperSpeedPlus, 5 SuperSpeedPlus by 2).
    public static func link(linkBitsPerSecond: UInt64?, deviceSpeed: Int?) -> UsbLinkSpeed {
        if let bps = linkBitsPerSecond, bps > 0 {
            if bps >= 10_000_000_000 { return .superPlus }
            if bps >= 5_000_000_000 { return .superSpeed }
            if bps >= 480_000_000 { return .high }
            if bps >= 12_000_000 { return .full }
            return .low
        }
        switch deviceSpeed {
        case 0?: return .low
        case 1?: return .full
        case 2?: return .high
        case 3?: return .superSpeed
        case 4?, 5?: return .superPlus
        default: return .unknown
        }
    }

    /// What the device could do on a good port and cable — never less than what it runs at. macOS does not expose the
    /// SuperSpeedPlus capability without reading the BOS descriptor, so bcdUSB ≥ 3.0 means "USB 3 (5 Gb/s)".
    public static func capability(bcdUSB: Int?, link: UsbLinkSpeed) -> UsbLinkSpeed {
        guard let bcd = bcdUSB else { return link }
        let cap: UsbLinkSpeed = bcd >= 0x0300 ? .superSpeed : bcd >= 0x0200 ? .high : .full
        return max(cap, link)
    }

    public static func label(_ s: UsbLinkSpeed) -> String {
        switch s {
        case .low: return "USB 1.0 (1,5 Mb/s)"
        case .full: return "USB 1.1 (12 Mb/s)"
        case .high: return "USB 2.0 (480 Mb/s)"
        case .superSpeed: return "USB 3 (5 Gb/s)"
        case .superPlus: return "USB 3 (10 Gb/s a víc)"
        case .unknown: return "neznámá"
        }
    }

    /// Realistic ceiling for a disk or card reader over this link, MB/s (protocol overhead included).
    public static func practicalMBps(_ s: UsbLinkSpeed) -> Double {
        switch s {
        case .low: return 0.15
        case .full: return 1
        case .high: return 40
        case .superSpeed: return 450
        case .superPlus: return 1000
        case .unknown: return 0
        }
    }
}
