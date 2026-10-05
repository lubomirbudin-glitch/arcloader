import XCTest
@testable import OffloadKit

final class JobTests: XCTestCase {
    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 64 * 1024
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    // Review focus 1 (queue): cancelled while queued → "Zrušeno", not "Čeká" forever.
    func testCancelledBeforeStartReportsCancelled() {
        let dir = tempDir()
        let job = OffloadJob(config: JobConfig(source: dir.sub("card"), destRoots: [dir.sub("d1")]))
        XCTAssertEqual(job.status().phase, .waiting)
        job.cancel()
        XCTAssertEqual(job.status().phase, .cancelled)
    }

    func testCancelledThenRunCopyStaysCancelledCopiesNothing() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/A.bin", size: 1 << 20, seed: 1)
        let d1 = dir.sub("d1")
        var config = JobConfig(source: card, destRoots: [d1])
        config.analyzeClips = false
        let job = OffloadJob(config: config, options: options())
        job.cancel()
        XCTAssertFalse(job.runCopy())
        XCTAssertEqual(job.status().phase, .cancelled)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: d1), [])
        XCTAssertNil(job.plan)
    }

    // Review focus 1: a cancelled copy never goes back from a finished phase and is never safe to eject.
    func testStartedJobCancelledDuringRunNeverGoesBackFromFinished() {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/A.bin", size: 8 << 20, seed: 1)
        TestFS.writeRandom(card + "/B.bin", size: 1 << 20, seed: 2)
        let atFirstFile = DispatchSemaphore(value: 0)
        let cancelled = DispatchSemaphore(value: 0)
        var o = options()
        // Deterministic cancel point: the copy thread waits before its first file until the test has cancelled.
        o.beforeFileCopy = { i in
            guard i == 0 else { return }
            atFirstFile.signal()
            _ = cancelled.wait(timeout: .now() + 30)
        }
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.analyzeClips = false
        let job = OffloadJob(config: config, options: o)
        let done = DispatchSemaphore(value: 0)
        Thread.detachNewThread {
            job.runCopy()
            done.signal()
        }
        XCTAssertEqual(atFirstFile.wait(timeout: .now() + 30), .success, "the copy never reached its first file")
        XCTAssertEqual(job.status().phase, .copying)
        job.cancel()
        cancelled.signal()
        var finished = false
        let deadline = Date().addingTimeInterval(60)
        while done.wait(timeout: .now()) == .timedOut {
            guard Date() < deadline else { return XCTFail("the cancelled copy did not end within 60 s") }
            let phase = job.status().phase
            if finished { XCTAssertTrue(phase.isFinished, "phase went back to \(phase) after a finished phase") }
            finished = finished || phase.isFinished
        }
        XCTAssertEqual(job.status().phase, .cancelled)
        XCTAssertFalse(job.status().safeToEject)
        XCTAssertEqual(TestFS.files(in: dir.path + "/d1").filter { $0.hasSuffix(".offload-part") }, [])
    }

    func testRunSuccessEndsDoneAndSafeToEject() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        let a = TestFS.writeRandom(card + "/CLIP/A.bin", size: 300_000, seed: 3)
        TestFS.writeRandom(card + "/B.bin", size: 0, seed: 4)
        var config = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        config.analyzeClips = false
        config.date = "2026-10-05"
        let job = OffloadJob(config: config, options: options())
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertTrue(job.status().safeToEject)
        XCTAssertTrue(report.safeToEject)
        for path in try XCTUnwrap(job.plan).cardPaths {
            XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: path + "/CLIP/A.bin")), a)
            XCTAssertTrue(FileManager.default.fileExists(atPath: path + "/_REPORT/card.xxh64"))
        }
    }

    func testConfigMapsToEngineOptions() {
        var config = JobConfig(source: "/a", destRoots: ["/b"])
        config.chunkMB = 4
        config.buffers = 8
        config.verify = false
        config.allowSameDevice = true
        let o = OffloadJob.engineOptions(config)
        XCTAssertEqual(o.chunkSize, 4 << 20)
        XCTAssertEqual(o.bufferCount, 8)
        XCTAssertFalse(o.verify)
        XCTAssertTrue(o.allowSameDevice)
        XCTAssertFalse(o.allowSourceVolume)
    }
}
