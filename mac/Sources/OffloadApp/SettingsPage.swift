import AppKit
import OffloadKit
import SwiftUI

/// The settings page (UI návrh obr. 4): it replaces the two columns; "‹ Back" / "Done" return to them.
/// Sections: Folders · Verify · Analysis · Proxy · Report · Decoders · Hardware. Every option the old left column had
/// lives here; the left column's step 3 shows a summary and opens the matching section.
///
/// Not on the Mac yet (hidden): projects ("Save as new project", the project name in the header), notifications,
/// update check, the checksum choice, day report / CSV / ALE outputs, LUT rules.
struct SettingsPage: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        let section = model.settingsSection ?? .folders
        VStack(spacing: 0) {
            header
            HStack(spacing: 0) {
                nav(section)
                ScrollView {
                    content(section)
                        .padding(.horizontal, 32).padding(.top, 26).padding(.bottom, 32)
                        .frame(maxWidth: 1100, alignment: .leading)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .background(p.bg)
            }
        }
    }

    private var header: some View {
        HStack(spacing: 14) {
            Button { model.closeSettings() } label: {
                HStack(spacing: 6) {
                    Image(systemName: "chevron.left").font(.system(size: 12, weight: .semibold))
                    Text("Back")
                }
            }
            .buttonStyle(.offload(.ghost))
            .keyboardShortcut(.cancelAction)
            Rectangle().fill(p.line).frame(width: 1, height: 22)
            VStack(alignment: .leading, spacing: 0) {
                Text("Offload").font(.ui(12)).foregroundStyle(p.text3)
                Text("Settings").font(.ui(14, .semibold)).foregroundStyle(p.text)
            }
            Spacer(minLength: 20)
            // TODO(projects): "Save as new project" once the Mac has projects (Windows plan 8).
            Button("Done") { model.closeSettings() }
                .buttonStyle(.offload(.primary, height: 34))
        }
        .padding(.leading, TopBar.trafficLightsInset)
        .padding(.trailing, 20)
        .padding(.vertical, 10)
        .frame(height: 54)
        .background(WindowDragArea())
        .background(p.bgHeader)
        .overlay(alignment: .bottom) { Rectangle().fill(p.line).frame(height: 1) }
    }

    private func nav(_ current: SettingsSection) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(SettingsSection.allCases) { s in
                let on = s == current
                Button { model.settingsSection = s } label: {
                    Text(s.title)
                        .font(.ui(14, on ? .semibold : .regular))
                        .foregroundStyle(on ? p.text : p.textSoft)
                        .padding(.horizontal, 12).padding(.vertical, 9)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(RoundedRectangle(cornerRadius: 8).fill(on ? p.navActive : .clear))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Spacer()
        }
        .padding(.horizontal, 14).padding(.vertical, 20)
        .frame(width: 220)
        .frame(maxHeight: .infinity)
        .background(p.panel)
        .overlay(alignment: .trailing) { Rectangle().fill(p.line).frame(width: 1) }
    }

    @ViewBuilder
    private func content(_ s: SettingsSection) -> some View {
        switch s {
        case .folders: FoldersSettings()
        case .verify: VerifySettings()
        case .analysis: AnalysisSettings()
        case .proxy: ProxySettingsView()
        case .report: ReportSettings()
        case .decoders: DecoderSettings()
        case .hardware: HardwareSettings(hw: model.hardware)
        }
    }
}

// MARK: - Building blocks

/// "Proxy" + one line under it, with an optional switch on the right.
struct SectionHeading: View {
    @Environment(\.palette) private var p
    let title: String
    let subtitle: String
    var isOn: Binding<Bool>?

    var body: some View {
        HStack(alignment: .center, spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.ui(22, .semibold)).foregroundStyle(p.text)
                Text(subtitle).font(.ui(14)).foregroundStyle(p.text2).fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 16)
            if let isOn { Toggle("On", isOn: isOn).toggleStyle(.offloadSwitch) }
        }
    }
}

