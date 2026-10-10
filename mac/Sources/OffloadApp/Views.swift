import AppKit
import OffloadKit
import SwiftUI

// MARK: - Root

struct ContentView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        RootView().themed()
    }
}

/// The top bar over the two columns — or over the settings page.
private struct RootView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        VStack(spacing: 0) {
            if model.settingsSection != nil {
                SettingsPage()
            } else {
                TopBar()
                HSplitView {
                    SetupPanel()
                        .frame(minWidth: 360, idealWidth: 400, maxWidth: 480)
                    QueuePanel()
                        .frame(minWidth: 520)
                }
            }
        }
        .background(p.bg)
        .ignoresSafeArea()
        .frame(minWidth: 940, minHeight: 640)
        .alert("Offload", isPresented: Binding(get: { model.message != nil }, set: { if !$0 { model.message = nil } })) {
            Button("OK") { model.message = nil }
        } message: {
            Text(model.message ?? "")
        }
    }
}

extension JobItem {
    var detailText: String { progress.detailText }
    var statusText: String { progress.statusText }
    var tone: CardTone { progress.tone }
    var chainLines: [String] { chain.map { ChainView.lines($0, brake: progress.brake) } ?? [] }
    var safeText: String { Texts.safeToEject(plan?.cardPaths.count ?? 0) }
    var failedText: String? {
        guard !isCancelled, let r = report, !r.success else { return nil }
        return Texts.failed(r.failedFiles.count)
    }
    var cancelledText: String? { isCancelled && !status.safeToEject ? Texts.cancelled : nil }
    var summaryText: String? {
        guard isFinished, !isCancelled, let r = report else { return nil }
        return JobProgress.summaryText(r)
    }
}

// MARK: - Setup (left)

struct SetupPanel: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    SourcesSection()
                    DestinationsSection()
                }
                .padding(18)
            }
            Divider()
            StartBar()
        }
        .background(Color(nsColor: .windowBackgroundColor))
    }
}

struct HardwareSection: View {
    @ObservedObject var hw: HardwareModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            SectionHeader(title: "Hardware", systemImage: "cable.connector",
                          trailing: AnyView(Button("Prověřit…") { openWindow(id: "hardware") }.buttonStyle(.borderless)
                              .help("Prověřit USB porty (⌘U)")))
            Text(hw.summary).font(.caption).foregroundStyle(.secondary)
        }
    }
}

struct SectionHeader: View {
    let title: String
    let systemImage: String
    var trailing: AnyView? = nil

    var body: some View {
        HStack {
            Label(title, systemImage: systemImage)
                .font(.headline)
            Spacer()
            trailing
        }
    }
}

struct SourcesSection: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionHeader(title: "Karty", systemImage: "sdcard",
                          trailing: AnyView(HStack(spacing: 4) {
                              Button { model.refreshVolumes() } label: { Image(systemName: "arrow.clockwise") }
                                  .help("Znovu načíst připojené disky")
                              Button("Složka…") { model.addManualSource() }
                          }.buttonStyle(.borderless)))
            let cards = model.volumes.filter { !$0.isDestination }
            if cards.isEmpty && model.manualSources.isEmpty {
                Text("Vložte kartu do čtečky — objeví se tady automaticky.")
                    .font(.callout).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(12)
                    .background(RoundedRectangle(cornerRadius: 8).fill(Color.secondary.opacity(0.08)))
            }
            ForEach(cards) { v in
                SourceRow(path: v.path, title: v.name,
                          detail: "\(formatBytes(v.total - v.free)) · \(v.format)\(v.looksLikeCard ? "" : " · nevypadá jako karta")",
                          icon: v.looksLikeCard ? "sdcard.fill" : "externaldrive")
            }
            ForEach(model.manualSources, id: \.self) { p in
                SourceRow(path: p, title: (p as NSString).lastPathComponent, detail: p, icon: "folder")
            }
        }
    }
}

struct SourceRow: View {
    @EnvironmentObject var model: AppModel
    let path: String
    let title: String
    let detail: String
    let icon: String

