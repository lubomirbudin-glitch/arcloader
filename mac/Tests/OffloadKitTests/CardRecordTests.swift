import XCTest
@testable import OffloadKit

final class CardRecordTests: XCTestCase {
    /// Writes report `r` of `card` into `<card>/_REPORT` under its generation's name.
    private func write(_ r: OffloadReport, card: String) throws {
        try FileManager.default.createDirectory(atPath: card + "/_REPORT", withIntermediateDirectories: true)
        try r.jsonData().write(to: URL(fileURLWithPath: card + "/_REPORT/" + OffloadReport.jsonName(r.generationNumber)))
    }

    private func report(card: String, gen: Int = 1, states: [String], verified: Bool = true, paths: [String]? = nil,
                        cardPaths: [String]? = nil, serials: [String?]? = nil) -> OffloadReport {
        var r = Reports.sample(verified: verified, states: states)
        r.generation = gen > 1 ? gen : nil
        let cps = cardPaths ?? [card]
        r.destinations = cps.enumerated().map { i, p in
            var d = OffloadReport.Destination(root: (p as NSString).deletingLastPathComponent, cardPath: p, filesystem: "apfs",
                                              filesOK: 2, filesFailed: 0)
            d.volumeSerial = serials?[i]
            return d
        }
        if let paths { for i in r.files.indices { r.files[i].path = paths[i] } }
        r.files = r.files.map { f in
            var x = f
            x.destinations = Array(repeating: f.destinations[0], count: cps.count)
            x.errors = Array(repeating: nil, count: cps.count)
            return x
        }
        return r
    }

    func testNewestGenerationWinsPerPath() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try write(report(card: card, states: ["failed", "verified"]), card: card)
        try write(report(card: card, gen: 2, states: ["verified", "verified"], paths: ["CLIP/A&B.mov", "CLIP/new.mov"]), card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertEqual(rec.generations.map(\.generation), [1, 2])
        XCTAssertEqual(rec.files.count, 3)
        XCTAssertEqual(rec.files["clip/a&b.mov"]?.generation, 2)
        XCTAssertTrue(rec.failedFiles.isEmpty)
        XCTAssertTrue(rec.verified)
        XCTAssertEqual(rec.highestReportNumber, 2)
    }

