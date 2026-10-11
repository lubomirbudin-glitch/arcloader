import Foundation

/// R6 (task 69; Windows `Unfinished`): a generation an earlier run started in these card folders and never committed to
/// the ASC MHL chain — the app ended during the copy (no report of it yet) or during the verification with the proxies
/// (its reports say verifyPending), or the commit failed part way (verifyIncomplete). `manifests`: per card folder what its
/// block manifest says. `fixedFiles`: the generation is in the chain of some folder already — its files are those its
/// report lists and no others (lower-cased); nil = not committed anywhere, the card's other new files may join it.
public struct Unfinished {
    public let generation: Int
    public let manifests: [[ManifestRecord]]
    public let fixedFiles: Set<String>?
    /// The report of the generation from the interrupted run (any folder), when it wrote one.
    public let report: OffloadReport?
    private let files: [[String: ManifestRecord]]
    private let parts: [[String: PartCheckpoint]]

    public init(generation: Int, manifests: [[ManifestRecord]], fixedFiles: Set<String>?, report: OffloadReport? = nil) {
        self.generation = generation
        self.manifests = manifests
        self.fixedFiles = fixedFiles
        self.report = report
        files = manifests.map { ManifestFile.files($0, generation: generation) }
        parts = manifests.map { ManifestFile.parts($0, generation: generation) }
    }

    /// Folder `d`'s `file` record of `f` for this generation, when it is the card's file (size, time).
    public func fileOf(_ d: Int, _ f: SourceFile, localTime: Bool) -> ManifestRecord? {
        guard let r = files[d][f.relPath.lowercased()], r.size == f.size,
              FileTimes.close(r.modified, f.modifiedUtc, localTime: localTime) else { return nil }
        return r
    }

    /// Folder `d`'s checkpoint of `f` for this generation, when it is the card's file (size, time).
    public func partOf(_ d: Int, _ f: SourceFile, localTime: Bool) -> PartCheckpoint? {
        guard let p = parts[d][f.relPath.lowercased()], p.size == f.size,
              FileTimes.close(p.modified, f.modifiedUtc, localTime: localTime) else { return nil }
        return p
    }

    /// The interrupted run wrote `f` there: a file at its place with the card's size whose record (or checkpoint — it was
    /// renamed into place before its record was written) names the card's file.
    public func owns(_ d: Int, _ f: SourceFile, sizeOnDisk: Int64, localTime: Bool) -> Bool {
        sizeOnDisk == f.size && (fileOf(d, f, localTime: localTime) != nil || partOf(d, f, localTime: localTime) != nil)
    }

    /// The manifest of some folder mentions `f` (the card is the one that run copied — ruling I3 for a resume).
    public func mentions(_ f: SourceFile) -> Bool {
        let key = f.relPath.lowercased()
        return files.contains { $0[key] != nil } || parts.contains { $0[key] != nil }
    }
}

/// R6, tied into the append: finds an unfinished generation and decides, per card file, what a resume does — complete
/// copies are never copied again, a part on the disk goes on from its checkpoint. Reads only.
public enum ResumePlanner {
    /// The unfinished generation of the card folders, or nil when every newest generation is committed (the append plans
    /// as before). A folder is open at generation g when g's report says verifyPending — g after its committed generation
    /// (the run ended before its commit), or g its committed one (the commit wrote the ASC MHL but not the reports) — or
    /// says verifyIncomplete at its committed one (the commit failed in another folder), or when there is no report of g
    /// after its committed one and its manifest has records of g (the run ended during the copy). Every other folder must
    /// stand right before g with no report of it, or have g committed by the same run (the same start and destinations)
    /// — else `appendDiverged`.
    public static func find(_ cardPaths: [String]) throws -> Unfinished? {
        let states = cardPaths.map { p in (path: p, committed: committed(p), records: ManifestFile.read(p)) }
        func openAt(_ s: (path: String, committed: Int, records: [ManifestRecord])) -> Int? {
            if s.committed > 0 && unsettled(s.path, s.committed) { return s.committed }
            let next = s.committed + 1
            if pending(s.path, next) || (!reportExists(s.path, next) && s.records.contains { $0.gen == next }) { return next }
            return nil
        }
        let open = states.map(openAt)
        guard let g = open.compactMap({ $0 }).first else { return nil }
        let pendingReport = states.indices.filter { open[$0] == g && unsettled(states[$0].path, g) }
            .lazy.compactMap { read(states[$0].path, g) }.first
        let fixed: Set<String>? = states.contains { $0.committed == g } && pendingReport != nil
            ? Set(pendingReport!.files.map { $0.path.lowercased() }) : nil
        for (d, s) in states.enumerated() where open[d] != g {
            let before = open[d] == nil && s.committed == g - 1 && !reportExists(s.path, g)
            var doneBySameRun = false
            if open[d] == nil, s.committed == g, let p = pendingReport, let done = read(s.path, g) { doneBySameRun = sameRun(done, p) }
            if !before && !doneBySameRun {
                throw OffloadError.preflight(VIPMsg.resumeDiverged(s.path, g), code: .appendDiverged)
            }
        }
        let anyReport = pendingReport ?? states.indices.lazy.compactMap { read(states[$0].path, g) }.first
        return Unfinished(generation: g, manifests: states.map(\.records), fixedFiles: fixed, report: anyReport)
    }

