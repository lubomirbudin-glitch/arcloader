import XCTest
@testable import OffloadKit

/// Task 54 (Windows 0.3.0 / 0.5.1): proxies for relinking — the reel in the timecode track, every audio stream, proxies in
/// folders as on the card (`_2` only within one folder), an append into an older flat backup stays flat, the HTML link.
final class ProxyRelinkTests: XCTestCase {
    private func clip(_ path: String, format: ClipFormat = .video) -> Clip {
        let name = format == .arriraw ? (path as NSString).lastPathComponent
            : ((path as NSString).lastPathComponent as NSString).deletingPathExtension
        return Clip(name: name, path: path, format: format, segments: [path], bytes: 1, isCameraProxy: false)
    }

    // MARK: ProxyPaths

    func testFolderOfAClip() {
        XCTAssertEqual(ProxyPaths.folder(clip("C0001.MP4")), "")
        XCTAssertEqual(ProxyPaths.folder(clip("idle/1.mp4")), "idle")
        XCTAssertEqual(ProxyPaths.folder(clip("PRIVATE/M4ROOT/CLIP/C0001.MP4")), "PRIVATE/M4ROOT/CLIP")
        // An .ari sequence is a folder: its proxy sits beside it, named after it.
        XCTAssertEqual(ProxyPaths.folder(clip("A001R1AB/A001C001_220101_R1AB", format: .arriraw)), "A001R1AB")
        // R3D: the clip's folder is the .RDC.
        XCTAssertEqual(ProxyPaths.folder(clip("A001_C002_0101AB.RDC/A001_C002_0101AB_001.R3D", format: .r3d)), "A001_C002_0101AB.RDC")
    }

    func testMirrorKeepsNamesAcrossFoldersAndSuffixesOnlyWithinOne() {
        var used = Set<String>()
        XCTAssertEqual(ProxyPaths.pick(clip("idle/1.mp4"), tree: .mirror, used: &used).path, "_PROXY/idle/1.mov")
        XCTAssertEqual(ProxyPaths.pick(clip("listening/1.mp4"), tree: .mirror, used: &used).path, "_PROXY/listening/1.mov")
        let clash = ProxyPaths.pick(clip("idle/1.mxf"), tree: .mirror, used: &used)
        XCTAssertEqual(clash.path, "_PROXY/idle/1_2.mov")
        XCTAssertEqual(clash.name, "1_2")
        XCTAssertEqual(ProxyPaths.pick(clip("C0001.MP4"), tree: .mirror, used: &used).path, "_PROXY/C0001.mov")
        // Case does not make another name (APFS, exFAT).
        XCTAssertEqual(ProxyPaths.pick(clip("IDLE/1.mov"), tree: .mirror, used: &used).path, "_PROXY/IDLE/1_3.mov")
    }

    func testFlatPutsEveryProxyAtTheTop() {
        var used: Set<String> = ["c0001"]   // already in an older flat backup
        XCTAssertEqual(ProxyPaths.pick(clip("idle/1.mp4"), tree: .flat, used: &used).path, "_PROXY/1.mov")
        XCTAssertEqual(ProxyPaths.pick(clip("listening/1.mp4"), tree: .flat, used: &used).path, "_PROXY/1_2.mov")
        XCTAssertEqual(ProxyPaths.pick(clip("CLIP/C0001.MP4"), tree: .flat, used: &used).path, "_PROXY/C0001_2.mov")
    }

