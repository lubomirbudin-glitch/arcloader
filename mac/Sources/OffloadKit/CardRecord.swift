import Foundation

/// A file as the card folder's reports record it: the newest generation that lists it.
public struct RecordedFile {
    public let file: OffloadReport.FileEntry
    public let generation: Int
    public let report: OffloadReport
}

/// What the reports in `<card>/_REPORT` say about a card folder — every generation, reports from Windows and older Macs
/// included. The truth about a backup (spec D.22.1): appends, repairs, the day report, clip lists and history read it.
public struct CardRecord {
    public static let mainReport = "offload_report.json"

    public var cardPath: String
    public var generations: [(generation: Int, report: OffloadReport)] = []
    /// Newest record per path; key = path lower-cased (APFS/NTFS/exFAT compare names in any case).
    public var files: [String: RecordedFile] = [:]
    /// Every clip of every generation, in generation order; a generation without clip analysis gets the clips
    /// ClipDetector finds in its files (no metadata) and never replaces a clip described before.
    public var clips: [ClipEntry] = []
    /// `_REPORT/offload_report.json` is not there.
    public var reportMissing = false
    /// Why the main report (or the `_REPORT` folder) could not be read; nil when it was read or is missing.
    public var error: String?
    /// Append reports that could not be read (file name: reason) — appends and repairs refuse then.
    public var problems: [String] = []
    /// The highest generation any report file in `_REPORT` stands for — also one an interrupted append left without JSON.
    public var highestReportNumber = 0
    /// The serial of the volume the folder is on now (R12), nil when unknown.
    public var volumeSerial: String?
    /// The newest generation's result is not written everywhere (verifyIncomplete here, or verifyPending on another
    /// destination that is connected).
    public var incomplete = false

    public init(cardPath: String) { self.cardPath = cardPath }

    public var folderName: String { (cardPath.trimmingTrailingSlash() as NSString).lastPathComponent }
    public var started: Date? { generations.map(\.report.started).min() }
    public var finished: Date? { generations.map(\.report.finished).max() }
    /// The first generation's project that names one.
    public var project: String? {
        generations.compactMap { $0.report.project?.trimmingCharacters(in: .whitespaces) }.first { !$0.isEmpty }
    }
    /// A generation's verification with the proxies has not ended.
    public var verifyPending: Bool { generations.contains { $0.report.verifyPending == true } }
    /// Paths whose newest record failed (the card or a destination), sorted.
    public var failedFiles: [String] {
        files.values.filter { $0.report.isFailed($0.file) }.map(\.file.path).sorted()
    }
    /// Every file's newest record was read back here (readBack). A folder whose reports list no file: every report
    /// was verified.
    public var verified: Bool {
        guard !generations.isEmpty, problems.isEmpty, !incomplete else { return false }
        if files.isEmpty { return generations.allSatisfy { $0.report.verified } }
        return files.values.allSatisfy { CardRecord.readBack($0, cardPath: cardPath, serial: volumeSerial) }
    }

