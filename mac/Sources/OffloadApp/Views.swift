import AppKit
import OffloadKit
import SwiftUI

// MARK: - Root

struct ContentView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        HSplitView {
            SetupPanel()
                .frame(minWidth: 360, idealWidth: 400, maxWidth: 480)
            QueuePanel()
                .frame(minWidth: 520)
        }
        .frame(minWidth: 940, minHeight: 640)
        .alert("Offload", isPresented: Binding(get: { model.message != nil }, set: { if !$0 { model.message = nil } })) {
            Button("OK") { model.message = nil }
        } message: {
            Text(model.message ?? "")
        }
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
                    StructureSection()
                    ProxySection()
                    ToolsSection()
                }
                .padding(18)
            }
            Divider()
            StartBar()
        }
        .background(Color(nsColor: .windowBackgroundColor))
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
                        TextField(Layout.defaultCardName(for: path), text: Binding(
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
                          trailing: AnyView(Button("Přidat…") { model.addDestination() }.buttonStyle(.borderless)))
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
                        .background(Circle().fill(info.exists ? Color.accentColor : .red))
                    VStack(alignment: .leading, spacing: 2) {
                        Text((root as NSString).lastPathComponent).fontWeight(.medium)
                        if info.exists {
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

struct StructureSection: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionHeader(title: "Struktura a kontrola", systemImage: "folder.badge.gearshape")
            HStack {
                Toggle("Vlastní datum", isOn: $model.useCustomDate)
                if model.useCustomDate {
                    DatePicker("", selection: $model.customDate, displayedComponents: .date).labelsHidden()
                }
            }
            Text("…/\(model.dateString)/<název karty>/").font(.system(.caption, design: .monospaced)).foregroundStyle(.secondary)
            Toggle("Ověřit kopie zpětným čtením (xxHash64)", isOn: $model.settings.verify)
            if !model.settings.verify {
                Label("Bez ověření není jisté, že data na disku jsou v pořádku.", systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(.orange)
            }
            Toggle("Analýza klipů a náhledy v reportu", isOn: $model.settings.analyzeClips)
        }
    }
}

struct ProxySection: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionHeader(title: "Proxy", systemImage: "film.stack")
            Toggle("Renderovat proxy po ověření", isOn: $model.settings.proxy.enabled)
            if model.settings.proxy.enabled {
                Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 8) {
                    GridRow {
                        Text("Kodek").foregroundStyle(.secondary)
                        Picker("", selection: $model.settings.proxy.codec) {
                            ForEach(ProxyCodec.allCases) { Text($0.label).tag($0) }
                        }.labelsHidden()
                    }
                    GridRow {
                        Text("Výška").foregroundStyle(.secondary)
                        Picker("", selection: $model.settings.proxy.height) {
                            ForEach([540, 720, 1080, 2160], id: \.self) { Text("\($0)p").tag($0) }
                        }.labelsHidden().pickerStyle(.segmented)
                    }
                    if model.settings.proxy.codec == .h264 || model.settings.proxy.codec == .hevc {
                        GridRow {
                            Text("Datový tok").foregroundStyle(.secondary)
                            Stepper("\(model.settings.proxy.bitrateMbps) Mb/s", value: $model.settings.proxy.bitrateMbps, in: 2...50)
                        }
                    }
                    GridRow {
                        Text("Výřez").foregroundStyle(.secondary)
                        HStack {
                            Picker("", selection: $model.settings.proxy.frameAspect) {
                                Text("celý obraz").tag(Double?.none)
                                ForEach([1.33, 1.66, 1.78, 1.85, 2.0, 2.39], id: \.self) { a in
                                    Text(String(format: "%.2f", a)).tag(Double?.some(a))
                                }
                            }.labelsHidden().frame(maxWidth: 130)
                            if model.settings.proxy.frameAspect != nil {
                                Stepper("\(Int((model.settings.proxy.frameScale * 100).rounded())) % obrazu",
                                        value: $model.settings.proxy.frameScale, in: 0.5...1.0, step: 0.01)
                                    .help("Velikost frame lines vůči obrazu – 90 % = 10 % overscan")
                            }
                        }
                    }
                    GridRow {
                        Text("LUT").foregroundStyle(.secondary)
                        HStack {
                            Text(model.settings.proxy.lutPath.map { ($0 as NSString).lastPathComponent } ?? "bez LUT")
                                .lineLimit(1).truncationMode(.middle)
                                .foregroundStyle(model.settings.proxy.lutPath == nil ? .secondary : .primary)
                            Spacer()
                            Button("Vybrat…") { model.pickLUT() }
                            if model.settings.proxy.lutPath != nil {
                                Button { model.settings.proxy.lutPath = nil } label: { Image(systemName: "xmark.circle.fill") }
                                    .buttonStyle(.borderless)
                            }
                        }
                    }
                }
                Toggle("Burn-in názvu klipu", isOn: $model.settings.proxy.burnFilename)
                Toggle("Burn-in timecodu", isOn: $model.settings.proxy.burnTimecode)
                Toggle("Zvuk", isOn: $model.settings.proxy.audio)
                Toggle("Přeskočit proxy nahrané kamerou (SUB/PROXY)", isOn: $model.settings.proxy.skipCameraProxies)
                Toggle("Kopírovat proxy na všechny cíle", isOn: $model.settings.proxy.copyToAllDestinations)
            }
        }
    }
}

