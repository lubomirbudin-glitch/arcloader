import Foundation
import XCTest
@testable import OffloadKit

/// The capture rate from the metadata of a clip's first frame (`clips[].info.captureFrameRate`, Windows 0.8.0). The bug:
/// an ALEXA 35 card shot at 35 fps showed 24 fps in the report (the file's playback rate).
final class CaptureFrameRateTests: XCTestCase {
    private func info(_ playback: String?, _ capture: String?) -> ClipInfo {
        var i = ClipInfo()
        i.frameRate = playback
        i.captureFrameRate = capture
        return i
    }

    func testNormalizeRate() {
        XCTAssertEqual(ClipInfo.normalizeRate("35/1"), "35")
        XCTAssertEqual(ClipInfo.normalizeRate("24000/1001"), "24000/1001")
        XCTAssertEqual(ClipInfo.normalizeRate("23.976"), "24000/1001")
        XCTAssertEqual(ClipInfo.normalizeRate("35000"), "35", "ARRI thousandths")
        XCTAssertEqual(ClipInfo.normalizeRate("23976"), "24000/1001")
        XCTAssertEqual(ClipInfo.normalizeRate("120"), "120")
        XCTAssertNil(ClipInfo.normalizeRate("0/1"))
        XCTAssertNil(ClipInfo.normalizeRate("abc"))
        XCTAssertNil(ClipInfo.normalizeRate(nil))
    }

    func testLabelsAndSummary() {
        XCTAssertEqual(info("24", "35").fpsSummary, "35 fps · playback 24")
        XCTAssertTrue(info("24", "35").captureDiffers)
        XCTAssertEqual(info("24000/1001", "120000/1001").fpsSummary, "119.880 fps · playback 23.976")
        XCTAssertEqual(info("25", "25").fpsSummary, "25", "the same rate is shown once")
        XCTAssertFalse(info("25", "25").captureDiffers)
        XCTAssertEqual(info("25", nil).fpsSummary, "25", "unknown capture rate = playback")
        XCTAssertEqual(info(nil, "48").fpsSummary, "48 fps")
        XCTAssertEqual(info(nil, nil).fpsSummary, "")
        XCTAssertEqual(ClipInfo.label("24000/1001"), "23.976")
        XCTAssertEqual(ClipInfo.label("25/1"), "25")
        XCTAssertEqual(ClipInfo.label(nil), "")
    }

