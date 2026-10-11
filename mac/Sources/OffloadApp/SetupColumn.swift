import AppKit
import OffloadKit
import SwiftUI

/// The left column (UI návrh obr. 1–3): 1 · Cards → 2 · Destinations → 3 · After copy, read top to bottom, with the
/// start bar pinned at the bottom (the steps scroll on their own). With no card in a reader while the queue has jobs,
/// steps 2 and 3 shrink to a short list and one line (obr. 2, 3); a card coming in brings the full form back.
///
/// Each card is looked at off the main thread (CardInspector): its clips and size, the line from the card history
/// ("Backed up on … — verified, safe to format"), "Append" for new clips, and the folder name the clips suggest.
/// Not on the Mac yet (hidden): the camera chip ("ALEXA 35 · LogC4"), the time estimate.
struct SetupColumn: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    /// No card shown while offloads are queued: the short form.
    private var compact: Bool {
        model.volumes.allSatisfy(\.isDestination) && model.manualSources.isEmpty && !model.jobs.isEmpty
    }

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: Metrics.sectionGap) {
                    CardsStep()
                    DestinationsStep(compact: compact)
                    AfterCopyStep(compact: compact)
                }
                .padding(.horizontal, Metrics.leftPadding)
                .padding(.top, 22).padding(.bottom, 16)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            StartBar()
        }
        .background(p.panel)
    }
}

// MARK: - 1 · Cards

struct CardsStep: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        VStack(alignment: .leading, spacing: Metrics.tileGap) {
            StepTitle(title: "1 · Cards") {
                HStack(spacing: 12) {
                    IconButton(systemImage: "arrow.clockwise", help: "Rescan connected disks") { model.refreshVolumes() }
                    LinkButton(title: "From folder…") { model.addManualSource() }
                }
            }
            let cards = model.volumes.filter { !$0.isDestination }
            if cards.isEmpty && model.manualSources.isEmpty {
                EmptyCard(running: !model.jobs.isEmpty)
            }
            ForEach(cards) { v in
                CardTile(path: v.path, title: v.name, detail: detail(v), icon: v.looksLikeCard ? "sdcard" : "externaldrive")
            }
            ForEach(model.manualSources, id: \.self) { path in
                CardTile(path: path, title: (path as NSString).lastPathComponent, detail: path, icon: "folder", isFolder: true)
            }
        }
    }

    /// "24 clips · 1.70 GB · exFAT · USB 3 (5 Gb/s)" — before the card was looked at, its used space stands for the size.
    private func detail(_ v: VolumeItem) -> String {
        var parts = [model.clipFacts(v.path) ?? Fmt.bytes(max(0, v.total - v.free)), DiskFormat.label(v.format)]
        if let c = model.volumeConnections[v.path] { parts.append(c) }
        if !v.looksLikeCard { parts.append("doesn't look like a card") }
        return parts.filter { !$0.isEmpty }.joined(separator: " · ")
    }
}

/// The dashed box of an empty step 1.
struct EmptyCard: View {
    @Environment(\.palette) private var p
    let running: Bool

    var body: some View {
        VStack(spacing: 8) {
            Image(systemName: "sdcard").font(.system(size: 22, weight: .light)).foregroundStyle(p.text2)
            Text(running ? "Insert the next card" : "Insert a card into the reader")
                .font(.ui(15, .semibold)).foregroundStyle(p.text)
            Text(running ? "It shows up here and joins the queue after the running offload." : "It shows up here automatically.")
                .font(.ui(13)).foregroundStyle(p.text2).multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 26).padding(.horizontal, 16)
        .background(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.dashedLine, style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
    }
}

/// One card (or folder): ticked = blue border, icon on blue, the folder name field under it.
struct CardTile: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    let path: String
    let title: String
    let detail: String
    let icon: String
    var isFolder = false

    private var selected: Bool { model.selectedSources.contains(path) }

