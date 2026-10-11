import Foundation

/// One card in "Today" and in "Card history": name, size, time, "✓ 2 copies verified · safe to format" (Windows
/// `HistoryRow`). The Mac also shows the files and the disks of the backup on a second line.
public struct HistoryRow: Equatable, Identifiable {
    public let id: String
    public let cardName: String
    public let size: String
    public let time: String
    public let status: String
    public let tone: HistoryTone
    public let project: String?
    /// "212 files" — history.json does not count clips.
    public let files: String
    /// "RAID_A, SHUTTLE_B": the disks the card was backed up to, named from their paths (no disk is touched).
    public let disks: String

    public init(id: String, cardName: String, size: String, time: String, status: String, tone: HistoryTone,
                project: String?, files: String, disks: String) {
        self.id = id; self.cardName = cardName; self.size = size; self.time = time; self.status = status; self.tone = tone
        self.project = project; self.files = files; self.disks = disks
    }
}

/// One day of the history tab: "Today", "Yesterday", "2026-10-06" with its summary.
public struct HistoryDay: Equatable, Identifiable {
    public let title: String
    public let summary: String
    public let rows: [HistoryRow]
    public var id: String { title }
}

/// "Today" under the queue and the "Card history" tab (UI návrh, nová věc 7) — from history.json as the app already loads
/// it. Port of Windows `HistoryView`; same English texts.
public enum HistoryView {
    /// The history tab shows at most this many entries (history.json keeps up to 5 000).
    public static let maxEntries = 300
    public static let todayTitle = "Today"

    static func n(_ count: Int, _ one: String, _ many: String? = nil) -> String {
        "\(count) \(count == 1 ? one : many ?? one + "s")"
    }

    public static func row(_ e: HistoryEntry, calendar: Calendar = .current) -> HistoryRow {
        let copies = e.destinations.count
        let (status, tone): (String, HistoryTone) = e.unfinished ? ("! unfinished", .warning)
            : !e.success ? ("✕ with errors (\(n(e.failedFiles ?? 0, "file")))", .error)
            : !e.verified ? ("! not verified · do not format", .warning)
            : ("✓ \(n(copies, "copy", "copies")) verified · safe to format", .ok)
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = calendar.timeZone
        f.dateFormat = "HH:mm"
        return HistoryRow(id: e.id, cardName: e.cardName, size: formatBytes3(e.bytes), time: f.string(from: e.finished),
                          status: status, tone: tone, project: e.project, files: n(e.files, "file"),
                          disks: e.destinations.map { diskName($0.root) }.joined(separator: ", "))
    }

    /// The disk of a destination root from its path alone: "/Volumes/RAID_A/Offload" → "RAID_A"; elsewhere the folder.
    public static func diskName(_ root: String) -> String {
        let parts = root.split(separator: "/").map(String.init)
        if parts.count >= 2 && parts[0] == "Volumes" { return parts[1] }
        return parts.last ?? root
    }

    /// "3 cards · 412 GB · all verified", "2 cards · 18.0 GB · 1 card with errors", "1 card · 2.00 GB · 1 not verified".
    public static func summary(_ entries: [HistoryEntry]) -> String {
        var parts = [n(entries.count, "card"), formatBytes3(entries.reduce(0) { $0 + $1.bytes })]
        // An unfinished backup is neither verified nor failed — its own count (Windows ruling I5).
        let unfinished = entries.filter(\.unfinished).count
        let failed = entries.filter { !$0.unfinished && !$0.success }.count
        let unverified = entries.filter { !$0.unfinished && $0.success && !$0.verified }.count
        if failed == 0 && unverified == 0 && unfinished == 0 { parts.append("all verified") }
        if unfinished > 0 { parts.append("\(unfinished) unfinished") }
        if failed > 0 { parts.append("\(n(failed, "card")) with errors") }
        if unverified > 0 { parts.append("\(unverified) not verified") }
        return parts.joined(separator: " · ")
    }

    /// The cards finished today (local time), newest first; nil when there are none (the section hides).
    public static func today(_ entries: [HistoryEntry], now: Date, calendar: Calendar = .current) -> HistoryDay? {
        days(entries, now: now, calendar: calendar).first { $0.title == todayTitle }
    }

    /// Every day with a card, newest first (at most maxEntries cards in all): "Today", "Yesterday", then "2026-10-06".
    public static func days(_ entries: [HistoryEntry], now: Date, calendar: Calendar = .current) -> [HistoryDay] {
        let newest = entries.sorted { $0.finished > $1.finished }.prefix(maxEntries)
        let today = calendar.startOfDay(for: now)
        let yesterday = calendar.date(byAdding: .day, value: -1, to: today)!
        var order: [Date] = []
        var groups: [Date: [HistoryEntry]] = [:]
        for e in newest {
            let day = calendar.startOfDay(for: e.finished)
            if groups[day] == nil { order.append(day) }
            groups[day, default: []].append(e)
        }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = calendar.timeZone
        f.dateFormat = "yyyy-MM-dd"
        return order.sorted(by: >).map { day in
            let list = groups[day]!
            let title = day == today ? todayTitle : day == yesterday ? "Yesterday" : f.string(from: day)
            return HistoryDay(title: title, summary: summary(list), rows: list.map { row($0, calendar: calendar) })
        }
    }
}
