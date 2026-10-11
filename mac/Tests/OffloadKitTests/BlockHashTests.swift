import XCTest
@testable import OffloadKit

/// Task 51, narrowed to what verify-in-proxy (tasks 65–68) needs: the XXH3-64 of every block of every file, computed in
/// the one read of the card next to xxh64 (`SourceHasher.blocks`, `OffloadEngine.blocks(i)`). The report and the ASC MHL
/// stay as before (xxh64 only).
final class BlockHashTests: XCTestCase {
    func copy(sizes: [Int]) throws -> (OffloadReport, OffloadEngine, [Data], String) {
        let dir = tempDir()
        let card = dir.sub("card"), dest = dir.sub("dest")
        let data = sizes.enumerated().map { i, n in TestFS.writeRandom(card + "/F\(i).bin", size: n, seed: UInt64(i + 4)) }
        var o = OffloadOptions(); o.allowSourceVolume = true; o.chunkSize = 1 << 20
        let e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: [dest], cardName: "A001", date: "2026-10-08")
        let r = try e.run(plan)
        return (r, e, data, plan.cardPaths[0])
    }

    func expected(_ d: Data) -> (xxh64: String?, blocks: [UInt64]) {
        let h = MultiHasher(kinds: .xxh64, blockSize: 1 << 20)
        d.withUnsafeBytes { if d.count > 0 { h.update($0.baseAddress!, d.count) } }
        let r = h.finish()
        return (r.hashes.xxh64Hex, r.blocks)
    }

    func testBlockHashesOfTheCardRead() throws {
        // A partial last block, an exact multiple of the block size, one smaller than a block, an empty file.
        let (r, e, data, _) = try copy(sizes: [(5 << 20) + 123, 2 << 20, 1000, 0])
        XCTAssertTrue(r.safeToFormat)
        for (i, d) in data.enumerated() {
            let want = expected(d)
            XCTAssertEqual(e.blocks(i), want.blocks, "file \(i)")
            XCTAssertEqual(r.files[i].xxh64, want.xxh64, "the xxh64 of the copy is the same one")
        }
        XCTAssertEqual(e.blocks(0).count, 6)
        XCTAssertEqual(e.blocks(1).count, 2)
        XCTAssertEqual(e.blocks(2).count, 1)
        XCTAssertEqual(e.blocks(3), [])
        XCTAssertEqual(e.blocks(1)[0], BlockHash.xxh3([UInt8](data[1].prefix(1 << 20)), 1 << 20))
        XCTAssertEqual(e.blocks(99), [], "no such file")
    }

    func testReportAndMhlUnchanged() throws {
        let (r, _, _, card) = try copy(sizes: [3000])
        XCTAssertNil(r.hashAlgorithms)
        XCTAssertNil(r.files[0].xxh128, "no per-file XXH3-128 (scope of task 51 narrowed)")
        XCTAssertNil(r.files[0].md5)
        let chain = try XCTUnwrap(MHLReader.readChain(ascmhlDir: card + "/ascmhl"))
        let mhl = try String(contentsOfFile: card + "/ascmhl/" + chain[0].path, encoding: .utf8)
        XCTAssertTrue(mhl.contains("<xxh64 "))
        XCTAssertFalse(mhl.contains("<xxh128 "))
    }

    func testFileCancelledMidwayHasNoBlocks() throws {
        let dir = tempDir()
        let card = dir.sub("card"), dest = dir.sub("dest")
        TestFS.writeRandom(card + "/A.bin", size: 3 << 20, seed: 1)
        TestFS.writeRandom(card + "/B.bin", size: 3 << 20, seed: 2)
        var o = OffloadOptions(); o.allowSourceVolume = true; o.chunkSize = 1 << 20
        var e: OffloadEngine!
        o.beforeFileCopy = { i in if i == 1 { e.cancel() } }
        e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: [dest], cardName: "A001", date: "2026-10-08")
        _ = try e.run(plan)
        XCTAssertEqual(e.blocks(0).count, 3)
        XCTAssertEqual(e.blocks(1), [], "not read: no block hashes")
    }

    // Cancel during the read-back stops inside a big file, not after it; the file is not verified.
    func testCancelDuringReadBackStopsInsideTheFile() throws {
        let dir = tempDir()
        let card = dir.sub("card"), dest = dir.sub("dest")
        TestFS.writeRandom(card + "/A.bin", size: 16 << 20, seed: 3)
        var o = OffloadOptions(); o.allowSourceVolume = true; o.chunkSize = 1 << 20
        var e: OffloadEngine!
        o.afterVerifyChunk = { e.cancel() }
        e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: [dest], cardName: "A001", date: "2026-10-08")
        let r = try e.run(plan)
        let read = e.snapshot().verifiedBytes[0]
        XCTAssertGreaterThan(read, 0)
        XCTAssertLessThanOrEqual(read, 2 << 20, "stopped within a block or two of 16")
        XCTAssertNotEqual(r.files[0].destinations[0], "verified", "a cancelled read-back never verifies the file")
    }

    func testMultiHasherWithoutXxh128() {
        let d = Data("abcdefgh".utf8)
        let h = MultiHasher(kinds: .xxh64, blockSize: 4, xxh128: false)
        d.withUnsafeBytes { h.update($0.baseAddress!, d.count) }
        let r = h.finish()
        XCTAssertNil(r.hashes.xxh128)
        XCTAssertEqual(r.blocks.count, 2)
        let full = MultiHasher(kinds: .xxh64, blockSize: 4)
        d.withUnsafeBytes { full.update($0.baseAddress!, d.count) }
        let f = full.finish()
        XCTAssertEqual(r.hashes.xxh64, f.hashes.xxh64)
        XCTAssertEqual(r.blocks, f.blocks)
    }
}
