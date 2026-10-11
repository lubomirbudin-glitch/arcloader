import XCTest
@testable import OffloadKit

final class GenerationWriteTests: XCTestCase {
    private func gen(_ card: String, _ g: Int, files: [(String, String)], md5: Bool = false) -> OffloadReport {
        var r = Reports.sample()
        r.cardName = "A001"
        r.generation = g > 1 ? g : nil
        r.hashAlgorithms = md5 ? ["xxh64", "md5"] : nil
        r.destinations = [OffloadReport.Destination(root: "/r", cardPath: card, filesystem: "apfs", filesOK: files.count, filesFailed: 0)]
        r.files = files.map {
            OffloadReport.FileEntry(path: $0.0, size: 1, modified: Reports.t0, xxh64: $0.1,
                                    md5: md5 ? String(repeating: "a", count: 32) : nil,
                                    sourceError: nil, destinations: ["verified"], errors: [nil])
        }
        return r
    }

    private func read(_ path: String) throws -> Data { try Data(contentsOf: URL(fileURLWithPath: path)) }

    // Review focus 1.
    func testAppendCreatesNumberedReportsNeverReplaces() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        let earlier = try ["offload_report.json", "offload_report.txt", "A001_report.html"].map { try read(card + "/_REPORT/" + $0) }
        let mhl1 = try XCTUnwrap(MHLReader.read(cardPath: card)?.chain.first)
        let mhl1Bytes = try read(card + "/ascmhl/" + mhl1.path)
        let g2 = gen(card, 2, files: [("B.bin", "2222222222222222")])
        try g2.write(cardPath: card, includeHashRecords: true)
        for name in ["offload_report_0002.json", "offload_report_0002.txt", "A001_report_0002.html"] {
            XCTAssertTrue(FileManager.default.fileExists(atPath: card + "/_REPORT/" + name), name)
        }
        XCTAssertEqual(try ["offload_report.json", "offload_report.txt", "A001_report.html"].map { try read(card + "/_REPORT/" + $0) },
                       earlier, "generation 1 untouched")
        XCTAssertEqual(try read(card + "/ascmhl/" + mhl1.path), mhl1Bytes, "MHL generation 1 untouched")
        XCTAssertEqual(MHLReader.read(cardPath: card)?.chain.map(\.seq), [1, 2])

