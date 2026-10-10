import ArgumentParser
import Darwin
import Foundation
import OffloadKit

@main
struct Offload: ParsableCommand {
    static let configuration = CommandConfiguration(
        abstract: "Camera card offload: read once, write to several disks, verify with xxh64.",
        version: OffloadVersion.string,
        subcommands: [Copy.self, Verify.self, Bench.self, Ports.self, Luts.self],
        defaultSubcommand: Copy.self)

    /// Errors of a command print as "Chyba: …" (Czech, as Windows). Parser errors (unknown option, a value that is
    /// not a number …) and --help stay as swift-argument-parser prints them — the library is not localised.
    static func main() {
        var command: ParsableCommand
        do {
            command = try parseAsRoot()
        } catch {
            Self.exit(withError: error)
        }
        do {
            try command.run()
        } catch let e as ValidationError {
            fputs(Msg.cliError + e.message + "\n", stderr)
            Darwin.exit(Self.exitCode(for: e).rawValue)
        } catch let e as ExitCode {
            Self.exit(withError: e)
        } catch let e as CleanExit {
            Self.exit(withError: e)
        } catch {
            fputs(Msg.cliError + "\(error)\n", stderr)
            Darwin.exit(1)
        }
    }
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

    @Option(help: "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload luts prints them) or a .cube path.")
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

    @Option(help: "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto.")
    var lutFallback: String?

    @Option(help: "Also copy every proxy to <folder>/<date>/<card name>/ (with --proxy; never overwrites).")
    var proxyFolder: String?

    func run() throws {
        guard chunkMb > 0 else { throw ValidationError(Msg.notPositiveNumber(String(chunkMb))) }
        if let a = frameAspect, !(a > 0) { throw ValidationError(Msg.notPositiveNumber(numberText(a))) }
        guard frameScale > 0 else { throw ValidationError(Msg.notPositiveNumber(numberText(frameScale))) }
        var config = JobConfig(source: abs(source), destRoots: dest.map(abs))
        config.cardName = name
        config.date = date
        config.chunkMB = chunkMb
        config.buffers = buffers
        config.verify = !noVerify
        config.allowSameDevice = allowSameDevice
        config.analyzeClips = !noAnalyze
        config.proxy.enabled = proxy
        // An empty --lut "" is "no LUT" (LutOption.parse → none), not the current folder.
        let choice = LutOption.parse(lut, fallback: lutFallback, catalog: LutCatalog.shared)
        if let e = choice.error { throw ValidationError(e) }
        config.proxy.lutMode = choice.mode
        config.proxy.lutBuiltin = choice.builtin
        config.proxy.lutPath = choice.path.map(abs)
        config.proxy.lutFallback = choice.fallback
        if let p = config.proxy.lutPath, !FileManager.default.fileExists(atPath: p) {
            throw ValidationError(Msg.lutNotFound(p))
        }
        config.proxy.height = proxyHeight
        guard let codec = ProxyCodec(rawValue: proxyCodec) else {
            throw ValidationError(Msg.unknownProxyCodec(proxyCodec))
        }
        config.proxy.codec = codec
        config.proxy.bitrateMbps = proxyBitrate
        config.proxy.frameAspect = frameAspect
        config.proxy.frameScale = frameScale / 100
        config.proxy.burnFilename = !noBurnIn
        config.proxy.burnTimecode = !noBurnIn
        config.proxy.extraFolder = proxyFolder.map(abs)
        if config.proxy.extraFolder != nil && !proxy {
            fputs(Msg.proxyFolderNoEffect + "\n", stderr)
        }
        if let folder = ProxyPreflight.extraFolderInUse(config.proxy), !ProxyPreflight.isDirectory(folder) {
            throw ValidationError(Msg.proxyFolderNotFound(folder))
        }
        // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all (Windows LutOption.PlanLine).
        if proxy || config.analyzeClips { print("LUT: " + Copy.lutLine(config.proxy)) }

        let job = OffloadJob(config: config)
        let plan = try job.prepare()
        print("Card \(plan.cardName): \(plan.scan.files.count) files, \(formatBytes(plan.scan.totalBytes))")
        for p in plan.cardPaths { print("  → \(p)") }
        if let extra = ProxyPreflight.extraFolderInUse(config.proxy) {
            let target = URL(fileURLWithPath: extra).appendingPathComponent(plan.date).appendingPathComponent(plan.cardName).path
            print("  proxy also → \(target)")
        }
        let tools = config.tools.resolved()
        if config.analyzeClips || proxy {
            print("  ffmpeg: \(tools.ffmpeg ?? "NOT FOUND")")
        }
        // Connection chain: informational only — resolving it never fails the copy.
        var chain = [RoleLink(role: "Karta", link: DriveLinks.resolve(plan.source))]
        chain += plan.destRoots.enumerated().map { RoleLink(role: "Cíl \($0.offset + 1)", link: DriveLinks.resolve($0.element)) }
        let connectors = UsbScanner.scan().connectors
        // Status-line names first ("card", "D1"), so "brzdí: D1" there reads against this list.
        print("  Připojení:")
        for (i, l) in chain.enumerated() { print("    \(ProgressDisplay.shortName(i)) = \(l.role): \(l.link.describe(connectors))") }
        for a in Advisor.advise(chain, connectors: connectors) where a.kind == .warning { print("  ! \(a.text)") }
        fflush(stdout)
        if dryRun { return }

        signal(SIGINT, SIG_IGN)
        let sigint = DispatchSource.makeSignalSource(signal: SIGINT, queue: .global())
        sigint.setEventHandler {
            fputs("\n" + Msg.cancelling + "\n", stderr)
            job.cancel()
        }
        sigint.resume()

        let display = ProgressDisplay(job: job, chain: chain)
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
        if let e = status.error { print(Msg.cliError + e) }
        if status.phase != .done || report?.success != true { throw ExitCode(1) }
    }

