import AppKit
import SwiftUI

// The look of docs/ui-navrh (README "Design tokens", mockup/*.html), as on Windows (Themes/Dark.xaml, Light.xaml):
// one palette per system appearance, system fonts (SF Pro, SF Mono), the shared sizes and a few small controls.

/// Every colour of the UI. Same tokens in both sets; the light set is the one Windows derived (contrast-checked there).
struct Palette {
    let bg, bgHeader, panel, raised, raisedAlt, input: Color
    let line, lineSoft, lineStrong, lineInput, track: Color
    let text, text2, text3, textSoft: Color
    let accent, accentHover, accentText, accentBar, accentSoft, accentSoft2, accentBorder, pillRunText, onAccent: Color
    let success, successFill, onSuccess, successBg, successBorder, successLine, successText2, successLabel: Color
    let successBtnBg, successBtnBorder, checkBg: Color
    let warn, warnFill, onWarn, warnBg, warnBorder, warnText2, warnListBg, warnListBorder: Color
    let warnSoftBg, warnSoftBorder, warnSoftText: Color
    let danger, dangerText, dangerText2, dangerBorder, dangerBg, dangerCardBorder: Color
    let diskUsed, chip, tabActive, dashedLine, runBorder, runBg, rowLine, flowLine, navActive: Color
    let segmentBg, segmentActive, thumbBg, hoverBg, previewBg, previewText, shade, frameLine: Color

    static let dark = Palette(
        bg: hex(0x0E1012), bgHeader: hex(0x121518), panel: hex(0x14171A), raised: hex(0x1B1F23), raisedAlt: hex(0x171A1E),
        input: hex(0x0F1215),
        line: hex(0x262B31), lineSoft: hex(0x22272C), lineStrong: hex(0x2E343B), lineInput: hex(0x323840), track: hex(0x2A3038),
        text: hex(0xECEFF2), text2: hex(0xA7AFB8), text3: hex(0x808994), textSoft: hex(0xC9D0D7),
        accent: hex(0x2366D1), accentHover: hex(0x2B72E0), accentText: hex(0x7DB6FF), accentBar: hex(0x5AA2FF),
        accentSoft: hex(0x1E3354), accentSoft2: hex(0x172235), accentBorder: hex(0x3A6FBF), pillRunText: hex(0xA9CEFF),
        onAccent: hex(0xFFFFFF),
        success: hex(0x42D392), successFill: hex(0x42D392), onSuccess: hex(0x0B1F15), successBg: hex(0x111A16),
        successBorder: hex(0x1F4A36), successLine: hex(0x1C3328), successText2: hex(0x9FD9BC), successLabel: hex(0x8FA89B),
        successBtnBg: hex(0x16201B), successBtnBorder: hex(0x2B3A33), checkBg: hex(0x163A2A),
        warn: hex(0xF2B04A), warnFill: hex(0xF2B04A), onWarn: hex(0x2A1A05), warnBg: hex(0x1A1510), warnBorder: hex(0x5A3A1E),
        warnText2: hex(0xE8C891), warnListBg: hex(0x120F0B), warnListBorder: hex(0x3A2A18),
        warnSoftBg: hex(0x2A2418), warnSoftBorder: hex(0x3B3426), warnSoftText: hex(0xF2C77A),
        danger: hex(0xFF7D6B), dangerText: hex(0xFF9A8A), dangerText2: hex(0xFFB4A6), dangerBorder: hex(0x3F2A27),
        dangerBg: hex(0x1C1312), dangerCardBorder: hex(0x5A2A24),
        diskUsed: hex(0x5D6670), chip: hex(0x23292F), tabActive: hex(0x1F2429), dashedLine: hex(0x343B43),
        runBorder: hex(0x2B3B55), runBg: hex(0x13171C), rowLine: hex(0x1D2125), flowLine: hex(0x3A424B), navActive: hex(0x1F2933),
        segmentBg: hex(0x171A1E), segmentActive: hex(0x2E3640), thumbBg: hex(0x26302B), hoverBg: hex(0x22272C),
        previewBg: hex(0x2A2F33), previewText: hex(0x6F7882), shade: Color.black.opacity(0.6), frameLine: Color.white.opacity(0.55))