        let listBefore = try read(card + "/_REPORT/A001.xxh64")
        let chainBefore = try read(card + "/ascmhl/ascmhl_chain.xml")
        XCTAssertThrowsError(try g2.write(cardPath: card, includeHashRecords: true), "a second first write of generation 2 is refused")
        XCTAssertEqual(try read(card + "/_REPORT/A001.xxh64"), listBefore, "refused before any shared file is touched")
        XCTAssertEqual(try read(card + "/ascmhl/ascmhl_chain.xml"), chainBefore)
        try g2.write(cardPath: card, includeHashRecords: false)   // the job's own refresh of its generation
        XCTAssertEqual(try read(card + "/_REPORT/A001.xxh64"), listBefore)
    }

    func testHashListKeepsEarlierBytes() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        let list = card + "/_REPORT/A001.xxh64"
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8), "1111111111111111  A.bin\n")
        // A Windows tool may have written CRLF: the earlier bytes stay exactly as they are.
        try Data("1111111111111111  A.bin\r\n".utf8).write(to: URL(fileURLWithPath: list))
        try gen(card, 2, files: [("B.bin", "2222222222222222")]).write(cardPath: card, includeHashRecords: true)
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8), "1111111111111111  A.bin\r\n2222222222222222  B.bin\n")
    }

    func testMissingFinalNewlineIsAddedEarlierBytesKept() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        let list = card + "/_REPORT/A001.xxh64"
        try Data("1111111111111111  A.bin".utf8).write(to: URL(fileURLWithPath: list))
        try gen(card, 2, files: [("B.bin", "2222222222222222")]).write(cardPath: card, includeHashRecords: true)
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8), "1111111111111111  A.bin\n2222222222222222  B.bin\n")
    }

    func testMissedLinesFromMhlAndNoDuplicates() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        try gen(card, 2, files: [("B.bin", "2222222222222222")]).write(cardPath: card, includeHashRecords: true)
        let list = card + "/_REPORT/A001.xxh64"
        try Data("1111111111111111  A.bin\n".utf8).write(to: URL(fileURLWithPath: list))   // generation 2's lines were lost
        XCTAssertTrue(try OffloadReport.healHashList(cardPath: card, cardName: "A001"))
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8), "1111111111111111  A.bin\n2222222222222222  B.bin\n")
        XCTAssertFalse(try OffloadReport.healHashList(cardPath: card, cardName: "A001"), "nothing missed now")

        // A generation 3 after lost lines: the missed lines from the MHL first, then its own.
        try Data("1111111111111111  A.bin\n".utf8).write(to: URL(fileURLWithPath: list))
        try gen(card, 3, files: [("C.bin", "3333333333333333")]).write(cardPath: card, includeHashRecords: true)
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8),
                       "1111111111111111  A.bin\n2222222222222222  B.bin\n3333333333333333  C.bin\n")
    }

    func testRepairDoesNotRepeatLines() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111"), ("B.bin", "2222222222222222")]).write(cardPath: card, includeHashRecords: true)
        let list = card + "/_REPORT/A001.xxh64"
        let before = try read(list)
        var repair = gen(card, 2, files: [("B.bin", "2222222222222222")])
        repair.kind = "repair"
        repair.files[0].rewritten = [true]
        try repair.write(cardPath: card, includeHashRecords: true)
        XCTAssertEqual(try read(list), before, "the repaired file is listed already with its recorded hash")
    }

    func testMd5ListOnlyWithMd5() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        try gen(card, 1, files: [("A.bin", "1111111111111111")]).write(cardPath: card, includeHashRecords: true)
        XCTAssertFalse(FileManager.default.fileExists(atPath: card + "/_REPORT/A001.md5"))
        let card2 = tempDir().sub("E", "2026-10-08", "A001")
        try gen(card2, 1, files: [("A.bin", "1111111111111111")], md5: true).write(cardPath: card2, includeHashRecords: true)
        XCTAssertEqual(try String(contentsOfFile: card2 + "/_REPORT/A001.md5", encoding: .utf8), String(repeating: "a", count: 32) + "  A.bin\n")
    }

    func testCommitWritesMhlFirstAndRefusesPending() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        var r = gen(card, 1, files: [("A.bin", "1111111111111111")])
        r.verifyPending = true
        try r.write(cardPath: card, includeHashRecords: false)        // stage 1: reports only, no generation yet
        XCTAssertNil(MHLReader.read(cardPath: card))
        XCTAssertFalse(FileManager.default.fileExists(atPath: card + "/_REPORT/A001.xxh64"))
        XCTAssertThrowsError(try r.commit(cardPath: card))
        XCTAssertNil(MHLReader.read(cardPath: card), "a pending report is never committed")
        r.verifyPending = nil
        try r.commit(cardPath: card)
        try r.commit(cardPath: card)                                   // retried: idempotent
        XCTAssertEqual(MHLReader.read(cardPath: card)?.chain.count, 1)
        XCTAssertNil(try OffloadReport.read(card + "/_REPORT/offload_report.json").verifyPending)
        XCTAssertEqual(try String(contentsOfFile: card + "/_REPORT/A001.xxh64", encoding: .utf8), "1111111111111111  A.bin\n")
    }

    func testCommitKeepsAnExistingListOfGenerationOne() throws {
        let card = tempDir().sub("D", "2026-10-08", "A001")
        let r = gen(card, 1, files: [("A.bin", "1111111111111111")])
        let list = card + "/_REPORT/A001.xxh64"
        try FileManager.default.createDirectory(atPath: card + "/_REPORT", withIntermediateDirectories: true)
        try Data("1111111111111111  A.bin\r\n".utf8).write(to: URL(fileURLWithPath: list))
        try r.commit(cardPath: card)
        XCTAssertEqual(try String(contentsOfFile: list, encoding: .utf8), "1111111111111111  A.bin\r\n", "nothing missing: unchanged")
    }

    func testHashListOfAlgorithm() {
        let r = gen("/c", 1, files: [("A.bin", "1111111111111111")], md5: true)
        XCTAssertEqual(r.hashList, "1111111111111111  A.bin\n")
        XCTAssertEqual(r.hashList(of: "md5"), String(repeating: "a", count: 32) + "  A.bin\n")
    }
}