/// A label 120 px wide on the left, the control on the right (README: "label left 120 px, control right").
struct FormRow<Content: View>: View {
    @Environment(\.palette) private var p
    let label: String
    @ViewBuilder var content: () -> Content

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 18) {
            Text(label).font(.ui(14)).foregroundStyle(p.text2).frame(width: 120, alignment: .leading)
            content().frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

/// A coloured one-line message (yellow warning, red problem).
struct Notice: View {
    @Environment(\.palette) private var p
    enum Kind { case warn, danger, info }
    let kind: Kind
    let text: String

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Image(systemName: kind == .info ? "info.circle" : "exclamationmark.triangle")
                .font(.system(size: 12))
            Text(text).fixedSize(horizontal: false, vertical: true)
        }
        .font(.ui(13))
        .foregroundStyle(kind == .warn ? p.warn : kind == .danger ? p.dangerText : p.text3)
    }
}

// MARK: - Sections

struct FoldersSettings: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            SectionHeading(title: "Folders", subtitle: "Where each card lands on every destination disk.")
            VStack(alignment: .leading, spacing: 16) {
                FormRow(label: "Structure") {
                    Text("<disk>/\(model.dateString)/<card name>/")
                        .font(.mono(13)).foregroundStyle(p.textSoft).textSelection(.enabled)
                }
                FormRow(label: "Date folder") {
                    HStack(spacing: 12) {
                        Toggle("Custom date", isOn: $model.useCustomDate).toggleStyle(.offloadCheckbox)
                        if model.useCustomDate {
                            DatePicker("", selection: $model.customDate, displayedComponents: .date)
                                .labelsHidden().fixedSize()
                        }
                    }
                }
                FormRow(label: "Card name") {
                    VStack(alignment: .leading, spacing: 6) {
                        Toggle("Suggest the card name from the clips", isOn: $model.settings.naming.suggestFromClips)
                            .toggleStyle(.offloadCheckbox)
                        Text("Set per card in step 1. Empty uses the reel of the clip names (A001C003… → A001), the next in line of the day (A002 after A001), or the card's own name.")
                            .font(.ui(12)).foregroundStyle(p.text3).fixedSize(horizontal: false, vertical: true)
                    }
                }
                FormRow(label: "Appearance") {
                    Picker("", selection: $model.settings.appearance) {
                        Text("Dark").tag("dark")
                        Text("Light").tag("light")
                        Text("System").tag("system")
                    }
                    .labelsHidden().pickerStyle(.segmented).fixedSize()
                }
                FormRow(label: "Disks") {
                    VStack(alignment: .leading, spacing: 6) {
                        Toggle("Add connected disks automatically", isOn: $model.settings.autoDestinations)
                            .toggleStyle(.offloadCheckbox)
                        Text("A writable external disk that doesn't look like a card joins the destinations when it's connected and leaves when it's unplugged. A disk you removed by hand isn't added again.")
                            .font(.ui(12)).foregroundStyle(p.text3).fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
        }
    }
}

struct VerifySettings: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            SectionHeading(title: "Verify", subtitle: "Every file is read back from each disk and compared with the card.",
                           isOn: $model.settings.verify)
            FormRow(label: "Checksum") {
                Text("xxHash64").font(.mono(13)).foregroundStyle(p.textSoft)
            }
            // TODO(hash): xxHash64 + MD5 once the Mac engine writes both (AppSettings.hash is kept, not used yet).
            if !model.settings.verify { Notice(kind: .warn, text: AfterCopyTexts.verifyOffWarning) }
            // Plan 10 (task 70): verify-in-proxy and its primary drive.
            FormRow(label: "During proxies") {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle("Verify the primary drive during proxy render", isOn: $model.settings.verifyInProxy)
                        .toggleStyle(.offloadCheckbox)
                    Text("With proxies the card is safe to eject right after the copy; every drive is read in full while the proxies render, and the card is safe to format once all are verified. Off, or without proxies: read back after the copy.")
                        .font(.ui(12)).foregroundStyle(p.text3).fixedSize(horizontal: false, vertical: true)
                }
            }
            FormRow(label: "Primary drive") {
                Picker("", selection: $model.settings.verifyPrimary) {
                    Text("Fastest in the copy").tag(VerifyOptions.fastest)
                    Text("First destination").tag(VerifyOptions.first)
                }
                .labelsHidden().pickerStyle(.segmented).fixedSize()
                .disabled(!model.settings.verifyInProxy)
            }
            FormRow(label: "Verify backup") {
                VStack(alignment: .leading, spacing: 10) {
                    Text("Checks an earlier backup on a destination against its ASC MHL, without the card (⇧⌘O).")
                        .font(.ui(12)).foregroundStyle(p.text3).fixedSize(horizontal: false, vertical: true)
                    HStack(spacing: 18) {
                        Button("Verify backup…") { model.verifyBackup() }.buttonStyle(.offload(.secondary))
                        Toggle("Save a verification report", isOn: $model.saveVerifyReport).toggleStyle(.offloadCheckbox)
                    }
                }
            }
        }
    }
}

