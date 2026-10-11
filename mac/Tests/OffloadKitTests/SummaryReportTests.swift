import Foundation
import XCTest
@testable import OffloadKit

/// The summary report of selected backups (user's task 2026-10-09, Windows 0.8.0 `SummaryReport`): one HTML + PDF from
/// backups picked in the card history; first and last slate = start of the first and last recorded clip.
final class SummaryReportTests: XCTestCase {
    private func date(_ s: String) -> Date { ISO8601DateFormatter().date(from: s)! }

    private func clip(_ name: String, tc: String?, recorded: String?, seconds: Double = 10, camera: String = "Sony ILME-FX6V",
                      format: ClipFormat = .video, proxy: Bool = false) -> ClipEntry {
        var e = ClipEntry(clip: Clip(name: name, path: "Clip/" + name + ".MP4", format: format, segments: [], bytes: 1, isCameraProxy: proxy))
        var i = ClipInfo()
        i.timecode = tc
        i.recorded = recorded.map(date)
        i.duration = seconds
        i.camera = camera
        i.frameRate = "25"
        e.info = i
        return e
    }

    /// A card folder `<root>/<day>/<name>` with a report (written as an offload writes it) naming `destinations`.
    @discardableResult
    private func card(_ root: String, _ day: String, _ name: String, clips: [ClipEntry], verified: Bool = true,
                      destinations: [String]? = nil, started: Date = Reports.t0) throws -> String {
        let path = root + "/" + day + "/" + name
        var r = Reports.sample(verified: verified)
        r.cardName = name
        r.date = day
        r.started = started
        let dests = destinations ?? [root]
        r.destinations = dests.map { OffloadReport.Destination(root: $0, cardPath: $0 + "/" + day + "/" + name, filesystem: "apfs",
                                                              filesOK: 2, filesFailed: 0) }
        r.files = r.files.map { f in var x = f; x.destinations = dests.map { _ in verified ? "verified" : "written" }; x.errors = dests.map { _ in nil }; return x }
        r.clips = clips
        try IO.mkdirs(path)
        try r.write(cardPath: path, includeHashRecords: true)
        return path
    }