    private var isOn: Binding<Bool> {
        Binding(get: { model.selectedSources.contains(path) }, set: { on in
            if on { model.selectedSources.insert(path) } else { model.selectedSources.remove(path) }
        })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top, spacing: 12) {
                IconTile(systemImage: icon, size: 38, radius: 9, iconSize: 18,
                         fg: selected ? p.accentText : p.text2, bg: selected ? p.accentSoft : p.chip)
                VStack(alignment: .leading, spacing: 3) {
                    // TODO(camera-chip): "ALEXA 35 · LogC4" once the Mac checks a clip of the card first (Windows plan 9).
                    Text(title).font(.ui(15, .semibold)).foregroundStyle(selected ? p.text : p.textSoft)
                        .lineLimit(1).truncationMode(.middle)
                    Text(detail).font(.ui(13)).foregroundStyle(p.text2)
                        .lineLimit(isFolder ? 1 : 2).truncationMode(.middle)
                        .fixedSize(horizontal: false, vertical: true)
                    if isFolder, let facts = model.clipFacts(path) {
                        Text(facts).font(.ui(13)).foregroundStyle(p.text2).lineLimit(1)
                    }
                    if let line = model.historyLine(path) {
                        Text(line.text).font(.ui(13)).foregroundStyle(color(line.tone))
                            .fixedSize(horizontal: false, vertical: true)
                            .textSelection(.enabled)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
                .onTapGesture { isOn.wrappedValue.toggle() }
                if let appendText = model.appendText(path) {
                    // Only the new clips into the earlier backup (spec 2.2).
                    let on = model.appendChosen(path)
                    Button { model.setAppend(path, !on) } label: {
                        Label(model.appendIsResume(path) ? "Finish" : "Append", systemImage: on ? "checkmark" : "plus").font(.ui(12, .semibold))
                    }
                    .buttonStyle(.offload(on ? .warnSoft : .secondary, height: 28))
                    .help(appendText)
                    .padding(.top, 2)
                }
                if isFolder {
                    IconButton(systemImage: "xmark", help: "Remove this folder") { model.removeManualSource(path) }
                }
                Toggle("", isOn: isOn).labelsHidden().toggleStyle(.offloadCheckbox)
                    .help(selected ? "Don't offload this card" : "Offload this card")
                    .padding(.top, 2)
            }
            if selected, let folder = model.appendFolderText(path) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(folder).font(.mono(12)).foregroundStyle(p.textSoft)
                    if let text = model.appendText(path) { Text(text).font(.ui(12)).foregroundStyle(p.text3) }
                }
            } else if selected {
                let placeholder = model.namePlaceholder(path)
                VStack(alignment: .leading, spacing: 6) {
                    (Text("Folder name ").foregroundColor(p.text2)
                        + Text(placeholder.hint.map { "— \($0)" } ?? "— empty uses the card's name").foregroundColor(p.text3))
                        .font(.ui(12))
                        .lineLimit(1).truncationMode(.tail)
                    TextField(placeholder.name, text: Binding(
                        get: { model.cardNames[path] ?? "" },
                        set: { model.cardNames[path] = $0 }))
                        .textFieldStyle(.plain)
                        .font(.mono(13))
                        .foregroundStyle(p.text)
                        .padding(.horizontal, 10)
                        .frame(height: 36)
                        .background(RoundedRectangle(cornerRadius: 8).fill(p.input))
                        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(p.lineInput, lineWidth: 1))
                }
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: Metrics.tileRadius).fill(selected ? p.raised : p.raisedAlt))
        .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(selected ? p.accentBorder : p.line, lineWidth: 1))
    }

    private func color(_ tone: HistoryTone) -> Color {
        switch tone {
        case .ok: return p.success
        case .warning: return p.warn
        case .error: return p.dangerText
        case .neutral: return p.text2
        }
    }
}

// MARK: - 2 · Destinations

