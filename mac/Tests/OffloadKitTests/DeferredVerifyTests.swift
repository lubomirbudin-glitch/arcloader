import XCTest
@testable import OffloadKit

/// Task 67: the verification deferred to stage 2 — pending reports without ASC MHL, the commit (MHL → reports → lists),
/// verifyIncomplete when it fails part way, the folders' lock until the commit (Review focus 10). Without proxies: the
/// test switches the deferral on (`deferVerifyOverride`), stage 2 reads every copy the classic way.
final class DeferredVerifyTests: XCTestCase {
    struct Setup {
        let card: String
        let roots: [String]
        let history: String
    }

    func setup(_ dir: TempDir) -> Setup {
        let card = dir.sub("A001")
        TestFS.writeRandom(card + "/CLIP/A001C001.bin", size: (3 << 20) + 17, seed: 1)
        TestFS.writeRandom(card + "/CLIP/A001C002.bin", size: 5000, seed: 2)
        TestFS.writeRandom(card + "/A001.xml", size: 300, seed: 3)
        return Setup(card: card, roots: [dir.sub("d1"), dir.sub("d2")], history: dir.sub("history"))
    }

    func job(_ s: Setup) -> OffloadJob {
        var cfg = JobConfig(source: s.card, destRoots: s.roots)
        cfg.date = "2026-10-08"
        cfg.analyzeClips = false
        cfg.proxy.enabled = false
        cfg.historyFolder = s.history
        var o = OffloadJob.engineOptions(cfg)
        o.chunkSize = 1 << 20
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let j = OffloadJob(config: cfg, options: o)
        j.deferVerifyOverride = true
        return j
    }

    func testStage1WritesPendingReportsWithoutMhl() throws {
        let s = setup(tempDir())
        let j = job(s)
        XCTAssertTrue(j.runCopy(), "stage 2 verifies")
        let st = j.status()
        XCTAssertTrue(st.verifyInProxy)
        XCTAssertTrue(st.safeToEject, "everything written and flushed: the card may be taken out")
        XCTAssertFalse(st.safeToFormat, "nothing verified yet")
        for p in try XCTUnwrap(j.plan).cardPaths {
            let r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            XCTAssertEqual(r.verifyPending, true)
            XCTAssertFalse(r.verified)
            XCTAssertTrue(r.files.allSatisfy { $0.destinations.allSatisfy { $0 == "written" } })
            XCTAssertFalse(FileManager.default.fileExists(atPath: p + "/ascmhl"), "no ASC MHL generation before the verification")
            XCTAssertFalse(FileManager.default.fileExists(atPath: p + "/_REPORT/A001.xxh64"), "no hash list before the verification")
            XCTAssertFalse(CardRecord.load(p).verified)
            XCTAssertEqual(ManifestFile.files(ManifestFile.read(p), generation: 1).count, 3, "the manifest knows every file")
        }
        XCTAssertEqual(HistoryStore(folder: s.history).load().first?.unfinished, true, "history: unfinished until the commit")
        j.runPost()
    }

    func testCommitWritesMhlThenReportsThenLists() throws {
        let s = setup(tempDir())
        let j = job(s)
        let report = try XCTUnwrap(j.run())
        let st = j.status()
        XCTAssertEqual(st.phase, .done, st.error ?? "")
        XCTAssertTrue(st.safeToFormat, st.warning ?? "")
        XCTAssertNil(st.warning)
        XCTAssertNil(report.verifyPending)
        XCTAssertTrue(report.verified)
        XCTAssertNotNil(report.safeToFormatSeconds)
        for (d, p) in try XCTUnwrap(j.plan).cardPaths.enumerated() {
            let r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            XCTAssertNil(r.verifyPending)
            XCTAssertNil(r.verifyIncomplete)
            XCTAssertTrue(r.verified)
            XCTAssertTrue(r.files.allSatisfy { $0.destinations.allSatisfy { $0 == "verified" } })
            XCTAssertNotNil(r.destinations[d].verifyMethod)
            XCTAssertEqual(MHLReader.readChain(ascmhlDir: p + "/ascmhl")?.map(\.seq), [1])
            XCTAssertTrue(FileManager.default.fileExists(atPath: p + "/_REPORT/A001.xxh64"))
            XCTAssertTrue(CardRecord.load(p).verified)
            let mhl = try XCTUnwrap(MHLReader.read(cardPath: p))
            XCTAssertEqual(mhl.lastGood.count, 3)
        }
        XCTAssertEqual(report.destinations.filter { $0.verifyMethod == OffloadReport.verifyInProxyMethod }.count, 1, "one primary")
        XCTAssertEqual(report.destinations.filter { $0.verifyMethod == OffloadReport.readBackMethod }.count, 1)
        XCTAssertEqual(report.destinations.first { $0.verifyMethod == OffloadReport.readBackMethod }?.verifyNote, VIPMsg.classicSecondary)
        let e = try XCTUnwrap(HistoryStore(folder: s.history).load().first)
        XCTAssertFalse(e.unfinished)
        XCTAssertTrue(e.verified)
    }

