import Foundation
import XCTest
@testable import OffloadKit

/// The day report (FORMATY „Denní report“, Windows `DayReport`): `<root>/<date>/_REPORT/<date>_day_report.html/.pdf`,
/// regenerated after each job and on request (`offload day-report`).
final class DayReportTests: XCTestCase {
    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 64 * 1024
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    /// A card folder with a report written as an offload writes it (JSON, TXT, HTML, ASC MHL, hash list).
    private func card(_ day: String, _ name: String, verified: Bool = true, failed: Bool = false, clips: [ClipEntry] = []) throws {
        var r = Reports.sample(verified: verified, destError: failed ? "size differs" : nil)
        r.cardName = name
        r.date = (day as NSString).lastPathComponent
        r.destinations[0].cardPath = day + "/" + name
        r.destinations[0].root = (day as NSString).deletingLastPathComponent
        r.clips = clips
        try IO.mkdirs(day + "/" + name)
        try r.write(cardPath: day + "/" + name, includeHashRecords: true)
    }

    private func clip(_ name: String, playback: String, capture: String?, seconds: Double, camera: String) -> ClipEntry {
        var e = ClipEntry(clip: Clip(name: name, path: name + ".mxf", format: .video, segments: [name + ".mxf"], bytes: 1, isCameraProxy: false))
        var i = ClipInfo()
        i.frameRate = playback
        i.captureFrameRate = capture
        i.duration = seconds
        i.camera = camera
        e.info = i
        e.proxyStatus = .done
        e.lut = LutUse(source: "auto", id: "arri-logc4-awg4", name: "ARRI LogC4 to Rec.709", reason: "ALEXA 35")
        return e
    }

    func testDayWithCardsRunningCardAndOtherFolder() throws {
        let dir = tempDir()
        let day = dir.sub("RAID", "2026-10-11")
        try card(day, "A001", clips: [clip("A001C001", playback: "24", capture: "35", seconds: 60, camera: "ARRI ALEXA 35"),
                                      clip("A001C002", playback: "24", capture: "35", seconds: 30, camera: "ARRI ALEXA 35")])
        try card(day, "B001", verified: false)
        try card(day, "C001", failed: true)
        dir.sub("RAID", "2026-10-11", "A002", "_REPORT")            // a copy in progress: _REPORT without a report
        dir.sub("RAID", "2026-10-11", "SOUND")                      // copied by hand
        // A verification report of another day job lives in the day's _REPORT and is never touched.
        let verify = dir.file("RAID", "2026-10-11", "_REPORT", "A001_verify_2026-10-11_120000Z.html")
        try Data("verify".utf8).write(to: URL(fileURLWithPath: verify))

        let model = DayReport.load(day)
        XCTAssertEqual(model.folders.map(\.name), ["A001", "A002", "B001", "C001", "SOUND"])
        XCTAssertEqual(model.cards.count, 4, "SOUND is no card")
        XCTAssertEqual(model.verifiedCards, 1, "B001 not verified, C001 with a failed file, A002 without a report")
        let html = DayReport.html(model)
        XCTAssertTrue(html.contains("Day report — 2026-10-11"))
        XCTAssertTrue(html.contains("NEEDS ATTENTION"))
        XCTAssertTrue(html.contains("no report (running, or it was interrupted)"))
        XCTAssertTrue(html.contains("other folder (no report)"))
        XCTAssertTrue(html.contains("1 / 4"))
        XCTAssertTrue(html.contains("35 fps · playback 24"), "the capture rate in the FPS column")
        XCTAssertTrue(html.contains("1:30"), "total duration from the playback rate")
        XCTAssertTrue(html.contains("ARRI ALEXA 35"))
        XCTAssertTrue(html.contains("ARRI LogC4 to Rec.709"))
        XCTAssertTrue(html.contains("not verified — the copy was not read back"))
        XCTAssertTrue(html.contains("1 file failed: CLIP/c 02.mov"))
        XCTAssertTrue(html.contains("verified, with errors"))

        let written = try DayReportService.regenerate(day, pdf: false)
        XCTAssertEqual(written, day + "/_REPORT/2026-10-11_day_report.html")
        XCTAssertEqual(try String(contentsOfFile: verify), "verify", "the verification report stays as it was")
        // A card folder given: its day.
        XCTAssertEqual(try DayReportService.regenerate(day + "/A001", pdf: false), written)
        XCTAssertEqual(Set(try FileManager.default.contentsOfDirectory(atPath: day + "/_REPORT")),
                       ["A001_verify_2026-10-11_120000Z.html", "2026-10-11_day_report.html"])
    }

