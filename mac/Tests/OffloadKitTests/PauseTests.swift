import XCTest
@testable import OffloadKit

/// Task 61 with the user's requirement 2026-10-09 (Windows 0.8.0): Pause lets the step in progress finish — the file
/// being copied (on every destination), the file being read back, the clip being analysed or rendered — then the job
/// stands; Resume continues with the next file or clip.
final class PauseTests: XCTestCase {
    // MARK: Gate

    final class FakeClock {
        var now = Date(timeIntervalSince1970: 1_000)
        func advance(_ s: TimeInterval) { now = now.addingTimeInterval(s) }
    }

    /// Waits up to 10 s for `f`.
    func eventually(_ what: String, _ f: () -> Bool, file: StaticString = #filePath, line: UInt = #line) {
        let end = Date().addingTimeInterval(10)
        while !f() {
            if Date() > end { XCTFail("timed out: \(what)", file: file, line: line); return }
            usleep(10_000)
        }
    }

    func testGateWaitsUntilResumeAndCountsOnlyTheStandingTime() {
        let clock = FakeClock()
        let gate = PauseGate(clock: { clock.now })
        XCTAssertTrue(gate.waitWhilePaused(isCancelled: { false }), "open gate: no wait")
        XCTAssertEqual(gate.holds, 0)
        gate.pause()
        XCTAssertTrue(gate.isPaused)
        XCTAssertFalse(gate.isHeld, "nobody waits yet: still finishing")
        clock.advance(5)   // finishing the step is work, not pause
        let released = expectation(description: "released")
        var result: Bool?
        DispatchQueue.global().async {
            result = gate.waitWhilePaused(isCancelled: { false })
            released.fulfill()
        }
        eventually("held") { gate.isHeld }
        XCTAssertEqual(gate.holds, 1)
        clock.advance(7)
        XCTAssertEqual(gate.pausedTotal, 7, accuracy: 0.001)
        gate.resume()
        wait(for: [released], timeout: 5)
        XCTAssertEqual(result, true)
        XCTAssertFalse(gate.isPaused)
        clock.advance(100)
        XCTAssertEqual(gate.pausedTotal, 7, accuracy: 0.001, "the clock stops at Resume")
        XCTAssertEqual(gate.requests, 1)
    }

    func testCancelWhilePausedReturnsFalse() {
        let gate = PauseGate()
        gate.pause()
        let flag = NSLock()
        var cancelled = false
        let done = expectation(description: "done")
        var result: Bool?
        DispatchQueue.global().async {
            result = gate.waitWhilePaused(isCancelled: { flag.lock(); defer { flag.unlock() }; return cancelled })
            done.fulfill()
        }
        eventually("held") { gate.isHeld }
        flag.lock(); cancelled = true; flag.unlock()
        wait(for: [done], timeout: 5)
        XCTAssertEqual(result, false)
    }

    func testBusyWorkerKeepsTheJobFromStanding() {
        let clock = FakeClock()
        let gate = PauseGate(clock: { clock.now })
        let a = gate.work(), b = gate.work()
        gate.pause()
        let done = expectation(description: "a released")
        DispatchQueue.global().async {
            _ = a.waitWhilePaused(isCancelled: { false })
            done.fulfill()
        }
        eventually("a waits") { a.isHolding }
        XCTAssertFalse(gate.isHeld, "b still finishes its file")
        clock.advance(3)
        b.leave()   // b's work is over
        XCTAssertTrue(gate.isHeld)
        clock.advance(2)
        XCTAssertEqual(gate.pausedTotal, 2, accuracy: 0.001)
        gate.resume()
        wait(for: [done], timeout: 5)
        a.leave()
    }

    func testEndClosesThePauseAndLaterPressesChangeNothing() {
        let clock = FakeClock()
        let gate = PauseGate(clock: { clock.now })
        gate.pause()
        let done = expectation(description: "released by end")
        DispatchQueue.global().async {
            _ = gate.waitWhilePaused(isCancelled: { false })
            done.fulfill()
        }
        eventually("held") { gate.isHeld }
        clock.advance(4)
        gate.end()
        wait(for: [done], timeout: 5)
        clock.advance(50)
        XCTAssertEqual(gate.pausedTotal, 4, accuracy: 0.001)
        gate.pause()
        XCTAssertFalse(gate.isPaused, "a finished job is not paused again")
        XCTAssertEqual(gate.requests, 1)
    }

    // MARK: Engine and job

    struct Card {
        let card: String
        let dest: String
        let dir: TempDir
    }

