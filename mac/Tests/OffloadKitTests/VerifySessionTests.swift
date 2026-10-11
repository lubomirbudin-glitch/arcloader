import XCTest
@testable import OffloadKit

/// Task 68: the verification session — the tap (mode A), the pre-read before RAW SDKs (mode B), the background readers,
/// the fill-in, the notes; Review focus 9 (never "verified" from the tap for a file in memory).
final class VerifySessionTests: XCTestCase {
    static let block = 1 << 20

    struct Copy {
        let card: String
        let report: OffloadReport
        let engine: OffloadEngine
        let plan: OffloadPlan
        func index(_ rel: String) -> Int { report.files.firstIndex { $0.path == rel }! }
        func clip(_ rel: String) -> Clip { ClipDetector.detect(plan.scan.files).first { $0.segments.contains(rel) }! }
    }

    /// A card copied to two destinations with verifyPending (the read-back deferred).
    func pendingCopy(_ dir: TempDir, _ files: [(String, Int)]) throws -> Copy {
        let card = dir.sub("A001")
        for (k, (rel, size)) in files.enumerated() { TestFS.writeRandom(card + "/" + rel, size: size, seed: UInt64(k + 11)) }
        var o = OffloadOptions()
        o.chunkSize = VerifySessionTests.block
        o.allowSourceVolume = true
        o.allowSameDevice = true
        o.deferVerify = true
        let e = OffloadEngine(options: o)
        let plan = try e.prepare(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")], cardName: "A001", date: "2026-10-08")
        let r = try e.run(plan)
        XCTAssertEqual(r.verifyPending, true)
        return Copy(card: card, report: r, engine: e, plan: plan)
    }

    func session(_ c: Copy, why: [String?] = [nil, nil], gate: PauseGate = PauseGate()) -> VerifySession {
        VerifySession(report: c.report, cardPaths: c.plan.cardPaths, blocks: c.engine.fileBlocks, blockSize: VerifySessionTests.block,
                      rates: [2, 1], preferred: 0, classicWhy: why, gate: gate, cardBlock: { i, b in
                          VerifySession.readCardBlock(c.card + "/" + c.report.files[i].path, block: b, blockSize: VerifySessionTests.block)
                      })
    }

    func flip(_ path: String, at offset: UInt64) throws {
        let h = try XCTUnwrap(FileHandle(forUpdatingAtPath: path))
        try h.seek(toOffset: offset)
        let b = try XCTUnwrap(h.readData(ofLength: 1).first)
        try h.seek(toOffset: offset)
        h.write(Data([b ^ 0x10]))
        try h.close()
        CacheProbe.evict(path)
    }

    // The proxy read only the head of the file through the tap; a flipped bit in the tail is found by the fill-in.
    func testFlippedBitInUnreadTailFoundByFillIn() throws {
        let c = try pendingCopy(tempDir(), [("CLIP/A001C001.mov", (3 << 20) + 4000), ("CLIP/A001C001.xml", 500)])
        let i = c.index("CLIP/A001C001.mov")
        try flip(c.plan.cardPaths[0] + "/CLIP/A001C001.mov", at: (3 << 20) + 10)
        let s = session(c)
        s.startBackground()
        let lease = try XCTUnwrap(s.prepare(c.clip("CLIP/A001C001.mov"), dest: 0))
        let url = try XCTUnwrap(lease.url, "mode A: a URL for ffmpeg")
        let r = try RawHTTP.get(url, range: "bytes=0-1048575")
        XCTAssertEqual(r.status, 206)
        let card = try Data(contentsOf: URL(fileURLWithPath: c.card + "/CLIP/A001C001.mov"))
        XCTAssertEqual(r.body, card.prefix(1 << 20), "the tap serves the destination's bytes")
        XCTAssertGreaterThan(lease.activity?() ?? 0, 0)
        lease.end()
        let byTap = s.verifiersSnapshot(0, i)?.verifiedBytes ?? 0
        XCTAssertTrue(byTap == 1 << 20 || byTap == 2 << 20, "block 0 verified by the tap's read (block 1 may be read ahead): \(byTap)")
        let fin = s.finish(isCancelled: { false })
        XCTAssertTrue(fin.verified)
        XCTAssertEqual(fin.results[0][i].state, .failed)
        XCTAssertTrue(fin.results[0][i].error?.hasPrefix("HASH MISMATCH: block 3 dest ") ?? false, fin.results[0][i].error ?? "nil")
        XCTAssertEqual(fin.results[1][i].state, .verified)
        XCTAssertEqual(fin.results[0][c.index("CLIP/A001C001.xml")].state, .verified, "the primary's other files: the background")
        XCTAssertEqual(s.verifiersSnapshot(0, i)?.fedBytes, Int64((3 << 20) + 4000 + 4000),
                       "nothing read twice but the mismatching last block")
        XCTAssertEqual(s.tapsServed, 1)
        s.close()
    }

    // Review focus 9.
    func testResidentFileIsNeverVerifiedFromTheTap() throws {
        let c = try pendingCopy(tempDir(), [("A001C002.mov", 2 << 20)])
        let path = c.plan.cardPaths[0] + "/A001C002.mov"
        CacheProbe.evictHook = { _ in true }   // the pages cannot be evicted
        defer { CacheProbe.evictHook = nil }
        _ = try Data(contentsOf: URL(fileURLWithPath: path))   // read normally: now in memory
        XCTAssertEqual(CacheProbe.isResident(path), true)
        let s = session(c)
        s.startBackground()
        XCTAssertNil(s.prepare(c.clip("A001C002.mov"), dest: 0), "no tap for a file in memory: ffmpeg reads it itself")
        let fin = s.finish(isCancelled: { false })
        XCTAssertEqual(fin.results[0][0].state, .verified, "the fill-in read it")
        XCTAssertEqual(fin.throughCache[0], 1)
        XCTAssertTrue(fin.notes.contains { $0.hasPrefix("Verified by reading through the macOS cache: ") }, "\(fin.notes)")
        XCTAssertEqual(s.tapsServed, 0)
        s.close()
    }

    func testSecondaryReadInBackground() throws {
        let c = try pendingCopy(tempDir(), [("A.bin", (2 << 20) + 1), ("B.bin", 300)])
        let s = session(c)
        s.startBackground()
        let deadline = Date().addingTimeInterval(20)
        while s.verifiedBytes[1] < s.toVerify[1] && Date() < deadline { usleep(20_000) }
        XCTAssertEqual(s.verifiedBytes[1], s.toVerify[1], "the secondary is read before the proxies end")
        let fin = s.finish(isCancelled: { false })
        XCTAssertEqual(s.primary, 0)
        XCTAssertEqual(s.classicWhy, [nil, VIPMsg.classicSecondary])
        XCTAssertTrue(fin.results.allSatisfy { $0.allSatisfy { $0.state == .verified } })
        XCTAssertEqual(fin.notes, [])
        s.close()
    }

    func testCancelLeavesWritten() throws {
        let c = try pendingCopy(tempDir(), [("A001C003.mov", 2 << 20)])
        let s = session(c)
        let fin = s.finish(isCancelled: { true })
        XCTAssertFalse(fin.verified)
        XCTAssertTrue(fin.results.allSatisfy { $0.allSatisfy { $0.state == .written } }, "nothing judged without the whole read")
        s.close()
    }

    // Mode B: a RAW clip is read once straight from the disk before the SDK; the SDK then reads it itself.
    func testPreReadForRawSdk() throws {
        let c = try pendingCopy(tempDir(), [("A001_08291430_C001.braw", (2 << 20) + 77)])
        let s = session(c)
        s.startBackground()
        let clip = c.clip("A001_08291430_C001.braw")
        XCTAssertEqual(TapRegistry.mode(for: clip), .preRead)
        let lease = try XCTUnwrap(s.prepare(clip, dest: 0))
        XCTAssertNil(lease.url)
        XCTAssertEqual(s.verifiersSnapshot(0, 0)?.finish(), .verified, "verified before the SDK reads it")
        XCTAssertEqual(s.verifiersSnapshot(0, 0)?.fedBytes, Int64((2 << 20) + 77), "read once")
        lease.end()
        let fin = s.finish(isCancelled: { false })
        XCTAssertEqual(fin.results[0][0].state, .verified)
        XCTAssertEqual(fin.results[1][0].state, .verified)
        s.close()
    }

    func testPreReadOfHungDiskGivesUp() throws {
        let c = try pendingCopy(tempDir(), [("A001_08291430_C002.braw", 3 << 20)])
        let path = c.plan.cardPaths[0] + "/A001_08291430_C002.braw"
        let s = session(c)
        s.stallTimeout = 0.3
        var slept = false
        BlockReads.setHook(path) { if !slept { slept = true; usleep(1_500_000) } }
        defer { BlockReads.setHook(path, nil) }
        let t0 = Date()
        _ = s.prepare(c.clip("A001_08291430_C002.braw"), dest: 0)
        XCTAssertLessThan(Date().timeIntervalSince(t0), 1.2, "a hung pre-read never holds the job")
        let fin = s.finish(isCancelled: { false })
        XCTAssertEqual(fin.results[0][0].state, .verified, "the fill-in decides")
        s.close()
    }

    // Spec 10: the same block wrong on both destinations — the card is read again.
    func testSameBlockWrongEverywhereAsksTheCard() throws {
        let c = try pendingCopy(tempDir(), [("DATA/x.bin", (2 << 20) + 9)])
        for p in c.plan.cardPaths { try flip(p + "/DATA/x.bin", at: (1 << 20) + 3) }
        let s = session(c)
        let fin = s.finish(isCancelled: { false })
        XCTAssertEqual(fin.results.map { $0[0].state }, [.failed, .failed])
        XCTAssertEqual(fin.notes.count, 1)
        XCTAssertTrue(fin.notes[0].hasPrefix("DATA/x.bin: block 1 mismatches the same way on "), fin.notes[0])
        XCTAssertTrue(fin.notes[0].hasSuffix(VIPMsg.cardGivesRecorded), fin.notes[0])
        s.close()
    }

    func testPauseWaitsBetweenFilesAndStands() throws {
        let c = try pendingCopy(tempDir(), [("A.bin", 1 << 20), ("B.bin", 1 << 20), ("C.bin", 1 << 20)])
        let gate = PauseGate()
        let s = session(c, why: [nil, VIPMsg.classicNetwork], gate: gate)
        gate.pause()
        s.startBackground()
        let deadline = Date().addingTimeInterval(10)
        while !gate.isHeld && Date() < deadline { usleep(10_000) }
        XCTAssertTrue(gate.isHeld, "the readers stand before their first file")
        XCTAssertEqual(s.verifiedBytes, [0, 0])
        gate.resume()
        let fin = s.finish(isCancelled: { false })
        XCTAssertTrue(fin.results.allSatisfy { $0.allSatisfy { $0.state == .verified } })
        s.close()
    }

    func testHungBackgroundDiskFailsItsUnreadCopies() throws {
        let c = try pendingCopy(tempDir(), [("A.bin", 2 << 20)])
        let path = c.plan.cardPaths[1] + "/A.bin"
        BlockReads.setHook(path) { usleep(2_000_000) }
        defer { BlockReads.setHook(path, nil) }
        let s = session(c, why: [nil, VIPMsg.classicNetwork])
        s.readStallTimeout = 0.4
        let fin = s.finish(isCancelled: { false })
        XCTAssertEqual(fin.results[1][0].state, .failed)
        XCTAssertEqual(fin.results[1][0].error, VIPMsg.verifyDiskHung)
        s.close()
    }
}
