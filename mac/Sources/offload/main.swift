import ArgumentParser
import Darwin
import Foundation
import OffloadKit

@main
struct Offload: ParsableCommand {
    static let configuration = CommandConfiguration(
        commandName: Product.cli,
        abstract: "\(Product.name): camera card offload — read once, write to several disks, verify with xxh64.",
        version: OffloadVersion.string,
        subcommands: [Copy.self, Append.self, Verify.self, DayReportCommand.self, SummaryReportCommand.self, Bench.self, Ports.self, Luts.self],
        defaultSubcommand: Copy.self)

    /// Errors of a command print as "Error: …" (the Mac is English; Windows prints them in Czech). Parser errors
    /// (unknown option, a value that is not a number …) and --help stay as swift-argument-parser prints them.
    static func main() {
        // The renames (Offload → ArcLoader → mmMover): the old app folder is copied once, before anything reads it (never moved).
        AppSupport.migrateFromOffload { fputs("\(Product.cli): \($0)\n", stderr) }
        var command: ParsableCommand
        do {
            command = try parseAsRoot()
        } catch {
            Self.exit(withError: error)
        }
        do {
            try command.run()
        } catch let e as ValidationError {
            fflush(stdout)
            fputs(Msg.cliError + e.message + "\n", stderr)
            Darwin.exit(Self.exitCode(for: e).rawValue)
        } catch let e as ExitCode {
            Self.exit(withError: e)
        } catch let e as CleanExit {
            Self.exit(withError: e)
        } catch where String(reflecting: type(of: error)).hasPrefix("ArgumentParser.") {
            // --help comes back from parseAsRoot as the parser's own help command, whose run() throws a help request:
            // the parser prints it (before, it printed as "Error: CommandError(… helpRequested …)").
            Self.exit(withError: error)
        } catch {
            fflush(stdout)
            fputs(Msg.cliError + "\(error)\n", stderr)
            Darwin.exit(1)
        }
    }
}

/// Clip analysis and proxies — the same options for `copy` and `append`.
struct StageTwo: ParsableArguments {
    @Flag(help: "Skip clip analysis (metadata + thumbnails in the report).")
    var noAnalyze = false

    @Flag(help: "Render proxies after the copy; the copies are verified while they render (see --classic-verify).")
    var proxy = false

    @Option(help: "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (mmmover luts prints them) or a .cube path.")
    var lut: String?

    @Option(help: "Proxy height in pixels.")
    var proxyHeight = 1080

    @Option(help: "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.")
    var frameAspect: Double?

    @Option(help: "Frame lines size in % of the image (90 = 10% overscan).")
    var frameScale: Double = 100

    @Option(help: "Proxy codec: h264, hevc, proresProxy, proresLT, prores422, prores422HQ.")
    var proxyCodec = "h264"

    @Option(help: "Proxy bitrate (Mb/s) for H.264/HEVC.")
    var proxyBitrate = 10

    @Flag(help: "No burn-in of clip name and timecode.")
    var noBurnIn = false

    @Flag(help: "Render a proxy also when the original is lighter (H.264/HEVC no bigger than the proxy and at most its bitrate; by default such a clip gets no H.264/HEVC proxy — a ProRes proxy is always rendered; only when no LUT and no frame-lines crop is used).")
    var proxyAlways = false

    @Option(help: "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto.")
    var lutFallback: String?

    @Option(help: "Also copy every proxy to <folder>/<date>/<card name>/ (with --proxy; never overwrites).")
    var proxyFolder: String?

    @Flag(help: "Verify by reading every destination back after the copy, not while the proxies render.")
    var classicVerify = false

    @Option(help: "The destination verified through the proxies, 1… in the order of --dest (default: the fastest in the copy).")
    var primary: Int?

    @Flag(help: "Do not write the day report (<dest>/<date>/_REPORT/<date>_day_report.html/.pdf) after the job.")
    var noDayReport = false

    /// --primary as a 0-based index for `destinations` destinations; out of range: the message and exit 1.
    func primaryIndex(destinations: Int) throws -> Int? {
        do {
            return try VerifyOptions.primaryIndex(primary, destinations: destinations)
        } catch {
            fputs(Msg.cliError + "\(error)\n", stderr)
            throw ExitCode(1)
        }
    }

