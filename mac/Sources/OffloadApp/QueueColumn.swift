import AppKit
import OffloadKit
import SwiftUI

/// The right column (UI návrh obr. 1–3): the queue with one card per job by its state — running (blue, phase
/// indicator and speed table), waiting (compact), done (green, safe to eject), problem (yellow), failed (red),
/// cancelled (grey); an empty queue shows the flow Card → disks → Verify → Proxy → Report.
///
/// Verify backup items (⇧⌘O) are cards of the same queue — they run one after another with the copies.
/// The "Card history" tab and "Today" come from history.json. Not on the Mac yet: "Copy again" (repair), the time estimate for
/// the whole job.
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
            if model.queueTab == .history {
                CardHistoryTab()
            } else {
                queue
            }
        }
        .background(p.bg)
    }

    private var queue: some View {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    if model.jobs.isEmpty && model.verifies.isEmpty {
                        EmptyQueue()
                    } else {
                        ForEach(model.verifies.filter { !$0.isFinished }) { item in
                            VerifyCardContainer(item: item)
                        }
                        ForEach(ordered) { item in
                            JobCardContainer(item: item)
                        }
                        ForEach(model.verifies.filter(\.isFinished).reversed()) { item in
                            VerifyCardContainer(item: item)
                        }
                    }
                    // Today (new item 7): the cards finished today, from history.json.
                    if let day = model.today { TodaySection(day: day).padding(.top, 4) }
                }
                .padding(.horizontal, 28).padding(.bottom, 28)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
    }

    private var tabs: some View {
        HStack(spacing: 4) {
            let open = model.jobs.filter { !$0.queueFinished }.count + model.verifies.filter { !$0.isFinished }.count
            tab(.queue) {
                HStack(spacing: 6) {
                    Text("Queue").font(.ui(13, .semibold)).foregroundStyle(model.queueTab == .queue ? p.text : p.text2)
                    if open > 0 { Text("\(open)").font(.ui(13, .semibold)).foregroundStyle(p.accentText) }
                }
            }
            tab(.history) {
                Text("Card history").font(.ui(13, .semibold)).foregroundStyle(model.queueTab == .history ? p.text : p.text2)
            }
            Spacer(minLength: 8)
            // A narrow window: "Clear selection" becomes an ✕ (its text as the tooltip).
            ViewThatFits(in: .horizontal) {
                actions(compact: false)
                actions(compact: true)
            }
        }
    }

    private func actions(compact: Bool) -> some View {
        HStack(spacing: 4) {
            if !model.summarySelection.isEmpty {
                if compact {
                    IconButton(systemImage: "xmark", help: "Clear selection") { model.clearSummarySelection() }
                } else {
                    Button("Clear selection") { model.clearSummarySelection() }.buttonStyle(.offload(.ghost, height: 32))
                }
            }
            if model.queueTab == .history {
                // One report (HTML + PDF) from the ticked cards: days, clips, size, duration, first and last slate, disks.
                Button(model.summaryButtonText) { model.makeSummaryReport() }
                    .buttonStyle(.offload(.ghost, height: 32))
                    .disabled(model.summarySelection.isEmpty || model.makingSummary)
                    .help("One report (HTML + PDF) from the ticked cards: days, clips, size, duration, first and last slate, where everything is backed up")
                Button("Day report…") { model.dayReportOnRequest() }.buttonStyle(.offload(.ghost, height: 32))
            }
            if model.queueTab == .queue,
               model.jobs.contains(where: \.queueFinished) || model.verifies.contains(where: \.isFinished) {
                Button("Clear finished") { model.clearFinished() }.buttonStyle(.offload(.ghost, height: 32))
            }
        }
        .lineLimit(1)
        .fixedSize()
    }

    private func tab<L: View>(_ t: QueueTab, @ViewBuilder label: () -> L) -> some View {
        Button { model.queueTab = t } label: {
            label()
                .lineLimit(1).fixedSize()
                .padding(.horizontal, 14).frame(height: 32)
                .background(RoundedRectangle(cornerRadius: 8).fill(model.queueTab == t ? p.tabActive : .clear))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

/// "TODAY" under the queue: summary on the right, one row per card.
struct TodaySection: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    let day: HistoryDay

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            StepTitle(title: day.title) {
                HStack(spacing: 14) {
                    Text(day.summary).font(.ui(13)).foregroundStyle(p.text3).lineLimit(1)
                    if !model.summarySelection.isEmpty || model.makingSummary {
                        LinkButton(title: model.summaryButtonText) { model.makeSummaryReport() }
                            .disabled(model.makingSummary)
                    }
                    LinkButton(title: "Day report…") { model.dayReportOnRequest() }
                }
            }
            HistoryRows(rows: day.rows)
        }
    }
}

