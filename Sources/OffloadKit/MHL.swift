import CommonCrypto
import Foundation

/// ASC MHL v2 (urn:ASC:MHL:v2.0) — the industry-standard hash record, written as the first generation
/// into `<card>/ascmhl/` so `ascmhl verify` and other DIT tools can check the copy later.
/// Optional directory/root hashes are omitted; per-file xxh64 hashes are what verification relies on.
enum MHL {
    static let dirName = "ascmhl"
    static let chainName = "ascmhl_chain.xml"
    /// Our own folders inside the card folder are not camera data.
    static let ignorePatterns = [".DS_Store", "ascmhl", "ascmhl/", Layout.reportDirName, Layout.proxyDirName]

    static func write(report: OffloadReport, cardPath: String) throws {
        guard let d = report.destinations.firstIndex(where: { $0.cardPath == cardPath }) else { return }
        let dir = cardPath + "/" + dirName
        try IO.mkdirs(dir)

        let now = Date()
        let stamp = isoDate(now)
        let utc = DateFormatter()
        utc.locale = Locale(identifier: "en_US_POSIX")
        utc.timeZone = TimeZone(identifier: "UTC")
        utc.dateFormat = "yyyy-MM-dd_HHmmss'Z'"
        let folder = (cardPath as NSString).lastPathComponent
        let fileName = String(format: "%04d_%@_%@.mhl", 1, folder, utc.string(from: now))

        var x = """
        <?xml version="1.0" encoding="UTF-8"?>
        <hashlist version="2.0" xmlns="urn:ASC:MHL:v2.0">
          <creatorinfo>
            <creationdate>\(stamp)</creationdate>
            <hostname>\(esc(ProcessInfo.processInfo.hostName))</hostname>
            <tool version="\(esc(report.version))">\(esc(report.tool))</tool>
            <comment>Offload of \(esc(report.cardName)) from \(esc(report.source))</comment>
          </creatorinfo>
          <processinfo>
            <process>transfer</process>
            <ignore>

        """
        for p in ignorePatterns + report.skipped.map({ ($0 as NSString).lastPathComponent }) {
            x += "      <pattern>\(esc(p))</pattern>\n"
        }
        x += "    </ignore>\n  </processinfo>\n  <hashes>\n"
        for f in report.files {
            guard let h = f.xxh64 else { continue }
            let action = f.destinations[d] == FileState.verified.rawValue || f.destinations[d] == FileState.written.rawValue ? "original" : "failed"
            x += "    <hash>\n"
            x += "      <path size=\"\(f.size)\" lastmodificationdate=\"\(isoDate(f.modified))\">\(esc(f.path))</path>\n"
            x += "      <xxh64 action=\"\(action)\" hashdate=\"\(stamp)\">\(h)</xxh64>\n"
            x += "    </hash>\n"
        }
        x += "  </hashes>\n</hashlist>\n"

        let data = Data(x.utf8)
        try IO.writeFileDurably(dir + "/" + fileName, data)
        let chain = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ascmhldirectory xmlns="urn:ASC:MHL:DIRECTORY:v2.0">
          <hashlist sequencenr="1">
            <path>\(esc(fileName))</path>
            <c4>\(c4(data))</c4>
          </hashlist>
        </ascmhldirectory>

        """
        try IO.writeFileDurably(dir + "/" + chainName, Data(chain.utf8))
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