    func apply(to config: inout JobConfig) throws {
        if let a = frameAspect, !(a > 0) { throw ValidationError(Msg.notPositiveNumber(numberText(a))) }
        guard frameScale > 0 else { throw ValidationError(Msg.notPositiveNumber(numberText(frameScale))) }
        config.analyzeClips = !noAnalyze
        config.proxy.enabled = proxy
        config.dayReport = !noDayReport
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
        config.proxy.skipLighterSources = !proxyAlways
        config.proxy.extraFolder = proxyFolder.map(abs)
        // Plan 10: with proxies the copies verify while they render, unless --classic-verify.
        config.verifyInProxy = !classicVerify
        if config.proxy.extraFolder != nil && !proxy {
            fputs(Msg.proxyFolderNoEffect + "\n", stderr)
        }
        if let folder = ProxyPreflight.extraFolderInUse(config.proxy), !ProxyPreflight.isDirectory(folder) {
            throw ValidationError(Msg.proxyFolderNotFound(folder))
        }
    }
}

struct Copy: ParsableCommand {
    static let configuration = CommandConfiguration(abstract: "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.")

    @Argument(help: "Card mount point or source folder.")
    var source: String

    @Option(name: [.short, .long], help: "Destination root (repeat for each disk).")
    var dest: [String]

    @Option(help: "Card folder name (default: volume label); auto = the reel from the clip names (A001C003… → A001).")
    var name: String?

    @Flag(help: "Neither read nor write the card history (history.json).")
    var noHistory = false

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

    @OptionGroup var stage: StageTwo

    func run() throws {
        guard chunkMb > 0 else { throw ValidationError(Msg.notPositiveNumber(String(chunkMb))) }
        var config = JobConfig(source: abs(source), destRoots: dest.map(abs))
        config.cardName = name
        config.date = date
        config.recordHistory = !noHistory
        // The history line before the plan (no question — a script has nobody to answer it) and --name auto.
        if !noHistory || name?.lowercased() == "auto" {
            let insight = Copy.inspect(config.source, history: !noHistory)
            if !noHistory, let line = insight.flatMap(Copy.historyLine) { print(line) }
            if name?.lowercased() == "auto" {
                if let s = insight?.nameSuggestion {
                    config.cardName = s.name
                    print(Msg.cardNameAuto(s.name))
                } else {
                    config.cardName = nil
                    print(Msg.cardNameAutoNone)
                }
            }
        }
        config.chunkMB = chunkMb
        config.buffers = buffers
        config.verify = !noVerify
        config.allowSameDevice = allowSameDevice
        try stage.apply(to: &config)
        config.primaryDestination = try stage.primaryIndex(destinations: dest.count)
        try JobRun.run(config, dryRun: dryRun)
    }

    /// One look at the card (never written to): history match and the name from the clips. Nil when it cannot be read.
    static func inspect(_ source: String, history: Bool) -> CardInsight? {
        let store = history ? HistoryStore() : nil
        return try? CardInspector.inspect(source, store: store, history: store?.load())
    }