    static let light = Palette(
        bg: hex(0xF3F5F7), bgHeader: hex(0xFFFFFF), panel: hex(0xFFFFFF), raised: hex(0xF7F8FA), raisedAlt: hex(0xF1F3F5),
        input: hex(0xFFFFFF),
        line: hex(0xDCE1E6), lineSoft: hex(0xE6E9ED), lineStrong: hex(0xC8CFD6), lineInput: hex(0xC3CAD2), track: hex(0xE1E5EA),
        text: hex(0x15191D), text2: hex(0x4B545E), text3: hex(0x5E6873), textSoft: hex(0x3A424B),
        accent: hex(0x2366D1), accentHover: hex(0x1D5BC0), accentText: hex(0x1A56B8), accentBar: hex(0x2F7BE6),
        accentSoft: hex(0xE2ECFB), accentSoft2: hex(0xEEF4FD), accentBorder: hex(0x7FA8E6), pillRunText: hex(0x1A56B8),
        onAccent: hex(0xFFFFFF),
        success: hex(0x147A4B), successFill: hex(0x2BBF7A), onSuccess: hex(0x06281A), successBg: hex(0xEEF8F2),
        successBorder: hex(0xA9DDBF), successLine: hex(0xCDEBD9), successText2: hex(0x1E6B45), successLabel: hex(0x4F6B5D),
        successBtnBg: hex(0xFFFFFF), successBtnBorder: hex(0xA9DDBF), checkBg: hex(0xDDF3E7),
        warn: hex(0x8F5600), warnFill: hex(0xF2B04A), onWarn: hex(0x2A1A05), warnBg: hex(0xFFF6E6), warnBorder: hex(0xEBC27F),
        warnText2: hex(0x7A4D0A), warnListBg: hex(0xFFFBF3), warnListBorder: hex(0xF1D7A8),
        warnSoftBg: hex(0xFFF1D6), warnSoftBorder: hex(0xEBC27F), warnSoftText: hex(0x7A4D0A),
        danger: hex(0xC2382B), dangerText: hex(0xB42318), dangerText2: hex(0xA3261A), dangerBorder: hex(0xF0B4AC),
        dangerBg: hex(0xFFF1EF), dangerCardBorder: hex(0xF0B4AC),
        diskUsed: hex(0xA3ACB6), chip: hex(0xEBEEF1), tabActive: hex(0xE6EAEE), dashedLine: hex(0xC3CAD2),
        runBorder: hex(0x9DB9E6), runBg: hex(0xFFFFFF), rowLine: hex(0xE6E9ED), flowLine: hex(0xC3CAD2), navActive: hex(0xE2ECFB),
        segmentBg: hex(0xEBEEF1), segmentActive: hex(0xFFFFFF), thumbBg: hex(0xDDE3E0), hoverBg: hex(0xE9EDF1),
        previewBg: hex(0x2A2F33), previewText: hex(0x8A939C), shade: Color.black.opacity(0.6), frameLine: Color.white.opacity(0.55))

    static func of(_ scheme: ColorScheme) -> Palette { scheme == .dark ? dark : light }

    private static func hex(_ v: UInt32) -> Color {
        Color(.sRGB, red: Double(v >> 16 & 0xFF) / 255, green: Double(v >> 8 & 0xFF) / 255, blue: Double(v & 0xFF) / 255)
    }
}

private struct PaletteKey: EnvironmentKey {
    static let defaultValue = Palette.dark
}

extension EnvironmentValues {
    var palette: Palette {
        get { self[PaletteKey.self] }
        set { self[PaletteKey.self] = newValue }
    }
}

/// The palette follows the system appearance (dark / light) of the window it is in.
private struct Themed: ViewModifier {
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        let p = Palette.of(scheme)
        content.environment(\.palette, p).tint(p.accentBar)
    }
}

extension View {
    func themed() -> some View { modifier(Themed()) }
}

// MARK: - Sizes and type

enum Metrics {
    static let tileRadius: CGFloat = 12
    static let cardRadius: CGFloat = 16
    static let buttonRadius: CGFloat = 8
    static let primaryRadius: CGFloat = 10
    static let leftPadding: CGFloat = 22
    static let sectionGap: CGFloat = 26
    static let tileGap: CGFloat = 10
    static let leftMin: CGFloat = 380
    static let leftIdeal: CGFloat = 400
    static let leftMax: CGFloat = 460
}

extension Font {
    /// Base 14, captions 13, small 12 (README "Typography"); SF Mono for paths, files, speeds, hashes.
    static func ui(_ size: CGFloat = 14, _ weight: Font.Weight = .regular) -> Font { .system(size: size, weight: weight) }
    static func mono(_ size: CGFloat = 13, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .monospaced)
    }
}