    func testDetectLayoutFromEarlierReports() {
        func entry(_ path: String, proxy: String?) -> ClipEntry {
            var e = ClipEntry(clip: clip(path))
            e.proxyPath = proxy
            return e
        }
        // The Mac before task 54 (and Windows 0.5.0): a clip from a subfolder with its proxy at the top.
        XCTAssertEqual(ProxyPaths.detect([entry("CLIP/C0001.MP4", proxy: "_PROXY/C0001.mov")]), .flat)
        XCTAssertEqual(ProxyPaths.detect([entry("CLIP/C0001.MP4", proxy: "_PROXY/CLIP/C0001.mov")]), .mirror)
        XCTAssertEqual(ProxyPaths.detect([entry("CLIP/C0001.MP4", proxy: nil)]), .mirror, "no proxies: as on the card")
        XCTAssertEqual(ProxyPaths.detect([entry("C0001.MP4", proxy: "_PROXY/C0001.mov")]), .mirror, "root clips: both agree")
        XCTAssertEqual(ProxyPaths.detect([]), .mirror)
        XCTAssertEqual(ProxyPaths.detect([entry("CLIP/C0001.MP4", proxy: "_PROXY/C0001.mov"),
                                          entry("CLIP/C0002.MP4", proxy: "_PROXY/CLIP/C0002.mov")]), .mirror)
    }

    // MARK: Reel, ffprobe, audio

    func testReelResolverTagThenNameThenCard() {
        var info = ClipInfo()
        info.reel = " TAPE07 "
        XCTAssertEqual(ReelResolver.resolve(clip("A001C003_220101_R1AB.mov"), info: info, cardName: "CARD"), "TAPE07")
        XCTAssertEqual(ReelResolver.resolve(clip("A001C003_220101_R1AB.mov"), info: ClipInfo(), cardName: "CARD"), "A001")
        XCTAssertEqual(ReelResolver.resolve(clip("C0001.MP4"), info: nil, cardName: "CARD"), "CARD")
    }

