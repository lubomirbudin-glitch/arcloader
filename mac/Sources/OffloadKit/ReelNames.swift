import Foundation

/// The reel ("A001") in a camera's clip name — from the name only, clips are never read before the copy (R10).
/// Port of Windows `ReelNames`.
public enum ReelNames {
    /// Blackmagic, RED, then ARRI / Sony Venice / Canon Cinema EOS — in this order; any case.
    static let patterns: [NSRegularExpression] = [
        #"^([A-Z])(\d{3})_\d{8}_C(\d{3})"#,
        #"^([A-Z])(\d{3})_C(\d{3})"#,
        #"^([A-Z])(\d{3})C(\d{3,4})"#,
    ].map { try! NSRegularExpression(pattern: $0, options: [.caseInsensitive]) }

    /// The reel in upper case and the part of the name that gave it ("A001C003"), or nil (Sony XAVC "C0001", Panasonic,
    /// DJI, phones …). `name`: the clip name without extension.
    public static func match(_ name: String) -> (reel: String, prefix: String)? {
        let range = NSRange(name.startIndex..., in: name)
        for re in patterns {
            guard let m = re.firstMatch(in: name, range: range),
                  let cam = Range(m.range(at: 1), in: name), let reel = Range(m.range(at: 2), in: name),
                  let whole = Range(m.range, in: name) else { continue }
            return ((String(name[cam]) + String(name[reel])).uppercased(), String(name[whole]))
        }
        return nil
    }

    public static func fromClipName(_ name: String) -> String? { match(name)?.reel }
}

/// The reel of a clip (Windows spec 3.4) — what the proxy's timecode track and the report (`clips[].info.reel`) call it,
/// like DIT tools do: (1) the camera's own tag (ffprobe `reel_name`, `tape_name`, `reel`, in `ClipInfo.reel`), (2) the reel
/// in the clip name (`ReelNames`: "A001C003…" → "A001"), (3) the card folder's name. Port of Windows `ReelResolver`.
public enum ReelResolver {
    public static func resolve(_ clip: Clip, info: ClipInfo?, cardName: String) -> String {
        if let tag = info?.reel?.trimmingCharacters(in: .whitespacesAndNewlines), !tag.isEmpty { return tag }
        return ReelNames.fromClipName(clip.name) ?? cardName
    }
}

/// A card folder name proposed instead of the volume label. `hint` is the grey line under the name field.
public struct NameSuggestion: Equatable {
    public let name: String
    public let hint: String

    public init(name: String, hint: String) {
        self.name = name
        self.hint = hint
    }
}

public enum ReelSuggestion {
    static func isReelFolder(_ s: String) -> Bool { s.range(of: #"^[A-Z]\d{3}\z"#, options: .regularExpression) != nil }

    /// One reel among all recognised clips (camera proxies left out; an ARRIRAW sequence by its folder name, which is its
    /// clip name) → that reel. Several reels → no suggestion, but `note` says so. `reels`: every reel found, sorted.
    public static func fromClips(_ clips: [Clip]) -> (suggestion: NameSuggestion?, note: String?, reels: [String]) {
        let named = clips.filter { !$0.isCameraProxy }.compactMap { ReelNames.match($0.name) }
        var reels: [String] = []
        for m in named where !reels.contains(m.reel) { reels.append(m.reel) }
        reels.sort { $0.utf16.lexicographicallyPrecedes($1.utf16) }
        switch reels.count {
        case 0: return (nil, nil, reels)
        case 1: return (NameSuggestion(name: reels[0], hint: "from the clip names (\(named[0].prefix)…)"), nil, reels)
        default: return (nil, "The card has more than one reel: \(reels.joined(separator: ", "))", reels)
        }
    }

    /// "Next in line": history entries of the same date folder and project (nil = "No project"; a name in any case)
    /// named like "A001"; exactly one camera letter → the next number. Several letters → nil (which camera is this card?).
    /// `taken`: names typed or suggested for other cards of this start — the number goes past them. "A999" → nil.
    public static func next(_ history: [HistoryEntry], date: String, project: String?, today: String, taken: [String] = []) -> NameSuggestion? {
        let names = history.filter { e in
            e.date == date && sameProject(e.project, project) && isReelFolder(e.cardName)
        }.map(\.cardName)
        guard let first = names.first, Set(names.map(\.first)).count == 1, let letter = first.first else { return nil }
        let others = taken.map { $0.uppercased() }.filter { isReelFolder($0) && $0.first == letter }
        let max = (names + others).compactMap { Int($0.dropFirst()) }.max() ?? 0
        guard max < 999 else { return nil }
        let whenText = date == today ? "today" : date
        return NameSuggestion(name: String(letter) + String(format: "%03d", max + 1),
                              hint: "next after \(letter)\(String(format: "%03d", max)) (\(whenText))")
    }

    static func sameProject(_ a: String?, _ b: String?) -> Bool {
        switch (a, b) {
        case (nil, nil): return true
        case let (x?, y?): return x.caseInsensitiveCompare(y) == .orderedSame
        default: return false
        }
    }
}
