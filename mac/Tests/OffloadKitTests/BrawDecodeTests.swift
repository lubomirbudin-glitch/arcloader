import XCTest
@testable import OffloadKit

/// The real braw-decode (mac/Tools/bin). The command line needs nothing else, exit codes the Blackmagic RAW library,
/// the rest a sample clip (OFFLOAD_BRAW_SAMPLE).
final class BrawDecodeTests: XCTestCase {
    // Strict like Windows Options.Parse: checked before the library is looked for, so it runs on any Mac.
    func testBadCommandLineIsExit2() throws {
        let exe = try BrawTest.requireDecoder()
        let bad: [[String]] = [[], ["--info"], ["--scale", "huge", "a.braw"], ["--scale"], ["--start", "abc", "a.braw"],
                               ["--start", "-1", "a.braw"], ["--count", "1x", "a.braw"], ["--count", "", "a.braw"],
                               ["--bogus", "a.braw"]]
        for args in bad {
            let r = try BrawTest.run(exe, args)
            XCTAssertEqual(r.code, 2, "\(args)")
            XCTAssertTrue(r.stderr.hasPrefix("usage: braw-decode"), "\(args): \(r.stderr)")
            XCTAssertTrue(r.stdout.isEmpty, "\(args)")
        }
    }

    // Windows final fix I1: exit 4 names the library in use (an old one cannot open clips from newer cameras).
    func testMissingOrInvalidClipIsExit4AndNamesTheLibrary() throws {
        let exe = try BrawTest.requireDecoder()
        _ = try BrawTest.requireLibrary()
        let dir = tempDir()
        let invalid = dir.path + "/not-a-clip.braw"
        try "x".write(toFile: invalid, atomically: true, encoding: .utf8)
        for clip in [dir.path + "/missing.braw", invalid] {
            let r = try BrawTest.run(exe, ["--info", clip])
            XCTAssertEqual(r.code, 4, r.stderr)
            XCTAssertTrue(r.stderr.contains("braw-decode: cannot open \(clip) (0x"), r.stderr)
            XCTAssertTrue(r.stderr.contains("; library /"), r.stderr)
            XCTAssertTrue(r.stderr.contains("/BlackmagicRawAPI.framework)"), r.stderr)
        }
    }

    // Spec D.14 (Windows task 6, fix 2): stderr is UTF-8 — a path with diacritics comes back intact in the note.
    func testStderrKeepsADiacriticPath() throws {
        let exe = try BrawTest.requireDecoder()
        _ = try BrawTest.requireLibrary()
        let clip = tempDir().path + "/Natáčení den 1/Klip č. 1.braw"
        let r = try BrawTest.run(exe, ["--info", clip])
        XCTAssertEqual(r.code, 4, r.stderr)
        XCTAssertTrue(r.stderr.contains(clip), r.stderr)
    }

    // MARK: with a sample clip

    private func info(_ exe: String, _ sample: String, _ scale: String = "eighth") throws -> [String: Any] {
        let r = try BrawTest.run(exe, ["--info", "--scale", scale, sample])
        XCTAssertEqual(r.code, 0, r.stderr)
        return try BrawTest.json(r.stdout)
    }

    private func frameBytes(_ i: [String: Any]) -> Int { (i["width"] as? Int ?? 0) * (i["height"] as? Int ?? 0) * 6 }

