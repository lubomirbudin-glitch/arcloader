import Foundation

/// The one place that names a USB socket — drive lines, the port list, advice and the copy chain all go through here,
/// so one socket always reads the same. User names (`UsbConnector.id` → name) replace the default label.
public enum PortLabels {
    /// The socket's name: the user's name (under its id or one of its alt ids), else the default unique label.
    public static func name(_ c: UsbConnector, _ names: [String: String]? = nil) -> String {
        if let names {
            for id in [c.id] + c.altIds {
                if let n = names[id]?.trimmingCharacters(in: .whitespaces), !n.isEmpty { return n }
            }
        }
        return c.label
    }

    /// Name plus what the socket can do: "port 6 USB 3", "port 3 USB 2.0 only", "port 4" (unknown).
    public static func withKind(_ c: UsbConnector, _ names: [String: String]? = nil) -> String {
        c.kind.isEmpty ? name(c, names) : "\(name(c, names)) \(c.kind)"
    }

    public static func find(_ connectors: [UsbConnector]?, _ connectorId: String) -> UsbConnector? {
        connectors?.first { $0.has(connectorId) }
    }

    /// "Intel(R) USB 3.20 eXtensible Host Controller - 1.20 (Microsoft)" → "Intel USB 3.20 xHCI"; Mac names pass unchanged.
    public static func shortController(_ name: String) -> String {
        var s = name.replacingOccurrences(of: #"\((R|TM|C)\)|®|™"#, with: "", options: [.regularExpression, .caseInsensitive])
        s = s.replacingOccurrences(of: #"\s+-\s+[\d.]+(\s*\([^)]*\))?\s*$|\s*\((Microsoft|AMD|Intel)\)\s*$"#, with: "",
                                   options: .regularExpression)
        for (long, short) in [("eXtensible Host Controller", "xHCI"), ("Enhanced Host Controller", "EHCI")] {
            s = s.replacingOccurrences(of: long, with: short, options: .caseInsensitive)
        }
        s = s.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression).trimmingCharacters(in: .whitespaces)
        return s.isEmpty ? name : s
    }

    static func padRight(_ s: String, _ width: Int) -> String {
        s.count >= width ? s : s + String(repeating: " ", count: width - s.count)
    }
}
