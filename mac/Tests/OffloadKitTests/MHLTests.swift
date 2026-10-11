import XCTest
@testable import OffloadKit

final class MHLTests: XCTestCase {
    private func card(_ dir: TempDir, files: [String: String]) throws -> String {
        let card = dir.sub("D", "2026-10-08", "A001")
        for (p, text) in files {
            try FileManager.default.createDirectory(atPath: ((card + "/" + p) as NSString).deletingLastPathComponent,
                                                    withIntermediateDirectories: true)
            try Data(text.utf8).write(to: URL(fileURLWithPath: card + "/" + p))
        }
        return card
    }

    private func hashes(_ s: String) -> FileHashes {
        let data = Data(s.utf8)
        let h = MultiHasher(kinds: .xxh64, blockSize: 1 << 16)
        data.withUnsafeBytes { if data.count > 0 { h.update($0.baseAddress!, data.count) } }
        return h.finish().hashes
    }

    private func report(_ card: String, gen: Int, files: [(String, String)], states: [String]? = nil,
                        verified: Bool = true) -> OffloadReport {
        var r = Reports.sample(verified: verified)
        r.generation = gen > 1 ? gen : nil
        r.destinations = [OffloadReport.Destination(root: "/r", cardPath: card, filesystem: "apfs", filesOK: files.count, filesFailed: 0)]
        r.files = files.enumerated().map { i, f in
            OffloadReport.FileEntry(path: f.0, size: Int64(f.1.utf8.count), modified: Reports.t0,
                                    xxh64: hashes(f.1).xxh64.map(hex64), xxh128: hashes(f.1).xxh128, sourceError: nil,
                                    destinations: [states?[i] ?? (verified ? "verified" : "written")], errors: [nil])
        }
        return r
    }