/// The "Card history" tab: every day with a card, newest first; a card can be ticked for the summary report.
struct CardHistoryTab: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                let days = model.historyDays
                if model.history == nil {
                    Text("Loading the card history…").font(.ui(14)).foregroundStyle(p.text2)
                } else if days.isEmpty {
                    Text("No card backed up yet.").font(.ui(14)).foregroundStyle(p.text2)
                }
                ForEach(days) { day in
                    VStack(alignment: .leading, spacing: 10) {
                        StepTitle(title: day.title) {
                            Text(day.summary).font(.ui(13)).foregroundStyle(p.text3)
                        }
                        HistoryRows(rows: day.rows)
                    }
                }
            }
            .padding(.horizontal, 28).padding(.bottom, 28)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

/// One bordered list of history rows: tick · name · size · time · state; files and disks under the name.
struct HistoryRows: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.palette) private var p
    let rows: [HistoryRow]

    var body: some View {
        VStack(spacing: 0) {
            ForEach(Array(rows.enumerated()), id: \.element.id) { i, row in
                HStack(alignment: .center, spacing: 12) {
                    Toggle("", isOn: Binding(get: { model.isInSummary(row.id) }, set: { model.setInSummary(row.id, $0) }))
                        .labelsHidden().toggleStyle(.offloadCheckbox)
                        .help("Select for the summary report")
                    VStack(alignment: .leading, spacing: 2) {
                        Text(row.cardName).font(.ui(14, .semibold)).foregroundStyle(p.text)
                            .lineLimit(1).truncationMode(.middle)
                        Text(([row.files, row.disks] + (row.project.map { ["project \($0)"] } ?? [])).filter { !$0.isEmpty }
                                .joined(separator: " · "))
                            .font(.ui(12)).foregroundStyle(p.text3).lineLimit(1).truncationMode(.tail)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    Text(row.size).font(.ui(14)).foregroundStyle(p.text2).frame(width: 78, alignment: .leading)
                    Text(row.time).font(.ui(14)).foregroundStyle(p.text2).frame(width: 52, alignment: .leading)
                    Text(row.status).font(.ui(13)).foregroundStyle(color(row.tone))
                        .lineLimit(1).truncationMode(.tail)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .help(row.status)
                }
                .padding(.horizontal, 16).padding(.vertical, 11)
                .overlay(alignment: .bottom) {
                    if i < rows.count - 1 { Rectangle().fill(p.rowLine).frame(height: 1) }
                }
            }
        }
        .background(RoundedRectangle(cornerRadius: 12).fill(p.panel))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(p.line, lineWidth: 1))
    }

    private func color(_ t: HistoryTone) -> Color {
        switch t {
        case .ok: return p.success
        case .warning: return p.warn
        case .error: return p.dangerText
        case .neutral: return p.text2
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
            // A narrow window gets a tighter flow (the full one is ~680 pt wide).
            ViewThatFits(in: .horizontal) {
                flow(steps, step: 92, line: 34, spacing: 10)
                flow(steps, step: 64, line: 6, spacing: 5)
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

    private func flow(_ steps: [(String, String)], step: CGFloat, line: CGFloat, spacing: CGFloat) -> some View {
        HStack(alignment: .top, spacing: spacing) {
            ForEach(Array(steps.enumerated()), id: \.offset) { i, s in
                if i > 0 { Rectangle().fill(p.flowLine).frame(width: line, height: 1).padding(.top, 26) }
                VStack(spacing: 8) {
                    IconTile(systemImage: s.0, size: 52, radius: 14, iconSize: 22,
                             fg: i == 0 ? p.accentText : p.text2, bg: i == 0 ? p.accentSoft : p.raised,
                             border: i == 0 ? .clear : p.lineStrong)
                    Text(s.1).font(.ui(13)).foregroundStyle(p.text2).multilineTextAlignment(.center)
                }
                .frame(width: step)
            }
        }
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
            pause: { model.pause(item) },
            resume: { model.resume(item) },
            eject: { model.eject(item) },
            report: { model.openReport(item) },
            reveal: { model.reveal(item) },
            proxies: { model.revealProxies(item) },
            confirmContent: { item.contentConfirmed = true }))
    }
}

