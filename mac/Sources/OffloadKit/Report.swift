import Darwin
import Foundation

public struct SetAsideCopy: Codable, Equatable {
    /// Index of the destination in `destinations`.
    public var destination: Int
    /// Path on the card ("/").
    public var path: String
    /// "_REPORT/neshoda_NNNN/<path>" relative to the card folder.
    public var movedTo: String
}

public struct OffloadReport: Codable {
    public struct Destination: Codable {
        public var root: String
        public var cardPath: String
        public var filesystem: String
        public var filesOK: Int
        public var filesFailed: Int
        /// Windows only (`false` = buffered I/O); the Mac reads it and never writes it.
        public var unbuffered: Bool?
        /// "verify-in-proxy" or "read-back"; absent without verification.
        public var verifyMethod: String?
        /// Czech: why this destination was read back although verify-in-proxy ran.
        public var verifyNote: String?
        /// "1A2B-3C4D" (FAT/exFAT/NTFS) or "uuid:<volume UUID>" (Mac, other file systems) — spec D.23.2.
        public var volumeSerial: String?

        public init(root: String, cardPath: String, filesystem: String, filesOK: Int, filesFailed: Int) {
            self.root = root; self.cardPath = cardPath; self.filesystem = filesystem
            self.filesOK = filesOK; self.filesFailed = filesFailed
        }
    }

    public struct FileEntry: Codable {
        public var path: String
        public var size: Int64
        public var modified: Date
        public var xxh64: String?
        /// XXH3-128, 32 lower-case hex.
        public var xxh128: String?
        /// Only with MD5 (`hashAlgorithms` contains "md5").
        public var md5: String?
        public var sourceError: String?
        public var destinations: [String]
        public var errors: [String?]
        /// Repair only: per destination true = a new copy, false = a good copy only read back.
        public var rewritten: [Bool]?

        public init(path: String, size: Int64, modified: Date, xxh64: String?, xxh128: String? = nil, md5: String? = nil,
                    sourceError: String?, destinations: [String], errors: [String?], rewritten: [Bool]? = nil) {
            self.path = path; self.size = size; self.modified = modified; self.xxh64 = xxh64; self.xxh128 = xxh128
            self.md5 = md5; self.sourceError = sourceError; self.destinations = destinations; self.errors = errors
            self.rewritten = rewritten
        }
    }

    public var tool = "offload"
    public var version = OffloadVersion.string
    public var hashAlgorithm = "xxh64"
    /// Only when not just xxh64: ["xxh64", "md5"].
    public var hashAlgorithms: [String]?
    /// ASC MHL generation of an append or a repair (`offload_report_NNNN.json`); absent = 1.
    public var generation: Int?
    /// "repair" for "Copy again"; absent otherwise.
    public var kind: String?
    public var project: String?
    public var cardName: String
    public var date: String
    public var source: String
    public var sourceFilesystem: String
    public var started: Date
    public var copyFinished: Date
    public var finished: Date
    public var verified: Bool
    /// The copy is done and verifies with the proxies; no ASC MHL generation yet (spec D.22.5).
    public var verifyPending: Bool?
    /// The verification result is not written into every card folder: never "verified" until a resume rewrites it.
    public var verifyIncomplete: Bool?
    public var safeToFormatSeconds: Double?
    public var verifyNotes: [String]?
    public var copyPausedSeconds: Double?
    public var verifyPausedSeconds: Double?
    public var setAside: [SetAsideCopy]?
    public var totalFiles: Int
    public var totalBytes: Int64
    public var destinations: [Destination]
    public var files: [FileEntry]
    public var skipped: [String]
    public var clips: [ClipEntry] = []
    public var proxy: ProxySettings?
    /// Windows only; read, never written (its value is not carried over by the Mac).
    public var proxyEncoder: String?

