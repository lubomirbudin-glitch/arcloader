#if DEBUG
import AppKit
import OffloadKit
import SwiftUI

/// Renders the views offscreen to PNG files for checking the look against docs/ui-navrh/screeny without opening a
/// window (`OffloadApp --ui-snapshots <folder>`, debug builds only). Nothing is written but the PNG files: the model
/// is a preview model (no scans, no timers, settings not saved) filled with made-up cards, disks and jobs; only the
/// Hardware dot reads the last saved port check.
@MainActor
enum Snapshots {
    static func run(to folder: String) {
        let app = NSApplication.shared
        app.setActivationPolicy(.prohibited)   // no Dock icon, no menu bar, never frontmost
        try? FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        for (name, make) in scenarios {
            for dark in [true, false] {
                for width in [1360, 1000] {
                    let model = make()
                    let file = "\(folder)/\(name)-\(width)-\(dark ? "dark" : "light").png"
                    render(ContentView().environmentObject(model), size: CGSize(width: width, height: 900), dark: dark, to: file)
                }
            }
        }
        for dark in [true, false] {
            render(PaletteSwatch(), size: CGSize(width: 900, height: 520), dark: dark, to: "\(folder)/palette-\(dark ? "dark" : "light").png")
        }
        print("snapshots: \(folder)")
    }

    /// An NSHostingView in a borderless window that is never ordered on screen; AppKit-backed controls (text fields,
    /// pop-up menus) draw too, which `ImageRenderer` leaves out on macOS.
    static func render<V: View>(_ view: V, size: CGSize, dark: Bool, to path: String) {
        let host = NSHostingView(rootView: view.frame(width: size.width, height: size.height))
        host.frame = CGRect(origin: .zero, size: size)
        let window = NSWindow(contentRect: host.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        window.contentView = host
        for _ in 0..<3 {
            host.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.15))
        }
        guard let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { return }
        host.cacheDisplay(in: host.bounds, to: rep)
        if let data = rep.representation(using: .png, properties: [:]) {
            try? data.write(to: URL(fileURLWithPath: path))
        }
        window.contentView = nil
    }

    static var scenarios: [(String, () -> AppModel)] {
        [("ready", ready),
         ("ready-warnings", readyWarnings),
         ("empty", base),
         ("running", running),
         ("finished", finished),
         ("settings-proxy", { settings(.proxy) }),
         ("settings-folders", { settings(.folders) }),
         ("settings-decoders", { settings(.decoders) })]
    }

    static func job(_ card: String, _ state: JobCardState) -> JobItem {
        let item = JobItem(job: OffloadJob(config: JobConfig(source: "/Volumes/" + card, destRoots: [raid, shuttle])),
                           sourceName: card, canEject: true)
        item.preview = state
        return item
    }

    /// obr. 2: a card copying (phase 1 of 4, speed table, slowest link), another waiting; no card in a reader.
    static func running() -> AppModel {
        let m = base()
        var run = JobCardState(kind: .running, phase: .copying, name: "A035_ARRI", sub: "24 files · 1.70 GB")
        run.percent = 38
        run.remaining = "~0:02 left · copy"
        run.steps = [PhaseStep(number: 1, label: "Copy", state: .active, fill: 0.38),
                     PhaseStep(number: 2, label: "Verify", state: .pending, fill: 0),
                     PhaseStep(number: 3, label: "Proxy", state: .pending, fill: 0),
                     PhaseStep(number: 4, label: "Report", state: .pending, fill: 0)]
        run.rows = [TransferRow(id: 0, name: "Card read", number: nil, share: 0.38, rate: "661 MB/s"),
                    TransferRow(id: 1, name: "RAID_A", number: 1, share: 0.38, rate: "661 MB/s"),
                    TransferRow(id: 2, name: "SHUTTLE_B", number: 2, share: 0.37, rate: "655 MB/s")]
        run.fileCaption = "File 10 / 24"
        run.fileValue = "A035C010_261007_R2KQ.mxf"
        run.brake = "Slowest link: card reader (USB 3 (5 Gb/s)). The disks could go faster."
        run.canCancel = true
        var wait = JobCardState(kind: .waiting, phase: .waiting, name: "B012_FX6", sub: "~14.2 GB · 2 disks")
        wait.canCancel = true
        var proxies = JobCardState(kind: .running, phase: .proxies, name: "A034_ARRI", sub: "212 files · 256 GB")
        proxies.percent = 61
        proxies.steps = [PhaseStep(number: 1, label: "Copy", state: .done, fill: 1),
                         PhaseStep(number: 2, label: "Verify", state: .done, fill: 1),
                         PhaseStep(number: 3, label: "Proxy", state: .active, fill: 0.61),
                         PhaseStep(number: 4, label: "Report", state: .pending, fill: 0)]
        proxies.fileCaption = "Proxy 15 / 24"
        proxies.fileValue = "A034C015_261007_R1AB.mxf · 40 %"
        proxies.safeWhileRunning = true
        proxies.showEject = true
        proxies.canCancel = true
        m.jobs = [job("A034_ARRI", proxies), job("A035_ARRI", run), job("B012_FX6", wait)]
        return m
    }

    /// obr. 3: one card done and verified (green), one with a file that doesn't match (yellow), a failed and a
    /// cancelled one.
    static func finished() -> AppModel {
        let m = base()
        var done = JobCardState(kind: .done, phase: .done, name: "A035_ARRI", sub: "24 files · 1.70 GB")
        done.title = "A035_ARRI — safe to eject"
        done.text = "All 24 files are on 2 disks and verified (xxHash64)."
        done.summary = [SummaryField(label: "Copy", value: "602 MB/s"), SummaryField(label: "Verify", value: "799 MB/s"),
                        SummaryField(label: "Proxy", value: "24 / 24"), SummaryField(label: "Total", value: "6 min 12 s")]
        done.thumbs = (1...5).map { ClipThumb(id: $0, clip: String(format: "C%03d", $0), image: nil) }
        done.moreThumbs = 19
        done.path = "…/2026-10-10/A035_ARRI/"
        done.showEject = true
        done.hasReport = true
        done.hasProxies = true
        var problem = JobCardState(kind: .problem, phase: .done, name: "B012_FX6", sub: "31 files · 14.2 GB")
        problem.title = "B012_FX6 — don't eject the card yet"
        problem.text = "1 file on SHUTTLE_B doesn't match the card. RAID_A is fine."
        problem.failed = [FailedFileRow(id: 0, path: "XDROOT/Clip/B012C015_261007_9QW1.MXF",
                                        disks: [.init(ok: true, text: "RAID_A ✓"), .init(ok: false, text: "SHUTTLE_B ✕ hash")])]
        problem.hasReport = true
        problem.path = "…/2026-10-10/B012_FX6/"
        var failed = JobCardState(kind: .failed, phase: .failed, name: "C007_VENICE", sub: "2 disks")
        failed.title = "C007_VENICE — offload failed"
        failed.text = "Not enough space on SHUTTLE_B: needs 412 GB, free 120 GB"
        var cancelled = JobCardState(kind: .cancelled, phase: .cancelled, name: "D002_BMPCC", sub: "2 disks")
        cancelled.title = "D002_BMPCC — cancelled"
        cancelled.text = "The card isn't fully backed up — copy it again in full."
        m.jobs = [job("D002_BMPCC", cancelled), job("C007_VENICE", failed), job("B012_FX6", problem), job("A035_ARRI", done)]
        return m
    }

    /// One destination nearly full, verification and proxies off, a folder source.
    static func readyWarnings() -> AppModel {
        let m = base()
        let a = card("A035_ARRI", used: 120 * gb)
        m.settings.destRoots = [shuttle]
        m.settings.verify = false
        m.settings.proxy.enabled = false
        m.previewState(volumes: [a], connections: [:], destinations: [
            shuttle: DestinationStatus(free: 100 * gb, total: 4 * tb, format: "exfat", exists: true, checking: false,
                                       connection: "USB 3 (10 Gb/s)"),
        ])
        m.manualSources = ["/Users/dit/Footage/B-cam day 3"]
        m.selectedSources = [a.path]
        m.cardNames[a.path] = "A035_ARRI_DAY3"
        return m
    }

    static func settings(_ s: SettingsSection) -> AppModel {
        let m = ready()
        m.settings.proxy.frameAspect = 2.39
        m.settings.proxy.frameScale = 0.9
        m.openSettings(s)
        return m
    }

    // MARK: Made-up data

    static let raid = "/Volumes/RAID_A"
    static let shuttle = "/Volumes/SHUTTLE_B"
    static let tb: Int64 = 1_000_000_000_000
    static let gb: Int64 = 1_000_000_000

    static func card(_ name: String, used: Int64, total: Int64 = 128_000_000_000, format: String = "exfat") -> VolumeItem {
        VolumeItem(name: name, path: "/Volumes/\(name)", total: total, free: total - used, format: format, looksLikeCard: true,
                   isDestination: false, identity: name, ejectProblem: nil, isWritable: true)
    }

    static func base() -> AppModel {
        let m = AppModel(preview: true)
        m.settings.destRoots = [raid, shuttle]
        m.settings.proxy.enabled = true
        m.previewState(volumes: [], connections: [:], destinations: [
            raid: DestinationStatus(free: 7_200 * gb, total: 8 * tb, format: "apfs", exists: true, checking: false,
                                    connection: "Thunderbolt"),
            shuttle: DestinationStatus(free: 1_920 * gb, total: 4 * tb, format: "exfat", exists: true, checking: false,
                                       connection: "USB 3 (10 Gb/s)"),
        ])
        return m
    }

    static func ready() -> AppModel {
        let m = base()
        let a = card("A035_ARRI", used: 1_700_000_000)
        let b = card("B012_FX6", used: 14_200_000_000)
        m.previewState(volumes: [a, b], connections: [a.path: "USB 3 (5 Gb/s)", b.path: "SD reader"],
                       destinations: m.destStatus)
        m.selectedSources = [a.path]
        return m
    }
}