    /// The history line with full destination paths, or nil when history does not know the card.
    static func historyLine(_ insight: CardInsight) -> String? {
        guard let m = insight.match else { return nil }
        return insight.isEmptied ? HistoryText.emptied(m).0 : HistoryText.line(m, fullPaths: true).0
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

struct Append: ParsableCommand {
    static let configuration = CommandConfiguration(
        abstract: "Copy only the new files of a card into its earlier backup (same card folders, next ASC MHL generation).")

    @Argument(help: "Card mount point or source folder.")
    var source: String

    @Option(help: "Card folder of the earlier backup (<root>/<date>/<card>); repeat for each destination. Default: from the card history.")
    var into: [String] = []

    @Option(help: "Block size in MB.")
    var chunkMb = 16

    @Option(help: "Blocks in flight.")
    var buffers = 16

    @Flag(help: "Skip read-back verification (not recommended).")
    var noVerify = false

    @Flag(help: "Only scan and check, do not copy (the real run plans again under the folder lock, so it may differ).")
    var dryRun = false

    @Flag(help: "Do not read or write the card history (needs --into).")
    var noHistory = false

    @OptionGroup var stage: StageTwo

    func run() throws {
        guard chunkMb > 0 else { throw ValidationError(Msg.notPositiveNumber(String(chunkMb))) }
        var config = JobConfig(source: abs(source), destRoots: [])
        config.chunkMB = chunkMb
        config.buffers = buffers
        config.verify = !noVerify
        config.recordHistory = !noHistory
        try stage.apply(to: &config)
        var folders = into.map(abs)
        let insight = noHistory ? nil : Copy.inspect(config.source, history: true)
        if let line = insight.flatMap(Copy.historyLine) { print(line) }
        if folders.isEmpty {
            guard let known = insight?.match else { throw ValidationError(Msg.cardNotInHistory) }
            folders = known.entry.destinations.map(\.cardPath)
            config.project = known.entry.project
        }
        config.appendInto = folders
        config.primaryDestination = try stage.primaryIndex(destinations: folders.count)
        try JobRun.run(config, dryRun: dryRun)
    }
}

/// Runs a copy or an append job for the CLI: the plan, the connection chain, the progress line, the summary and the
/// exit code.
enum JobRun {
    /// "Card A001: 3 files, 1 GB" for a full copy; "Append to card A001 (generation 2), plan (may still change): 1 file · …".
    static func planLine(_ plan: OffloadPlan) -> String {
        plan.mode == .append
            ? "Append to card \(plan.cardName) (generation \(plan.generation)), plan (may still change): \(Texts.files(plan.scan.files.count, plan.scan.totalBytes))"
            : "Card \(plan.cardName): \(plan.scan.files.count) files, \(formatBytes(plan.scan.totalBytes))"
    }

    static func run(_ config: JobConfig, dryRun: Bool) throws {
        // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all (Windows LutOption.PlanLine).
        if config.proxy.enabled || config.analyzeClips { print("LUT: " + Copy.lutLine(config.proxy)) }
        let job = OffloadJob(config: config)
        var plan: OffloadPlan?
        do {
            plan = try job.prepare()
        } catch let e as OffloadError where e.code == .nothingNew {
            // Not an error — the run still refreshes history and heals the hash list, so it goes on (and plans again).
            if dryRun {
                print(e.description)
                return
            }
        }
        var chain: [RoleLink]?
        if let plan {
            print(planLine(plan))
            for p in plan.cardPaths { print("  → \(p)") }
            if plan.unverifiedEarlier > 0 { print(Msg.cliWarning + Msg.unverifiedEarlier(plan.unverifiedEarlier)) }
            // R11: less than 5 % left never blocks — a warning, and the copy goes on.
            for w in job.engine.spaceWarnings { print(Msg.cliWarning + w) }
            if dryRun {
                for l in job.engine.spaceLines { print(Msg.spaceLine(l.label, total: l.need.total, parts: l.need.partsText("card"), free: l.free)) }
            }
            if let extra = ProxyPreflight.extraFolderInUse(config.proxy) {
                let target = URL(fileURLWithPath: extra).appendingPathComponent(plan.date).appendingPathComponent(plan.cardName).path
                print("  proxy also → \(target)")
            }
            if config.analyzeClips || config.proxy.enabled {
                print("  ffmpeg: \(config.tools.resolved().ffmpeg ?? "NOT FOUND")")
            }
            if config.verify {
                print(job.verifiesInProxy(plan) ? "  Verify: during proxy render — safe to eject after the copy, safe to format after the verification"
                                                : "  Verify: read back after the copy")
            }
            // Connection chain: informational only — resolving it never fails the copy.
            var links = [RoleLink(role: RoleLink.card, link: DriveLinks.resolve(plan.source))]
            links += plan.destRoots.enumerated().map { RoleLink(role: RoleLink.destination($0.offset + 1), link: DriveLinks.resolve($0.element)) }
            let connectors = UsbScanner.scan().connectors
            // Status-line names first ("card", "D1"), so "bottleneck: D1" there reads against this list.
            print("  Connections:")
            for (i, l) in links.enumerated() { print("    \(ProgressDisplay.shortName(i)) = \(l.role): \(l.link.describe(connectors))") }
            for a in Advisor.advise(links, connectors: connectors) where a.kind == .warning { print("  ! \(a.text)") }
            chain = links
        }
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
        if let report, let plan = job.plan {
            print("\n" + report.summary)
            if !report.clips.isEmpty {
                let done = report.clips.filter { $0.proxyStatus == .done }.count
                let lighter = report.clips.filter(\.originalLighter).count
                print("Clips: \(report.clips.count)" + (config.proxy.enabled ? "  proxies OK \(done), failed \(status.proxiesFailed)"
                    + (lighter > 0 ? ", none (original lighter) \(lighter)" : "") : ""))
                for c in report.clips where c.proxyStatus == .failed || c.proxyStatus == .unsupported {
                    print("  \(c.clip.name): \(c.proxyStatus.rawValue) — \(c.proxyNote ?? "")")
                }
            }
            print("Report: \(plan.cardPaths[0])/\(Layout.reportDirName)/\(OffloadReport.htmlName(plan.cardName, plan.generation))")
            if status.safeToFormat {
                print(Texts.safeToFormat(plan.cardPaths.count))
            } else if status.safeToEject {
                print(Texts.safeToEject(plan.cardPaths.count))
            }
        }
        for day in job.writtenDayReports { print(Msg.dayReportWritten(day)) }
        if let n = status.note { print(n) }
        if let e = status.error { print(Msg.cliError + e) }
        if !HistoryRecorder.drain(timeout: 0) {
            // The history stayed busy for 60 s: the CLI does not wait in the background.
            print(Msg.cliWarning + Msg.historyNotWritten)
        } else if let w = status.warning, w != Msg.historyDelayed, !job.engine.spaceWarnings.contains(w) {
            print(Msg.cliWarning + w)
        }
        if VerifyOptions.exitCode(verify: config.verify, status, report) != 0 { throw ExitCode(1) }
    }
}

struct Verify: ParsableCommand {
    static let configuration = CommandConfiguration(
        abstract: "Verify a backup: a card folder (<root>/<date>/<card>) or a whole date folder, against every ASC MHL generation (or the _REPORT checksum lists).")

    @Argument(help: "Card folder (<root>/<date>/<card>) or a date folder (every card folder in it).")
    var folder: String

    @Flag(help: "Write the verification report (HTML, PDF, JSON) into each card's _REPORT folder.")
    var report = false

    func run() throws {
        let root = abs(folder)
        let cards = BackupVerifier.cardFolders(in: root)
        guard !cards.isEmpty else {
            throw OffloadError.preflight(Msg.noHashRecord(root + "/" + Layout.reportDirName), code: .noHashRecord)
        }
        let flag = CancelFlag()
        signal(SIGINT, SIG_IGN)
        let sigint = DispatchSource.makeSignalSource(signal: SIGINT, queue: .global())
        sigint.setEventHandler {
            fputs("\n" + Msg.cancelling + "\n", stderr)
            flag.cancel()
        }
        sigint.resume()
        var allGood = true
        for (i, card) in cards.enumerated() {
            if cards.count > 1 { print("\n\(i + 1)/\(cards.count)  \(card)") }
            let t0 = Date()
            var last = Date.distantPast
            let r: VerifyResult
            do {
                r = try BackupVerifier.verify(card, progress: { p in
                    if Date().timeIntervalSince(last) > 0.5 {
                        last = Date()
                        // "<file>/<files>  <bytes>  <path>" on one line.
                        fputs("\r\u{1B}[K\(p.fileIndex + 1)/\(p.fileCount)  \(formatBytes(p.bytes))  \(p.file)", stderr)
                    }
                }, isCancelled: { flag.isCancelled })
            } catch {
                fputs("\r\u{1B}[K", stderr)
                print(Msg.cliError + "\(error)")
                allGood = false
                continue
            }
            fputs("\r\u{1B}[K", stderr)
            for line in VerifyReport.machineLines(r) { print(line) }
            for w in VerifyReport.warnings(r) where !w.isEmpty { print(Msg.cliWarning + w) }
            print(VerifyReport.cliSummary(r, seconds: Date().timeIntervalSince(t0)))
            if report && !r.cancelled {
                do {
                    let (html, _, inside) = try VerifyReport.write(r, log: { fputs($0 + "\n", stderr) })
                    print(inside ? "Report: \(html)" : VerifyReport.reportOutsideNote(html))
                } catch {
                    print(Msg.cliWarning + Msg.reportNotWritten("\(error)"))
                }
            }
            if !r.success || r.unfinished || r.cancelled { allGood = false }
            if flag.isCancelled { break }
        }
        if !allGood { throw ExitCode(1) }
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
                fputs(Msg.cliError + "destination \(d) is on the same volume as card \(c) — nothing is ever written to a card\n", stderr)
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
            fputs("\nInterrupted — stopping the measurement and deleting the temporary file…\n", stderr)
            flag.cancel()
        }
        sigint.resume()
        let result = HwTest.run(cards: cards, destinations: dests, measure: measure, bytesPerDevice: Int64(gb * 1_000_000_000),
                                rolesByDrive: byDrive, isCancelled: { flag.isCancelled })
        printResult(result)
        if flag.isCancelled {
            print("\nMeasurement interrupted — the result is not saved.")
            throw ExitCode(1)
        }
        do {
            try HwTest.save(result, to: HwTest.defaultPath)
        } catch {
            fputs("Warning: could not save the test result: \(error)\n", stderr)
        }
    }

    private func printResult(_ r: HwTestResult) {
        func pad(_ s: String, _ n: Int) -> String { s.count >= n ? s : s + String(repeating: " ", count: n - s.count) }
        print("Connections:")
        for d in r.drives {
            // Listed by volume: the role is the volume name — no second column repeating it.
            let name = d.link.root == "/" ? "/" : (d.link.root as NSString).lastPathComponent
            let whereText = d.role == name ? pad(d.role, 13) : pad(d.role, 13) + " " + pad(d.link.root, 14)
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
            print("\nUSB ports (\(g.name)):")
            for c in g.ports { print("  \(PortLabels.listRow(c, drives: r.drives))") }
        }
        if !r.measurements.isEmpty {
            print("\nMeasurement:")
            for m in r.measurements { print("  \(pad(m.role, 13)) \(pad(m.root, 14)) read \(mb(m.readMBps))  write \(mb(m.writeMBps))") }
        }
        print("\nAdvice:")
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

    /// chain: the drives in the order card, D1, D2 … (for the "link full" check).
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
        return "bottleneck: \(ProgressDisplay.shortName(b.element))\(b.linkFull ? " (link full)" : "")  "
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
            if st.safeToFormat { line += "  (card safe to format)" } else if st.safeToEject { line += "  (card safe to eject)" }
            if st.verifyTotal > 0 { line += String(format: "  verified %3.0f%%", Double(st.verifyDone) / Double(st.verifyTotal) * 100) }
        } else if st.phase == .finishingVerification {
            line = String(format: "finishing verification  %3.0f%%", Double(st.verifyDone) / Double(max(st.verifyTotal, 1)) * 100)
            if st.safeToEject { line += "  (card safe to eject)" }
        } else {
            line = st.phase.rawValue
        }
        fputs("\r\u{1B}[K" + line, stderr)
        lastTime = now
    }
}

