import XCTest
@testable import OffloadKit

/// ARRIRAW without a LUT: art-cmd refuses `--target-colorspace` together with `--embedded-look` ("target-colorspace
/// argument is only valid for embedded looks with DRT LUTs") — it says so on stdout, exits 0 and writes no file.
/// Every ARRIRAW clip must still get a proxy and thumbnails, with a built-in LUT, a custom one, or none.
final class ArriNoLutTests: XCTestCase {
    private static let refusal = #"[2026-10-06 00:50:21.365] [default] [error] process failed with: Error INVALID_ARGUMENT in file Process.cpp:467: "target-colorspace argument is only valid for embedded looks with DRT LUTs""#

    /// A stand-in for art-cmd that behaves like the real one (log on stdout, exit 0): refuses the combination above,
    /// otherwise writes a 1 s intermediate to `--output`. Every call is appended to `<dir>/calls.txt`.
    private func fakeArtCmd(_ dir: TempDir, ffmpeg: String) -> String {
        let path = dir.path + "/art-cmd"
        let script = """
        #!/bin/sh
        out=""; tcs=0; look=0; prev=""
        for a in "$@"; do
          [ "$prev" = "--output" ] && out="$a"
          [ "$a" = "--target-colorspace" ] && tcs=1
          [ "$a" = "--embedded-look" ] && look=1
          prev="$a"
        done
        echo "$*" >> "\(dir.path)/calls.txt"
        echo '[2026-10-06 00:50:21.354] [default] [info] processing'
        if [ $tcs = 1 ] && [ $look = 1 ]; then
          echo '\(ArriNoLutTests.refusal)'
          exit 0
        fi
        "\(ffmpeg)" -hide_banner -loglevel error -nostdin -y -f lavfi -i color=c=gray:s=320x180:d=1:r=25 \
          -c:v prores_ks -profile:v 1 -f mov "$out" || exit 3
        exit 0
        """
        try! script.write(toFile: path, atomically: true, encoding: .utf8)
        chmod(path, 0o755)
        return path
    }

    /// art-cmd that only logs (stdout) and exits with `status`, never writing the output file.
    private func failingArtCmd(_ dir: TempDir, status: Int32, lines: [String]) -> String {
        let path = dir.path + "/art-cmd-\(status)"
        let body = lines.map { "echo '\($0)'" }.joined(separator: "\n")
        try! "#!/bin/sh\n\(body)\nexit \(status)\n".write(toFile: path, atomically: true, encoding: .utf8)
        chmod(path, 0o755)
        return path
    }

    private func arriClip(_ card: String) -> Clip {
        try? FileManager.default.createDirectory(atPath: card + "/A", withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: card + "/A/A001C001.mxf", contents: Data("x".utf8))
        return Clip(name: "A001C001", path: "A/A001C001.mxf", format: .arriraw, segments: ["A/A001C001.mxf"], bytes: 1,
                    isCameraProxy: false)
    }

    private func arriInfo(camera: String = "ALEXA 35") -> ClipInfo {
        var i = ClipInfo()
        i.codec = "ARRIRAW"
        i.width = 2048
        i.height = 1152
        i.frameRate = "25"
        i.frames = 25
        i.duration = 1
        i.camera = camera
        return i
    }

    private func settings() -> ProxySettings {
        var s = ProxySettings()
        s.enabled = true
        s.height = 180
        s.burnFilename = false
        s.burnTimecode = false
        return s
    }

    private func calls(_ dir: TempDir) -> [String] {
        ((try? String(contentsOfFile: dir.path + "/calls.txt", encoding: .utf8)) ?? "").split(separator: "\n").map(String.init)
    }