    func testRecordedFromFFprobeAndLenientReading() throws {
        let json = #"{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"avg_frame_rate":"25/1","tags":{"creation_time":"2026-10-06T08:15:30.123456Z"}}],"format":{"duration":"10.0","tags":{}}}"#
        XCTAssertEqual(try FFProbe.parse(Data(json.utf8)).recorded, date("2026-10-06T08:15:30Z"))
        let unset = json.replacingOccurrences(of: "2026-10-06T08:15:30.123456Z", with: "1970-01-01T00:00:00.000000Z")
        XCTAssertNil(try FFProbe.parse(Data(unset.utf8)).recorded, "a camera without its clock set")
        // In a report: written like `started`; a value of another shape is unknown, the report stays readable.
        var r = Reports.sample()
        r.clips = [clip("C0001", tc: "08:15:30:00", recorded: "2026-10-06T08:15:30Z")]
        let text = String(decoding: try r.jsonData(), as: UTF8.self)
        XCTAssertTrue(text.contains(#""recorded" : "2026-10-06T08:15:30Z""#), text)
        let odd = text.replacingOccurrences(of: #""recorded" : "2026-10-06T08:15:30Z""#, with: #""recorded" : 12345"#)
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        let back = try dec.decode(OffloadReport.self, from: Data(odd.utf8))
        XCTAssertEqual(back.clips.count, 1)
        XCTAssertNil(back.clips[0].info?.recorded)
        XCTAssertEqual(back.clips[0].info?.timecode, "08:15:30:00")
    }

    // First and last slate: by the record time when every clip has one, else by day and clip number; camera proxies left out.
    func testSlatesByRecordTimeElseByNumber() throws {
        let dir = tempDir()
        let raid = dir.sub("RAID")
        let a = try card(raid, "2026-10-06", "B001", clips: [
            clip("C0010", tc: "10:00:00:00", recorded: "2026-10-06T07:00:00Z"),
            clip("C0002", tc: "12:00:00:00", recorded: "2026-10-06T09:00:00Z"),
            clip("C0003", tc: "08:00:00:00", recorded: "2026-10-06T06:00:00Z"),
            clip("P0001", tc: "05:00:00:00", recorded: "2026-10-06T05:00:00Z", proxy: true),
        ])
        var m = SummaryReport.load([.folder(a)], localRoots: [])
        XCTAssertEqual(m.cards.first?.first?.clip, "C0003", "the earliest record time")
        XCTAssertEqual(m.cards.first?.last?.clip, "C0002")
        XCTAssertEqual(m.cards.first?.clipCount, 3, "camera proxies are not counted")
        XCTAssertEqual(m.cards.first?.seconds, 30)
        let b = try card(raid, "2026-10-07", "B002", clips: [
            clip("C0010", tc: "10:00:00:00", recorded: nil),
            clip("C0002", tc: "12:00:00:00", recorded: "2026-10-07T09:00:00Z"),
        ])
        m = SummaryReport.load([.folder(a), .folder(b)], localRoots: [])
        XCTAssertEqual(m.days.map(\.date), ["2026-10-06", "2026-10-07"])
        XCTAssertEqual(m.cards[1].first?.clip, "C0002", "a clip without a record time: by clip number (C0002 < C0010)")
        XCTAssertEqual(m.cards[1].last?.clip, "C0010")
        // Over both days: not every clip is timed — by day, then number.
        XCTAssertEqual(m.total.first?.clip, "C0002")
        XCTAssertEqual(m.total.first?.date, "2026-10-06")
        XCTAssertEqual(m.total.last?.clip, "C0010")
        XCTAssertEqual(m.total.last?.date, "2026-10-07")
        let html = SummaryReport.html(m)
        XCTAssertTrue(html.contains("Summary report — 2026-10-06 – 2026-10-07"))
        XCTAssertTrue(html.contains("Shooting days"))
        XCTAssertTrue(html.contains("First slate"))
        XCTAssertTrue(html.contains("Where everything is backed up"))
        XCTAssertTrue(html.contains("2026-10-06 · C0002 · TC"), "the total names the day of a slate when there are several days")
        XCTAssertFalse(html.contains("<img"), "no thumbnails")
    }

    func testAlexaTimeOfDay() throws {
        let dir = tempDir()
        let a = try card(dir.sub("RAID"), "2026-10-06", "A001", clips: [
            clip("A001C001", tc: "13:23:05:17", recorded: nil, camera: "ARRI ALEXA 35"),
        ])
        let m = SummaryReport.load([.folder(a)], localRoots: [])
        XCTAssertEqual(m.cards[0].first?.timeOfDay, true)
        XCTAssertTrue(SummaryReport.html(m).contains("A001C001 · time-of-day TC"))
    }

    // One card on two disks is one card: counted once, both copies listed with their own verification.
    func testSameCardOnTwoDisksCountsOnce() throws {
        let dir = tempDir()
        let r1 = dir.sub("RAID"), r2 = dir.sub("SHUTTLE")
        let clips = [clip("C0001", tc: "10:00:00:00", recorded: nil)]
        let a = try card(r1, "2026-10-06", "B001", clips: clips, destinations: [r1, r2])
        let b = try card(r2, "2026-10-06", "B001", clips: clips, destinations: [r1, r2])
        let m = SummaryReport.load([.folder(a), .folder(b)], localRoots: [])
        XCTAssertEqual(m.cards.count, 1)
        XCTAssertEqual(m.cards[0].copies.map(\.cardPath).sorted(), [a, b].sorted())
        XCTAssertTrue(m.cards[0].copies.allSatisfy { $0.connected && $0.verified == true })
        XCTAssertEqual(m.total.files, 2)
        XCTAssertEqual(m.total.verifiedCards, 1)
        XCTAssertTrue(SummaryReport.html(m).contains("ALL VERIFIED"))
    }

    // A history entry whose disk went away: the local report copy, else the history with a note — never an error.
    func testDisconnectedDiskLocalCopyElseHistory() throws {
        let dir = tempDir()
        let gone = dir.path + "/GONE"   // never created: the disk is not connected
        let local = dir.sub("Offload reporty")
        let started = date("2026-10-06T08:00:00Z")
        try card(local, "2026-10-06", "B001", clips: [clip("C0001", tc: "10:00:00:00", recorded: nil)], destinations: [gone], started: started)
        var e = HistoryEntry(id: "20261006T080015Z-00000001", source: "/Volumes/A001", cardName: "B001", date: "2026-10-06",
                             finished: Reports.t0.addingTimeInterval(15))
        e.success = true; e.verified = true; e.files = 7; e.bytes = 9_000
        e.destinations = [HistoryDestination(root: gone, cardPath: gone + "/2026-10-06/B001")]
        e.generations = [HistoryGeneration(seq: 1, kind: "full", finished: Reports.t0.addingTimeInterval(15), files: 7, bytes: 9_000, verified: true)]
        // The local copy's report names the source "/Volumes/A001" (Reports.sample) and ends with the history's generation.
        var m = SummaryReport.load([.entry(e)], localRoots: [local])
        XCTAssertEqual(m.cards[0].source, .localCopy)
        XCTAssertEqual(m.cards[0].clipCount, 1)
        XCTAssertEqual(m.cards[0].localCopy, local + "/2026-10-06/B001")
        XCTAssertEqual(m.cards[0].copies.first?.connected, false)
        XCTAssertEqual(m.cards[0].copies.first?.basis, "per the report")
        XCTAssertTrue(SummaryReport.html(m).contains("data from the report copy on this computer"))
        // No local copy: the history's numbers, a note, no error.
        m = SummaryReport.load([.entry(e)], localRoots: [dir.path + "/nowhere"])
        XCTAssertEqual(m.cards[0].source, SummarySource.none)
        XCTAssertEqual(m.cards[0].files, 7)
        XCTAssertEqual(m.cards[0].bytes, 9_000)
        XCTAssertFalse(m.cards[0].verified, "never verified without a report")
        let html = SummaryReport.html(m)
        XCTAssertTrue(html.contains("report not available"))
        XCTAssertTrue(html.contains("verified (history)"))
        XCTAssertTrue(html.contains("1 card has no report available"))
    }

    func testInputsFromFoldersDaysAndIds() throws {
        let dir = tempDir()
        let raid = dir.sub("RAID")
        let a = try card(raid, "2026-10-06", "A001", clips: [])
        let b = try card(raid, "2026-10-06", "A002", clips: [])
        let e = HistoryEntry(id: "20261006T080015Z-abcdef01", source: "/Volumes/A001", cardName: "A001", date: "2026-10-06", finished: Reports.t0)
        let inputs = try SummaryReport.inputs([raid + "/2026-10-06", "20261006T080015Z-ABCDEF01"], history: [e])
        XCTAssertEqual(inputs.count, 3)
        if case .folder(let p) = inputs[0] { XCTAssertEqual(p, DayReport.fullPath(a)) } else { XCTFail() }
        if case .folder(let p) = inputs[1] { XCTAssertEqual(p, DayReport.fullPath(b)) } else { XCTFail() }
        if case .entry(let x) = inputs[2] { XCTAssertEqual(x.id, e.id) } else { XCTFail() }
        XCTAssertThrowsError(try SummaryReport.inputs(["no-such-id"], history: [e])) { XCTAssertTrue("\($0)".contains("neither a card folder")) }
        XCTAssertThrowsError(try SummaryReport.inputs([raid], history: [])) { XCTAssertTrue("\($0)".contains("is not a card folder")) }
    }

    // Saved into <local>/summary, never over a file (_2), and into a picked folder only when it is no backup or card.
    func testSaveNeverOverwritesAndRefusesBackupFolders() throws {
        let dir = tempDir()
        let raid = dir.sub("RAID")
        let a = try card(raid, "2026-10-06", "A001", clips: [clip("C0001", tc: "10:00:00:00", recorded: nil)])
        let m = SummaryReport.load([.folder(a)], localRoots: [])
        let local = dir.sub("local")
        let now = date("2026-10-11T10:20:30Z")
        let first = try SummaryReport.save(m, localRoot: local, extraFolder: nil, now: now, pdf: false)
        XCTAssertEqual((first.html as NSString).lastPathComponent, SummaryReport.baseName(now) + ".html")
        XCTAssertTrue(first.html.hasPrefix(local + "/summary/"))
        XCTAssertTrue(SummaryReport.baseName(now).hasSuffix("_summary"))
        let firstBytes = try Data(contentsOf: URL(fileURLWithPath: first.html))
        let second = try SummaryReport.save(m, localRoot: local, extraFolder: nil, now: now, pdf: false)
        XCTAssertEqual((second.html as NSString).lastPathComponent, SummaryReport.baseName(now) + "_2.html")
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: first.html)), firstBytes, "never over a file")

        SummaryReport.volumeProbe = { _ in SummaryReport.TargetVolume(root: "/", removable: false, readOnly: false) }
        defer { SummaryReport.volumeProbe = nil }
        let before = TestFS.files(in: raid)
        for bad in [a, a + "/_REPORT", raid + "/2026-10-06/A001/ascmhl"] {
            let r = try SummaryReport.save(m, localRoot: local, extraFolder: bad, now: now, pdf: false)
            XCTAssertNotNil(r.extraError, bad)
            XCTAssertNil(r.extraHTML)
        }
        XCTAssertEqual(TestFS.files(in: raid), before, "nothing written into the backup")
        let source = dir.sub("CARD", "sub")
        XCTAssertNotNil(try SummaryReport.save(m, localRoot: local, extraFolder: source, now: now, cards: [dir.path + "/CARD"], pdf: false).extraError)
        let good = dir.sub("Summaries")
        let ok = try SummaryReport.save(m, localRoot: local, extraFolder: good, now: now, pdf: false)
        XCTAssertNil(ok.extraError)
        XCTAssertEqual(ok.extraHTML, good + "/" + SummaryReport.baseName(now) + ".html")
        SummaryReport.volumeProbe = { _ in SummaryReport.TargetVolume(root: "/Volumes/X", removable: true, readOnly: false) }
        XCTAssertTrue(try SummaryReport.save(m, localRoot: local, extraFolder: good, now: now, pdf: false).extraError?.contains("removable") == true)
        SummaryReport.volumeProbe = { _ in SummaryReport.TargetVolume(root: "/Volumes/X", removable: false, readOnly: true) }
        XCTAssertTrue(try SummaryReport.save(m, localRoot: local, extraFolder: good, now: now, pdf: false).extraError?.contains("read-only") == true)
    }

    func testPDF() throws {
        let dir = tempDir()
        let a = try card(dir.sub("RAID"), "2026-10-06", "A001", clips: [clip("C0001", tc: "10:00:00:00", recorded: nil)])
        let saved = try SummaryReport.save(SummaryReport.load([.folder(a)], localRoots: []), localRoot: dir.sub("local"), extraFolder: nil)
        let pdf = try XCTUnwrap(saved.pdf)
        XCTAssertEqual(String(decoding: try Data(contentsOf: URL(fileURLWithPath: pdf)).prefix(5), as: UTF8.self), "%PDF-")
        XCTAssertTrue(try String(contentsOfFile: saved.html).contains("href=\"\((pdf as NSString).lastPathComponent)\""))
    }
}