struct DayReportCommand: ParsableCommand {
    static let configuration = CommandConfiguration(
        commandName: "day-report",
        abstract: "Write the day report of a day folder on a destination (<dest>/<date>, or a card folder in it).")

    @Argument(help: "Day folder (<dest>/<date>) or a card folder in it.")
    var folder: String

    func run() throws {
        var result: Result<String, Error>?
        // The PDF prints with WebKit on the main run loop: the report is made on a worker thread meanwhile.
        Thread.detachNewThread {
            let r = Result { try DayReportService.regenerate(abs(folder)) }
            DispatchQueue.main.async { result = r }
        }
        while result == nil { RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.1)) }
        switch result! {
        case .success(let html): print(Msg.dayReportWritten(html))
        case .failure(let e):
            fputs(Msg.cliError + "\(e)\n", stderr)
            throw ExitCode(1)
        }
    }
}

struct SummaryReportCommand: ParsableCommand {
    static let configuration = CommandConfiguration(
        commandName: "summary-report",
        abstract: "One summary report (HTML + PDF) from several backups: shooting days, clips, files, size, duration, first and last slate, cameras, verification, where each card is backed up. Always saved into ~/Documents/mmMover reports/summary.")

    @Argument(help: "Card folders (on a destination, or their copy in ~/Documents/mmMover reports — or Offload reporty of builds before the rename), date folders (every card in them), or history ids.")
    var backups: [String]

