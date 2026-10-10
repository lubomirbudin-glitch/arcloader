import AppKit
import OffloadKit
import SwiftUI

/// The right column (UI návrh obr. 1–3): the queue with one card per job by its state — running (blue, phase
/// indicator and speed table), waiting (compact), done (green, safe to eject), problem (yellow), failed (red),
/// cancelled (grey); an empty queue shows the flow Card → disks → Verify → Proxy → Report.
///
/// Not on the Mac yet (hidden): the "Card history" tab and "Today" table (no card history), Pause, "Copy again"
/// (repair), the time estimate for the whole job.
struct QueueColumn: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    /// Running first, then waiting in queue order, then finished ones newest first.
    private var ordered: [JobItem] {
        let active = model.jobs.filter { !$0.queueFinished && $0.queuePhase != .waiting }
        let waiting = model.jobs.filter { $0.queuePhase == .waiting }
        let finished = model.jobs.filter(\.queueFinished).reversed()
        return active + waiting + finished
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            tabs.padding(.horizontal, 28).padding(.top, 22).padding(.bottom, 18)
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    if model.jobs.isEmpty {
                        EmptyQueue()
                    } else {
                        ForEach(ordered) { item in
                            JobCardContainer(item: item)
                        }
                    }
                    // TODO(history): "Today" — the cards backed up today, from the card history (Windows plan 9).
                }
                .padding(.horizontal, 28).padding(.bottom, 28)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .background(p.bg)
    }

    private var tabs: some View {
        HStack(spacing: 4) {
            let open = model.jobs.filter { !$0.queueFinished }.count
            HStack(spacing: 6) {
                Text("Queue").font(.ui(13, .semibold)).foregroundStyle(p.text)
                if open > 0 { Text("\(open)").font(.ui(13, .semibold)).foregroundStyle(p.accentText) }
            }
            .padding(.horizontal, 14).frame(height: 32)
            .background(RoundedRectangle(cornerRadius: 8).fill(p.tabActive))
            // TODO(history): the "Card history" tab once the Mac keeps a card history.
            Spacer()
            if model.jobs.contains(where: \.queueFinished) {
                Button("Clear finished") { model.clearFinished() }.buttonStyle(.offload(.ghost, height: 32))
            }
        }
    }
}

/// The flow of an empty queue and one sentence on what Start will do.
struct EmptyQueue: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        let cards = model.selectedInOrder
        let disks = model.settings.destRoots.count
        let s = model.settings
        var steps: [(String, String)] = [("sdcard", "Card"), ("externaldrive", disks <= 1 ? "1 disk" : "\(disks) disks at once")]
        if s.verify { steps.append(("checkmark.shield", "Verify")) }
        if s.proxy.enabled { steps.append(("play.rectangle", "Proxy")) } else if s.analyzeClips { steps.append(("photo.on.rectangle", "Analysis")) }
        steps.append(("doc.text", "Report"))
        let ready = !cards.isEmpty && disks > 0
        return VStack(spacing: 26) {
            HStack(alignment: .top, spacing: 10) {
                ForEach(Array(steps.enumerated()), id: \.offset) { i, step in
                    if i > 0 { Rectangle().fill(p.flowLine).frame(width: 34, height: 1).padding(.top, 26) }
                    VStack(spacing: 8) {
                        IconTile(systemImage: step.0, size: 52, radius: 14, iconSize: 22,
                                 fg: i == 0 ? p.accentText : p.text2, bg: i == 0 ? p.accentSoft : p.raised,
                                 border: i == 0 ? .clear : p.lineStrong)
                        Text(step.1).font(.ui(13)).foregroundStyle(p.text2).multilineTextAlignment(.center)
                    }
                    .frame(width: 92)
                }
            }
            VStack(spacing: 6) {
                Text(ready ? "Ready — \(cards.count == 1 ? model.sourceName(cards[0]) : Fmt.count(cards.count, "card")) to \(Fmt.count(disks, "disk"))"
                     : "No offload yet")
                    .font(.ui(18, .semibold)).foregroundStyle(p.text)
                Text(ready ? sentence(disks: disks, verify: s.verify) : "Pick a card and destinations on the left, then press ⌘↩.")
                    .font(.ui(14)).foregroundStyle(p.text2).multilineTextAlignment(.center).lineSpacing(3)
                    .frame(maxWidth: 460)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 44).padding(.horizontal, 32)
        .background(RoundedRectangle(cornerRadius: Metrics.cardRadius).fill(p.bgHeader))
        .overlay(RoundedRectangle(cornerRadius: Metrics.cardRadius).strokeBorder(p.lineSoft, lineWidth: 1))
    }

    private func sentence(disks: Int, verify: Bool) -> String {
        let write = disks == 1 ? "written to the disk" : disks == 2 ? "written to both disks at once" : "written to all \(disks) disks at once"
        let check = verify ? " and every file is read back and verified" : "; read-back verification is off"
        return "The card is read once, \(write)\(check). Insert the next card any time — it joins the queue."
    }
}