    func testMaxGenerationLeavesOutLaterOnes() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try write(report(card: card, states: ["verified", "verified"]), card: card)
        try write(report(card: card, gen: 2, states: ["failed", "verified"], paths: ["CLIP/A&B.mov", "CLIP/new.mov"]), card: card)
        let rec = CardRecord.load(card, maxGeneration: 1, serial: .some(nil))
        XCTAssertEqual(rec.generations.map(\.generation), [1])
        XCTAssertEqual(rec.highestReportNumber, 1)
        XCTAssertTrue(rec.verified)
    }

    // Review focus 2.
    func testPendingOrIncompleteNeverVerified() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        var r = report(card: card, states: ["verified", "verified"])
        r.verifyPending = true
        try write(r, card: card)
        let pending = CardRecord.load(card, serial: .some(nil))
        XCTAssertTrue(pending.verifyPending)
        XCTAssertFalse(pending.verified)
        XCTAssertFalse(CardRecord.readBack(pending.files["clip/a&b.mov"]!, cardPath: card, serial: nil))
        r.verifyPending = nil
        r.verifyIncomplete = true
        try write(r, card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertTrue(rec.incomplete)
        XCTAssertFalse(rec.verified)
        XCTAssertFalse(CardRecord.readBack(rec.files["clip/a&b.mov"]!, cardPath: card, serial: nil))
    }

    func testPendingOnAnotherDestinationMakesThisOneIncomplete() throws {
        let root = tempDir()
        let d1 = root.sub("D", "2026-10-08", "A001"), d2 = root.sub("E", "2026-10-08", "A001")
        let here = report(card: d1, states: ["verified", "verified"], cardPaths: [d1, d2])
        try write(here, card: d1)
        var there = here
        there.verifyPending = true
        try write(there, card: d2)
        let rec = CardRecord.load(d1, serial: .some(nil))
        XCTAssertTrue(rec.incomplete, "the other destination's result is not written yet")
        XCTAssertFalse(rec.verified)
    }

    // A "written" copy was not read back; a failed one is failed (not merely unverified). In a report whose
    // verification ran to the end, "written" counts as failed too (OffloadReport.isGood, as Windows IsFailed).
    func testWrittenIsNotReadBackButFailedCountsAsFailed() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try write(report(card: card, states: ["written", "failed"], verified: false), card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertFalse(rec.verified)
        XCTAssertEqual(rec.failedFiles, ["CLIP/c 02.mov"])
        XCTAssertFalse(CardRecord.readBack(rec.files["clip/a&b.mov"]!, cardPath: card, serial: nil))

        try write(report(card: card, states: ["written", "failed"], verified: true), card: card)
        let read = CardRecord.load(card, serial: .some(nil))
        XCTAssertFalse(read.verified)
        XCTAssertEqual(read.failedFiles, ["CLIP/A&B.mov", "CLIP/c 02.mov"])
        XCTAssertFalse(CardRecord.readBack(read.files["clip/a&b.mov"]!, cardPath: card, serial: nil))
    }

    func testForeignPathsNeedEveryColumnVerified() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        var r = report(card: card, states: ["verified", "verified"], cardPaths: [#"D:\Z\2026-10-08\A001"#, #"E:\Z\2026-10-08\A001"#])
        r.files[0].destinations = ["verified", "written"]
        try write(r, card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertFalse(CardRecord.readBack(rec.files["clip/a&b.mov"]!, cardPath: card, serial: nil), "no column: every column must say verified")
        XCTAssertTrue(CardRecord.readBack(rec.files["clip/c 02.mov"]!, cardPath: card, serial: nil))
        XCTAssertFalse(rec.verified)
    }

    func testSerialMatchesWindowsReportOnSameDisk() throws {
        let root = tempDir()
        let card = root.sub("Z", "2026-10-08", "A001")
        var r = report(card: card, states: ["verified", "verified"], cardPaths: [#"D:\Z\2026-10-08\A001"#, #"E:\Z\2026-10-08\A001"#],
                       serials: ["1A2B-3C4D", "5E6F-7A8B"])
        r.files[0].destinations = ["written", "verified"]
        try write(r, card: card)
        // A folder under "/Z/2026-10-08/A001" of a disk whose serial is 5E6F-7A8B (the second column). The disk is not
        // here: the path is only compared as text.
        let mac = "/Volumes/OffloadTestNoSuchDisk/Z/2026-10-08/A001"
        let rec = CardRecord.load(card, serial: .some("5E6F-7A8B"))
        let column = CardRecord.columnOf(rec.files["clip/a&b.mov"]!.report, cardPath: mac, serial: "5E6F-7A8B")
        XCTAssertEqual(column, 1)
        XCTAssertTrue(CardRecord.readBack(rec.files["clip/a&b.mov"]!, cardPath: mac, serial: "5E6F-7A8B"))
        XCTAssertFalse(CardRecord.readBack(rec.files["clip/a&b.mov"]!, cardPath: mac, serial: "1A2B-3C4D"),
                       "the first disk's column says written")
        XCTAssertEqual(CardRecord.columnOf(r, cardPath: mac, serial: nil), -1,
                       "the report knows serials, this folder's is unknown: no guess by path")
        XCTAssertEqual(CardRecord.columnOf(r, cardPath: "/Volumes/OffloadTestNoSuchDisk/Other/A001", serial: "5E6F-7A8B"), -1)
    }

    func testReportMissingErrorAndProblems() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        XCTAssertTrue(CardRecord.load(card).reportMissing)
        try FileManager.default.createDirectory(atPath: card + "/_REPORT", withIntermediateDirectories: true)
        try Data("{".utf8).write(to: URL(fileURLWithPath: card + "/_REPORT/offload_report.json"))
        XCTAssertNotNil(CardRecord.load(card).error)
        try write(report(card: card, states: ["verified", "verified"]), card: card)
        try Data("{".utf8).write(to: URL(fileURLWithPath: card + "/_REPORT/offload_report_0002.json"))
        let rec = CardRecord.load(card)
        XCTAssertEqual(rec.problems.count, 1)
        XCTAssertFalse(rec.verified)
        XCTAssertEqual(rec.highestReportNumber, 2)
    }

    func testLeftoverNumberedReportCountsAsHighest() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try write(report(card: card, states: ["verified", "verified"]), card: card)
        try Data().write(to: URL(fileURLWithPath: card + "/_REPORT/A001_report_0003.html"))
        XCTAssertEqual(CardRecord.load(card).highestReportNumber, 3)
    }

    func testClipsOfAnUnanalysedGenerationNeverReplaceDescribedOnes() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        var g1 = report(card: card, states: ["verified", "verified"])
        var entry = ClipEntry(clip: ClipDetector.detect([SourceFile(relPath: "CLIP/A&B.mov", size: 1000, modified: Reports.t0)])[0])
        var info = ClipInfo()
        info.camera = "ARRI ALEXA 35"
        entry.info = info
        g1.clips = [entry]
        try write(g1, card: card)
        try write(report(card: card, gen: 2, states: ["verified", "verified"], paths: ["CLIP/A&B.mov", "CLIP/new.mov"]), card: card)
        let rec = CardRecord.load(card, serial: .some(nil))
        XCTAssertEqual(rec.clips.map(\.clip.path), ["CLIP/A&B.mov", "CLIP/new.mov"])
        XCTAssertEqual(rec.clips[0].info?.camera, "ARRI ALEXA 35", "a generation without analysis keeps the described clip")
        XCTAssertNil(rec.clips[1].info)
    }

    func testBackupStateAcrossFolders() throws {
        let root = tempDir()
        let d1 = root.sub("D", "2026-10-08", "A001"), d2 = root.sub("E", "2026-10-08", "A001")
        try write(report(card: d1, states: ["verified", "failed"]), card: d1)
        try write(report(card: d2, states: ["written", "verified"], verified: false), card: d2)
        XCTAssertEqual(BackupState.of([d1, d2]), BackupState(failed: 1, unverified: 1))
    }
}
