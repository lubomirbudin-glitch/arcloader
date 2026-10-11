import XCTest
@testable import OffloadKit

final class SafeStatesTests: XCTestCase {
    private func status(_ phase: JobStatus.Phase, eject: Bool, format: Bool, warning: String? = nil,
                        copied: Bool? = true) -> JobStatus {
        var s = JobStatus()
        s.phase = phase
        s.safeToEject = eject
        s.safeToFormat = format
        s.warning = warning
        s.copySucceeded = copied
        return s
    }

    func testTexts() {
        XCTAssertEqual(Texts.safeToEject(1), "All files are on 1 drive — the card is safe to eject. Format it only after verification.")
        XCTAssertEqual(Texts.safeToFormat(2), "All files are on 2 drives and verified — the card is safe to eject and format.")
    }

    // Review focus 2: green only when the card may be formatted.
    func testGreenOnlyWhenSafeToFormat() {
        let done = JobProgress.chips(status(.done, eject: true, format: true), contentConfirmed: true)
        XCTAssertEqual(done.format, CardChip(text: "Safe to format", tone: .ok))
        XCTAssertEqual(done.eject, CardChip(text: "Safe to eject", tone: .ok))
        let notVerified = JobProgress.chips(status(.done, eject: true, format: false), contentConfirmed: true)
        XCTAssertEqual(notVerified.eject, CardChip(text: "Safe to eject", tone: .waiting), "safe to eject alone is neutral")
        XCTAssertEqual(notVerified.format, CardChip(text: "Do not format", tone: .warning))
        let failed = JobProgress.chips(status(.done, eject: false, format: false, copied: false), contentConfirmed: true)
        XCTAssertEqual(failed.eject, CardChip(text: "Do not eject", tone: .error))
        XCTAssertEqual(failed.format, CardChip(text: "Do not format", tone: .error))
        let verifying = JobProgress.chips(status(.verifying, eject: true, format: false), contentConfirmed: true)
        XCTAssertEqual(verifying.format, CardChip(text: "Verifying…", tone: .waiting))
        let copying = JobProgress.chips(status(.copying, eject: false, format: false), contentConfirmed: true)
        XCTAssertEqual(copying.eject, CardChip(text: "Do not eject yet", tone: .waiting))
        XCTAssertEqual(copying.format, CardChip(text: "Do not format yet", tone: .waiting))
        let defects = JobProgress.chips(status(.done, eject: true, format: true), contentConfirmed: false, contentWarnings: 2)
        XCTAssertEqual(defects.format, CardChip(text: "Safe to format after checking defects", tone: .warning))
        XCTAssertEqual(defects.eject.tone, .waiting)
    }

    func testToneOkOnlyWhenFormattable() {
        func tone(_ s: JobStatus) -> CardTone {
            let p = JobProgress(now: Date())
            p.update(s, now: Date())
            return p.tone
        }
        XCTAssertEqual(tone(status(.done, eject: true, format: true)), .ok)
        XCTAssertEqual(tone(status(.done, eject: true, format: false)), .warning, "copied, not verified: not green")
        XCTAssertEqual(tone(status(.done, eject: true, format: true, warning: "3 files were not verified")), .warning)
        XCTAssertEqual(tone(status(.done, eject: false, format: false, copied: false)), .error)
    }

    func testChimeOnlyWhenFormattableAndNoWarning() {
        XCTAssertTrue(JobProgress.chime(status(.done, eject: true, format: true)))
        XCTAssertFalse(JobProgress.chime(status(.done, eject: true, format: false)))
        XCTAssertFalse(JobProgress.chime(status(.done, eject: true, format: true,
                                                warning: "3 files of the earlier backup were not verified")))
        XCTAssertFalse(JobProgress.chime(status(.cancelled, eject: true, format: true)))
        XCTAssertFalse(JobProgress.chime(status(.failed, eject: true, format: true)))
    }

    func testWarningKeepsTheFirst() {
        var s = JobStatus()
        s.addWarning("first")
        s.addWarning("second")
        XCTAssertEqual(s.warning, "first")
    }

    func testCopyWithoutVerifyIsEjectableNotFormattable() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        let dest = dir.sub("dest")
        TestFS.writeRandom(card + "/A.bin", size: 1000, seed: 1)
        var cfg = JobConfig(source: card, destRoots: [dest])
        cfg.verify = false
        cfg.analyzeClips = false
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        let job = OffloadJob(config: cfg, options: o)
        let report = try XCTUnwrap(job.run())
        XCTAssertFalse(report.verified)
        XCTAssertTrue(job.status().safeToEject)
        XCTAssertFalse(job.status().safeToFormat)
    }

    func testCopyWithVerifyIsFormattable() throws {
        let dir = tempDir()
        let card = dir.sub("card")
        TestFS.writeRandom(card + "/A.bin", size: 1000, seed: 2)
        var cfg = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        cfg.analyzeClips = false
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let job = OffloadJob(config: cfg, options: o)
        _ = try XCTUnwrap(job.run(), job.status().error ?? "")
        XCTAssertTrue(job.status().safeToEject)
        XCTAssertTrue(job.status().safeToFormat)
    }

    func testHtmlReportGreenOnlyWhenSafeToFormat() {
        let ok = HTMLReport.render(Reports.sample())
        XCTAssertTrue(ok.contains("All files are on 1 drive and verified — the card is safe to eject and format."))
        XCTAssertTrue(ok.contains("class=\"badge ok\""))
        let notVerified = HTMLReport.render(Reports.sample(verified: false))
        XCTAssertTrue(notVerified.contains("All files are on 1 drive — the card is safe to eject. Format it only after verification."))
        XCTAssertFalse(notVerified.contains("class=\"badge ok\""))
        var pending = Reports.sample()
        pending.verifyPending = true
        let p = HTMLReport.render(pending)
        XCTAssertFalse(p.contains("class=\"badge ok\""), "verification still running: never green")
        XCTAssertFalse(p.contains("safe to eject and format"))
        var incomplete = Reports.sample()
        incomplete.verifyIncomplete = true
        XCTAssertFalse(HTMLReport.render(incomplete).contains("class=\"badge ok\""))
        let failed = HTMLReport.render(Reports.sample(destError: "HASH MISMATCH"))
        XCTAssertFalse(failed.contains("safe to eject"))
    }
}
