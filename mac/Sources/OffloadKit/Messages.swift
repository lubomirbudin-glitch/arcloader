import Foundation

/// Which check stopped a job. Tests and the GUI tell the cases apart by the code, never by the text.
public enum PreflightCode: String {
    case other, chunkSize, bufferCount, date, sourceNotFound, noDestination, duplicateDestination, sourceIsOffload
    case sourceUnreadable, destinationNotFound, destinationNotWritable, destinationUnreadable, sameVolumeAsSource
    case sharedVolume, emptySource, noSpace, noHashRecord, noFilesToRead
    case appendUnavailable, appendNoReport, appendNoMhl, appendDiverged, appendConflict, appendMissingFile, appendFailedFile
    case appendOtherCard, appendMhlDamaged, appendBusy
    /// Not an error: the backup already has everything (the job ends "Done").
    case nothingNew
    /// Not an error: a repair found every file good on every destination.
    case repairNothing
    case repairConflict
}

/// English count with the right plural: "1 file", "2 files".
func plural(_ n: Int, _ one: String, _ other: String) -> String { Texts.plural(n, one, other) }

/// Every message a user sees from checks and validation — English (the Mac app is English only, Windows stays Czech:
/// docs/ROZDILY.md), the same meaning as Windows `Messages.cs`, paths with "/". Terms: mac/docs/en-glossary.md.
public enum Msg {
    public static func chunkSize(_ n: Int) -> String { "Block size must be a multiple of 4096 B and at most 1 GiB (got \(n))." }
    public static func bufferCount(_ n: Int) -> String { "Buffer count must be at least 2 (got \(n))." }
    public static func date(_ d: String) -> String { "Date must be YYYY-MM-DD (got “\(d)”)." }
    public static func sourceNotFound(_ p: String) -> String { "Source not found: \(p)" }
    public static let noDestination = "No destination selected."
    public static func duplicateDestination(_ r: String) -> String { "Destination added twice: \(r)" }
    public static let sourceIsOffload =
        "The source looks like an earlier offload (it has _REPORT, _PROXY or ascmhl) — copy the original card, or remove those folders."
    public static func sourceUnreadable(_ p: String) -> String { "Cannot read the source: \(p)" }
    public static func destinationNotFound(_ r: String) -> String { "Destination not found: \(r) — is the drive connected?" }
    public static func destinationNotWritable(_ r: String) -> String { "Cannot write to the destination: \(r)" }
    public static func destinationUnreadable(_ r: String) -> String { "Cannot read the destination: \(r)" }
    public static func sameVolumeAsSource(_ r: String) -> String { "Destination \(r) is on the same drive as the source." }
    public static func sharedVolume(_ r: String) -> String {
        "Two destinations are on the same drive (\(r)) — that is not a second copy. Allow it with --allow-same-device."
    }
    public static let emptySource = "There are no files on the source."
    public static func reportNotWritten(_ d: String) -> String { "Could not write the report: \(d)" }
    public static func noHashRecord(_ dir: String) -> String { "\(dir) has no ASC MHL and no checksum list — nothing to compare against." }
    public static func noFilesToRead(_ p: String) -> String { "No files to read in \(p)." }
    public static let cardVolumeUnreadable = "Cannot read the card."
    public static let cardInspectFailed = "Could not check the card."
    public static let cardSwapped = "The card was swapped while it was being read."
    public static let cardNameAutoNone = "No name suggestion found — using the volume label."
    public static func cardNameAuto(_ n: String) -> String { "Card name: \(n) (from the clips)" }
    public static func cardUnreadable(_ d: String) -> String { "Cannot read the card: \(d)" }
    /// Where a backup is checked (the new look: the window toolbar; before it: the Tools menu).
    public static let verifyHint = "Verify backup (window toolbar; on the command line: \(Product.cli) verify)"

    // Space (task 48 fills the parts)
    public static func noSpace(_ whereTo: String, total: Int64, parts: String, free: Int64) -> String {
        "Not enough space on \(whereTo): needs \(formatBytesShort(total)) (\(parts)), free \(formatBytesShort(free))."
    }
    public static func spaceLine(_ whereTo: String, total: Int64, parts: String, free: Int64) -> String {
        "Space: \(whereTo) needs \(formatBytesShort(total)) (\(parts)), free \(formatBytesShort(free))"
    }
    public static func lowSpace(_ whereTo: String, left: Int64) -> String {
        "Only \(formatBytesShort(left)) will be left on \(whereTo) after the offload — under 5% headroom, the proxy estimate is approximate."
    }
    public static func proxiesSkippedForSpace(_ whereTo: String, need: Int64, free: Int64) -> String {
        "Proxies were not rendered: not enough space on \(whereTo) (needs ~\(formatBytesShort(need)), free \(formatBytesShort(free)))."
    }