/// Rebuilds the card's state from its job on every tick.
struct JobCardContainer: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject var item: JobItem

    var body: some View {
        let state = JobCardState.make(item, model: model)
        JobCardView(state: state, actions: JobCardActions(
            cancel: { model.cancel(item) },
            eject: { model.eject(item) },
            report: { model.openReport(item) },
            reveal: { model.reveal(item) },
            proxies: { model.revealProxies(item) }))
    }
}

struct JobCardActions {
    var cancel: () -> Void = {}
    var eject: () -> Void = {}
    var report: () -> Void = {}
    var reveal: () -> Void = {}
    var proxies: () -> Void = {}
}

struct JobCardView: View {
    @Environment(\.palette) private var p
    let state: JobCardState
    let actions: JobCardActions

    var body: some View {
        switch state.kind {
        case .waiting: WaitingCard(state: state, actions: actions)
        case .running: RunningCard(state: state, actions: actions)
        case .done: DoneCard(state: state, actions: actions)
        case .problem, .failed, .cancelled: ProblemCard(state: state, actions: actions)
        }
    }
}

// MARK: - Running

struct RunningCard: View {
    @Environment(\.palette) private var p
    let state: JobCardState
    let actions: JobCardActions

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 22) {
                header
                if !state.steps.isEmpty { phases }
                if !state.rows.isEmpty { speedTable }
                footer
                if state.safeWhileRunning { safeStrip }
            }
            .padding(.horizontal, 24).padding(.vertical, 22)
            .background(RoundedRectangle(cornerRadius: Metrics.cardRadius).fill(p.runBg))
            .overlay(RoundedRectangle(cornerRadius: Metrics.cardRadius).strokeBorder(p.runBorder, lineWidth: 1))
            if let brake = state.brake {
                HStack(spacing: 10) {
                    Image(systemName: "info.circle").font(.system(size: 13))
                    Text(brake)
                }
                .font(.ui(13)).foregroundStyle(p.text3)
                .padding(.horizontal, 4).padding(.top, 4)
            }
        }
    }

    private var header: some View {
        HStack(alignment: .center, spacing: 14) {
            IconTile(systemImage: "sdcard", size: 42, radius: 10, iconSize: 20, fg: p.accentText, bg: p.accentSoft)
            VStack(alignment: .leading, spacing: 2) {
                Text(state.name).font(.ui(18, .semibold)).foregroundStyle(p.text).lineLimit(1)
                Text(state.sub).font(.ui(13)).foregroundStyle(p.text2).lineLimit(1)
            }
            Spacer(minLength: 12)
            VStack(alignment: .trailing, spacing: 2) {
                if let percent = state.percent {
                    Text("\(percent) %").font(.system(size: 30, weight: .semibold).monospacedDigit()).foregroundStyle(p.text)
                }
                if let remaining = state.remaining {
                    Text(remaining).font(.ui(13)).foregroundStyle(p.text2)
                }
            }
        }
    }

    private var phases: some View {
        HStack(alignment: .top, spacing: 8) {
            ForEach(state.steps) { step in
                VStack(alignment: .leading, spacing: 8) {
                    ThinBar(height: 4, segments: [(step.fill, p.accentBar)])
                    Text(step.text)
                        .font(.ui(13, step.state == .active ? .semibold : .regular))
                        .foregroundStyle(step.state == .active ? p.text : step.state == .done ? p.text2 : p.text3)
                        .lineLimit(1)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private var speedTable: some View {
        VStack(spacing: 0) {
            ForEach(Array(state.rows.enumerated()), id: \.element.id) { i, row in
                HStack(spacing: 16) {
                    HStack(spacing: 8) {
                        if let n = row.number {
                            NumberBadge(number: n, size: 18)
                        } else {
                            Image(systemName: "sdcard").font(.system(size: 12)).frame(width: 18)
                        }
                        Text(row.name).lineLimit(1).truncationMode(.middle)
                    }
                    .font(.ui(14)).foregroundStyle(p.textSoft)
                    .frame(width: 150, alignment: .leading)
                    ThinBar(segments: [(row.share, p.accentBar)])
                    Text(row.rate).font(.mono(13)).foregroundStyle(p.text).frame(width: 90, alignment: .trailing)
                }
                .padding(.horizontal, 16).padding(.vertical, 12)
                .overlay(alignment: .bottom) { if i < state.rows.count - 1 { Rectangle().fill(p.rowLine).frame(height: 1) } }
                .help(row.tooltip ?? "")
            }
        }
        .background(RoundedRectangle(cornerRadius: Metrics.tileRadius).fill(p.input))
        .overlay(RoundedRectangle(cornerRadius: Metrics.tileRadius).strokeBorder(p.lineSoft, lineWidth: 1))
    }

    private var footer: some View {
        HStack(alignment: .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text(state.fileCaption).font(.ui(12)).foregroundStyle(p.text3)
                if !state.fileValue.isEmpty {
                    Text(state.fileValue).font(.mono(13)).foregroundStyle(p.textSoft).lineLimit(1).truncationMode(.middle)
                }
            }
            Spacer(minLength: 12)
            // TODO(pause): "Pause" once the Mac engine can pause (Windows plan 9, Mac task 61).
            if state.canCancel {
                Button("Cancel", action: actions.cancel).buttonStyle(.offload(.danger, height: 36))
            }
        }
    }

    /// Proxies still render, but the copy is verified: the card may go.
    private var safeStrip: some View {
        HStack(spacing: 12) {
            Image(systemName: "checkmark.shield.fill").foregroundStyle(p.success)
            Text(state.ejected ? "Card ejected — the copy is verified." : "The copy is verified — safe to eject the card.")
                .font(.ui(13)).foregroundStyle(p.success)
            Spacer(minLength: 8)
            if state.showEject {
                Button(state.ejecting ? "Ejecting…" : "Eject card", action: actions.eject)
                    .buttonStyle(.offload(.successSoft, height: 32))
                    .disabled(state.ejecting)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 10).fill(p.successBg))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(p.successBorder, lineWidth: 1))
    }
}

// MARK: - Waiting

struct WaitingCard: View {
    @Environment(\.palette) private var p
    let state: JobCardState
    let actions: JobCardActions

    var body: some View {
        HStack(spacing: 14) {
            IconTile(systemImage: "sdcard", size: 34, radius: 9, iconSize: 16, fg: p.text2, bg: p.chip)
            VStack(alignment: .leading, spacing: 2) {
                Text(state.name).font(.ui(14, .semibold)).foregroundStyle(p.text).lineLimit(1)
                Text(state.sub).font(.ui(13)).foregroundStyle(p.text2).lineLimit(1)
            }
            Spacer(minLength: 12)
            Pill(text: "Waiting in queue", fg: p.textSoft, bg: p.chip)
            if state.canCancel {
                Button("Cancel", action: actions.cancel).buttonStyle(.offload(.danger, height: 30))
            }
        }
        .padding(.horizontal, 18).padding(.vertical, 14)
        .background(RoundedRectangle(cornerRadius: 14).fill(p.bgHeader))
        .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(p.lineSoft, lineWidth: 1))
    }
}

