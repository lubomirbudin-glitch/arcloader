import Foundation

/// One folder of a day as the day report shows it — sums over every generation of its reports (Windows `DayCard`).
public struct DayCard {
    public let record: CardRecord
    /// The folder has `_REPORT/` or `ascmhl/` — an offload card (also one whose copy stopped). Any other folder (a
    /// hand-copied SOUND) gets a row, but is not counted as a card and is no problem.
    public let isCard: Bool

    public init(record: CardRecord, isCard: Bool = true) {
        self.record = record
        self.isCard = isCard
    }

    public var name: String { record.folderName }
    /// The folder has no `_REPORT/offload_report.json`: a copy in progress, or one that was stopped.
    public var noReport: Bool { record.reportMissing }
    public var unreadable: String? { DayReport.unreadableTexts(record).first }
    var videoClips: [ClipEntry] { record.clips.filter { !$0.clip.isCameraProxy } }
    public var clipCount: Int { videoClips.count }
    public var seconds: Double { videoClips.reduce(0) { $0 + ($1.info?.duration ?? 0) } }
    public var cameras: [String] {
        var seen = Set<String>(), out: [String] = []
        for c in videoClips {
            guard let cam = c.info?.camera?.trimmingCharacters(in: .whitespaces), !cam.isEmpty, seen.insert(cam.lowercased()).inserted
            else { continue }
            out.append(cam)
        }
        return out.sorted()
    }
    /// The clips' frame rates as the card report shows them: "35 fps · playback 24", "25" — each once, in clip order.
    public var frameRates: [String] {
        var out: [String] = []
        for c in videoClips { if let f = c.info?.fpsSummary, !f.isEmpty, !out.contains(f) { out.append(f) } }
        return out
    }
    /// LUT name → clips (no LUT and switched-off LUT are left out), in first-use order.
    public var luts: [(name: String, clips: Int)] {
        var out: [(name: String, clips: Int)] = []
        for c in videoClips {
            guard let l = c.lut, l.source != "none" else { continue }
            if let i = out.firstIndex(where: { $0.name == l.name }) { out[i].clips += 1 } else { out.append((l.name, 1)) }
        }
        return out
    }
    public var proxiesDone: Int { record.clips.filter { $0.proxyStatus == .done }.count }
    public var proxiesFailed: Int { record.clips.filter { $0.proxyStatus == .failed }.count }
    /// Clips without a proxy because the original is lighter (LighterSource, task 54 / Windows 0.5.1).
    public var proxiesLighter: Int { record.clips.filter(\.originalLighter).count }
    public var totalFiles: Int { record.files.count }
    public var totalBytes: Int64 { record.files.values.reduce(0) { $0 + $1.file.size } }
}

/// A day on one destination: every folder in `<root>/<date>/`.
public struct DayModel {
    public let dayPath: String
    /// Every folder of the day in Finder order — the table's rows.
    public let folders: [DayCard]

    /// The date folder's name ("2026-10-06").
    public var date: String { (dayPath as NSString).lastPathComponent }
    /// The destination the day is on.
    public var root: String { (dayPath as NSString).deletingLastPathComponent }
    /// The cards among the folders: what the tiles, "N / M" and the badge count.
    public var cards: [DayCard] { folders.filter(\.isCard) }
    public var verifiedCards: Int { cards.filter { $0.record.verified && $0.record.failedFiles.isEmpty }.count }
    /// The project in the header: only when every card with a readable report names the same one (any case).
    public var project: String? {
        let read = cards.filter { !$0.record.generations.isEmpty }.map(\.record.project)
        guard let first = read.first, let p = first, read.allSatisfy({ $0?.lowercased() == p.lowercased() }) else { return nil }
        return p
    }
}

/// The day report (FORMATY „Denní report“, Windows `DayReport`): `<root>/<date>/_REPORT/<date>_day_report.html/.pdf`.
/// Derived from the cards' reports — written whole every time, never anything else in that folder (verify reports live
/// there too), never anything in a card folder. Same English texts as Windows. The day clip lists (CSV/ALE) are not
/// on the Mac yet (task 52).
public enum DayReport {
    public static func htmlName(_ date: String) -> String { "\(date)_day_report.html" }
    public static func pdfName(_ date: String) -> String { "\(date)_day_report.pdf" }

    static let noReportText = "no report (running, or it was interrupted)"

