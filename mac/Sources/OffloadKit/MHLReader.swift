import Foundation

/// One hash of a file in an ASC MHL generation; `action`: "original", "verified", "failed" …
public struct MHLHash: Equatable {
    public let algorithm: String
    public let value: String
    public let action: String?
}

public struct MHLFileRecord: Equatable {
    public let path: String
    public let size: Int64?
    public let modified: Date?
    public let hashes: [MHLHash]
    public let generation: Int
    public var previousPath: String?

    /// The copy recorded this file as failed (action="failed").
    public var failed: Bool { hashes.contains { $0.action == "failed" } }
}

public struct MHLGeneration {
    public let seq: Int
    public let fileName: String
    public let ignore: [String]
    public let files: [MHLFileRecord]
}

/// One `<hashlist>` of ascmhl_chain.xml.
public struct MHLChainEntry: Equatable {
    public let seq: Int
    public let path: String
    public let c4: String
}

/// Everything ascmhl/ of a card folder says (spec 2.4). `files`: per path the newest generation that lists it.
public struct MHLHistory {
    public let generations: [MHLGeneration]
    /// Keyed by the lower-cased path (the disk ignores case); the record keeps the path as written.
    public let files: [String: MHLFileRecord]
    /// Per path the newest record that is not `failed` — what a file whose newest record failed is verified against.
    public let lastGood: [String: MHLFileRecord]
    /// The ignore patterns of every generation.
    public let ignore: [String]
    /// The chain as read, in order (an append writes it back with one more entry); empty when there is none.
    public let chain: [MHLChainEntry]
    /// Czech texts for the verify report: a changed or missing generation, a missing chain.
    public let chainProblems: [String]

    public func record(_ path: String) -> MHLFileRecord? { files[path.lowercased()] }

    /// A report written into `<card>/_REPORT` does not break `ascmhl verify` (R8 exception, spec D.22.4).
    public var ignoresReportFolder: Bool { MHLReader.ignored(Layout.reportDirName + "/x", ignore) }
}

/// Reads every generation of `<card>/ascmhl` (port of Windows `MhlReader`) — ours, the Windows version's, and those of
/// ascmhl, Silverstack or ShotPut (paths with "\", previousPath, directory hashes, any hash type).
public enum MHLReader {
    static let ns = "urn:ASC:MHL:v2.0"
    static let dirNs = "urn:ASC:MHL:DIRECTORY:v2.0"

    static func gen(_ seq: Int) -> String { String(format: "%04d", seq) }

    /// nil when the card folder has no ascmhl/ or no generation in it (then a hash list is the only record).
    public static func read(cardPath: String) -> MHLHistory? {
        let dir = cardPath + "/" + MHL.dirName
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: dir, isDirectory: &isDir), isDir.boolValue else { return nil }
        let mhlFiles = ((try? FileManager.default.contentsOfDirectory(atPath: dir)) ?? [])
            .filter { ($0 as NSString).pathExtension.lowercased() == "mhl" && !$0.hasPrefix(".") }
        var problems: [String] = []
        let chain = readChain(ascmhlDir: dir, problems: &problems)
        var order: [(seq: Int, file: String)] = []
        if let chain {
            for c in chain {
                let path = dir + "/" + c.path
                if c.path.contains("/") || c.path.contains("\\") || !FileManager.default.fileExists(atPath: path) {
                    problems.append("The ASC MHL chain lists generation \(gen(c.seq)), but its file is missing: \(c.path)")
                    continue
                }
                if let data = FileManager.default.contents(atPath: path), MHL.c4(data) != c.c4 {
                    problems.append("ASC MHL generation \(gen(c.seq)) does not match the chain (the MHL file was changed).")
                }
                order.append((c.seq, c.path))
            }
            for extra in mhlFiles.sorted() where !chain.contains(where: { $0.path.caseInsensitiveCompare(extra) == .orderedSame }) {
                problems.append("ASC MHL file not in the chain: \(extra)")
            }
        } else if !mhlFiles.isEmpty {
            problems.append("\(MHL.chainName) is missing — generations are read by the number in the file name.")
            order = mhlFiles.map { f in (leadingNumber(f) ?? Int.max, f) }
                .sorted { $0.seq != $1.seq ? $0.seq < $1.seq : $0.file < $1.file }
        }