    // Spec D.14: the SDK's Variant for 'gama'/'gamt'/'csgn' — only a sample proves the types.
    func testInfoDescribesTheClipWithGammaGamutGen() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let r = try BrawTest.run(exe, ["--info", "--scale", "eighth", sample])
        XCTAssertEqual(r.code, 0, r.stderr)
        // A variant of an unexpected type is a warning here and the key is missing — read stderr first.
        XCTAssertFalse(r.stderr.contains("clip attribute"), r.stderr)
        let i = try BrawTest.json(r.stdout)
        let sw = i["sourceWidth"] as? Int ?? 0, sh = i["sourceHeight"] as? Int ?? 0, w = i["width"] as? Int ?? 0
        XCTAssertTrue(sw >= 1920 && sh >= 1080, "\(sw)x\(sh)")
        XCTAssertTrue((sw / 9...sw / 4).contains(w), "processed width \(w) of \(sw) at 1/8")
        XCTAssertGreaterThan(i["fps"] as? Double ?? 0, 0)
        XCTAssertGreaterThan(i["frames"] as? Int ?? 0, 0)
        XCTAssertNotNil((i["timecode"] as? String)?.range(of: #"^\d\d:\d\d:\d\d[:;]\d\d$"#, options: .regularExpression))
        XCTAssertFalse((i["camera"] as? String ?? "").isEmpty)
        let gamma = i["gamma"] as? String ?? ""
        XCTAssertFalse(gamma.isEmpty, "gamma missing")
        XCTAssertFalse((i["gamut"] as? String ?? "").isEmpty, "gamut missing")
        XCTAssertTrue((4...9).contains(i["colorScienceGen"] as? Int ?? 0), "\(i["colorScienceGen"] ?? "nil")")
        // The LUT rule (task 28) compares the gamma with exactly this SDK string.
        if gamma.localizedCaseInsensitiveContains("film") { XCTAssertEqual(gamma, "Blackmagic Design Film") }
        let full = try info(exe, sample, "full")
        XCTAssertEqual(full["width"] as? Int, sw)
        XCTAssertEqual(full["height"] as? Int, sh)
    }

    func testFramesExactRGB48BytesInOrder() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let frame = frameBytes(try info(exe, sample))
        let two = try BrawTest.run(exe, ["--scale", "eighth", "--start", "0", "--count", "2", sample])
        XCTAssertEqual(two.code, 0, two.stderr)
        XCTAssertEqual(two.stdout.count, 2 * frame)
        let second = try BrawTest.run(exe, ["--scale", "eighth", "--start", "1", "--count", "1", sample])
        XCTAssertEqual(two.stdout.subdata(in: frame..<2 * frame), second.stdout)   // the same frame decodes to the same bytes
        XCTAssertTrue(two.stdout.contains { $0 != 0 })                              // not a black buffer
    }

    /// More than one batch of 8: batches are written in frame order.
    func testFramesAcrossBatchesInOrder() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let i = try info(exe, sample)
        try XCTSkipUnless((i["frames"] as? Int ?? 0) >= 10, "the sample needs at least 10 frames")
        let frame = frameBytes(i)
        let ten = try BrawTest.run(exe, ["--scale", "eighth", "--count", "10", sample])
        XCTAssertEqual(ten.code, 0, ten.stderr)
        XCTAssertEqual(ten.stdout.count, 10 * frame)
        let ninth = try BrawTest.run(exe, ["--scale", "eighth", "--start", "9", "--count", "1", sample])
        XCTAssertEqual(ten.stdout.subdata(in: 9 * frame..<10 * frame), ninth.stdout)
    }

    func testFramesClampedAtTheEnd() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let i = try info(exe, sample)
        let frames = i["frames"] as? Int ?? 0
        let last = try BrawTest.run(exe, ["--scale", "eighth", "--start", String(frames - 1), "--count", "5", sample])
        XCTAssertEqual(last.code, 0, last.stderr)
        XCTAssertEqual(last.stdout.count, frameBytes(i))
        let past = try BrawTest.run(exe, ["--scale", "eighth", "--start", String(frames), sample])
        XCTAssertEqual(past.code, 0, past.stderr)
        XCTAssertTrue(past.stdout.isEmpty)
    }

    /// Windows ruling M1: when the reader closes the pipe (ffmpeg has what it wanted) braw-decode stops promptly and
    /// exits 0 — not killed by SIGPIPE, and not decoding the rest of the clip for nobody.
    func testReaderClosesThePipeStopsWithExit0() throws {
        let exe = try BrawTest.requireDecoder()
        let sample = try BrawTest.requireSample()
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = ["--scale", "eighth", sample]   // the whole clip
        let out = Pipe(), err = Pipe()
        p.standardOutput = out
        p.standardError = err
        try p.run()
        defer { if p.isRunning { p.terminate() } }
        XCTAssertEqual(try out.fileHandleForReading.read(upToCount: 1024)?.count, 1024)   // decoding has started
        try out.fileHandleForReading.close()                                                // "ffmpeg exited"
        let deadline = Date().addingTimeInterval(60)
        while p.isRunning && Date() < deadline { usleep(50_000) }
        XCTAssertFalse(p.isRunning, "braw-decode kept running after its reader went away")
        XCTAssertEqual(p.terminationReason, .exit, "ended by a signal — SIGPIPE not ignored?")
        XCTAssertEqual(p.terminationStatus, 0)
        let stderr = String(decoding: err.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        XCTAssertTrue(stderr.contains("braw-decode: stdout closed by the reader, stopping"), stderr)
    }
}
