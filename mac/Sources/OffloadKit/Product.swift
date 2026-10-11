import Foundation

/// The product's name (user decisions 2026-10-11: Offload → ArcLoader as Windows 0.10.0, then mmMover): window titles, dialogs, report footers and HTML titles,
/// the ASC MHL creatorinfo, the CLI. Names inside a backup (`offload_report.json`, `.offload-part` …), JSON keys and the
/// report's `tool: "offload"` stay as they are (docs/FORMATY.md) — older versions and Windows read them.
public enum Product {
    public static let name = "mmMover"
    /// The command-line tool (`Contents/Resources/bin/mmmover`) as it is typed.
    public static let cli = "mmmover"

    /// A report's `tool` as people read it: written by this app (or by Offload before the rename, or by Windows) it says
    /// "offload" — shown as mmMover; anything else (another tool's report) as it is. Windows `HtmlReport.ToolName`.
    public static func toolName(_ tool: String) -> String {
        tool.caseInsensitiveCompare("offload") == .orderedSame ? name : tool
    }
}

/// The app's own folder of this Mac user (history, hardware test, user LUTs, tools): `~/Library/Application
/// Support/mmMover`, or MMMOVER_SETTINGS_DIR / OFFLOAD_SETTINGS_DIR (tests, manual runs; the first one set wins) —
/// like `%APPDATA%\ArcLoader` on Windows 0.10.0. Builds before the rename used `…/Offload`: the first start of the app or the CLI
/// copies it (`migrateFromOffload`), never moves, changes or deletes it, so an older build still finds its own files.
/// Tools (`bin/`, `arri/`) are not copied — they are searched in both folders; user LUTs are read from both (new first).
public enum AppSupport {
    public static let folderName = Product.name
    /// The folder of builds before the first rename (tools, user LUTs and the locks stay there).
    public static let oldFolderName = "Offload"
    /// The folder of the ArcLoader builds (D8–D11, 2026-10-11) — renamed to mmMover the same day.
    public static let arcLoaderFolderName = "ArcLoader"
    /// The settings-folder variables, the first one set wins: the newest name first.
    public static let settingsDirVariables = ["MMMOVER_SETTINGS_DIR", "ARCLOADER_SETTINGS_DIR", "OFFLOAD_SETTINGS_DIR"]

    static var applicationSupport: String { NSHomeDirectory() + "/Library/Application Support" }

    /// The first settings-folder variable that is set (not empty), or nil.
    static var settingsDirVariable: String? {
        let env = ProcessInfo.processInfo.environment
        return settingsDirVariables.lazy.compactMap { env[$0] }.first { !$0.isEmpty }
    }

    public static var folder: String { settingsDirVariable ?? applicationSupport + "/" + folderName }

    /// `~/Library/Application Support/Offload` — still read (tools, user LUTs); nil with a settings-folder variable (tests
    /// and manual runs never touch the user's folders).
    public static var oldFolder: String? { settingsDirVariable == nil ? applicationSupport + "/" + oldFolderName : nil }

    /// Where the lock files live: the OLD folder's `locks/` (with a settings-folder variable: that folder's) — the same
    /// files an older Offload takes, so the two never work on one backup at once.
    public static var lockFolder: String { (oldFolder ?? folder) + "/locks" }

    /// `~/Library/Application Support/ArcLoader` (builds D8–D11) — read like the old folder; nil with a variable.
    public static var arcLoaderFolder: String? { settingsDirVariable == nil ? applicationSupport + "/" + arcLoaderFolderName : nil }

    /// The folders searched for something of the app, the new one first (tools, user LUTs).
    public static var searchFolders: [String] { [folder] + [arcLoaderFolder, oldFolder].compactMap { $0 } }

    /// Not copied: tool installs (searched in the old folder too — big, and `bin/art-cmd` links into `arri/`) and the
    /// locks (they stay in the old folder).
    static let notCopied: Set<String> = ["bin", "arri", "locks"]

    /// The rename: at the start of the app and the CLI, before anything reads or writes a file of the app. Only for the
    /// real folder — never with a settings-folder variable. `log` gets one line when something was copied (or failed).
    @discardableResult
    public static func migrateFromOffload(log: ((String) -> Void)?) -> Bool {
        // The newest earlier folder: ArcLoader's (a copy of Offload's plus what D8–D11 wrote), else Offload's.
        guard let old = [arcLoaderFolder, oldFolder].compactMap({ $0 }).first(where: { FileManager.default.fileExists(atPath: $0) })
        else { return false }
        return copyOldFolder(old, to: folder, log: log)
    }

    /// Copies `oldFolder` to `newFolder` when `newFolder` does not exist and `oldFolder` does; otherwise nothing happens.
    /// The old folder is only read — never moved, changed or deleted. The copy is made in a temporary folder next to the
    /// new one and renamed at the end: a copy cut short never looks finished, the next start tries again (and leaves the
    /// partial folder alone). An entry that cannot be copied is left out and named in the log line. Symbolic links are
    /// copied as links. True when the new folder was made here.
    @discardableResult
    public static func copyOldFolder(_ oldFolder: String, to newFolder: String, log: ((String) -> Void)?) -> Bool {
        let fm = FileManager.default
        var isDir: ObjCBool = false
        guard !fm.fileExists(atPath: newFolder), fm.fileExists(atPath: oldFolder, isDirectory: &isDir), isDir.boolValue else { return false }
        let parent = (newFolder as NSString).deletingLastPathComponent
        let temp = parent + "/." + (newFolder as NSString).lastPathComponent + ".copying-" + UUID().uuidString.prefix(8).lowercased()
        var skipped: [String] = []
        do {
            try fm.createDirectory(atPath: temp, withIntermediateDirectories: true)
            for name in try fm.contentsOfDirectory(atPath: oldFolder).sorted() where !notCopied.contains(name) {
                do {
                    try fm.copyItem(atPath: oldFolder + "/" + name, toPath: temp + "/" + name)
                } catch {
                    skipped.append(name)
                }
            }
            // Never over a folder another process made meanwhile (the app and the CLI started at the same moment).
            if renamex_np(temp, newFolder, UInt32(RENAME_EXCL)) != 0 {
                let e = errno
                if e == EEXIST || fm.fileExists(atPath: newFolder) || rename(temp, newFolder) != 0 {
                    try? fm.removeItem(atPath: temp)   // only the copy made here
                    return false
                }
            }
            log?("settings copied from \(oldFolder) to \(newFolder) (the old folder is left as it was; tools and locks stay there)"
                 + (skipped.isEmpty ? "" : "; not copied (could not be read): \(skipped.joined(separator: ", "))"))
            return true
        } catch {
            try? fm.removeItem(atPath: temp)
            log?("could not copy the settings from \(oldFolder) to \(newFolder): \(error.localizedDescription)")
            return false
        }
    }
}
