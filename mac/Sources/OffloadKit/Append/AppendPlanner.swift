import Foundation

/// What an append copies (FORMATY „Doplnění“; Windows spec 2.2): only the card's new files, into the same card folders,
/// as ASC MHL generation `generation`.
public struct AppendPlan {
    public let source: String
    public let cardPaths: [String]
    public let destRoots: [String]
    public let date: String
    public let cardName: String
    /// The generations already there + 1 (the same on every destination).
    public let generation: Int
    /// The whole card (history learns it as it is now).
    public let fullScan: SourceScan
    public let newFiles: [SourceFile]
    /// Folders the new files need (every level), sorted.
    public let newDirs: [String]
    /// Files of the earlier backup that are good but were never read back: the card is not safe to format after it.
    public var unverifiedEarlier = 0
    /// The checksums of the earlier backup (its generation 1 report), not the current choice.
    public var hashAlgorithms = ["xxh64"]
    /// The project of the earlier backup, not the one shown in the app.
    public var project: String?
    /// Task 69: this append completes a generation an earlier run never committed.
    public var resuming = false
    /// Task 69: card files a resumed generation cannot take (shot since).
    public var later = 0
}

/// The truth is the content of the card folders (every report generation and the files themselves), never history.
/// Reads only — the card and the destinations — and refuses everything with `OffloadError.preflight` before anything
/// is written.
public enum AppendPlanner {
    struct Backup {
        let paths: [String]
        let roots: [String]
        let date: String
        let cardName: String
        let records: [CardRecord]
        let nextGeneration: Int
    }

    /// "RAID" for "/Volumes/RAID/Zaloha/2026-10-08/A001" (the volume name); "/" when unknown.
    public static func disk(_ cardPath: String) -> String {
        var p = cardPath
        while !p.isEmpty && p != "/" && !FileManager.default.fileExists(atPath: p) { p = (p as NSString).deletingLastPathComponent }
        if let name = (try? URL(fileURLWithPath: p).resourceValues(forKeys: [.volumeNameKey]))?.volumeName, !name.isEmpty { return name }
        let parts = cardPath.split(separator: "/")
        return cardPath.hasPrefix("/Volumes/") && parts.count >= 2 ? String(parts[1]) : "/"
    }

    /// "10-06 09:14" in local time (as Windows).
    static func when(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "MM-dd HH:mm"
        return f.string(from: d)
    }

    /// "CLIP/A001C003.mov: different size or date (card 1.2 GB · 10-06 09:14, RAID 1.1 GB · 10-06 09:10)"
    public static func conflict(_ card: SourceFile, _ disk: String, size: Int64, modified: Date) -> String {
        // A difference smaller than the rounding ("100 KB" on both sides) shows the exact bytes instead.
        let (a, b) = formatBytesShort(card.size) == formatBytesShort(size) && card.size != size
            ? ("\(card.size) B", "\(size) B") : (formatBytesShort(card.size), formatBytesShort(size))
        return "\(card.relPath): different size or date (card \(a) · \(when(card.modifiedUtc)), \(disk) \(b) · \(when(modified)))"
    }