struct AnalysisSettings: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            SectionHeading(title: "Analysis",
                           subtitle: "After verification: clip metadata and three frames per clip in the report.",
                           isOn: $model.settings.analyzeClips)
            FormRow(label: "Thumbnails") {
                Text("3 frames / clip (first, middle, last) · the proxy LUT applies").font(.ui(13))
            }
            if let w = ProxyPreflight.warning(model.settings.proxy, analyzeClips: model.settings.analyzeClips, resolved: model.tools) {
                Notice(kind: .warn, text: w)
            }
            if let p = model.lutProblem, !model.settings.proxy.enabled { Notice(kind: .danger, text: p) }
        }
    }
}

struct ReportSettings: View {
    @Environment(\.palette) private var p

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            SectionHeading(title: "Report", subtitle: "Always written — into the card folder on every disk and on this Mac.")
            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(ReportSettings.items.enumerated()), id: \.offset) { i, item in
                    HStack {
                        Image(systemName: "checkmark").font(.system(size: 11, weight: .bold)).foregroundStyle(p.success)
                            .frame(width: 20, height: 20)
                            .background(RoundedRectangle(cornerRadius: 6).fill(p.checkBg))
                        Text(item.0).foregroundStyle(p.text)
                        Spacer()
                        Text(item.1).font(.ui(12)).foregroundStyle(p.text2)
                    }
                    .font(.ui(14))
                    .padding(.horizontal, 14).padding(.vertical, 11)
                    .overlay(alignment: .bottom) {
                        if i < ReportSettings.items.count - 1 { Rectangle().fill(p.lineSoft).frame(height: 1) }
                    }
                }
            }
            .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.line, lineWidth: 1))
            .frame(maxWidth: 620)
            // TODO(outputs): day report, CSV and ALE clip lists once the Mac writes them (AppSettings.outputs, Windows plan 7).
        }
    }

    static let items: [(String, String)] = [
        ("PDF and HTML report", "clips, thumbnails, LUTs"),
        ("ASC MHL", "hash list for post"),
        ("Text and JSON report", "every file, every disk"),
    ]
}

struct DecoderSettings: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            SectionHeading(title: "Decoders", subtitle: "Tools for thumbnails and proxies. Copy and verify never need them.")
            VStack(alignment: .leading, spacing: 0) {
                let rows: [(String, String?, String, WritableKeyPath<ExternalTools, String?>)] = [
                    ("ffmpeg", model.tools.ffmpeg, "proxies, thumbnails", \.ffmpeg),
                    ("ffprobe", model.tools.ffprobe, "clip metadata", \.ffprobe),
                    ("braw-decode", model.tools.brawDecoder, "Blackmagic RAW", \.brawDecoder),
                    ("REDline", model.tools.redline, "RED R3D", \.redline),
                    ("art-cmd", model.tools.artCmd, "ARRIRAW", \.artCmd),
                ]
                ForEach(Array(rows.enumerated()), id: \.offset) { i, r in
                    ToolRow(name: r.0, path: r.1, purpose: r.2, keyPath: r.3)
                        .padding(.horizontal, 14).padding(.vertical, 10)
                        .overlay(alignment: .bottom) {
                            if i < rows.count - 1 { Rectangle().fill(p.lineSoft).frame(height: 1) }
                        }
                }
            }
            .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.line, lineWidth: 1))
            .frame(maxWidth: 720)
            Notice(kind: .info, text: AfterCopyTexts.rawNote)
        }
    }
}

struct ToolRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    let name: String
    let path: String?
    let purpose: String
    let keyPath: WritableKeyPath<ExternalTools, String?>

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: path != nil ? "checkmark.circle.fill" : "xmark.circle")
                .font(.system(size: 15))
                .foregroundStyle(path != nil ? p.success : p.text3)
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 8) {
                    Text(name).font(.ui(14, .semibold)).foregroundStyle(p.text)
                    Text(purpose).font(.ui(12)).foregroundStyle(p.text2)
                }
                Text(path ?? "not found").font(.mono(12)).foregroundStyle(p.text3)
                    .lineLimit(1).truncationMode(.middle).help(path ?? "")
            }
            Spacer()
            Button("Choose…") { model.pickTool(keyPath) }.buttonStyle(.offload(.secondary, height: 30))
                .help("Choose the tool's path")
        }
    }
}