    func testAllVerifiedBadgeAndProject() throws {
        let dir = tempDir()
        let day = dir.sub("RAID", "2026-10-12")
        try card(day, "A001")
        let html = DayReport.html(DayReport.load(day))
        XCTAssertTrue(html.contains("ALL VERIFIED"))
        XCTAssertTrue(html.contains("1 / 1"))
    }

    func testRefusesWhatIsNoDay() throws {
        let dir = tempDir()
        let root = dir.sub("RAID")
        let day = dir.sub("RAID", "2026-10-11")
        XCTAssertThrowsError(try DayReport.checkDay(dir.path + "/missing")) { XCTAssertTrue("\($0)".contains("Day folder not found")) }
        XCTAssertThrowsError(try DayReport.checkDay(day)) { XCTAssertTrue("\($0)".contains("has no card with a report"), "\($0)") }
        try card(day, "A001")
        XCTAssertThrowsError(try DayReport.checkDay(root), "a destination root is no day")
        XCTAssertThrowsError(try DayReport.checkDay(day + "/A001"), "a card folder itself is no day (its subfolders have no report)")
        XCTAssertThrowsError(try DayReport.checkDay("/"), "a volume root")
        XCTAssertNoThrow(try DayReport.checkDay(day))
        // A card named _REPORT in the day: the day files would land in it — refused, nothing written.
        try card(day, "_REPORT")
        XCTAssertThrowsError(try DayReportService.regenerate(day, pdf: false)) { XCTAssertTrue("\($0)".contains("has a card named"), "\($0)") }
        XCTAssertFalse(FileManager.default.fileExists(atPath: day + "/_REPORT/2026-10-11_day_report.html"))
    }

    func testBusyDayIsSkippedAfterTheWait() throws {
        let dir = tempDir()
        let day = dir.sub("RAID", "2026-10-11")
        try card(day, "A001")
        let held = try XCTUnwrap(FileLock.lock(Locks.day(DayReport.fullPath(day)), timeout: 1))
        defer { held.release() }
        // flock is per open file: another process (or descriptor) cannot take it.
        XCTAssertThrowsError(try DayReportService.regenerate(day, wait: 0.3, pdf: false)) {
            XCTAssertTrue("\($0)".contains("right now"), "\($0)")
        }
    }

    func testReservedCardNamesGetAnUnderscore() {
        XCTAssertEqual(Layout.sanitize("_REPORT"), "_REPORT_")
        XCTAssertEqual(Layout.sanitize("_report"), "_report_")
        XCTAssertEqual(Layout.sanitize("_Proxy"), "_Proxy_")
        XCTAssertEqual(Layout.sanitize("ASCMHL"), "ASCMHL_")
        XCTAssertEqual(Layout.sanitize("A001"), "A001")
    }

    // After a job: the day report on every destination; with --no-day-report none.
    func testJobWritesTheDayReportOnEveryDestination() throws {
        let dir = tempDir()
        let src = dir.sub("card")
        TestFS.writeRandom(src + "/A.bin", size: 1 << 20, seed: 1)
        var config = JobConfig(source: src, destRoots: [dir.sub("d1"), dir.sub("d2")])
        config.analyzeClips = false
        config.recordHistory = false
        config.date = "2026-10-11"
        config.cardName = "A001"
        let job = OffloadJob(config: config, options: options())
        _ = job.run()
        XCTAssertEqual(job.status().phase, .done)
        for d in ["d1", "d2"] {
            let html = dir.path + "/\(d)/2026-10-11/_REPORT/2026-10-11_day_report.html"
            XCTAssertTrue(FileManager.default.fileExists(atPath: html), d)
            XCTAssertTrue(try String(contentsOfFile: html).contains("ALL VERIFIED"))
        }
        XCTAssertEqual(job.writtenDayReports.count, 2)

        config.dayReport = false
        config.cardName = "B001"
        config.destRoots = [dir.sub("d3")]
        let off = OffloadJob(config: config, options: options())
        _ = off.run()
        XCTAssertFalse(FileManager.default.fileExists(atPath: dir.path + "/d3/2026-10-11/_REPORT/2026-10-11_day_report.html"))
    }

    // The PDF (WebKit, A4 landscape) once, on purpose — jobs inside XCTest skip it.
    func testPDF() throws {
        let dir = tempDir()
        let day = dir.sub("RAID", "2026-10-11")
        try card(day, "A001")
        try DayReportService.regenerate(day, pdf: true)
        let pdf = day + "/_REPORT/2026-10-11_day_report.pdf"
        let data = try Data(contentsOf: URL(fileURLWithPath: pdf))
        XCTAssertEqual(String(decoding: data.prefix(5), as: UTF8.self), "%PDF-")
    }
}
