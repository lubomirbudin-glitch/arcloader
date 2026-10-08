import XCTest
@testable import OffloadKit

/// Spec 3.4 (Windows plan 7, tasks 2–3): the clip's reel and proxies that relink — reel name on the timecode track,
/// every audio stream of the source.
final class ReelTests: XCTestCase {
    private func clip(_ name: String) -> Clip {
        Clip(name: name, path: name + ".mov", format: .video, segments: [name + ".mov"], bytes: 1, isCameraProxy: false)
    }

    private func info(reel: String?) -> ClipInfo {
        var i = ClipInfo()
        i.reel = reel
        return i
    }

    // The camera's tag first, then the reel in the clip name, then the card folder (Windows ReelNamesTests).
    func testResolveTagThenClipNameThenCard() {
        XCTAssertEqual(ReelResolver.resolve(clip("A001C003_210101_R1AB"), info: info(reel: "TAPE07"), cardName: "KARTA"), "TAPE07")
        XCTAssertEqual(ReelResolver.resolve(clip("C0001"), info: info(reel: " TAPE07 "), cardName: "KARTA"), "TAPE07")
        XCTAssertEqual(ReelResolver.resolve(clip("A001C003_210101_R1AB"), info: info(reel: "  "), cardName: "KARTA"), "A001")
        XCTAssertEqual(ReelResolver.resolve(clip("a002_c004_0101ab"), info: nil, cardName: "KARTA"), "A002")
        XCTAssertEqual(ReelResolver.resolve(clip("C0001"), info: nil, cardName: "KARTA"), "KARTA")
        XCTAssertEqual(ReelResolver.fromClipName("A001_08151202_C002"), "A001")   // Blackmagic
        XCTAssertEqual(ReelResolver.fromClipName("M003C003_161207_R00H"), "M003")  // ARRI
        XCTAssertNil(ReelResolver.fromClipName("DW0001C021_251020_132307_a1I7H"))
    }

    // MOV keeps the reel on the timecode track, MXF on every stream; other writers use tape_name or reel.
    func testProbeReadsTheReelTag() throws {
        func reel(_ json: String) throws -> String? { try FFProbe.parse(Data(json.utf8)).reel }
        XCTAssertEqual(try reel(#"{"streams":[{"codec_type":"video"},{"codec_type":"data","codec_tag_string":"tmcd","tags":{"reel_name":"A001","timecode":"01:00:00:00"}}],"format":{}}"#), "A001")
        XCTAssertEqual(try reel(#"{"streams":[{"codec_type":"video","tags":{"reel_name":"B002"}},{"codec_type":"audio","tags":{"reel_name":"B002"}}],"format":{}}"#), "B002")
        XCTAssertEqual(try reel(#"{"streams":[],"format":{"tags":{"tape_name":" T7 "}}}"#), "T7")
        XCTAssertEqual(try reel(#"{"streams":[{"codec_type":"video","tags":{"Reel":"R9"}}]}"#), "R9")
        XCTAssertNil(try reel(#"{"streams":[{"codec_type":"video","tags":{"reel_name":" "}}],"format":{}}"#))
        XCTAssertFalse(String(decoding: try JSONEncoder().encode(ClipInfo()), as: UTF8.self).contains("reel"))
    }

    private func run(_ args: [String]) throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y"] + args)
    }

    /// ffprobe's top-level streams (JSON has no stream-group duplicates): (codec_type, codec_name, channels, tags).
    private func streams(_ path: String) throws -> [(type: String, codec: String?, channels: Int, tags: [String: String])] {
        let (_, ffprobe) = try TestMedia.requireFFmpeg()
        let out = try ToolProcess.run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", path])
        let json = try XCTUnwrap(try JSONSerialization.jsonObject(with: Data(out.utf8)) as? [String: Any])
        return (json["streams"] as? [[String: Any]] ?? []).map {
            ($0["codec_type"] as? String ?? "", $0["codec_name"] as? String, $0["channels"] as? Int ?? 0,
             ($0["tags"] as? [String: Any] ?? [:]).compactMapValues { $0 as? String })
        }
    }

    // The proxy relinks: start TC and the reel on the timecode track, every audio stream; no TC = no tmcd, no reel.
    func testProxyHasTheReelOnTheTimecodeTrackAndEveryAudioStream() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try run(["-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=1", "-f", "lavfi", "-i", "sine=f=440:d=1",
                 "-f", "lavfi", "-i", "sine=f=880:d=1", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-timecode", "01:00:00:00",
                 "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", card + "/a.mov"])
        try run(["-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=1", "-f", "lavfi", "-i", "sine=d=1",
                 "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", card + "/b.mov"])
        var s = ProxySettings()
        s.enabled = true
        s.height = 180
        for codec in [ProxyCodec.h264, .proresProxy] {
            s.codec = codec
            let r = ProxyRenderer(settings: s, tools: TestMedia.tools)
            let a = clip("a")
            var info = try r.probe(a, cardPath: card)
            info.reel = ReelResolver.resolve(a, info: info, cardName: "A001")
            XCTAssertEqual(info.reel, "A001")
            let out = card + "/_PROXY/\(codec.rawValue)/a.mov"
            try r.render(a, cardPath: card, info: info, output: out, progress: { _ in }, isCancelled: { false })
            let all = try streams(out)
            let tmcd = try XCTUnwrap(all.first { $0.type == "data" })
            XCTAssertEqual(tmcd.tags["reel_name"], "A001")
            XCTAssertEqual(tmcd.tags["timecode"], "01:00:00:00")
            XCTAssertEqual(all.filter { $0.type == "audio" }.map(\.codec), Array(repeating: codec == .h264 ? "aac" : "pcm_s16le", count: 2))
            XCTAssertEqual(try FFProbe.probe(TestMedia.tools.ffprobe!, out).reel, "A001")

            let b = clip("b")
            var noTC = try r.probe(b, cardPath: card)
            XCTAssertNil(noTC.timecode)
            noTC.reel = "A001"
            let outB = card + "/_PROXY/\(codec.rawValue)/b.mov"
            try r.render(b, cardPath: card, info: noTC, output: outB, progress: { _ in }, isCancelled: { false })
            let bs = try streams(outB)
            XCTAssertFalse(bs.contains { $0.type == "data" || $0.tags["reel_name"] != nil })
            XCTAssertEqual(bs.filter { $0.type == "audio" }.count, 1)
        }
    }

    // The report records every clip's reel — the camera's tag, else the clip name, else the card folder.
    func testReportRecordsTheReel() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        try TestMedia.makeClip(card + "/CLIP/A001C003_210101_R1AB.mov", seconds: 1)
        try TestMedia.makeClip(card + "/CLIP/C0001.mov", seconds: 1)
        try run(["-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=1", "-timecode", "01:00:00:00", "-c:v", "libx264",
                 "-pix_fmt", "yuv420p", "-metadata:s:v:0", "reel_name=TAPE07", card + "/CLIP/take.mov"])
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        var config = JobConfig(source: card, destRoots: [dir.sub("d1")])
        config.date = "2026-10-05"
        config.cardName = "KARTA"
        config.tools = TestMedia.tools
        let job = OffloadJob(config: config, options: o)
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        XCTAssertEqual(report.clips.map { $0.info?.reel }, ["A001", "KARTA", "TAPE07"])
        let json = try String(contentsOfFile: try XCTUnwrap(job.plan).cardPaths[0] + "/_REPORT/offload_report.json", encoding: .utf8)
        XCTAssertTrue(json.contains("\"reel\" : \"TAPE07\""), json)
    }
}