struct HardwareSettings: View {
    @ObservedObject var hw: HardwareModel
    @Environment(\.palette) private var p
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            SectionHeading(title: "Hardware", subtitle: "Where the cards and disks are connected, and how fast.")
            FormRow(label: "Last check") {
                Text(hw.summary).font(.ui(14)).foregroundStyle(p.text)
            }
            FormRow(label: "Ports") {
                VStack(alignment: .leading, spacing: 10) {
                    Button("Check ports and disks…") { openWindow(id: "hardware") }
                        .buttonStyle(.offload(.secondary))
                    Text("Name your USB ports in the Hardware window (⌘U) — the names show in the speed table and the advice.")
                        .font(.ui(12)).foregroundStyle(p.text3).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }
}

// MARK: - Proxy (obr. 4)

struct ProxySettingsView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    private var proxy: Binding<ProxySettings> { $model.settings.proxy }

    var body: some View {
        // Side by side from 860 px of content (the 1360 px window), stacked below that (obr. 4 wraps the same way).
        AdaptiveColumns(threshold: 860, asideWidth: 380, spacing: 32) {
            form
            aside
        }
    }

    private var form: some View {
        VStack(alignment: .leading, spacing: 26) {
            SectionHeading(title: "Proxy", subtitle: "Rendered after verification — the originals always come first.",
                           isOn: proxy.enabled)
            VStack(alignment: .leading, spacing: 16) {
                FormRow(label: "Codec") {
                    Segmented(options: ProxyCodec.allCases, label: AfterCopyTexts.codecShort, selection: proxy.codec)
                }
                FormRow(label: "Height") {
                    Segmented(options: SettingsStore.proxyHeights, label: { "\($0)p" }, selection: proxy.height)
                }
                if model.settings.proxy.codec == .h264 || model.settings.proxy.codec == .hevc {
                    FormRow(label: "Bitrate") { bitrate }
                }
            }
            .disabled(!model.settings.proxy.enabled)
            .opacity(model.settings.proxy.enabled ? 1 : 0.55)
            lutBlock
            more
            if let problem = model.lutProblem { Notice(kind: .danger, text: problem) }
            if let problem = model.proxyFolderProblem { Notice(kind: .danger, text: problem) }
        }
    }