    func card(files: Int = 3, size: Int = 300_000) -> Card {
        let dir = tempDir()
        let card = dir.sub("card")
        for i in 0..<files { TestFS.writeRandom(card + "/CLIP/F\(i).bin", size: size, seed: UInt64(i + 1)) }
        return Card(card: card, dest: dir.sub("dest"), dir: dir)
    }

    final class Box { weak var job: OffloadJob? }

    /// `pauseAt`: Pause is pressed on the copy thread after the first block of that file is read — in the middle of it.
    func job(_ c: Card, identity: ((String) -> String?)? = nil, analyze: Bool = false, pauseAt: Int? = nil,
             onChunk: ((OffloadJob, Int) -> Void)? = nil) -> OffloadJob {
        var cfg = JobConfig(source: c.card, destRoots: [c.dest])
        cfg.analyzeClips = analyze
        cfg.historyFolder = c.dir.sub("history")
        cfg.date = "2026-10-11"
        cfg.cardName = "A001"
        return make(cfg, identity: identity, pauseAt: pauseAt, onChunk: onChunk)
    }

    func make(_ cfg: JobConfig, identity: ((String) -> String?)? = nil, pauseAt: Int? = nil,
              onChunk: ((OffloadJob, Int) -> Void)? = nil) -> OffloadJob {
        let box = Box()
        var pressed = false
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        o.allowSameDevice = true
        o.chunkSize = 64 << 10
        o.cardIdentity = identity ?? { _ in "card-A" }
        o.afterChunkRead = { i in
            guard let j = box.job else { return }
            if i == pauseAt && !pressed { pressed = true; j.pause() }
            onChunk?(j, i)
        }
        let j = OffloadJob(config: cfg, options: o)
        box.job = j
        return j
    }

    func parts(_ dir: String) -> [String] { TestFS.files(in: dir).filter { $0.hasSuffix(".offload-part") } }

