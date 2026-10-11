import Foundation

/// Plan 10 (verify-in-proxy): the texts of the report, the job card and the log — English, word for word as Windows
/// `Msg.VerifyInProxy.cs` (0.9.0), "macOS" where Windows names its own cache.
public enum VIPMsg {
    // Why a destination is verified the classic way (report `destinations[].verifyNote`).
    public static let classicNetwork = "network destination — verified by reading back"
    public static let classicCacheCheck = "the check of reads past the cache did not pass — verified by reading back"
    public static func classicCacheCheckFailed(_ detail: String) -> String {
        "cannot check reads past the cache (\(detail)) — verified by reading back"
    }
    public static let classicSetting = "verification during proxies is off — verified by reading back"
    public static let classicSecondary = "another destination — verified by reading in the background"
    /// Mac only: `mincore` does not see the cache on this Mac, so no read can be shown to come from the disk.
    public static let classicBlind = "macOS cannot show what is in its cache — verified by reading back"

    /// Mac counterpart of Windows ruling I3: a file stayed in the cache and could not be evicted before it was read.
    public static func verifiedThroughCache(_ disk: String, _ files: Int) -> String {
        "Verified by reading through the macOS cache: \(disk), \(plural(files, "1 file", "\(files) files")) — the file stayed in memory and could not be evicted, so the data may have come from memory instead of the disk."
    }

    // Spec 10 notes (report `verifyNotes`, the job's warning).
    public static func unstableRead(_ path: String, _ disk: String, _ blocks: [Int]) -> String {
        "Unstable read: \(path) on \(disk) (block \(blocks.map(String.init).joined(separator: ", "))) gave different data in one read — the next read matched the card."
    }
    public static func sameBlockWrong(_ path: String, _ block: Int, _ disks: String, _ card: String) -> String {
        "\(path): block \(block) mismatches the same way on \(disks) — \(card)"
    }
    public static func sameBlockWrongMore(_ path: String, _ blocks: Int) -> String {
        "\(path): \(blocks == 1 ? "1 more block mismatches" : "\(blocks) more blocks mismatch") the same way on several destinations (the card was not read again for \(blocks == 1 ? "it" : "them"))."
    }
    public static let cardNotReadAgain = "could not read the card again (no longer in the reader?) — insert it and start Copy again."
    public static let cardGivesRecorded = "the card now gives the recorded data: the defect arose while writing to the disks."
    public static let cardGivesWhatDisksHave = "the card now gives the same data as the disks: the card was probably misread during the copy."
    public static let cardGivesOther = "the card now gives data different from the copy and from the disks: the card or the reader is probably faulty."

    /// The job's warning when the verified generation could not be written into a card folder (it stays uncommitted:
    /// "Finish interrupted backup", an append, finishes it).
    public static func verifyNotCommitted(_ detail: String) -> String {
        "Could not write the verification result (\(detail)) — do not format the card yet; \"Finish interrupted backup\" (Append, \(Product.cli) append) finishes the backup."
    }

    /// A destination's disk stopped answering while it was verified (its unread files fail, the job finishes).
    public static let verifyDiskHung = "the disk stopped responding during verification"

    /// CLI --primary 0 or less.
    public static func primaryNotPositive(_ n: Int) -> String { "--primary \(n): destinations are numbered from 1 (the order of --dest)." }

    /// CLI --primary above the number of destinations.
    public static func primaryTooHigh(_ n: Int, _ count: Int) -> String {
        "--primary \(n): there \(count == 1 ? "is" : "are") only \(plural(count, "1 destination", "\(count) destinations")) (numbered from 1 in the order of --dest)."
    }

    // R6 (resume).
    /// A resume keeps the proxy the interrupted run finished.
    public static let proxyKeptOnResume = "proxy from the interrupted run — not rendered again (the decoder did not check the clip's content this time)"
    public static func resumeDiverged(_ cardPath: String, _ generation: Int) -> String {
        "\(cardPath): the unfinished backup (generation \(generation)) does not match the other destinations — check it by hand."
    }
    public static let repairUnfinished = "The backup was not finished (the app ended during the copy or the verification) — finish it first with \"Finish interrupted backup\" on the card (\(Product.cli) append)."

    /// A card folder gone before the verification results were written into it (an unplugged disk) — never made anew.
    public static let cardFolderGone = "the card folder on the destination disappeared (disk disconnected?)"

    /// Spec 7: "2 clips have a content defect — check them before formatting (the copy matches the card)."
    public static func contentWarnings(_ n: Int) -> String {
        "\(n == 1 ? "1 clip has" : "\(n) clips have") a content defect — check \(n == 1 ? "it" : "them") before formatting (the copy matches the card)."
    }
}
