import XCTest
@testable import OffloadKit

final class BackupVerifierTests: XCTestCase {
    /// A real offload of two files into `dest`; returns the card folder.
    func offload(_ dir: TempDir) throws -> String {
        let card = dir.sub("card"), dest = dir.sub("dest")
        _ = TestFS.writeRandom(card + "/CLIP/A.mov", size: 3000, seed: 1)
        _ = TestFS.writeRandom(card + "/CLIP/B.mov", size: 5000, seed: 2)
        var o = OffloadOptions()
        o.allowSourceVolume = true
        let e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: [dest], cardName: "A001", date: "2026-10-08")
        _ = try e.run(plan)
        return plan.cardPaths[0]
    }

    func testAllGood() throws {
        let r = try BackupVerifier.verify(try offload(tempDir()), progress: nil, isCancelled: { false })
        XCTAssertEqual(r.basis, "ascmhl")
        XCTAssertEqual(r.ok, 2)
        XCTAssertEqual(r.generations, 1)
        XCTAssertTrue(r.success)
        XCTAssertFalse(r.unfinished)
        XCTAssertTrue(r.reportInsideCard)
        XCTAssertEqual(r.bytesRead, 8000)
        // "xxHash64" alone until the engine writes xxh128 too (task 51): then "xxHash64 + xxHash128".
        XCTAssertTrue(VerifyReport.summary(r).text.hasPrefix("Verified: all 2 files match (ASC MHL, 1 generation, xxHash64"),
                      VerifyReport.summary(r).text)
        XCTAssertEqual(VerifyReport.summary(r).tone, .ok)
        XCTAssertTrue(VerifyReport.cliSummary(r, seconds: 1).hasSuffix("— all OK."))
    }

    func testChangedMissingExtra() throws {
        let card = try offload(tempDir())
        let fh = try FileHandle(forUpdating: URL(fileURLWithPath: card + "/CLIP/A.mov"))
        try fh.seek(toOffset: 10)
        let old = try XCTUnwrap(try FileHandle(forReadingFrom: URL(fileURLWithPath: card + "/CLIP/A.mov")).readToEnd())[10]
        try fh.write(contentsOf: Data([old ^ 0xFF]))
        try fh.close()
        try FileManager.default.removeItem(atPath: card + "/CLIP/B.mov")
        try Data("x".utf8).write(to: URL(fileURLWithPath: card + "/CLIP/new.txt"))
        try Data("x".utf8).write(to: URL(fileURLWithPath: card + "/_PROXY_note.txt"))
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertEqual(r.changed, ["CLIP/A.mov"])
        XCTAssertEqual(r.missing, ["CLIP/B.mov"])
        XCTAssertEqual(r.extra.sorted(), ["CLIP/new.txt", "_PROXY_note.txt"])
        XCTAssertFalse(r.success)
        XCTAssertEqual(VerifyReport.summary(r).text, "Mismatch 1 file, missing 1, unreadable 0 — details in the verification report.")
        XCTAssertEqual(VerifyReport.summary(r).tone, .error)
        XCTAssertEqual(VerifyReport.warnings(r), ["2 files extra (not in the MHL)."])
        XCTAssertEqual(VerifyReport.machineLines(r), ["MISMATCH  CLIP/A.mov", "MISSING   CLIP/B.mov", "EXTRA     CLIP/new.txt", "EXTRA     _PROXY_note.txt"])
        XCTAssertTrue(VerifyReport.cliSummary(r, seconds: 1).contains("PROBLEMS FOUND: mismatch 1, missing 1, unreadable 0, failed during the copy 0, extra 2."))
    }

    func testSizeAloneIsAMismatch() throws {
        let card = try offload(tempDir())
        let fh = try FileHandle(forUpdating: URL(fileURLWithPath: card + "/CLIP/A.mov"))
        try fh.seekToEnd()
        try fh.write(contentsOf: Data([1]))
        try fh.close()
        XCTAssertEqual(try BackupVerifier.verify(card).changed, ["CLIP/A.mov"])
    }

    func testHashListFallbackWithCRLF() throws {
        let card = try offload(tempDir())
        try FileManager.default.removeItem(atPath: card + "/ascmhl")
        let list = card + "/_REPORT/A001.xxh64"
        let text = try String(contentsOfFile: list, encoding: .utf8).replacingOccurrences(of: "\n", with: "\r\n")
        try text.write(toFile: list, atomically: true, encoding: .utf8)
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertEqual(r.basis, "hashList")
        XCTAssertEqual(r.generations, 0)
        XCTAssertEqual(r.ok, 2)
        XCTAssertTrue(r.success)
        XCTAssertTrue(VerifyReport.summary(r).text.hasSuffix("(checksum list, xxHash64)."), VerifyReport.summary(r).text)
    }

    func testNoRecordIsRefused() throws {
        let card = try offload(tempDir())
        try FileManager.default.removeItem(atPath: card + "/ascmhl")
        try FileManager.default.removeItem(atPath: card + "/_REPORT/A001.xxh64")
        XCTAssertThrowsError(try BackupVerifier.verify(card)) { XCTAssertEqual(($0 as? OffloadError)?.code, .noHashRecord) }
    }

    func testCancelledWritesNoReport() throws {
        let card = try offload(tempDir())
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { true })
        XCTAssertTrue(r.cancelled)
        XCTAssertEqual(VerifyReport.summary(r).text, "Verification cancelled after 0 / 2 files")
        XCTAssertThrowsError(try VerifyReport.write(r))
        XCTAssertFalse(TestFS.files(in: card + "/_REPORT").contains { $0.contains("_verify_") })
    }

    // Review focus 2: an unfinished generation is never "verified".
    func testUnfinishedNeverOk() throws {
        let card = try offload(tempDir())
        var rep = try OffloadReport.read(card + "/_REPORT/offload_report.json")
        rep.verifyIncomplete = true
        try rep.jsonData().write(to: URL(fileURLWithPath: card + "/_REPORT/offload_report.json"))
        let r = try BackupVerifier.verify(card, progress: nil, isCancelled: { false })
        XCTAssertTrue(r.success, "the files themselves match")
        XCTAssertTrue(r.unfinished)
        XCTAssertEqual(VerifyReport.summary(r).tone, .warning)
        XCTAssertTrue(VerifyReport.summary(r).text.hasPrefix(VerifyReport.unfinishedText))
        XCTAssertTrue(VerifyReport.cliSummary(r, seconds: 1).contains("BACKUP UNFINISHED"))
    }

    func testFailedAtCopyAndSuperseded() throws {
        let card = try offload(tempDir())
        let h = try XCTUnwrap(MHLReader.read(cardPath: card))
        let a = try XCTUnwrap(h.record("CLIP/A.mov"))
        // A generation 2 that recorded A as failed: verified against generation 1.
        var rep = try OffloadReport.read(card + "/_REPORT/offload_report.json")
        rep.generation = 2
        rep.files = rep.files.filter { $0.path == a.path }.map { var f = $0; f.destinations = ["failed"]; return f }
        try MHL.write(report: rep, cardPath: card)
        let r = try BackupVerifier.verify(card)
        XCTAssertEqual(r.failedSuperseded, ["CLIP/A.mov"])
        XCTAssertEqual(r.ok, 2)
        XCTAssertTrue(r.success)
        XCTAssertTrue(VerifyReport.warnings(r).contains {
            $0 == "1 file has the record \"failed during the copy\" in the newest generation — verified against an older valid generation."
        })
    }

    func testIgnorePatterns() {
        XCTAssertTrue(BackupVerifier.ignored("a/b/.DS_Store", [".DS_Store"]))
        XCTAssertTrue(BackupVerifier.ignored("_REPORT/x.html", ["_REPORT"]))
        XCTAssertTrue(BackupVerifier.ignored("x/tmp/y", ["tmp/"]))
        XCTAssertFalse(BackupVerifier.ignored("tmp", ["tmp/"]), "name/ is a folder only")
        XCTAssertTrue(BackupVerifier.ignored("CLIP/a.TMP", ["*.tmp"]))
        XCTAssertFalse(BackupVerifier.ignored("CLIP/a.tmp", ["/*.tmp"]), "anchored at the card folder")
        XCTAssertTrue(BackupVerifier.ignored("a/b/c.x", ["a/**/c.x"]))
    }

    func testDayFolderMeansEveryCard() throws {
        let card = try offload(tempDir())
        XCTAssertEqual(BackupVerifier.cardFolders(in: (card as NSString).deletingLastPathComponent), [card])
        XCTAssertEqual(BackupVerifier.cardFolders(in: card), [card])
    }
}
