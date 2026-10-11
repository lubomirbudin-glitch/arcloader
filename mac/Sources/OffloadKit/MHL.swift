import CommonCrypto
import Foundation

/// ASC MHL v2 (urn:ASC:MHL:v2.0) — the industry-standard hash record in `<card>/ascmhl/`, so `ascmhl verify` and other
/// DIT tools can check the copy later. A full copy writes generation 0001; an append (spec 2.2, R5) writes generation N
/// with only its new files and adds it to the chain — the earlier generation files are never touched.
/// Optional directory/root hashes are omitted; per-file xxh64 hashes are what verification relies on.
public enum MHL {
    public static let dirName = "ascmhl"
    public static let chainName = "ascmhl_chain.xml"
    /// Our own folders inside the card folder are not camera data.
    static let ignorePatterns = [".DS_Store", "ascmhl", "ascmhl/", Layout.reportDirName, Layout.proxyDirName]

    static func write(report: OffloadReport, cardPath: String) throws {
        try write(report: report, cardPath: cardPath, now: Date(), host: ProcessInfo.processInfo.hostName)
    }

    static func write(report: OffloadReport, cardPath: String, now: Date, host: String) throws {
        guard let d = report.destinations.firstIndex(where: { $0.cardPath == cardPath }) else { return }
        let dir = cardPath + "/" + dirName
        try IO.mkdirs(dir)

        let generation = report.generationNumber
        // Generation 1 starts a chain — never over one that is there (a commit retried, another copy).
        if generation == 1 && FileManager.default.fileExists(atPath: dir + "/" + chainName) {
            throw IOError(message: "The ASC MHL in \(cardPath) already has a chain — generation 1 was not written.")
        }
        // An append keeps the chain and the ignore list of the earlier generations (spec 2.2) — and goes right after them.
        let earlier = generation > 1 ? MHLReader.read(cardPath: cardPath) : nil
        if generation > 1 {
            let last = earlier?.chain.map(\.seq).max() ?? 0
            if last != generation - 1 {
                throw IOError(message: "The ASC MHL in \(cardPath) changed during the append (last generation \(last), the append writes \(generation)) — the generation was not written.")
            }
        }
        let stamp = isoDate(now)
        let utc = DateFormatter()
        utc.locale = Locale(identifier: "en_US_POSIX")
        utc.timeZone = TimeZone(identifier: "UTC")
        utc.dateFormat = "yyyy-MM-dd_HHmmss'Z'"
        let folder = (cardPath.trimmingTrailingSlash() as NSString).lastPathComponent
        let fileName = String(format: "%04d_%@_%@.mhl", generation, folder, utc.string(from: now))
        let listed = report.files.filter { $0.xxh64 != nil }.count
        let copied = report.files.filter { $0.xxh64 != nil && ($0.rewritten?.contains(true) ?? false) }.count
        var comment = report.isRepair ? Msg.mhlRepairComment(copied: copied, readBack: listed - copied, source: report.source)
            : generation > 1
            ? "Offload append: \(listed) new \(plural(listed, "file", "files")) from \(report.source)"
            : "Offload of \(report.cardName) from \(report.source)"
        // How this destination was verified. `<process>` stays "transfer" — the ASC MHL v2 schema allows only in-place /
        // transfer / flatten there — so the method goes into the generation's comment (as Windows).
        if let method = report.destinations[d].verifyMethod { comment += " · verification: \(method)" }
        let ignore = earlier?.generations.first?.ignore
            ?? ignorePatterns + report.skipped.map { ($0 as NSString).lastPathComponent }

        var x = """
        <?xml version="1.0" encoding="UTF-8"?>
        <hashlist version="2.0" xmlns="urn:ASC:MHL:v2.0">
          <creatorinfo>
            <creationdate>\(stamp)</creationdate>
            <hostname>\(esc(host))</hostname>
            <tool version="\(esc(report.version))">\(esc(report.tool))</tool>
            <comment>\(esc(comment))</comment>
          </creatorinfo>
          <processinfo>
            <process>transfer</process>
            <ignore>

        """
        for p in ignore {
            x += "      <pattern>\(esc(p))</pattern>\n"
        }
        x += "    </ignore>\n  </processinfo>\n  <hashes>\n"
        for (f, action) in entries(report, destination: d) {
            x += "    <hash>\n"
            x += "      <path size=\"\(f.size)\" lastmodificationdate=\"\(isoDate(f.modified))\">\(esc(f.path))</path>\n"
            x += "      <xxh64 action=\"\(action)\" hashdate=\"\(stamp)\">\(f.xxh64!)</xxh64>\n"
            if let v = f.xxh128 { x += "      <xxh128 action=\"\(action)\" hashdate=\"\(stamp)\">\(v)</xxh128>\n" }
            if let m = f.md5 { x += "      <md5 action=\"\(action)\" hashdate=\"\(stamp)\">\(m)</md5>\n" }   // spec 3.5
            x += "    </hash>\n"
        }
        x += "  </hashes>\n</hashlist>\n"

        let data = Data(x.utf8)
        // A new generation file never replaces one; generation 0001 goes into a fresh card folder.
        if generation > 1 {
            try IO.writeFileNew(dir + "/" + fileName, data)
        } else {
            try IO.writeFileDurably(dir + "/" + fileName, data)
        }

        // The chain: every earlier generation exactly as it was read, then this one — replaced atomically (temporary
        // name + rename). Writing it is the commit point of the generation (see OffloadReport.write).
        let entries = (earlier?.chain ?? []) + [MHLChainEntry(seq: generation, path: fileName, c4: c4(data))]
        var chain = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n"
        for e in entries {
            chain += "  <hashlist sequencenr=\"\(e.seq)\">\n"
            chain += "    <path>\(esc(e.path))</path>\n"
            chain += "    <c4>\(esc(e.c4))</c4>\n"
            chain += "  </hashlist>\n"
        }
        chain += "</ascmhldirectory>\n"
        try IO.writeFileDurably(dir + "/" + chainName, Data(chain.utf8))
    }

