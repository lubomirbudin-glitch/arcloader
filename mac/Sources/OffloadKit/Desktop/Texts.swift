import Foundation

/// English UI texts with the right plural (1 card, 3 cards) — the meaning of Windows `Desktop/Texts.cs`.
public enum Texts {
    public static func plural(_ n: Int, _ one: String, _ other: String) -> String { n == 1 ? one : other }

    public static func cards(_ n: Int) -> String { n == 0 ? "Select a card" : "\(n) \(plural(n, "card", "cards"))" }

    public static func destinations(_ n: Int) -> String { "\(n) \(plural(n, "destination", "destinations"))" }

    public static func files(_ n: Int, _ bytes: Int64) -> String { "\(n) \(plural(n, "file", "files")) · \(formatBytes(bytes))" }

    public static func clips(_ n: Int) -> String { "\(n) \(plural(n, "clip", "clips"))" }

    /// "Safe to eject" (Windows R5): copied everywhere, the verification may still run or be off.
    public static func safeToEject(_ disks: Int) -> String {
        "All files are on \(disks) \(plural(disks, "drive", "drives")) — the card is safe to eject. Format it only after verification."
    }

    /// "Safe to format" (Windows R5): verified everywhere and the result written.
    public static func safeToFormat(_ disks: Int) -> String {
        "All files are on \(disks) \(plural(disks, "drive", "drives")) and verified — the card is safe to eject and format."
    }

    public static func failed(_ n: Int) -> String {
        "\(n) \(plural(n, "file", "files")) failed — the card is NOT safely backed up. See the report for details."
    }

    public static func warnings(_ n: Int) -> String { n == 0 ? "no warnings" : "\(n) \(plural(n, "warning", "warnings"))" }

    /// A cancelled job card: one orange line instead of "N files failed" and the rate summary.
    public static let cancelled = "Cancelled — the card is not fully backed up."
}