    // Without a LUT the camera's embedded look (Rec.709 from ARRI's display transform; plain LogC when the clip has none)
    // — never a target colour space with it.
    func testArgsWithoutLutEmbeddedLookOnly() {
        let a = ARRIRAWBackend.args(input: "in", output: "out", info: arriInfo(), settings: ProxySettings(),
                                    look: RawLook(log: false, arriColorSpace: nil), parametersFile: "p.json")
        XCTAssertEqual(a.args, ["process", "--input", "in", "--video-codec", "prores422lt", "--deflip", "--output", "out",
                                "--output-height", "1080", "--embedded-look"])
        XCTAssertFalse(a.args.contains("--target-colorspace"))
        // With a LUT: log in the LUT's colour space, no look.
        let log = ARRIRAWBackend.args(input: "in", output: "out", info: arriInfo(), settings: ProxySettings(),
                                      look: RawLook(log: true, arriColorSpace: "AWG4/D65/LogC4"), parametersFile: "p.json")
        XCTAssertEqual(Array(log.args.suffix(2)), ["--target-colorspace", "AWG4/D65/LogC4"])
        XCTAssertFalse(log.args.contains("--embedded-look"))
    }

    // An empty LUT path is "no LUT" everywhere: no log from the RAW decoder, nothing copied, no start blocked.
    func testEmptyLutPathIsNoLut() throws {
        let empty = LutUse(source: "custom", id: nil, name: "", reason: "custom LUT", file: "")
        XCTAssertEqual(ProxyRenderer.lookFor(empty, info: arriInfo()), RawLook(log: false, arriColorSpace: nil))
        var s = settings()
        s.lutMode = .custom
        s.lutPath = ""
        XCTAssertNil(try ProxyRenderer(settings: s, tools: ExternalTools()).prepareLUT(empty))
        XCTAssertNil(ProxyPreflight.lutInUse(s, analyzeClips: true))
        XCTAssertNil(ProxyPreflight.blocking(s, analyzeClips: true, lutExists: false))
        XCTAssertEqual(LutOption.parse("", fallback: nil, catalog: TestMedia.luts).mode, .off)
        XCTAssertNil(LutOption.parse("  ", fallback: nil, catalog: TestMedia.luts).error)
    }

    // The bug: no LUT → art-cmd refused the arguments, exited 0, and the clip got "no output" instead of a proxy.
    func testNoLutBuiltinCustomAndEmptyAllGetAProxyAndThumbnails() throws {
        let (ffmpeg, ffprobe) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        let cube = dir.path + "/show_LogC4.cube"
        TestMedia.identityLUT(cube)
        let tools = ExternalTools(ffmpeg: ffmpeg, ffprobe: ffprobe, artCmd: fakeArtCmd(dir, ffmpeg: ffmpeg))
        let clip = arriClip(card)
        let builtin = try XCTUnwrap(TestMedia.luts.find("arri-logc4-awg4"))
        let cases: [(String, LutMode, String?, LutUse?, Bool)] = [
            ("none", .off, nil, .off("off in settings"), false),
            ("nil", .off, nil, nil, false),
            ("empty path", .custom, "", LutUse(source: "custom", id: nil, name: "", reason: "custom LUT", file: ""), false),
            ("builtin", .auto, nil, LutUse(source: "auto", id: builtin.id, name: builtin.name, reason: "r", file: builtin.path), true),
            ("custom", .custom, cube, TestMedia.customLut(cube), true),
        ]
        for (name, mode, path, lut, log) in cases {
            var s = settings()
            s.lutMode = mode
            s.lutPath = path
            let r = ProxyRenderer(settings: s, tools: tools)
            let out = dir.path + "/proxy-\(name.replacingOccurrences(of: " ", with: "-")).mov"
            try r.render(clip, cardPath: card, info: arriInfo(), lut: lut, output: out, progress: { _ in }, isCancelled: { false })
            XCTAssertTrue(FileManager.default.fileExists(atPath: out), name)
            let call = try XCTUnwrap(calls(dir).last, name)
            XCTAssertEqual(call.contains("--target-colorspace"), log, "\(name): \(call)")
            XCTAssertEqual(call.contains("--embedded-look"), !log, "\(name): \(call)")
            // Thumbnails of RAW clips come from the finished proxy (as the job takes them).
            let proxyClip = Clip(name: "p", path: (out as NSString).lastPathComponent, format: .video,
                                 segments: [(out as NSString).lastPathComponent], bytes: 0, isCameraProxy: false)
            XCTAssertFalse(try r.thumbnails(proxyClip, cardPath: dir.path, info: try r.probe(proxyClip, cardPath: dir.path)).isEmpty, name)
        }
    }