    private var bitrate: some View {
        HStack(spacing: 10) {
            HStack(spacing: 0) {
                Button { proxy.wrappedValue.bitrateMbps = max(2, proxy.wrappedValue.bitrateMbps - 1) } label: {
                    Text("−").font(.ui(16)).frame(width: 34, height: 34).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                Text("\(model.settings.proxy.bitrateMbps) Mb/s").font(.mono(13)).foregroundStyle(p.text)
                    .padding(.horizontal, 10)
                Button { proxy.wrappedValue.bitrateMbps = min(50, proxy.wrappedValue.bitrateMbps + 1) } label: {
                    Text("+").font(.ui(16)).frame(width: 34, height: 34).contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            .foregroundStyle(p.textSoft)
            .background(RoundedRectangle(cornerRadius: 8).fill(p.input))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(p.lineInput, lineWidth: 1))
            Text("≈ \(Int((Double(model.settings.proxy.bitrateMbps) * 60 / 8).rounded())) MB / minute")
                .font(.ui(13)).foregroundStyle(p.text3)
        }
    }

    private var lutBlock: some View {
        let mode = LutResolver.effectiveMode(model.settings.proxy)
        return VStack(alignment: .leading, spacing: 10) {
            StepTitle(title: "LUT") {
                LinkButton(title: "Custom .cube…") { model.pickLUT() }
            }
            FormRow(label: "Use") {
                Picker("", selection: Binding(get: { model.lutChoice }, set: { model.setLutChoice($0) })) {
                    Text("By camera (automatic)").tag("auto")
                    ForEach(model.luts.luts, id: \.id) { Text($0.name).tag($0.id) }
                    Text("No LUT (log)").tag("none")
                    Text("Custom .cube…").tag("custom")
                }
                .labelsHidden().frame(maxWidth: 320)
            }
            if !model.luts.luts.isEmpty {
                VStack(spacing: 0) {
                    ForEach(Array(model.luts.luts.enumerated()), id: \.element.id) { i, lut in
                        lutRow(lut, inUse: mode == .builtin && model.lutChoice == lut.id, last: i == model.luts.luts.count - 1)
                    }
                }
                .clipShape(RoundedRectangle(cornerRadius: Metrics.tileRadius))
                .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.line, lineWidth: 1))
            }
            switch mode {
            case .auto:
                Text("Picked per clip from the camera's metadata. Click a row to always use that LUT.")
                    .font(.ui(12)).foregroundStyle(p.text3)
                HStack(spacing: 10) {
                    Text("If the camera is unknown:").font(.ui(13)).foregroundStyle(p.text2)
                    Picker("", selection: Binding(get: { model.settings.proxy.lutFallback ?? "none" },
                                                  set: { model.settings.proxy.lutFallback = $0 == "none" ? nil : $0 })) {
                        Text("No LUT (log)").tag("none")
                        ForEach(model.luts.luts, id: \.id) { Text($0.name).tag($0.id) }
                    }
                    .labelsHidden().fixedSize()
                }
            case .custom:
                HStack(spacing: 10) {
                    Image(systemName: "doc").foregroundStyle(p.text2)
                    // An empty path (old settings) is "no LUT" — shown as not chosen.
                    Text(model.settings.proxy.lutPath.flatMap { $0.isEmpty ? nil : ($0 as NSString).lastPathComponent } ?? "not chosen")
                        .font(.mono(13)).foregroundStyle(p.text)
                        .lineLimit(1).truncationMode(.middle)
                        .help(model.settings.proxy.lutPath ?? "")
                    Spacer()
                    Button("Choose…") { model.pickLUT() }.buttonStyle(.offload(.secondary, height: 30))
                }
            case .off, .builtin:
                EmptyView()
            }
            // TODO(lut-rules): "LUT rules…" (per camera serial) once the Mac has them (Windows plan 8).
        }
    }