    func testGenerationOneThenTwoAndChain() throws {
        let card = try card(tempDir(), files: ["A.bin": "a", "B.bin": "b"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card)
        try MHL.write(report: report(card, gen: 2, files: [("B.bin", "b")]), cardPath: card)
        let h = try XCTUnwrap(MHLReader.read(cardPath: card))
        XCTAssertEqual(h.chain.map(\.seq), [1, 2])
        XCTAssertTrue(h.chainProblems.isEmpty, "\(h.chainProblems)")
        XCTAssertEqual(h.files["a.bin"]?.generation, 1)
        XCTAssertEqual(h.files["b.bin"]?.generation, 2)
        XCTAssertEqual(h.files["b.bin"]?.hashes.map(\.algorithm), ["xxh64", "xxh128"])
        XCTAssertTrue(h.ignoresReportFolder)
        let gen2 = try String(contentsOfFile: card + "/ascmhl/" + h.chain[1].path, encoding: .utf8)
        XCTAssertTrue(gen2.contains("<comment>ArcLoader append: 1 new file from /Volumes/A001</comment>"), gen2)
        XCTAssertTrue(gen2.contains(">ArcLoader</tool>"), "creatorinfo tool name as Windows 0.10.0 writes it")
        XCTAssertTrue(gen2.contains("<xxh128 action=\"original\""))
        XCTAssertTrue(gen2.contains("<pattern>_REPORT</pattern>"), "ignore patterns of generation 1")
        XCTContext.runActivity(named: "ascmhl verify \(card)") { _ in }
    }

    func testVerifyMethodInTheComment() throws {
        let card = try card(tempDir(), files: ["A.bin": "a"])
        var r = report(card, gen: 1, files: [("A.bin", "a")])
        r.destinations[0].verifyMethod = "read-back"
        try MHL.write(report: r, cardPath: card)
        let h = try XCTUnwrap(MHLReader.read(cardPath: card))
        let gen1 = try String(contentsOfFile: card + "/ascmhl/" + h.chain[0].path, encoding: .utf8)
        XCTAssertTrue(gen1.contains("<comment>ArcLoader offload of A001 from /Volumes/A001 · verification: read-back</comment>"), gen1)
        XCTAssertTrue(gen1.contains("<tool version=\"\(r.version)\">ArcLoader</tool>"), gen1)
        XCTAssertTrue(gen1.contains("<process>transfer</process>"))
    }

    func testGenerationOneNeverOverAChainAndGapRefused() throws {
        let card = try card(tempDir(), files: ["A.bin": "a"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card)
        let chain = try Data(contentsOf: URL(fileURLWithPath: card + "/ascmhl/ascmhl_chain.xml"))
        XCTAssertThrowsError(try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card)) { e in
            XCTAssertEqual("\(e)", "The ASC MHL in \(card) already has a chain — generation 1 was not written.")
        }
        XCTAssertThrowsError(try MHL.write(report: report(card, gen: 3, files: [("A.bin", "a")]), cardPath: card), "2 is missing") { e in
            XCTAssertEqual("\(e)", "The ASC MHL in \(card) changed during the append (last generation 1, the append writes 3) — the generation was not written.")
        }
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: card + "/ascmhl/ascmhl_chain.xml")), chain, "the chain is untouched")
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: card + "/ascmhl").count, 2)
    }

    func testWrittenInVerifiedGenerationIsLeftOutFailedIsFailed() throws {
        let card = try card(tempDir(), files: ["A.bin": "a", "B.bin": "b"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a"), ("B.bin", "b")], states: ["written", "failed"]), cardPath: card)
        let h = try XCTUnwrap(MHLReader.read(cardPath: card))
        XCTAssertNil(h.files["a.bin"], "written but the generation was verified: not in it")
        XCTAssertEqual(h.files["b.bin"]?.hashes.first?.action, "failed")
        XCTAssertNil(h.lastGood["b.bin"])
    }

    func testWrittenInUnverifiedGenerationIsOriginal() {
        let r = report("/c", gen: 1, files: [("A.bin", "a")], verified: false)
        XCTAssertEqual(MHL.entries(r, destination: 0).map(\.action), ["original"])
    }

    func testRepairActions() throws {
        var r = report("/c", gen: 3, files: [("A.bin", "a"), ("B.bin", "b"), ("C.bin", "c")])
        r.kind = "repair"
        r.destinations.append(OffloadReport.Destination(root: "/r2", cardPath: "/c2", filesystem: "apfs", filesOK: 3, filesFailed: 0))
        r.files[0].destinations = ["verified", "verified"]
        r.files[0].rewritten = [true, false]
        r.files[1].destinations = ["failed", "verified"]
        r.files[1].rewritten = [true, false]
        r.files[2].destinations = ["pending", "written"]
        r.files[2].rewritten = [false, false]
        XCTAssertEqual(MHL.entries(r, destination: 0).map { "\($0.file.path)=\($0.action)" }, ["A.bin=original", "B.bin=failed"])
        XCTAssertEqual(MHL.entries(r, destination: 1).map { "\($0.file.path)=\($0.action)" }, ["A.bin=verified", "B.bin=verified"],
                       "a kept copy not read back in this generation is not in it")
    }

    func testChainProblems() throws {
        let card = try card(tempDir(), files: ["A.bin": "a"])
        try MHL.write(report: report(card, gen: 1, files: [("A.bin", "a")]), cardPath: card)
        let chain = try XCTUnwrap(MHLReader.readChain(ascmhlDir: card + "/ascmhl"))
        let file = card + "/ascmhl/" + chain[0].path
        try (try String(contentsOfFile: file, encoding: .utf8)).replacingOccurrences(of: "A.bin", with: "X.bin")
            .write(toFile: file, atomically: true, encoding: .utf8)
        XCTAssertEqual(MHLReader.read(cardPath: card)?.chainProblems,
                       ["ASC MHL generation 0001 does not match the chain (the MHL file was changed)."])
        try Data().write(to: URL(fileURLWithPath: card + "/ascmhl/0009_x.mhl"))
        XCTAssertTrue(MHLReader.read(cardPath: card)!.chainProblems.contains("ASC MHL file not in the chain: 0009_x.mhl"))
    }

    func testHasGenerationIsIdempotentAndRefusesAnother() throws {
        let card = try card(tempDir(), files: ["A.bin": "a"])
        let r = report(card, gen: 1, files: [("A.bin", "a")])
        XCTAssertFalse(try MHL.hasGeneration(r, cardPath: card))
        try MHL.write(report: r, cardPath: card)
        XCTAssertTrue(try MHL.hasGeneration(r, cardPath: card))
        var other = r
        other.files[0].xxh64 = "0000000000000000"
        XCTAssertThrowsError(try MHL.hasGeneration(other, cardPath: card)) { e in
            XCTAssertEqual("\(e)", "The ASC MHL in \(card) already has a different generation 1 — the generation was not written again.")
        }
        let chain = try XCTUnwrap(MHLReader.readChain(ascmhlDir: card + "/ascmhl"))
        try Data("changed".utf8).write(to: URL(fileURLWithPath: card + "/ascmhl/" + chain[0].path))
        XCTAssertThrowsError(try MHL.hasGeneration(r, cardPath: card), "a damaged generation is never taken as written")
    }

    func testIgnorePatterns() {
        XCTAssertTrue(BackupVerifier.ignored("_REPORT/x", ["_REPORT"]))
        XCTAssertTrue(BackupVerifier.ignored("CLIP/.DS_Store", [".DS_Store"]))
        XCTAssertTrue(BackupVerifier.ignored("ascmhl/x.mhl", ["ascmhl/"]))
        XCTAssertFalse(BackupVerifier.ignored("ascmhl", ["ascmhl/"]), "name/ only matches a folder")
        XCTAssertTrue(BackupVerifier.ignored("a/b/c.tmp", ["**/*.tmp"]))
        XCTAssertFalse(BackupVerifier.ignored("a/b.mov", ["/b.mov"]), "a pattern with / starts at the card root")
        XCTAssertTrue(BackupVerifier.ignored("_report/x", ["_REPORT"]), "any case")
        XCTAssertFalse(BackupVerifier.ignored("CLIP/A.mov", ["_REPORT", "*.tmp"]))
    }

    // Review focus 6.
    func testGenerationFileShapeMatchesWindowsFixture() throws {
        let fixture = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("Fixtures/ascmhl-two-generations").path
        guard let h = MHLReader.read(cardPath: fixture), h.chain.count == 2 else { throw XCTSkip("Windows fixture not in the repo yet") }
        XCTAssertTrue(h.chainProblems.isEmpty, "\(h.chainProblems)")
        XCTAssertTrue(h.ignoresReportFolder)
        XCTAssertEqual(h.generations[1].files.first?.hashes.map(\.algorithm).sorted(), ["xxh128", "xxh64"])
        // The recorded hashes are those of the files (the Mac computes the same values as Windows).
        for (key, rec) in h.files {
            let data = try Data(contentsOf: URL(fileURLWithPath: fixture + "/" + rec.path))
            let mine = hashes(String(decoding: data, as: UTF8.self))
            XCTAssertEqual(rec.hashes.first { $0.algorithm == "xxh64" }?.value, mine.xxh64.map(hex64), key)
            XCTAssertEqual(rec.hashes.first { $0.algorithm == "xxh128" }?.value, mine.xxh128, key)
        }
        // A Mac append on top of the Windows backup goes on with generation 3 and keeps both earlier ones.
        let dir = tempDir()
        let copy = dir.path + "/A001"
        try FileManager.default.copyItem(atPath: fixture, toPath: copy)
        let before = try FileManager.default.contentsOfDirectory(atPath: copy + "/ascmhl").filter { $0.hasSuffix(".mhl") }.sorted()
        let bytes = try before.map { try Data(contentsOf: URL(fileURLWithPath: copy + "/ascmhl/" + $0)) }
        try Data("c".utf8).write(to: URL(fileURLWithPath: copy + "/C.bin"))
        try MHL.write(report: report(copy, gen: 3, files: [("C.bin", "c")]), cardPath: copy)
        let after = try XCTUnwrap(MHLReader.read(cardPath: copy))
        XCTAssertEqual(after.chain.map(\.seq), [1, 2, 3])
        XCTAssertEqual(Array(after.chain.prefix(2)), h.chain, "earlier chain entries exactly as read")
        XCTAssertTrue(after.chainProblems.isEmpty, "\(after.chainProblems)")
        XCTAssertEqual(try before.map { try Data(contentsOf: URL(fileURLWithPath: copy + "/ascmhl/" + $0)) }, bytes)
        XCTAssertEqual(after.generations[2].ignore, h.generations[0].ignore)
    }

    func testIOErrorMessage() {
        XCTAssertEqual("\(IOError(message: "Whole sentence."))", "Whole sentence.")
    }
}
