import XCTest
@testable import OffloadKit

final class ReportKeysTests: XCTestCase {
    static var fixture: String {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Fixtures/windows-0.6.0-report.json").path
    }

    func testReadsEveryWindows060Key() throws {
        let r = try OffloadReport.read(Self.fixture)
        XCTAssertEqual(r.generationNumber, 2)
        XCTAssertTrue(r.isRepair)
        XCTAssertEqual(r.project, "Film X")
        XCTAssertEqual(r.algorithms, ["xxh64", "md5"])
        XCTAssertEqual(r.destinations[0].verifyMethod, "verify-in-proxy")
        XCTAssertEqual(r.destinations[1].volumeSerial, "5E6F-7A8B")
        XCTAssertEqual(r.files[0].md5, "900150983cd24fb0d6963f7d28e17f72")
        XCTAssertEqual(r.files[0].rewritten, [true, false])
        XCTAssertEqual(r.setAside?.first?.movedTo, "_REPORT/neshoda_0002/CLIP/A001C002.mov")
        XCTAssertEqual(r.pausedSeconds, 13.0, accuracy: 0.001)
        XCTAssertTrue(r.safeToFormat)
    }

    func testPendingOrIncompleteIsNeverSafeToFormat() {
        var r = Reports.sample()
        XCTAssertTrue(r.safeToFormat)
        r.verifyPending = true
        XCTAssertFalse(r.safeToFormat)
        r.verifyPending = nil
        r.verifyIncomplete = true
        XCTAssertFalse(r.safeToFormat)
    }

    func testMacNeverWritesWindowsOnlyKeysAndOmitsNils() throws {
        var r = try OffloadReport.read(Self.fixture)
        r.version = OffloadVersion.string
        let obj = try XCTUnwrap(JSONSerialization.jsonObject(with: try r.jsonData()) as? [String: Any])
        XCTAssertNil(obj["proxyEncoder"], "the Mac never writes the encoder (it is fixed)")
        let d0 = try XCTUnwrap((obj["destinations"] as? [[String: Any]])?.first)
        XCTAssertNil(d0["unbuffered"], "Windows only")
        let plain = try XCTUnwrap(JSONSerialization.jsonObject(with: try Reports.sample().jsonData()) as? [String: Any])
        for key in ["generation", "kind", "project", "hashAlgorithms", "verifyPending", "verifyIncomplete", "safeToFormatSeconds",
                    "verifyNotes", "copyPausedSeconds", "verifyPausedSeconds", "setAside"] {
            XCTAssertNil(plain[key], "\(key) is absent when not set")
        }
        XCTAssertEqual(plain["hashAlgorithm"] as? String, "xxh64")
    }

    func testFileNamesPerGeneration() {
        XCTAssertEqual(OffloadReport.jsonName(1), "offload_report.json")
        XCTAssertEqual(OffloadReport.textName(1), "offload_report.txt")
        XCTAssertEqual(OffloadReport.htmlName("A001", 1), "A001_report.html")
        XCTAssertEqual(OffloadReport.jsonName(2), "offload_report_0002.json")
        XCTAssertEqual(OffloadReport.pdfName("A001", 12), "A001_report_0012.pdf")
    }

    func testWriteFileNewNeverReplaces() throws {
        let dir = tempDir()
        let p = dir.file("x.json")   // the plan's dir.sub("x.json") would make a folder of that name (TempDir.sub)
        try IO.writeFileNew(p, Data("one".utf8))
        XCTAssertThrowsError(try IO.writeFileNew(p, Data("two".utf8))) { XCTAssertTrue($0 is FileExists) }
        XCTAssertEqual(try String(contentsOfFile: p, encoding: .utf8), "one")
        XCTAssertEqual(TestFS.files(in: dir.path), ["x.json"], "no temporary file left behind")
    }

    func testSummaryShowsPendingHashesAndPause() throws {
        var r = try OffloadReport.read(Self.fixture)
        XCTAssertTrue(r.summary.contains("Hash:        xxh64+md5"))
        XCTAssertTrue(r.summary.contains("Paused:      13.0 s (not in the speeds)"))
        r.verified = false
        r.verifyPending = true
        XCTAssertTrue(r.summary.contains("Verify:      PENDING (with the proxies)"))
    }
}