// MARK: - Small pieces

/// "1 · CARDS": 12 semibold, upper case, tracked, Text2 — with an optional link on the right.
struct StepTitle<Trailing: View>: View {
    @Environment(\.palette) private var p
    let title: String
    @ViewBuilder var trailing: () -> Trailing

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(title.uppercased())
                .font(.ui(12, .semibold))
                .tracking(0.96)
                .foregroundStyle(p.text2)
            Spacer(minLength: 8)
            trailing()
        }
    }
}

extension StepTitle where Trailing == EmptyView {
    init(_ title: String) {
        self.title = title
        trailing = { EmptyView() }
    }
}

/// A blue text button ("From folder…", "+ Add disk", "Edit settings").
struct LinkButton: View {
    @Environment(\.palette) private var p
    let title: String
    var systemImage: String?
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 4) {
                if let systemImage { Image(systemName: systemImage).font(.system(size: 11, weight: .semibold)) }
                Text(title)
            }
            .font(.ui(13))
            .foregroundStyle(p.accentText)
            .padding(.vertical, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// Buttons of the new look: primary (blue, 48 high in the start bar), secondary (raised), ghost (top bar),
/// danger outline (Cancel), success (Eject card) and warning-soft.
struct OffloadButtonStyle: ButtonStyle {
    enum Kind { case primary, secondary, ghost, danger, success, successSoft, warnSoft }
    @Environment(\.palette) private var p
    @Environment(\.isEnabled) private var enabled
    var kind: Kind = .secondary
    var height: CGFloat = 34
    var fullWidth = false

    func makeBody(configuration: Configuration) -> some View {
        let (bg, fg, border) = colors(pressed: configuration.isPressed)
        configuration.label
            .font(.ui(kind == .primary ? 15 : 13, kind == .primary || kind == .success ? .semibold : .medium))
            .foregroundStyle(fg)
            .padding(.horizontal, kind == .primary ? 18 : 12)
            .frame(maxWidth: fullWidth ? .infinity : nil, minHeight: height)
            .background(RoundedRectangle(cornerRadius: radius).fill(bg))
            .overlay(RoundedRectangle(cornerRadius: radius).strokeBorder(border, lineWidth: 1))
            .contentShape(RoundedRectangle(cornerRadius: radius))
            .opacity(enabled || kind == .primary ? 1 : 0.5)
    }

    private var radius: CGFloat { kind == .primary || kind == .success ? Metrics.primaryRadius : Metrics.buttonRadius }

    private func colors(pressed: Bool) -> (Color, Color, Color) {
        switch kind {
        case .primary:
            return enabled ? (pressed ? p.accentHover : p.accent, p.onAccent, .clear) : (p.raised, p.text3, p.line)
        case .secondary: return (pressed ? p.hoverBg : p.raised, p.text, p.lineStrong)
        case .ghost: return (pressed ? p.hoverBg : .clear, p.textSoft, .clear)
        case .danger: return (pressed ? p.dangerBg : .clear, p.dangerText, p.dangerBorder)
        case .success: return (p.successFill.opacity(pressed ? 0.85 : 1), p.onSuccess, .clear)
        case .successSoft: return (pressed ? p.successLine : p.successBtnBg, p.text, p.successBtnBorder)
        case .warnSoft: return (p.warnSoftBg, p.warnSoftText, p.warnSoftBorder)
        }
    }
}

extension ButtonStyle where Self == OffloadButtonStyle {
    static func offload(_ kind: OffloadButtonStyle.Kind, height: CGFloat = 34, fullWidth: Bool = false) -> OffloadButtonStyle {
        OffloadButtonStyle(kind: kind, height: height, fullWidth: fullWidth)
    }
}

/// A rounded pill ("Waiting in queue", "● Running 1 offload").
struct Pill: View {
    let text: String
    let fg: Color
    let bg: Color
    var dot: Color?

    var body: some View {
        HStack(spacing: 7) {
            if let dot { Circle().fill(dot).frame(width: 7, height: 7) }
            Text(text).font(.ui(12, .semibold))
        }
        .foregroundStyle(fg)
        .padding(.horizontal, 10).padding(.vertical, 4)
        .background(Capsule().fill(bg))
    }
}

/// A thin progress bar; `segments` draws several fills one after another (used / needed space).
struct ThinBar: View {
    @Environment(\.palette) private var p
    var height: CGFloat = 6
    var segments: [(Double, Color)]
    var track: Color?

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(track ?? p.track)
                HStack(spacing: 0) {
                    ForEach(Array(segments.enumerated()), id: \.offset) { _, s in
                        Rectangle().fill(s.1).frame(width: max(0, min(1, s.0)) * g.size.width)
                    }
                }
                .clipShape(Capsule())
            }
        }
        .frame(height: height)
    }
}

