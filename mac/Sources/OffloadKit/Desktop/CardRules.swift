import Foundation

/// The choice in "Cards already backed up" before a full copy.
public enum BackedUpChoice { case copyAgain, skipBackedUp, cancel }

/// What the prompt "Cards already backed up" shows (Windows `BackedUpPrompt`, spec 2.1).
public struct BackedUpPromptModel: Equatable {
    public let title: String
    public let lines: [String]
    public let question: String
}

public enum BackedUpPrompt {
    public static let copyAgainText = "Copy again in full"
    public static let skipText = "Skip backed-up"
    public static let cancelText = "Cancel"

    /// A selected card history knows is asked about before a full copy — except one that changed since (its own line
    /// says only a full copy is possible), one emptied in the camera, and an unfinished backup (R6: it is no backup).
    public static func shouldAsk(_ insight: CardInsight?) -> Bool {
        guard let insight, !insight.isEmptied, let m = insight.match else { return false }
        return m.kind != .changed && !m.entry.unfinished
    }

    /// The selected cards to ask about: backed up and not ticked for the append.
    public static func cardsToAsk(_ selected: [String], insight: (String) -> CardInsight?, appendChosen: (String) -> Bool) -> [String] {
        selected.filter { shouldAsk(insight($0)) && !appendChosen($0) }
    }

    /// "A001 — 2026-10-06 08:15, Zaloha (RAID), Zaloha2 (SSD), verified · new files: 12"
    public static func line(_ m: HistoryMatch) -> String {
        let e = m.entry
        let state = !e.success ? "with errors (\(e.failedFiles ?? 0) \(Texts.plural(e.failedFiles ?? 0, "file", "files")))"
            : e.verified ? "verified" : "NOT VERIFIED"
        let line = "\(e.cardName) — \(HistoryText.when(e.finished)), \(e.destinations.map { HistoryText.diskName($0.root) }.joined(separator: ", ")), \(state)"
        return m.kind == .newFiles ? "\(line) · new files: \(m.new.count)" : line
    }

    /// The folder a full copy would probably create ("e.g."; the real name is picked at start): "A001_2" when the card was
    /// copied under the same name into this date folder, otherwise "2026-10-07/A001".
    public static func newFolder(_ entry: HistoryEntry, date: String, plannedName: String) -> String {
        entry.date == date && entry.cardName.caseInsensitiveCompare(plannedName) == .orderedSame ? "\(plannedName)_2" : "\(date)/\(plannedName)"
    }

    public static func build(_ cards: [(match: HistoryMatch, plannedName: String)], date: String) -> BackedUpPromptModel {
        let folders = cards.map { newFolder($0.match.entry, date: date, plannedName: $0.plannedName) }.joined(separator: ", ")
        return cards.count == 1
            ? BackedUpPromptModel(title: "Card already backed up", lines: [line(cards[0].match)],
                                  question: "Copy it again in full? A new folder will be created (e.g. \(folders)).")
            : BackedUpPromptModel(title: "Cards already backed up", lines: cards.map { line($0.match) },
                                  question: "Copy them again in full? New folders will be created (e.g. \(folders)).")
    }
}

/// "Append" in the cards list (Windows `AppendRules`, spec 2.2).
public enum AppendRules {
    /// Only for a card volume whose history match found new files (and nothing changed) and whose earlier card folders
    /// can be reached — never for a folder (matched by its shape alone, too weak a proof) or another card.
    /// Ruling I5 (plan 10, task 69): a backup the app never finished ("unfinished") is offered also with nothing new on
    /// the card — "Finish interrupted backup" completes its copy or its verification (R6).
    public static func offer(_ insight: CardInsight?) -> Bool {
        guard let i = insight, i.error == nil, i.volume != nil, i.appendReachable, let m = i.match, !m.entry.destinations.isEmpty else { return false }
        return m.kind == .newFiles || (m.kind == .identical && m.entry.unfinished)
    }

