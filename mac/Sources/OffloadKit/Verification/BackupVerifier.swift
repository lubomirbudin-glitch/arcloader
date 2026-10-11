import Darwin
import Foundation

/// What a verification of one card folder found (FORMATY „Report ověření“).
public struct VerifyResult {
    public var cardPath: String
    public var started = Date()
    public var finished = Date()
    /// "ascmhl", "hashList", or "pending" (generation 1 still verifies with its proxies — nothing to compare yet).
    public var basis = "ascmhl"
    public var generations = 0
    public var hashAlgorithms: [String] = []
    public var totalFiles = 0
    public var totalBytes: Int64 = 0
    public var ok = 0
    public var changed: [String] = []
    public var missing: [String] = []
    public var extra: [String] = []
    public var failedAtCopy: [String] = []
    public var failedSuperseded: [String] = []
    public var unreadable: [(path: String, error: String)] = []
    public var chainProblems: [String] = []
    public var checked = 0
    public var bytesRead: Int64 = 0
    public var cancelled = false
    public var folderGone = false
    /// The card's MHL history ignores _REPORT: the verification report may go inside the card folder.
    public var reportInsideCard = true
    /// The newest generation is not finished (verifyPending / verifyIncomplete): never "the backup is verified".
    public var unfinished = false
    /// Nothing differs, nothing is missing or unreadable, nothing failed at the copy (extra files do not count).
    public var success: Bool { changed.isEmpty && missing.isEmpty && unreadable.isEmpty && failedAtCopy.isEmpty }

    public init(cardPath: String) { self.cardPath = cardPath }
}

/// Verify backup (Windows spec 2.4, R8, R9; spec D.22.4): every file of a card folder read once (F_NOCACHE) and hashed
/// with every algorithm its record has, against every ASC MHL generation — or, without ascmhl/, against the checksum
/// lists in _REPORT. Only reads; nothing is ever written into ascmhl/.
public enum BackupVerifier {
    public static let pendingBasis = "pending"

    struct Expected {
        let path: String
        let size: Int64?
        let hashes: [MHLHash]
    }

    /// Offload's own folders in a card folder (never "extra").
    static let ours = [Layout.reportDirName + "/", Layout.proxyDirName + "/", MHL.dirName + "/"]

    public static func verify(_ cardPath: String, chunkSize: Int = 16 << 20, progress: ((String, Int64) -> Void)? = nil,
                              isCancelled: () -> Bool = { false }) throws -> VerifyResult {
        var r = VerifyResult(cardPath: cardPath)
        r.started = Date()
        var expected: [Expected] = []
        var ignore: [String] = []
        let record = CardRecord.load(cardPath)
        if let mhl = MHLReader.read(cardPath: cardPath) {
            r.basis = "ascmhl"
            r.generations = mhl.generations.count
            r.chainProblems = mhl.chainProblems
            r.reportInsideCard = mhl.ignoresReportFolder
            ignore = mhl.ignore
            for (key, rec) in mhl.files.sorted(by: { $0.key < $1.key }) {
                if rec.failed {
                    if let good = mhl.lastGood[key] {
                        r.failedSuperseded.append(rec.path)
                        expected.append(Expected(path: good.path, size: good.size, hashes: good.hashes.filter { $0.action != "failed" }))
                    } else {
                        r.failedAtCopy.append(rec.path)
                    }
                } else {
                    expected.append(Expected(path: rec.path, size: rec.size, hashes: rec.hashes))
                }
            }
        } else if let lists = hashLists(cardPath) {
            r.basis = "hashList"
            expected = lists
        } else if record.verifyPending {
            r.basis = pendingBasis
        } else {
            throw OffloadError.preflight(Msg.noHashRecord(cardPath + "/" + Layout.reportDirName), code: .noHashRecord)
        }
        r.unfinished = r.basis == pendingBasis || record.incomplete
            || record.generations.last?.report.verifyPending == true
        let algorithms = Set(expected.flatMap { $0.hashes.map { $0.algorithm.lowercased() } })
        r.hashAlgorithms = algorithms.sorted { (HashAlgo.known.firstIndex(of: $0) ?? 99, $0) < (HashAlgo.known.firstIndex(of: $1) ?? 99, $1) }
        r.totalFiles = expected.count + r.failedAtCopy.count
        r.totalBytes = expected.reduce(0) { $0 + ($1.size ?? 0) }
        let buf = IO.allocateAligned(chunkSize)
        defer { free(buf) }
        for e in expected {
            if isCancelled() {
                r.cancelled = true
                break
            }
            r.checked += 1
            let full = cardPath + "/" + e.path
            if !FileManager.default.fileExists(atPath: cardPath) { r.folderGone = true }
            guard FileManager.default.fileExists(atPath: full) else {
                if r.folderGone { r.unreadable.append((e.path, "the card folder disappeared")) } else { r.missing.append(e.path) }
                continue
            }
            let running = e.hashes.compactMap { h in HashAlgo.make(h.algorithm).map { (h, $0) } }
            do {
                let fd = try IO.openRead(full)
                defer { close(fd) }
                var off: Int64 = 0
                while true {
                    if isCancelled() {
                        r.cancelled = true
                        break
                    }
                    let n = try IO.pread(fd, buf, chunkSize, off, path: full)
                    if n == 0 { break }
                    running.forEach { $0.1.update(buf, n) }
                    off += Int64(n)
                    r.bytesRead += Int64(n)
                    progress?(e.path, r.bytesRead)
                }
                if r.cancelled { break }
                let sizeOK = e.size.map { $0 == off } ?? true
                let hashOK = running.allSatisfy { HashAlgo.equal($0.0.algorithm, $0.0.value, $0.1.value()) }
                // A record with no hash this version knows proves nothing: never "matches".
                if sizeOK && hashOK && !running.isEmpty { r.ok += 1 } else { r.changed.append(e.path) }
            } catch {
                r.unreadable.append((e.path, "\(error)"))
            }
        }
        if !r.cancelled { addExtra(&r, cardPath, expected.map(\.path) + r.failedAtCopy, ignore) }
        r.finished = Date()
        return r
    }