struct JobCardActions {
    var cancel: () -> Void = {}
    var pause: () -> Void = {}
    var resume: () -> Void = {}
    var eject: () -> Void = {}
    var report: () -> Void = {}
    var reveal: () -> Void = {}
    var proxies: () -> Void = {}
    var confirmContent: () -> Void = {}
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
                VerdictChips(state: state)
                if !state.steps.isEmpty { phases }
                if !state.rows.isEmpty { speedTable }
                if state.pauseText != nil { pauseLine }
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
            // Pause lets the file or clip in progress finish; Resume goes on with the next one (Windows 0.8.0).
            if state.canPause {
                if state.paused {
                    Button("Resume", action: actions.resume).buttonStyle(.offload(.secondary, height: 36))
                } else {
                    Button("Pause", action: actions.pause).buttonStyle(.offload(.secondary, height: 36))
                }
            }
            if state.canCancel {
                Button("Cancel", action: actions.cancel).buttonStyle(.offload(.danger, height: 36))
            }
        }
    }

    /// "Pausing (finishing file A001C003.mov…)" → "Paused — Resume continues with the next file."
    private var pauseLine: some View {
        HStack(spacing: 10) {
            Image(systemName: state.pauseText?.hasPrefix("Cancelling") == true ? "xmark.circle"
                  : state.pauseText?.hasPrefix("Paused") == true ? "pause.circle.fill" : "pause.circle")
                .font(.system(size: 14))
            Text(state.pauseText ?? "")
        }
        .font(.ui(13)).foregroundStyle(p.warn)
    }

    /// Proxies still render, but the copy is verified: the card may go.
    /// Green only when the card may be formatted too (spec D.22.7); copied alone is neutral.
    private var safeStrip: some View {
        let green = state.safeFormat
        let text = state.ejected
            ? (green ? "Card ejected — the copy is verified." : "Card ejected — don't format it until the copy is verified.")
            : (green ? "The copy is verified — the card is safe to eject and format."
                     : "Everything is copied — the card is safe to eject. Don't format it yet.")
        return HStack(spacing: 12) {
            Image(systemName: green ? "checkmark.shield.fill" : "shield.lefthalf.filled").foregroundStyle(green ? p.success : p.text2)
            Text(text).font(.ui(13)).foregroundStyle(green ? p.success : p.text2)
            Spacer(minLength: 8)
            if state.showEject {
                Button(state.ejecting ? "Ejecting…" : "Eject card", action: actions.eject)
                    .buttonStyle(.offload(green ? .successSoft : .secondary, height: 32))
                    .disabled(state.ejecting)
            }
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 10).fill(green ? p.successBg : p.chip))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(green ? p.successBorder : p.lineSoft, lineWidth: 1))
    }
}

/// The card row's two verdicts (Windows R5), never merged: "Safe to eject" and "Safe to format" — green only when the
/// card may be formatted (JobProgress.chips).
struct VerdictChips: View {
    @Environment(\.palette) private var p
    let state: JobCardState

    var body: some View {
        if let eject = state.ejectChip, let format = state.formatChip {
            HStack(spacing: 8) {
                chip(eject)
                chip(format)
            }
        }
    }