struct DestinationsStep: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    let compact: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: Metrics.tileGap) {
            StepTitle(title: "2 · Destinations") {
                if !compact {
                    HStack(spacing: 12) {
                        IconButton(systemImage: "arrow.clockwise", help: "Check free space and connections again") {
                            model.refreshDestinations()
                        }
                        LinkButton(title: "Add disk", systemImage: "plus") { model.addDestination() }
                    }
                }
            }
            let roots = model.settings.destRoots
            if roots.isEmpty {
                Text("Add two disks — each card is written to both at once.")
                    .font(.ui(13)).foregroundStyle(p.text2)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(14)
                    .background(RoundedRectangle(cornerRadius: Metrics.tileRadius)
                        .strokeBorder(p.dashedLine, style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
            } else if compact {
                compactList(roots)
            } else {
                ForEach(Array(roots.enumerated()), id: \.element) { i, root in
                    DestinationTile(index: i + 1, root: root)
                }
                Text(pathPreview).font(.mono(12)).foregroundStyle(p.text3)
                    .lineLimit(1).truncationMode(.middle)
                    .padding(.horizontal, 2).padding(.top, 2)
            }
            if roots.count == 1 {
                Notice(kind: .warn, text: "Only one destination — two independent copies are recommended.")
            }
            if !compact {
                Toggle("Add connected disks automatically", isOn: $model.settings.autoDestinations)
                    .toggleStyle(.offloadCheckbox)
                    .font(.ui(13))
                    .help("A writable external disk that doesn't look like a card joins the destinations when it's connected and leaves when it's unplugged. A disk you removed by hand isn't added again.")
                    .padding(.top, 2)
            }
        }
    }

    /// "…/2026-10-10/A035_ARRI/" for one ticked card, "<card name>" otherwise.
    private var pathPreview: String {
        let ticked = model.selectedInOrder
        let name = ticked.count == 1 ? model.sourceName(ticked[0]) : "<card name>"
        return "…/\(model.dateString)/\(name)/"
    }

    private func compactList(_ roots: [String]) -> some View {
        VStack(spacing: 0) {
            ForEach(Array(roots.enumerated()), id: \.element) { i, root in
                let info = model.destinationInfo(root)
                HStack(spacing: 12) {
                    NumberBadge(number: i + 1)
                    Text(AppModel.diskName(root)).font(.ui(14, .medium)).foregroundStyle(p.text).lineLimit(1)
                    Spacer()
                    Text(info.checking ? "checking…" : info.exists ? "\(Fmt.bytes(info.free)) free" : "not connected")
                        .font(.ui(13))
                        .foregroundStyle(info.exists || info.checking ? (SpaceBar.lowSpace(info) ? p.warn : p.text2) : p.dangerText)
                }
                .padding(.horizontal, 14).padding(.vertical, 11)
                .overlay(alignment: .bottom) { if i < roots.count - 1 { Rectangle().fill(p.lineSoft).frame(height: 1) } }
            }
        }
        .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.line, lineWidth: 1))
    }
}

/// One destination disk: order, name, "7.20 TB free of 8 TB · APFS · Thunderbolt", ✕, and the space bar.
struct DestinationTile: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    let index: Int
    let root: String

    var body: some View {
        let info = model.destinationInfo(root)
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 12) {
                NumberBadge(number: index, fill: info.exists || info.checking ? nil : p.dangerBg)
                VStack(alignment: .leading, spacing: 2) {
                    Text(AppModel.diskName(root)).font(.ui(14, .semibold)).foregroundStyle(p.text).lineLimit(1)
                        .help(root)
                    if info.checking {
                        Text("Checking…").font(.ui(13)).foregroundStyle(p.text2)
                    } else if info.exists {
                        Text(facts(info)).font(.ui(13)).foregroundStyle(p.text2).fixedSize(horizontal: false, vertical: true)
                    } else {
                        Text("Disk not connected").font(.ui(13)).foregroundStyle(p.dangerText)
                    }
                }
                Spacer(minLength: 4)
                IconButton(systemImage: "xmark", help: "Remove this destination") { model.removeDestination(root) }
            }
            if info.exists {
                SpaceBar(info: info, need: model.selectedBytes ?? 0)
            }
            if ["exfat", "msdos"].contains(info.format.lowercased()) {
                Notice(kind: .warn, text: "APFS recommended — more reliable, and verification bypasses the cache.")
                    .font(.ui(12))
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: Metrics.tileRadius).fill(p.raised))
        .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.line, lineWidth: 1))
    }

    private func facts(_ info: DestinationStatus) -> String {
        var parts = [info.total > 0 ? "\(Fmt.bytes(info.free)) free of \(Fmt.bytes(info.total))" : "\(Fmt.bytes(info.free)) free",
                     DiskFormat.label(info.format)]
        if let c = info.connection { parts.append(c) }
        return parts.filter { !$0.isEmpty }.joined(separator: " · ")
    }
}