    /// `_REPORT/*.xxh64` and `*.md5` ("<hash>  <path>", "\n" or "\r\n"); nil when there is none.
    static func hashLists(_ cardPath: String) -> [Expected]? {
        let dir = cardPath + "/" + Layout.reportDirName
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: dir) else { return nil }
        var byPath: [String: Expected] = [:]
        var order: [String] = []
        var any = false
        for name in names.sorted() where !name.hasPrefix(".") {
            let lower = name.lowercased()
            let algorithm = lower.hasSuffix(".xxh64") ? "xxh64" : lower.hasSuffix(".md5") ? "md5" : nil
            guard let algorithm, let data = FileManager.default.contents(atPath: dir + "/" + name) else { continue }
            any = true
            for line in OffloadReport.listLines(data) {
                guard let sep = line.range(of: "  ") else { continue }
                let path = String(line[sep.upperBound...]), value = String(line[..<sep.lowerBound])
                let key = path.lowercased()
                if byPath[key] == nil { order.append(key) }
                let had = byPath[key]?.hashes ?? []
                byPath[key] = Expected(path: path, size: nil, hashes: had + [MHLHash(algorithm: algorithm, value: value, action: "original")])
            }
        }
        return any ? order.compactMap { byPath[$0] }.sorted { $0.path < $1.path } : nil
    }

    static func addExtra(_ r: inout VerifyResult, _ cardPath: String, _ known: [String], _ ignore: [String]) {
        let k = Set(known.map { $0.lowercased() })
        guard let scan = try? Scanner.scan(cardPath) else { return }
        r.extra = scan.files.map(\.relPath).filter { p in
            let lower = p.lowercased()
            return !k.contains(lower) && !ours.contains { lower.hasPrefix($0.lowercased()) } && !ignored(p, ignore)
        }
    }

    /// A card folder (ascmhl/, or a report or checksum list in _REPORT/) is itself; a day folder means every card folder
    /// in it.
    public static func cardFolders(in folder: String) -> [String] {
        let path = folder.trimmingTrailingSlash()
        if isCardFolder(path) { return [path] }
        let subs = ((try? FileManager.default.contentsOfDirectory(atPath: path)) ?? []).filter { !$0.hasPrefix(".") }.sorted()
        return subs.map { path + "/" + $0 }.filter(isCardFolder)
    }

    static func isCardFolder(_ p: String) -> Bool {
        var isDir: ObjCBool = false
        if FileManager.default.fileExists(atPath: p + "/" + MHL.dirName, isDirectory: &isDir), isDir.boolValue { return true }
        let names = (try? FileManager.default.contentsOfDirectory(atPath: p + "/" + Layout.reportDirName)) ?? []
        return names.contains { n in
            let l = n.lowercased()
            return l == CardRecord.mainReport || l.hasSuffix(".xxh64") || l.hasSuffix(".md5")
        }
    }

    /// ASC MHL ignore patterns (gitwildmatch): a name without "/" matches any folder level of the path, "name/" only a
    /// folder, a pattern with "/" from the card root; "*" and "?" never cross "/", "**" does; any case. A matching
    /// folder ignores everything under it.
    public static func ignored(_ relPath: String, _ patterns: [String]) -> Bool {
        let parts = relPath.split(separator: "/").map(String.init)
        for raw in patterns {
            let folderOnly = raw.hasSuffix("/")
            let pattern = folderOnly ? String(raw.dropLast()) : raw
            if pattern.isEmpty { continue }
            if pattern.contains("/") {
                let body = wildcard(pattern.hasPrefix("/") ? String(pattern.dropFirst()) : pattern)
                if relPath.range(of: "^" + body + (folderOnly ? "/.*" : "(/.*)?") + "$", options: [.regularExpression, .caseInsensitive]) != nil {
                    return true
                }
                continue
            }
            let name = "^" + wildcard(pattern) + "$"
            let candidates = folderOnly ? Array(parts.dropLast()) : parts
            if candidates.contains(where: { $0.range(of: name, options: [.regularExpression, .caseInsensitive]) != nil }) { return true }
        }
        return false
    }

    static func wildcard(_ glob: String) -> String {
        var rx = ""
        let c = Array(glob)
        var i = 0
        while i < c.count {
            if c[i] == "*", i + 1 < c.count, c[i + 1] == "*" {
                i += 1
                if i + 1 < c.count, c[i + 1] == "/" {
                    i += 1
                    rx += "(.*/)?"
                } else {
                    rx += ".*"
                }
            } else if c[i] == "*" {
                rx += "[^/]*"
            } else if c[i] == "?" {
                rx += "[^/]"
            } else {
                rx += NSRegularExpression.escapedPattern(for: String(c[i]))
            }
            i += 1
        }
        return rx
    }
}