// MARK: - Done (green)

struct DoneCard: View {
    @Environment(\.palette) private var p
    let state: JobCardState
    let actions: JobCardActions

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .center, spacing: 18) {
                IconTile(systemImage: "checkmark.shield", size: 52, radius: 14, iconSize: 24, fg: p.onSuccess, bg: p.successFill)
                VStack(alignment: .leading, spacing: 4) {
                    Text(state.title).font(.ui(20, .semibold)).foregroundStyle(p.text)
                    Text(state.text).font(.ui(14)).foregroundStyle(p.successText2).fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 12)
                if state.ejected {
                    Label("Card ejected", systemImage: "eject.fill").font(.ui(14, .medium)).foregroundStyle(p.success)
                } else if state.showEject {
                    Button(action: actions.eject) {
                        Label(state.ejecting ? "Ejecting…" : "Eject card", systemImage: "eject")
                    }
                    .buttonStyle(.offload(.success, height: 44))
                    .disabled(state.ejecting)
                }
            }
            .padding(.horizontal, 24).padding(.vertical, 22)
            if !state.summary.isEmpty {
                divider
                HStack(alignment: .top, spacing: 12) {
                    ForEach(state.summary) { f in
                        VStack(alignment: .leading, spacing: 2) {
                            Text(f.label).font(.ui(12)).foregroundStyle(p.successLabel)
                            Text(f.value).font(.system(size: 18, weight: .semibold).monospacedDigit()).foregroundStyle(p.text)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal, 24).padding(.vertical, 18)
            }
            divider
            VStack(alignment: .leading, spacing: 14) {
                if !state.thumbs.isEmpty { thumbs }
                CardButtons(state: state, actions: actions, tone: .success)
            }
            .padding(.horizontal, 24).padding(.vertical, 18)
        }
        .background(RoundedRectangle(cornerRadius: Metrics.cardRadius).fill(p.successBg))
        .overlay(RoundedRectangle(cornerRadius: Metrics.cardRadius).strokeBorder(p.successBorder, lineWidth: 1))
    }

    private var divider: some View { Rectangle().fill(p.successLine).frame(height: 1) }

    private var thumbs: some View {
        HStack(spacing: 8) {
            ForEach(state.thumbs) { t in
                ZStack(alignment: .bottomLeading) {
                    Rectangle().fill(p.thumbBg)
                    if let image = t.image {
                        Image(nsImage: image).resizable().scaledToFill()
                    }
                    Text(t.clip).font(.mono(10)).foregroundStyle(Color.white)
                        .padding(.horizontal, 4).padding(.vertical, 1)
                        .background(p.shade)
                        .lineLimit(1)
                        .padding(5)
                }
                .aspectRatio(16 / 9, contentMode: .fit)
                .frame(maxWidth: .infinity)
                .clipShape(RoundedRectangle(cornerRadius: 6))
            }
            if state.moreThumbs > 0 {
                Color.clear.aspectRatio(16 / 9, contentMode: .fit).frame(maxWidth: .infinity)
                    .overlay(
                        Text("+\(state.moreThumbs)").font(.ui(12)).foregroundStyle(p.successText2)
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                            .background(RoundedRectangle(cornerRadius: 6).fill(p.successBtnBg))
                            .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(p.successBtnBorder, lineWidth: 1)))
            }
            ForEach(0..<max(0, 6 - state.thumbs.count - (state.moreThumbs > 0 ? 1 : 0)), id: \.self) { _ in
                Color.clear.frame(maxWidth: .infinity).aspectRatio(16 / 9, contentMode: .fit)
            }
        }
    }
}