    private func chip(_ c: CardChip) -> some View {
        let colors: (fg: Color, bg: Color)
        switch c.tone {
        case .ok: colors = (p.success, p.successBg)
        case .warning: colors = (p.warnSoftText, p.warnSoftBg)
        case .error: colors = (p.dangerText, p.dangerBg)
        case .waiting, .active: colors = (p.text2, p.chip)
        }
        return Pill(text: c.text, fg: colors.fg, bg: colors.bg)
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
            Pill(text: state.waitingHint ?? "Waiting in queue", fg: p.textSoft, bg: p.chip)
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
                    VerdictChips(state: state).padding(.top, 4)
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
                    if state.kind != .failed { VerdictChips(state: state).padding(.top, 4) }
                }
                Spacer(minLength: 12)
                // TODO(repair): "Copy again" (only the failed files, only where they failed) once the Mac can repair.
            }
            if !state.failed.isEmpty { failedList }
            if !state.defects.isEmpty { defectList }
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

    /// Task 70: the clips whose decoder reported a defect (the copy matches the card) and the explicit confirmation.
    private var defectList: some View {
        VStack(alignment: .leading, spacing: 8) {
            ForEach(Array(state.defects.enumerated()), id: \.offset) { _, line in
                Text(line).foregroundStyle(p.text).lineLimit(2).textSelection(.enabled)
            }
            if state.canConfirmContent {
                Button("Checked — the card can be formatted", action: actions.confirmContent)
                    .buttonStyle(.offload(.secondary, height: 32))
                    .help("The copy matches the card; the defects are in the camera files. Look at them in the report first.")
            }
        }
        .font(.ui(13))
        .padding(.horizontal, 14).padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 10).fill(p.warnListBg))
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

// MARK: - Verify backup

/// One Verify backup item of the queue (Windows QueuePanel verify template): title, state pill, progress while it runs,
/// the result (green / orange / red), orange warnings, the note on where the report went, and its buttons.
struct VerifyCardContainer: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject var item: VerifyItem
    @Environment(\.palette) private var p

    var body: some View {
        let s = item.status
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .center, spacing: 14) {
                IconTile(systemImage: "checkmark.shield", size: 40, radius: 10, iconSize: 19, fg: p.accentText, bg: p.accentSoft)
                VStack(alignment: .leading, spacing: 2) {
                    Text(VerifyJobView.title(item.job.cardName)).font(.ui(16, .semibold)).foregroundStyle(p.text)
                    Text(item.job.cardPath).font(.mono(12)).foregroundStyle(p.text3).lineLimit(1).truncationMode(.middle)
                        .help(item.job.cardPath)
                }
                Spacer(minLength: 12)
                Pill(text: VerifyJobView.statusText(s), fg: p.textSoft, bg: p.chip)
            }
            if s.state == .running {
                VStack(alignment: .leading, spacing: 8) {
                    ThinBar(height: 4, segments: [(VerifyJobView.fraction(s.progress), p.accentBar)])
                    Text(VerifyJobView.detail(s.progress, bytesPerSecond: item.rate)).font(.ui(13)).foregroundStyle(p.text2)
                }
            }
            if let result = VerifyJobView.result(s) {
                Text(result.text).font(.ui(14)).foregroundStyle(color(result.tone))
                    .fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
            }
            if let r = s.result {
                ForEach(Array(VerifyReport.warnings(r).enumerated()), id: \.offset) { _, w in
                    Text(w).font(.ui(13)).foregroundStyle(p.warn).fixedSize(horizontal: false, vertical: true)
                }
            }
            if let note = VerifyJobView.reportNote(s) {
                Text(note).font(.ui(13)).foregroundStyle(p.warn).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
            }
            HStack(spacing: 8) {
                if s.reportPath != nil {
                    Button { model.openVerifyReport(item) } label: { Label("Verification report", systemImage: "doc.text") }.fixedSize()
                }
                Button { model.revealVerify(item) } label: { Label("Show in Finder", systemImage: "folder") }.fixedSize()
                Spacer(minLength: 8)
                if !item.isFinished {
                    Button("Cancel") { model.cancelVerify(item) }.buttonStyle(.offload(.danger, height: 36))
                }
            }
            .buttonStyle(.offload(.secondary, height: 36))
        }
        .padding(.horizontal, 22).padding(.vertical, 18)
        .background(RoundedRectangle(cornerRadius: Metrics.cardRadius).fill(p.bgHeader))
        .overlay(RoundedRectangle(cornerRadius: Metrics.cardRadius).strokeBorder(p.lineSoft, lineWidth: 1))
    }

    private func color(_ tone: CardTone) -> Color {
        switch tone {
        case .ok: return p.success
        case .warning: return p.warn
        case .error: return p.dangerText
        case .waiting, .active: return p.text2
        }
    }
}
