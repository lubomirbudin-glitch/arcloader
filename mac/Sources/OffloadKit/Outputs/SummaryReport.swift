import Foundation

/// One backup the user picked: an entry of the card history, or a card folder (on a destination, or its local copy).
public enum SummaryInput {
    case entry(HistoryEntry)
    case folder(String)
}

/// Where the numbers of a card come from: its folder on a destination, the copy of its report on this Mac
/// (`~/Documents/Offload reporty`, the disk went away), or nothing but the history entry.
public enum SummarySource { case destination, localCopy, none }

/// One copy of the backup: the destination card folder, whether it is connected now, and whether it was verified —
/// read on the disk (`basis` empty), or what the report or the history says about a disk that is not connected.
public struct SummaryCopy: Equatable {
    public let root: String
    public let cardPath: String
    public let connected: Bool
    public let verified: Bool?
    public let basis: String
}

/// The first or the last clapper: the clip and its start timecode (ALEXA: time of day).
public struct SummaryClap: Equatable {
    public let clip: String
    public let card: String
    public let date: String
    public let timecode: String?
    public let timeOfDay: Bool
    public let recorded: Date?
    /// The clip's path in the card folder — the last tie-break of the order.
    public let clipPath: String
}

/// One backup in the summary report (Windows `SummaryCard`).
public struct SummaryCard {
    public var name: String
    /// The shooting day: the date folder ("2026-10-06").
    public var date: String
    public var entry: HistoryEntry?
    /// Every generation of the card's reports (full copy, appends, repairs); nil when none could be found.
    public var record: CardRecord?
    public var source: SummarySource = .none
    /// The folder the reports were read from (a destination card folder or the local copy).
    public var readFrom: String?
    /// The copy of the card's report on this Mac, nil when there is none.
    public var localCopy: String?
    public var copies: [SummaryCopy] = []

    public var hasReport: Bool { !(record?.generations.isEmpty ?? true) }
    /// The clips the camera recorded: camera proxies (Sony SUB, PROXY folders) are not counted.
    var videoClips: [ClipEntry] { hasReport ? record!.clips.filter { !$0.clip.isCameraProxy } : [] }
    public var clipCount: Int { videoClips.count }
    /// From the reports; without one, what the history entry recorded.
    public var files: Int { hasReport ? record!.files.count : entry?.files ?? 0 }
    public var bytes: Int64 { hasReport ? record!.files.values.reduce(0) { $0 + $1.file.size } : entry?.bytes ?? 0 }
    /// Running time: the sum of the clips' durations.
    public var seconds: Double { videoClips.reduce(0) { $0 + ($1.info?.duration ?? 0) } }
    public var cameras: [String] { SummaryReport.camerasOf(videoClips) }
    /// Every copy was verified (the shared definition, CardRecord.readBack) — never without a report.
    public var verified: Bool {
        hasReport && (copies.isEmpty ? record!.verified : copies.allSatisfy { $0.verified == true })
    }
    var claps: ClapSet { ClapSet.of(self) }
    public var first: SummaryClap? { claps.first }
    public var last: SummaryClap? { claps.last }
}

/// The candidates for the first and the last clapper of a card, both ways (record time; day + clip number).
struct ClapSet {
    var allTimed = false
    var firstByTime, lastByTime, firstByNumber, lastByNumber: SummaryClap?
    var first: SummaryClap? { allTimed ? firstByTime : firstByNumber }
    var last: SummaryClap? { allTimed ? lastByTime : lastByNumber }

    static func of(_ card: SummaryCard) -> ClapSet {
        let clips = card.videoClips.map { SummaryReport.clapOf(card, $0) }
        guard !clips.isEmpty else { return ClapSet() }
        let timed = clips.allSatisfy { $0.recorded != nil }
        return ClapSet(allTimed: timed,
                       firstByTime: timed ? min(clips, SummaryReport.byTime) : nil,
                       lastByTime: timed ? max(clips, SummaryReport.byTime) : nil,
                       firstByNumber: min(clips, SummaryReport.byNumber),
                       lastByNumber: max(clips, SummaryReport.byNumber))
    }

    /// The first and the last of `cards` together: by record time when every clip of every card has one (decision of
    /// 2026-10-09), otherwise by day and clip number.
    static func of(_ cards: [SummaryCard]) -> (first: SummaryClap?, last: SummaryClap?) {
        let sets = cards.map(\.claps).filter { $0.firstByNumber != nil }
        guard !sets.isEmpty else { return (nil, nil) }
        if sets.allSatisfy(\.allTimed) {
            return (min(sets.map { $0.firstByTime! }, SummaryReport.byTime), max(sets.map { $0.lastByTime! }, SummaryReport.byTime))
        }
        return (min(sets.map { $0.firstByNumber! }, SummaryReport.byNumber), max(sets.map { $0.lastByNumber! }, SummaryReport.byNumber))
    }

    static func min(_ clips: [SummaryClap], _ by: (SummaryClap, SummaryClap) -> Int) -> SummaryClap {
        clips.dropFirst().reduce(clips[0]) { a, b in by(b, a) < 0 ? b : a }
    }

    static func max(_ clips: [SummaryClap], _ by: (SummaryClap, SummaryClap) -> Int) -> SummaryClap {
        clips.dropFirst().reduce(clips[0]) { a, b in by(b, a) > 0 ? b : a }
    }
}