/// Grey = used, blue = what the ticked cards take (their used space); yellow when under 5 % stays free after them,
/// red when they don't fit. Only a hint: the engine checks the space itself before it copies.
struct SpaceBar: View {
    @Environment(\.palette) private var p
    let info: DestinationStatus
    let need: Int64

    static func lowSpace(_ info: DestinationStatus, need: Int64 = 0) -> Bool {
        info.exists && info.total > 0 && Double(info.free - need) < Double(info.total) * 0.05
    }

    var body: some View {
        let total = Double(max(info.total, 1))
        let used = Double(max(0, info.total - info.free)) / total
        let tooBig = need > info.free
        let low = !tooBig && SpaceBar.lowSpace(info, need: need)
        let needColor = tooBig ? p.danger : low ? p.warn : p.accentBar
        VStack(alignment: .leading, spacing: 6) {
            ThinBar(segments: [(used, p.diskUsed), (need > 0 ? max(Double(min(need, info.free)) / total, 0.01) : 0, needColor)])
            if tooBig {
                Text("Not enough space: the cards need \(Fmt.bytes(need)), \(Fmt.bytes(info.free)) free.")
                    .font(.ui(12)).foregroundStyle(p.dangerText)
            } else if low {
                Text("Less than 5 % stays free after this offload.").font(.ui(12)).foregroundStyle(p.warn)
            }
        }
    }
}

// MARK: - 3 · After copy

/// One row of step 3: the tick switches it (verify, analysis, proxy — the report is always written), a click on the
/// row opens its settings section (as Windows 0.5.2).
struct AfterCopyRow: Identifiable {
    enum Kind { case verify, verifyInProxy, analysis, proxy, report }
    let kind: Kind
    let label: String
    let value: String
    let on: Bool
    var warn = false
    var id: Kind { kind }

    init(kind: Kind, label: String, value: String, on: Bool, warn: Bool = false, disabled: Bool = false) {
        self.kind = kind
        self.label = label
        self.value = value
        self.on = on
        self.warn = warn
        self.disabled = disabled
    }

    var section: SettingsSection {
        switch kind {
        case .verify, .verifyInProxy: return .verify
        case .analysis: return .analysis
        case .proxy: return .proxy
        case .report: return .report
        }
    }

    /// The report is always written; "Verify during proxy render" needs verification and proxies on.
    var hasSwitch: Bool { kind != .report && !disabled }
    var disabled = false

