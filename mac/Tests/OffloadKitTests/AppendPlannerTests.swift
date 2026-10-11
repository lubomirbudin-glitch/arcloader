import XCTest
@testable import OffloadKit

final class AppendPlannerTests: XCTestCase {
    struct Setup {
        let card: String
        let cardPaths: [String]
    }

    /// A card with two clips copied to two destinations.
    func setup(_ dir: TempDir) throws -> Setup {
        let card = dir.sub("card")
        _ = TestFS.writeRandom(card + "/CLIP/A001C001.mov", size: 3000, seed: 1)
        _ = TestFS.writeRandom(card + "/CLIP/A001C002.mov", size: 3000, seed: 2)
        let roots = [dir.sub("D"), dir.sub("E")]
        var o = OffloadOptions()
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: roots, cardName: "A001", date: "2026-10-08")
        _ = try e.run(plan)
        return Setup(card: card, cardPaths: plan.cardPaths)
    }

    func code(_ f: () throws -> Void) -> PreflightCode? {
        do {
            try f()
            return nil
        } catch {
            return (error as? OffloadError)?.code
        }
    }

    func testNewFilesOnly() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/SUB/A001C003.mov", size: 3000, seed: 3)
        let p = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths)
        XCTAssertEqual(p.generation, 2)
        XCTAssertEqual(p.newFiles.map(\.relPath), ["CLIP/SUB/A001C003.mov"])
        XCTAssertEqual(p.newDirs, ["CLIP", "CLIP/SUB"])
        XCTAssertEqual(p.unverifiedEarlier, 0)
        XCTAssertEqual(p.hashAlgorithms, ["xxh64"])
        XCTAssertEqual(p.cardName, "A001")
        XCTAssertEqual(p.date, "2026-10-08")
        XCTAssertEqual(p.fullScan.files.count, 3)
        XCTAssertEqual(p.destRoots.map { ($0 as NSString).lastPathComponent }, ["D", "E"])
    }

    func testNothingNew() throws {
        let s = try setup(tempDir())
        XCTAssertTrue(try AppendPlanner.plan(s.card, cardPaths: s.cardPaths).newFiles.isEmpty)
    }

    func testSizeConflict() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C002.mov", size: 3001, seed: 2)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendConflict)
        do {
            _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths)
        } catch {
            let text = "\(error)"
            XCTAssertTrue(text.hasPrefix("Append stopped — nothing was copied."), text)
            XCTAssertTrue(text.contains("CLIP/A001C002.mov: different size or date (card 3001 B · "), text)
            XCTAssertTrue(text.hasSuffix("Make a full copy (it goes to A001_2)."), text)
        }
    }

    // Review focus 3.
    func testDivergedGenerationsRefused() throws {
        let s = try setup(tempDir())
        var r = try OffloadReport.read(s.cardPaths[0] + "/_REPORT/offload_report.json")
        r.generation = 2
        r.files = []
        try r.write(cardPath: s.cardPaths[0], includeHashRecords: true)   // generation 2 only on D
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendDiverged)
    }

    func testReportsWithoutChainRefused() throws {
        let s = try setup(tempDir())
        for p in s.cardPaths { try Data("{}".utf8).write(to: URL(fileURLWithPath: p + "/_REPORT/A001_report_0002.html")) }
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendDiverged)
    }

    func testDamagedChainRefused() throws {
        let s = try setup(tempDir())
        let dir = s.cardPaths[1] + "/ascmhl"
        let gen = try XCTUnwrap(TestFS.files(in: dir).first { $0.hasPrefix("0001_") })
        try Data("<changed/>".utf8).write(to: URL(fileURLWithPath: dir + "/" + gen))
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendMhlDamaged)
        try FileManager.default.removeItem(atPath: dir)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendNoMhl)
    }

    func testMissingReportOrFolderRefused() throws {
        let s = try setup(tempDir())
        try FileManager.default.removeItem(atPath: s.cardPaths[1] + "/_REPORT/offload_report.json")
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendNoReport)
        try FileManager.default.removeItem(atPath: s.cardPaths[1])
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendUnavailable)
    }

    func testDifferentFolderNamesRefused() throws {
        let s = try setup(tempDir())
        let other = (s.cardPaths[1] as NSString).deletingLastPathComponent + "/A001_2"
        try FileManager.default.moveItem(atPath: s.cardPaths[1], toPath: other)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: [s.cardPaths[0], other]) }, .appendDiverged)
    }

    func testUnknownFileOnDestinationIsConflict() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        _ = TestFS.writeRandom(s.cardPaths[1] + "/CLIP/A001C003.mov", size: 3000, seed: 9)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendConflict)
    }

    func testOtherCardRefused() throws {
        let s = try setup(tempDir())
        let other = tempDir().sub("other")
        _ = TestFS.writeRandom(other + "/X/B001C001.mov", size: 10, seed: 5)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(other, cardPaths: s.cardPaths) }, .appendOtherCard)
    }

    func testMissingAndFailedFiles() throws {
        let s = try setup(tempDir())
        try FileManager.default.removeItem(atPath: s.cardPaths[0] + "/CLIP/A001C001.mov")
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendMissingFile)
    }

    func testFailedFileThatIsOnTheDiskRefused() throws {
        let s = try setup(tempDir())
        for p in s.cardPaths {
            var r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            r.files = r.files.map { f in var x = f; if x.path == "CLIP/A001C001.mov" { x.destinations = ["failed", "failed"] }; return x }
            try r.jsonData().write(to: URL(fileURLWithPath: p + "/_REPORT/offload_report.json"))
        }
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendFailedFile)
        // Not on the disks (a cancelled copy): copied again by the append.
        for p in s.cardPaths { try FileManager.default.removeItem(atPath: p + "/CLIP/A001C001.mov") }
        XCTAssertEqual(try AppendPlanner.plan(s.card, cardPaths: s.cardPaths).newFiles.map(\.relPath), ["CLIP/A001C001.mov"])
    }

    func testFatHourShiftOnTheCardIsNoConflict() throws {
        let s = try setup(tempDir())
        let p = s.card + "/CLIP/A001C001.mov"
        let m = try XCTUnwrap(try FileManager.default.attributesOfItem(atPath: p)[.modificationDate] as? Date)
        try FileManager.default.setAttributes([.modificationDate: m.addingTimeInterval(3600)], ofItemAtPath: p)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths, sourceFilesystem: "exfat") }, .appendConflict)
        XCTAssertNil(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths, sourceFilesystem: "msdos") })
    }

    func testPartNameTakenIsConflict() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 10, seed: 3)
        _ = TestFS.writeRandom(s.card + "/CLIP/.A001C003.mov.offload-part", size: 10, seed: 4)
        XCTAssertEqual(code { _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths) }, .appendConflict)
    }

    func testUnverifiedEarlierCounted() throws {
        let s = try setup(tempDir())
        for p in s.cardPaths {
            var r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            r.verified = false
            r.files = r.files.map { var f = $0; f.destinations = f.destinations.map { _ in "written" }; return f }
            try r.jsonData().write(to: URL(fileURLWithPath: p + "/_REPORT/offload_report.json"))
        }
        XCTAssertEqual(try AppendPlanner.plan(s.card, cardPaths: s.cardPaths).unverifiedEarlier, 2)
    }

    func testPlannerWritesNothing() throws {
        let s = try setup(tempDir())
        _ = TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        let before = s.cardPaths.map { TestFS.files(in: $0) }
        _ = try AppendPlanner.plan(s.card, cardPaths: s.cardPaths)
        XCTAssertEqual(s.cardPaths.map { TestFS.files(in: $0) }, before)
    }
}