/// A shooting day, or all the cards (date "").
public struct SummaryGroup {
    public let date: String
    public let cards: [SummaryCard]
    public var clipCount: Int { cards.reduce(0) { $0 + $1.clipCount } }
    public var files: Int { cards.reduce(0) { $0 + $1.files } }
    public var bytes: Int64 { cards.reduce(0) { $0 + $1.bytes } }
    public var seconds: Double { cards.reduce(0) { $0 + $1.seconds } }
    public var cameras: [String] { SummaryReport.camerasOf(cards.flatMap(\.videoClips)) }
    public var verifiedCards: Int { cards.filter(\.verified).count }
    public var withoutReport: Int { cards.filter { !$0.hasReport }.count }
    public var first: SummaryClap? { ClapSet.of(cards).first }
    public var last: SummaryClap? { ClapSet.of(cards).last }
}

public struct SummaryModel {
    public let cards: [SummaryCard]
    public var days: [SummaryGroup] {
        var order: [String] = [], by: [String: [SummaryCard]] = [:]
        for c in cards {
            if by[c.date] == nil { order.append(c.date) }
            by[c.date, default: []].append(c)
        }
        return order.sorted { $0.utf16.lexicographicallyPrecedes($1.utf16) }.map { SummaryGroup(date: $0, cards: by[$0]!) }
    }
    public var total: SummaryGroup { SummaryGroup(date: "", cards: cards) }
}

/// What `save` wrote: the local HTML (always), its PDF (nil when it could not be printed), the copies in the folder the
/// user picked, or why they could not be written there.
public struct SummaryFiles {
    public let html: String
    public let pdf: String?
    public let extraHTML: String?
    public let extraPDF: String?
    public let extraError: String?
}

/// "Summary report of selected backups" (user's task 2026-10-09, Windows 0.8.0 `SummaryReport`): one HTML + PDF (no
/// thumbnails) from backups picked in the card history — cards of different days and disks. Read from the cards'
/// reports on the destinations (every generation), else from their copy on this Mac (LocalReports), else only what
/// history.json says — never an error. Reads only; writes nothing but `<Offload reporty>/summary/` and, when the user
/// picks one, a folder on a disk (never over a file). Same English texts as Windows.
public enum SummaryReport {
    public static let folderName = "summary"

    /// Where the summary is saved on this Mac: the folder of the local report copies (LocalReports.root, moved by
    /// OFFLOAD_REPORTS_DIR) — also when that copy is switched off.
    public static var localRoot: String { LocalReports.root }

    /// Every folder the local report copies may be in: this Mac's, and the names other versions use.
    public static var localRoots: [String] {
        let docs = (NSHomeDirectory() as NSString).appendingPathComponent("Documents")
        var out: [String] = []
        for r in [LocalReports.root, docs + "/" + LocalReports.folderName] + LocalReports.oldFolderNames.map({ docs + "/" + $0 })
            where !out.contains(r) { out.append(r) }
        return out
    }

    static func hasReport(_ folder: String) -> Bool { DayReport.hasReport(folder) }

