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
                    SetupColumn()
                        .frame(minWidth: Metrics.leftMin, idealWidth: Metrics.leftIdeal, maxWidth: Metrics.leftMax)
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
                                .foregroundStyle(line.hasPrefix(ChainView.bottleneckPrefix) ? Color.orange : Color.secondary)
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