/// A checkbox in the palette's blue (17 px, as the mockup's `accent-color`). Pure SwiftUI: also renders offscreen.
struct OffloadCheckboxStyle: ToggleStyle {
    @Environment(\.palette) private var p
    @Environment(\.isEnabled) private var enabled

    func makeBody(configuration: Configuration) -> some View {
        Button { configuration.isOn.toggle() } label: {
            HStack(spacing: 10) {
                ZStack {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(configuration.isOn ? p.accentBar : p.input)
                    RoundedRectangle(cornerRadius: 4)
                        .strokeBorder(configuration.isOn ? Color.clear : p.lineInput, lineWidth: 1)
                    if configuration.isOn {
                        Image(systemName: "checkmark").font(.system(size: 10, weight: .heavy)).foregroundStyle(Color.white)
                    }
                }
                .frame(width: 17, height: 17)
                configuration.label.foregroundStyle(p.text)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .opacity(enabled ? 1 : 0.5)
    }
}

/// The 42×24 switch of the settings page ("On").
struct OffloadSwitchStyle: ToggleStyle {
    @Environment(\.palette) private var p

    func makeBody(configuration: Configuration) -> some View {
        Button { configuration.isOn.toggle() } label: {
            HStack(spacing: 10) {
                configuration.label.font(.ui(13)).foregroundStyle(p.textSoft)
                ZStack(alignment: configuration.isOn ? .trailing : .leading) {
                    Capsule().fill(configuration.isOn ? p.accent : p.track)
                    Circle().fill(Color.white).frame(width: 18, height: 18).padding(3)
                }
                .frame(width: 42, height: 24)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

extension ToggleStyle where Self == OffloadCheckboxStyle {
    static var offloadCheckbox: OffloadCheckboxStyle { OffloadCheckboxStyle() }
}

extension ToggleStyle where Self == OffloadSwitchStyle {
    static var offloadSwitch: OffloadSwitchStyle { OffloadSwitchStyle() }
}

/// A segmented choice that wraps on a narrow window (codec, height).
struct Segmented<T: Hashable>: View {
    @Environment(\.palette) private var p
    let options: [T]
    let label: (T) -> String
    @Binding var selection: T

    var body: some View {
        FlowLayout(spacing: 4) {
            ForEach(options, id: \.self) { o in
                let on = o == selection
                Button { selection = o } label: {
                    Text(label(o))
                        .font(.ui(13, on ? .semibold : .regular))
                        .foregroundStyle(on ? p.text : p.text2)
                        .padding(.horizontal, 12)
                        .frame(height: 32)
                        .background(RoundedRectangle(cornerRadius: 7).fill(on ? p.segmentActive : .clear))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .padding(3)
        .background(RoundedRectangle(cornerRadius: 9).fill(p.segmentBg))
        .overlay(RoundedRectangle(cornerRadius: 9).strokeBorder(p.line, lineWidth: 1))
        .fixedSize(horizontal: false, vertical: true)
    }
}

/// Lays its children out in rows, wrapping when the width runs out (macOS 13 `Layout`).
struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(proposal.width ?? .infinity, subviews)
        let width = rows.map(\.width).max() ?? 0
        let height = rows.map(\.height).reduce(0, +) + spacing * CGFloat(max(0, rows.count - 1))
        return CGSize(width: width, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for row in arrange(bounds.width, subviews) {
            var x = bounds.minX
            for i in row.items {
                let s = subviews[i].sizeThatFits(.unspecified)
                subviews[i].place(at: CGPoint(x: x, y: y + (row.height - s.height) / 2), proposal: ProposedViewSize(s))
                x += s.width + spacing
            }
            y += row.height + spacing
        }
    }

    private struct Row { var items: [Int] = []; var width: CGFloat = 0; var height: CGFloat = 0 }

    private func arrange(_ maxWidth: CGFloat, _ subviews: Subviews) -> [Row] {
        var rows: [Row] = []
        var row = Row()
        for (i, v) in subviews.enumerated() {
            let s = v.sizeThatFits(.unspecified)
            let needed = row.items.isEmpty ? s.width : row.width + spacing + s.width
            if needed > maxWidth && !row.items.isEmpty {
                rows.append(row)
                row = Row()
            }
            row.width = row.items.isEmpty ? s.width : row.width + spacing + s.width
            row.height = max(row.height, s.height)
            row.items.append(i)
        }
        if !row.items.isEmpty { rows.append(row) }
        return rows
    }
}

/// A rounded tile with a 1 px border.
struct Tile<Content: View>: View {
    var fill: Color
    var border: Color
    var radius: CGFloat = Metrics.tileRadius
    var padding = EdgeInsets(top: 12, leading: 14, bottom: 12, trailing: 14)
    @ViewBuilder var content: () -> Content

    var body: some View {
        content()
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: radius).fill(fill))
            .overlay(RoundedRectangle(cornerRadius: radius).strokeBorder(border, lineWidth: 1))
    }
}

/// A number in a circle (destination order).
struct NumberBadge: View {
    @Environment(\.palette) private var p
    let number: Int
    var size: CGFloat = 24
    var fill: Color?

    var body: some View {
        Text("\(number)")
            .font(.ui(size > 20 ? 12 : 11, .semibold))
            .foregroundStyle(p.text)
            .frame(width: size, height: size)
            .background(Circle().fill(fill ?? p.track))
    }
}

/// A square icon tile (card glyph, flow steps, status shield).
struct IconTile: View {
    let systemImage: String
    var size: CGFloat = 38
    var radius: CGFloat = 9
    var iconSize: CGFloat = 18
    let fg: Color
    let bg: Color
    var border: Color = .clear

    var body: some View {
        Image(systemName: systemImage)
            .font(.system(size: iconSize, weight: .regular))
            .foregroundStyle(fg)
            .frame(width: size, height: size)
            .background(RoundedRectangle(cornerRadius: radius).fill(bg))
            .overlay(RoundedRectangle(cornerRadius: radius).strokeBorder(border, lineWidth: 1))
    }
}

/// Lets the window be dragged by the custom top bar (the title bar is hidden); a double click zooms like a title bar.
struct WindowDragArea: NSViewRepresentable {
    final class DragView: NSView {
        override var mouseDownCanMoveWindow: Bool { true }
        override func mouseDown(with event: NSEvent) {
            if event.clickCount == 2 {
                window?.performZoom(nil)
            } else {
                window?.performDrag(with: event)
            }
        }
    }

    func makeNSView(context: Context) -> NSView { DragView() }
    func updateNSView(_ nsView: NSView, context: Context) {}
}

// MARK: - Formatting (English, three significant digits like the mockup: "1.70 GB", "14.2 GB", "412 GB")

enum Fmt {
    static func bytes(_ b: Int64) -> String {
        let units = ["B", "KB", "MB", "GB", "TB", "PB"]
        var v = Double(b)
        var i = 0
        while v >= 999.5 && i < units.count - 1 { v /= 1000; i += 1 }
        if i == 0 { return "\(b) B" }
        let digits = v < 9.995 ? 2 : v < 99.95 ? 1 : 0
        return String(format: "%.\(digits)f %@", v, units[i])
    }

    static func rate(_ bytesPerSecond: Double) -> String { String(format: "%.0f MB/s", max(0, bytesPerSecond) / 1_000_000) }

    static func rate(_ bytes: Int64, _ seconds: Double) -> String {
        seconds > 0 ? rate(Double(bytes) / seconds) : "—"
    }

    /// "0:42", "12:05", "1:02:03".
    static func clock(_ seconds: Double) -> String {
        let i = seconds.isFinite && seconds > 0 ? Int(min(seconds, Double(Int32.max))) : 0
        return i >= 3600 ? String(format: "%d:%02d:%02d", i / 3600, i / 60 % 60, i % 60) : String(format: "%d:%02d", i / 60, i % 60)
    }

    /// "45 s", "6 min 12 s", "1 h 05 min".
    static func duration(_ seconds: Double) -> String {
        let s = Int((seconds.isFinite ? max(0, seconds) : 0).rounded())
        if s < 60 { return "\(s) s" }
        if s < 3600 { return "\(s / 60) min \(s % 60) s" }
        return String(format: "%d h %02d min", s / 3600, s / 60 % 60)
    }

    static func count(_ n: Int, _ one: String, _ many: String? = nil) -> String { "\(n) \(n == 1 ? one : many ?? one + "s")" }
}