    // art-cmd logs to stdout and may exit 0 without writing anything: the error says so and quotes art-cmd's own lines.
    func testArtCmdWithoutOutputErrorQuotesItsLog() throws {
        let (ffmpeg, ffprobe) = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("card")
        let clip = arriClip(card)
        for (status, lines, expected) in [
            (Int32(0), ["[2026-10-06 00:50:21.354] [default] [info] processing", ArriNoLutTests.refusal],
             "only valid for embedded looks with DRT LUTs"),
            (Int32(2), ["[2026-10-06 00:50:21.354] [default] [info] processing", "[2026-10-06 00:50:21.400] [default] [critical] no license"],
             "no license"),
        ] {
            let tools = ExternalTools(ffmpeg: ffmpeg, ffprobe: ffprobe, artCmd: failingArtCmd(dir, status: status, lines: lines))
            let r = ProxyRenderer(settings: settings(), tools: tools)
            XCTAssertThrowsError(try r.render(clip, cardPath: card, info: arriInfo(), lut: nil, output: dir.path + "/p.mov",
                                              progress: { _ in }, isCancelled: { false })) { error in
                let text = "\(error)"
                XCTAssertTrue(text.contains("art-cmd"), text)
                XCTAssertTrue(text.contains(expected), text)
                XCTAssertFalse(text.contains("2026-10-06 00:50"), text)   // the timestamp prefix is dropped
                if status == 0 { XCTAssertTrue(text.contains("no output file"), text) }
            }
            XCTAssertFalse(FileManager.default.fileExists(atPath: dir.path + "/p.mov"))
        }
    }

    /// mac/TestMedia/ARRIRAW (not in git): the real clips and the real art-cmd, if both are here.
    private static let sampleFolder: String = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("TestMedia/ARRIRAW").path

    // Real art-cmd: ALEXA 35 (LogC4) and ALEXA Mini (LogC3) samples, no LUT and the built-in LUT.
    func testRealArrirawProxyWithAndWithoutLut() throws {
        _ = try TestMedia.requireFFmpeg()
        guard TestMedia.tools.artCmd != nil else { throw XCTSkip("art-cmd not installed") }
        let samples = ((try? FileManager.default.contentsOfDirectory(atPath: ArriNoLutTests.sampleFolder)) ?? [])
            .filter { $0.lowercased().hasSuffix(".mxf") }.sorted()
        guard !samples.isEmpty else { throw XCTSkip("no ARRIRAW samples in \(ArriNoLutTests.sampleFolder)") }
        let dir = tempDir()
        for file in samples {
            let clip = Clip(name: (file as NSString).deletingPathExtension, path: file, format: .arriraw, segments: [file],
                            bytes: 1, isCameraProxy: false)
            for mode in [LutMode.off, .auto] {
                var s = settings()
                s.height = 540   // art-cmd scales to at least 360 px wide; 540 is the smallest height the app offers
                s.lutMode = mode
                let r = ProxyRenderer(settings: s, tools: TestMedia.tools)
                let info = try r.probe(clip, cardPath: ArriNoLutTests.sampleFolder)
                let lut = LutResolver.resolve(clip, info: info, clipFullPath: ArriNoLutTests.sampleFolder + "/" + file,
                                              settings: s, catalog: TestMedia.luts)
                XCTAssertEqual(lut.source, mode == .off ? "none" : "auto", file)
                let out = dir.path + "/\(clip.name)-\(mode.rawValue).mov"
                try r.render(clip, cardPath: ArriNoLutTests.sampleFolder, info: info, lut: lut, output: out,
                             progress: { _ in }, isCancelled: { false })
                XCTAssertTrue(FileManager.default.fileExists(atPath: out), "\(file) \(mode)")
                let proxyClip = Clip(name: clip.name, path: (out as NSString).lastPathComponent, format: .video,
                                     segments: [(out as NSString).lastPathComponent], bytes: 0, isCameraProxy: false)
                XCTAssertEqual(try r.thumbnails(proxyClip, cardPath: dir.path, info: info).count, 3, "\(file) \(mode)")
            }
        }
    }
}