    // JSON: the key only when known (also when it equals frameRate); a report without it reads as before.
    func testJSONKey() throws {
        var e = ClipEntry(clip: Clip(name: "A", path: "A.mxf", format: .arriraw, segments: ["A.mxf"], bytes: 1, isCameraProxy: false))
        e.info = info("24", "35")
        var r = Reports.sample()
        r.clips = [e]
        let json = String(decoding: try r.jsonData(), as: UTF8.self)
        XCTAssertTrue(json.contains(#""captureFrameRate" : "35""#), json)
        let back = try JSONDecoder.iso.decode(OffloadReport.self, from: try r.jsonData())
        XCTAssertEqual(back.clips.first?.info?.captureFrameRate, "35")
        r.clips[0].info = info("24", nil)
        XCTAssertFalse(String(decoding: try r.jsonData(), as: UTF8.self).contains("captureFrameRate"))
    }

    // ARRIRAW: the first frame's Sensor State, not the file's sampleRate; duration stays from the playback rate.
    func testArriSensorSampleRate() {
        func meta(_ sensor: Any) -> [String: Any] {
            [
                "clipBasedMetadataSets": [["metadataSetPayload": [
                    "nativePictureEssenceDescriptor": ["file": ["sampleRate": "24/1"]], "clipDuration": 1,
                ] as [String: Any]]],
                "descriptiveMetadataSets": [["metadataSetPayload": ["cameraModel": "ARRI ALEXA 35"]]],
                "frameBasedMetadata": ["frames": [
                    ["timecode": "13:23:05:17", "frameBasedMetadataSets": [["Sensor State": ["sensorSampleRate": sensor]]]] as [String: Any],
                    ["timecode": "13:23:05:18", "frameBasedMetadataSets": [["Sensor State": ["sensorSampleRate": "99/1"]]]],
                ]],
            ]
        }
        let c = ARRIRAWBackend.parse(meta("35/1"))
        XCTAssertEqual(c.frameRate, "24")
        XCTAssertEqual(c.captureFrameRate, "35")
        XCTAssertEqual(c.fpsSummary, "35 fps · playback 24")
        XCTAssertNil(c.frames, "clipDuration 1 of the one-frame export is not the length")
        XCTAssertEqual(c.timecode, "13:23:05:17")
        XCTAssertEqual(ARRIRAWBackend.parse(meta(35000)).captureFrameRate, "35", "thousandths")
        XCTAssertEqual(ARRIRAWBackend.parse(meta(23976)).captureFrameRate, "24000/1001")
        var noSensor = meta("35/1")
        noSensor["frameBasedMetadata"] = ["frames": [["timecode": "01:00:00:00"]]]
        XCTAssertNil(ARRIRAWBackend.parse(noSensor).captureFrameRate)
    }

    // Real art-cmd on mac/TestMedia/ARRIRAW (both samples shot at 24 fps): the fast one-frame probe gives the capture rate.
    func testRealArrirawSamples() throws {
        guard TestMedia.tools.artCmd != nil else { throw XCTSkip("art-cmd not installed") }
        let folder = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().appendingPathComponent("TestMedia/ARRIRAW").path
        let samples = ((try? FileManager.default.contentsOfDirectory(atPath: folder)) ?? []).filter { $0.lowercased().hasSuffix(".mxf") }
        guard !samples.isEmpty else { throw XCTSkip("no ARRIRAW samples") }
        for file in samples {
            let c = try ARRIRAWBackend().probe(folder + "/" + file, tools: TestMedia.tools)
            XCTAssertEqual(c.captureFrameRate, "24", file)
            XCTAssertEqual(c.frameRate, "24", file)
            XCTAssertNotNil(c.duration, file)
        }
    }

    func testBrawSensorRate() throws {
        let line = #"{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":24.000000,"frames":240,"timecode":"01:02:03:04","camera":"URSA","colorScienceGen":5,"sensorRate":48.000000}"#
        let c = BRAWBackend.clipInfo(try BRAWBackend.parseInfo(line))
        XCTAssertEqual(c.captureFrameRate, "48")
        XCTAssertEqual(c.frameRate, "24")
        XCTAssertEqual(c.duration, 10, "frames ÷ playback fps")
        let old = #"{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"","camera":""}"#
        XCTAssertNil(BRAWBackend.clipInfo(try BRAWBackend.parseInfo(old)).captureFrameRate, "an older braw-decode")
        let zero = line.replacingOccurrences(of: "48.000000", with: "0.000000")
        XCTAssertNil(BRAWBackend.clipInfo(try BRAWBackend.parseInfo(zero)).captureFrameRate)
        let one = line.replacingOccurrences(of: "48.000000", with: "1.000000")
        XCTAssertNil(BRAWBackend.clipInfo(try BRAWBackend.parseInfo(one)).captureFrameRate, "profile.braw reports 1.0")
    }

    // RED: record fps = capture, project fps / timebase = playback; without a project rate the record rate is both.
    func testRedRecordAndProjectFps() {
        let both = R3DBackend.parseMeta("Frame Width: 8192\nFrame Height: 4320\nRecord FPS: 48.000\nProject FPS: 24.000\nTotal Frames: 480\nMonitor FPS: 60\n")
        XCTAssertEqual(both.captureFrameRate, "48")
        XCTAssertEqual(both.frameRate, "24")
        XCTAssertEqual(both.duration, 20)
        let recordOnly = R3DBackend.parseMeta("Record FPS: 23.976\nTotal Frames: 24\nMonitor FPS: 60\n")
        XCTAssertEqual(recordOnly.frameRate, "24000/1001", "never a monitor rate that merely contains fps")
        XCTAssertEqual(recordOnly.captureFrameRate, "24000/1001")
        let generic = R3DBackend.parseMeta("FPS: 25\n")
        XCTAssertEqual(generic.frameRate, "25")
        XCTAssertNil(generic.captureFrameRate)
    }

    func testSonyCaptureFps() throws {
        let dir = tempDir()
        let clip = dir.file("XDROOT", "Clip", "C0001.MP4")
        FileManager.default.createFile(atPath: clip, contents: Data())
        let xml = dir.path + "/XDROOT/Clip/C0001M01.XML"
        func write(_ attrs: String) throws {
            try """
            <?xml version="1.0" encoding="UTF-8"?>
            <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.20">
            <VideoFormat><VideoFrame videoCodec="AVC_3840_2160_HP@L51" \(attrs)/></VideoFormat>
            </NonRealTimeMeta>
            """.write(toFile: xml, atomically: true, encoding: .utf8)
        }
        try write(#"captureFps="119.88p" formatFps="23.98p""#)
        XCTAssertEqual(SonyXML.captureFrameRate(clip), "120000/1001")
        try write(#"captureFps="59.94i" formatFps="59.94i""#)
        XCTAssertEqual(SonyXML.captureFrameRate(clip), "30000/1001", "fields, not frames")
        try write(#"formatFps="25p""#)
        XCTAssertNil(SonyXML.captureFrameRate(clip))
        try FileManager.default.removeItem(atPath: xml)
        XCTAssertNil(SonyXML.captureFrameRate(clip))
    }

    // HTML: "35 fps" with "playback 24" under it and the explanation; TXT: the "Frame rate:" line (Windows wording).
    func testReportShowsCaptureAndPlayback() {
        var r = Reports.sample()
        func clip(_ name: String, _ playback: String?, _ capture: String?, proxy: Bool = false) -> ClipEntry {
            var e = ClipEntry(clip: Clip(name: name, path: name + ".mxf", format: .arriraw, segments: [name + ".mxf"], bytes: 1,
                                         isCameraProxy: proxy))
            e.info = info(playback, capture)
            return e
        }
        r.clips = [clip("A", "24", "35"), clip("B", "24", "35"), clip("C", "25", nil), clip("P", "50", nil, proxy: true)]
        let html = HTMLReport.render(r)
        XCTAssertTrue(html.contains(#"35 fps<div class="note">playback 24</div>"#), html)
        XCTAssertTrue(html.contains("FPS: the capture rate from the metadata of the first frame, followed by the file's playback rate when it differs. Duration = frame count ÷ playback fps (the length in the edit)."))
        XCTAssertTrue(r.summary.contains("Frame rate:  35 fps capture, 24 playback (2 clips); 25 fps (1 clip)\n"), r.summary)
        r.clips = [clip("C", "25", "25")]
        XCTAssertFalse(HTMLReport.render(r).contains("playback"))
        XCTAssertTrue(r.summary.contains("Frame rate:  25 fps (1 clip)\n"))
        r.clips = []
        XCTAssertFalse(r.summary.contains("Frame rate:"))
    }
}

private extension JSONDecoder {
    static var iso: JSONDecoder {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }
}
