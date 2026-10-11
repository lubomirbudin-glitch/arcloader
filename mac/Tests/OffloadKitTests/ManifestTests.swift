import XCTest
@testable import OffloadKit

/// FORMATY „Blokový manifest“ (task 65): only appended, a torn line skipped, checkpoints put together, the engine's records.
final class ManifestTests: XCTestCase {
    private let t = Date(timeIntervalSince1970: 1_791_100_800)   // 2026-10-04T08:00:00Z

    private func card() -> String {
        let c = tempDir().sub("D", "2026-10-08", "A001")
        try! FileManager.default.createDirectory(atPath: c + "/_REPORT", withIntermediateDirectories: true)
        return c
    }

    private func part(_ path: String, first: Int, _ blocks: [UInt64], size: Int64 = 100, modified: Date? = nil, gen: Int = 1) -> ManifestRecord {
        ManifestRecord(kind: ManifestRecord.partKind, gen: gen, path: path, size: size, modified: modified ?? t, blockSize: 4,
                       first: first, blocks: ManifestRecord.hex(blocks))
    }

    private func file(_ path: String, xxh64: String, _ blocks: [UInt64], gen: Int = 1) -> ManifestRecord {
        ManifestRecord(gen: gen, path: path, size: Int64(blocks.count * 4), modified: t, xxh64: xxh64, blockSize: 4,
                       blocks: ManifestRecord.hex(blocks))
    }

    func testTwoRecordsTwoLines() throws {
        let c = card()
        try ManifestFile.append(c, [file("A.mov", xxh64: "0000000000000001", [1])])
        try ManifestFile.append(c, [file("B.mov", xxh64: "0000000000000002", [2])])
        let text = try String(contentsOfFile: ManifestFile.path(in: c), encoding: .utf8)
        XCTAssertEqual(text.components(separatedBy: "\n").count, 3, "two lines, each ending with \\n")
        XCTAssertTrue(text.hasSuffix("\n"))
        XCTAssertEqual(ManifestFile.read(c).map(\.path), ["A.mov", "B.mov"])
    }

