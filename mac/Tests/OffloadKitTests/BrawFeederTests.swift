import XCTest
@testable import OffloadKit

final class BrawFeederTests: XCTestCase {
    private let lib = "/BM/BlackmagicRawAPI.framework"

    /// Stand-in ffmpeg that reads its input to the end and prints no progress.
    private func consumer(_ dir: TempDir) -> String { BrawTest.script(dir, "ffmpeg", "cat > /dev/null") }

    private func renderer(_ dir: TempDir, decoder: String, log: LogLines? = nil, infoTimeout: TimeInterval = 60) -> ProxyRenderer {
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: consumer(dir), brawDecoder: decoder))
        r.stallTimeout = 1
        r.backends[.braw] = BRAWBackend(findLibrary: { _ in "/BM/BlackmagicRawAPI.framework" }, infoTimeout: infoTimeout)
        if let log { r.log = { log.add($0) } }
        return r
    }

    // Review focus 11 (Windows ruling M3): a BRAW thumbnail (decoder piped into ffmpeg) runs under the stall watchdog.
    func testThumbnailDecoderHangsKilledAsStalled() {
        let dir = tempDir()
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang)
        let t0 = Date()
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path).thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"),
                                                                          info: BrawTest.clipInfo(frames: 1))) { e in
            XCTAssertEqual("\(e)", "dekodér nebo ffmpeg se zasekl (1 s bez nového snímku)")
        }
        XCTAssertLessThan(Date().timeIntervalSince(t0), 30)
    }

    // Windows ruling T8-extra, final fix M2: one stall per clip, and the log says why the other frames are missing.
    func testThumbnailDecoderHangsOnlyOneStallAttempt() {
        let dir = tempDir()
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.hang)
        let log = LogLines()
        let info = BrawTest.clipInfo(frames: 100)
        XCTAssertEqual(ProxyRenderer.framePositions(info).count, 3)
        let t0 = Date()
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path, log: log).thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"),
                                                                                    info: info)) { e in
            XCTAssertTrue("\(e)".hasPrefix("dekodér nebo ffmpeg se zasekl"), "\(e)")
        }
        XCTAssertEqual(BrawTest.calls(fake.calls).filter { $0 != "--info" }.count, 1)
        XCTAssertLessThan(Date().timeIntervalSince(t0), 60)
        XCTAssertTrue(log.lines.contains("thumbnails A001: dekodér nebo ffmpeg se zasekl (1 s bez nového snímku); the remaining frames are skipped"),
                      "\(log.lines)")
    }

    // Windows final fix M1: a hung `braw-decode --info` (timeout) is not tried again at the next positions either.
    func testThumbnailInfoHangsOnlyOneAttempt() {
        let dir = tempDir()
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.hang, frames: BrawTest.hang)
        let log = LogLines()
        let info = BrawTest.clipInfo(frames: 100)
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path, log: log, infoTimeout: 1)
            .thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"), info: info)) { e in
            XCTAssertEqual("\(e)", "braw-decode failed (exit -1): neodpověděl do 1 s — ukončen")
        }
        XCTAssertEqual(BrawTest.calls(fake.calls), ["--info"])
        XCTAssertTrue(log.lines.contains("thumbnails A001: braw-decode failed (exit -1): neodpověděl do 1 s — ukončen; the remaining frames are skipped"),
                      "\(log.lines)")
    }

    // Windows final fix I1: the decoder fails while ffmpeg reads its frames — the same Czech note, its own line logged.
    func testFeederDecoderExitCodeBecomesCzechNoteOriginalLogged() {
        let dir = tempDir()
        let line = "braw-decode: cannot open /card/A001.braw (0x80004005; library /BM/BlackmagicRawAPI.framework)"
        let fake = BrawTest.fakeDecoder(dir, info: BrawTest.printInfo(), frames: BrawTest.fail(4, line))
        let log = LogLines()
        XCTAssertThrowsError(try renderer(dir, decoder: fake.path, log: log).thumbnails(BrawTest.brawClip, cardPath: dir.sub("card"),
                                                                                    info: BrawTest.clipInfo(frames: 1))) { e in
            XCTAssertTrue(e is RawDecoderError, "\(e)")
            XCTAssertTrue("\(e)".hasPrefix("Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna \(lib)"), "\(e)")
        }
        XCTAssertTrue(log.lines.contains("braw-decode failed (exit 4): \(line)"), "\(log.lines)")
    }

    // A decoder failing while ffmpeg still reads stays the error — with its stderr, no explanation without one.
    func testFeederFailsWhileFFmpegReadsIsTheError() {
        let dir = tempDir()
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: BrawTest.script(dir, "feeder", "echo boom >&2\nexit 3"))
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        XCTAssertThrowsError(try r.runFFmpeg(consumer(dir), [], feeder: feeder, duration: nil, progress: nil,
                                             isCancelled: { false }, stallAfter: 30)) { e in
            XCTAssertEqual("\(e)", "feeder failed (exit 3): boom")
        }
    }

    /// A decoder that writes until a write fails (SIGPIPE ignored, like braw-decode), then writes `stopped` and exits
    /// `code`; and a consumer that takes 1 KiB and ends, like ffmpeg with its one thumbnail frame.
    private func writesUntilCut(_ dir: TempDir, code: Int) -> (feeder: Process, consumer: String, stopped: String) {
        let stopped = dir.path + "/stopped.txt"
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: BrawTest.script(dir, "feeder",
            "trap '' PIPE\n/bin/dd if=/dev/zero bs=1024 2>/dev/null\necho stopped > '\(stopped)'\nexit \(code)"))
        return (feeder, BrawTest.script(dir, "consumer", "head -c 1024 > /dev/null"), stopped)
    }

    // Windows task 7: ffmpeg ends first → the pump closes the decoder's stdout, the decoder sees the broken pipe and
    // stops by itself instead of blocking on a full pipe until it is terminated.
    func testConsumerEndsFirstFeederSeesTheClosedPipeExitsByItself() throws {
        let dir = tempDir()
        let (feeder, consumer, stopped) = writesUntilCut(dir, code: 0)
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false }, stallAfter: 30)
        XCTAssertTrue(FileManager.default.fileExists(atPath: stopped), "the feeder never saw its stdout closed (it was terminated)")
        XCTAssertEqual(feeder.terminationReason, .exit)
    }

    // Windows task 7, fix 1: a decoder that fails because the pump cut its stdout is not the clip's error — ffmpeg decides.
    func testConsumerEndsFirstFeederFailsOnTheCutPipeNotAnError() throws {
        let dir = tempDir()
        let (feeder, consumer, stopped) = writesUntilCut(dir, code: 1)
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false }, stallAfter: 30)
        XCTAssertTrue(FileManager.default.fileExists(atPath: stopped), "the feeder never saw its stdout closed (it was terminated)")
        XCTAssertEqual(feeder.terminationStatus, 1)
    }
}