    // Append
    public static func appendUnavailable(_ p: String) -> String {
        "The earlier backup is not available: \(p) — connect the drive, or make a full copy."
    }
    public static func appendNoReport(_ p: String) -> String {
        "\(p) has no report of the earlier backup (_REPORT/offload_report.json) — cannot append."
    }
    public static func appendBadReport(_ p: String, _ d: String) -> String {
        "Cannot read the report of the earlier backup in \(p) (\(d)) — cannot append."
    }
    public static func appendNoMhl(_ p: String) -> String { "\(p) has no ASC MHL (ascmhl/ascmhl_chain.xml) — cannot append." }
    public static func appendDiverged(_ details: String) -> String {
        "The backups on the destinations differ (\(details)) — check them with \(verifyHint); cannot append."
    }
    public static func appendReportsMismatch(_ p: String, report: Int, mhl: Int) -> String {
        "The reports in \(p) do not match the ASC MHL (the last report is generation \(report), the last MHL generation \(mhl)) — "
            + "an earlier append did not finish. Check the backup with \(verifyHint) and fix the folder by hand; cannot append."
    }
    public static func appendNotACardFolder(_ p: String) -> String {
        "\(p) is not a card folder of a backup (<destination>/<date>/<card>) — cannot append."
    }
    public static func appendDifferentFolders(_ paths: [String]) -> String {
        "The folders of the earlier backup have different names (\(paths.joined(separator: ", "))) — "
            + "can append only into folders with the same date and card name."
    }
    public static func appendConflict(_ lines: [String], fullCopyFolder: String) -> String {
        "Append stopped — nothing was copied. \(lines.count) \(plural(lines.count, "file does not match", "files do not match")) "
            + "the backup:\n  " + lines.prefix(10).joined(separator: "\n  ") + (lines.count > 10 ? "\n  …" : "")
            + "\nMake a full copy (it goes to \(fullCopyFolder))."
    }
    public static func appendMissingFile(_ disk: String, _ path: String) -> String {
        "A file of the earlier backup is missing on \(disk): \(path) — check the backup with \(verifyHint)"
    }
    public static func appendFailedFile(_ path: String, _ disk: String) -> String {
        "\(path) was not copied correctly to \(disk) in the earlier backup — cannot append, make a full copy."
    }
    public static let appendOtherCard = "None of the files of the earlier backup is on the card — is it a different card? Make a full copy."
    public static func appendMhlDamaged(_ p: String) -> String {
        "The ASC MHL history in \(p) is damaged — check it with \(verifyHint); cannot append."
    }
    public static func appendUnknownFile(_ disk: String, _ path: String) -> String {
        "\(disk) has a file \(path) that no report knows (an unfinished append?)"
    }
    public static func appendPartNameTaken(_ path: String, _ part: String) -> String {
        "\(path) (the temporary copy name \(part) is another file of the backup or the card)"
    }
    public static func hashListNotHealed(_ p: String, _ e: String) -> String {
        "Cannot add the missing lines to the checksum list (.xxh64) in \(p): \(e)"
    }
    public static func appendBusy(_ p: String) -> String {
        "Another job is working with this backup right now — an append, a repair or a verify with proxies (\(p)) — wait until it finishes."
    }
    public static let nothingNew = "Nothing new — the backup is complete."
    public static func unverifiedEarlier(_ n: Int) -> String {
        "\(n) \(plural(n, "file", "files")) of the earlier backup \(plural(n, "was", "were")) not verified — Verify backup recommended."
    }
    public static let cardNotInHistory = "The card is not in the history — give the backup folders with --into."
    public static let historyDelayed =
        "The card history is busy right now — the record of this backup will be written as soon as possible (do not close the app yet)."
    public static let historyNotWritten =
        "Could not write the record of this backup to the card history (the history was busy for too long) — the backup on the disks is fine, but the card will not be recognised the next time it is inserted."