    func testTornLineStaysAloneAndIsSkipped() throws {
        let c = card()
        try ManifestFile.append(c, [file("A.mov", xxh64: "0000000000000001", [1])])
        let h = try XCTUnwrap(FileHandle(forWritingAtPath: ManifestFile.path(in: c)))
        h.seekToEndOfFile()
        h.write(Data(#"{"v":1,"ki"#.utf8))   // a crash in the middle of a line
        try h.close()
        try ManifestFile.append(c, [file("B.mov", xxh64: "0000000000000002", [2])])
        let lines = try String(contentsOfFile: ManifestFile.path(in: c), encoding: .utf8).components(separatedBy: "\n")
        XCTAssertEqual(lines[1], #"{"v":1,"ki"#, "the torn line is left as it is, on its own line")
        XCTAssertEqual(ManifestFile.read(c).map(\.path), ["A.mov", "B.mov"])
    }

    func testReadSkipsOtherVersionsAndEmptyPaths() throws {
        let c = card()
        var other = file("X.mov", xxh64: "0000000000000001", [1])
        other.v = 2
        var empty = file("", xxh64: "0000000000000001", [1])
        empty.path = ""
        try ManifestFile.append(c, [other, empty, file("A.mov", xxh64: "0000000000000001", [1])])
        XCTAssertEqual(ManifestFile.read(c).map(\.path), ["A.mov"])
        XCTAssertEqual(ManifestFile.read(tempDir().path), [], "no manifest")
    }

    func testPartsChainIgnoreGapsAndRestart() {
        let records = [
            part("a.mov", first: 0, [1, 2]),
            part("a.mov", first: 2, [3]),
            part("a.mov", first: 5, [9]),               // a gap: ignored
            part("b.mov", first: 0, [7]),
            part("b.mov", first: 1, [8], size: 200),    // another size, not first 0: ignored
            part("c.mov", first: 0, [1, 2]),
            part("c.mov", first: 0, [5]),               // a new attempt
            part("d.mov", first: 0, [1], gen: 2),       // another generation
        ]
        let p = ManifestFile.parts(records, generation: 1)
        XCTAssertEqual(p["a.mov"]?.blocks, [1, 2, 3])
        XCTAssertEqual(p["b.mov"]?.blocks, [7])
        XCTAssertEqual(p["c.mov"]?.blocks, [5])
        XCTAssertNil(p["d.mov"])
        XCTAssertEqual(ManifestFile.parts(records, generation: 2)["d.mov"]?.blocks, [1])
    }

    func testFilesNewestOfTheGenerationAnyCase() {
        let records = [file("Clip/A.MOV", xxh64: "0000000000000001", [1]), file("clip/a.mov", xxh64: "0000000000000002", [2]),
                       file("B.mov", xxh64: "0000000000000003", [3], gen: 2)]
        let f = ManifestFile.files(records, generation: 1)
        XCTAssertEqual(f.count, 1)
        XCTAssertEqual(f["clip/a.mov"]?.xxh64, "0000000000000002")
        XCTAssertEqual(ManifestFile.files(records, generation: 2).count, 1)
    }

    func testJsonShape() throws {
        let line = String(decoding: try ManifestFile.line(file("A/B.mov", xxh64: "00000000000000ff", [0xab])), as: UTF8.self)
        XCTAssertTrue(line.hasSuffix("}\n"))
        XCTAssertFalse(line.contains("\"first\""), "first only in a part with first > 0")
        XCTAssertFalse(line.contains("\"md5\""))
        XCTAssertFalse(line.contains("\"xxh128\""))
        XCTAssertTrue(line.contains(#""modified":"2026-10-04T08:00:00Z""#), line)
        XCTAssertTrue(line.contains(#""blocks":["00000000000000ab"]"#), line)
        XCTAssertTrue(line.contains(#""path":"A/B.mov""#), line)
        XCTAssertTrue(line.contains(#""kind":"file""#))
        XCTAssertTrue(line.contains(#""v":1"#))
        let p = String(decoding: try ManifestFile.line(part("A.mov", first: 3, [1])), as: UTF8.self)
        XCTAssertTrue(p.contains(#""first":3"#))
        XCTAssertTrue(p.contains(#""kind":"part""#))
        // A Windows line (fractional seconds never written, keys in any order) reads.
        let win = #"{"v":1,"kind":"file","gen":2,"path":"C/D.mxf","size":8,"modified":"2026-10-04T08:00:00Z","xxh64":"0000000000000001","xxh128":"0123456789abcdef0123456789abcdef","blockSize":4,"blocks":["0000000000000001","0000000000000002"]}"#
        let c = card()
        try (win + "\n").write(toFile: ManifestFile.path(in: c), atomically: true, encoding: .utf8)
        let r = try XCTUnwrap(ManifestFile.read(c).first)
        XCTAssertEqual(r.gen, 2)
        XCTAssertEqual(r.toManifest()?.blocks, [1, 2])
        XCTAssertEqual(r.xxh64Value, 1)
    }

    func testManifestOfRecordNeedsBlocksThatFit() {
        XCTAssertNil(file("A", xxh64: "0000000000000001", [1, 2]).toManifest().flatMap { m in m.count == 3 ? m : nil })
        var bad = file("A", xxh64: "0000000000000001", [1, 2])
        bad.size = 100
        XCTAssertNil(bad.toManifest(), "100 bytes in blocks of 4 are 25 blocks, not 2")
        XCTAssertNotNil(BlockManifest.make(size: 0, blockSize: 4, blocks: []))
        XCTAssertEqual(BlockManifest(size: 10, blockSize: 4, blocks: [1, 2, 3]).length(2), 2)
    }

    // The engine: a checkpoint after every 2 blocks, the file record with all its blocks; the report knows the volumes.
    func testEngineWritesCheckpointAndFileRecords() throws {
        let dir = tempDir()
        let src = dir.sub("card"), d1 = dir.sub("d1"), d2 = dir.sub("d2")
        TestFS.writeRandom(src + "/CLIP/A.mov", size: 3 << 20, seed: 1)
        TestFS.writeRandom(src + "/small.xml", size: 300, seed: 2)
        var o = OffloadOptions(); o.allowSourceVolume = true; o.allowSameDevice = true; o.chunkSize = 1 << 20
        o.checkpointBlocks = 2
        let e = OffloadEngine(options: o)
        let plan = try e.prepare(source: src, destRoots: [d1, d2], cardName: "A001", date: "2026-10-08")
        let r = try e.run(plan)
        XCTAssertTrue(r.safeToFormat)
        let ia = try XCTUnwrap(plan.scan.files.firstIndex { $0.relPath == "CLIP/A.mov" })
        let ix = try XCTUnwrap(plan.scan.files.firstIndex { $0.relPath == "small.xml" })
        for (d, path) in plan.cardPaths.enumerated() {
            let recs = ManifestFile.read(path)
            let parts = recs.filter { $0.kind == ManifestRecord.partKind }
            XCTAssertEqual(parts.count, 1)
            XCTAssertEqual(parts.first?.first, 0)
            XCTAssertEqual(parts.first?.blockValues(), Array(e.blocks(ia).prefix(2)))
            let files = ManifestFile.files(recs, generation: 1)
            XCTAssertEqual(files["clip/a.mov"]?.blockValues(), e.blocks(ia))
            XCTAssertEqual(files["clip/a.mov"]?.xxh64, r.files[ia].xxh64)
            XCTAssertEqual(files["clip/a.mov"]?.blockSize, 1 << 20)
            XCTAssertEqual(files["small.xml"]?.blockValues(), e.blocks(ix), "a small file is recorded after the flush of the drive")
            XCTAssertEqual(ManifestFile.parts(recs, generation: 1)["clip/a.mov"]?.blocks.count, 2)
            XCTAssertNotNil(r.destinations[d].volumeSerial)
            XCTAssertEqual(r.destinations[d].volumeSerial, VolumeFacts.volumeSerialText(path))
        }
        // The report JSON carries it (R12) and the one definition of "verified" still finds the column by the disk.
        let back = try OffloadReport.read(plan.cardPaths[0] + "/_REPORT/offload_report.json")
        XCTAssertNotNil(back.destinations[0].volumeSerial)
        XCTAssertTrue(CardRecord.load(plan.cardPaths[0]).verified)
        XCTAssertTrue(CardRecord.load(plan.cardPaths[1]).verified)
    }
}