    static func rows(_ s: AppSettings, luts: LutCatalog) -> [AfterCopyRow] {
        let px = s.proxy
        return [
            AfterCopyRow(kind: .verify, label: "Verify (read back)", value: s.verify ? "xxHash64" : "off", on: s.verify, warn: !s.verify),
            // User requirement 2026-10-11: per copy — the next card added takes it (settings verifyInProxy).
            AfterCopyRow(kind: .verifyInProxy, label: "Verify during proxy render",
                         value: !s.verify || !px.enabled ? "needs Verify and Proxy"
                             : s.verifyInProxy ? "safe to eject after the copy" : "off — read back after the copy",
                         on: s.verify && px.enabled && s.verifyInProxy, disabled: !s.verify || !px.enabled),
            AfterCopyRow(kind: .analysis, label: "Clip analysis & thumbnails", value: s.analyzeClips ? "3 frames / clip" : "off",
                         on: s.analyzeClips),
            AfterCopyRow(kind: .proxy, label: "Proxy",
                         value: px.enabled ? "\(px.height)p · \(AfterCopyTexts.codecShort(px.codec)) · \(AfterCopyTexts.lutShort(px, luts))" : "off",
                         on: px.enabled),
            AfterCopyRow(kind: .report, label: "Report", value: "PDF · HTML · ASC MHL", on: true),
        ]
    }

    /// Step 3 while offloads run, in one line.
    static func line(_ s: AppSettings) -> String {
        [s.verify ? (s.verifyInProxy && s.proxy.enabled ? "Verify xxHash64 during proxy render" : "Verify xxHash64") : "No verification",
         s.analyzeClips ? "analysis & thumbnails" : nil,
         s.proxy.enabled ? "proxy \(s.proxy.height)p \(AfterCopyTexts.codecShort(s.proxy.codec))" : nil,
         "report PDF, HTML, MHL"].compactMap { $0 }.joined(separator: " · ")
    }
}

struct AfterCopyStep: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    let compact: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: Metrics.tileGap) {
            StepTitle(title: "3 · After copy") {
                if !compact { LinkButton(title: "Edit settings") { model.openSettings(.folders) } }
            }
            if compact {
                Text(AfterCopyRow.line(model.settings)).font(.ui(14)).foregroundStyle(p.text2).lineSpacing(4)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                let rows = AfterCopyRow.rows(model.settings, luts: model.luts)
                VStack(spacing: 0) {
                    ForEach(Array(rows.enumerated()), id: \.element.id) { i, row in
                        rowView(row, last: i == rows.count - 1)
                    }
                }
                .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.line, lineWidth: 1))
            }
            // What blocks a start or is skipped (red / yellow) — the reason stays visible here.
            if let w = ProxyPreflight.warning(model.settings.proxy, analyzeClips: model.settings.analyzeClips, resolved: model.tools) {
                Notice(kind: .warn, text: w)
            }
        }
    }

    private func rowView(_ row: AfterCopyRow, last: Bool) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 12) {
                Button { toggle(row.kind) } label: { mark(row) }
                    .buttonStyle(.plain)
                    .disabled(!row.hasSwitch || model.starting)
                    .help(row.hasSwitch ? (row.on ? "Switch off" : "Switch on")
                          : row.kind == .report ? "The report is always written" : "Needs Verify and Proxy on")
                Button { model.openSettings(row.section) } label: {
                    HStack(spacing: 12) {
                        Text(row.label).font(.ui(14)).foregroundStyle(row.warn ? p.warn : p.text)
                            .lineLimit(1)
                        Spacer(minLength: 8)
                        Text(row.value).font(row.kind == .verify && row.on ? .mono(12) : .ui(12))
                            .foregroundStyle(p.text2).lineLimit(1).truncationMode(.middle)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("\(row.section.title) settings")
            }
            if row.warn {
                Text(AfterCopyTexts.verifyOffWarning).font(.ui(12)).foregroundStyle(p.warn)
                    .padding(.leading, 32).fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 11)
        .overlay(alignment: .bottom) { if !last { Rectangle().fill(p.lineSoft).frame(height: 1) } }
    }

    /// The green tick on green, or a grey ring when off.
    private func mark(_ row: AfterCopyRow) -> some View {
        ZStack {
            if row.on {
                RoundedRectangle(cornerRadius: 6).fill(p.checkBg)
                Image(systemName: "checkmark").font(.system(size: 10, weight: .heavy)).foregroundStyle(p.success)
            } else {
                Circle().strokeBorder(p.lineStrong, lineWidth: 1.5)
            }
        }
        .frame(width: 20, height: 20)
        .contentShape(Rectangle())
    }

    private func toggle(_ kind: AfterCopyRow.Kind) {
        switch kind {
        case .verify: model.settings.verify.toggle()
        case .verifyInProxy: model.settings.verifyInProxy.toggle()
        case .analysis: model.settings.analyzeClips.toggle()
        case .proxy: model.settings.proxy.enabled.toggle()
        case .report: break
        }
    }
}