    static let appendReport = try! NSRegularExpression(pattern: #"^offload_report_(\d{4})\.json$"#, options: .caseInsensitive)
    static let numberedFile = try! NSRegularExpression(pattern: #"_report_(\d{4})\.(json|txt|html|pdf)$"#, options: .caseInsensitive)

    static func number(_ re: NSRegularExpression, _ name: String) -> Int? {
        guard let m = re.firstMatch(in: name, range: NSRange(name.startIndex..., in: name)),
              let r = Range(m.range(at: 1), in: name) else { return nil }
        return Int(name[r])
    }

    /// `maxGeneration`: later generations are left out (resume, task 69). `serial`: the folder's volume serial for
    /// tests; nil = read it (VolumeFacts), .some(nil) = unknown.
    public static func load(_ cardPath: String, maxGeneration: Int? = nil, serial: String?? = nil) -> CardRecord {
        var rec = CardRecord(cardPath: cardPath)
        let dir = cardPath + "/" + Layout.reportDirName
        let main = dir + "/" + mainReport
        guard FileManager.default.fileExists(atPath: main) else {
            rec.reportMissing = true
            return rec
        }
        do {
            let r = try readChecked(main)
            rec.generations.append((r.generationNumber, r))
        } catch {
            rec.error = "\(error)"
            return rec
        }
        var highest = rec.generations[0].generation
        let names: [String]
        do {
            names = try FileManager.default.contentsOfDirectory(atPath: dir)
        } catch {
            // The appends cannot be listed: what the folder holds is unknown.
            rec.generations = []
            rec.error = "\(error)"
            return rec
        }
        let limit = maxGeneration ?? Int.max
        for name in names.sorted() {
            if let n = number(numberedFile, name), n <= limit { highest = max(highest, n) }
            guard let n = number(appendReport, name), n <= limit else { continue }
            do {
                let r = try readChecked(dir + "/" + name)
                let g = r.generation ?? n
                rec.generations.append((g, r))
                highest = max(highest, g)
            } catch {
                rec.problems.append("\(name): \(error)")
            }
        }
        rec.generations.sort { $0.generation < $1.generation }
        rec.highestReportNumber = highest
        var clipAt: [String: Int] = [:]
        for g in rec.generations {
            for f in g.report.files {
                rec.files[f.path.lowercased()] = RecordedFile(file: f, generation: g.generation, report: g.report)
            }
            let analysed = !g.report.clips.isEmpty
            let clips = analysed ? g.report.clips
                : ClipDetector.detect(g.report.files.map { SourceFile(relPath: $0.path, size: $0.size, modified: $0.modified) })
                    .map { ClipEntry(clip: $0) }
            for c in clips {
                let key = c.clip.path.lowercased()
                if let i = clipAt[key] {
                    // A generation without analysis (a repair rewrites a clip's file) never replaces a described clip.
                    if analysed || rec.clips[i].info == nil { rec.clips[i] = c }
                } else {
                    clipAt[key] = rec.clips.count
                    rec.clips.append(c)
                }
            }
        }
        rec.volumeSerial = serial ?? VolumeFacts.volumeSerialText(cardPath)
        rec.incomplete = incompleteElsewhere(cardPath, rec.generations)
        return rec
    }

    /// A report whose lists hold together (a hand-edited list must not crash later).
    static func readChecked(_ path: String) throws -> OffloadReport {
        let r = try OffloadReport.read(path)
        if r.files.contains(where: { $0.destinations.isEmpty && !r.destinations.isEmpty }) {
            throw OffloadError.failed("the list of files or destinations is missing")
        }
        return r
    }

    /// The newest generation says verifyIncomplete here, or its report on another destination that is connected (only
    /// read) still says verifyPending for the same run.
    static func incompleteElsewhere(_ cardPath: String, _ gens: [(generation: Int, report: OffloadReport)]) -> Bool {
        guard let newest = gens.max(by: { $0.generation < $1.generation })?.report else { return false }
        if newest.verifyIncomplete == true { return true }
        let me = normal(cardPath)
        for d in newest.destinations where normal(d.cardPath) != me && d.cardPath.hasPrefix("/") {
            let json = d.cardPath + "/" + Layout.reportDirName + "/" + OffloadReport.jsonName(newest.generationNumber)
            guard FileManager.default.fileExists(atPath: json) else { continue }
            if let other = try? OffloadReport.read(json), other.verifyPending == true, other.started == newest.started { return true }
        }
        return false
    }

    static func normal(_ p: String) -> String {
        (p.hasPrefix("/") ? URL(fileURLWithPath: p).standardizedFileURL.path : p).trimmingTrailingSlash().lowercased()
    }

    /// THE one definition of "verified for this destination folder" (spec D.22.6): the newest record of the copy in
    /// `cardPath` says it was read back — or it failed in a copy that was read back (a failure is a failure, not
    /// "unverified"). "written", or anything in a report whose verification is pending or not written everywhere, is
    /// not. Column = columnOf; when there is none (a report from Windows, a disk under another name) every column must
    /// say so — never falsely green.
    public static func readBack(_ rec: RecordedFile, cardPath: String, serial: String?) -> Bool {
        let r = rec.report
        if r.verifyPending == true || r.verifyIncomplete == true { return false }
        let column = columnOf(r, cardPath: cardPath, serial: serial)
        let states: [String] = column >= 0
            ? [column < rec.file.destinations.count ? rec.file.destinations[column] : "pending"]
            : (rec.file.destinations.isEmpty ? ["pending"] : rec.file.destinations)
        return states.allSatisfy { $0 == "verified" || (r.verified && $0 != "written" && $0 != "pending") }
    }

    /// The column of `r` that is this folder, or -1. A report that knows its destinations' volume serials is matched by
    /// the disk (the same serial and the same path under its root, exactly one); when this folder's serial is unknown,
    /// -1 (never a guess by the path). Older reports (Mac ≤ 0.2, Windows < 0.6): by the full path.
    public static func columnOf(_ r: OffloadReport, cardPath: String, serial: String?) -> Int {
        let knowsSerials = r.destinations.contains { $0.volumeSerial != nil }
        if knowsSerials {
            guard let serial else { return -1 }
            let mine = VolumeFacts.underRoot(cardPath).lowercased()
            let hits = r.destinations.indices.filter {
                r.destinations[$0].volumeSerial == serial && VolumeFacts.underRoot(r.destinations[$0].cardPath).lowercased() == mine
            }
            return hits.count == 1 ? hits[0] : -1
        }
        let me = normal(cardPath)
        return r.destinations.firstIndex { normal($0.cardPath) == me } ?? -1
    }

    /// Which column belongs to `cardPath` for planning (never for "verified"): the same path, else the only one, else
    /// the same position.
    public static func destinationIndex(_ r: OffloadReport, cardPath: String, position: Int) -> Int {
        let me = normal(cardPath)
        if let i = r.destinations.firstIndex(where: { normal($0.cardPath) == me }) { return i }
        return r.destinations.count == 1 ? 0 : min(max(position, 0), max(0, r.destinations.count - 1))
    }

    /// The state the newest record gives the copy in `cardPath` ("verified", "written", "failed", "pending").
    public static func stateOf(_ rec: RecordedFile, cardPath: String, position: Int) -> String {
        let c = destinationIndex(rec.report, cardPath: cardPath, position: position)
        return c < rec.file.destinations.count ? rec.file.destinations[c] : "pending"
    }
}

/// The whole backup as its card folders' reports record it now: failed paths, and paths good everywhere but not read
/// back somewhere (CardRecord.readBack).
public struct BackupState: Equatable {
    public let failed: Int
    public let unverified: Int

    public init(failed: Int, unverified: Int) {
        self.failed = failed
        self.unverified = unverified
    }

    public static func of(_ cardPaths: [String]) -> BackupState {
        var failed = Set<String>(), unverified = Set<String>()
        for p in cardPaths {
            let rec = CardRecord.load(p)
            failed.formUnion(rec.failedFiles.map { $0.lowercased() })
            for (key, f) in rec.files where !CardRecord.readBack(f, cardPath: p, serial: rec.volumeSerial) { unverified.insert(key) }
        }
        return BackupState(failed: failed.count, unverified: unverified.subtracting(failed).count)
    }
}