    func testFFProbeReadsReelAudioStreamsAndVideoRate() throws {
        let json = """
        {"streams":[
          {"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"avg_frame_rate":"25/1","bit_rate":"5200000"},
          {"codec_type":"audio","channels":2,"bit_rate":"128000"},
          {"codec_type":"audio","channels":1,"bit_rate":"64000"},
          {"codec_type":"data","tags":{"timecode":"01:00:00:00","Reel_Name":"A007"}}],
         "format":{"duration":"2.0","bit_rate":"9000000","tags":{}}}
        """
        let info = try FFProbe.parse(Data(json.utf8))
        XCTAssertEqual(info.reel, "A007")
        XCTAssertEqual(info.audioStreams, [2, 1])
        XCTAssertEqual(info.audioChannels, 3)
        XCTAssertEqual(info.videoBitrate, 5_200_000)
        // MTS: no rate per stream — the container's minus the audio streams'.
        let mts = """
        {"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080},
          {"codec_type":"audio","channels":2,"bit_rate":"256000"}],
         "format":{"bit_rate":"17256000","tags":{"tape_name":"  "}}}
        """
        let m = try FFProbe.parse(Data(mts.utf8))
        XCTAssertEqual(m.videoBitrate, 17_000_000)
        XCTAssertNil(m.reel, "an empty tag is no reel")
        XCTAssertNil(try FFProbe.parse(Data(#"{"streams":[{"codec_type":"video","codec_name":"h264"}],"format":{}}"#.utf8)).videoBitrate)
    }

    func testReelIsInTheReportOnlyWhenKnown() throws {
        var info = ClipInfo()
        XCTAssertFalse(String(decoding: try JSONEncoder().encode(info), as: UTF8.self).contains("reel"))
        info.reel = "A001"
        info.audioStreams = [2, 2]
        info.videoBitrate = 1
        let json = String(decoding: try JSONEncoder().encode(info), as: UTF8.self)
        XCTAssertTrue(json.contains("\"reel\":\"A001\""))
        XCTAssertFalse(json.contains("audioStreams"), "known only right after the probe")
        XCTAssertFalse(json.contains("videoBitrate"))
        XCTAssertEqual(try JSONDecoder().decode(ClipInfo.self, from: Data(json.utf8)).reel, "A001")
    }

    func testAudioArgsPerStream() {
        XCTAssertEqual(ProxyCodec.h264.audioArgs([2, 6, 1, 16]), ["-c:a", "aac", "-b:a:0", "128k", "-b:a:1", "384k", "-b:a:2", "128k", "-b:a:3", "512k"])
        XCTAssertEqual(ProxyCodec.hevc.audioArgs([]), ["-c:a", "aac", "-b:a", "192k"])
        XCTAssertEqual(ProxyCodec.proresLT.audioArgs([2, 2]), ["-c:a", "pcm_s16le"])
    }

    func testSpaceCountsEveryAudioStream() {
        var p = ProxySettings()
        p.enabled = true
        var one = ClipInfo(), two = ClipInfo()
        one.duration = 100
        one.audioChannels = 4
        two.duration = 100
        two.audioChannels = 4
        two.audioStreams = [2, 2]
        let c = clip("a.mov")
        // One stream of 4 channels: AAC 256k. Two streams of 8 channels: 2 × 512k — 768 kb/s more for 100 s.
        let a = SpaceEstimator.proxyBytes([c], p, renderable: { _ in true }, info: { _ in one })
        two.audioStreams = [8, 8]
        let b = SpaceEstimator.proxyBytes([c], p, renderable: { _ in true }, info: { _ in two })
        XCTAssertEqual(Double(b - a), 100 * 768_000 / 8, accuracy: 2)
        // Mono streams: each at least 128k.
        two.audioStreams = [1, 1, 1]
        let m = SpaceEstimator.proxyBytes([c], p, renderable: { _ in true }, info: { _ in two })
        XCTAssertEqual(Double(m - a), 100 * 128_000 / 8, accuracy: 2)
    }

    // MARK: Proxy folder, HTML

    func testExtraFolderKeepsTheClipFolder() {
        let dir = tempDir()
        let src = dir.path + "/d1/2026-10-05/A001/_PROXY/idle/1.mov"
        TestFS.writeRandom(src, size: 500, seed: 1)
        let extra = dir.sub("proxies")
        TestFS.writeRandom(extra + "/2026-10-05/A001/1.mov", size: 10, seed: 2)   // another folder's proxy: no clash
        let a = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "idle/1.mov")
        XCTAssertNil(a.error)
        XCTAssertEqual(a.path, extra + "/2026-10-05/A001/idle/1.mov")
        let b = ProxyCopy.toExtraFolder(src, extraFolder: extra, date: "2026-10-05", cardName: "A001", fileName: "idle/1.mov")
        XCTAssertEqual(b.path, extra + "/2026-10-05/A001/idle/1_2.mov", "_2 only within the same folder, never overwritten")
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: extra + "/2026-10-05/A001/1.mov")).count, 10)
    }

    func testHTMLDoneLinksToTheProxy() {
        var done = ClipEntry(clip: clip("idle/1 a.mp4"))
        done.proxyStatus = .done
        done.proxyPath = "_PROXY/idle/1 a.mov"
        XCTAssertEqual(HTMLReport.proxyCell(done, "done"), "<a href=\"../_PROXY/idle/1%20a.mov\" title=\"_PROXY/idle/1 a.mov\">done</a>")
        var failed = done
        failed.proxyStatus = .failed
        XCTAssertEqual(HTMLReport.proxyCell(failed, "failed"), "failed")
    }

    // MARK: With ffmpeg

    /// A clip with a TC track and two audio streams (stereo + mono).
    private func twoStreamClip(_ path: String, timecode: String = "01:00:00:00") throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        try FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                                     "-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=1",
                                     "-f", "lavfi", "-i", "sine=f=440:d=1", "-f", "lavfi", "-i", "sine=f=880:d=1",
                                     "-map", "0:v", "-map", "1:a", "-map", "2:a", "-ac:a:0", "2", "-ac:a:1", "1",
                                     "-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", path])
    }

    private func options() -> OffloadOptions {
        var o = OffloadOptions()
        o.chunkSize = 1 << 20
        o.bufferCount = 4
        o.allowSameDevice = true
        o.allowSourceVolume = true
        return o
    }

    private func config(_ card: String, _ dest: String) -> JobConfig {
        var c = JobConfig(source: card, destRoots: [dest])
        c.date = "2026-10-08"
        c.proxy.enabled = true
        c.proxy.height = 360
        c.proxy.burnFilename = false
        c.proxy.burnTimecode = false
        c.tools = TestMedia.tools
        c.dayReport = false
        return c
    }

    private func probe(_ path: String) throws -> [[String: Any]] {
        let (_, ffprobe) = try TestMedia.requireFFmpeg()
        let out = try ToolProcess.run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", path])
        let json = try JSONSerialization.jsonObject(with: Data(out.utf8)) as? [String: Any]
        return json?["streams"] as? [[String: Any]] ?? []
    }

    func testProxiesInFoldersWithReelAndEveryAudioStream() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try twoStreamClip(card + "/idle/1.mov")
        try twoStreamClip(card + "/listening/1.mov", timecode: "02:00:00:00")
        try twoStreamClip(card + "/listening/1.mp4", timecode: "03:00:00:00")
        try twoStreamClip(card + "/B002C004_221010_R2CD.mov")
        let job = OffloadJob(config: config(card, dir.sub("d1")), options: options())
        let report = try XCTUnwrap(job.run())
        XCTAssertEqual(job.status().phase, .done)
        let cardPath = try XCTUnwrap(job.plan).cardPaths[0]
        let byPath = Dictionary(uniqueKeysWithValues: report.clips.map { ($0.clip.path, $0) })
        XCTAssertEqual(byPath["idle/1.mov"]?.proxyPath, "_PROXY/idle/1.mov")
        XCTAssertEqual(byPath["listening/1.mov"]?.proxyPath, "_PROXY/listening/1.mov")
        XCTAssertEqual(byPath["listening/1.mp4"]?.proxyPath, "_PROXY/listening/1_2.mov")
        XCTAssertEqual(byPath["listening/1.mp4"]?.proxyNote, Msg.proxyRenamed("1_2"))
        XCTAssertNil(byPath["idle/1.mov"]?.proxyNote)
        XCTAssertEqual(byPath["B002C004_221010_R2CD.mov"]?.proxyPath, "_PROXY/B002C004_221010_R2CD.mov")
        // The reel: the card folder for "1", the clip name's reel for B002C004.
        XCTAssertEqual(byPath["idle/1.mov"]?.info?.reel, "A001")
        XCTAssertEqual(byPath["B002C004_221010_R2CD.mov"]?.info?.reel, "B002")
        let stored = try OffloadReport.read(cardPath + "/_REPORT/offload_report.json")
        XCTAssertEqual(stored.clips.first { $0.clip.path == "idle/1.mov" }?.info?.reel, "A001")

        for (rel, reel, tc) in [("_PROXY/idle/1.mov", "A001", "01:00:00:00"), ("_PROXY/B002C004_221010_R2CD.mov", "B002", "01:00:00:00"),
                                ("_PROXY/listening/1_2.mov", "A001", "03:00:00:00")] {
            let streams = try probe(cardPath + "/" + rel)
            let audio = streams.filter { $0["codec_type"] as? String == "audio" }
            XCTAssertEqual(audio.map { $0["channels"] as? Int ?? 0 }, [2, 1], rel)
            let tmcd = streams.first { ($0["tags"] as? [String: Any])?["timecode"] != nil && $0["codec_type"] as? String == "data" }
            let tags = tmcd?["tags"] as? [String: Any]
            XCTAssertEqual(tags?["timecode"] as? String, tc, rel)
            XCTAssertEqual(tags?["reel_name"] as? String, reel, rel)
        }
        // The HTML report links "done" to the proxy beside _REPORT.
        let html = try String(contentsOfFile: cardPath + "/_REPORT/A001_report.html", encoding: .utf8)
        XCTAssertTrue(html.contains("<a href=\"../_PROXY/listening/1_2.mov\""), "link to the proxy")
    }

    func testNoTimecodeNoReelInTheProxy() throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=1",
                                     "-c:v", "libx264", "-pix_fmt", "yuv420p", card + "/clip.mp4"])
        let job = OffloadJob(config: config(card, dir.sub("d1")), options: options())
        let report = try XCTUnwrap(job.run())
        let c = try XCTUnwrap(report.clips.first)
        XCTAssertEqual(c.proxyStatus, .done)
        XCTAssertEqual(c.info?.reel, "A001", "the report still names the reel")
        let streams = try probe(try XCTUnwrap(job.plan).cardPaths[0] + "/_PROXY/clip.mov")
        XCTAssertFalse(streams.contains { $0["codec_type"] as? String == "data" }, "no TC: no tmcd track")
        XCTAssertFalse(streams.contains { $0["codec_type"] as? String == "audio" })
    }

    /// An append keeps the backup's layout: an older flat backup (the Mac before task 54) stays flat, nothing moves.
    func testAppendIntoFlatBackupStaysFlatMirrorStaysMirror() throws {
        _ = try TestMedia.requireFFmpeg()
        for flat in [true, false] {
            let dir = tempDir()
            let card = dir.sub("A001")
            try TestMedia.makeClip(card + "/CLIP/C0001.mov", seconds: 1, width: 320, height: 180)
            let first = OffloadJob(config: config(card, dir.sub("d1")), options: options())
            XCTAssertNotNil(first.run())
            let paths = try XCTUnwrap(first.plan).cardPaths
            let mirrored = paths[0] + "/_PROXY/CLIP/C0001.mov"
            XCTAssertTrue(FileManager.default.fileExists(atPath: mirrored))
            if flat {
                // Make it a backup of the old Mac: proxy at the top of _PROXY, its report saying so.
                try FileManager.default.moveItem(atPath: mirrored, toPath: paths[0] + "/_PROXY/C0001.mov")
                let json = paths[0] + "/_REPORT/offload_report.json"
                let text = try String(contentsOfFile: json, encoding: .utf8)
                    .replacingOccurrences(of: "_PROXY\\/CLIP\\/C0001.mov", with: "_PROXY\\/C0001.mov")
                    .replacingOccurrences(of: "_PROXY/CLIP/C0001.mov", with: "_PROXY/C0001.mov")
                try text.write(toFile: json, atomically: true, encoding: .utf8)
                XCTAssertEqual(ProxyPaths.of(paths), .flat)
            }
            let before = try Data(contentsOf: URL(fileURLWithPath: paths[0] + (flat ? "/_PROXY/C0001.mov" : "/_PROXY/CLIP/C0001.mov")))
            try TestMedia.makeClip(card + "/CLIP/C0002.mov", seconds: 1, width: 320, height: 180)
            // A clip named like the earlier proxy, in another folder: flat → "_2", mirrored → its own folder.
            try TestMedia.makeClip(card + "/OTHER/C0001.mov", seconds: 1, width: 320, height: 180)
            var cfg = config(card, dir.sub("d1"))
            cfg.appendInto = paths
            let append = OffloadJob(config: cfg, options: options())
            let report = try XCTUnwrap(append.run())
            XCTAssertEqual(append.status().phase, .done, "\(append.status().error ?? "")")
            let byPath = Dictionary(uniqueKeysWithValues: report.clips.map { ($0.clip.path, $0) })
            if flat {
                XCTAssertEqual(byPath["CLIP/C0002.mov"]?.proxyPath, "_PROXY/C0002.mov")
                XCTAssertEqual(byPath["OTHER/C0001.mov"]?.proxyPath, "_PROXY/C0001_2.mov")
                XCTAssertEqual(TestFS.files(in: paths[0] + "/_PROXY").sorted(), ["C0001.mov", "C0001_2.mov", "C0002.mov"],
                               "nothing moved into folders")
            } else {
                XCTAssertEqual(byPath["CLIP/C0002.mov"]?.proxyPath, "_PROXY/CLIP/C0002.mov")
                XCTAssertEqual(byPath["OTHER/C0001.mov"]?.proxyPath, "_PROXY/OTHER/C0001.mov")
            }
            let after = try Data(contentsOf: URL(fileURLWithPath: paths[0] + (flat ? "/_PROXY/C0001.mov" : "/_PROXY/CLIP/C0001.mov")))
            XCTAssertEqual(before, after, "the earlier proxy is never replaced")
        }
    }
}