// MARK: - Start bar

/// Pinned under the steps: "1 card → 2 disks · 1.70 GB", why Start can't run yet, and the big button (⌘↩).
struct StartBar: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            summary
            // Spec 2.5: red blocks the start, orange only warns (the engine checks again when each job starts).
            ForEach(Array(model.spaceProblems.enumerated()), id: \.offset) { _, problem in
                Text(problem.text).font(.ui(12)).foregroundStyle(problem.severity == .blocking ? p.dangerText : p.warn)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let problem = model.startProblem {
                Text(problem).font(.ui(12)).foregroundStyle(p.dangerText).fixedSize(horizontal: false, vertical: true)
            } else if model.starting {
                Text("Checking the cards, the LUT and the proxy folder…").font(.ui(12)).foregroundStyle(p.text2)
            }
            Button { model.start() } label: {
                HStack(spacing: 10) {
                    Image(systemName: "play.fill").font(.system(size: 12))
                    Text(model.startQueues ? "Add to queue" : "Start offload")
                    if model.canStart {
                        Text("⌘ ↩").font(.ui(12, .medium))
                            .padding(.horizontal, 6).padding(.vertical, 2)
                            .background(RoundedRectangle(cornerRadius: 5).fill(Color.white.opacity(0.16)))
                    }
                }
            }
            .buttonStyle(.offload(.primary, height: 48, fullWidth: true))
            .keyboardShortcut(.return, modifiers: .command)
            .disabled(!model.canStart)
        }
        .padding(.horizontal, Metrics.leftPadding).padding(.top, 16).padding(.bottom, 20)
        .background(p.panel)
        .overlay(alignment: .top) { Rectangle().fill(p.line).frame(height: 1) }
    }

    @ViewBuilder
    private var summary: some View {
        let cards = model.selectedSources.count
        let disks = model.settings.destRoots.count
        HStack {
            if cards == 0 {
                Text("Pick a card").font(.ui(13, .semibold)).foregroundStyle(p.text2)
            } else {
                (Text("\(Fmt.count(cards, "card")) → \(Fmt.count(disks, "disk"))").font(.ui(13, .semibold)).foregroundColor(p.text)
                    + Text(model.selectedBytes.map { $0 > 0 ? " · \(Fmt.bytes($0))" : "" } ?? "").font(.ui(13)).foregroundColor(p.text2))
            }
            Spacer()
            // TODO(estimate): "estimate ~0:08 incl. proxy" once the Mac estimates from this session's speeds (Windows plan 9).
        }
    }
}

// MARK: - Small helpers

/// A 28 px borderless icon button (rescan, remove).
struct IconButton: View {
    @Environment(\.palette) private var p
    let systemImage: String
    let help: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: systemImage).font(.system(size: 12, weight: .semibold))
                .foregroundStyle(p.text3)
                .frame(width: 28, height: 28)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
    }
}

/// "exFAT", "APFS", "FAT32" — a file system as people say it.
enum DiskFormat {
    static func label(_ fs: String) -> String {
        switch fs.lowercased() {
        case "apfs": return "APFS"
        case "exfat": return "exFAT"
        case "msdos": return "FAT32"
        case "hfs": return "Mac OS Extended"
        case "ntfs": return "NTFS"
        case "smbfs": return "SMB"
        case "nfs": return "NFS"
        case "afpfs": return "AFP"
        default: return fs
        }
    }
}
