import AppKit
import OffloadKit
import SwiftUI

/// Plan 6 in the app: the card history line, "Append", the name from the clips, the space check, the prompt before a
/// full copy of a backed-up card, and Verify backup. Everything that reads a disk runs off the main thread.
extension AppModel {
    // MARK: History and card inspection

    /// history.json, off the main thread; then every card is looked at again (its line follows).
    func loadHistory() {
        guard !isPreview else { return }
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let entries = HistoryStore().load()
            DispatchQueue.main.async {
                guard let self else { return }
                self.history = entries
                for path in self.allSources { self.inspect(path) }
            }
        }
    }

    /// Volumes that are not destinations, then folders added by hand — step 1's list.
    var allSources: [String] { volumes.filter { !$0.isDestination }.map(\.path) + manualSources }

    /// New sources are looked at; insights of sources that went away are dropped (and their Append tick).
    func syncInspections() {
        let current = Set(allSources)
        for gone in insights.keys where !current.contains(gone) { insights[gone] = nil }
        appendSelected = appendSelected.filter { current.contains($0) }
        for path in allSources where insights[path] == nil && !inspecting.contains(path) { inspect(path) }
        // A card swapped in the same slot: another identity than the one inspected.
        for v in volumes where !v.isDestination {
            if let i = insights[v.path], let id = i.volume?.identity, let now = v.identity, id != now { inspect(v.path) }
        }
    }

    /// One look at a card, off the main thread; one at a time per path — asked again meanwhile, it runs once more after.
    /// A result for a source that is no longer listed is thrown away.
    func inspect(_ path: String) {
        guard !isPreview else { return }
        if inspecting.contains(path) {
            inspectAgain.insert(path)
            return
        }
        inspecting.insert(path)
        objectWillChange.send()
        let entries = history
        inspectQueue.async { [weak self] in
            let insight = try? CardInspector.inspect(path, store: entries == nil ? nil : HistoryStore(), history: entries)
            DispatchQueue.main.async {
                guard let self else { return }
                self.inspecting.remove(path)
                if self.allSources.contains(path) {
                    self.insights[path] = insight
                    if !self.canAppend(path) { self.appendSelected.remove(path) }
                }
                if self.inspectAgain.remove(path) != nil { self.inspect(path) }
                self.objectWillChange.send()
            }
        }
    }

    /// The line under a card: "Backed up on … — verified, safe to format" in its tone; "Checking…" while looking.
    func historyLine(_ path: String) -> (text: String, tone: HistoryTone)? {
        if let i = insights[path] {
            if let e = i.error { return (e, .error) }
            if let m = i.match { return i.isEmptied ? HistoryText.emptied(m) : HistoryText.line(m) }
            return inspecting.contains(path) ? (HistoryText.inspecting, .neutral) : nil
        }
        return history == nil || inspecting.contains(path) ? (HistoryText.inspecting, .neutral) : nil
    }

    /// "24 clips · 1.70 GB" once the card was looked at.
    func clipFacts(_ path: String) -> String? {
        guard let i = insights[path], i.error == nil else { return nil }
        let clips = i.clips.filter { !$0.isCameraProxy }.count
        return (clips > 0 ? "\(Fmt.count(clips, "clip")) · " : "") + Fmt.bytes(i.totalBytes)
    }

    func canAppend(_ path: String) -> Bool { AppendRules.offer(insights[path]) }

    func appendChosen(_ path: String) -> Bool { appendSelected.contains(path) && canAppend(path) }

    func setAppend(_ path: String, _ on: Bool) {
        if on && canAppend(path) { appendSelected.insert(path) } else { appendSelected.remove(path) }
    }

    /// "Append new clips only to the earlier backup (12 files · 3.2 GB)".
    func appendText(_ path: String) -> String? {
        guard canAppend(path), let m = insights[path]?.match else { return nil }
        return AppendRules.checkboxText(m)
    }

    /// "Folder: 2026-10-06/A001 on 2 destinations" while Append is ticked.
    func appendFolderText(_ path: String) -> String? {
        guard appendChosen(path), let e = insights[path]?.match?.entry else { return nil }
        return AppendRules.folderText(e)
    }

    /// The names of these cards (typed, else suggested, else the default) — what the fields show and Start uses.
    func cardNames(for paths: [String]) -> [String: (name: String, hint: String?)] {
        let cards = paths.map { CardNameInput(key: $0, typed: cardNames[$0], insight: insights[$0], fallback: defaultName($0)) }
        return CardNaming.resolve(suggestFromClips: settings.naming.suggestFromClips, cards: cards, history: history,
                                  date: dateString, today: Layout.today())
    }

    /// The placeholder of the empty "Folder name" field and its grey hint.
    func namePlaceholder(_ path: String) -> (name: String, hint: String?) {
        let paths = selectedInOrder.contains(path) ? selectedInOrder : selectedInOrder + [path]
        return cardNames(for: paths)[path] ?? (defaultName(path), nil)
    }

    // MARK: Space

    /// The destinations as the space check sees them (only connected ones with a probed volume).
    var spaceTargets: [SpaceTarget] {
        settings.destRoots.compactMap { r in
            guard let s = destStatus[r], s.exists, let v = s.volume else { return nil }
            return SpaceTarget(root: r, volume: v, label: s.label ?? AppModel.diskName(r))
        }
    }

    /// What the ticked cards need, per destination (append: on the backup's own destinations that are in step 2).
    func demands(for path: String) -> [(demand: VolumeDemand, destination: Int?)] {
        if appendChosen(path), let m = insights[path]?.match {
            return SpaceCheck.forAppend(m, proxy: settings.proxy, tools: tools, targets: spaceTargets, folder: nil)
        }
        return SpaceCheck.forCard(insights[path], usedOnVolume: usedBytes(path), proxy: settings.proxy, tools: tools,
                                  targets: spaceTargets, folder: nil)
    }

    /// Red (blocks Start) and orange problems from memory: the ticked cards, the queued jobs and the rest of the running
    /// ones, against the free space the probes found. Unknown free space never blocks — the engine checks again.
    var spaceProblems: [SpaceProblem] {
        var demands: [VolumeDemand] = []
        for path in selectedInOrder { demands += self.demands(for: path).map(\.demand) }
        for j in jobs where !j.isFinished {
            let s = j.status
            for (d, dest) in j.spaceDemands {
                let written = dest.flatMap { i in s.copy.flatMap { i < $0.writtenBytes.count ? $0.writtenBytes[i] : nil } } ?? 0
                demands.append(VolumeDemand(volume: d.volume, label: d.label, need: SpaceCheck.remaining(d.need, phase: s.phase, written: written)))
            }
        }
        guard !demands.isEmpty else { return [] }
        var free: [String: Int64] = [:]
        for r in settings.destRoots {
            if let s = destStatus[r], s.exists, let v = s.volume { free[v] = s.free }
        }
        return SpacePlanner.check(demands, free: free)
    }

    // MARK: Start

    /// Start waits for the look at the ticked cards (≤ 10 s, the button disabled meanwhile), then decides with what it
    /// knows — a card not looked at counts with the used space of its volume.
    func startAfterInspections() {
        let deadline = Date().addingTimeInterval(10)
        func step() {
            let pending = selectedInOrder.contains { inspecting.contains($0) || (insights[$0] == nil && history != nil) }
            if pending && Date() < deadline {
                starting = true
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { step() }
                return
            }
            starting = false
            if canStart { startSelected() }
        }
        step()
    }

    /// The prompt "Cards already backed up" before full copies, then one job per card.
    private func startSelected() {
        var toStart = selectedInOrder
        let ask = BackedUpPrompt.cardsToAsk(toStart, insight: { self.insights[$0] }, appendChosen: { self.appendChosen($0) })
        let names = cardNames(for: toStart)
        if !ask.isEmpty {
            let cards = ask.compactMap { p in insights[p]?.match.map { (match: $0, plannedName: names[p]?.name ?? defaultName(p)) } }
            let prompt = BackedUpPrompt.build(cards, date: dateString)
            let alert = NSAlert()
            alert.messageText = prompt.title
            alert.informativeText = prompt.lines.map { "• " + $0 }.joined(separator: "\n") + "\n\n" + prompt.question
            alert.addButton(withTitle: BackedUpPrompt.copyAgainText)
            alert.addButton(withTitle: BackedUpPrompt.skipText)
            alert.addButton(withTitle: BackedUpPrompt.cancelText)
            switch alert.runModal() {
            case .alertFirstButtonReturn: break
            case .alertSecondButtonReturn: toStart.removeAll { ask.contains($0) }
            default: return
            }
        }
        for src in toStart {
            var config = JobConfig(source: src, destRoots: settings.destRoots)
            config.date = dateString
            config.verify = settings.verify
            config.analyzeClips = settings.analyzeClips
            config.proxy = settings.proxy
            config.tools = settings.tools
            var sourceName = names[src]?.name ?? defaultName(src)
            if appendChosen(src), let m = insights[src]?.match {
                // Into the backup's own card folders (step 2 is not used); the project of the earlier backup.
                config.appendInto = m.entry.destinations.map(\.cardPath)
                config.destRoots = m.entry.destinations.map(\.root)
                config.project = m.entry.project
                sourceName = m.entry.cardName
            } else {
                config.cardName = sourceName == defaultName(src) ? customName(src) : sourceName
            }
            let volume = volumes.first { $0.path == src }
            let item = JobItem(job: OffloadJob(config: config), sourceName: sourceName,
                               canEject: volume != nil && volume?.ejectProblem == nil, estimatedBytes: insights[src]?.totalBytes ?? usedBytes(src))
            item.spaceDemands = demands(for: src)
            selectedSources.remove(src)
            appendSelected.remove(src)
            enqueue(item)
        }
    }

    // MARK: Verify backup

    /// Tools › Verify Backup… (⇧⌘O) and the top bar: a card folder on a destination, or a whole day — every card is one
    /// item of the copy queue (it loads the same disks as a copy).
    func verifyBackup() {
        guard !isPreview, !terminating else { return }
        let panel = NSOpenPanel()
        panel.title = "Choose a card folder on a destination (or a whole day)"
        panel.prompt = "Verify"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        let box = NSButton(checkboxWithTitle: "Save a verification report", target: nil, action: nil)
        box.state = saveVerifyReport ? .on : .off
        box.sizeToFit()
        let accessory = NSView(frame: NSRect(x: 0, y: 0, width: box.frame.width + 24, height: box.frame.height + 16))
        box.frame.origin = NSPoint(x: 12, y: 8)
        accessory.addSubview(box)
        panel.accessoryView = accessory
        panel.isAccessoryViewDisclosed = true
        guard panel.runModal() == .OK, let folder = panel.url?.path else { return }
        saveVerifyReport = box.state == .on
        let report = saveVerifyReport
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let cards = BackupVerifier.cardFolders(in: folder)   // the folder may be on a slow disk
            DispatchQueue.main.async {
                guard let self else { return }
                if cards.isEmpty {
                    self.message = Msg.noHashRecord(folder + "/" + Layout.reportDirName)
                    return
                }
                for card in cards { self.enqueueVerify(VerifyItem(job: VerifyJob(cardPath: card, writeReport: report))) }
            }
        }
    }

    func enqueueVerify(_ item: VerifyItem) {
        verifies.append(item)
        item.job.log = { NSLog("%@", "verify \(item.job.cardName): \($0)") }
        copyQueue.async { [weak self] in
            item.job.run()
            DispatchQueue.main.async {
                item.refresh()
                if let self, !self.terminating {
                    if item.tone == .error { NSSound(named: "Basso")?.play() }
                    NSApp.requestUserAttention(.informationalRequest)
                }
                self?.checkTerminateReady()
            }
        }
    }

    func cancelVerify(_ item: VerifyItem) {
        item.job.cancel()
        item.refresh()
    }

    func openVerifyReport(_ item: VerifyItem) {
        guard let path = item.status.reportPath else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: path))
    }

    func revealVerify(_ item: VerifyItem) {
        let target = item.status.reportPath ?? item.job.cardPath
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let exists = FileManager.default.fileExists(atPath: target)
            DispatchQueue.main.async {
                if exists {
                    NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: target)])
                } else {
                    self?.message = "Folder not found: \(target)"
                }
            }
        }
    }
}

/// One Verify backup item of the queue; mirrors the job's status for SwiftUI (main thread).
final class VerifyItem: ObservableObject, Identifiable {
    let id = UUID()
    let job: VerifyJob
    @Published private(set) var status = VerifyJob.Status()
    @Published private(set) var rate: Double = 0
    private var lastBytes: Int64 = 0
    private var lastTime = Date()
    private(set) var finishedAt: Date?

    init(job: VerifyJob) { self.job = job }

    var isFinished: Bool { [.done, .cancelled, .failed].contains(status.state) }
    var isRunning: Bool { status.state == .running }
    var tone: CardTone { VerifyJobView.tone(status) }

    func refresh() {
        let s = job.currentStatus()
        let now = Date()
        let bytes = s.progress?.bytes ?? 0
        let dt = now.timeIntervalSince(lastTime)
        if dt >= 0.3 {
            let r = Double(bytes - lastBytes) / dt
            rate = rate == 0 ? r : rate * 0.6 + r * 0.4
            lastBytes = bytes
            lastTime = now
        }
        status = s
        if finishedAt == nil && isFinished { finishedAt = now }
    }
}