    /// The card's backup was interrupted: the button says "Finish", not "Append".
    public static func isResume(_ m: HistoryMatch) -> Bool { m.entry.unfinished }

    /// "Append new clips only to the earlier backup (12 files · 3.2 GB)"; R6: "Finish interrupted backup (…)" for a copy the
    /// app never finished (what is on the disks already is not copied again) — the texts of Windows.
    public static func checkboxText(_ m: HistoryMatch) -> String {
        let files = "\(m.new.count) \(Texts.plural(m.new.count, "file", "files")) · \(formatBytesShort(m.newBytes))"
        if m.entry.unfinished {
            return m.new.isEmpty ? "Finish interrupted backup (finishes the verification)" : "Finish interrupted backup (\(files))"
        }
        return "Append new clips only to the earlier backup (\(files))"
    }

    /// "Folder: 2026-10-06/A001 on 2 destinations" — the backup's own card folders (the name may be "A001_2").
    public static func folderText(_ e: HistoryEntry) -> String {
        let first = (e.destinations.first?.cardPath ?? "").trimmingTrailingSlash()
        let folder = (first as NSString).lastPathComponent
        let date = ((first as NSString).deletingLastPathComponent as NSString).lastPathComponent
        let n = e.destinations.count
        return "Folder: \(date)/\(folder) on \(n) \(Texts.plural(n, "destination", "destinations"))"
    }

    /// The job card's title: "A001 · append".
    public static func title(_ cardName: String) -> String { "\(cardName) · append" }
}

/// One card row for the name resolution: `typed` is what the user wrote in "Folder name".
public struct CardNameInput {
    public let key: String
    public let typed: String?
    public let insight: CardInsight?
    public let fallback: String

    public init(key: String, typed: String?, insight: CardInsight?, fallback: String) {
        self.key = key; self.typed = typed; self.insight = insight; self.fallback = fallback
    }
}

/// What the empty "Folder name" field shows — and what Start uses when nothing is typed (Windows `CardNaming`).
public enum CardNaming {
    /// The clips' reel first; a card history does not know gets "next in line" for the date folder; otherwise
    /// `fallback`. `hint`: the grey line under the field, or nil.
    public static func placeholder(suggestFromClips: Bool, insight: CardInsight?, history: [HistoryEntry]?, date: String, today: String,
                                   fallback: String, taken: [String] = [], project: String? = nil) -> (name: String, hint: String?) {
        guard suggestFromClips, let insight else { return (fallback, nil) }
        if let s = insight.nameSuggestion { return (s.name, s.hint) }
        if let note = insight.nameNote { return (fallback, note) }
        if insight.error == nil, insight.match == nil, let history,
           let next = ReelSuggestion.next(history, date: date, project: project, today: today, taken: taken) {
            return (next.name, next.hint)
        }
        return (fallback, nil)
    }

    /// The names for all cards of one start, in list order: a typed name wins (no hint); the others get their suggestion,
    /// unknown cards are numbered one after another skipping typed and already suggested names. The GUI placeholders and
    /// Start both use this, so what is shown is what is copied.
    public static func resolve(suggestFromClips: Bool, cards: [CardNameInput], history: [HistoryEntry]?, date: String, today: String,
                               project: String? = nil) -> [String: (name: String, hint: String?)] {
        var result: [String: (name: String, hint: String?)] = [:]
        var taken: [String] = []
        for c in cards {
            if let t = c.typed?.trimmingCharacters(in: .whitespaces), !t.isEmpty { taken.append(t) }
            else if suggestFromClips, let n = c.insight?.nameSuggestion { taken.append(n.name) }
        }
        for c in cards {
            if let t = c.typed?.trimmingCharacters(in: .whitespaces), !t.isEmpty {
                result[c.key] = (t, nil)
                continue
            }
            let p = placeholder(suggestFromClips: suggestFromClips, insight: c.insight, history: history, date: date, today: today,
                                fallback: c.fallback, taken: taken, project: project)
            if p.name != c.fallback && c.insight?.nameSuggestion == nil { taken.append(p.name) }
            result[c.key] = p
        }
        return result
    }
}

