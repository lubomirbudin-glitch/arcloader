import ArgumentParser
import Darwin
import Foundation
import OffloadKit

@main
struct Offload: ParsableCommand {
    static let configuration = CommandConfiguration(
        abstract: "Camera card offload: read once, write to several disks, verify with xxh64.",
        version: OffloadVersion.string,
        subcommands: [Copy.self, Verify.self, Bench.self],
        defaultSubcommand: Copy.self)
}

struct Copy: ParsableCommand {
    static let configuration = CommandConfiguration(abstract: "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.")

    @Argument(help: "Card mount point or source folder.")
    var source: String

    @Option(name: [.short, .long], help: "Destination root (repeat for each disk).")
    var dest: [String]

    @Option(help: "Card folder name (default: volume label).")
    var name: String?

    @Option(help: "Date folder (default: today, YYYY-MM-DD).")
    var date: String?

    @Option(help: "Block size in MB.")
    var chunkMb = 16

    @Option(help: "Blocks in flight.")
    var buffers = 16

    @Flag(help: "Skip read-back verification (not recommended).")
    var noVerify = false

    @Flag(help: "Allow two destinations on the same volume.")
    var allowSameDevice = false

    @Flag(help: "Only scan and check, do not copy.")
    var dryRun = false

    @Flag(help: "Skip clip analysis (metadata + thumbnails in the report).")
    var noAnalyze = false

    @Flag(help: "Render proxies after the copy is verified.")
    var proxy = false

    @Option(help: "LUT (.cube) applied to proxies and thumbnails.")
    var lut: String?

    @Option(help: "Proxy height in pixels.")
    var proxyHeight = 1080

    @Option(help: "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.")
    var frameAspect: Double?

    @Option(help: "Frame lines size in % of the image (90 = 10 % overscan).")
    var frameScale: Double = 100

    @Option(help: "Proxy codec: h264, hevc, proresProxy, proresLT, prores422, prores422HQ.")
    var proxyCodec = "h264"

    @Option(help: "Proxy bitrate (Mb/s) for H.264/HEVC.")
    var proxyBitrate = 10

    @Flag(help: "No burn-in of clip name and timecode.")
    var noBurnIn = false

    func run() throws {
        var config = JobConfig(source: abs(source), destRoots: dest.map(abs))
        config.cardName = name
        config.date = date
        config.chunkMB = chunkMb
        config.buffers = buffers
        config.verify = !noVerify
        config.allowSameDevice = allowSameDevice
        config.analyzeClips = !noAnalyze
        config.proxy.enabled = proxy
        config.proxy.lutPath = lut.map(abs)
        config.proxy.height = proxyHeight
        guard let codec = ProxyCodec(rawValue: proxyCodec) else {
            throw ValidationError("Unknown proxy codec \(proxyCodec)")
        }
        config.proxy.codec = codec
        config.proxy.bitrateMbps = proxyBitrate
        config.proxy.frameAspect = frameAspect
        config.proxy.frameScale = frameScale / 100
        config.proxy.burnFilename = !noBurnIn
        config.proxy.burnTimecode = !noBurnIn
        if let lut = config.proxy.lutPath, !FileManager.default.fileExists(atPath: lut) {
            throw ValidationError("LUT not found: \(lut)")
        }

        let job = OffloadJob(config: config)
        let plan = try job.prepare()
        print("Card \(plan.cardName): \(plan.scan.files.count) files, \(formatBytes(plan.scan.totalBytes))")
        for p in plan.cardPaths { print("  → \(p)") }
        let tools = config.tools.resolved()
        if config.analyzeClips || proxy {
            print("  ffmpeg: \(tools.ffmpeg ?? "NOT FOUND")")
        }
        fflush(stdout)
        if dryRun { return }

        signal(SIGINT, SIG_IGN)
        let sigint = DispatchSource.makeSignalSource(signal: SIGINT, queue: .global())
        sigint.setEventHandler {
            fputs("\nCancelling — finishing current block, partial files will be removed…\n", stderr)
            job.cancel()
        }
        sigint.resume()

        let display = ProgressDisplay(job: job)
        display.start()
        // The job runs on a worker thread: the main thread keeps its run loop going,
        // which WebKit needs to print the PDF report.
        var report: OffloadReport?
        var finished = false
        Thread.detachNewThread {
            report = job.run()
            DispatchQueue.main.async { finished = true }
        }
        while !finished { RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.1)) }
        display.stop()

        let status = job.status()
        if let report {
            print("\n" + report.summary)
            if !report.clips.isEmpty {
                let done = report.clips.filter { $0.proxyStatus == .done }.count
                print("Clips: \(report.clips.count)" + (proxy ? "  proxies OK \(done), failed \(status.proxiesFailed)" : ""))
                for c in report.clips where c.proxyStatus == .failed || c.proxyStatus == .unsupported {
                    print("  \(c.clip.name): \(c.proxyStatus.rawValue) — \(c.proxyNote ?? "")")
                }
            }
            print("Report: \(plan.cardPaths[0])/\(Layout.reportDirName)/\(plan.cardName)_report.html")
        }
        if let e = status.error { print("ERROR: \(e)") }
        if status.phase != .done || report?.success != true { throw ExitCode(1) }
    }
}

struct Verify: ParsableCommand {
    static let configuration = CommandConfiguration(abstract: "Re-verify an existing card folder against its _REPORT hash list.")

    @Argument(help: "Card folder (<root>/<date>/<card>).")
    var cardPath: String