    private func lutRow(_ lut: BuiltinLut, inUse: Bool, last: Bool) -> some View {
        Button { model.setLutChoice(lut.id) } label: {
            HStack(spacing: 12) {
                Text("\(lut.maker) · \(AfterCopyTexts.curveShort(lut))")
                    .font(.ui(13, inUse ? .semibold : .regular)).foregroundStyle(p.text)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text("\(lut.gamut) → Rec.709").font(.ui(13)).foregroundStyle(inUse ? p.textSoft : p.text2)
                    .lineLimit(1)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(inUse ? "in use" : "").font(.ui(13)).foregroundStyle(p.accentText).frame(width: 56, alignment: .trailing)
            }
            .padding(.horizontal, 14).padding(.vertical, 10)
            .background(inUse ? p.accentSoft2 : Color.clear)
            .overlay(alignment: .bottom) { if !last { Rectangle().fill(p.lineSoft).frame(height: 1) } }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(lut.name)
    }

    private var more: some View {
        VStack(alignment: .leading, spacing: 10) {
            StepTitle("More")
            Toggle("Audio in proxies", isOn: proxy.audio).toggleStyle(.offloadCheckbox)
            Toggle("Skip proxies the camera recorded (SUB / PROXY)", isOn: proxy.skipCameraProxies).toggleStyle(.offloadCheckbox)
            Toggle("Copy proxies to every destination", isOn: proxy.copyToAllDestinations).toggleStyle(.offloadCheckbox)
            HStack(spacing: 10) {
                Text("Also copy proxies to").font(.ui(14)).foregroundStyle(p.text)
                Text(model.settings.proxy.extraFolder ?? "no folder")
                    .font(.mono(12))
                    .foregroundStyle(model.settings.proxy.extraFolder == nil ? p.text3 : p.textSoft)
                    .lineLimit(1).truncationMode(.middle)
                    .help(model.settings.proxy.extraFolder ?? "")
                Spacer(minLength: 8)
                Button("Choose…") { model.pickProxyFolder() }.buttonStyle(.offload(.secondary, height: 30))
                if model.settings.proxy.extraFolder != nil {
                    Button { model.settings.proxy.extraFolder = nil } label: {
                        Image(systemName: "xmark").font(.system(size: 11, weight: .semibold)).frame(width: 30, height: 30)
                    }
                    .buttonStyle(.offload(.ghost, height: 30))
                    .help("Don't copy to a folder")
                }
            }
        }
    }

    private var aside: some View {
        VStack(alignment: .leading, spacing: 14) {
            StepTitle("Burn-in preview")
            BurnInPreview(proxy: model.settings.proxy)
            VStack(spacing: 0) {
                burnRow(Toggle("Clip name", isOn: proxy.burnFilename).toggleStyle(.offloadCheckbox), value: "bottom left")
                burnRow(Toggle("Timecode", isOn: proxy.burnTimecode).toggleStyle(.offloadCheckbox), value: "bottom right")
                HStack(spacing: 10) {
                    Text("Crop to frame lines").font(.ui(14)).foregroundStyle(p.text)
                    Spacer(minLength: 6)
                    Picker("", selection: proxy.frameAspect) {
                        Text("full image").tag(Double?.none)
                        ForEach([1.33, 1.66, 1.78, 1.85, 2.0, 2.39], id: \.self) { a in
                            Text(String(format: "%.2f", a)).tag(Double?.some(a))
                        }
                    }
                    .labelsHidden().fixedSize()
                    if model.settings.proxy.frameAspect != nil {
                        Stepper("\(Int((model.settings.proxy.frameScale * 100).rounded())) %",
                                value: proxy.frameScale, in: 0.5...1.0, step: 0.01)
                            .font(.mono(12))
                            .help("Frame lines size relative to the image — 90 % = 10 % overscan")
                    }
                }
                .padding(.vertical, 7)
            }
            .padding(.horizontal, 14).padding(.vertical, 6)
            .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.line, lineWidth: 1))
            Text(AfterCopyTexts.rawNote).font(.ui(13)).foregroundStyle(p.text3).lineSpacing(3)
                .fixedSize(horizontal: false, vertical: true)
        }
        .disabled(!model.settings.proxy.enabled)
        .opacity(model.settings.proxy.enabled ? 1 : 0.55)
    }

    private func burnRow<T: View>(_ toggle: T, value: String) -> some View {
        HStack {
            toggle
            Spacer()
            Text(value).font(.ui(12)).foregroundStyle(p.text3)
        }
        .padding(.vertical, 9)
        .overlay(alignment: .bottom) { Rectangle().fill(p.lineSoft).frame(height: 1) }
    }
}

/// A grey 16:9 frame (no real picture yet — README "new things" 8): the crop to the frame lines, the clip name bottom
/// left and the timecode bottom right, inside the picture the proxy keeps.
struct BurnInPreview: View {
    @Environment(\.palette) private var p
    let proxy: ProxySettings

    var body: some View {
        GeometryReader { g in
            let w = g.size.width, h = g.size.height
            let crop = proxy.frameCrop(width: Int(w * 10), height: Int(h * 10))
                .map { CGRect(x: CGFloat($0.x) / 10, y: CGFloat($0.y) / 10, width: CGFloat($0.w) / 10, height: CGFloat($0.h) / 10) }
            let frame = crop ?? CGRect(x: 0, y: 0, width: w, height: h)
            ZStack(alignment: .topLeading) {
                Rectangle().fill(p.previewBg)
                Text("[frame from the clip]").font(.ui(12)).foregroundStyle(p.previewText)
                    .frame(width: w, height: h)
                if let crop {
                    Rectangle().fill(Color.black.opacity(0.35)).frame(width: w, height: h)
                        .mask(Rectangle().frame(width: w, height: h).overlay(
                            Rectangle().frame(width: crop.width, height: crop.height).offset(x: crop.minX, y: crop.minY)
                                .blendMode(.destinationOut), alignment: .topLeading).compositingGroup())
                    Rectangle().strokeBorder(p.frameLine, lineWidth: 1)
                        .frame(width: crop.width, height: crop.height).offset(x: crop.minX, y: crop.minY)
                }
                HStack(alignment: .bottom) {
                    if proxy.burnFilename { label("A035C010_261007_R2KQ") }
                    Spacer(minLength: 4)
                    if proxy.burnTimecode { label("14:22:07:13") }
                }
                .padding(.horizontal, max(6, frame.width * 0.015)).padding(.bottom, max(6, frame.height * 0.04))
                .frame(width: frame.width, height: frame.height, alignment: .bottom)
                .offset(x: frame.minX, y: frame.minY)
            }
        }
        .aspectRatio(16 / 9, contentMode: .fit)
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(p.lineInput, lineWidth: 1))
    }

    private func label(_ text: String) -> some View {
        Text(text).font(.mono(11)).foregroundStyle(Color.white)
            .padding(.horizontal, 6).padding(.vertical, 2)
            .background(p.shade)
            .lineLimit(1)
    }
}

