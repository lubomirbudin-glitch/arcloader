import XCTest
@testable import OffloadKit

/// Task 70: `--primary N`, `--classic-verify`, the exit code and the settings keys `verifyInProxy`, `verifyPrimary`.
final class VerifyInProxyCLITests: XCTestCase {
    func testPrimaryOutOfRange() {
        XCTAssertNil(try VerifyOptions.primaryIndex(nil, destinations: 2))
        XCTAssertEqual(try VerifyOptions.primaryIndex(2, destinations: 2), 1)
        XCTAssertThrowsError(try VerifyOptions.primaryIndex(0, destinations: 2)) {
            XCTAssertEqual("\($0)", "--primary 0: destinations are numbered from 1 (the order of --dest).")
        }
        XCTAssertThrowsError(try VerifyOptions.primaryIndex(5, destinations: 2)) {
            XCTAssertEqual("\($0)", "--primary 5: there are only 2 destinations (numbered from 1 in the order of --dest).")
        }
        XCTAssertThrowsError(try VerifyOptions.primaryIndex(3, destinations: 1)) {
            XCTAssertEqual("\($0)", "--primary 3: there is only 1 destination (numbered from 1 in the order of --dest).")
        }
    }

    func testExitCodeOnlyWhenSafeToFormat() {
        let r = OffloadReport(cardName: "A", date: "2026-10-08", source: "/s", sourceFilesystem: "apfs", started: Date(),
                              copyFinished: Date(), finished: Date(), verified: true, totalFiles: 0, totalBytes: 0,
                              destinations: [], files: [], skipped: [])
        var s = JobStatus()
        s.phase = .done
        s.safeToFormat = true
        XCTAssertEqual(VerifyOptions.exitCode(verify: true, s, r), 0)
        s.safeToFormat = false   // e.g. the verification result not written everywhere
        XCTAssertEqual(VerifyOptions.exitCode(verify: true, s, r), 1)
        XCTAssertEqual(VerifyOptions.exitCode(verify: false, s, r), 0, "--no-verify stays 0")
        s.phase = .failed
        XCTAssertEqual(VerifyOptions.exitCode(verify: true, s, r), 1)
    }

    func testSettingsKeys() throws {
        var s = AppSettings()
        XCTAssertTrue(s.verifyInProxy)
        XCTAssertEqual(s.verifyPrimary, "fastest")
        s.verifyInProxy = false
        s.verifyPrimary = "first"
        let json = try XCTUnwrap(SettingsStore.encode(s))
        let text = String(decoding: json, as: UTF8.self)
        XCTAssertTrue(text.contains(#""verifyInProxy":false"#), text)
        XCTAssertTrue(text.contains(#""verifyPrimary":"first""#), text)
        let back = SettingsStore.decode(json)
        XCTAssertFalse(back.verifyInProxy)
        XCTAssertEqual(back.verifyPrimary, "first")
        let odd = SettingsStore.decode(Data(#"{"verifyPrimary":"slowest"}"#.utf8))
        XCTAssertEqual(odd.verifyPrimary, "slowest", "a newer version's value is kept")
        XCTAssertEqual(VerifyOptions.primarySetting(odd.verifyPrimary), "fastest", "and reads as fastest")
        XCTAssertTrue(SettingsStore.decode(Data("{}".utf8)).verifyInProxy, "default on")
    }

    // --classic-verify: the read-back after the copy, as before 0.7.0 — the method in the report is read-back everywhere.
    func testClassicVerifyMethodIsReadBack() throws {
        let dir = tempDir()
        let card = dir.sub("A001")
        TestFS.writeRandom(card + "/A001C001.mov", size: 5000, seed: 1)
        var cfg = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        cfg.verifyInProxy = false
        cfg.analyzeClips = false
        cfg.proxy.enabled = true
        cfg.tools = ExternalTools(ffmpeg: "/usr/bin/true", ffprobe: "/usr/bin/true")
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let j = OffloadJob(config: cfg, options: o)
        XCTAssertTrue(j.runCopy())
        XCTAssertFalse(j.status().verifyInProxy)
        let r = try XCTUnwrap(j.report)
        XCTAssertEqual(r.destinations.map(\.verifyMethod), ["read-back", "read-back"])
        XCTAssertTrue(j.status().safeToFormat)
        j.cancel()
        j.runPost()
    }
}