/// A destination as the space check sees it: its volume (probed off the main thread) and how it is named.
public struct SpaceTarget: Equatable {
    public let root: String
    public let volume: String
    public let label: String

    public init(root: String, volume: String, label: String) {
        self.root = root; self.volume = volume; self.label = label
    }
}

/// The GUI's space check before a start (spec 2.5), from memory only. The engine checks again when each job starts.
public enum SpaceCheck {
    /// One card about to start: its inspected size, or — not inspected yet — the used space of its volume as an upper
    /// estimate (nil = unknown: adds nothing, never blocks). `folder`: the proxy folder.
    public static func forCard(_ insight: CardInsight?, usedOnVolume: Int64?, proxy: ProxySettings, tools: ExternalTools,
                               targets: [SpaceTarget], folder: SpaceTarget?) -> [(demand: VolumeDemand, destination: Int?)] {
        let bytes: Int64, files: Int, clips: [Clip]
        if let i = insight, i.error == nil {
            (bytes, files, clips) = (i.totalBytes, i.fileCount, i.clips)
        } else if let used = usedOnVolume {
            (bytes, files, clips) = (used, 0, [])
        } else {
            return []
        }
        return demands(SpaceEstimator.cardBytes(bytes, files: files), SpaceEstimator.extra(clips, proxy, tools), targets, folder,
                       indexes: Array(targets.indices))
    }

    /// An append: only the new files, onto the destinations of the earlier backup that are in `targets`.
    public static func forAppend(_ m: HistoryMatch, proxy: ProxySettings, tools: ExternalTools, targets: [SpaceTarget],
                                 folder: SpaceTarget?) -> [(demand: VolumeDemand, destination: Int?)] {
        var mine: [(SpaceTarget, Int)] = []
        for (i, d) in m.entry.destinations.enumerated() {
            if let t = targets.first(where: { CardRecord.normal($0.root) == CardRecord.normal(d.root) }) { mine.append((t, i)) }
        }
        let clips = ClipDetector.detect(m.new)
        return demands(SpaceEstimator.cardBytes(m.newBytes, files: m.new.count), SpaceEstimator.extra(clips, proxy, tools),
                       mine.map(\.0), folder, indexes: mine.map(\.1))
    }

    static func demands(_ card: Int64, _ extra: SpaceExtra, _ targets: [SpaceTarget], _ folder: SpaceTarget?,
                        indexes: [Int]) -> [(demand: VolumeDemand, destination: Int?)] {
        var out: [(demand: VolumeDemand, destination: Int?)] = targets.enumerated().map { i, t in
            (VolumeDemand(volume: t.volume, label: t.label, need: extra.forDestination(indexes[i], card: card)), indexes[i])
        }
        if let folder, extra.proxyFolder != nil, extra.proxy > 0 {
            out.append((VolumeDemand(volume: folder.volume, label: folder.label, need: SpaceNeed(card: 0, proxy: extra.proxy, report: 0)), nil))
        }
        return out
    }

    /// What a job in the queue still needs: all of it while waiting, the rest of the card while copying (`written` on
    /// that destination), proxies and report in stage 2, nothing once finished.
    public static func remaining(_ need: SpaceNeed, phase: JobStatus.Phase, written: Int64) -> SpaceNeed {
        switch phase {
        case .waiting: return need
        case .copying, .flushing, .verifying, .reporting:
            return SpaceNeed(card: max(0, need.card - written), proxy: need.proxy, report: need.report)
        case .analyzing, .proxies, .finishingVerification: return SpaceNeed(card: 0, proxy: need.proxy, report: need.report)
        case .done, .failed, .cancelled: return .none
        }
    }
}
