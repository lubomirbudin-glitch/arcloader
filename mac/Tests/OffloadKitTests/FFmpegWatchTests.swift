import XCTest
@testable import OffloadKit

final class FFmpegWatchTests: XCTestCase {
    // Hardware decoding is off in the app (green frames on Intel); these tests exercise the retry path with it on.
    override func setUp() { super.setUp(); ProxyRenderer.hardwareDecodeEnabled = true }
    override func tearDown() { ProxyRenderer.hardwareDecodeEnabled = false; super.tearDown() }

    func testHardwareDecodeOnlyForH264AndHEVC() {
        for (codec, expected) in [("h264", true), ("hevc", true), ("HEVC", true), ("h264 High", true), ("hevc Main 10", true),
                                  ("ffv1", false), ("prores HQ", false), ("mjpeg", false), ("dnxhd", false), ("", false)] {
            XCTAssertEqual(ProxyRenderer.usesHardwareDecode(codec), expected, codec)
        }
        XCTAssertFalse(ProxyRenderer.usesHardwareDecode(nil))
    }

    func testProgressWatchOnlyARisingFrameOrTimeCounts() {
        let w = ProgressWatch()
        // The opening block says nothing has been written yet (encoder lookahead, input probing).
        XCTAssertFalse(w.advanced("frame=0"))
        XCTAssertFalse(w.advanced("fps=0.00"))
        XCTAssertFalse(w.advanced("out_time_us=N/A"))
        XCTAssertFalse(w.advanced("out_time_us=-9223372036854775807"))
        XCTAssertFalse(w.advanced("progress=continue"))
        XCTAssertFalse(w.hasAdvanced)
        XCTAssertTrue(w.advanced("frame=1"))
        XCTAssertTrue(w.advanced("out_time_us=40000"))
        // ffmpeg keeps printing the same block every half second while it is stuck.
        XCTAssertFalse(w.advanced("frame=1"))
        XCTAssertFalse(w.advanced("out_time_us=40000"))
        XCTAssertFalse(w.advanced("progress=continue"))
        XCTAssertTrue(w.advanced("frame=2"))
        XCTAssertTrue(w.advanced("progress=end"))
        XCTAssertTrue(w.hasAdvanced)
    }

    // Before the first progress 3× the limit applies; data piped in by a RAW decoder is progress too.
    func testGraceUntilTheFirstProgressAndFeederDataCounts() {
        let t0 = Date()
        let w = ProgressWatch(now: t0)
        XCTAssertFalse(w.isStalled(limit: 60, now: t0.addingTimeInterval(179)))
        XCTAssertTrue(w.isStalled(limit: 60, now: t0.addingTimeInterval(181)))
        w.dataFlowed(now: t0.addingTimeInterval(10))
        XCTAssertTrue(w.hasAdvanced)
        XCTAssertEqual(w.idleSeconds(now: t0.addingTimeInterval(40)), 30, accuracy: 0.001)
        XCTAssertFalse(w.isStalled(limit: 60, now: t0.addingTimeInterval(69)))
        XCTAssertTrue(w.isStalled(limit: 60, now: t0.addingTimeInterval(71)))
    }

    func testStallNotesAreTheWindowsTexts() {
        XCTAssertEqual(ProxyRenderer.stallNote(60, retriedInSoftware: false), "ffmpeg stalled (60 s without a new frame)")
        XCTAssertEqual(ProxyRenderer.stallNote(60, retriedInSoftware: true),
                       "ffmpeg stalled (60 s without a new frame), also with software decoding; proxy not created")
        XCTAssertEqual(ProxyRenderer.defaultStallTimeout, 60)
        XCTAssertEqual(ProxyRenderer.killWait, 10)
    }

    private enum Mode: String { case ok, stall, fail, slowStart }

    private struct Fake {
        let path: String
        let calls: String
        /// One line per run: the arguments it got.
        var invocations: [String] {
            ((try? String(contentsOfFile: calls, encoding: .utf8)) ?? "").split(separator: "\n").map(String.init)
        }
    }

