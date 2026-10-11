import XCTest
@testable import OffloadKit

/// Task 68 with the real ffmpeg (skipped without it): ffmpeg reads clips through the tap (spec D.23.7, D.27 row 5) and
/// verify-in-proxy finds a damaged primary copy in a whole job.
final class TapFFmpegTests: XCTestCase {
    /// Encodes a test clip in a container: MOV with moov at the end, MP4 with +faststart, MXF (MPEG-2).
    func clip(_ path: String, _ container: String) throws {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        var args = ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=25:d=4",
                    "-f", "lavfi", "-i", "sine=d=4"]
        switch container {
        case "mxf": args += ["-c:v", "mpeg2video", "-b:v", "20M", "-c:a", "pcm_s16le", "-ar", "48000", "-shortest"]
        case "mp4": args += ["-c:v", "libx264", "-pix_fmt", "yuv420p", "-b:v", "8M", "-c:a", "aac", "-shortest", "-movflags", "+faststart"]
        default: args += ["-c:v", "libx264", "-pix_fmt", "yuv420p", "-b:v", "8M", "-c:a", "aac", "-shortest"]
        }
        try ToolProcess.run(ffmpeg, args + [path])
    }

    /// Decodes `path` through a tap (the proxy's reads, without the encoder) and returns what the tap read from the disk.
    func decodeThroughTap(_ path: String, blockSize: Int, output: [String] = ["-f", "null", "-"]) throws
        -> (fed: Int64, size: Int64, verified: Bool, seconds: Double) {
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let data = try Data(contentsOf: URL(fileURLWithPath: path))
        let blocks = stride(from: 0, to: data.count, by: blockSize).map { off -> UInt64 in
            data.subdata(in: off..<min(off + blockSize, data.count)).withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, $0.count) }
        }
        CacheProbe.evict(path)
        let v = BlockVerifier(BlockManifest(size: Int64(data.count), blockSize: blockSize, blocks: blocks))
        let tap = try TapFile(path: path, verifier: v)
        let server = try RangeServer()
        defer { server.close() }
        let url = server.register(tap, name: (path as NSString).lastPathComponent)
        let t0 = Date()
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-short_seek_size", "65536", "-i", url,
                                     "-map", "0:v:0", "-map", "0:a?"] + output)
        let seconds = Date().timeIntervalSince(t0)
        tap.close()
        return (v.fedBytes, Int64(data.count), v.finish() == .verified, seconds)
    }

    // D.27 row 5: what ffmpeg reads through the tap, per container (the numbers go to the log and the commit).
    func testFFmpegReadsThroughTheTapOnce() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir().sub("c")
        for container in ["mov", "mp4", "mxf"] {
            let p = dir + "/clip." + container
            try clip(p, container)
            let r = try decodeThroughTap(p, blockSize: 1 << 20)
            let ratio = Double(r.fed) / Double(r.size)
            print(String(format: "TAP %@: %lld bytes, read %lld (%.3f×), %.2f s", container, r.size, r.fed, ratio, r.seconds))
            XCTAssertTrue(r.verified || ratio < 1, "\(container): every block the decoder read is verified")
            // Spec 11: at most 1.05 × the file (measured 2026-10-11, ffmpeg 9.0.2: MOV moov at the end 1.000, MP4 1.000,
            // MXF 1.019).
            XCTAssertLessThanOrEqual(ratio, 1.05, "\(container): read once (Windows 8.1: 1.018 × with -short_seek_size)")
        }
    }

    // D.27 row 5, by hand: OFFLOAD_TAP_BENCH=1 swift test --filter testTapSpeedAgainstTheDisk — decoding through the tap
    // against decoding the file (spec 11: at most 5 % slower).
    func testTapSpeedAgainstTheDisk() throws {
        guard ProcessInfo.processInfo.environment["OFFLOAD_TAP_BENCH"] == "1" else { throw XCTSkip("OFFLOAD_TAP_BENCH=1 to measure") }
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let p = tempDir().sub("bench") + "/big.mov"
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i",
                                     "testsrc2=s=1920x1080:r=25:d=60", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "40M",
                                     "-pix_fmt", "yuv420p", p])
        // Decoding alone, then as a proxy is made (scaled to 1080p, encoded like the default H.264 proxy).
        let proxy = ["-vf", "scale=-2:1080"] + ProxyCodec.h264.encoderArgs(bitrateMbps: 10) + ["-f", "mov", "-y", p + ".proxy.mov"]
        for (name, output) in [("decode", ["-f", "null", "-"]), ("proxy", proxy)] {
            func direct() throws -> Double {
                CacheProbe.evict(p)
                let t0 = Date()
                try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", p, "-map", "0:v:0"] + output)
                return Date().timeIntervalSince(t0)
            }
            var d: [Double] = [], t: [Double] = []
            var fed: Int64 = 0, size: Int64 = 0
            for _ in 0..<3 {
                d.append(try direct())
                let r = try decodeThroughTap(p, blockSize: 16 << 20, output: output)
                t.append(r.seconds)
                fed = r.fed
                size = r.size
            }
            print(String(format: "TAP BENCH %@: %lld bytes, read %.3f×, direct %.2f s, tap %.2f s (best of 3), %+.1f %%", name, size,
                         Double(fed) / Double(size), d.min()!, t.min()!, (t.min()! / d.min()! - 1) * 100))
        }
    }

    // D.27 row 9, by hand: OFFLOAD_TAP_BENCH=1 OFFLOAD_TEST_DEST=<folder on another volume> — a secondary destination
    // read in the background (PRIO_DARWIN_BG or not) while a proxy renders through the tap from the primary.
    func testBackgroundPriorityAgainstTheProxy() throws {
        let env = ProcessInfo.processInfo.environment
        guard env["OFFLOAD_TAP_BENCH"] == "1", let dest = env["OFFLOAD_TEST_DEST"] else { throw XCTSkip("OFFLOAD_TAP_BENCH=1 and OFFLOAD_TEST_DEST") }
        let (ffmpeg, _) = try TestMedia.requireFFmpeg()
        let p = tempDir().sub("bench") + "/big.mov"
        try ToolProcess.run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i",
                                     "testsrc2=s=1920x1080:r=25:d=40", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "40M",
                                     "-pix_fmt", "yuv420p", p])
        let second = dest + "/offload-bench-" + String(UUID().uuidString.prefix(8)) + ".bin"
        defer { unlink(second) }
        let data = TestFS.writeRandom(second, size: 400 << 20, seed: 9)
        let bs = 16 << 20
        let blocks = stride(from: 0, to: data.count, by: bs).map { off -> UInt64 in
            data.subdata(in: off..<min(off + bs, data.count)).withUnsafeBytes { BlockHash.xxh3($0.baseAddress!, $0.count) }
        }
        let manifest = BlockManifest(size: Int64(data.count), blockSize: bs, blocks: blocks)
        func readSecond(_ policy: String) -> Double {
            CacheProbe.evict(second)
            var seconds = 0.0
            let done = DispatchSemaphore(value: 0)
            let t = Thread {
                if policy == "PRIO_DARWIN_BG" { setpriority(PRIO_DARWIN_THREAD, 0, PRIO_DARWIN_BG) }
                if policy == "IOPOL_UTILITY" { setiopolicy_np(IOPOL_TYPE_DISK, IOPOL_SCOPE_THREAD, IOPOL_UTILITY) }
                let t0 = Date()
                _ = try? BlockReads.complete(second, BlockVerifier(manifest), isCancelled: { false }, progress: nil)
                seconds = Date().timeIntervalSince(t0)
                done.signal()
            }
            t.start()
            done.wait()
            return seconds
        }
        let proxy = ["-vf", "scale=-2:1080"] + ProxyCodec.h264.encoderArgs(bitrateMbps: 10) + ["-f", "mov", "-y", p + ".proxy.mov"]
        let alone = try decodeThroughTap(p, blockSize: bs, output: proxy).seconds
        let mb = Double(data.count) / 1e6
        for policy in ["PRIO_DARWIN_BG", "IOPOL_UTILITY", "normal"] {
            let readAlone = readSecond(policy)
            var readSeconds = 0.0
            let reader = Thread { readSeconds = readSecond(policy) }
            reader.start()
            let withReader = try decodeThroughTap(p, blockSize: bs, output: proxy).seconds
            while reader.isExecuting { usleep(10_000) }
            print(String(format: "BG BENCH %@: proxy alone %.2f s, with the background read %.2f s (%+.1f %%); background read %.0f MB/s alone, %.0f MB/s during the proxy",
                         policy, alone, withReader, (withReader / alone - 1) * 100, mb / readAlone, mb / readSeconds))
        }
    }

    // A whole job: the primary's copy damaged after the copy (outside Offload) is found by the proxy's reads.
    func testFlippedBitOnPrimaryFoundInModeA() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A001")
        try clip(card + "/A001C001.mov", "mov")
        var cfg = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        cfg.date = "2026-10-08"
        cfg.proxy.enabled = true
        cfg.proxy.height = 360
        cfg.tools = TestMedia.tools
        cfg.primaryDestination = 0
        cfg.analyzeClips = true
        var o = OffloadJob.engineOptions(cfg)
        o.chunkSize = 1 << 20
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let job = OffloadJob(config: cfg, options: o)
        var session: VerifySession?
        job.onVerifySession = { session = $0 }
        XCTAssertTrue(job.runCopy())
        XCTAssertTrue(job.status().verifyInProxy)
        XCTAssertTrue(job.status().safeToEject)
        XCTAssertFalse(job.status().safeToFormat)
        let paths = try XCTUnwrap(job.plan).cardPaths
        let size = try XCTUnwrap(try FileManager.default.attributesOfItem(atPath: card + "/A001C001.mov")[.size] as? NSNumber).uint64Value
        // A bit in the middle of the media data (a frame the decoder reads; it may conceal it — the verifier does not).
        let h = try XCTUnwrap(FileHandle(forUpdatingAtPath: paths[0] + "/A001C001.mov"))
        try h.seek(toOffset: size / 2)
        let b = try XCTUnwrap(h.readData(ofLength: 1).first)
        try h.seek(toOffset: size / 2)
        h.write(Data([b ^ 0x01]))
        try h.close()
        job.runPost()
        let s = job.status()
        XCTAssertEqual(s.phase, .done, s.error ?? "")
        XCTAssertFalse(s.safeToFormat)
        XCTAssertEqual(session?.tapsServed, 1, "the proxy read the primary through the tap")
        let r = try OffloadReport.read(paths[0] + "/_REPORT/offload_report.json")
        XCTAssertNil(r.verifyPending)
        XCTAssertEqual(r.destinations.map(\.verifyMethod), ["verify-in-proxy", "read-back"])
        let f = try XCTUnwrap(r.files.first)
        XCTAssertEqual(f.destinations, ["failed", "verified"])
        let block = Int(size / 2) / (1 << 20)
        XCTAssertTrue(f.errors[0]?.hasPrefix("HASH MISMATCH: block \(block) dest ") ?? false, f.errors[0] ?? "nil")
        XCTAssertEqual(MHLReader.readChain(ascmhlDir: paths[0] + "/ascmhl")?.map(\.seq), [1])
    }

    // An undamaged card: Safe to format only after the verification, everything verified, the method in the report.
    func testCleanJobVerifiesInProxy() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A002")
        try clip(card + "/A002C001.mp4", "mp4")
        try "<x/>".write(toFile: card + "/A002C001.xml", atomically: true, encoding: .utf8)
        var cfg = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        cfg.proxy.enabled = true
        cfg.proxy.height = 360
        cfg.tools = TestMedia.tools
        var o = OffloadJob.engineOptions(cfg)
        o.chunkSize = 1 << 20
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let job = OffloadJob(config: cfg, options: o)
        let report = try XCTUnwrap(job.run())
        let s = job.status()
        XCTAssertEqual(s.phase, .done, s.error ?? "")
        XCTAssertTrue(s.safeToFormat, s.warning ?? "")
        XCTAssertNil(s.warning)
        XCTAssertTrue(report.verified)
        XCTAssertEqual(report.destinations.compactMap(\.verifyMethod).sorted(), ["read-back", "verify-in-proxy"])
        XCTAssertEqual(report.clips.first?.proxyStatus, .done)
        for p in try XCTUnwrap(job.plan).cardPaths {
            XCTAssertTrue(CardRecord.load(p).verified)
            XCTAssertTrue(FileManager.default.fileExists(atPath: p + "/_REPORT/A002.xxh64"))
        }
    }

    // Task 69: the app ended in stage 2 after its proxies, before the commit — "Finish interrupted backup" keeps them.
    func testResumeKeepsFinishedProxies() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A004")
        try clip(card + "/A004C001.mov", "mov")
        var cfg = JobConfig(source: card, destRoots: [dir.sub("d1"), dir.sub("d2")])
        cfg.proxy.enabled = true
        cfg.proxy.height = 360
        cfg.tools = TestMedia.tools
        cfg.historyFolder = dir.sub("history")
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        o.allowSameDevice = true
        let job = OffloadJob(config: cfg, options: o)
        job.run()
        let paths = try XCTUnwrap(job.plan).cardPaths
        // On the disks as a crash before the commit leaves it: reports pending, no ASC MHL, no hash lists.
        for p in paths {
            var r = try OffloadReport.read(p + "/_REPORT/offload_report.json")
            r = r.withVerification(results: r.destinations.indices.map { _ in r.files.map { _ in DestFileResult(state: .written) } },
                                   verified: false, primary: -1, classicWhy: [], notes: [], finished: r.finished, verifyPaused: 0)
            r.verifyPending = true
            try r.jsonData().write(to: URL(fileURLWithPath: p + "/_REPORT/offload_report.json"))
            try FileManager.default.removeItem(atPath: p + "/ascmhl")
            try FileManager.default.removeItem(atPath: p + "/_REPORT/A004.xxh64")
        }
        let proxyBefore = try Data(contentsOf: URL(fileURLWithPath: paths[0] + "/_PROXY/A004C001.mov"))
        var fc = JobConfig(source: card, destRoots: [])
        fc.appendInto = paths
        fc.proxy = cfg.proxy
        fc.tools = TestMedia.tools
        fc.historyFolder = cfg.historyFolder
        var fo = OffloadJob.engineOptions(fc)
        fo.allowSourceVolume = true
        fo.allowSameDevice = true
        let finish = OffloadJob(config: fc, options: fo)
        let r = try XCTUnwrap(finish.run())
        XCTAssertEqual(finish.status().phase, .done, finish.status().error ?? "")
        XCTAssertTrue(finish.status().safeToFormat, finish.status().warning ?? "")
        XCTAssertEqual(r.generationNumber, 1)
        let c = try XCTUnwrap(r.clips.first)
        XCTAssertEqual(c.proxyPath, "_PROXY/A004C001.mov")
        XCTAssertEqual(c.proxyNote, VIPMsg.proxyKeptOnResume)
        for p in paths {
            XCTAssertFalse(FileManager.default.fileExists(atPath: p + "/_PROXY/A004C001_2.mov"), "never _2")
            XCTAssertEqual(MHLReader.readChain(ascmhlDir: p + "/ascmhl")?.map(\.seq), [1])
        }
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: paths[0] + "/_PROXY/A004C001.mov")), proxyBefore, "nothing replaced")
    }

    // The setting off (CLI --classic-verify): the read-back after the copy, as before.
    func testClassicVerifyReadsBackInStage1() throws {
        _ = try TestMedia.requireFFmpeg()
        let dir = tempDir()
        let card = dir.sub("A003")
        try clip(card + "/A003C001.mov", "mov")
        var cfg = JobConfig(source: card, destRoots: [dir.sub("d1")])
        cfg.proxy.enabled = true
        cfg.proxy.height = 360
        cfg.tools = TestMedia.tools
        cfg.verifyInProxy = false
        var o = OffloadJob.engineOptions(cfg)
        o.allowSourceVolume = true
        let job = OffloadJob(config: cfg, options: o)
        XCTAssertTrue(job.runCopy())
        XCTAssertFalse(job.status().verifyInProxy)
        XCTAssertTrue(job.status().safeToFormat, "read back in stage 1")
        job.runPost()
        XCTAssertEqual(job.report?.destinations.first?.verifyMethod, "read-back", "the classic read-back of stage 1 (task 70: named as Windows does)")
    }
}