/// Every token on its background — a quick check of both sets.
struct PaletteSwatch: View {
    var body: some View {
        SwatchBody().themed()
    }
}

private struct SwatchBody: View {
    @Environment(\.palette) private var p

    var body: some View {
        let rows: [(String, Color, Color)] = [
            ("Bg / Text", p.bg, p.text), ("Panel / Text2", p.panel, p.text2), ("Panel / Text3", p.panel, p.text3),
            ("Raised / TextSoft", p.raised, p.textSoft), ("Accent / OnAccent", p.accent, p.onAccent),
            ("AccentSoft / AccentText", p.accentSoft, p.accentText), ("SuccessBg / Success", p.successBg, p.success),
            ("SuccessFill / OnSuccess", p.successFill, p.onSuccess), ("WarnBg / Warn", p.warnBg, p.warn),
            ("WarnFill / OnWarn", p.warnFill, p.onWarn), ("DangerBg / DangerText", p.dangerBg, p.dangerText),
            ("Chip / TextSoft", p.chip, p.textSoft),
        ]
        VStack(alignment: .leading, spacing: 8) {
            ForEach(rows, id: \.0) { r in
                Text(r.0).font(.ui(14, .semibold)).foregroundStyle(r.2)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(10)
                    .background(RoundedRectangle(cornerRadius: 8).fill(r.1))
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(p.bg)
    }
}
#endif
