import OffloadKit
import SwiftUI

/// The bar over both columns (UI návrh, every screen): logo and name · running pill · Hardware with its status dot ·
/// Settings. The window's title bar is hidden; the traffic lights sit over the bar's left inset.
///
/// Not on the Mac yet (hidden, hooks for later): the project switcher ("Project  Film X ▾", Windows plan 8).
struct TopBar: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    @Environment(\.openWindow) private var openWindow
    /// Room for the window's traffic lights (the title bar is hidden).
    static let trafficLightsInset: CGFloat = 78

    var body: some View {
        HStack(spacing: 12) {
            HStack(spacing: 10) {
                Image(systemName: "arrow.down.to.line")
                    .font(.system(size: 14, weight: .bold))
                    .foregroundStyle(p.onAccent)
                    .frame(width: 28, height: 28)
                    .background(RoundedRectangle(cornerRadius: 7).fill(p.accent))
                Text("Offload").font(.ui(15, .semibold)).foregroundStyle(p.text)
            }
            // TODO(projects): the project switcher goes here once the Mac has projects (Windows plan 8, Mac task 56).
            Spacer(minLength: 20)
            let counts = model.queueCounts
            if let pill = TopBar.pillText(running: counts.running, paused: counts.paused, waiting: counts.waiting) {
                Pill(text: pill, fg: p.pillRunText, bg: p.accentSoft2, dot: p.accentBar)
                    .padding(.horizontal, 4)
                    .frame(height: 34)
            }
            Button { model.verifyBackup() } label: {
                HStack(spacing: 8) {
                    Image(systemName: "checkmark.shield").font(.system(size: 15))
                    Text("Verify backup")
                }
            }
            .buttonStyle(.offload(.ghost))
            .help("Verify backup… (⇧⌘O) — check a backup on a destination against its ASC MHL, without the card")
            HardwareButton(hw: model.hardware) { openWindow(id: "hardware") }
            Button { model.openSettings() } label: {
                Image(systemName: "slider.horizontal.3").font(.system(size: 15))
                    .frame(width: 34, height: 34)
            }
            .buttonStyle(.offload(.ghost))
            .help("Settings (⌘,)")
        }
        .padding(.leading, TopBar.trafficLightsInset)
        .padding(.trailing, 20)
        .padding(.vertical, 10)
        .frame(height: 54)
        .background(WindowDragArea())
        .background(p.bgHeader)
        .overlay(alignment: .bottom) { Rectangle().fill(p.line).frame(height: 1) }
    }

    /// "Running 1 offload · 1 paused · 1 queued", "Paused: 1 offload", "2 queued"; nil when nothing runs, stands or waits.
    static func pillText(running: Int, paused: Int = 0, waiting: Int) -> String? {
        var parts: [String] = []
        if running > 0 { parts.append("Running \(Fmt.count(running, "offload"))") }
        if paused > 0 { parts.append(running > 0 ? "\(paused) paused" : "Paused: \(Fmt.count(paused, "offload"))") }
        if waiting > 0 { parts.append("\(waiting) queued") }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }
}

/// "Hardware ●": green = the last port check found nothing to warn about, yellow = advice; no dot before a check.
struct HardwareButton: View {
    @ObservedObject var hw: HardwareModel
    @Environment(\.palette) private var p
    let open: () -> Void

    var body: some View {
        Button(action: open) {
            HStack(spacing: 8) {
                Image(systemName: "cpu").font(.system(size: 15))
                Text("Hardware")
                if let warnings = hw.lastWarnings {
                    Circle().fill(warnings == 0 ? p.success : p.warn).frame(width: 7, height: 7)
                }
            }
        }
        .buttonStyle(.offload(.ghost))
        .help("Check USB ports and disks (⌘U)" + (hw.lastTest == nil ? "" : " — " + hw.summary))
    }
}
