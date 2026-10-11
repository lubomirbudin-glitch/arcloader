import Foundation

/// A copy of every card's report on this Mac — `~/Documents/ArcLoader reports/<date>/<card>/` (`_REPORT` and `ascmhl`).
/// The drives with the footage leave the set and the report must not leave with them (2026-10-09: the reports of
/// drives 1 and 2 were gone). Refreshed after every report write; the drives are only read; a failure is only logged
/// and never fails the job. Off inside XCTest so tests never write to the user's Documents.
public enum LocalReports {
    /// The folder in Documents (Windows 0.10.0: `Dokumenty\ArcLoader reports`).
    public static let folderName = Product.name + " reports"
    /// The folder's names of earlier builds: "Offload reporty" (the Mac before the rename and Windows before 0.9.0) and
    /// "Offload reports" (Windows 0.9.x). Copies there are still read (the summary report's fallback), never written.
    public static let oldFolderNames = ["Offload reporty", "Offload reports"]

    /// ARCLOADER_REPORTS_DIR or OFFLOAD_REPORTS_DIR (the first one set) moves it — manual CLI checks against temporary
    /// folders, like the settings-folder variables.
    public static var root = ["ARCLOADER_REPORTS_DIR", "OFFLOAD_REPORTS_DIR"].lazy
        .compactMap { ProcessInfo.processInfo.environment[$0] }.first { !$0.isEmpty }
        ?? (NSHomeDirectory() as NSString).appendingPathComponent("Documents/" + folderName)
    public static var enabled = NSClassFromString("XCTestCase") == nil

    /// Copies `_REPORT` and `ascmhl` of the first destination that has them. Returns the local folder, or nil.
    @discardableResult
    public static func sync(date: String, cardName: String, cardPaths: [String], log: ((String) -> Void)? = nil) -> String? {
        guard enabled else { return nil }
        let fm = FileManager.default
        guard let src = cardPaths.first(where: { fm.fileExists(atPath: $0 + "/" + Layout.reportDirName) }) else { return nil }
        let dst = root + "/" + date + "/" + cardName
        do {
            for sub in [Layout.reportDirName, "ascmhl"] {
                let from = src + "/" + sub
                guard fm.fileExists(atPath: from) else { continue }
                let to = dst + "/" + sub
                try fm.createDirectory(atPath: to, withIntermediateDirectories: true)
                for name in try fm.contentsOfDirectory(atPath: from) where !name.hasPrefix(".") {
                    let target = to + "/" + name
                    try? fm.removeItem(atPath: target)   // only the local copy is ever replaced
                    try fm.copyItem(atPath: from + "/" + name, toPath: target)
                }
            }
            return dst
        } catch {
            log?("local copy of the report: \(error)")
            fputs("Local copy of the report failed: \(error)\n", stderr)
            return nil
        }
    }
}
