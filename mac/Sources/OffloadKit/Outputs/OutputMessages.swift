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
}