    @Option(name: [.short, .long], help: "Also save the summary into this folder (a suffix _2, _3 … instead of overwriting).")
    var output: String?

    func validate() throws {
        if backups.isEmpty { throw ValidationError(Msg.summaryNothing) }
    }

    func run() throws {
        var outcome: Result<SummaryFiles, Error>?
        let args = backups.map { FileManager.default.fileExists(atPath: $0) ? abs($0) : $0 }
        let extra = output.map(abs)
        // The PDF prints with WebKit on the main run loop: the report is made on a worker thread meanwhile.
        Thread.detachNewThread {
            let r = Result { () throws -> SummaryFiles in
                // History only when something is not a folder: an id (reading it never writes it).
                let history = args.allSatisfy { FileManager.default.fileExists(atPath: $0) } ? [] : HistoryStore().load()
                let model = SummaryReport.load(try SummaryReport.inputs(args, history: history))
                return try SummaryReport.save(model, extraFolder: extra, log: { fputs($0 + "\n", stderr) })
            }
            DispatchQueue.main.async { outcome = r }
        }
        while outcome == nil { RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.1)) }
        switch outcome! {
        case .success(let saved):
            print(Msg.summaryWritten(saved.html))
            if let pdf = saved.pdf { print(Msg.summaryWritten(pdf)) }
            if let h = saved.extraHTML { print(Msg.summaryExtraWritten(h)) }
            if let e = saved.extraError {
                fputs(Msg.cliError + Msg.summaryExtraFailed(e) + "\n", stderr)
                throw ExitCode(1)
            }
        case .failure(let e):
            fputs(Msg.cliError + "\(e)\n", stderr)
            throw ExitCode(1)
        }
    }
}