    /// Step 0 and 1: one backup (`<root>/<date>/<card>`, the same date and name everywhere), every folder there, with
    /// readable reports and an intact ASC MHL chain of the same length, the last report generation = the last MHL one.
    /// `unfinishedGeneration` (task 69): the generation a resume completes — only those before it are checked.
    static func checkBackup(_ cardPaths: [String], unfinishedGeneration: Int? = nil) throws -> Backup {
        guard !cardPaths.isEmpty else { throw OffloadError.preflight(Msg.noDestination, code: .noDestination) }
        let paths = cardPaths.map { URL(fileURLWithPath: $0).standardizedFileURL.path.trimmingTrailingSlash() }
        var roots: [String] = []
        let date = ((paths[0] as NSString).deletingLastPathComponent as NSString).lastPathComponent
        let name = (paths[0] as NSString).lastPathComponent
        for p in paths {
            let parent = (p as NSString).deletingLastPathComponent, root = (parent as NSString).deletingLastPathComponent
            guard parent != p, root != parent, !root.isEmpty, parent != "/" else {
                throw OffloadError.preflight(Msg.appendNotACardFolder(p), code: .appendUnavailable)
            }
            guard (parent as NSString).lastPathComponent.lowercased() == date.lowercased(),
                  (p as NSString).lastPathComponent.lowercased() == name.lowercased() else {
                throw OffloadError.preflight(Msg.appendDifferentFolders(paths), code: .appendDiverged)
            }
            roots.append(root)
        }
        if unfinishedGeneration == 1 {
            for p in paths where !FileManager.default.fileExists(atPath: p) {
                throw OffloadError.preflight(Msg.appendUnavailable(p), code: .appendUnavailable)
            }
            return Backup(paths: paths, roots: roots, date: date, cardName: name, records: paths.map { CardRecord(cardPath: $0) }, nextGeneration: 1)
        }
        var records: [CardRecord] = [], gens: [Int] = []
        for p in paths {
            guard FileManager.default.fileExists(atPath: p) else { throw OffloadError.preflight(Msg.appendUnavailable(p), code: .appendUnavailable) }
            let rec = CardRecord.load(p, maxGeneration: unfinishedGeneration.map { $0 - 1 })
            if rec.reportMissing { throw OffloadError.preflight(Msg.appendNoReport(p), code: .appendNoReport) }
            if let e = rec.error { throw OffloadError.preflight(Msg.appendBadReport(p, e), code: .appendNoReport) }
            if let first = rec.problems.first { throw OffloadError.preflight(Msg.appendBadReport(p, first), code: .appendNoReport) }
            guard let mhl = MHLReader.read(cardPath: p), let last = mhl.chain.map(\.seq).max() else {
                throw OffloadError.preflight(Msg.appendNoMhl(p), code: .appendNoMhl)
            }
            // A changed or missing generation, or one outside the chain: the history the next generation would extend is
            // not what was written — have it checked first.
            if !mhl.chainProblems.isEmpty { throw OffloadError.preflight(Msg.appendMhlDamaged(p), code: .appendMhlDamaged) }
            records.append(rec)
            gens.append(min(last, (unfinishedGeneration ?? Int.max) - 1))
        }
        if Set(gens).count > 1 {
            let details = paths.enumerated().map { i, p in
                i == 0 ? "\(disk(p)) has \(gens[i]) \(plural(gens[i], "MHL generation", "MHL generations"))" : "\(disk(p)) \(gens[i])"
            }
            throw OffloadError.preflight(Msg.appendDiverged(details.joined(separator: ", ")), code: .appendDiverged)
        }
        for (i, p) in paths.enumerated() where records[i].highestReportNumber != gens[i] {
            // An append whose MHL was never written (reports without a generation), or a generation without reports.
            throw OffloadError.preflight(Msg.appendReportsMismatch(p, report: records[i].highestReportNumber, mhl: gens[i]), code: .appendDiverged)
        }
        return Backup(paths: paths, roots: roots, date: date, cardName: name, records: records, nextGeneration: gens[0] + 1)
    }