    // Review focus 2/10: a copy damaged after the copy (outside Offload) is found by stage 2 and never verified.
    func testDamageAfterCopyFoundInStage2() throws {
        let s = setup(tempDir())
        let j = job(s)
        XCTAssertTrue(j.runCopy())
        let paths = try XCTUnwrap(j.plan).cardPaths
        let victim = paths[1] + "/CLIP/A001C001.bin"
        let h = try XCTUnwrap(FileHandle(forUpdatingAtPath: victim))
        try h.seek(toOffset: (2 << 20) + 5)
        let b = try XCTUnwrap(h.readData(ofLength: 1).first)
        try h.seek(toOffset: (2 << 20) + 5)
        h.write(Data([b ^ 0x40]))
        try h.close()
        j.runPost()
        let st = j.status()
        XCTAssertFalse(st.safeToFormat)
        let r = try OffloadReport.read(paths[1] + "/_REPORT/offload_report.json")
        let f = try XCTUnwrap(r.files.first { $0.path == "CLIP/A001C001.bin" })
        XCTAssertEqual(f.destinations, ["verified", "failed"])
        XCTAssertTrue(f.errors[1]?.hasPrefix("HASH MISMATCH: block 2 dest ") ?? false, f.errors[1] ?? "nil")
        XCTAssertEqual(CardRecord.load(paths[1]).failedFiles, ["CLIP/A001C001.bin"], "read back and failed")
        XCTAssertEqual(BackupState.of(paths).failed, 1)
        XCTAssertFalse(j.report?.safeToFormat ?? true)
    }

    // Review focus 10.
    func testCommitFailureLeavesIncompleteEverywhereWritten() throws {
        let s = setup(tempDir())
        let j = job(s)
        j.beforeCommit = { path in
            // The second destination's disk is unplugged right before its result is written.
            if path.hasPrefix(s.roots[1]) { try? FileManager.default.removeItem(atPath: path) }
        }
        j.run()
        let paths = try XCTUnwrap(j.plan).cardPaths
        let st = j.status()
        XCTAssertFalse(st.safeToFormat)
        XCTAssertTrue(st.warning?.hasPrefix("Could not write the verification result (") ?? false, st.warning ?? "nil")
        XCTAssertFalse(FileManager.default.fileExists(atPath: paths[1]), "a folder gone is never made anew")
        let r = try OffloadReport.read(paths[0] + "/_REPORT/offload_report.json")
        XCTAssertEqual(r.verifyIncomplete, true, "on its own the first folder would look verified")
        XCTAssertFalse(r.safeToFormat)
        XCTAssertFalse(CardRecord.load(paths[0]).verified)
        let v = try BackupVerifier.verify(paths[0], progress: nil, isCancelled: { false })
        XCTAssertTrue(v.unfinished)
        XCTAssertEqual(j.report?.verifyIncomplete, true, "the job's own report (PDF, app) is not verified either")
        XCTAssertEqual(HistoryStore(folder: s.history).load().first?.unfinished, true)
    }

    // Review focus 10: the app ends (here: a cancel) before the commit — the folders never look verified.
    func testCrashBeforeCommitLeavesPending() throws {
        let s = setup(tempDir())
        let j = job(s)
        j.onVerifySession = { _ in j.cancel() }
        j.run()
        XCTAssertEqual(j.status().phase, .cancelled)
        XCTAssertFalse(j.status().safeToFormat)
        for p in try XCTUnwrap(j.plan).cardPaths {
            let r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            XCTAssertEqual(r.verifyPending, true)
            XCTAssertFalse(FileManager.default.fileExists(atPath: p + "/ascmhl"))
            XCTAssertFalse(CardRecord.load(p).verified)
        }
        XCTAssertEqual(HistoryStore(folder: s.history).load().first?.unfinished, true, "Finish interrupted backup completes it")
        XCTAssertNoThrow(try FolderLocks.acquire(try XCTUnwrap(j.plan).cardPaths).release(), "the lock is let go")
    }

