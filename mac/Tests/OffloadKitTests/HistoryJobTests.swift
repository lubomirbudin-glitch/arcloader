import XCTest
@testable import OffloadKit

final class HistoryJobTests: XCTestCase {
    func job(_ dir: TempDir, history: String, cancelAtFile: Int? = nil) throws -> OffloadJob {
        let card = dir.sub("card"), dest = dir.sub("dest")
        _ = TestFS.writeRandom(card + "/CLIP/A001C001.mov", size: 2000, seed: 1)
        _ = TestFS.writeRandom(card + "/CLIP/A001C002.mov", size: 2000, seed: 2)
        var cfg = JobConfig(source: card, destRoots: [dest])
        cfg.analyzeClips = false
        cfg.historyFolder = history
        cfg.project = "Film X"
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        weak var ref: OffloadJob?
        if let n = cancelAtFile { o.beforeFileCopy = { i in if i == n { ref?.cancel() } } }
        let job = OffloadJob(config: cfg, options: o)
        ref = job
        return job
    }

    func testFinishedCopyRecorded() throws {
        let dir = tempDir()
        let j = try job(dir, history: dir.sub("hist"))
        j.run()
        XCTAssertEqual(j.status().phase, .done)
        let store = HistoryStore(folder: dir.sub("hist"))
        let entries = store.load()
        XCTAssertEqual(entries.count, 1, "the start entry became the copy's entry")
        let e = try XCTUnwrap(entries.first)
        XCTAssertFalse(e.unfinished)
        XCTAssertTrue(e.success)
        XCTAssertTrue(e.verified)
        XCTAssertNil(e.card, "a folder source")
        XCTAssertEqual(e.files, 2)
        XCTAssertEqual(e.bytes, 4000)
        XCTAssertEqual(e.reels, ["A001"])
        XCTAssertEqual(e.project, "Film X")
        XCTAssertEqual(e.generations.map(\.kind), ["full"])
        XCTAssertEqual(e.generations.first?.verified, true)
        XCTAssertEqual(e.destinations.map(\.cardPath), j.plan?.cardPaths)
        let scan = try Scanner.scan(dir.sub("card"))
        XCTAssertEqual(e.fingerprint, CardFingerprint.compute(scan.files).fingerprint)
        XCTAssertEqual(store.readFileList(e)?.map(\.relPath), ["CLIP/A001C001.mov", "CLIP/A001C002.mov"])
        // The card inspected again: identical.
        let insight = try CardInspector.inspect(dir.sub("card"), store: store, history: entries)
        XCTAssertEqual(insight.match?.kind, .identical)
    }

    func testUnverifiedCopyIsNotVerifiedInHistory() throws {
        let dir = tempDir()
        let card = dir.sub("card"), dest = dir.sub("dest")
        _ = TestFS.writeRandom(card + "/A001C001.mov", size: 100, seed: 1)
        var cfg = JobConfig(source: card, destRoots: [dest])
        cfg.analyzeClips = false
        cfg.verify = false
        cfg.historyFolder = dir.sub("hist")
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        OffloadJob(config: cfg, options: o).run()
        let e = try XCTUnwrap(HistoryStore(folder: dir.sub("hist")).load().first)
        XCTAssertTrue(e.success)
        XCTAssertFalse(e.verified, "never verified without the read-back")
        XCTAssertEqual(e.generations.first?.verified, false)
    }

    func testCancelledCopyNotRecorded() throws {
        let dir = tempDir()
        let j = try job(dir, history: dir.sub("hist"), cancelAtFile: 1)
        j.run()
        XCTAssertEqual(j.status().phase, .cancelled)
        XCTAssertTrue(HistoryStore(folder: dir.sub("hist")).load().isEmpty, "the start entry is removed")
    }

    func testNoHistoryWritesNothing() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        _ = TestFS.writeRandom(card + "/A001C001.mov", size: 100, seed: 1)
        var cfg = JobConfig(source: card, destRoots: [dir.sub("dest")])
        cfg.analyzeClips = false
        cfg.recordHistory = false
        cfg.historyFolder = dir.path + "/hist"
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        OffloadJob(config: cfg, options: o).run()
        XCTAssertFalse(FileManager.default.fileExists(atPath: dir.path + "/hist/history.json"))
    }

    func testBusyHistoryGivesAWarningNeverLosesTheEntry() throws {
        let dir = tempDir()
        let held = try XCTUnwrap(FileLock.tryLock(Locks.history(dir.sub("hist"))))
        let j = try job(dir, history: dir.sub("hist"))
        let (wait, retry) = (HistoryRecorder.waitLimit, HistoryRecorder.retryDelay)
        HistoryRecorder.waitLimit = 0.2
        HistoryRecorder.retryDelay = 0.1
        defer { HistoryRecorder.waitLimit = wait; HistoryRecorder.retryDelay = retry }
        j.run()
        XCTAssertEqual(j.status().warning,
                       "The card history is busy right now — the record of this backup will be written as soon as possible (do not close the app yet).")
        XCTAssertEqual(j.status().phase, .done)
        held.release()
        XCTAssertTrue(HistoryRecorder.drain(timeout: 5), "the background write gets through")
        let entries = HistoryStore(folder: dir.sub("hist")).load()
        XCTAssertEqual(entries.count, 1)
        XCTAssertEqual(entries.first?.unfinished, false, "the start and the end were written in order")
        XCTAssertNil(j.status().warning, "the warning goes once the record is written")
    }
}