    public init(cardName: String, date: String, source: String, sourceFilesystem: String, started: Date, copyFinished: Date,
                finished: Date, verified: Bool, totalFiles: Int, totalBytes: Int64, destinations: [Destination],
                files: [FileEntry], skipped: [String]) {
        self.cardName = cardName; self.date = date; self.source = source; self.sourceFilesystem = sourceFilesystem
        self.started = started; self.copyFinished = copyFinished; self.finished = finished; self.verified = verified
        self.totalFiles = totalFiles; self.totalBytes = totalBytes; self.destinations = destinations; self.files = files
        self.skipped = skipped
    }

    enum CodingKeys: String, CodingKey {
        case tool, version, hashAlgorithm, hashAlgorithms, generation, kind, project, cardName, date, source, sourceFilesystem
        case started, copyFinished, finished, verified, verifyPending, verifyIncomplete, safeToFormatSeconds, verifyNotes
        case copyPausedSeconds, verifyPausedSeconds, setAside, totalFiles, totalBytes, destinations, files, skipped, clips, proxy
        case proxyEncoder
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(tool, forKey: .tool); try c.encode(version, forKey: .version)
        try c.encode(hashAlgorithm, forKey: .hashAlgorithm)
        try c.encodeIfPresent(hashAlgorithms, forKey: .hashAlgorithms)
        try c.encodeIfPresent((generation ?? 1) > 1 ? generation : nil, forKey: .generation)
        try c.encodeIfPresent(kind, forKey: .kind); try c.encodeIfPresent(project, forKey: .project)
        try c.encode(cardName, forKey: .cardName); try c.encode(date, forKey: .date)
        try c.encode(source, forKey: .source); try c.encode(sourceFilesystem, forKey: .sourceFilesystem)
        try c.encode(started, forKey: .started); try c.encode(copyFinished, forKey: .copyFinished)
        try c.encode(finished, forKey: .finished); try c.encode(verified, forKey: .verified)
        try c.encodeIfPresent(verifyPending == true ? true : nil, forKey: .verifyPending)
        try c.encodeIfPresent(verifyIncomplete == true ? true : nil, forKey: .verifyIncomplete)
        try c.encodeIfPresent(safeToFormatSeconds, forKey: .safeToFormatSeconds)
        try c.encodeIfPresent(verifyNotes?.isEmpty == false ? verifyNotes : nil, forKey: .verifyNotes)
        try c.encodeIfPresent(copyPausedSeconds, forKey: .copyPausedSeconds)
        try c.encodeIfPresent(verifyPausedSeconds, forKey: .verifyPausedSeconds)
        try c.encodeIfPresent(setAside?.isEmpty == false ? setAside : nil, forKey: .setAside)
        try c.encode(totalFiles, forKey: .totalFiles); try c.encode(totalBytes, forKey: .totalBytes)
        try c.encode(destinations.map { d -> Destination in var x = d; x.unbuffered = nil; return x }, forKey: .destinations)
        try c.encode(files, forKey: .files); try c.encode(skipped, forKey: .skipped)
        try c.encode(clips, forKey: .clips); try c.encodeIfPresent(proxy, forKey: .proxy)
        // proxyEncoder: never written by the Mac.
    }

