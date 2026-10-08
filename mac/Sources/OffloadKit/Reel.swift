import Foundation

/// The reel of a clip (Windows `ReelResolver`, spec 3.4) — what the proxy's timecode track (`reel_name`) and the report
/// (`clips[].info.reel`) call it, like DIT tools do: (1) the camera's own tag (ffprobe `reel_name`, `tape_name`, `reel`,
/// in ClipInfo.reel after the probe), (2) the reel in the clip name ("A001C003…" → "A001"), (3) the card folder's name.
/// Merge note: stream A's `ReelNames` (card name from clips) has the same clip-name patterns — keep one of the two.
public enum ReelResolver {
    public static func resolve(_ clip: Clip, info: ClipInfo?, cardName: String) -> String {
        if let tag = info?.reel?.trimmingCharacters(in: .whitespacesAndNewlines), !tag.isEmpty { return tag }
        return fromClipName(clip.name) ?? cardName
    }

    /// Blackmagic (A001_08151202_C002), RED (A001_C002_…), ARRI / Sony Venice / Canon (A001C003…) — Windows `ReelNames`.
    private static let patterns: [NSRegularExpression] = [
        #"^([A-Z])(\d{3})_\d{8}_C\d{3}"#, #"^([A-Z])(\d{3})_C\d{3}"#, #"^([A-Z])(\d{3})C\d{3,4}"#,
    ].map { try! NSRegularExpression(pattern: $0, options: [.caseInsensitive]) }

    /// The reel in a camera's clip name, upper case ("A001"), or nil (Sony XAVC "C0001", Panasonic, DJI, phones …).
    public static func fromClipName(_ name: String) -> String? {
        let ns = name as NSString
        for r in patterns {
            if let m = r.firstMatch(in: name, range: NSRange(location: 0, length: ns.length)) {
                return (ns.substring(with: m.range(at: 1)) + ns.substring(with: m.range(at: 2))).uppercased()
            }
        }
        return nil
    }
}

extension FFProbe {
    /// The camera's reel tag: MOV keeps it on the timecode track (tmcd "reel_name"), MXF on every stream; other writers
    /// use tape_name or reel. The format first, then the streams in order; blank values do not count.
    static func reel(format: [String: Any], streams: [[String: Any]]) -> String? {
        for tags in ([format] + streams).map({ $0["tags"] as? [String: Any] ?? [:] }) {
            for name in ["reel_name", "tape_name", "reel"] {
                for (k, v) in tags where k.caseInsensitiveCompare(name) == .orderedSame {
                    if let s = (v as? String)?.trimmingCharacters(in: .whitespacesAndNewlines), !s.isEmpty { return s }
                }
            }
        }
        return nil
    }
}
