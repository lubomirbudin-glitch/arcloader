import Foundation
import XCTest
@testable import OffloadKit

/// "Today" and "Card history" from history.json, the camera label of a card and the start estimate (Windows
/// `HistoryView`, `CameraLabel`, `CameraHints`, `RateMemory`; same English texts).
final class HistoryViewTests: XCTestCase {
    private var cal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "Europe/Prague")!
        return c
    }()

    private func at(_ s: String) -> Date {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = cal.timeZone
        f.dateFormat = "yyyy-MM-dd HH:mm"
        return f.date(from: s)!
    }

    private func entry(_ name: String, _ when: String, gb: Double = 1, success: Bool = true, verified: Bool = true,
                       unfinished: Bool = false, failed: Int? = nil, copies: Int = 2) -> HistoryEntry {
        var e = HistoryEntry(id: name + when, source: "/Volumes/\(name)", cardName: name, date: String(when.prefix(10)), finished: at(when))
        e.success = success; e.verified = verified; e.unfinished = unfinished; e.failedFiles = failed
        e.bytes = Int64(gb * 1e9); e.files = 24
        e.destinations = (0..<copies).map { HistoryDestination(root: "/Volumes/D\($0 + 1)/Offload", cardPath: "/Volumes/D\($0 + 1)/Offload/x/\(name)") }
        return e
    }

    func testRowTexts() {
        let ok = HistoryView.row(entry("A034_ARRI", "2026-10-11 18:12", gb: 256), calendar: cal)
        XCTAssertEqual(ok.size, "256 GB")
        XCTAssertEqual(ok.time, "18:12")
        XCTAssertEqual(ok.status, "✓ 2 copies verified · safe to format")
        XCTAssertEqual(ok.tone, .ok)
        XCTAssertEqual(ok.files, "24 files")
        XCTAssertEqual(ok.disks, "D1, D2")
        XCTAssertEqual(HistoryView.row(entry("A", "2026-10-11 10:00", copies: 1), calendar: cal).status, "✓ 1 copy verified · safe to format")
        let unverified = HistoryView.row(entry("A", "2026-10-11 10:00", verified: false), calendar: cal)
        XCTAssertEqual(unverified.status, "! not verified · do not format")
        XCTAssertEqual(unverified.tone, .warning)
        let failed = HistoryView.row(entry("A", "2026-10-11 10:00", success: false, failed: 3), calendar: cal)
        XCTAssertEqual(failed.status, "✕ with errors (3 files)")
        XCTAssertEqual(failed.tone, .error)
        let unfinished = HistoryView.row(entry("A", "2026-10-11 10:00", success: false, verified: false, unfinished: true), calendar: cal)
        XCTAssertEqual(unfinished.status, "! unfinished")
    }

    func testSummary() {
        XCTAssertEqual(HistoryView.summary([entry("A", "2026-10-11 10:00", gb: 256), entry("B", "2026-10-11 11:00", gb: 118),
                                            entry("C", "2026-10-11 12:00", gb: 38)]), "3 cards · 412 GB · all verified")
        XCTAssertEqual(HistoryView.summary([entry("A", "2026-10-11 10:00", gb: 2, verified: false)]), "1 card · 2.00 GB · 1 not verified")
        XCTAssertEqual(HistoryView.summary([entry("A", "2026-10-11 10:00", gb: 9, success: false, failed: 1),
                                            entry("B", "2026-10-11 10:00", gb: 9, unfinished: true)]),
                       "2 cards · 18.0 GB · 1 unfinished · 1 card with errors")
    }

    func testDaysTodayYesterdayAndDates() {
        let entries = [entry("OLD", "2026-10-04 09:00"), entry("Y", "2026-10-10 23:59"), entry("T1", "2026-10-11 00:01"),
                       entry("T2", "2026-10-11 18:12")]
        let days = HistoryView.days(entries, now: at("2026-10-11 20:00"), calendar: cal)
        XCTAssertEqual(days.map(\.title), ["Today", "Yesterday", "2026-10-04"])
        XCTAssertEqual(days[0].rows.map(\.cardName), ["T2", "T1"], "newest first")
        XCTAssertEqual(HistoryView.today(entries, now: at("2026-10-11 20:00"), calendar: cal)?.rows.count, 2)
        XCTAssertNil(HistoryView.today(entries, now: at("2026-10-13 08:00"), calendar: cal), "no card today: the section hides")
        // At most 300 cards in the tab.
        let many = (0..<400).map { entry("C\($0)", "2026-10-11 10:00") }
        XCTAssertEqual(HistoryView.days(many, now: at("2026-10-11 20:00"), calendar: cal).flatMap(\.rows).count, HistoryView.maxEntries)
    }

    func testDiskNameNeverTouchesTheDisk() {
        XCTAssertEqual(HistoryView.diskName("/Volumes/RAID_A"), "RAID_A")
        XCTAssertEqual(HistoryView.diskName("/Volumes/RAID_A/Offload"), "RAID_A")
        XCTAssertEqual(HistoryView.diskName("/Users/dit/Backup"), "Backup")
    }

    func testFormatBytes3() {
        XCTAssertEqual(formatBytes3(1_700_000_000), "1.70 GB")
        XCTAssertEqual(formatBytes3(14_200_000_000), "14.2 GB")
        XCTAssertEqual(formatBytes3(412_000_000_000), "412 GB")
        XCTAssertEqual(formatBytes3(999_600_000), "1.00 GB")
        XCTAssertEqual(formatBytes3(512), "512 B")
    }

    func testCameraLabel() {
        XCTAssertEqual(CameraLabel.model("ARRI ALEXA 35"), "ALEXA 35")
        XCTAssertEqual(CameraLabel.model("Sony ILME-FX6V"), "ILME-FX6V")
        XCTAssertEqual(CameraLabel.model("RED"), "RED")
        XCTAssertEqual(CameraLabel.shortCurve("ARRI LogC4"), "LogC4")
        XCTAssertEqual(CameraLabel.shortCurve("Blackmagic Film Generation 5"), "Film Gen 5")
        XCTAssertEqual(CameraLabel.shortCurve("Canon Log 3"), "C-Log3")
        XCTAssertEqual(CameraLabel(camera: "ALEXA 35", curve: "LogC4").text, "ALEXA 35 · LogC4")
        XCTAssertNil(CameraLabel(camera: " ", curve: nil).text)
    }

    func testCameraHints() {
        var insight = CardInsight(source: "/Volumes/CARD")
        insight.clips = [Clip(name: "P", path: "SUB/P.mp4", format: .video, segments: [], bytes: 1, isCameraProxy: true),
                         Clip(name: "A", path: "A.braw", format: .braw, segments: [], bytes: 1, isCameraProxy: false)]
        XCTAssertEqual(CameraHints.probe(insight, tools: ExternalTools(), catalog: TestMedia.luts)?.text, "Blackmagic RAW",
                       "RAW is named by its format (its decoder is too slow at insertion)")
        insight.clips = [Clip(name: "A", path: "A001.mxf", format: .video, segments: [], bytes: 1, isCameraProxy: false)]
        var asked: String?
        let label = CameraHints.probe(insight, tools: ExternalTools(), catalog: TestMedia.luts, probe: { path in
            asked = path
            var i = ClipInfo()
            i.camera = "ARRI ALEXA 35"
            return i
        })
        XCTAssertEqual(asked, "/Volumes/CARD/A001.mxf")
        XCTAssertEqual(label?.camera, "ALEXA 35")
        XCTAssertNotNil(label?.text)
        XCTAssertNil(CameraHints.probe(insight, tools: ExternalTools(), catalog: TestMedia.luts,
                                       probe: { _ in throw ToolError(tool: "ffprobe", status: 1, message: "x") }),
                     "a clip ffprobe cannot read leaves the label empty")
        insight.error = "unreadable"
        XCTAssertNil(CameraHints.probe(insight, tools: ExternalTools(), catalog: TestMedia.luts))
    }

    private func report(bytes: Int64, copy: Double, verify: Double, clipSeconds: Double, verified: Bool = true) -> OffloadReport {
        let t0 = Date(timeIntervalSince1970: 1_790_000_000)
        var r = OffloadReport(cardName: "A", date: "2026-10-11", source: "/Volumes/A", sourceFilesystem: "exfat", started: t0,
                              copyFinished: t0.addingTimeInterval(copy), finished: t0.addingTimeInterval(copy + verify),
                              verified: verified, totalFiles: 1, totalBytes: bytes, destinations: [], files: [], skipped: [])
        var c = ClipEntry(clip: Clip(name: "C", path: "C.mov", format: .video, segments: ["C.mov"], bytes: bytes, isCameraProxy: false))
        var i = ClipInfo()
        i.duration = clipSeconds
        c.info = i
        r.clips = [c]
        return r
    }

    func testRateMemoryEstimate() {
        let m = RateMemory()
        var proxy = ProxySettings()
        proxy.enabled = true
        let clips = [Clip(name: "C", path: "C.mov", format: .video, segments: [], bytes: 625_000_000, isCameraProxy: false)]  // 100 s at 50 Mb/s
        XCTAssertNil(m.estimate(bytes: 1_000_000_000, clips: clips, verify: true, analyze: true, proxy: proxy), "nothing measured yet")
        m.learn(report(bytes: 100_000_000, copy: 1, verify: 1, clipSeconds: 10), analyze: true, proxy: proxy, stage2: 5)
        XCTAssertNil(m.copyRate, "too small a job to measure a speed")
        m.learn(report(bytes: 1_000_000_000, copy: 2, verify: 4, clipSeconds: 100), analyze: true, proxy: proxy, stage2: 50, cancelled: true)
        XCTAssertNil(m.copyRate, "a cancelled job teaches nothing")
        m.learn(report(bytes: 1_000_000_000, copy: 2, verify: 4, clipSeconds: 100), analyze: true, proxy: proxy, stage2: 50)
        let e = m.estimate(bytes: 1_000_000_000, clips: clips, verify: true, analyze: true, proxy: proxy)
        XCTAssertEqual(e ?? 0, 2 + 4 + 50, accuracy: 0.01)
        XCTAssertEqual(RateMemory.startText(e, proxies: true), "estimate ~0:56 incl. proxies")
        XCTAssertEqual(RateMemory.startText(125, proxies: false), "estimate ~2:05")
        XCTAssertNil(RateMemory.startText(nil, proxies: true))
        var other = proxy
        other.height = 720
        XCTAssertNil(m.estimate(bytes: 1_000_000_000, clips: clips, verify: true, analyze: true, proxy: other),
                     "another proxy setting has no measured cost yet")
        XCTAssertEqual(m.estimate(bytes: 1_000_000_000, clips: [], verify: false, analyze: true, proxy: other) ?? 0, 2, accuracy: 0.01)
    }

    func testStage2ElapsedWithoutPauses() {
        let t0 = Date(timeIntervalSince1970: 1_790_000_000)
        let p = JobProgress(now: t0)
        var s = JobStatus()
        s.phase = .copying
        p.update(s, now: t0.addingTimeInterval(1))
        XCTAssertNil(p.stage2Elapsed)
        s.phase = .proxies
        s.pausedTotal = 3
        p.update(s, now: t0.addingTimeInterval(10))
        s.pausedTotal = 8
        s.phase = .done
        p.update(s, now: t0.addingTimeInterval(40))
        XCTAssertEqual(p.stage2Elapsed ?? 0, 25, accuracy: 0.001, "30 s of stage 2 minus 5 s paused in it")
        let noStage2 = JobProgress(now: t0)
        s.pausedTotal = 0
        noStage2.update(s, now: t0.addingTimeInterval(5))
        XCTAssertNil(noStage2.stage2Elapsed)
    }
}