    /// A stand-in "ffmpeg": logs its arguments, then — depending on whether it got -hwaccel — stalls (no progress,
    /// sleeps), fails (exit 1, "boom hw" / "boom sw" on stderr), starts slowly (2 s without progress) or prints `steps`
    /// rising progress blocks 0.3 s apart and creates its output (the last argument).
    private func fakeFFmpeg(_ dir: TempDir, hw: Mode, sw: Mode, steps: Int = 2) -> Fake {
        let path = dir.path + "/fake-ffmpeg"
        let calls = dir.path + "/calls.txt"
        let script = """
        #!/bin/sh
        echo "$*" >> '\(calls)'
        case " $* " in *" -hwaccel "*) MODE=\(hw.rawValue); WHO=hw;; *) MODE=\(sw.rawValue); WHO=sw;; esac
        [ "$MODE" = stall ] && exec sleep 30
        if [ "$MODE" = fail ]; then echo "boom $WHO" >&2; exit 1; fi
        [ "$MODE" = slowStart ] && sleep 2
        i=0
        while [ $i -lt \(steps) ]; do i=$((i+1)); echo "frame=$i"; echo "out_time_us=${i}00000"; sleep 0.3; done
        for last; do :; done
        : > "$last"
        """
        try! script.write(toFile: path, atomically: true, encoding: .utf8)
        try! FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: path)
        return Fake(path: path, calls: calls)
    }

    private func script(_ dir: TempDir, _ name: String, _ body: String) -> String {
        let path = dir.path + "/" + name
        try! ("#!/bin/sh\n" + body + "\n").write(toFile: path, atomically: true, encoding: .utf8)
        try! FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: path)
        return path
    }

    private func clip() -> Clip {
        Clip(name: "a", path: "a.mov", format: .video, segments: ["a.mov"], bytes: 1, isCameraProxy: false)
    }

    private func info(_ codec: String = "h264 High") -> ClipInfo {
        var info = ClipInfo()
        info.codec = codec
        info.frameRate = "25/1"
        info.duration = 1
        return info
    }

    private final class Lines {
        private let lock = NSLock()
        private var all: [String] = []
        func add(_ s: String) { lock.lock(); all.append(s); lock.unlock() }
        var lines: [String] { lock.lock(); defer { lock.unlock() }; return all }
    }

    private func renderer(_ fake: Fake, stall: TimeInterval = 1, log: Lines? = nil) -> ProxyRenderer {
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools(ffmpeg: fake.path))
        r.stallTimeout = stall
        if let log { r.log = { log.add($0) } }
        return r
    }

    private func render(_ r: ProxyRenderer, _ dir: TempDir, codec: String = "h264 High",
                        isCancelled: @escaping () -> Bool = { false }) throws -> String {
        let card = dir.sub("card")
        try r.render(clip(), cardPath: card, info: info(codec), output: card + "/_PROXY/a.mov", progress: { _ in }, isCancelled: isCancelled)
        return card + "/_PROXY/a.mov"
    }

    // Review focus 9: a stalled VideoToolbox decode never blocks the queue — stopped, rendered once more in software.
    func testStalledWithHwaccelSoftwareRetrySucceedsAndIsLogged() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        let log = Lines()
        let t0 = Date()
        let out = try render(renderer(fake, log: log), dir)
        XCTAssertLessThan(Date().timeIntervalSince(t0), 20)
        XCTAssertTrue(FileManager.default.fileExists(atPath: out))
        let calls = fake.invocations
        XCTAssertEqual(calls.count, 2, "\(calls)")
        XCTAssertTrue(calls[0].contains("-hwaccel videotoolbox"), calls[0])
        XCTAssertFalse(calls[1].contains("-hwaccel"), calls[1])
        XCTAssertEqual(TestFS.files(in: dir.path + "/card/_PROXY"), ["a.mov"])   // no .part left behind
        XCTAssertTrue(log.lines.contains { $0.contains("with hardware decoding: ffmpeg stalled") && $0.contains("retrying in software") }, "\(log.lines)")
        XCTAssertTrue(log.lines.contains("proxy a: software decoding succeeded"), "\(log.lines)")
    }

    // Review focus 9: stuck with and without hardware decoding → the Windows note, no proxy, no .part.
    func testStalledTwiceFailsWithTheNote() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .stall)
        let log = Lines()
        XCTAssertThrowsError(try render(renderer(fake, log: log), dir)) { e in
            XCTAssertEqual("\(e)", "ffmpeg stalled (1 s without a new frame), also with software decoding; proxy not created")
        }
        XCTAssertEqual(fake.invocations.count, 2)
        XCTAssertEqual(TestFS.files(in: dir.path + "/card/_PROXY"), [])
        XCTAssertTrue(log.lines.contains { $0.contains("software decoding failed too") }, "\(log.lines)")
    }

    func testStalledWithHwaccelThenSoftwareFailsSaysBoth() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .fail)
        XCTAssertThrowsError(try render(renderer(fake), dir)) { e in
            XCTAssertTrue("\(e)".hasPrefix("ffmpeg stalled (1 s without a new frame); software decoding failed: ffmpeg failed (exit 1)"), "\(e)")
        }
        XCTAssertEqual(fake.invocations.count, 2)
    }

    func testHwaccelFailsSoftwareFailsNoteNamesBoth() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .fail, sw: .fail)
        XCTAssertThrowsError(try render(renderer(fake), dir)) { e in
            let note = "\(e)"
            XCTAssertTrue(note.hasPrefix("ffmpeg failed (exit 1)"), note)
            XCTAssertTrue(note.contains("; earlier with hardware decoding: ffmpeg failed (exit 1)"), note)
        }
        XCTAssertEqual(fake.invocations.count, 2)
    }

    // A software codec that stalls is not retried (there is nothing to switch off) — and the note does not claim it was.
    func testSoftwareCodecStallsNoRetryNoteWithoutSoftwareClaim() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .ok, sw: .stall)
        XCTAssertThrowsError(try render(renderer(fake), dir, codec: "ffv1")) { e in
            XCTAssertEqual("\(e)", "ffmpeg stalled (1 s without a new frame); proxy not created")
        }
        XCTAssertEqual(fake.invocations.count, 1)
    }

    func testHwaccelFailureRetriedInSoftware() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .fail, sw: .ok)
        XCTAssertTrue(FileManager.default.fileExists(atPath: try render(renderer(fake), dir)))
        XCTAssertEqual(fake.invocations.count, 2)
    }

    func testSoftwareFailureIsNotRetried() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .ok, sw: .fail)
        XCTAssertThrowsError(try render(renderer(fake), dir, codec: "prores HQ")) { e in
            XCTAssertTrue(e is ToolError, "\(e)")
        }
        XCTAssertEqual(fake.invocations.count, 1)
    }

    func testSoftwareCodecNoHwaccelAtAll() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        _ = try render(renderer(fake), dir, codec: "ffv1")
        XCTAssertEqual(fake.invocations.count, 1)
        XCTAssertFalse(fake.invocations[0].contains("-hwaccel"))
    }

    // Cancelling while ffmpeg is stuck stops at once and is never retried.
    func testCancelledWhileStuckNoRetry() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        let t0 = Date()
        XCTAssertThrowsError(try render(renderer(fake, stall: 30), dir, isCancelled: { !fake.invocations.isEmpty })) { e in
            XCTAssertTrue("\(e)".contains("cancelled"), "\(e)")
        }
        XCTAssertEqual(fake.invocations.count, 1)
        XCTAssertLessThan(Date().timeIntervalSince(t0), 20)
    }

    // Cancelled while the hardware-decoded attempt failed: no software retry.
    func testCancelledAfterAHwaccelFailureNoRetry() {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .fail, sw: .ok)
        XCTAssertThrowsError(try render(renderer(fake), dir, isCancelled: { !fake.invocations.isEmpty }))
        XCTAssertEqual(fake.invocations.count, 1)
    }

    func testSlowButMovingRenderIsNotStopped() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .ok, sw: .ok, steps: 6)   // 1.8 s in all, but a new frame every 0.3 s
        _ = try render(renderer(fake), dir)
        XCTAssertEqual(fake.invocations.count, 1)
        XCTAssertTrue(fake.invocations[0].contains("-hwaccel videotoolbox"))
    }

    // Review: a slow first frame (2 s, limit 1 s) is within the 3× grace before the first progress.
    func testSlowFirstFrameWithinGraceNotKilled() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .slowStart, sw: .ok)
        _ = try render(renderer(fake), dir)
        XCTAssertEqual(fake.invocations.count, 1)
    }

    // Data a RAW decoder pipes into ffmpeg is progress: a consumer that prints nothing but takes data every 0.5 s for
    // 3.5 s (longer than the 3 s grace) is not killed.
    func testFeederDataIsProgress() throws {
        let dir = tempDir()
        let consumer = script(dir, "consumer", "cat > /dev/null")
        let feederPath = script(dir, "feeder", "i=0\nwhile [ $i -lt 7 ]; do i=$((i+1)); echo data; sleep 0.5; done")
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: feederPath)
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false }, stallAfter: 1)
    }

    func testNoFeederDataAtAllKilledAfterTheGrace() {
        let dir = tempDir()
        let consumer = script(dir, "consumer", "cat > /dev/null")
        let feeder = Process()
        feeder.executableURL = URL(fileURLWithPath: script(dir, "feeder", "exec sleep 60"))
        let r = ProxyRenderer(settings: ProxySettings(), tools: ExternalTools())
        let t0 = Date()
        XCTAssertThrowsError(try r.runFFmpeg(consumer, [], feeder: feeder, duration: nil, progress: nil, isCancelled: { false },
                                             stallAfter: 1)) { e in
            XCTAssertEqual("\(e)", "ffmpeg stalled (1 s without a new frame)")
        }
        XCTAssertTrue((2.8...20).contains(Date().timeIntervalSince(t0)), "\(Date().timeIntervalSince(t0))")
    }

    func testThumbnailsNeverUseHardwareDecode() throws {
        let dir = tempDir()
        let fake = fakeFFmpeg(dir, hw: .stall, sw: .ok)
        let thumbs = try renderer(fake).thumbnails(clip(), cardPath: dir.sub("card"), info: info())
        XCTAssertEqual(thumbs.count, 3)
        XCTAssertFalse(fake.invocations.contains { $0.contains("-hwaccel") }, "\(fake.invocations)")
    }
}