    // Repair (file -3, task 62)
    public static let repairNothing = "Nothing to repair — every file is good on every destination."
    public static func repairConflict(_ lines: [String]) -> String {
        "Repair stopped — nothing was copied. \(lines.count) \(plural(lines.count, "file cannot be repaired", "files cannot be repaired")):\n  "
            + lines.prefix(10).joined(separator: "\n  ") + (lines.count > 10 ? "\n  …" : "")
            + "\nThe card has changed since the backup, or a file is missing in the backup folder — check it with Verify backup, or make a full copy."
    }
    public static func repairNotOnCard(_ p: String) -> String { "\(p): no longer on the card" }
    public static func repairCardDiffers(_ p: String, _ algorithm: String, now: String, recorded: String) -> String {
        "\(p): the card has different content than at the backup (\(algorithm) \(now), in the backup \(recorded))"
    }
    public static func repairCardUnreadable(_ p: String, _ d: String) -> String { "\(p): cannot read it on the card (\(d))" }
    public static let repairNeedsVerify = "A repair is always verified by reading back — it cannot run without verification."
    public static func mhlRepairComment(copied: Int, readBack: Int, source: String) -> String {
        "\(Product.name) repair: \(copied) \(plural(copied, "file", "files")) again from \(source)"
            + (readBack > 0 ? "; \(readBack) \(plural(readBack, "file", "files")) only read back" : "")
    }
    public static let repairOtherCard =
        "The card in the reader is not the one the backup was made from (another card, or none) — nothing was copied. "
            + "Insert the original card and run Copy again."
    public static func cardFilesNotInBackup(_ n: Int) -> String {
        "The card has \(n) \(plural(n, "file", "files")) that the backup does not — do not eject the card, append to the backup."
    }
    public static func backupStillFailed(_ n: Int) -> String {
        "\(n) \(plural(n, "file", "files")) in the backup still \(plural(n, "does", "do")) not match — do not eject the card, run Copy again."
    }
    public static func backupStateUnreadable(_ d: String) -> String {
        "Cannot read the state of the backup (\(d)) — nothing was checked, do not eject the card yet."
    }
    public static func forRepair(_ appendRefusal: String) -> String {
        appendRefusal.replacingOccurrences(of: "cannot append", with: "cannot repair")
            .replacingOccurrences(of: "can append only", with: "can repair only")
    }
    public static let cardChangedWhilePaused =
        "The card in the reader was swapped or removed during the pause — the remaining files were not copied. "
            + "Insert the original card and append to the backup."

    // CLI
    public static let cliError = "Error: "
    public static let cliWarning = "Warning: "
    public static func unknownProxyCodec(_ x: String?) -> String {
        "Unknown proxy codec “\(x ?? "")” — use h264, hevc, proresProxy, proresLT, prores422 or prores422HQ."
    }
    public static func lutNotFound(_ p: String) -> String { "LUT not found: \(p)" }
    public static func unknownHash(_ x: String?) -> String { "Unknown checksum “\(x ?? "")” — use xxh64 or both (xxh64 + MD5)." }
    public static func proxyFolderNotFound(_ p: String) -> String { "Folder for proxies not found: \(p)" }
    public static let proxyFolderNoEffect = "Note: --proxy-folder does nothing without --proxy."
    public static let cancelling = "Cancelling — finishing the current block, partly copied files will be deleted…"
    public static func notPositiveNumber(_ s: String?) -> String { "“\(s ?? "")” is not a positive number." }
    public static func unknownFallbackLut(_ x: String, ids: String) -> String { "Unknown fallback LUT “\(x)”. Built-in: \(ids)" }
    public static func unknownLut(_ x: String, ids: String) -> String {
        "Unknown LUT “\(x)”. Use auto, none, a path to a .cube or one of: \(ids)"
    }
    public static let noBuiltinLuts = "Built-in LUTs not found (the luts folder is missing)."

    // Clip notes
    public static let noVerifiedCopy = "no verified copy"
    public static func proxyRenamed(_ name: String) -> String { "Proxy renamed to \(name) (name clash) — relink it by hand." }

    /// LighterSource (task 54, Windows 0.5.1): the note of a clip without a proxy because its original is lighter — neutral,
    /// not a failure. Same words as Windows.
    public static let proxyLighterPrefix = "no proxy — the original is lighter"
    /// The same note as Windows wrote it before 0.9.0 (in Czech): such reports still say it and must still count.
    public static let proxyLighterPrefixOld = "bez proxy — origin\u{e1}l je leh\u{10d}\u{ed}"

    public static func proxyLighter(_ sourceMbps: Double, _ targetMbps: Double) -> String {
        "\(proxyLighterPrefix) (\(mbps(sourceMbps)) Mb/s ≤ \(mbps(targetMbps)) Mb/s)"
    }

    /// One decimal at most, no trailing zero, a decimal point ("5.2", "10") — .NET "0.#" with the invariant culture.
    static func mbps(_ v: Double) -> String {
        let s = String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), v)
        return s.hasSuffix(".0") ? String(s.dropLast(2)) : s
    }
    public static let ffmpegNotFound = "ffmpeg not found"
    public static let ffprobeNotFound = "ffprobe not found"
    public static let unknownFormat = "unknown format"
    public static func noDecoder(_ f: String) -> String { "\(f): no decoder available" }
}