struct ToolsSection: View {
    @EnvironmentObject var model: AppModel
    @State private var expanded = false

    var body: some View {
        DisclosureGroup(isExpanded: $expanded) {
            VStack(alignment: .leading, spacing: 6) {
                ToolRow(name: "ffmpeg", path: model.tools.ffmpeg, purpose: "proxy, náhledy", keyPath: \.ffmpeg)
                ToolRow(name: "ffprobe", path: model.tools.ffprobe, purpose: "metadata klipů", keyPath: \.ffprobe)
                ToolRow(name: "braw-decode", path: model.tools.brawDecoder, purpose: "Blackmagic RAW", keyPath: \.brawDecoder)
                ToolRow(name: "REDline", path: model.tools.redline, purpose: "RED R3D", keyPath: \.redline)
                ToolRow(name: "art-cmd", path: model.tools.artCmd, purpose: "ARRIRAW", keyPath: \.artCmd)
                Text("Canon Cinema RAW Light a Sony X-OCN se zkopírují a ověří, proxy pro ně na macOS Intel nejde vyrobit.")
                    .font(.caption).foregroundStyle(.secondary).padding(.top, 4)
            }
            .padding(.top, 6)
        } label: {
            Label("Dekodéry", systemImage: "wrench.and.screwdriver").font(.headline)
        }
    }
}

struct ToolRow: View {
    @EnvironmentObject var model: AppModel
    let name: String
    let path: String?
    let purpose: String
    let keyPath: WritableKeyPath<ExternalTools, String?>

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: path != nil ? "checkmark.circle.fill" : "xmark.circle")
                .foregroundStyle(path != nil ? .green : .secondary)
            VStack(alignment: .leading, spacing: 1) {
                Text(name).font(.callout.weight(.medium)) + Text("  \(purpose)").font(.caption).foregroundColor(.secondary)
                Text(path ?? "nenalezeno").font(.caption2).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            }
            Spacer()
            Button("…") { model.pickTool(keyPath) }.buttonStyle(.borderless).help("Vybrat cestu k nástroji")
        }
    }
}