    /// Pause pressed in the middle of the second file: that file is read to its end and finished (renamed into place on
    /// the destination) before the copy stands; nothing is half written while it stands; Resume copies the rest.
    func testPauseFinishesTheFileInProgressAndResumeContinues() throws {
        let c = card()
        let j = job(c, pauseAt: 1)
        let done = expectation(description: "job")
        DispatchQueue.global().async { j.run(); done.fulfill() }
        eventually("standing") { j.status().pauseHeld }
        let s = j.status()
        XCTAssertTrue(s.paused)
        XCTAssertEqual(s.phase, .copying)
        XCTAssertEqual(JobProgress.pauseText(s), "Paused — Resume continues with the next file.")
        let folder = c.dest + "/2026-10-11/A001"
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: folder + "/CLIP/F1.bin")),
                       try Data(contentsOf: URL(fileURLWithPath: c.card + "/CLIP/F1.bin")), "the file in progress was finished")
        XCTAssertFalse(FileManager.default.fileExists(atPath: folder + "/CLIP/F2.bin"), "the next one waits")
        XCTAssertEqual(parts(folder), [], "no .offload-part while standing")
        usleep(300_000)
        j.resume()
        wait(for: [done], timeout: 30)
        let r = try XCTUnwrap(j.report)
        XCTAssertEqual(j.status().phase, .done, j.status().error ?? "")
        XCTAssertTrue(r.safeToFormat)
        XCTAssertEqual(r.files.map { $0.destinations }, [["verified"], ["verified"], ["verified"]])
        XCTAssertGreaterThanOrEqual(r.copyPausedSeconds ?? 0, 0.3)
        XCTAssertNil(r.verifyPausedSeconds)
        XCTAssertFalse(j.status().paused, "a finished job is not paused")
        XCTAssertTrue(r.summary.contains("Paused:"))
        XCTAssertTrue(HTMLReport.render(r).contains("paused · not in the speeds"))
    }

    /// While a file is still copying the card says what it finishes.
    func testPausingNamesTheStepInProgress() {
        var s = JobStatus()
        s.phase = .copying
        s.paused = true
        s.copy = ProgressSnapshot(phase: .copy, totalBytes: 1, totalFiles: 2, fileIndex: 0, currentFile: "CLIP/A001C003.mov",
                                  readBytes: 0, writtenBytes: [], verifiedBytes: [], errors: 0)
        s.pauseStep = OffloadJob.finishing(s, clipDone: false)
        XCTAssertEqual(JobProgress.pauseText(s), "Pausing (finishing file A001C003.mov…)")
        s.phase = .verifying
        s.copy?.verifyingFiles = ["CLIP/a.mov", "CLIP/b.wav", "CLIP/a.mov"]
        XCTAssertEqual(OffloadJob.finishing(s, clipDone: false), "files a.mov, b.wav")
        s.phase = .proxies
        s.clipName = "A003"
        XCTAssertEqual(OffloadJob.finishing(s, clipDone: false), "the proxy of clip A003")
        s.phase = .analyzing
        XCTAssertEqual(OffloadJob.finishing(s, clipDone: false), "clip A003")
        XCTAssertNil(OffloadJob.finishing(s, clipDone: true))
        s.pauseStep = nil
        XCTAssertEqual(JobProgress.pauseText(s), "Pausing…")
        s.pauseHeld = true
        XCTAssertEqual(JobProgress.pauseText(s), "Paused — Resume continues with the next clip.")
        let p = JobProgress(now: Date())
        p.update(s, now: Date())
        XCTAssertEqual(p.statusText, "Paused")
        XCTAssertEqual(p.readRate, 0)
        XCTAssertNil(p.eta)
        XCTAssertNil(JobProgress.pauseText(JobStatus()))
    }

    func testCancelWhilePausedEndsCancelledWithNoPartFiles() {
        let c = card()
        let j = job(c, pauseAt: 0)
        let done = expectation(description: "job")
        DispatchQueue.global().async { j.run(); done.fulfill() }
        eventually("standing") { j.status().pauseHeld }
        j.cancel()
        wait(for: [done], timeout: 30)
        XCTAssertEqual(j.status().phase, .cancelled)
        XCTAssertEqual(parts(c.dest), [])
        XCTAssertFalse(j.status().safeToFormat)
        XCTAssertEqual(j.report?.files[1].sourceError, "cancelled")
    }

    /// Pause during the read-back: the file being read is read to its end, then the read-back stands.
    func testPauseDuringVerifyStandsBetweenFiles() throws {
        let c = card(files: 2)
        // Pressed while the last file is copied: the copy has no next file, the read-back stands before its first.
        let j = job(c, pauseAt: 1)
        let done = expectation(description: "job")
        DispatchQueue.global().async { j.run(); done.fulfill() }
        eventually("standing") { j.status().pauseHeld }
        XCTAssertEqual(j.status().phase, .verifying)
        usleep(200_000)
        j.resume()
        wait(for: [done], timeout: 30)
        let r = try XCTUnwrap(j.report)
        XCTAssertTrue(r.safeToFormat)
        XCTAssertGreaterThanOrEqual(r.verifyPausedSeconds ?? 0, 0.2)
        XCTAssertNil(r.copyPausedSeconds)
    }

    // Review focus 8: a card swapped or pulled while paused is never copied from.
    func testCardSwappedWhilePausedStopsRemainingFiles() throws {
        let c = card()
        let lock = NSLock()
        var inReader = "card-A"
        let j = job(c, identity: { _ in lock.lock(); defer { lock.unlock() }; return inReader }, pauseAt: 0)
        let done = expectation(description: "job")
        DispatchQueue.global().async { j.run(); done.fulfill() }
        eventually("standing") { j.status().pauseHeld }
        lock.lock(); inReader = "card-B"; lock.unlock()   // same names, sizes and times — another card
        j.resume()
        wait(for: [done], timeout: 30)
        let r = try XCTUnwrap(j.report)
        XCTAssertEqual(r.files[0].destinations, ["verified"], "the file copied before the pause is good")
        XCTAssertNil(r.files[0].sourceError)
        XCTAssertEqual(r.files[1].sourceError, "card changed while paused")
        XCTAssertEqual(r.files[2].sourceError, "card changed while paused")
        XCTAssertFalse(FileManager.default.fileExists(atPath: c.dest + "/2026-10-11/A001/CLIP/F1.bin"), "nothing of the other card")
        XCTAssertFalse(r.safeToFormat)
        let s = j.status()
        XCTAssertEqual(s.phase, .failed)
        XCTAssertEqual(s.error, Msg.cardChangedWhilePaused)
        XCTAssertFalse(s.safeToEject)
        XCTAssertFalse(s.safeToFormat)
        XCTAssertTrue(HTMLReport.render(r).contains("the card was swapped or removed while paused"))
    }

    /// The card pulled while paused (nothing at the source) is the same as another card; asked again a few times first.
    func testCardPulledWhilePausedStopsRemainingFiles() throws {
        let c = card(files: 2)
        let lock = NSLock()
        var inReader: String? = "card-A"
        let j = job(c, identity: { _ in lock.lock(); defer { lock.unlock() }; return inReader }, pauseAt: 0)
        let done = expectation(description: "job")
        DispatchQueue.global().async { j.run(); done.fulfill() }
        eventually("standing") { j.status().pauseHeld }
        lock.lock(); inReader = nil; lock.unlock()
        j.resume()
        wait(for: [done], timeout: 30)
        XCTAssertEqual(j.report?.files[1].sourceError, OffloadEngine.cardChangedWhilePaused)
        XCTAssertEqual(j.status().phase, .failed)
    }

    /// Same card after the pause: nothing changes. A pause resumed before the copy stood still re-identifies the card.
    func testSameCardAfterPauseContinues() throws {
        let c = card()
        let asked = NSLock()
        var count = 0
        var pressed = false
        // Resumed while the file was still being copied: the copy never stood, the card is asked again anyway.
        let j = job(c, identity: { _ in asked.lock(); count += 1; asked.unlock(); return "card-A" }, onChunk: { j, i in
            if i == 0 && !pressed { pressed = true; j.pause(); j.resume() }
        })
        j.run()
        XCTAssertEqual(j.status().phase, .done)
        XCTAssertTrue(j.status().safeToFormat)
        asked.lock(); XCTAssertEqual(count, 2, "at the start and once after the pause"); asked.unlock()
    }

    // Review focus 8: a waiting worker holds no day or history lock.
    func testPausedJobHoldsNoDayLock() throws {
        let c = card(files: 3, size: 20_000)
        for i in 0..<3 { TestFS.writeRandom(c.card + "/CLIP/A001C00\(i + 1).mov", size: 20_000, seed: UInt64(10 + i)) }
        let j = job(c, analyze: true)
        XCTAssertTrue(j.runCopy(), "stage 2 follows")
        j.pause()
        let done = expectation(description: "stage 2")
        DispatchQueue.global().async { j.runPost(); done.fulfill() }
        eventually("standing between clips") { j.status().pauseHeld }
        let s = j.status()
        XCTAssertEqual(s.phase, .analyzing)
        XCTAssertEqual(JobProgress.pauseText(s), "Paused — Resume continues with the next clip.")
        let day = try XCTUnwrap(FileLock.tryLock(Locks.day(c.dest + "/2026-10-11")), "the day lock is free")
        day.release()
        let history = try XCTUnwrap(FileLock.tryLock(Locks.history(c.dir.path + "/history")), "the history lock is free")
        history.release()
        j.resume()
        wait(for: [done], timeout: 60)
        XCTAssertEqual(j.status().phase, .done)
        XCTAssertEqual(j.report?.clips.count, 3)
    }

    func testCancelWhilePausedInStageTwo() {
        let c = card(files: 1, size: 20_000)
        TestFS.writeRandom(c.card + "/CLIP/A001C001.mov", size: 20_000, seed: 9)
        let j = job(c, analyze: true)
        XCTAssertTrue(j.runCopy())
        j.pause()
        let done = expectation(description: "stage 2")
        DispatchQueue.global().async { j.runPost(); done.fulfill() }
        eventually("standing") { j.status().pauseHeld }
        j.cancel()
        wait(for: [done], timeout: 30)
        XCTAssertEqual(j.status().phase, .cancelled)
        XCTAssertTrue(j.status().safeToFormat, "the copy before it stays verified")
    }

    /// An append keeps its card folders locked while paused (no other append may number the same generation).
    func testPausedAppendKeepsTheFolderLock() throws {
        let s = try AppendPlannerTests().setup(tempDir())
        TestFS.writeRandom(s.card + "/CLIP/A001C003.mov", size: 3000, seed: 3)
        TestFS.writeRandom(s.card + "/CLIP/A001C004.mov", size: 3000, seed: 4)
        var cfg = JobConfig(source: s.card, destRoots: [])
        cfg.appendInto = s.cardPaths
        cfg.analyzeClips = false
        cfg.historyFolder = tempDir().path
        let j = make(cfg, pauseAt: 0)
        let done = expectation(description: "append")
        DispatchQueue.global().async { j.run(); done.fulfill() }
        eventually("standing") { j.status().pauseHeld }
        for p in s.cardPaths { XCTAssertNil(FileLock.tryLock(Locks.cardFolder(p)), "held while paused: \(p)") }
        j.resume()
        wait(for: [done], timeout: 30)
        XCTAssertEqual(j.status().phase, .done, j.status().error ?? "")
        XCTAssertTrue(j.status().safeToFormat)
        for p in s.cardPaths {
            let l = try XCTUnwrap(FileLock.tryLock(Locks.cardFolder(p)), "released after the job")
            l.release()
        }
    }

    func testWaitingJobBehindPausedCopy() {
        XCTAssertTrue(PausedQueue.blocked(.waiting, others: [(.copying, true)]))
        XCTAssertFalse(PausedQueue.blocked(.waiting, others: [(.proxies, true)]), "stage 2 does not hold the copy queue")
        XCTAssertFalse(PausedQueue.blocked(.waiting, others: [(.copying, false)]))
        XCTAssertFalse(PausedQueue.blocked(.copying, others: [(.copying, true)]))
    }
}