    /// `sourceFilesystem`: tests pretend a FAT card; nil = the card's own. `destFilesystem`: the same for a card folder.
    public static func plan(_ source: String, cardPaths: [String], sourceFilesystem: String? = nil,
                            destFilesystem: ((String) -> String)? = nil, isCancelled: () -> Bool = { false }) throws -> AppendPlan {
        let b = try checkBackup(cardPaths)
        // Step 2: the card, compared with every backup — a known file must be the same on the card, in the report and on
        // the disk.
        let scan = try Scanner.scan(source, isCancelled: isCancelled)
        let cardLocal = FileTimes.storesLocalTime(sourceFilesystem ?? IO.filesystemType(source))
        // A FAT destination shifts its own file times after a change of time zone, whatever the card is.
        let diskLocal = b.paths.map { cardLocal || FileTimes.storesLocalTime(destFilesystem?($0) ?? IO.filesystemType($0)) }
        let disks = b.paths.map(disk)
        var newFiles: [SourceFile] = [], conflicts: [String] = []
        var unverified = 0, known = 0
        for f in scan.files {
            if isCancelled() { throw CancellationError() }
            var present = 0, goodInRecord = 0, unverifiedFile = false
            for (d, p) in b.paths.enumerated() {
                let full = p + "/" + f.relPath
                let attrs = try? FileManager.default.attributesOfItem(atPath: full)
                let exists = attrs != nil
                if exists { present += 1 }
                guard let rec = b.records[d].files[f.relPath.lowercased()] else {
                    // Not in any report but on the disk: someone else's file — never overwritten, never trusted.
                    if exists { conflicts.append(Msg.appendUnknownFile(disks[d], f.relPath)) }
                    continue
                }
                let state = CardRecord.stateOf(rec, cardPath: p, position: d)
                guard state == FileState.verified.rawValue || state == FileState.written.rawValue else {
                    // A failed file not on the disk (a cancelled append) is simply copied now; one that is there may be
                    // anything — never trusted, never overwritten.
                    if exists { throw OffloadError.preflight(Msg.appendFailedFile(f.relPath, disks[d]), code: .appendFailedFile) }
                    continue
                }
                goodInRecord += 1
                if !CardRecord.readBack(rec, cardPath: p, serial: b.records[d].volumeSerial) { unverifiedFile = true }
                if rec.file.size != f.size || !FileTimes.close(rec.file.modified, f.modifiedUtc, localTime: cardLocal) {
                    conflicts.append(conflict(f, disks[d], size: rec.file.size, modified: rec.file.modified))
                } else if !exists {
                    throw OffloadError.preflight(Msg.appendMissingFile(disks[d], f.relPath), code: .appendMissingFile)
                } else if let size = (attrs?[.size] as? NSNumber)?.int64Value, let m = attrs?[.modificationDate] as? Date,
                          size != f.size || !FileTimes.close(m, f.modifiedUtc, localTime: diskLocal[d]) {
                    conflicts.append(conflict(f, disks[d], size: size, modified: m))
                }
            }
            if unverifiedFile { unverified += 1 }
            if goodInRecord > 0 { known += 1 }
            if goodInRecord == 0 && present == 0 {
                newFiles.append(f)
            } else if goodInRecord > 0 && goodInRecord < b.paths.count && present < b.paths.count {
                let on = b.paths.enumerated().filter { FileManager.default.fileExists(atPath: $0.element + "/" + f.relPath) }
                    .map { disks[$0.offset] }.joined(separator: ", ")
                throw OffloadError.preflight(Msg.appendDiverged("\(f.relPath) is only on \(on)"), code: .appendDiverged)
            }
        }
        // Not one file of the card is in the backup — another card, never mixed into it (ruling I3).
        if !scan.files.isEmpty && known == 0 { throw OffloadError.preflight(Msg.appendOtherCard, code: .appendOtherCard) }
        // A new file is copied via ".<name>.offload-part" next to it — never over a file the backup recorded, nor over
        // another file of the card.
        let taken = Set(scan.files.map { $0.relPath.lowercased() } + b.records.flatMap { $0.files.keys })
        for f in newFiles where taken.contains(DestWriter.partName(f.relPath).lowercased()) {
            conflicts.append(Msg.appendPartNameTaken(f.relPath, DestWriter.partName(f.relPath)))
        }
        if !conflicts.isEmpty {
            var seen = Set<String>()
            throw OffloadError.preflight(Msg.appendConflict(conflicts.filter { seen.insert($0).inserted }, fullCopyFolder: b.cardName + "_2"),
                                         code: .appendConflict)
        }
        var dirs = Set<String>()
        for f in newFiles {
            let parts = f.relPath.split(separator: "/").dropLast()
            for i in parts.indices { dirs.insert(parts[parts.startIndex...i].joined(separator: "/")) }
        }
        var plan = AppendPlan(source: URL(fileURLWithPath: source).standardizedFileURL.path, cardPaths: b.paths, destRoots: b.roots,
                              date: b.date, cardName: b.cardName, generation: b.nextGeneration, fullScan: scan, newFiles: newFiles,
                              newDirs: dirs.sorted())
        plan.unverifiedEarlier = unverified
        plan.hashAlgorithms = b.records[0].generations.first?.report.algorithms ?? ["xxh64"]
        plan.project = b.records[0].project
        return plan
    }
}