struct StartBar: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        let n = model.selectedSources.count
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(n == 0 ? "Vyberte kartu" : (n == 1 ? "1 karta" : "\(n) karty"))
                    .fontWeight(.medium)
                Text("\(model.settings.destRoots.count) cíle\(model.settings.proxy.enabled ? " · proxy" : "")")
                    .font(.caption).foregroundStyle(.secondary)
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
                if model.jobs.contains(where: { $0.status.phase.isFinished }) {
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
        switch item.status.phase {
        case .done: return item.status.copySucceeded == false ? .red : .green
        case .failed: return .red
        case .cancelled: return .orange
        case .waiting: return .secondary
        default: return .accentColor
        }
    }

    var body: some View {
        let s = item.status
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Image(systemName: "sdcard.fill").foregroundStyle(phaseColor)
                Text(item.plan?.cardName ?? item.sourceName).font(.headline)
                if let p = item.plan {
                    Text("\(p.scan.files.count) souborů · \(formatBytes(p.scan.totalBytes))").font(.callout).foregroundStyle(.secondary)
                }
                Spacer()
                Text(statusText).font(.caption.weight(.semibold))
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
                Text(detailText).font(.callout.monospacedDigit()).foregroundStyle(.secondary).lineLimit(1)
            }
            if s.safeToEject && !item.ejected {
                HStack {
                    Label("Všechny soubory jsou na \(item.plan?.cardPaths.count ?? 0) discích a ověřené — kartu lze vysunout.",
                          systemImage: "checkmark.shield.fill")
                        .foregroundStyle(.green).font(.callout)
                    Spacer()
                    Button("Vysunout kartu") { model.eject(item) }
                }
            } else if item.ejected {
                Label("Karta vysunuta", systemImage: "eject.fill").foregroundStyle(.secondary).font(.callout)
            }
            if let report = item.report, !report.success {
                Label("\(report.failedFiles.count) souborů selhalo — karta NENÍ bezpečně zálohovaná. Podrobnosti v reportu.",
                      systemImage: "xmark.octagon.fill")
                    .foregroundStyle(.red).font(.callout)
            }
            if let e = s.error {
                Label(e, systemImage: "xmark.octagon.fill").foregroundStyle(.red).font(.callout)
            }
            if s.phase.isFinished, let r = item.report {
                Text(summaryText(r)).font(.caption).foregroundStyle(.secondary)
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

    var statusText: String {
        let s = item.status
        if s.phase == .done && s.copySucceeded == false { return "Chyba" }
        return s.phase.label
    }

    var detailText: String {
        let s = item.status
        let mb = { (v: Double) in String(format: "%.0f MB/s", v / 1_000_000) }
        let eta = item.eta.map { t -> String in
            let i = Int(t)
            return " · zbývá " + (i >= 3600 ? String(format: "%d:%02d:%02d", i / 3600, i / 60 % 60, i % 60) : String(format: "%d:%02d", i / 60, i % 60))
        } ?? ""
        switch s.phase {
        case .copying:
            let dests = item.destRates.enumerated().map { "D\($0.offset + 1) \(mb($0.element))" }.joined(separator: " · ")
            let file = s.copy.map { ($0.currentFile as NSString).lastPathComponent } ?? ""
            return "Karta \(mb(item.readRate)) · \(dests)\(eta) · \(file)"
        case .verifying:
            return item.destRates.enumerated().map { "D\($0.offset + 1) \(mb($0.element))" }.joined(separator: " · ") + eta
        case .analyzing:
            return "Klip \(s.clipIndex + 1)/\(s.clipCount) · \(s.clipName)"
        case .proxies:
            return String(format: "Proxy %d/%d · %@ · %.0f %%", s.clipIndex + 1, s.clipCount, s.clipName, s.clipFraction * 100)
        default:
            return s.phase.label + "…"
        }
    }

    func summaryText(_ r: OffloadReport) -> String {
        var parts = ["kopie \(formatRate(r.totalBytes, r.copySeconds))"]
        if r.verified { parts.append("ověření \(formatRate(r.totalBytes, r.verifySeconds))") }
        if !r.clips.isEmpty { parts.append("\(r.clips.count) klipů") }
        if r.proxy?.enabled == true {
            let count = { (st: ProxyStatus) in r.clips.filter { $0.proxyStatus == st }.count }
            var proxy = "proxy \(count(.done))"
            if count(.failed) > 0 { proxy += ", \(count(.failed)) chyba" }
            if count(.unsupported) > 0 { proxy += ", \(count(.unsupported)) nepodporováno" }
            parts.append(proxy)
        }
        return parts.joined(separator: " · ")
    }
}