/// Report · Show in Finder · Proxy, and the card folder on the right.
struct CardButtons: View {
    @Environment(\.palette) private var p
    enum Tone { case success, neutral }
    let state: JobCardState
    let actions: JobCardActions
    let tone: Tone

    var body: some View {
        HStack(spacing: 8) {
            if state.hasReport {
                Button(action: actions.report) { Label("Report", systemImage: "doc.text").lineLimit(1) }.fixedSize()
                Button(action: actions.reveal) { Label("Show in Finder", systemImage: "folder").lineLimit(1) }.fixedSize()
            }
            if state.hasProxies {
                Button(action: actions.proxies) { Label("Proxy", systemImage: "play.rectangle").lineLimit(1) }.fixedSize()
            }
            Spacer(minLength: 8)
            if let path = state.path {
                Text(path).font(.mono(12)).foregroundStyle(tone == .success ? p.successLabel : p.text3)
                    .lineLimit(1).truncationMode(.middle)
            }
        }
        .buttonStyle(.offload(tone == .success ? .successSoft : .secondary, height: 36))
    }
}

// MARK: - Problem (yellow), failed (red), cancelled (grey)

struct ProblemCard: View {
    @Environment(\.palette) private var p
    let state: JobCardState
    let actions: JobCardActions

    private var colors: (bg: Color, border: Color, fill: Color, onFill: Color, text2: Color, icon: String) {
        switch state.kind {
        case .failed: return (p.dangerBg, p.dangerCardBorder, p.danger, p.onWarn, p.dangerText, "xmark.octagon")
        case .cancelled: return (p.bgHeader, p.lineSoft, p.chip, p.text2, p.text2, "stop.circle")
        default: return (p.warnBg, p.warnBorder, p.warnFill, p.onWarn, p.warnText2, "exclamationmark.triangle")
        }
    }

