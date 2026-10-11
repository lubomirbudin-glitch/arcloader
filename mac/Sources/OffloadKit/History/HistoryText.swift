import Foundation

public enum HistoryTone: Equatable { case neutral, ok, warning, error }

/// The history line under a card and in the CLI — the English texts of Windows `HistoryText`.
public enum HistoryText {
    public static let inspecting = "Checking whether the card is already backed up…"

    /// "2026-10-06 08:15" in local time.
    public static func when(_ finished: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = .current
        f.dateFormat = "yyyy-MM-dd HH:mm"
        return f.string(from: finished)
    }

    /// "Zaloha (RAID)": the destination folder and its volume; a volume root alone is its volume name (Windows
    /// "Zaloha (D:)" / "D:"). Touches the disk (the volume name) — never on the main thread for many rows.
    public static func diskName(_ root: String) -> String {
        let path = root.trimmingTrailingSlash()
        let name = (path as NSString).lastPathComponent
        let parts = path.split(separator: "/")
        let fromPath = path.hasPrefix("/Volumes/") && parts.count >= 2 ? String(parts[1]) : nil
        let volume = FileManager.default.fileExists(atPath: path)
            ? (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeNameKey]))?.volumeName ?? fromPath
            : fromPath
        if path.hasPrefix("/Volumes/"), parts.count == 2 { return volume ?? name }
        if FileManager.default.fileExists(atPath: path), let m = VolumeFacts.mount(of: path),
           m.mountPoint.trimmingTrailingSlash() == VolumeFacts.realPath(path).trimmingTrailingSlash() {
            return volume ?? name
        }
        guard let volume, !name.isEmpty, name != volume else { return name.isEmpty ? (volume ?? root) : name }
        return "\(name) (\(volume))"
    }

    static func files(_ n: Int) -> String { "\(n) \(Texts.plural(n, "file", "files"))" }

    /// A card emptied in the camera after the backup (everything on it was backed up, then deleted).
    public static func emptied(_ m: HistoryMatch) -> (String, HistoryTone) {
        ("The card is empty — backed up earlier on \(when(m.entry.finished)) to \(m.entry.destinations.map { diskName($0.root) }.joined(separator: ", "))",
         .neutral)
    }

    /// `fullPaths`: the CLI names the destinations by their full root, the GUI by diskName.
    public static func line(_ m: HistoryMatch, fullPaths: Bool = false) -> (String, HistoryTone) {
        let e = m.entry
        let disks = e.destinations.map { fullPaths ? $0.root : diskName($0.root) }.joined(separator: ", ")
        // R6: a copy the app never finished — it can be resumed.
        if e.unfinished {
            return ("Unfinished backup on \(when(e.finished)) to \(disks) — the copy was interrupted; it can be finished", .warning)
        }
        let head = "Backed up on \(when(e.finished)) to \(disks)"
        if m.kind == .changed {
            let n = m.changed.count
            return ("\(head), but the card has changed since (\(files(n)) differ\(n == 1 ? "s" : "")) — cannot append, only a full copy", .warning)
        }
        var (state, tone): (String, HistoryTone) = !e.success ? (" — with errors (\(files(e.failedFiles ?? 0)))", .error)
            : !e.verified ? (" — NOT VERIFIED (safe to eject, do not format)", .warning)
            : (" — verified, safe to format", .ok)
        if m.kind == .newFiles {
            state += " · new files: \(m.new.count) (\(formatBytesShort(m.newBytes)))"
            if tone == .ok { tone = .warning }
        }
        return (head + state, tone)
    }
}