        var generations: [MHLGeneration] = []
        for (seq, file) in order {
            do {
                generations.append(try parseGeneration(dir + "/" + file, seq: seq))
            } catch {
                problems.append("Cannot read ASC MHL generation \(gen(seq)): \(describe(error))")
            }
        }
        if generations.isEmpty && problems.isEmpty { return nil }

        // A later generation wins; a path a later generation lists as previousPath was renamed, so its old record no
        // longer describes a file.
        var files: [String: MHLFileRecord] = [:]
        var lastGood: [String: MHLFileRecord] = [:]
        for g in generations {
            for f in g.files {
                if let prev = f.previousPath, !prev.isEmpty, prev.lowercased() != f.path.lowercased() {
                    files.removeValue(forKey: prev.lowercased())
                    lastGood.removeValue(forKey: prev.lowercased())
                }
                files[f.path.lowercased()] = f
                if !f.failed { lastGood[f.path.lowercased()] = f }
            }
        }
        var ignore: [String] = []
        for p in generations.flatMap(\.ignore) where !ignore.contains(p) { ignore.append(p) }
        return MHLHistory(generations: generations, files: files, lastGood: lastGood, ignore: ignore,
                          chain: chain ?? [], chainProblems: problems)
    }

    /// ASC MHL ignore patterns (gitwildmatch): a name without "/" matches any folder level of the path, "name/" only a
    /// folder, a pattern with "/" from the card root; "*" and "?" never cross "/", "**" does; any case. A matching
    /// folder ignores everything under it. (Task 45 moves it to BackupVerifier.)
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

    static func leadingNumber(_ name: String) -> Int? {
        let digits = name.prefix { $0.isASCII && $0.isNumber }
        guard !digits.isEmpty, name.dropFirst(digits.count).first == "_" else { return nil }
        return Int(digits)
    }

    static func describe(_ error: Error) -> String {
        let ns = error as NSError
        if let detail = ns.userInfo[NSLocalizedDescriptionKey] as? String { return detail }
        return "\(error)"
    }

    /// ascmhl_chain.xml in `ascmhlDir`, or nil when it is missing; a broken one is a problem and nil.
    public static func readChain(ascmhlDir: String, problems: inout [String]) -> [MHLChainEntry]? {
        let path = ascmhlDir + "/" + MHL.chainName
        guard FileManager.default.fileExists(atPath: path) else { return nil }
        do {
            let doc = try XMLDocument(contentsOf: URL(fileURLWithPath: path), options: [])
            var entries: [MHLChainEntry] = []
            for h in descendants(doc.rootElement(), named: "hashlist", uri: dirNs) {
                guard let seq = Int(h.attribute(forName: "sequencenr")?.stringValue ?? "0") else {
                    throw MHLFormatError("sequencenr")
                }
                let p = child(h, "path", uri: dirNs)?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                let c = child(h, "c4", uri: dirNs)?.stringValue?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                entries.append(MHLChainEntry(seq: seq, path: p, c4: c))
            }
            return entries.enumerated().sorted { $0.element.seq != $1.element.seq ? $0.element.seq < $1.element.seq : $0.offset < $1.offset }
                .map(\.element)
        } catch {
            problems.append("Cannot read \(MHL.chainName): \(describe(error))")
            return nil
        }
    }

    public static func readChain(ascmhlDir: String) -> [MHLChainEntry]? {
        var ignored: [String] = []
        return readChain(ascmhlDir: ascmhlDir, problems: &ignored)
    }

    static func parseGeneration(_ file: String, seq: Int) throws -> MHLGeneration {
        let doc = try XMLDocument(contentsOf: URL(fileURLWithPath: file), options: [])
        let root = doc.rootElement()
        var ignore: [String] = []
        for i in descendants(root, named: "ignore", uri: ns) {
            for p in children(i, "pattern", uri: ns) {
                let v = (p.stringValue ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                if !v.isEmpty { ignore.append(v) }
            }
        }
        var files: [MHLFileRecord] = []
        for hashes in descendants(root, named: "hashes", uri: ns) {
            for h in children(hashes, "hash", uri: ns) {
                guard let p = child(h, "path", uri: ns) else { continue }
                let size = (p.attribute(forName: "size")?.stringValue).flatMap { s -> Int64? in
                    s.allSatisfy { $0.isASCII && $0.isNumber } ? Int64(s) : nil
                }
                let modified = (p.attribute(forName: "lastmodificationdate")?.stringValue).flatMap(parseDate)
                var list: [MHLHash] = []
                for e in elements(h) where !(e.uri == ns && (e.localName == "path" || e.localName == "previousPath")) {
                    list.append(MHLHash(algorithm: e.localName ?? "",
                                        value: (e.stringValue ?? "").trimmingCharacters(in: .whitespacesAndNewlines),
                                        action: e.attribute(forName: "action")?.stringValue))
                }
                let previous = child(h, "previousPath", uri: ns)?.stringValue?
                    .trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: "\\", with: "/")
                let path = (p.stringValue ?? "").trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: "\\", with: "/")
                files.append(MHLFileRecord(path: path, size: size, modified: modified, hashes: list, generation: seq,
                                           previousPath: previous))
            }
        }
        return MHLGeneration(seq: seq, fileName: (file as NSString).lastPathComponent, ignore: ignore, files: files)
    }

    /// ISO 8601 with or without fractions and offset (ascmhl writes "2026-10-06T21:52:47.026650+02:00"); without an
    /// offset the time is UTC.
    static func parseDate(_ s: String) -> Date? {
        var t = s.trimmingCharacters(in: .whitespaces)
        // Fractions of any length → milliseconds (what ISO8601DateFormatter reads).
        if let dot = t.firstIndex(of: ".") {
            let afterDot = t.index(after: dot)
            let end = t[afterDot...].firstIndex { !($0.isASCII && $0.isNumber) } ?? t.endIndex
            let frac = String(t[afterDot..<end])
            t = String(t[..<dot]) + "." + String((frac + "000").prefix(3)) + String(t[end...])
        }
        if let tee = t.firstIndex(of: "T") {
            let time = t[t.index(after: tee)...]
            if !(time.contains("Z") || time.contains("+") || time.contains("-")) { t += "Z" }   // no offset: UTC
        }
        let f = ISO8601DateFormatter()
        f.formatOptions = t.contains(".") ? [.withInternetDateTime, .withFractionalSeconds] : [.withInternetDateTime]
        return f.date(from: t)
    }

    // MARK: XML helpers (namespace-aware)

    static func elements(_ e: XMLElement) -> [XMLElement] { (e.children ?? []).compactMap { $0 as? XMLElement } }

    static func children(_ e: XMLElement, _ name: String, uri: String) -> [XMLElement] {
        elements(e).filter { $0.localName == name && $0.uri == uri }
    }

    static func child(_ e: XMLElement, _ name: String, uri: String) -> XMLElement? { children(e, name, uri: uri).first }

    static func descendants(_ e: XMLElement?, named name: String, uri: String) -> [XMLElement] {
        guard let e else { return [] }
        var out: [XMLElement] = []
        var stack: [XMLElement] = [e]
        while let n = stack.popLast() {
            if n.localName == name && n.uri == uri { out.append(n) }
            stack.append(contentsOf: elements(n).reversed())
        }
        return out
    }
}

struct MHLFormatError: Error, CustomStringConvertible {
    let what: String
    init(_ what: String) { self.what = what }
    var description: String { "invalid value \(what)" }
}