    func run() throws {
        let t0 = Date()
        var last = Date.distantPast
        let r = try Verifier.verify(cardPath: abs(cardPath)) { file, bytes in
            if Date().timeIntervalSince(last) > 0.5 {
                last = Date()
                fputs("\r\u{1B}[K\(formatBytes(bytes))  \(file)", stderr)
            }
        }
        let secs = Date().timeIntervalSince(t0)
        fputs("\r\u{1B}[K", stderr)
        print("Verified \(r.ok.count) files, \(formatBytes(r.bytes)) in \(String(format: "%.1f", secs)) s (\(formatRate(r.bytes, secs)))")
        for m in r.mismatched { print("MISMATCH  \(m)") }
        for m in r.missing { print("MISSING   \(m)") }
        for m in r.unreadable { print("ERROR     \(m)") }
        for m in r.extra { print("EXTRA     \(m) (not in hash list)") }
        print(r.success ? "OK" : "FAILED")
        if !r.success { throw ExitCode(1) }
    }
}

struct Bench: ParsableCommand {
    static let configuration = CommandConfiguration(abstract: "Measure card read speed and disk write/read speed.")

    @Option(help: "Card or file to read from.")
    var read: String?

    @Option(help: "Destination folder to test write speed (repeatable).")
    var write: [String] = []

    @Option(help: "Amount of data per test in GB.")
    var gb: Double = 2

    func run() throws {
        let bytes = Int64(gb * 1_000_000_000)
        let print2: (OffloadKit.Bench.Line) -> Void = { l in
            print("  \(l.label.padding(toLength: 22, withPad: " ", startingAt: 0)) \(formatBytes(l.bytes).padding(toLength: 10, withPad: " ", startingAt: 0)) \(l.rate)")
        }
        print("CPU")
        print2(OffloadKit.Bench.hash(size: 256 << 20))
        if let read {
            print("Read \(read)")
            for l in try OffloadKit.Bench.read(path: abs(read), limit: bytes, blockSizes: [64 << 10, 1 << 20, 4 << 20, 16 << 20]) {
                print2(l)
            }
        }
        for w in write {
            print("Write \(w)")
            for l in try OffloadKit.Bench.write(dir: abs(w), size: bytes, blockSize: 16 << 20) { print2(l) }
        }
    }
}

func abs(_ p: String) -> String {
    URL(fileURLWithPath: (p as NSString).expandingTildeInPath).standardizedFileURL.path
}

/// One status line on stderr, refreshed twice a second.
final class ProgressDisplay {
    let job: OffloadJob
    private let timer = DispatchSource.makeTimerSource(queue: .global())
    private var lastTime = Date()
    private var lastRead: Int64 = 0
    private var lastWritten: [Int64] = []
    private var lastVerified: [Int64] = []
    private var lastPhase = ""

    init(job: OffloadJob) { self.job = job }

    func start() {
        timer.schedule(deadline: .now() + 0.5, repeating: 0.5)
        timer.setEventHandler { [weak self] in self?.tick() }
        timer.resume()
    }

    func stop() {
        timer.cancel()
        fputs("\r\u{1B}[K", stderr)
    }

    private func tick() {
        let st = job.status()
        let now = Date()
        let dt = now.timeIntervalSince(lastTime)
        if st.phase.rawValue != lastPhase {
            if !lastPhase.isEmpty { fputs("\n", stderr) }
            lastPhase = st.phase.rawValue
        }
        var line: String
        if let s = st.copy, [.copying, .verifying, .flushing, .reporting].contains(st.phase) {
            if lastWritten.count != s.writtenBytes.count {
                lastWritten = s.writtenBytes
                lastVerified = s.verifiedBytes
            }
            let rate = { (now: Int64, before: Int64) in String(format: "%4.0f", Double(now - before) / dt / 1_000_000) }
            switch st.phase {
            case .copying:
                let pct = s.totalBytes > 0 ? Double(s.readBytes) / Double(s.totalBytes) * 100 : 0
                let dests = s.writtenBytes.indices.map { "D\($0 + 1) \(rate(s.writtenBytes[$0], lastWritten[$0]))" }.joined(separator: "  ")
                line = String(format: "copy %d/%d  %@ / %@  %3.0f%%  card %@ MB/s  %@ MB/s",
                              s.fileIndex + 1, s.totalFiles, formatBytes(s.readBytes), formatBytes(s.totalBytes), pct,
                              rate(s.readBytes, lastRead), dests)
                line += "  " + (s.currentFile as NSString).lastPathComponent
            case .verifying:
                line = "verify  " + s.verifiedBytes.indices.map {
                    String(format: "D%d %3.0f%% %@ MB/s", $0 + 1, Double(s.verifiedBytes[$0]) / Double(max(s.totalBytes, 1)) * 100,
                           rate(s.verifiedBytes[$0], lastVerified[$0]))
                }.joined(separator: "  ")
            default:
                line = st.phase.rawValue + "…"
            }
            if s.errors > 0 { line += "  ERRORS: \(s.errors)" }
            lastRead = s.readBytes
            lastWritten = s.writtenBytes
            lastVerified = s.verifiedBytes
        } else if st.phase == .analyzing || st.phase == .proxies {
            line = String(format: "%@ %d/%d  %3.0f%%  %@", st.phase.rawValue, st.clipIndex + 1, st.clipCount, st.clipFraction * 100, st.clipName)
            if st.safeToEject { line += "  (card safe to eject)" }
        } else {
            line = st.phase.rawValue
        }
        fputs("\r\u{1B}[K" + line, stderr)
        lastTime = now
    }
}