/// Two views side by side when the width allows (the second `asideWidth` wide), else one under the other.
struct AdaptiveColumns: SwiftUI.Layout {
    var threshold: CGFloat
    var asideWidth: CGFloat
    var spacing: CGFloat

    private func wide(_ width: CGFloat?) -> Bool { (width ?? 0) >= threshold }

    func sizeThatFits(proposal: ProposedViewSize, subviews: LayoutSubviews, cache: inout ()) -> CGSize {
        guard subviews.count == 2 else { return .zero }
        let width = proposal.width ?? threshold
        if wide(proposal.width) {
            let a = subviews[0].sizeThatFits(ProposedViewSize(width: width - asideWidth - spacing, height: nil))
            let b = subviews[1].sizeThatFits(ProposedViewSize(width: asideWidth, height: nil))
            return CGSize(width: width, height: max(a.height, b.height))
        }
        let a = subviews[0].sizeThatFits(ProposedViewSize(width: width, height: nil))
        let b = subviews[1].sizeThatFits(ProposedViewSize(width: min(width, 520), height: nil))
        return CGSize(width: width, height: a.height + spacing + b.height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: LayoutSubviews, cache: inout ()) {
        guard subviews.count == 2 else { return }
        if wide(bounds.width) {
            let formWidth = bounds.width - asideWidth - spacing
            subviews[0].place(at: bounds.origin, proposal: ProposedViewSize(width: formWidth, height: nil))
            subviews[1].place(at: CGPoint(x: bounds.minX + formWidth + spacing, y: bounds.minY),
                              proposal: ProposedViewSize(width: asideWidth, height: nil))
        } else {
            let a = subviews[0].sizeThatFits(ProposedViewSize(width: bounds.width, height: nil))
            subviews[0].place(at: bounds.origin, proposal: ProposedViewSize(width: bounds.width, height: nil))
            subviews[1].place(at: CGPoint(x: bounds.minX, y: bounds.minY + a.height + spacing),
                              proposal: ProposedViewSize(width: min(bounds.width, 520), height: nil))
        }
    }
}

// MARK: - Texts shared by step 3 and the settings page

enum AfterCopyTexts {
    static let verifyOffWarning = "Without verification you can't be sure the data on the disk is good."
    static let rawNote = "Canon Cinema RAW Light and Sony X-OCN are copied and verified; proxies for them can't be made on an Intel Mac yet."

    /// "H.264", "H.265", "ProRes Proxy" — the codec as the summary and the segments say it.
    static func codecShort(_ c: ProxyCodec) -> String {
        switch c {
        case .h264: return "H.264"
        case .hevc: return "H.265"
        case .proresProxy: return "ProRes Proxy"
        case .proresLT: return "ProRes LT"
        case .prores422: return "ProRes 422"
        case .prores422HQ: return "ProRes HQ"
        }
    }

    /// "LogC4", "S-Log3", "Film Gen 5": a built-in LUT's curve without the maker.
    static func curveShort(_ lut: BuiltinLut) -> String {
        var c = lut.curve
        let maker = lut.maker.split(separator: " ").first.map(String.init) ?? lut.maker
        if c.hasPrefix("Canon Log ") { return "C-Log" + c.dropFirst("Canon Log ".count) }
        if c.hasPrefix(maker + " ") { c.removeFirst(maker.count + 1) }
        return c.replacingOccurrences(of: "Generation", with: "Gen")
    }

    /// "LogC4 → 709" for a fixed built-in LUT, else what the mode says.
    static func lutShort(_ p: ProxySettings, _ catalog: LutCatalog) -> String {
        switch LutResolver.effectiveMode(p) {
        case .off: return "no LUT"
        case .custom: return "custom LUT"
        case .builtin: return catalog.find(p.lutBuiltin).map { "\(curveShort($0)) → 709" } ?? "built-in LUT"
        case .auto: return "LUT by camera"
        }
    }
}