    /// Two reports of one generation written by the same run: the same start and the same card folders.
    static func sameRun(_ a: OffloadReport, _ b: OffloadReport) -> Bool {
        a.started == b.started && a.destinations.count == b.destinations.count
            && zip(a.destinations, b.destinations).allSatisfy { CardRecord.normal($0.cardPath) == CardRecord.normal($1.cardPath) }
    }

    static func reportPath(_ cardPath: String, _ g: Int) -> String {
        cardPath + "/" + Layout.reportDirName + "/" + OffloadReport.jsonName(g)
    }

    static func reportExists(_ cardPath: String, _ g: Int) -> Bool { FileManager.default.fileExists(atPath: reportPath(cardPath, g)) }

    static func read(_ cardPath: String, _ g: Int) -> OffloadReport? { try? OffloadReport.read(reportPath(cardPath, g)) }

    /// The newest committed generation: the ASC MHL chain's (0 without one).
    static func committed(_ cardPath: String) -> Int {
        MHLReader.readChain(ascmhlDir: cardPath + "/" + MHL.dirName)?.map(\.seq).max() ?? 0
    }

    static func pending(_ cardPath: String, _ g: Int) -> Bool { read(cardPath, g)?.verifyPending == true }

    /// Pending, or its result written here but not into every folder (verifyIncomplete).
    static func unsettled(_ cardPath: String, _ g: Int) -> Bool {
        guard let r = read(cardPath, g) else { return false }
        return r.verifyPending == true || r.verifyIncomplete == true
    }

    /// What a resume does with each card file of the unfinished generation (`files` in copy order; `present[i][d]`: at its
    /// place on the disk): written where it is missing; not read at all when every folder has it with the same record
    /// (`known`); read only for its hashes when every folder has it without one (`hashOnly`); resumed from the checkpoint
    /// every folder it is written to has — as far as all of them agree, only on a destination written past the cache (M1:
    /// the Mac always is, F_NOCACHE + F_FULLFSYNC before a checkpoint), with the manifest's block size (M2).
    /// `blockSize`: this copy's block size — a record or checkpoint of another one (another --chunk-mb) is not used: the
    /// file is hashed or copied again (the Mac's take on M2: its verifier needs one block size per file).
    public static func decide(_ u: Unfinished, cardPaths: [String], files: [SourceFile], present: [[Bool]], localTime: [Bool],
                              blockSize: Int? = nil)
        -> (write: [[Bool]], known: [ManifestRecord?], hashOnly: [Bool], resume: [PartCheckpoint?]) {
        var write: [[Bool]] = [], known: [ManifestRecord?] = [], hashOnly: [Bool] = [], resume: [PartCheckpoint?] = []
        for (i, f) in files.enumerated() {
            let mask = present[i].map { !$0 }
            write.append(mask)
            let records = cardPaths.indices.map { u.fileOf($0, f, localTime: localTime[$0]) }
            let everywhere = !mask.contains(true)
            let same = records.allSatisfy { $0 != nil } && Set(records.compactMap { $0?.xxh64 }).count == 1
                && records.allSatisfy { $0?.toManifest() != nil && $0?.xxh64Value != nil && (blockSize == nil || $0?.blockSize == blockSize) }
            known.append(everywhere && same ? records[0] : nil)
            hashOnly.append(everywhere && !same)
            let part = mask.contains(true) ? commonCheckpoint(u, cardPaths, f, mask, localTime) : nil
            resume.append(part.flatMap { blockSize == nil || $0.blockSize == blockSize ? $0 : nil })
        }
        return (write, known, hashOnly, resume)
    }

    static func commonCheckpoint(_ u: Unfinished, _ cardPaths: [String], _ f: SourceFile, _ mask: [Bool], _ localTime: [Bool]) -> PartCheckpoint? {
        var common: PartCheckpoint?
        for d in cardPaths.indices where mask[d] {
            let part = cardPaths[d] + "/" + DestWriter.partName(f.relPath)
            let size = (try? FileManager.default.attributesOfItem(atPath: part))?[.size] as? NSNumber
            guard let p = u.partOf(d, f, localTime: localTime[d]), !p.blocks.isEmpty, let size,
                  size.int64Value >= Int64(p.blocks.count) * Int64(p.blockSize) else { return nil }
            guard var c = common else { common = p; continue }
            if c.blockSize != p.blockSize { return nil }
            var same = 0
            while same < min(c.blocks.count, p.blocks.count) && c.blocks[same] == p.blocks[same] { same += 1 }
            if same == 0 { return nil }
            c.blocks = Array(c.blocks[..<same])
            common = c
        }
        return common
    }
}