    var body: some View {
        let c = colors
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .center, spacing: 14) {
                IconTile(systemImage: c.icon, size: 40, radius: 11, iconSize: 19, fg: c.onFill, bg: c.fill)
                VStack(alignment: .leading, spacing: 3) {
                    Text(state.title).font(.ui(16, .semibold)).foregroundStyle(p.text)
                    Text(state.text).font(.ui(14)).foregroundStyle(c.text2).fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                }
                Spacer(minLength: 12)
                // TODO(repair): "Copy again" (only the failed files, only where they failed) once the Mac can repair.
            }
            if !state.failed.isEmpty { failedList }
            if !state.summary.isEmpty && state.kind == .problem {
                Text(state.summary.map { "\($0.label) \($0.value)" }.joined(separator: " · "))
                    .font(.ui(12)).foregroundStyle(p.text3)
            }
            if state.hasReport || state.hasProxies {
                CardButtons(state: state, actions: actions, tone: .neutral)
            }
        }
        .padding(.horizontal, 22).padding(.vertical, 18)
        .background(RoundedRectangle(cornerRadius: Metrics.cardRadius).fill(c.bg))
        .overlay(RoundedRectangle(cornerRadius: Metrics.cardRadius).strokeBorder(c.border, lineWidth: 1))
    }

    private var failedList: some View {
        VStack(alignment: .leading, spacing: 8) {
            ForEach(state.failed) { f in
                HStack(alignment: .firstTextBaseline, spacing: 12) {
                    Text(f.path).foregroundStyle(p.text).lineLimit(1).truncationMode(.middle)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .help(f.path)
                    ForEach(Array(f.disks.enumerated()), id: \.offset) { _, d in
                        Text(d.text).foregroundStyle(d.ok ? p.successText2 : p.dangerText2).lineLimit(1)
                            .frame(minWidth: 110, alignment: .leading)
                    }
                }
            }
            if let more = state.moreFailed {
                Text(more).foregroundStyle(p.text3)
            }
        }
        .font(.mono(12))
        .padding(.horizontal, 14).padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 10).fill(p.warnListBg))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(p.warnListBorder, lineWidth: 1))
    }
}