    var selected: Bool { model.selectedSources.contains(path) }

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Toggle("", isOn: Binding(get: { selected }, set: { on in
                if on { model.selectedSources.insert(path) } else { model.selectedSources.remove(path) }
            }))
            .labelsHidden()
            .toggleStyle(.checkbox)
            Image(systemName: icon).font(.title3).foregroundStyle(selected ? Color.accentColor : .secondary).frame(width: 22)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).fontWeight(.medium)
                Text(detail).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
                if selected {
                    HStack(spacing: 6) {
                        Text("Název složky").font(.caption).foregroundStyle(.secondary)
                        TextField(model.defaultName(path), text: Binding(
                            get: { model.cardNames[path] ?? "" },
                            set: { model.cardNames[path] = $0 }))
                            .textFieldStyle(.roundedBorder)
                            .font(.caption)
                    }
                }
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 8).fill(selected ? Color.accentColor.opacity(0.08) : Color.secondary.opacity(0.05)))
    }
}

struct DestinationsSection: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionHeader(title: "Cíle", systemImage: "externaldrive.connected.to.line.below",
                          trailing: AnyView(HStack(spacing: 4) {
                              Button { model.refreshDestinations() } label: { Image(systemName: "arrow.clockwise") }
                                  .help("Znovu zjistit volné místo a připojení cílů")
                              Button("Přidat…") { model.addDestination() }
                          }.buttonStyle(.borderless)))
            if model.settings.destRoots.isEmpty {
                Text("Přidejte dva disky — každá karta se zapíše na oba zároveň.")
                    .font(.callout).foregroundStyle(.secondary)
            }
            ForEach(Array(model.settings.destRoots.enumerated()), id: \.element) { i, root in
                let info = model.destinationInfo(root)
                HStack(spacing: 10) {
                    Text("\(i + 1)")
                        .font(.caption.bold()).foregroundStyle(.white)
                        .frame(width: 20, height: 20)
                        .background(Circle().fill(info.exists || info.checking ? Color.accentColor : .red))
                    VStack(alignment: .leading, spacing: 2) {
                        Text((root as NSString).lastPathComponent).fontWeight(.medium)
                        if info.checking {
                            Text("Zjišťuji…").font(.caption).foregroundStyle(.secondary)
                        } else if info.exists {
                            Text("\(formatBytes(info.free)) volno · \(info.format)").font(.caption).foregroundStyle(.secondary)
                        } else {
                            Text("Disk není připojen").font(.caption).foregroundStyle(.red)
                        }
                        if info.format.lowercased() == "exfat" || info.format.lowercased() == "msdos" {
                            Label("Doporučeno APFS — spolehlivější a ověřování obchází cache", systemImage: "exclamationmark.triangle")
                                .font(.caption).foregroundStyle(.orange)
                        }
                    }
                    Spacer()
                    Button { model.removeDestination(root) } label: { Image(systemName: "minus.circle") }
                        .buttonStyle(.borderless).help("Odebrat cíl")
                }
                .padding(10)
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.secondary.opacity(0.05)))
            }
            if model.settings.destRoots.count == 1 {
                Label("Jen jeden cíl — doporučeny jsou dvě nezávislé kopie.", systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(.orange)
            }
        }
    }
}

struct StartBar: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(Texts.cards(model.selectedSources.count)).fontWeight(.medium)
                Text(Texts.destinations(model.settings.destRoots.count) + (model.settings.proxy.enabled ? " · proxy" : ""))
                    .font(.caption).foregroundStyle(.secondary)
                // The LUT is also used by thumbnails when proxies are off: say so here, where the start button is.
                if let p = model.lutProblem, !model.settings.proxy.enabled {
                    Text(p).font(.caption).foregroundStyle(.red)
                }
            }
            Spacer()
            Button {
                model.start()
            } label: {
                Label("Spustit offload", systemImage: "play.fill").frame(minWidth: 150)
            }
            .controlSize(.large)
            .buttonStyle(.borderedProminent)
            .keyboardShortcut(.return, modifiers: .command)
            .disabled(!model.canStart)
        }
        .padding(14)
    }
}

// MARK: - Queue (right)