    /// Every subfolder of the day but `_REPORT` and hidden ones. Reads only.
    static func foldersOf(_ dayPath: String) -> [String] {
        let fm = FileManager.default
        let names = (try? fm.contentsOfDirectory(atPath: dayPath)) ?? []
        return names.filter { n in
            guard !n.hasPrefix("."), n.lowercased() != Layout.reportDirName.lowercased() else { return false }
            var dir: ObjCBool = false
            return fm.fileExists(atPath: dayPath + "/" + n, isDirectory: &dir) && dir.boolValue
        }
        .sorted { $0.localizedStandardCompare($1) == .orderedAscending }
        .map { dayPath + "/" + $0 }
    }

    static func hasReport(_ folder: String) -> Bool {
        FileManager.default.fileExists(atPath: folder + "/" + Layout.reportDirName + "/" + CardRecord.mainReport)
    }

    /// A folder an offload made: `_REPORT/` or `ascmhl/` in it.
    static func isCard(_ folder: String) -> Bool {
        let fm = FileManager.default
        return fm.fileExists(atPath: folder + "/" + Layout.reportDirName) || fm.fileExists(atPath: folder + "/" + MHL.dirName)
    }

    /// The absolute, standardized path without a trailing "/".
    public static func fullPath(_ path: String) -> String {
        let p = URL(fileURLWithPath: path).standardizedFileURL.path
        return p == "/" ? p : p.trimmingTrailingSlash()
    }