    /// Tolerant reading: every key added since 0.1.0 may be missing; `clips` missing or null (a report without analysis,
    /// an older Mac) is an empty list — the files are the record of the copy, the clips only describe them.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        tool = try c.decodeIfPresent(String.self, forKey: .tool) ?? "offload"
        version = try c.decodeIfPresent(String.self, forKey: .version) ?? "?"
        hashAlgorithm = try c.decodeIfPresent(String.self, forKey: .hashAlgorithm) ?? "xxh64"
        hashAlgorithms = try c.decodeIfPresent([String].self, forKey: .hashAlgorithms)
        generation = try c.decodeIfPresent(Int.self, forKey: .generation)
        kind = try c.decodeIfPresent(String.self, forKey: .kind)
        project = try c.decodeIfPresent(String.self, forKey: .project)
        cardName = try c.decode(String.self, forKey: .cardName)
        date = try c.decode(String.self, forKey: .date)
        source = try c.decode(String.self, forKey: .source)
        sourceFilesystem = try c.decodeIfPresent(String.self, forKey: .sourceFilesystem) ?? "?"
        started = try c.decode(Date.self, forKey: .started)
        copyFinished = try c.decodeIfPresent(Date.self, forKey: .copyFinished) ?? started
        finished = try c.decodeIfPresent(Date.self, forKey: .finished) ?? copyFinished
        verified = try c.decodeIfPresent(Bool.self, forKey: .verified) ?? false
        verifyPending = try c.decodeIfPresent(Bool.self, forKey: .verifyPending)
        verifyIncomplete = try c.decodeIfPresent(Bool.self, forKey: .verifyIncomplete)
        safeToFormatSeconds = try c.decodeIfPresent(Double.self, forKey: .safeToFormatSeconds)
        verifyNotes = try c.decodeIfPresent([String].self, forKey: .verifyNotes)
        copyPausedSeconds = try c.decodeIfPresent(Double.self, forKey: .copyPausedSeconds)
        verifyPausedSeconds = try c.decodeIfPresent(Double.self, forKey: .verifyPausedSeconds)
        setAside = try c.decodeIfPresent([SetAsideCopy].self, forKey: .setAside)
        destinations = try c.decode([Destination].self, forKey: .destinations)
        files = try c.decode([FileEntry].self, forKey: .files)
        totalFiles = try c.decodeIfPresent(Int.self, forKey: .totalFiles) ?? files.count
        totalBytes = try c.decodeIfPresent(Int64.self, forKey: .totalBytes) ?? files.reduce(0) { $0 + $1.size }
        skipped = try c.decodeIfPresent([String].self, forKey: .skipped) ?? []
        clips = (try? c.decodeIfPresent([ClipEntry?].self, forKey: .clips))?.compactMap { $0 } ?? []
        proxy = try? c.decodeIfPresent(ProxySettings.self, forKey: .proxy)
        proxyEncoder = try c.decodeIfPresent(String.self, forKey: .proxyEncoder)
    }

    public static func jsonName(_ g: Int) -> String { g <= 1 ? "offload_report.json" : String(format: "offload_report_%04d.json", g) }
    public static func textName(_ g: Int) -> String { g <= 1 ? "offload_report.txt" : String(format: "offload_report_%04d.txt", g) }
    public static func htmlName(_ card: String, _ g: Int) -> String { g <= 1 ? "\(card)_report.html" : String(format: "%@_report_%04d.html", card, g) }
    public static func pdfName(_ card: String, _ g: Int) -> String { g <= 1 ? "\(card)_report.pdf" : String(format: "%@_report_%04d.pdf", card, g) }

    public var generationNumber: Int { generation ?? 1 }
    public var isRepair: Bool { kind == "repair" }
    public var algorithms: [String] { hashAlgorithms ?? ["xxh64"] }
    public var pausedSeconds: Double { (copyPausedSeconds ?? 0) + (verifyPausedSeconds ?? 0) }
    public var copySeconds: Double { max(0, copyFinished.timeIntervalSince(started) - (copyPausedSeconds ?? 0)) }
    public var verifySeconds: Double { max(0, finished.timeIntervalSince(copyFinished) - (verifyPausedSeconds ?? 0)) }
    /// The card could not be read, or a destination does not hold a good copy (isGood). A cancelled verification
    /// leaves files "written" or already "verified": both are fine then.
    public func isFailed(_ f: FileEntry) -> Bool {
        f.sourceError != nil || f.destinations.contains { !OffloadReport.isGood($0, verified: verified) }
    }
    public var failedFiles: [FileEntry] { files.filter(isFailed) }
    public var success: Bool { failedFiles.isEmpty }
    /// Spec D.22.7: every file on every destination verified, and the result written everywhere.
    public var safeToFormat: Bool { success && verified && verifyPending != true && verifyIncomplete != true }

    /// Reads a report written by any Offload (Mac 0.1+, Windows 0.1+): unknown keys ignored, dates ISO 8601.
    public static func read(_ path: String) throws -> OffloadReport {
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        return try dec.decode(OffloadReport.self, from: Data(contentsOf: URL(fileURLWithPath: path)))
    }

    /// A file state that counts as good: "verified" — or, when verification did not run to the end
    /// (off or cancelled), also "written". Shared with Windows `OffloadReport.FailedFiles`.
    static func isGood(_ state: String, verified: Bool) -> Bool {
        state == FileState.verified.rawValue || (!verified && state == FileState.written.rawValue)
    }

    static func build(plan: OffloadPlan, hashes: [UInt64?], sourceErrors: [String?], destResults: [[DestFileResult]],
                      verified: Bool, started: Date, copyFinished: Date, finished: Date) -> OffloadReport {
        let files = plan.scan.files.enumerated().map { i, f in
            FileEntry(path: f.relPath, size: f.size,
                      modified: Date(timeIntervalSince1970: Double(f.mtime.tv_sec) + Double(f.mtime.tv_nsec) / 1e9),
                      xxh64: hashes[i].map(hex64), sourceError: sourceErrors[i],
                      destinations: destResults.map { $0[i].state.rawValue },
                      errors: destResults.map { $0[i].error })
        }
        let dests = plan.destRoots.enumerated().map { d, root in
            Destination(root: root, cardPath: plan.cardPaths[d], filesystem: IO.filesystemType(root),
                        filesOK: destResults[d].filter { isGood($0.state.rawValue, verified: verified) }.count,
                        filesFailed: destResults[d].filter { !isGood($0.state.rawValue, verified: verified) }.count)
        }
        return OffloadReport(cardName: plan.cardName, date: plan.date, source: plan.source,
                             sourceFilesystem: IO.filesystemType(plan.source),
                             started: started, copyFinished: copyFinished, finished: finished, verified: verified,
                             totalFiles: files.count, totalBytes: plan.scan.totalBytes,
                             destinations: dests, files: files, skipped: plan.scan.skipped)
    }

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
    public var hashList: String { hashList(of: "xxh64") }

    /// "<hash>  <path>\n" per file that has `algorithm` ("xxh64" or "md5", the md5sum format).
    public func hashList(of algorithm: String) -> String {
        files.compactMap { f in (algorithm == "md5" ? f.md5 : f.xxh64).map { "\($0)  \(f.path)\n" } }.joined()
    }

    static func hashListPath(_ cardPath: String, _ cardName: String, _ algorithm: String) -> String {
        cardPath + "/" + Layout.reportDirName + "/" + cardName + "." + algorithm
    }

    public var summary: String {
        let ts = ISO8601DateFormatter()
        ts.formatOptions = [.withInternetDateTime]
        ts.timeZone = .current
        var s = """
        OFFLOAD REPORT — \(success ? "OK" : "FAILED")
        Card:        \(cardName)
        Source:      \(source) [\(sourceFilesystem)]
        Started:     \(ts.string(from: started))
        Finished:    \(ts.string(from: finished))
        Files:       \(totalFiles)  (\(formatBytes(totalBytes)))
        Copy:        \(String(format: "%.1f s", copySeconds))  \(formatRate(totalBytes, copySeconds))
        Verify:      \(verified ? String(format: "%.1f s", verifySeconds) + "  " + formatRate(totalBytes, verifySeconds) : verifyPending == true ? "PENDING (with the proxies)" : "NOT VERIFIED")

        """
        if let t = safeToFormatSeconds { s += String(format: "Safe to format after: %.1f s\n", t) }
        if pausedSeconds > 0 { s += String(format: "Paused:      %.1f s (not in the speeds)\n", pausedSeconds) }
        s += "Hash:        \(algorithms.joined(separator: "+"))\n"
        for d in destinations {
            s += "Destination: \(d.cardPath) [\(d.filesystem)\(d.volumeSerial.map { " " + $0 } ?? "")]  ok \(d.filesOK)  failed \(d.filesFailed)\(d.verifyMethod.map { "  verify: " + $0 } ?? "")\n"
        }
        if !skipped.isEmpty {
            s += "\nSkipped (system files): \(skipped.joined(separator: ", "))\n"
        }
        let failed = failedFiles
        if !failed.isEmpty {
            s += "\nFAILED FILES (\(failed.count)):\n"
            for f in failed {
                s += "  \(f.path)\n"
                if let e = f.sourceError { s += "    source: \(e)\n" }
                for (d, e) in f.errors.enumerated() where e != nil {
                    s += "    dest \(d + 1): \(e!)\n"
                }
            }
        }
        return s
    }

    /// Pretty JSON with sorted keys and second-precision ISO 8601 dates — the shared format (docs/FORMATY.md).
    public func jsonData() throws -> Data {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        enc.dateEncodingStrategy = .iso8601
        return try enc.encode(self)
    }

    /// Writes this generation into `<card>` in the order of spec D.22.2 — every failure leaves the folder in a state the
    /// next append refuses or completes correctly:
    /// 1. this generation's reports (JSON, TXT, HTML; the job writes the PDF after them) — the first write of a
    ///    generation ≥ 2 only creates new files (an existing one stops everything before a shared file is touched);
    ///    the job's own refresh of its generation (`includeHashRecords == false`) replaces them;
    /// 2. the ASC MHL generation and the chain (the commit point);
    /// 3. the hash lists last (`<card>.xxh64`, `<card>.md5` only with MD5) — only extended after generation 1.
    public func write(cardPath: String, includeHashRecords: Bool, createNew: Bool? = nil) throws {
        try writeReports(cardPath, create: createNew ?? (generationNumber > 1 && includeHashRecords))
        guard includeHashRecords else { return }
        try MHL.write(report: self, cardPath: cardPath)
        try writeHashLists(cardPath)
    }

    /// The verification result of a generation that verified with its proxies (task 67): the MHL generation and chain
    /// first (unless a retry finds them written), then the reports written with verifyPending are replaced, then the hash
    /// lists (merged — an existing list keeps its bytes, even of generation 1). A report still pending is never committed.
    public func commit(cardPath: String) throws {
        if verifyPending == true { throw OffloadError.failed("A report whose verification is still pending is never committed.") }
        if !(try MHL.hasGeneration(self, cardPath: cardPath)) { try MHL.write(report: self, cardPath: cardPath) }
        try writeReports(cardPath, create: false)
        try writeHashLists(cardPath, merge: true)
    }

    func writeReports(_ cardPath: String, create: Bool) throws {
        let dir = cardPath + "/" + Layout.reportDirName
        try IO.mkdirs(dir)
        let g = generationNumber
        let outputs: [(String, Data)] = [
            (OffloadReport.jsonName(g), try jsonData()),
            (OffloadReport.textName(g), Data(summary.utf8)),
            (OffloadReport.htmlName(cardName, g), Data(HTMLReport.render(self).utf8)),
        ]
        if create {
            // Every name is checked before the first is written: a taken one stops the generation with nothing new.
            for (name, _) in outputs where FileManager.default.fileExists(atPath: dir + "/" + name) {
                throw FileExists(path: dir + "/" + name)
            }
        }
        for (name, data) in outputs {
            if create { try IO.writeFileNew(dir + "/" + name, data) } else { try IO.writeFileDurably(dir + "/" + name, data) }
        }
    }

    /// Generation 1 writes the lists whole; later generations (and `merge`) keep the earlier bytes, add the lines the ASC
    /// MHL knows and the list does not (an earlier append whose list step failed), then this generation's lines the list
    /// does not have word for word (a repair repeats none). Nothing new = the file is not touched.
    func writeHashLists(_ cardPath: String, merge: Bool = false) throws {
        for algorithm in algorithms where algorithm == "xxh64" || algorithm == "md5" {
            let path = OffloadReport.hashListPath(cardPath, cardName, algorithm)
            let lines = hashList(of: algorithm)
            if generationNumber == 1 && !(merge && FileManager.default.fileExists(atPath: path)) {
                try IO.writeFileDurably(path, Data(lines.utf8))
                continue
            }
            let (earlier, missed) = OffloadReport.listAndMissed(path, cardPath: cardPath, alsoListed: files.map(\.path), algorithm: algorithm)
            let added = missed + OffloadReport.notIn(earlier, lines)
            if added.isEmpty && FileManager.default.fileExists(atPath: path) { continue }
            try IO.writeFileDurably(path, earlier + Data(added.utf8))
        }
    }

    /// The lines of `lines` the list does not have word for word.
    static func notIn(_ earlier: Data, _ lines: String) -> String {
        let have = Set(listLines(earlier))
        return listLines(Data(lines.utf8)).filter { !have.contains($0) }.map { $0 + "\n" }.joined()
    }

    /// The lines of a hash list without their "\n" or "\r\n" (a Windows tool may write CRLF). Not `split(separator:)`:
    /// Swift reads "\r\n" as one Character, which is not "\n".
    static func listLines(_ data: Data) -> [String] {
        String(decoding: data, as: UTF8.self).components(separatedBy: "\n")
            .map { $0.hasSuffix("\r") ? String($0.dropLast()) : $0 }
            .filter { !$0.isEmpty }
    }

    /// The list as it is (exact bytes, a final "\n" added when missing) and the lines of every path the ASC MHL history
    /// records (newest good record) but neither the list nor `alsoListed` has.
    static func listAndMissed(_ path: String, cardPath: String, alsoListed: [String], algorithm: String) -> (Data, String) {
        var earlier = FileManager.default.contents(atPath: path) ?? Data()
        if let last = earlier.last, last != UInt8(ascii: "\n") { earlier.append(UInt8(ascii: "\n")) }
        var listed = Set(alsoListed.map { $0.lowercased() })
        for l in listLines(earlier) {
            if let r = l.range(of: "  ") { listed.insert(String(l[r.upperBound...]).lowercased()) }
        }
        var missed = ""
        if let mhl = MHLReader.read(cardPath: cardPath) {
            for rec in mhl.lastGood.values.sorted(by: { $0.path < $1.path }) where !listed.contains(rec.path.lowercased()) {
                if let h = rec.hashes.first(where: { $0.algorithm == algorithm }) { missed += "\(h.value)  \(rec.path)\n" }
            }
        }
        return (earlier, missed)
    }

    /// An append with nothing new: add the lines the hash lists missed (`.md5` only when the ASC MHL history has MD5).
    /// True when something was added.
    public static func healHashList(cardPath: String, cardName: String) throws -> Bool {
        var healed = false
        for algorithm in ["xxh64", "md5"] {
            let path = hashListPath(cardPath, cardName, algorithm)
            let (earlier, missed) = listAndMissed(path, cardPath: cardPath, alsoListed: [], algorithm: algorithm)
            guard !missed.isEmpty else { continue }
            try IO.writeFileDurably(path, earlier + Data(missed.utf8))
            healed = true
        }
        return healed
    }
}

public enum ProxyStatus: String, Codable {
    case none, skipped, unsupported, done, failed
}

/// One report thumbnail: a small JPEG and its caption (source timecode or "#frame"). Never written to the JSON.
public struct Thumbnail: Equatable {
    public let jpeg: Data
    public let label: String

    public init(jpeg: Data, label: String) {
        self.jpeg = jpeg
        self.label = label
    }
}

public struct ClipEntry: Codable {
    public var clip: Clip
    public var info: ClipInfo?
    public var probeError: String?
    public var proxyStatus: ProxyStatus = .none
    /// Relative to the card folder.
    public var proxyPath: String?
    public var proxyNote: String?
    public var proxySeconds: Double?
    /// First, middle and last frame (JPEG + caption); embedded in the HTML/PDF report only.
    public var thumbnails: [Thumbnail] = []

    /// The LUT applied to this clip's proxy and thumbnails, and why (JSON "lut"; absent when nil, like Windows).
    public var lut: LutUse?

    enum CodingKeys: String, CodingKey {
        case clip, info, probeError, proxyStatus, proxyPath, proxyNote, proxySeconds, lut
    }

    public init(clip: Clip) { self.clip = clip }
}

public enum OffloadVersion {
    public static let string = "0.3.0"
}
