import Darwin
import Foundation

/// Re-verifies an existing offload against the hash list in its _REPORT folder.
public enum Verifier {
    public struct Result {
        public var ok: [String] = []
        public var mismatched: [String] = []
        public var missing: [String] = []
        public var unreadable: [String] = []
        public var extra: [String] = []
        public var bytes: Int64 = 0
        public var success: Bool { mismatched.isEmpty && missing.isEmpty && unreadable.isEmpty }
    }

    public static func verify(cardPath: String, chunkSize: Int = 16 << 20, progress: ((String, Int64) -> Void)? = nil) throws -> Result {
        let reportDir = cardPath + "/" + Layout.reportDirName
        let lists = (try? FileManager.default.contentsOfDirectory(atPath: reportDir))?.filter { $0.hasSuffix(".xxh64") } ?? []
        guard let listName = lists.first else {
            throw OffloadError.preflight(Msg.noHashRecord(reportDir), code: .noHashRecord)
        }
        let text = try String(contentsOfFile: reportDir + "/" + listName, encoding: .utf8)
        var expected: [(hash: String, path: String)] = []
        for line in text.split(separator: "\n") where !line.isEmpty {
            guard let sep = line.range(of: "  ") else { continue }
            expected.append((String(line[..<sep.lowerBound]), String(line[sep.upperBound...])))
        }

        var result = Result()
        let buf = IO.allocateAligned(chunkSize)
        defer { free(buf) }
        for e in expected {
            let path = cardPath + "/" + e.path
            guard FileManager.default.fileExists(atPath: path) else { result.missing.append(e.path); continue }
            do {
                let r = try hashFile(path, chunkSize: chunkSize, buffer: buf) { n in
                    result.bytes += Int64(n)
                    progress?(e.path, result.bytes)
                }
                if hex64(r.hashes.xxh64!) == e.hash { result.ok.append(e.path) } else { result.mismatched.append(e.path) }
            } catch {
                result.unreadable.append("\(e.path): \(error)")
            }
        }

        let known = Set(expected.map(\.path))
        if let scan = try? Scanner.scan(cardPath) {
            result.extra = scan.files.map(\.relPath).filter {
                !known.contains($0) && !$0.hasPrefix(Layout.reportDirName + "/") && !$0.hasPrefix(Layout.proxyDirName + "/") && !$0.hasPrefix(MHL.dirName + "/")
            }
        }
        return result
    }
}