    /// Throws unless `dayPath` may get a day report: it exists, it is no volume root, it has at least one card folder
    /// with its report — so a destination root, a card folder or any other folder never gets a `_REPORT` — and its
    /// `_REPORT` is not itself a card folder (the day files would land in that card).
    public static func checkDay(_ path: String) throws {
        let dayPath = fullPath(path)
        var dir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: dayPath, isDirectory: &dir), dir.boolValue else {
            throw OffloadError.failed(Msg.dayNotFound(dayPath))
        }
        if isVolumeRoot(dayPath) { throw OffloadError.failed(Msg.dayIsVolumeRoot(dayPath)) }
        let report = dayPath + "/" + Layout.reportDirName
        if hasReport(report) || FileManager.default.fileExists(atPath: report + "/" + MHL.dirName) {
            throw OffloadError.failed(Msg.dayReportFolderIsCard(dayPath))
        }
        if !foldersOf(dayPath).contains(where: hasReport) { throw OffloadError.failed(Msg.notADayFolder(dayPath)) }
    }

    static func isVolumeRoot(_ full: String) -> Bool {
        if full == "/" { return true }
        guard let m = VolumeFacts.mount(of: full) else { return false }
        return VolumeFacts.realPath(m.mountPoint).trimmingTrailingSlash() == VolumeFacts.realPath(full).trimmingTrailingSlash()
    }

    /// The folders of `dayPath`, in Finder order. Reads only.
    public static func load(_ dayPath: String) -> DayModel {
        let full = fullPath(dayPath)
        return DayModel(dayPath: full, folders: foldersOf(full).map { DayCard(record: CardRecord.load($0), isCard: isCard($0)) })
    }

    /// The reports of a card that cannot be read, one sentence each (the reason in parentheses).
    static func unreadableTexts(_ r: CardRecord) -> [String] {
        (r.error.map { ["cannot read the report (\($0))"] } ?? []) + r.problems.map { "cannot read the report of an append (\($0))" }
    }

    static func time(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "HH:mm"
        return f.string(from: d)
    }

    static func files(_ n: Int) -> String { "\(n) \(Texts.plural(n, "file", "files"))" }

    static func stat(_ value: String, _ label: String) -> String { "<div class=\"stat\"><b>\(value)</b><span>\(label)</span></div>\n" }

    /// Self-contained HTML, A4 landscape when printed; no thumbnails.
    public static func html(_ day: DayModel, forPrint: Bool = false) -> String {
        let e = HTMLReport.esc
        let cards = day.cards
        let verified = day.verifiedCards
        let (badge, badgeClass) = verified == cards.count && !cards.isEmpty ? ("ALL VERIFIED", "ok") : ("NEEDS ATTENTION", "warn")
        var h = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Day report \(e(day.date))</title>
        <style>
        \(HTMLReport.styles(forPrint: forPrint))
        </style></head><body><main>
        <header><h1>Day report — \(e(day.date))</h1><span class="badge \(badgeClass)">\(badge)</span>
        <span class="sub">Destination: <span class="mono">\(e(day.root))</span></span>
        """
        if let p = day.project { h += "<span class=\"sub\">Project: \(e(p))</span>" }
        h += "</header>\n<div class=\"stats\">\n"
        h += stat(String(cards.count), Texts.plural(cards.count, "card", "cards"))
        h += stat(String(cards.reduce(0) { $0 + $1.totalFiles }), "files")
        h += stat(formatBytes(cards.reduce(0) { $0 + $1.totalBytes }), "data")
        h += stat(String(cards.reduce(0) { $0 + $1.clipCount }), "clips")
        h += stat(HTMLReport.duration(cards.reduce(0) { $0 + $1.seconds }), "total duration")
        h += stat("\(verified) / \(cards.count)", "verified cards")
        h += "</div>\n\n<h2>Cards</h2><div class=\"panel\"><table>\n"
        h += "<tr><th>Card</th><th>Time</th><th class=\"num\">Files</th><th class=\"num\">Data</th><th class=\"num\">Clips</th><th class=\"num\">Duration</th><th>FPS</th>"
        h += "<th>Cameras</th><th>Verification</th><th class=\"num\">Errors</th><th>Proxies</th><th>LUTs</th><th class=\"num\">Appends</th></tr>\n"
        for c in day.folders {
            let r = c.record
            if !c.isCard {
                h += "<tr><td>\(e(c.name))</td><td colspan=\"12\" class=\"muted\">other folder (no report)</td></tr>\n"
                continue
            }
            if c.noReport || r.generations.isEmpty {
                h += "<tr><td>\(e(c.name))</td><td colspan=\"12\" class=\"warn\">\(c.noReport ? noReportText : "cannot read the report")</td></tr>\n"
                continue
            }
            let failed = r.failedFiles.count
            // Read back, but a file failed: not green. A verification with the proxies that has not ended says so.
            let (vClass, vText) = r.verifyPending ? ("warn", "VERIFYING") : r.incomplete ? ("warn", "UNFINISHED")
                : !r.verified ? ("warn", "NOT VERIFIED") : failed > 0 ? ("warn", "verified, with errors") : ("ok", "verified")
            var proxies = c.proxiesDone + c.proxiesFailed == 0 ? "—" : "\(c.proxiesDone) / \(c.proxiesFailed)"
            if c.proxiesLighter > 0 { proxies += " · \(c.proxiesLighter) no proxy" }
            let lutNames = c.luts.isEmpty ? "—" : c.luts.map(\.name).joined(separator: ", ")
            let generations = r.generations.count > 1 ? String(r.generations.count) : "—"
            h += "<tr><td>\(e(c.name))</td><td>\(time(r.started!))–\(time(r.finished!))</td>"
            h += "<td class=\"num\">\(c.totalFiles)</td><td class=\"num\">\(formatBytes(c.totalBytes))</td><td class=\"num\">\(c.clipCount)</td>"
            h += "<td class=\"num\">\(c.seconds > 0 ? HTMLReport.duration(c.seconds) : "—")</td><td class=\"wrap\">\(e(c.frameRates.isEmpty ? "—" : c.frameRates.joined(separator: ", ")))</td>"
            h += "<td class=\"wrap\">\(e(c.cameras.isEmpty ? "—" : c.cameras.joined(separator: ", ")))</td>"
            h += "<td class=\"\(vClass)\">\(vText)</td><td class=\"num \(failed > 0 ? "bad" : "muted")\">\(failed)</td>"
            h += "<td>\(proxies)</td><td class=\"wrap\">\(e(lutNames))</td><td class=\"num\">\(generations)</td></tr>\n"
        }
        h += "</table></div>\n"
        h += "<p class=\"sub\">Proxies: done / failed (· no proxy = the original is lighter than a proxy). Appends: the number of ASC MHL generations when the card was appended to.</p>\n"

        let problems = self.problems(day)
        if !problems.isEmpty {
            h += "<h2 class=\"warn\">Problems (\(problems.count))</h2><div class=\"panel\"><table>\n"
            for p in problems { h += "<tr><td>\(e(p.card))</td><td class=\"\(p.bad ? "err" : "warn wrap")\">\(e(p.text))</td></tr>\n" }
            h += "</table></div>\n"
        }

        var luts: [(name: String, clips: Int)] = []
        for c in cards {
            for l in c.luts {
                if let i = luts.firstIndex(where: { $0.name == l.name }) { luts[i].clips += l.clips } else { luts.append(l) }
            }
        }
        luts.sort { $0.clips != $1.clips ? $0.clips > $1.clips : $0.name < $1.name }
        if !luts.isEmpty {
            h += "<h2>LUTs</h2><div class=\"panel\"><table>\n<tr><th>LUT</th><th class=\"num\">Clips</th></tr>\n"
            for l in luts { h += "<tr><td>\(e(l.name))</td><td class=\"num\">\(l.clips)</td></tr>\n" }
            h += "</table></div>\n"
        }
        h += "<footer>Offload \(e(OffloadVersion.string)) · day report · \(HTMLReport.dateTime(Date()))</footer></main></body></html>\n"
        return h
    }

    /// Unreadable reports, card folders without one, failed files (count + the first five), cards not verified. A
    /// folder that is no card is none. `bad`: red.
    public static func problems(_ day: DayModel) -> [(card: String, text: String, bad: Bool)] {
        var list: [(card: String, text: String, bad: Bool)] = []
        for c in day.cards {
            if c.noReport {
                list.append((c.name, noReportText, false))
                continue
            }
            let unreadable = unreadableTexts(c.record)
            for t in unreadable { list.append((c.name, t, true)) }
            if c.record.generations.isEmpty { continue }
            let failed = c.record.failedFiles
            if !failed.isEmpty {
                list.append((c.name, "\(files(failed.count)) failed: " + failed.prefix(5).joined(separator: ", ") + (failed.count > 5 ? ", …" : ""), true))
            }
            if c.record.verifyPending && unreadable.isEmpty {
                list.append((c.name, "the verification with the proxies is still running, or was interrupted — do not format the card yet", false))
            } else if c.record.incomplete && unreadable.isEmpty {
                list.append((c.name, "the verification result is not written on all destinations — do not format the card yet; finish the backup (Finish interrupted backup)", false))
            } else if !c.record.verified && unreadable.isEmpty {
                list.append((c.name, "not verified — the copy was not read back", false))
            }
        }
        return list
    }

    /// Writes the HTML and the PDF (best effort, logged) into `<day>/_REPORT`, each replaced whole. Returns the HTML
    /// path. Throws when the folder is no day (checkDay) or the HTML cannot be written. `pdf` false: tests, no WebKit.
    @discardableResult
    public static func write(_ path: String, pdf: Bool = true, log: ((String) -> Void)? = nil) throws -> String {
        let dayPath = fullPath(path)
        try checkDay(dayPath)
        let day = load(dayPath)
        let dir = dayPath + "/" + Layout.reportDirName
        try IO.mkdirs(dir)
        let html = dir + "/" + htmlName(day.date)
        try IO.writeFileDurably(html, Data(self.html(day).utf8))
        if pdf {
            do {
                try PDFReport.writeHTML(self.html(day, forPrint: true), to: dir + "/" + pdfName(day.date))
            } catch {
                log?("day report PDF \(dir): \(error)")   // the HTML is there; the PDF is a convenience copy
            }
        }
        return html
    }
}

/// One writer per day (FORMATY „Denní report“): the day's lock (`Locks.day`, flock — does not exclude Windows writing
/// the same day; the last write wins, the files are derived). Never fails a job: a problem is only logged.
public enum DayReportService {
    /// How long a job waits for a busy day before it skips the day report.
    public static let jobWait: TimeInterval = 5
    /// The PDF after a job — off inside XCTest (every test job would print one with WebKit; DayReportTests prints one
    /// on purpose), like LocalReports.enabled.
    public static var jobPDF = NSClassFromString("XCTestCase") == nil

    /// The day folder of a card folder or a day folder: a folder with `_REPORT/offload_report.json` is a card, its parent
    /// is the day; anything else is taken as the day itself.
    public static func dayFolder(of path: String) -> String {
        let full = DayReport.fullPath(path)
        return DayReport.hasReport(full) ? (full as NSString).deletingLastPathComponent : full
    }

    /// Under the day's lock (waiting at most `wait`): writes the day report of `path` (a day or a card folder).
    @discardableResult
    public static func regenerate(_ path: String, wait: TimeInterval = jobWait, pdf: Bool = true,
                                  log: ((String) -> Void)? = nil) throws -> String {
        let day = dayFolder(of: path)
        guard let held = FileLock.lock(Locks.day(day), timeout: wait) else { throw OffloadError.failed(Msg.dayReportBusy(day)) }
        defer { held.release() }
        return try DayReport.write(day, pdf: pdf, log: log)
    }

    /// After a job: the day report of every card folder's day (one per day folder). Failures go to `log` only.
    public static func afterJob(cardPaths: [String], pdf: Bool = true, log: ((String) -> Void)? = nil) -> [String] {
        var written: [String] = [], seen = Set<String>()
        for p in cardPaths {
            let day = (DayReport.fullPath(p) as NSString).deletingLastPathComponent
            guard seen.insert(day).inserted, FileManager.default.fileExists(atPath: day) else { continue }
            do {
                written.append(try regenerate(day, pdf: pdf, log: log))
            } catch {
                log?("day report \(day): \(error)")
            }
        }
        return written
    }
}