    /// The "LUT: …" line before the copy (same wording as Windows).
    static func lutLine(_ p: ProxySettings) -> String {
        switch LutResolver.effectiveMode(p) {
        case .off: return "none"
        case .custom: return p.lutPath ?? ""
        case .builtin: return p.lutBuiltin ?? ""
        case .auto: return "auto" + (p.lutFallback.map { " (fallback \($0))" } ?? "")
        }
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
        guard gb > 0 else { throw ValidationError(Msg.notPositiveNumber(numberText(gb))) }
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

struct Ports: ParsableCommand {
    static let configuration = CommandConfiguration(
        abstract: "Show USB ports, where cards and disks are connected and how fast, with advice.")

    @Flag(help: "Also measure card read and destination write/read speed (temporary file, deleted).")
    var measure = false

    @Option(help: "Card or reader to check (repeatable).")
    var card: [String] = []

    @Option(help: "Destination to check (repeatable).")
    var dest: [String] = []

    @Option(help: "Amount of data per device when measuring, in GB.")
    var gb: Double = 1

    func run() throws {
        guard gb > 0 else { throw ValidationError(Msg.notPositiveNumber(numberText(gb))) }
        let cards = card.map(abs)
        var dests = dest.map(abs)
        for d in dests {
            if let c = cards.first(where: { HwTest.sameVolume($0, d) }) {
                fputs(Msg.cliError + "cíl \(d) je na stejném svazku jako karta \(c) — na kartu se nikdy nezapisuje\n", stderr)
                throw ExitCode(1)
            }
        }
        var byDrive = false
        if cards.isEmpty && dests.isEmpty {
            // No roles given: every volume, labelled by its name.
            if measure {
                fputs(Msg.cliError + "--measure needs --card and/or --dest\n", stderr)
                throw ExitCode(1)
            }
            dests = HwTest.allVolumes()
            byDrive = true
        }
        let flag = CancelFlag()
        signal(SIGINT, SIG_IGN)
        let sigint = DispatchSource.makeSignalSource(signal: SIGINT, queue: .global())
        sigint.setEventHandler {
            // Stops the measurement at once; the process stays until the temporary file is deleted.
            guard !flag.isCancelled else { return }
            fputs("\nPřerušeno — zastavuji měření a mažu dočasný soubor…\n", stderr)
            flag.cancel()
        }
        sigint.resume()
        let result = HwTest.run(cards: cards, destinations: dests, measure: measure, bytesPerDevice: Int64(gb * 1_000_000_000),
                                rolesByDrive: byDrive, isCancelled: { flag.isCancelled })
        printResult(result)
        if flag.isCancelled {
            print("\nMěření přerušeno — výsledek se neukládá.")
            throw ExitCode(1)
        }
        do {
            try HwTest.save(result, to: HwTest.defaultPath)
        } catch {
            fputs("Varování: výsledek testu nešlo uložit: \(error)\n", stderr)
        }
    }

    private func printResult(_ r: HwTestResult) {
        func pad(_ s: String, _ n: Int) -> String { s.count >= n ? s : s + String(repeating: " ", count: n - s.count) }
        print("Připojení:")
        for d in r.drives {
            // Listed by volume: the role is the volume name — no second column repeating it.
            let name = d.link.root == "/" ? "/" : (d.link.root as NSString).lastPathComponent
            let whereText = d.role == name ? pad(d.role, 8) : pad(d.role, 8) + " " + pad(d.link.root, 14)
            print("  \(whereText) \(d.link.describe(r.connectors))")
        }
        var groups: [(id: String, name: String, ports: [UsbConnector])] = []
        for c in r.connectors where c.userVisible {
            if let i = groups.firstIndex(where: { $0.id == c.controllerId }) {
                groups[i].ports.append(c)
            } else {
                groups.append((c.controllerId, c.controllerName, [c]))
            }
        }
        for g in groups {
            print("\nUSB porty (\(g.name)):")
            for c in g.ports { print("  \(PortLabels.listRow(c, drives: r.drives))") }
        }
        if !r.measurements.isEmpty {
            print("\nMěření:")
            for m in r.measurements { print("  \(pad(m.role, 8)) \(pad(m.root, 14)) čtení \(mb(m.readMBps))  zápis \(mb(m.writeMBps))") }
        }
        print("\nDoporučení:")
        for a in r.advice { print("  \(a.kind == .warning ? "!" : a.kind == .info ? "i" : "·") \(a.text)") }
    }

    private func mb(_ v: Double?) -> String { v.map { String(format: "%.0f MB/s", $0) } ?? "—" }
}

struct Luts: ParsableCommand {
    static let configuration = CommandConfiguration(abstract: "List the built-in LUTs (ids for --lut and --lut-fallback).")

    func run() throws {
        let cat = LutCatalog.shared
        if cat.luts.isEmpty { fputs(Msg.noBuiltinLuts + "\n", stderr) }
        for l in cat.luts { print("\(l.id) — \(l.name)") }
    }
}

/// A number as it was most likely typed: "0", "-2", "1.5" (for Msg.notPositiveNumber).
func numberText(_ v: Double) -> String { Swift.abs(v) < 1e15 && v == v.rounded() ? String(Int64(v)) : String(v) }

func abs(_ p: String) -> String {
    URL(fileURLWithPath: (p as NSString).expandingTildeInPath).standardizedFileURL.path
}

/// One status line on stderr, refreshed twice a second.
final class ProgressDisplay {
    let job: OffloadJob
    private let timer = DispatchSource.makeTimerSource(queue: .global())
    private let brakes: BrakeMeter
    private let started = Date()
    private var lastTime = Date()
    private var lastRead: Int64 = 0
    private var lastWritten: [Int64] = []
    private var lastVerified: [Int64] = []
    private var lastPhase = ""

    /// chain: the drives in the order card, D1, D2 … (for the "linka plná" check).
    init(job: OffloadJob, chain: [RoleLink]? = nil) {
        self.job = job
        brakes = BrakeMeter(chunkBytes: Int64(job.config.chunkMB) << 20, bufferCount: job.config.buffers,
                            linkMBps: chain?.map(ChainView.linkCeiling) ?? [])
    }

    /// The status-line name of a chain element: "card", "D1", "D2" …
    static func shortName(_ element: Int) -> String { element == 0 ? "card" : "D\(element)" }

    func start() {
        timer.schedule(deadline: .now() + 0.5, repeating: 0.5)
        timer.setEventHandler { [weak self] in self?.tick() }
        timer.resume()
    }

    func stop() {
        timer.cancel()
        fputs("\r\u{1B}[K", stderr)
    }

    /// Which element sets the pace — decided from the backlog (BrakeMeter), not from the rates on this line.
    private func brake(_ s: ProgressSnapshot) -> String {
        guard let b = brakes.add(seconds: Date().timeIntervalSince(started), readBytes: s.readBytes, writtenBytes: s.writtenBytes) else { return "" }
        return "brzdí: \(ProgressDisplay.shortName(b.element))\(b.linkFull ? " (linka plná)" : "")  "
    }

    private func tick() {
        let st = job.status()
        let now = Date()
        let dt = max(0.001, now.timeIntervalSince(lastTime))
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
                // The brake goes before the file name: a narrow terminal cuts the end of the line.
                line += "  " + brake(s) + (s.currentFile as NSString).lastPathComponent
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
