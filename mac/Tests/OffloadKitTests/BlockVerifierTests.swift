import XCTest
@testable import OffloadKit

/// Task 66: the block verifier (any order, pieces, a read again, the length) and the fill-in reads (Review focus 11).
final class BlockVerifierTests: XCTestCase {
    let data = Data((0..<10).map { UInt8($0) })
    var manifest: BlockManifest {
        BlockManifest(size: 10, blockSize: 4, blocks: [0..<4, 4..<8, 8..<10].map { r in data.subdata(in: r).withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, r.count) } })
    }
    func feed(_ v: BlockVerifier, _ off: Int, _ d: Data) { d.withUnsafeBytes { v.feed(offset: Int64(off), $0.baseAddress!, d.count) } }

    func testWholeFileVerified() {
        let v = BlockVerifier(manifest)
        feed(v, 0, data); v.observeLength(10)
        XCTAssertEqual(v.finish(), .verified)
        XCTAssertEqual(v.verifiedBytes, 10)
    }

    // Review focus 11.
    func testPiecesAssembleByOffset() {
        let v = BlockVerifier(manifest)
        feed(v, 6, data.subdata(in: 6..<10)); feed(v, 4, data.subdata(in: 4..<6)); feed(v, 0, data.subdata(in: 0..<4))
        XCTAssertEqual(v.finish(), .verified)
    }

    func testOverlappingPiecesAndBytesPastTheEnd() {
        let v = BlockVerifier(manifest)
        feed(v, 1, data.subdata(in: 1..<3)); feed(v, 0, data.subdata(in: 0..<2)); feed(v, 2, data.subdata(in: 2..<10))
        feed(v, 10, Data([1, 2, 3]))   // past the card's size: ignored
        XCTAssertEqual(v.finish(), .verified)
    }

    func testReReadWithDifferentDataIsMismatch() {
        let v = BlockVerifier(manifest)
        feed(v, 0, data)
        var bad = data.subdata(in: 4..<8); bad[bad.startIndex] ^= 0xFF
        feed(v, 4, bad)
        XCTAssertEqual(v.finish(), .mismatch(block: 1))
        XCTAssertEqual(v.unstableBlocks, [1])
        XCTAssertNotNil(v.diskHash(1))
    }

    func testPieceOfVerifiedBlockIgnored() {
        let v = BlockVerifier(manifest)
        feed(v, 0, data)
        feed(v, 5, Data([0xEE]))   // a piece of a judged block decides nothing
        XCTAssertEqual(v.finish(), .verified)
        XCTAssertEqual(v.unstableBlocks, [])
    }

    func testMismatchThenGoodReadIsUnstableButVerified() {
        let v = BlockVerifier(manifest)
        var bad = data; bad[5] ^= 1
        feed(v, 0, bad); feed(v, 4, data.subdata(in: 4..<8))
        XCTAssertEqual(v.finish(), .verified)
        XCTAssertEqual(v.unstableBlocks, [1])
        XCTAssertNil(v.diskHash(1))
    }

    func testShorterFileIsMismatch() {
        let v = BlockVerifier(manifest)
        feed(v, 0, data.subdata(in: 0..<8)); v.observeLength(8)
        XCTAssertEqual(v.finish(), .sizeDiffers(onDisk: 8))
    }

    func testMissingRangesJoinNeighbours() {
        let v = BlockVerifier(manifest)
        feed(v, 4, data.subdata(in: 4..<8))
        XCTAssertEqual(v.missingRanges(), [ByteRange(offset: 0, length: 4), ByteRange(offset: 8, length: 2)])
        XCTAssertEqual(v.finish(), .incomplete)
    }

    func testOldestPartialDropped() {
        let size = 4 * 12
        let bytes = Data((0..<size).map { UInt8($0) })
        let m = BlockManifest(size: Int64(size), blockSize: 4,
                              blocks: (0..<12).map { b in bytes.subdata(in: b * 4..<b * 4 + 4).withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, 4) } })
        let v = BlockVerifier(m)
        for b in 0..<9 { feed(v, b * 4, bytes.subdata(in: b * 4..<b * 4 + 2)) }   // 9 half blocks: the first is dropped
        for b in (1..<9).reversed() { feed(v, b * 4 + 2, bytes.subdata(in: b * 4 + 2..<b * 4 + 4)) }
        XCTAssertEqual(v.verifiedBytes, 8 * 4, "block 0 lost its first half and stays missing")
        XCTAssertEqual(v.missingRanges().first, ByteRange(offset: 0, length: 4))
    }

    func testThreadSafeFeeds() {
        let size = 4096 * 64
        let bytes = Data((0..<size).map { UInt8(truncatingIfNeeded: $0 &* 31) })
        let m = BlockManifest(size: Int64(size), blockSize: 4096,
                              blocks: (0..<64).map { b in bytes.subdata(in: b * 4096..<(b + 1) * 4096).withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, 4096) } })
        let v = BlockVerifier(m)
        DispatchQueue.concurrentPerform(iterations: 64 * 4) { i in
            let b = i / 4, q = i % 4
            self.feed(v, b * 4096 + q * 1024, bytes.subdata(in: b * 4096 + q * 1024..<b * 4096 + (q + 1) * 1024))
        }
        // Pieces of more than 8 blocks at once may drop some; whatever is missing, the fill-in reads.
        XCTAssertNotEqual(v.finish(), .mismatch(block: 0))
        if case .mismatch = v.finish() { XCTFail("no block may mismatch") }
    }

    func testCompleteReadsOnlyWhatIsMissingAndFlipsAreFound() throws {
        let p = tempDir().sub("x") + "/f.bin"
        var bad = data; bad[9] ^= 1
        try bad.write(to: URL(fileURLWithPath: p))
        let v = BlockVerifier(manifest)
        feed(v, 0, data.subdata(in: 0..<8))
        let (o, _) = try BlockReads.complete(p, v, isCancelled: { false }, progress: nil)
        XCTAssertEqual(o, .mismatch(block: 2), "a flipped bit in a block the decoder never read is found by the fill-in")
        XCTAssertEqual(v.fedBytes, 8 + 2 + 2, "only block 2 read, then once more because it did not match")
        XCTAssertTrue(BlockReads.result(v, o).error?.hasPrefix("HASH MISMATCH: block 2 dest ") ?? false)
        XCTAssertEqual(BlockReads.result(v, o).state, .failed)
    }

    func testCompleteWholeFileVerifiedAndLengthChecked() throws {
        let d = tempDir().sub("x")
        try data.write(to: URL(fileURLWithPath: d + "/ok.bin"))
        let v = BlockVerifier(manifest)
        let (o, cached) = try BlockReads.complete(d + "/ok.bin", v, isCancelled: { false }, progress: nil)
        XCTAssertEqual(o, .verified)
        XCTAssertFalse(cached, "the normal write was evicted before the read")
        XCTAssertEqual(BlockReads.result(v, o).state, .verified)
        try data.subdata(in: 0..<9).write(to: URL(fileURLWithPath: d + "/short.bin"))
        let s = BlockVerifier(manifest)
        let (o2, _) = try BlockReads.complete(d + "/short.bin", s, isCancelled: { false }, progress: nil)
        XCTAssertEqual(o2, .sizeDiffers(onDisk: 9))
        XCTAssertEqual(BlockReads.result(s, o2).error, "size mismatch: 9 != 10")
    }

    func testCompleteMissingFileIsAnErrorNeverAVerdict() {
        let v = BlockVerifier(manifest)
        XCTAssertThrowsError(try BlockReads.complete(tempDir().path + "/gone.bin", v, isCancelled: { false }, progress: nil))
        XCTAssertEqual(v.finish(), .incomplete)
    }

    func testCompleteCancelledLeavesWritten() throws {
        let p = tempDir().sub("x") + "/f.bin"
        try data.write(to: URL(fileURLWithPath: p))
        let v = BlockVerifier(manifest)
        XCTAssertThrowsError(try BlockReads.complete(p, v, isCancelled: { true }, progress: nil)) { XCTAssertTrue($0 is CancellationError) }
        XCTAssertEqual(BlockReads.result(v, v.finish()).state, .written)
    }

    // Spec D.23.1 rule 3: a file that stays in memory is still read, but never silently — throughCache says so.
    func testResidentFileReadIsMarkedThroughCache() throws {
        let p = tempDir().sub("x") + "/f.bin"
        try data.write(to: URL(fileURLWithPath: p))
        CacheProbe.evictHook = { _ in true }   // eviction "fails": the pages stay
        defer { CacheProbe.evictHook = nil }
        _ = try Data(contentsOf: URL(fileURLWithPath: p))
        let v = BlockVerifier(manifest)
        let (o, cached) = try BlockReads.complete(p, v, isCancelled: { false }, progress: nil)
        XCTAssertEqual(o, .verified)
        XCTAssertTrue(cached)
    }
}