    /// The command line's arguments: history ids, card folders, or date folders (every card in them).
    public static func inputs(_ args: [String], history: [HistoryEntry]) throws -> [SummaryInput] {
        var list: [SummaryInput] = []
        for arg in args {
            if let e = history.first(where: { $0.id.lowercased() == arg.lowercased() }) {
                list.append(.entry(e))
                continue
            }
            var dir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: arg, isDirectory: &dir), dir.boolValue else {
                throw OffloadError.failed(Msg.summaryUnknown(arg))
            }
            let full = DayReport.fullPath(arg)
            if hasReport(full) {
                list.append(.folder(full))
                continue
            }
            let cards = DayReport.foldersOf(full).filter(hasReport)
            if cards.isEmpty { throw OffloadError.failed(Msg.summaryNotACard(full)) }
            list += cards.map { .folder($0) }
        }
        return list
    }

    /// The picked backups, by day and card name. Reads only. One card is one card: backups of the same card (the
    /// history's identity — the shape of its files, and its name) are joined — counted once, every copy listed.
    public static func load(_ inputs: [SummaryInput], localRoots: [String] = localRoots) -> SummaryModel {
        var roots: [String: Bool] = [:]
        var groups: [(keys: Set<String>, cards: [SummaryCard])] = []
        for input in inputs {
            let card: SummaryCard
            switch input {
            case .entry(let e): card = fromEntry(e, localRoots: localRoots, roots: &roots)
            case .folder(let f): card = fromFolder(f, localRoots: localRoots, roots: &roots)
            }
            let keys = identity(card)
            let hits = groups.indices.filter { !groups[$0].keys.isDisjoint(with: keys) }
            guard let into = hits.first else {
                groups.append((keys, [card]))
                continue
            }
            groups[into].keys.formUnion(keys)
            groups[into].cards.append(card)
            for other in hits.dropFirst().reversed() {
                groups[into].keys.formUnion(groups[other].keys)
                groups[into].cards += groups[other].cards
                groups.remove(at: other)
            }
        }
        let cards = groups.map { join($0.cards) }.sorted { a, b in
            a.date != b.date ? a.date.utf16.lexicographicallyPrecedes(b.date.utf16)
                : a.name.localizedStandardCompare(b.name) == .orderedAscending
        }
        return SummaryModel(cards: cards)
    }

    /// The history's `shape` of every file the card's reports record; nil without a readable report or files.
    public static func shapeOf(_ r: CardRecord) -> String? {
        guard !r.generations.isEmpty, !r.files.isEmpty else { return nil }
        return CardFingerprint.compute(r.files.values.map { SourceFile(relPath: $0.file.path, size: $0.file.size, modified: $0.file.modified) }).shape
    }

    /// How a card is known again: its shape and its name, the folder it was read from, the history id.
    static func identity(_ c: SummaryCard) -> Set<String> {
        var keys = Set<String>()
        let name = (c.record?.generations.first?.report.cardName ?? c.entry?.cardName ?? c.name).lowercased()
        if let r = c.record, let shape = shapeOf(r) { keys.insert("shape|\(shape)|\(name)") }
        if let e = c.entry, !e.shape.isEmpty { keys.insert("shape|\(e.shape)|\(e.cardName.lowercased())") }
        if let f = c.readFrom { keys.insert("path|" + f.trimmingTrailingSlash().lowercased()) }
        if let e = c.entry { keys.insert("id|" + e.id) }
        if keys.isEmpty { keys.insert("card|\(c.name)|\(c.date)|\(UUID().uuidString)") }
        return keys
    }

    /// Backups of one card as one: numbers from the best read one (a destination, then the local copy, then the one with
    /// more generations), every copy of every backup (once per folder).
    static func join(_ cards: [SummaryCard]) -> SummaryCard {
        guard cards.count > 1 else { return cards[0] }
        func rank(_ s: SummarySource) -> Int { s == .destination ? 0 : s == .localCopy ? 1 : 2 }
        let best = cards.enumerated().min { a, b in
            rank(a.element.source) != rank(b.element.source) ? rank(a.element.source) < rank(b.element.source)
                : (a.element.record?.generations.count ?? 0) != (b.element.record?.generations.count ?? 0)
                ? (a.element.record?.generations.count ?? 0) > (b.element.record?.generations.count ?? 0)
                : a.offset < b.offset
        }!
        var copies: [SummaryCopy] = []
        for copy in ([best.element] + cards.enumerated().filter { $0.offset != best.offset }.map(\.element)).flatMap(\.copies)
            where !copies.contains(where: { samePath($0.cardPath, copy.cardPath) }) {
            copies.append(copy)
        }
        var c = best.element
        c.entry = c.entry ?? cards.compactMap(\.entry).first
        c.localCopy = c.localCopy ?? cards.compactMap(\.localCopy).first
        c.copies = copies
        return c
    }

    static func fromEntry(_ e: HistoryEntry, localRoots: [String], roots: inout [String: Bool]) -> SummaryCard {
        let copies = e.destinations.map { (root: $0.root, cardPath: $0.cardPath) }
        let folderName = copies.lazy.map { ($0.cardPath.trimmingTrailingSlash() as NSString).lastPathComponent }.first { !$0.isEmpty } ?? e.cardName
        return build(e, copies: copies, localFolder: nil, folderName: folderName, date: e.date, localRoots: localRoots, roots: &roots)
    }

    static func fromFolder(_ path: String, localRoots: [String], roots: inout [String: Bool]) -> SummaryCard {
        let folder = DayReport.fullPath(path)
        let record = CardRecord.load(folder)
        let newest = record.generations.last?.report
        var copies = (newest?.destinations ?? []).map { (root: $0.root, cardPath: $0.cardPath) }
        let isLocal = localRoots.contains { folder.lowercased().hasPrefix(DayReport.fullPath($0).lowercased() + "/") }
        // A destination folder the report names differently (another mount name, a Windows report): it is a copy too.
        if !isLocal && !copies.contains(where: { samePath($0.cardPath, folder) }) {
            copies.insert((root: ((folder as NSString).deletingLastPathComponent as NSString).deletingLastPathComponent, cardPath: folder), at: 0)
        }
        let date = record.generations.first?.report.date ?? ((folder as NSString).deletingLastPathComponent as NSString).lastPathComponent
        return build(nil, copies: copies, localFolder: isLocal ? folder : nil, folderName: (folder as NSString).lastPathComponent,
                     date: date, localRoots: localRoots, roots: &roots)
    }

    /// The folder's report is there — when its volume is reachable (looked at once per volume: a network path that is
    /// gone costs its timeout once, not once per card).
    static func reachable(_ cardPath: String, roots: inout [String: Bool]) -> Bool {
        guard cardPath.hasPrefix("/") else { return false }
        let root = Volumes.volumeRoot(of: cardPath) ?? "/"
        if roots[root] == nil { roots[root] = FileManager.default.fileExists(atPath: root) }
        return roots[root]! && hasReport(cardPath)
    }

    /// The reports are read from a connected destination (the newest first); one that cannot be read gives way to the
    /// next, then to the local copy, then to none — its reason stays in the record for the notes.
    static func build(_ entry: HistoryEntry?, copies: [(root: String, cardPath: String)], localFolder: String?, folderName: String,
                      date: String, localRoots: [String], roots: inout [String: Bool]) -> SummaryCard {
        var connected: [String] = []
        for c in copies where reachable(c.cardPath, roots: &roots) { connected.append(c.cardPath) }
        var records: [String: CardRecord] = [:]
        func loadAt(_ p: String) -> CardRecord {
            if let r = records[p.lowercased()] { return r }
            let r = CardRecord.load(p)
            records[p.lowercased()] = r
            return r
        }
        var record: CardRecord?, unreadable: CardRecord?, readFrom: String?
        var source = SummarySource.none
        // The newest report first (the one whose main report was written last — an append or a repair updates it).
        let ordered = connected.sorted { modified($0) > modified($1) }
        for p in ordered {
            let r = loadAt(p)
            if r.generations.isEmpty {
                if unreadable == nil { unreadable = r }
                continue
            }
            record = r
            readFrom = p
            source = .destination
            break
        }
        let local = localFolder ?? findLocal(localRoots, date: date, folderName: folderName, record: record, entry: entry)
        if record == nil, let local {
            let copy = CardRecord.load(local)
            if !copy.generations.isEmpty {
                record = copy
                readFrom = local
                source = .localCopy
            }
        }
        if record == nil { record = unreadable }
        let summaryCopies = copies.map { c -> SummaryCopy in
            if connected.contains(c.cardPath) {
                return SummaryCopy(root: c.root, cardPath: c.cardPath, connected: true, verified: loadAt(c.cardPath).verified, basis: "")
            }
            if let r = record, !r.generations.isEmpty {
                return SummaryCopy(root: c.root, cardPath: c.cardPath, connected: false, verified: verifiedAt(r, c.cardPath), basis: "per the report")
            }
            return SummaryCopy(root: c.root, cardPath: c.cardPath, connected: false,
                               verified: entry.flatMap { $0.unfinished ? nil : $0.verified }, basis: entry != nil ? "per the history" : "")
        }
        let name = source == .destination && readFrom != nil ? (readFrom! as NSString).lastPathComponent : folderName
        let day = date.isEmpty ? record?.generations.first?.report.date ?? "" : date
        return SummaryCard(name: name, date: day, entry: entry, record: record, source: source, readFrom: readFrom,
                           localCopy: local, copies: summaryCopies)
    }

    static func modified(_ cardPath: String) -> Date {
        let p = cardPath + "/" + Layout.reportDirName + "/" + CardRecord.mainReport
        return (try? FileManager.default.attributesOfItem(atPath: p)[.modificationDate] as? Date) ?? .distantPast
    }

    /// The shared "verified" definition (CardRecord.readBack) for a destination card folder that is not connected now:
    /// the report's own column of that folder (its recorded serial, so the column is found by disk and path).
    static func verifiedAt(_ r: CardRecord, _ cardPath: String) -> Bool {
        guard !r.generations.isEmpty, r.problems.isEmpty, !r.incomplete else { return false }
        if r.files.isEmpty { return r.generations.allSatisfy { $0.report.verified } }
        return r.files.values.allSatisfy { f in
            CardRecord.readBack(f, cardPath: cardPath, serial: f.report.destinations.first { samePath($0.cardPath, cardPath) }?.volumeSerial)
        }
    }

    static func samePath(_ a: String, _ b: String) -> Bool { a.trimmingTrailingSlash().lowercased() == b.trimmingTrailingSlash().lowercased() }

    /// `<started UTC>|<cardName>|<source>` of a generation-1 report — the backup a local copy belongs to (Windows
    /// `.offload-origin`; the Mac reads it from the copy's own report, or from the marker when a copy carries one).
    static func origin(_ r: OffloadReport) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return "\(f.string(from: r.started))|\(r.cardName)|\(r.source)"
    }

    static func originOf(_ localFolder: String) -> String? {
        if let marker = try? String(contentsOfFile: localFolder + "/.offload-origin", encoding: .utf8) {
            return marker.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        let main = localFolder + "/" + Layout.reportDirName + "/" + CardRecord.mainReport
        return (try? OffloadReport.read(main)).map(origin)
    }

    /// The local copy of this backup: `<root>/<date>/<folder>` or `…_2`, `_3` whose origin names it — exactly (the
    /// report's start, card and source) when a report was read; otherwise only the one copy whose origin fits the
    /// history's card and source AND whose reports ended when the history's generations did — never a guess.
    static func findLocal(_ roots: [String], date: String, folderName: String, record: CardRecord?, entry: HistoryEntry?) -> String? {
        guard !date.isEmpty, !folderName.isEmpty else { return nil }
        let main = record?.generations.first { $0.generation == 1 }?.report
        let wanted = main.map(origin)
        guard wanted != nil || entry != nil else { return nil }
        var candidates: [String] = []
        let fm = FileManager.default
        for root in roots {
            let day = root + "/" + date
            guard let names = try? fm.contentsOfDirectory(atPath: day) else { continue }
            for n in names.sorted() {
                let lower = n.lowercased(), base = folderName.lowercased()
                let named = lower == base || (lower.hasPrefix(base + "_") && lower.count > base.count + 1
                                              && lower.dropFirst(base.count + 1).allSatisfy(\.isNumber))
                let dir = day + "/" + n
                guard named, hasReport(dir), let o = originOf(dir) else { continue }
                if let wanted { if o == wanted { candidates.append(dir) } } else if markerFits(o, entry!) { candidates.append(dir) }
            }
        }
        if wanted != nil { return candidates.first }
        let ends = entry!.generations.map(\.finished) + [entry!.finished]
        let fitting = candidates.filter { c in
            CardRecord.load(c).generations.contains { g in ends.contains { abs(g.report.finished.timeIntervalSince($0)) <= 2 } }
        }
        return fitting.count == 1 ? fitting[0] : nil
    }

    static func markerFits(_ marker: String, _ e: HistoryEntry) -> Bool {
        let parts = marker.components(separatedBy: "|")
        return parts.count == 3 && parts[1].lowercased() == e.cardName.lowercased()
            && parts[2].trimmingTrailingSlash().lowercased() == e.source.trimmingTrailingSlash().lowercased()
    }

    static func camerasOf(_ clips: [ClipEntry]) -> [String] {
        var seen = Set<String>(), out: [String] = []
        for c in clips {
            guard let cam = c.info?.camera?.trimmingCharacters(in: .whitespaces), !cam.isEmpty, seen.insert(cam.lowercased()).inserted
            else { continue }
            out.append(cam)
        }
        return out.sorted { $0.utf16.lexicographicallyPrecedes($1.utf16) }
    }

    /// ALEXA records time-of-day timecode: its start TC is the time of the clapper.
    static func timeOfDay(_ c: ClipEntry) -> Bool {
        c.clip.format == .arriraw || (c.info?.camera?.range(of: "ALEXA", options: .caseInsensitive) != nil)
    }

    static func clapOf(_ card: SummaryCard, _ c: ClipEntry) -> SummaryClap {
        let tc = c.info?.timecode?.trimmingCharacters(in: .whitespaces)
        return SummaryClap(clip: c.clip.name, card: card.name, date: card.date, timecode: (tc?.isEmpty ?? true) ? nil : tc,
                           timeOfDay: timeOfDay(c), recorded: ClipInfo.plausible(c.info?.recorded), clipPath: c.clip.path)
    }

    /// By the camera's record time, then the clip number.
    static func byTime(_ a: SummaryClap, _ b: SummaryClap) -> Int {
        switch (a.recorded, b.recorded) {
        case let (x?, y?) where x != y: return x < y ? -1 : 1
        case (nil, _?): return -1
        case (_?, nil): return 1
        default: return byNumber(a, b)
        }
    }

    /// By the shooting day, then the clip number (Finder order: A001C002 before A001C010), then the path.
    static func byNumber(_ a: SummaryClap, _ b: SummaryClap) -> Int {
        if a.date != b.date { return a.date.utf16.lexicographicallyPrecedes(b.date.utf16) ? -1 : 1 }
        switch a.clip.localizedStandardCompare(b.clip) {
        case .orderedAscending: return -1
        case .orderedDescending: return 1
        case .orderedSame: return a.clipPath == b.clipPath ? 0 : (a.clipPath.utf16.lexicographicallyPrecedes(b.clipPath.utf16) ? -1 : 1)
        }
    }

    // MARK: HTML

    static func stat(_ value: String, _ label: String) -> String { "<div class=\"stat\"><b>\(value)</b><span>\(label)</span></div>\n" }

    static func clapCell(_ c: SummaryClap?, withDate: Bool) -> String {
        guard let c else { return "<td class=\"muted\">—</td>" }
        let tc = c.timecode.map(HTMLReport.esc) ?? "<span class=\"muted\">no TC</span>"
        let note = (withDate ? c.date + " · " : "") + c.clip + (c.timeOfDay ? " · time-of-day TC" : c.timecode != nil ? " · TC" : "")
        return "<td class=\"nw\"><span class=\"mono\">\(tc)</span><div class=\"note\">\(HTMLReport.esc(note))</div></td>"
    }

    static func state(_ c: SummaryCard) -> (String, String) {
        guard c.hasReport, let r = c.record else {
            guard let e = c.entry else { return ("warn", "unknown") }
            return e.unfinished ? ("warn", "unfinished (history)") : !e.success ? ("bad", "with errors (history)")
                : !e.verified ? ("warn", "not verified (history)") : ("ok", "verified (history)")
        }
        return r.verifyPending ? ("warn", "VERIFYING") : r.incomplete ? ("warn", "UNFINISHED") : !c.verified ? ("warn", "NOT VERIFIED")
            : !r.failedFiles.isEmpty ? ("warn", "verified, with errors") : ("ok", "verified")
    }

    static func sourceText(_ c: SummaryCard) -> String {
        switch c.source {
        case .destination: return "from the disk"
        case .localCopy: return "report copy on this computer"
        case .none: return "report not available"
        }
    }

    static func cameras(_ list: [String]) -> String { list.isEmpty ? "—" : list.joined(separator: ", ") }
    static func seconds(_ s: Double) -> String { s > 0 ? HTMLReport.duration(s) : "—" }
    static func n(_ count: Int, _ one: String, _ many: String? = nil) -> String { "\(count) \(count == 1 ? one : many ?? one + "s")" }

    /// Self-contained HTML, A4 landscape when printed; no thumbnails. `pdfName`: the PDF next to it, linked at the top;
    /// nil = there is none, no link.
    public static func html(_ model: SummaryModel, forPrint: Bool = false, pdfName: String? = nil, created: Date = Date()) -> String {
        let e = HTMLReport.esc
        let total = model.total
        let days = model.days
        let cards = total.cards.count
        let (badge, badgeClass) = cards > 0 && total.verifiedCards == cards ? ("ALL VERIFIED", "ok") : ("NEEDS ATTENTION", "warn")
        let dayList = days.isEmpty ? "" : days.count == 1 ? days[0].date : "\(days[0].date) – \(days[days.count - 1].date)"
        var h = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Summary report</title>
        <style>
        \(HTMLReport.styles(forPrint: forPrint))
        </style></head><body><main>

        """
        if !forPrint, let pdfName { h += "<p class=\"sub\"><a href=\"\(e(pdfName))\">PDF</a></p>\n" }
        h += "<header><h1>Summary report\(dayList.isEmpty ? "" : " — " + e(dayList))</h1><span class=\"badge \(badgeClass)\">\(badge)</span>\n"
        h += "<span class=\"sub\">Shooting \(Texts.plural(days.count, "day", "days")): \(e(days.map(\.date).joined(separator: ", ")))</span>"
        var projects: [String] = []
        for c in total.cards {
            if let p = (c.record?.project ?? c.entry?.project)?.trimmingCharacters(in: .whitespaces), !p.isEmpty,
               !projects.contains(where: { $0.lowercased() == p.lowercased() }) { projects.append(p) }
        }
        if !projects.isEmpty { h += "<span class=\"sub\">\(projects.count == 1 ? "Project" : "Projects"): \(e(projects.joined(separator: ", ")))</span>" }
        h += "</header>\n<div class=\"stats\">\n"
        h += stat(String(cards), Texts.plural(cards, "card", "cards"))
        h += stat(String(days.count), "shooting " + Texts.plural(days.count, "day", "days"))
        h += stat(String(total.clipCount), "clips")
        h += stat(String(total.files), "files")
        h += stat(formatBytes(total.bytes), "data")
        h += stat(HTMLReport.duration(total.seconds), "duration")
        h += stat("\(total.verifiedCards) / \(cards)", "verified cards")
        h += "</div>\n"
        if total.withoutReport > 0 {
            h += "<p class=\"sub warn\">\(total.withoutReport) \(total.withoutReport == 1 ? "card has" : "cards have") no report available: "
                + "files and size per the history, clips and duration without them.</p>\n"
        }
        let head = "<th class=\"num\">Clips</th><th class=\"num\">Files</th><th class=\"num\">Data</th><th class=\"num\">Duration</th><th>First slate</th><th>Last slate</th><th>Cameras</th>"
        func numbers(_ clips: Int, _ files: Int, _ bytes: Int64, _ s: Double) -> String {
            "<td class=\"num\">\(clips)</td><td class=\"num\">\(files)</td><td class=\"num nw\">\(formatBytes(bytes))</td><td class=\"num\">\(seconds(s))</td>"
        }
        h += "\n<h2>Shooting days</h2><div class=\"panel\"><table>\n<tr><th>Day</th><th class=\"num\">Cards</th>\(head)</tr>\n"
        for d in days {
            h += "<tr><td class=\"nw\">\(e(d.date))</td><td class=\"num\">\(d.cards.count)</td>\(numbers(d.clipCount, d.files, d.bytes, d.seconds))"
            h += "\(clapCell(d.first, withDate: false))\(clapCell(d.last, withDate: false))<td class=\"wrap\">\(e(cameras(d.cameras)))</td></tr>\n"
        }
        h += "</table></div>\n"

        h += "\n<h2>Cards</h2><div class=\"panel\"><table>\n<tr><th>Card</th><th>Day</th>\(head)<th>Verification</th><th>Data from</th></tr>\n"
        for c in model.cards {
            let (vClass, vText) = state(c)
            h += "<tr><td class=\"nw\"><b>\(e(c.name))</b></td><td class=\"nw\">\(e(c.date))</td>"
            if c.hasReport {
                h += numbers(c.clipCount, c.files, c.bytes, c.seconds) + clapCell(c.first, withDate: false) + clapCell(c.last, withDate: false)
            } else {
                h += "<td class=\"num muted\">—</td><td class=\"num\">\(c.files)</td><td class=\"num nw\">\(formatBytes(c.bytes))</td><td class=\"num muted\">—</td><td class=\"muted\">—</td><td class=\"muted\">—</td>"
            }
            h += "<td class=\"wrap\">\(e(cameras(c.cameras)))</td><td class=\"\(vClass)\">\(vText)</td>"
            h += "<td class=\"\(c.source == .none ? "warn" : c.source == .localCopy ? "muted" : "")\">\(sourceText(c))</td></tr>\n"
        }
        h += "<tr><td class=\"nw\"><b>Total</b></td><td class=\"nw\">\(n(days.count, "day"))</td>\(numbers(total.clipCount, total.files, total.bytes, total.seconds))"
        h += "\(clapCell(total.first, withDate: days.count > 1))\(clapCell(total.last, withDate: days.count > 1))<td class=\"wrap\">\(e(cameras(total.cameras)))</td>"
        h += "<td>\(total.verifiedCards) / \(cards) verified</td><td></td></tr>\n</table></div>\n"
        h += "<p class=\"sub\">Slate = the start of the first and the last recorded clip (ordered by the record time from the camera metadata, "
            + "otherwise by the clip number); time = the clip's start timecode, time-of-day TC on an ALEXA. Duration = the sum of the clip lengths.</p>\n"

        h += "\n<h2>Where everything is backed up</h2><div class=\"panel\"><table>\n"
        h += "<tr><th>Card</th><th>Disk</th><th>Path</th><th>Disk connected</th><th>Verified</th><th>Report copy on this computer</th></tr>\n"
        for c in model.cards {
            let rows: [SummaryCopy?] = c.copies.isEmpty ? [nil] : c.copies
            let span = rows.count > 1 ? " rowspan=\"\(rows.count)\"" : ""
            for (i, row) in rows.enumerated() {
                h += "<tr>"
                if i == 0 { h += "<td\(span)><b>\(e(c.name))</b><div class=\"note\">\(e(c.date))</div></td>" }
                if let copy = row {
                    var (vc, vt) = copy.verified == true ? ("ok", "yes") : copy.verified == false ? ("warn", "no") : ("muted", "unknown")
                    if !copy.basis.isEmpty { vt += " <span class=\"note\">(\(copy.basis))</span>" }
                    h += "<td class=\"mono\">\(e(copy.root))</td><td class=\"mono\">\(e(copy.cardPath))</td>"
                    h += "<td class=\"\(copy.connected ? "ok" : "muted")\">\(copy.connected ? "yes" : "not connected")</td><td class=\"\(vc)\">\(vt)</td>"
                } else {
                    h += "<td colspan=\"4\" class=\"muted\">the backup's destinations are not known</td>"
                }
                if i == 0 { h += "<td\(span) class=\"mono\">\(c.localCopy.map(e) ?? "<span class=\"muted\">none</span>")</td>" }
                h += "</tr>\n"
            }
        }
        h += "</table></div>\n"

        let notes = self.notes(model)
        if !notes.isEmpty {
            h += "\n<h2 class=\"warn\">Warnings (\(notes.count))</h2><div class=\"panel\"><table>\n"
            for n in notes { h += "<tr><td>\(e(n.card))</td><td class=\"\(n.bad ? "err" : "warn wrap")\">\(e(n.text))</td></tr>\n" }
            h += "</table></div>\n"
        }
        h += "<footer>\(Product.name) \(e(OffloadVersion.string)) · summary report · \(HTMLReport.dateTime(created))</footer></main></body></html>\n"
        return h
    }

    /// Cards without a report, read from the local copy, with unreadable reports, failed files, not verified.
    public static func notes(_ model: SummaryModel) -> [(card: String, text: String, bad: Bool)] {
        var list: [(card: String, text: String, bad: Bool)] = []
        for c in model.cards {
            let name = "\(c.name) (\(c.date))"
            if c.source == .none {
                list.append((name, "report not available — the backup disk is not connected and there is no report copy on this computer; "
                    + (c.entry != nil ? "files, size and verification per the history" : "no data"), false))
            } else if c.source == .localCopy {
                list.append((name, "the backup disk is not connected — data from the report copy on this computer", false))
            }
            guard let r = c.record else { continue }
            for t in DayReport.unreadableTexts(r) { list.append((name, t, true)) }
            if r.generations.isEmpty { continue }
            let failed = r.failedFiles
            if !failed.isEmpty {
                list.append((name, "\(n(failed.count, "file")) failed: " + failed.prefix(5).joined(separator: ", ") + (failed.count > 5 ? ", …" : ""), true))
            }
            if !c.verified && !r.verifyPending {
                list.append((name, "not verified on all disks — do not format the card until the backup is verified", false))
            }
        }
        return list
    }

    // MARK: Saving

    /// `<date>_<time>_summary` of `now` (local time).
    public static func baseName(_ now: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd'_'HHmmss"
        return f.string(from: now) + "_summary"
    }

    /// `name`, `name_2`, `name_3` … — the first whose HTML and PDF are both free in `dir`.
    static func freeName(_ dir: String, _ name: String) -> String {
        var n = 1
        while true {
            let candidate = n == 1 ? name : "\(name)_\(n)"
            let fm = FileManager.default
            if !fm.fileExists(atPath: dir + "/" + candidate + ".html") && !fm.fileExists(atPath: dir + "/" + candidate + ".pdf") { return candidate }
            n += 1
        }
    }

    /// The volume of a target folder: its mount point, removable media, read-only. Tests replace it.
    public struct TargetVolume {
        public let root: String
        public let removable: Bool
        public let readOnly: Bool
        public init(root: String, removable: Bool, readOnly: Bool) { self.root = root; self.removable = removable; self.readOnly = readOnly }
    }

    static var volumeProbe: ((String) -> TargetVolume)?

    static func volumeOf(_ full: String) -> TargetVolume {
        if let probe = volumeProbe { return probe(full) }
        let v = try? URL(fileURLWithPath: full).resourceValues(forKeys: [.volumeURLKey, .volumeIsRemovableKey, .volumeIsReadOnlyKey])
        let root = v?.volume?.path ?? VolumeFacts.mount(of: full)?.mountPoint ?? "/"
        return TargetVolume(root: root, removable: v?.volumeIsRemovable == true, readOnly: v?.volumeIsReadOnly == true)
    }

    static func isDate(_ name: String) -> Bool {
        name.range(of: #"^\d{4}-\d{2}-\d{2}$"#, options: .regularExpression) != nil
    }

    /// Offload's output on a volume: a date folder at its root or one folder down (a backup disk, as for an automatic
    /// destination).
    static func hasOffloadOutput(_ root: String) -> Bool {
        let fm = FileManager.default
        let top = (try? fm.contentsOfDirectory(atPath: root)) ?? []
        if top.contains(where: isDate) { return true }
        return top.prefix(50).contains { !$0.hasPrefix(".") && ((try? fm.contentsOfDirectory(atPath: root + "/" + $0)) ?? []).contains(where: isDate) }
    }

    /// Throws (English, why) unless the summary may be written into `folder`, which the user picked: it exists; it is in
    /// no backup's card folder, also one still being copied or stopped (no folder from it up to the root holds `_REPORT`
    /// or `ascmhl` — a day folder's own `_REPORT` does not count — and it is not inside `<YYYY-MM-DD>/<card>/…`); it is not
    /// on a card being offloaded (`cards`); its volume is no camera card (unless Offload's output is on it), not
    /// removable media and not read-only. Reads only — never on the main thread.
    public static func checkTarget(_ folder: String, cards: [String] = []) throws {
        let full = DayReport.fullPath(folder)
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: full, isDirectory: &isDir), isDir.boolValue else {
            throw OffloadError.failed(Msg.summaryTargetMissing(full))
        }
        var dir = full
        while true {
            let name = (dir as NSString).lastPathComponent
            let parent = (dir as NSString).deletingLastPathComponent
            let cardFolder = !isDate(name) && (FileManager.default.fileExists(atPath: dir + "/" + Layout.reportDirName)
                                               || FileManager.default.fileExists(atPath: dir + "/" + MHL.dirName))
            if cardFolder || (dir != "/" && isDate((parent as NSString).lastPathComponent)) {
                throw OffloadError.failed(Msg.summaryTargetIsCard(full))
            }
            if dir == "/" || parent == dir { break }
            dir = parent
        }
        for card in cards {
            let c = DayReport.fullPath(card)
            if full.lowercased() == c.lowercased() || full.lowercased().hasPrefix(c.trimmingTrailingSlash().lowercased() + "/") {
                throw OffloadError.failed(Msg.summaryTargetIsSource(full))
            }
        }
        let volume = volumeOf(full)
        if volume.removable { throw OffloadError.failed(Msg.summaryTargetRemovable(full)) }
        if volume.readOnly { throw OffloadError.failed(Msg.summaryTargetReadOnly(full)) }
        if volume.root != "/" && Volumes.looksLikeCameraCard(volume.root) && !hasOffloadOutput(volume.root) {
            throw OffloadError.failed(Msg.summaryTargetCameraCard(full))
        }
    }

    /// Writes the summary into `<localRoot>/summary/<date>_<time>_summary.html/.pdf` and, with `extraFolder`, the same
    /// into that folder (checkTarget first); nothing is ever overwritten (`_2`, `_3` …). The PDF is best effort (logged)
    /// and printed first, so an HTML links to its PDF only when there is one. A failure in the picked folder is returned
    /// (extraError), the local copy stays. Throws when the local HTML cannot be written. `pdf` false: tests, no WebKit.
    public static func save(_ model: SummaryModel, localRoot: String = localRoot, extraFolder: String?, now: Date = Date(),
                            cards: [String] = [], pdf makePDF: Bool = true, log: ((String) -> Void)? = nil) throws -> SummaryFiles {
        let name = baseName(now)
        let dir = localRoot + "/" + folderName
        try IO.mkdirs(dir)
        let local = freeName(dir, name)
        var pdf: String? = dir + "/" + local + ".pdf"
        if makePDF {
            do {
                try writePDFNew(html(model, forPrint: true, created: now), to: pdf!)
            } catch {
                log?("summary report PDF \(pdf!): \(error)")   // the HTML is still made; the PDF is a convenience copy
                pdf = nil
            }
        } else {
            pdf = nil
        }
        let htmlPath = dir + "/" + local + ".html"
        try IO.writeFileNew(htmlPath, Data(html(model, pdfName: pdf != nil ? local + ".pdf" : nil, created: now).utf8))
        guard let extraFolder, !extraFolder.trimmingCharacters(in: .whitespaces).isEmpty else {
            return SummaryFiles(html: htmlPath, pdf: pdf, extraHTML: nil, extraPDF: nil, extraError: nil)
        }
        do {
            try checkTarget(extraFolder, cards: cards)
            let target = DayReport.fullPath(extraFolder)
            let free = freeName(target, name)
            var extraPDF: String?
            if let pdf {
                extraPDF = target + "/" + free + ".pdf"
                try IO.writeFileNew(extraPDF!, try Data(contentsOf: URL(fileURLWithPath: pdf)))
            }
            let extraHTML = target + "/" + free + ".html"
            try IO.writeFileNew(extraHTML, Data(html(model, pdfName: extraPDF != nil ? free + ".pdf" : nil, created: now).utf8))
            return SummaryFiles(html: htmlPath, pdf: pdf, extraHTML: extraHTML, extraPDF: extraPDF, extraError: nil)
        } catch {
            log?("summary report on \(extraFolder): \(error)")
            return SummaryFiles(html: htmlPath, pdf: pdf, extraHTML: nil, extraPDF: nil, extraError: "\(error)")
        }
    }

    /// The PDF printed into a temporary folder, then created at `path` only when the name is free (never over a file).
    static func writePDFNew(_ html: String, to path: String) throws {
        let work = NSTemporaryDirectory() + "offload-summary-" + UUID().uuidString.prefix(8)
        try IO.mkdirs(work)
        defer { try? FileManager.default.removeItem(atPath: work) }
        let printed = work + "/" + (path as NSString).lastPathComponent
        try PDFReport.writeHTML(html, to: printed)
        try IO.writeFileNew(path, try Data(contentsOf: URL(fileURLWithPath: printed)))
    }
}