    func testFolderLockHeldUntilCommit() throws {
        let s = setup(tempDir())
        let j = job(s)
        XCTAssertTrue(j.runCopy())
        let paths = try XCTUnwrap(j.plan).cardPaths
        XCTAssertThrowsError(try FolderLocks.acquire([paths[0]])) { e in
            XCTAssertEqual((e as? OffloadError)?.code, .appendBusy)
        }
        j.runPost()
        XCTAssertNoThrow(try FolderLocks.acquire(paths).release())
    }

    func testPrimaryIsFastestUnlessChosen() {
        let r = OffloadReport(cardName: "A", date: "2026-10-08", source: "/s", sourceFilesystem: "apfs", started: Date(),
                              copyFinished: Date(), finished: Date(), verified: false, totalFiles: 0, totalBytes: 0,
                              destinations: [], files: [], skipped: [])
        func session(_ rates: [Double], _ preferred: Int?, _ why: [String?]) -> VerifySession {
            VerifySession(report: r, cardPaths: ["/a", "/b", "/c"], blocks: [], blockSize: 4, rates: rates, preferred: preferred,
                          classicWhy: why, gate: PauseGate(), cardBlock: nil)
        }
        XCTAssertEqual(session([100, 300, 200], nil, [nil, nil, nil]).primary, 1)
        XCTAssertEqual(session([100, 300, 200], 2, [nil, nil, nil]).primary, 2)
        XCTAssertEqual(session([100, 300, 200], nil, [nil, VIPMsg.classicNetwork, nil]).primary, 2, "only a destination that passed the check")
        XCTAssertEqual(session([100, 300, 200], 1, [nil, VIPMsg.classicNetwork, nil]).primary, 2, "a chosen one that cannot is not used")
        XCTAssertEqual(session([100, 100, 100], nil, [nil, nil, nil]).primary, 0, "a tie: the first")
        let none = session([1, 2, 3], nil, [VIPMsg.classicBlind, VIPMsg.classicBlind, VIPMsg.classicBlind])
        XCTAssertEqual(none.primary, -1)
        XCTAssertEqual(none.classicWhy, [VIPMsg.classicBlind, VIPMsg.classicBlind, VIPMsg.classicBlind])
        let s = session([100, 300, 200], nil, [nil, nil, VIPMsg.classicNetwork])
        XCTAssertEqual(s.classicWhy, [VIPMsg.classicSecondary, nil, VIPMsg.classicNetwork])
    }

    func testWithVerificationShape() throws {
        let s = setup(tempDir())
        let j = job(s)
        XCTAssertTrue(j.runCopy())
        let pending = try XCTUnwrap(j.report)
        let n = pending.files.count
        let ok = Array(repeating: DestFileResult(state: .verified, error: nil), count: n)
        var bad = ok
        bad[0] = DestFileResult(state: .failed, error: "HASH MISMATCH: block 0 dest 0000000000000001 != source 0000000000000002")
        let v = pending.withVerification(results: [ok, bad], verified: true, primary: 0, classicWhy: [nil, VIPMsg.classicSecondary],
                                         notes: ["n"], finished: pending.copyFinished.addingTimeInterval(5), verifyPaused: 0)
        XCTAssertNil(v.verifyPending)
        XCTAssertTrue(v.verified)
        XCTAssertEqual(v.destinations.map(\.verifyMethod), ["verify-in-proxy", "read-back"])
        XCTAssertEqual(v.destinations.map(\.verifyNote), [nil, VIPMsg.classicSecondary])
        XCTAssertEqual(v.destinations[1].filesFailed, 1)
        XCTAssertNil(v.safeToFormatSeconds, "not every copy verified")
        XCTAssertEqual(v.verifyNotes, ["n"])
        let cancelled = pending.withVerification(results: [ok, ok], verified: false, primary: 0, classicWhy: [nil, nil], notes: [],
                                                 finished: Date(), verifyPaused: 0)
        XCTAssertEqual(cancelled.destinations.map(\.verifyMethod), [nil, nil])
        j.cancel()
        j.runPost()
    }
}
