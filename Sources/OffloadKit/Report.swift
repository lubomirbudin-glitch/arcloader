import Darwin
import Foundation

public struct OffloadReport: Codable {
    public struct Destination: Codable {
        public let root: String
        public let cardPath: String
        public let filesystem: String
        public let filesOK: Int
        public let filesFailed: Int
    }

    public struct FileEntry: Codable {
        public let path: String
        public let size: Int64
        public let modified: Date
        public let xxh64: String?
        public let sourceError: String?
        public let destinations: [String]
        public let errors: [String?]
    }

    public var tool = "offload"
    public var version = OffloadVersion.string
    public var hashAlgorithm = "xxh64"
    public let cardName: String
    public let date: String
    public let source: String
    public let sourceFilesystem: String
    public let started: Date
    public let copyFinished: Date
    public let finished: Date
    public let verified: Bool
    public let totalFiles: Int
    public let totalBytes: Int64
    public let destinations: [Destination]
    public let files: [FileEntry]
    public let skipped: [String]
    public var clips: [ClipEntry] = []
    public var proxy: ProxySettings?

    public var copySeconds: Double { copyFinished.timeIntervalSince(started) }
    public var verifySeconds: Double { finished.timeIntervalSince(copyFinished) }
    public var failedFiles: [FileEntry] {
        files.filter { f in f.sourceError != nil || f.destinations.contains { $0 != (verified ? "verified" : "written") } }
    }
    public var success: Bool { failedFiles.isEmpty }
    /// Every file is on every destination and was read back with a matching hash.
    public var safeToEject: Bool { success && verified }

    static func build(plan: OffloadPlan, hashes: [UInt64?], sourceErrors: [String?], destResults: [[DestFileResult]],
                      verified: Bool, started: Date, copyFinished: Date, finished: Date) -> OffloadReport {
        let okState: FileState = verified ? .verified : .written
        let files = plan.scan.files.enumerated().map { i, f in
            FileEntry(path: f.relPath, size: f.size,
                      modified: Date(timeIntervalSince1970: Double(f.mtime.tv_sec) + Double(f.mtime.tv_nsec) / 1e9),
                      xxh64: hashes[i].map(hex64), sourceError: sourceErrors[i],
                      destinations: destResults.map { $0[i].state.rawValue },
                      errors: destResults.map { $0[i].error })
        }
        let dests = plan.destRoots.enumerated().map { d, root in
            Destination(root: root, cardPath: plan.cardPaths[d], filesystem: IO.filesystemType(root),
                        filesOK: destResults[d].filter { $0.state == okState }.count,
                        filesFailed: destResults[d].filter { $0.state != okState }.count)
        }
        return OffloadReport(cardName: plan.cardName, date: plan.date, source: plan.source,
                             sourceFilesystem: IO.filesystemType(plan.source),
                             started: started, copyFinished: copyFinished, finished: finished, verified: verified,
                             totalFiles: files.count, totalBytes: plan.scan.totalBytes,
                             destinations: dests, files: files, skipped: plan.scan.skipped)
    }

    /// Plain hash list, one "<xxh64>  <path>" per line — readable by humans and by `offload verify`.
    public var hashList: String {
        files.compactMap { f in f.xxh64.map { "\($0)  \(f.path)" } }.joined(separator: "\n") + "\n"
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
        Verify:      \(verified ? String(format: "%.1f s", verifySeconds) + "  " + formatRate(totalBytes, verifySeconds) : "NOT VERIFIED")
        Hash:        \(hashAlgorithm)

        """
        for d in destinations {
            s += "Destination: \(d.cardPath) [\(d.filesystem)]  ok \(d.filesOK)  failed \(d.filesFailed)\n"
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

    /// Writes all report files into `<card>/_REPORT`. The hash list and MHL are the record of the copy itself,
    /// so they are written once (`includeHashRecords`); the HTML/JSON are refreshed as clips get analysed.
    public func write(cardPath: String, includeHashRecords: Bool) throws {
        let dir = cardPath + "/" + Layout.reportDirName
        try IO.mkdirs(dir)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        enc.dateEncodingStrategy = .iso8601
        var outputs: [(String, Data)] = [
            ("offload_report.json", try enc.encode(self)),
            ("offload_report.txt", Data(summary.utf8)),
            ("\(cardName)_report.html", Data(HTMLReport.render(self).utf8)),
        ]
        if includeHashRecords {
            outputs.append(("\(cardName).xxh64", Data(hashList.utf8)))
        }
        for (name, data) in outputs {
            try IO.writeFileDurably(dir + "/" + name, data)
        }
        if includeHashRecords {
            try MHL.write(report: self, cardPath: cardPath)
        }
    }
}

public enum ProxyStatus: String, Codable {
    case none, skipped, unsupported, done, failed
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
    /// JPEG; embedded in the HTML report only.
    public var thumbnail: Data?

    enum CodingKeys: String, CodingKey {
        case clip, info, probeError, proxyStatus, proxyPath, proxyNote, proxySeconds
    }

    public init(clip: Clip) { self.clip = clip }
}

public enum OffloadVersion {
    public static let string = "0.1.0"
}
