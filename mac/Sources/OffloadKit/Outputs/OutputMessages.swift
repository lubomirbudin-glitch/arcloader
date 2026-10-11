import Foundation

// Day report and summary report (Windows 0.3.0 / 0.8.0 `Messages.cs`) — the same English texts, paths with "/".
extension Msg {
    public static func dayNotFound(_ p: String) -> String { "Day folder not found: \(p)" }
    public static func notADayFolder(_ p: String) -> String {
        "Folder \(p) has no card with a report — select a day folder on a destination (<destination>/<date>)."
    }
    public static func dayReportFolderIsCard(_ p: String) -> String {
        "Day folder \(p) has a card named \"\(Layout.reportDirName)\" — the day report would be written into it, so it is not created."
    }
    public static func dayIsVolumeRoot(_ p: String) -> String {
        "\(p) is the root of a disk, not a day folder — select a day folder on a destination (<destination>/<date>) or a card in it."
    }
    public static func dayReportBusy(_ p: String) -> String { "Another job is creating the day report in \(p) right now — try again in a moment." }
    public static func dayReportFailed(_ detail: String) -> String { "Could not create the day report: \(detail)" }
    public static func dayReportWritten(_ p: String) -> String { "Day report: \(p)" }

    // Summary report of selected backups (2026-10-09)
    public static func summaryNotACard(_ p: String) -> String {
        "\(p) is not a card folder with a backup (\(Layout.reportDirName)/\(CardRecord.mainReport) is missing) nor a day folder with cards."
    }
    public static func summaryUnknown(_ arg: String) -> String { "\"\(arg)\" is neither a card folder nor a backup id in the history." }
    public static let summaryNothing = "Select at least one backup."
    public static func summaryTargetMissing(_ p: String) -> String { "Folder for the summary report not found: \(p)" }
    public static func summaryTargetIsCard(_ p: String) -> String {
        "\(p) is inside the backup folder of a card (even an unfinished or interrupted one) — the summary report is never written into a backup; select another folder."
    }
    public static func summaryTargetCameraCard(_ p: String) -> String { "\(p) looks like a camera card — \(Product.name) never writes to a card." }
    public static func summaryTargetRemovable(_ p: String) -> String {
        "\(p) is on removable media (a card in a reader, a flash drive) — the summary report is not saved there; select a disk."
    }
    public static func summaryTargetReadOnly(_ p: String) -> String { "\(p) is on a read-only disk." }
    public static func summaryTargetIsSource(_ p: String) -> String { "\(p) is on the card — \(Product.name) never writes to a card." }
    public static func summaryWritten(_ p: String) -> String { "Summary report: \(p)" }
    public static func summaryExtraWritten(_ p: String) -> String { "Summary report also on the selected disk: \(p)" }
    public static func summaryExtraFailed(_ detail: String) -> String {
        "The summary report was not written to the selected disk: \(detail) — it is only on this computer."
    }
    public static func summaryFailed(_ detail: String) -> String { "Could not create the summary report: \(detail)" }
}