    /// The files generation `report` records for destination `d`, with their action (Windows Mhl.Entries).
    /// A repair (`rewritten`): a good copy only read back is "verified"; one not read back in this generation, or not
    /// touched, is left out; a new copy is "original"; a bad one "failed". Otherwise only a verified (or failed) copy
    /// goes in — "written" only into a generation made without (or cancelled before) its verification.
    static func entries(_ report: OffloadReport, destination d: Int) -> [(file: OffloadReport.FileEntry, action: String)] {
        report.files.compactMap { f in
            guard f.xxh64 != nil, d < f.destinations.count else { return nil }
            let state = f.destinations[d]
            if let rw = f.rewritten, d < rw.count {
                if state == FileState.pending.rawValue
                    || (!rw[d] && (state == FileState.written.rawValue || !report.verified)) { return nil }
                return (f, state == FileState.failed.rawValue ? "failed" : rw[d] ? "original" : "verified")
            }
            if state == FileState.written.rawValue && report.verified { return nil }
            return (f, state == FileState.verified.rawValue || state == FileState.written.rawValue ? "original" : "failed")
        }
    }

    /// The chain of `cardPath` has `report`'s generation already (a commit retried) — its file intact and recording
    /// exactly these files, xxh64 hashes and actions. False when the chain has not reached it yet (write it then).
    /// Throws when the chain has that generation but another one, or a damaged one — nothing is overwritten.
    static func hasGeneration(_ report: OffloadReport, cardPath: String) throws -> Bool {
        guard let d = report.destinations.firstIndex(where: { $0.cardPath == cardPath }) else { return true }
        let generation = report.generationNumber
        let dir = cardPath + "/" + dirName
        guard let entry = (MHLReader.readChain(ascmhlDir: dir) ?? []).first(where: { $0.seq == generation }) else { return false }
        let file = dir + "/" + entry.path
        guard !entry.path.contains("/"), !entry.path.contains("\\"), let data = FileManager.default.contents(atPath: file),
              c4(data) == entry.c4 else {
            throw IOError(message: "The ASC MHL in \(cardPath) has generation \(generation), but its file is missing or was changed — the generation was not written again.")
        }
        let have = try MHLReader.parseGeneration(file, seq: generation).files.map { f -> String in
            let h = f.hashes.first { $0.algorithm == "xxh64" }
            return "\(f.path)|\(h?.value ?? "")|\(h?.action ?? "")"
        }.sorted()
        let want = entries(report, destination: d).map { "\($0.file.path)|\($0.file.xxh64!)|\($0.action)" }.sorted()
        guard have == want else {
            throw IOError(message: "The ASC MHL in \(cardPath) already has a different generation \(generation) — the generation was not written again.")
        }
        return true
    }

    static func isoDate(_ d: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        f.timeZone = .current
        return f.string(from: d)
    }

    static func esc(_ s: String) -> String {
        s.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
    }

    /// C4 ID: "c4" + base58(SHA-512), left-padded with "1" to 88 characters.
    static func c4(_ data: Data) -> String {
        var digest = [UInt8](repeating: 0, count: Int(CC_SHA512_DIGEST_LENGTH))
        data.withUnsafeBytes { _ = CC_SHA512($0.baseAddress, CC_LONG(data.count), &digest) }
        return c4FromSHA512(digest)
    }

    /// The C4 ID of data whose SHA-512 is `digest` (a file hashed block by block).
    static func c4FromSHA512(_ digest: [UInt8]) -> String {
        let alphabet = Array("123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz")
        var num = digest
        var out: [Character] = []
        while num.contains(where: { $0 != 0 }) {
            var rem = 0
            var next: [UInt8] = []
            for b in num {
                let acc = rem * 256 + Int(b)
                let q = acc / 58
                rem = acc % 58
                if !next.isEmpty || q != 0 { next.append(UInt8(q)) }
            }
            out.append(alphabet[rem])
            num = next
        }
        let s = String(out.reversed())
        return "c4" + String(repeating: "1", count: max(0, 88 - s.count)) + s
    }
}