struct QueuePanel: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("Fronta").font(.title2.weight(.semibold))
                Spacer()
                if model.jobs.contains(where: \.isFinished) {
                    Button("Vyčistit hotové") { model.clearFinished() }
                }
            }
            .padding(.horizontal, 20).padding(.vertical, 14)
            Divider()
            if model.jobs.isEmpty {
                VStack(spacing: 10) {
                    Image(systemName: "tray").font(.system(size: 40)).foregroundStyle(.tertiary)
                    Text("Zatím žádný offload").font(.title3).foregroundStyle(.secondary)
                    Text("Vyberte kartu a cíle vlevo a stiskněte ⌘↩︎.").foregroundStyle(.tertiary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    LazyVStack(spacing: 12) {
                        ForEach(model.jobs.reversed()) { JobCard(item: $0) }
                    }
                    .padding(20)
                }
            }
        }
        .background(Color(nsColor: .controlBackgroundColor))
    }
}

struct JobCard: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject var item: JobItem

    var phaseColor: Color {
        switch item.tone {
        case .ok: return .green
        case .error: return .red
        case .warning: return .orange
        case .waiting: return .secondary
        case .active: return .accentColor
        }
    }

    var body: some View {
        let s = item.status
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Image(systemName: "sdcard.fill").foregroundStyle(phaseColor)
                Text(item.plan?.cardName ?? item.sourceName).font(.headline)
                if let p = item.plan {
                    Text(Texts.files(p.scan.files.count, p.scan.totalBytes)).font(.callout).foregroundStyle(.secondary)
                }
                Spacer()
                Text(item.statusText).font(.caption.weight(.semibold))
                    .padding(.horizontal, 8).padding(.vertical, 3)
                    .background(Capsule().fill(phaseColor.opacity(0.15)))
                    .foregroundStyle(phaseColor)
            }
            if let p = item.plan {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(Array(p.destRoots.enumerated()), id: \.offset) { i, root in
                        HStack(spacing: 4) {
                            Image(systemName: "externaldrive.fill").font(.caption2)
                            Text((root as NSString).lastPathComponent).fontWeight(.medium)
                            Text("› \(p.date)/\(p.cardName)").font(.system(.caption, design: .monospaced))
                        }
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .help(p.cardPaths[i])
                    }
                }
            }
            if item.isRunning {
                ProgressView(value: item.phaseProgress)
                Text(item.detailText).font(.callout.monospacedDigit()).foregroundStyle(.secondary).lineLimit(1)
                if !item.chainLines.isEmpty {
                    VStack(alignment: .leading, spacing: 1) {
                        ForEach(Array(item.chainLines.enumerated()), id: \.offset) { _, line in
                            Text(line)
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(line.hasPrefix("Úzké hrdlo") ? Color.orange : Color.secondary)
                                .lineLimit(1).truncationMode(.middle)
                        }
                    }
                }
            }
            if s.safeToEject && !item.ejected {
                HStack {
                    Label(item.safeText, systemImage: "checkmark.shield.fill").foregroundStyle(.green).font(.callout)
                    Spacer()
                    if model.canShowEject(item) {
                        Button("Vysunout kartu") { model.eject(item) }.disabled(item.ejecting)
                    }
                }
            } else if item.ejected {
                Label("Karta vysunuta", systemImage: "eject.fill").foregroundStyle(.secondary).font(.callout)
            }
            if let t = item.cancelledText {
                Label(t, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.orange).font(.callout)
            }
            if let t = item.failedText {
                Label(t, systemImage: "xmark.octagon.fill").foregroundStyle(.red).font(.callout)
            }
            if let e = s.error {
                Label(e, systemImage: "xmark.octagon.fill").foregroundStyle(.red).font(.callout)
            }
            if let summary = item.summaryText {
                Text(summary).font(.caption).foregroundStyle(.secondary)
            }
            HStack {
                if item.report != nil {
                    Button { model.openReport(item) } label: { Label("Report", systemImage: "doc.richtext") }
                    Button { model.reveal(item) } label: { Label("Finder", systemImage: "folder") }
                }
                Spacer()
                if item.isRunning || s.phase == .waiting {
                    Button(role: .destructive) { model.cancel(item) } label: { Text("Zrušit") }
                }
            }
            .buttonStyle(.bordered)
            .controlSize(.small)
        }
        .padding(16)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color(nsColor: .windowBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.secondary.opacity(0.15)))
    }
}
