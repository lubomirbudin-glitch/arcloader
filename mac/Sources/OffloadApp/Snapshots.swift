#if DEBUG
import AppKit
import OffloadKit
import SwiftUI

/// Renders the views offscreen to PNG files for checking the look against docs/ui-navrh/screeny without opening a
/// window (`OffloadApp --ui-snapshots <folder>`, debug builds only). Nothing touches a disk but the PNG files: the model
/// is a preview model (no scans, no timers, settings not saved) filled with made-up cards, disks and jobs.
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
         ("settings-proxy", { settings(.proxy) }),
         ("settings-folders", { settings(.folders) }),
         ("settings-decoders", { settings(.decoders) })]
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
