import XCTest
@testable import OffloadKit

final class ReportTests: XCTestCase {
    func testSuccessAndEject() {
        XCTAssertTrue(Reports.sample().safeToFormat)
        let notVerified = Reports.sample(verified: false)
        XCTAssertTrue(notVerified.success)
        XCTAssertFalse(notVerified.safeToFormat)
        let failed = Reports.sample(destError: "boom")
        XCTAssertFalse(failed.success)
        XCTAssertEqual(failed.failedFiles.map(\.path), ["CLIP/c 02.mov"])
    }

    // Review focus 1: a cancelled verification leaves files "written" or already "verified" — both are fine then.
    func testCancelledVerifyWrittenOrVerifiedFilesAreNotFailures() {
        for state in ["verified", "written"] {
            let r = Reports.sample(verified: false, states: [state, state])
            XCTAssertTrue(r.success, state)
            XCTAssertFalse(r.safeToFormat, state)
            XCTAssertTrue(r.failedFiles.isEmpty, state)
        }
        let mixed = Reports.sample(verified: false, states: ["verified", "failed"])
        XCTAssertEqual(mixed.failedFiles.map(\.path), ["CLIP/c 02.mov"])
    }

    func testVerifiedReportStillNeedsVerifiedState() {
        let r = Reports.sample(verified: true, states: ["verified", "written"])
        XCTAssertEqual(r.failedFiles.map(\.path), ["CLIP/c 02.mov"])
    }

    func testBuildNotVerifiedCountsWrittenAndVerifiedAsOK() {
        let scan = SourceScan(root: "/c", files: [SourceFile(relPath: "a", size: 1, mtime: timespec()),
                                                  SourceFile(relPath: "b", size: 2, mtime: timespec())],
                              dirs: [], skipped: [])
        let plan = OffloadPlan(source: "/c", scan: scan, date: "2026-10-04", cardName: "A001", destRoots: ["/d1"])
        let r = OffloadReport.build(plan: plan, hashes: [1, 2], sourceErrors: [nil, nil],
                                    destResults: [[DestFileResult(state: .verified, error: nil), DestFileResult(state: .written, error: nil)]],
                                    verified: false, started: Date(), copyFinished: Date(), finished: Date())
        XCTAssertEqual(r.destinations[0].filesOK, 2)
        XCTAssertEqual(r.destinations[0].filesFailed, 0)
        XCTAssertTrue(r.success)
        XCTAssertFalse(r.safeToFormat)
    }

    func testJSONKeysAreTheSharedFormatSorted() throws {
        let data = try Reports.sample().jsonData()
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let expected = ["cardName", "clips", "copyFinished", "date", "destinations", "files", "finished", "hashAlgorithm",
                        "skipped", "source", "sourceFilesystem", "started", "tool", "totalBytes", "totalFiles", "verified", "version"]
        XCTAssertEqual(obj.keys.sorted(), expected)
        XCTAssertEqual(obj["started"] as? String, "2026-10-04T08:00:00Z")
        XCTAssertEqual(obj["tool"] as? String, "offload")
        let file = try XCTUnwrap((obj["files"] as? [[String: Any]])?.first)
        XCTAssertEqual(file.keys.sorted(), ["destinations", "errors", "modified", "path", "size", "xxh64"])
        XCTAssertTrue((file["errors"] as? [Any])?.first is NSNull, "null inside arrays stays")
        // The file itself has the keys in sorted order (top level, two-space indent).
        let text = String(decoding: data, as: UTF8.self)
        let positions = expected.compactMap { text.range(of: "\n  \"\($0)\"")?.lowerBound }
        XCTAssertEqual(positions.count, expected.count)
        XCTAssertEqual(positions, positions.sorted())
    }

    func testReadsAReportWrittenByWindows() throws {
        let json = """
        {
          "cardName": "A001", "clips": [], "copyFinished": "2026-10-04T08:00:10Z", "date": "2026-10-04",
          "destinations": [{"cardPath": "D:/dst/2026-10-04/A001", "filesFailed": 0, "filesOK": 1, "filesystem": "NTFS",
                            "root": "D:/dst", "unbuffered": false}],
          "files": [{"destinations": ["verified"], "errors": [null], "modified": "2026-10-04T08:00:00Z",
                     "path": "CLIP/a.mov", "size": 1, "xxh64": "0123456789abcdef"}],
          "finished": "2026-10-04T08:00:15Z", "hashAlgorithm": "xxh64", "proxyEncoder": "h264_nvenc", "skipped": [],
          "source": "E:\\\\", "sourceFilesystem": "exFAT", "started": "2026-10-04T08:00:00Z", "tool": "offload",
          "totalBytes": 1, "totalFiles": 1, "verified": true, "version": "0.1.0-win"
        }
        """
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        let r = try dec.decode(OffloadReport.self, from: Data(json.utf8))
        XCTAssertEqual(r.version, "0.1.0-win")
        XCTAssertEqual(r.destinations.first?.filesOK, 1)
        XCTAssertTrue(r.safeToFormat)
    }

    func testHashListIsHashTwoSpacesPath() {
        XCTAssertEqual(Reports.sample().hashList, "0123456789abcdef  CLIP/A&B.mov\nfedcba9876543210  CLIP/c 02.mov\n")
    }
}
