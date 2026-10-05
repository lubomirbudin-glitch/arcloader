import Foundation

/// Czech texts with the right plural (1 karta, 3 karty, 5 karet) — port of Windows `Desktop/Texts.cs`.
public enum Texts {
    public static func plural(_ n: Int, _ one: String, _ few: String, _ many: String) -> String {
        n == 1 ? one : (2...4).contains(n) ? few : many
    }

    public static func cards(_ n: Int) -> String { n == 0 ? "Vyberte kartu" : "\(n) \(plural(n, "karta", "karty", "karet"))" }

    public static func destinations(_ n: Int) -> String { "\(n) \(plural(n, "cíl", "cíle", "cílů"))" }

    public static func files(_ n: Int, _ bytes: Int64) -> String { "\(n) \(plural(n, "soubor", "soubory", "souborů")) · \(formatBytes(bytes))" }

    public static func clips(_ n: Int) -> String { "\(n) \(plural(n, "klip", "klipy", "klipů"))" }

    public static func safeToEject(_ disks: Int) -> String {
        "Všechny soubory jsou na \(disks) \(plural(disks, "disku", "discích", "discích")) a ověřené — kartu lze vysunout."
    }

    public static func failed(_ n: Int) -> String {
        "\(n) \(plural(n, "soubor selhal", "soubory selhaly", "souborů selhalo")) — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu."
    }

    public static func warnings(_ n: Int) -> String { n == 0 ? "bez varování" : "\(n) varování" }

    /// A cancelled job card: one orange line instead of "N souborů selhalo" and the rate summary.
    public static let cancelled = "Zrušeno — karta není kompletně zálohovaná."
}
