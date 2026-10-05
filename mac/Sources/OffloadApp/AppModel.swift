import AppKit
import Combine
import OffloadKit
import SwiftUI

/// Free space, filesystem and existence of a destination — probed off the main thread.
struct DestinationStatus: Equatable {
    var free: Int64 = 0
    var format = ""
    var exists = false
    var checking = true
}

/// One card in the queue; mirrors the job's status for SwiftUI. Main thread only, except `recordCopiedCard`.
final class JobItem: ObservableObject, Identifiable {
    let id = UUID()
    let job: OffloadJob
    let sourceName: String
    /// The source is a whole card volume that may be ejected (from the volume scan when the job was created).
    let canEject: Bool
    @Published var ejected = false
    @Published var ejecting = false
    private let progress = JobProgress(now: Date())
    private let lock = NSLock()
    private var copied: String?

    init(job: OffloadJob, sourceName: String, canEject: Bool) {
        self.job = job
        self.sourceName = sourceName
        self.canEject = canEject
    }

    var plan: OffloadPlan? { job.plan }
    var report: OffloadReport? { job.report }
    var status: JobStatus { progress.status }
    var isRunning: Bool { progress.isRunning }
    var isFinished: Bool { progress.status.phase.isFinished }
    var isCancelled: Bool { progress.status.phase == .cancelled }
    var phaseProgress: Double { progress.phaseProgress }
    var detailText: String { progress.detailText }
    var statusText: String { progress.statusText }
    var tone: CardTone { progress.tone }
    var isError: Bool { progress.isError }

    /// The card recorded right before the copy (`CardIdentity`); nil before or when unknown.
    var copiedIdentity: String? { lock.lock(); defer { lock.unlock() }; return copied }

    /// On the copy queue, right before `runCopy` — never on the main thread (it reads the card's volume).
    func recordCopiedCard() {
        guard canEject else { return }
        let id = CardIdentity.of(job.config.source)
        lock.lock(); copied = id; lock.unlock()
    }

    func refresh() {
        objectWillChange.send()
        progress.update(job.status(), now: Date())
    }

    var safeText: String { Texts.safeToEject(plan?.cardPaths.count ?? 0) }

    /// Not for a cancelled job: it shows one orange line instead (a cut-short copy is not "N souborů selhalo").
    var failedText: String? {
        guard !isCancelled, let r = report, !r.success else { return nil }
        return Texts.failed(r.failedFiles.count)
    }

    var cancelledText: String? { isCancelled && !status.safeToEject ? Texts.cancelled : nil }

    /// No rate summary for a cancelled job — the rate of a cut-short copy would mislead.
    var summaryText: String? {
        guard isFinished, !isCancelled, let r = report else { return nil }
        return JobProgress.summaryText(r)
    }
}

@MainActor
final class AppModel: ObservableObject {
    @Published var settings: AppSettings { didSet { save() } }
    @Published var volumes: [VolumeItem] = []
    @Published var selectedSources: Set<String> = []
    @Published var manualSources: [String] = []
    @Published var jobs: [JobItem] = []
    @Published var useCustomDate = false
    @Published var customDate = Date()
    @Published var cardNames: [String: String] = [:]
    @Published var tools = ExternalTools()
    @Published var message: String?
    @Published private(set) var destStatus: [String: DestinationStatus] = [:]
    @Published private(set) var lutExists: Bool?
    @Published private(set) var terminating = false
    private var lutChecked: String?
    private var probedRoots: [String] = []
    private var probedLut: String?
    /// One destination check in flight, at most one more requested — a hung NAS never stacks a check every 5 s.
    private var probing = false
    private var probeAgain = false

    private let copyQueue = DispatchQueue(label: "app.copy", qos: .userInitiated)
    private let postQueue = DispatchQueue(label: "app.post", qos: .utility)
    /// Volume scans (cards). Separate from destination checks, so a hung network destination never blocks them.
    private let probeQueue = DispatchQueue(label: "app.probe", qos: .userInitiated)
    /// Destination and LUT checks; a hung network disk stalls only this queue, never the window or the card scan.
    private let destQueue = DispatchQueue(label: "app.dest", qos: .utility)
    private var timers: [Timer] = []
    private var observers: [NSObjectProtocol] = []

    init() {
        settings = SettingsStore.decode(UserDefaults.standard.data(forKey: SettingsStore.defaultsKey))
        tools = settings.tools.resolved()
        refreshVolumes()
        refreshDestinations()
        let nc = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didMountNotification, NSWorkspace.didUnmountNotification, NSWorkspace.didRenameVolumeNotification] {
            observers.append(nc.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                Task { @MainActor in
                    self?.refreshVolumes()
                    self?.refreshDestinations()
                }
            })
        }
        // Common modes: the ticks keep coming while a menu or an alert is open.
        let tick = Timer(timeInterval: 0.33, repeats: true) { [weak self] _ in Task { @MainActor in self?.tick() } }
        let health = Timer(timeInterval: 5, repeats: true) { [weak self] _ in Task { @MainActor in self?.refreshDestinations() } }
        for t in [tick, health] {
            RunLoop.main.add(t, forMode: .common)
            timers.append(t)
        }
    }

    private func save() {
        if let data = SettingsStore.encode(settings) { UserDefaults.standard.set(data, forKey: SettingsStore.defaultsKey) }
        let t = settings.tools.resolved()
        if t != tools { tools = t }
        if settings.destRoots != probedRoots || ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips) != probedLut {
            refreshDestinations()
        }
    }

    private func tick() {
        for j in jobs where !j.isFinished { j.refresh() }
    }

    var dateString: String {
        guard useCustomDate else { return Layout.today() }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: customDate)
    }

    // MARK: Volumes and destinations (always off the main thread)

    func refreshVolumes() {
        let roots = settings.destRoots
        probeQueue.async { [weak self] in
            let found = Volumes.scan(destRoots: roots)
            DispatchQueue.main.async { self?.applyVolumes(found) }
        }
    }

    private func applyVolumes(_ found: [VolumeItem]) {
        selectedSources = Volumes.selectionAfterRefresh(previousPaths: volumes.map(\.path), found: found,
                                                        selected: selectedSources, manual: manualSources)
        volumes = found
        for j in jobs { j.objectWillChange.send() }   // "Vysunout kartu" follows the scan
    }

    /// Free space / filesystem / existence of every destination and whether the LUT exists — every 5 s,
    /// after a change and from the refresh button.
    func refreshDestinations() {
        if probing {
            probeAgain = true   // runs once the check in flight returns (it may hang on a network disk)
            return
        }
        probing = true
        let roots = settings.destRoots
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        probedRoots = roots
        probedLut = lut
        destQueue.async { [weak self] in
            var found: [String: DestinationStatus] = [:]
            for r in roots { found[r] = AppModel.probe(r) }
            let lutOK = lut.map { FileManager.default.fileExists(atPath: $0) }
            DispatchQueue.main.async {
                guard let self else { return }
                self.destStatus = found
                self.lutExists = lutOK
                self.lutChecked = lut
                self.probing = false
                if self.probeAgain {
                    self.probeAgain = false
                    self.refreshDestinations()
                }
            }
        }
    }

    nonisolated static func probe(_ path: String) -> DestinationStatus {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir), isDir.boolValue else {
            return DestinationStatus(checking: false)
        }
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return DestinationStatus(checking: false) }
        let format = withUnsafeBytes(of: fs.f_fstypename) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
        return DestinationStatus(free: Int64(fs.f_bavail) * Int64(fs.f_bsize), format: format, exists: true, checking: false)
    }

    func destinationInfo(_ path: String) -> DestinationStatus { destStatus[path] ?? DestinationStatus() }

    /// A missing LUT the next start needs — from the last background check; nil until checked.
    var lutProblem: String? {
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        return ProxyPreflight.blocking(settings.proxy, analyzeClips: settings.analyzeClips,
                                       lutExists: lut == lutChecked ? lutExists : nil)
    }

    func addManualSource() {
        guard let path = pickFolder(title: "Vyber kartu nebo složku se záznamem") else { return }
        if !manualSources.contains(path) { manualSources.append(path) }
        selectedSources.insert(path)
    }

    func addDestination() {
        guard let path = pickFolder(title: "Vyber cílovou složku (root)") else { return }
        if !settings.destRoots.contains(path) { settings.destRoots.append(path) }
        refreshVolumes()
    }

    func removeDestination(_ path: String) {
        settings.destRoots.removeAll { $0 == path }
        refreshVolumes()
    }

    func pickLUT() {
        let panel = NSOpenPanel()
        panel.title = "Vyber LUT (.cube)"
        panel.allowedContentTypes = [.init(filenameExtension: "cube")!]
        if panel.runModal() == .OK, let url = panel.url { settings.proxy.lutPath = url.path }
    }

    func pickTool(_ keyPath: WritableKeyPath<ExternalTools, String?>) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        if panel.runModal() == .OK, let url = panel.url { settings.tools[keyPath: keyPath] = url.path }
    }

    private func pickFolder(title: String) -> String? {
        let panel = NSOpenPanel()
        panel.title = title
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        return panel.runModal() == .OK ? panel.url?.path : nil
    }

    /// The card folder name: the typed name (blank = default), else the default name.
    func sourceName(_ path: String) -> String {
        customName(path) ?? defaultName(path)
    }

    /// The default card folder name without touching the disk (main thread): the scanned volume's name, else the
    /// folder's name — what `Layout.defaultCardName(for:)` gives, which the engine itself still calls off the main thread.
    func defaultName(_ path: String) -> String {
        Layout.sanitize(volumes.first { $0.path == path }?.name ?? (path as NSString).lastPathComponent)
    }

    private func customName(_ path: String) -> String? {
        let t = cardNames[path]?.trimmingCharacters(in: .whitespaces) ?? ""
        return t.isEmpty ? nil : t
    }

    // MARK: Jobs

    var canStart: Bool {
        !terminating && !selectedSources.isEmpty && !settings.destRoots.isEmpty
            && settings.destRoots.allSatisfy { destStatus[$0]?.exists == true } && lutProblem == nil
    }

    /// A copy writes or verifies the destinations — no speed measurement now.
    var copyRunning: Bool { QueueRules.measureBlocked(jobs.map(\.status.phase)) }

    func start() {
        guard canStart else { return }
        let sources = (volumes.map(\.path) + manualSources).filter { selectedSources.contains($0) }
        for src in sources {
            var config = JobConfig(source: src, destRoots: settings.destRoots)
            config.cardName = customName(src)
            config.date = dateString
            config.verify = settings.verify
            config.analyzeClips = settings.analyzeClips
            config.proxy = settings.proxy
            config.tools = settings.tools
            let volume = volumes.first { $0.path == src }
            let item = JobItem(job: OffloadJob(config: config), sourceName: sourceName(src),
                               canEject: volume != nil && volume?.ejectProblem == nil)
            jobs.append(item)
            selectedSources.remove(src)
            // Copies run one after another (they share the destination disks); post-processing runs
            // in parallel on its own queue, so the next card starts copying while proxies render.
            copyQueue.async { [weak self] in
                // Cancelled while queued: it never runs, but the queue still reports it finished.
                if item.job.isCancelled {
                    DispatchQueue.main.async { self?.jobFinished(item) }
                    return
                }
                item.recordCopiedCard()
                let needsPost = item.job.runCopy()
                DispatchQueue.main.async {
                    item.refresh()
                    self?.copyFinished(item)
                }
                if needsPost {
                    self?.postQueue.async {
                        item.job.runPost()
                        DispatchQueue.main.async { self?.jobFinished(item) }
                    }
                } else {
                    DispatchQueue.main.async { self?.jobFinished(item) }
                }
            }
        }
    }

    private func copyFinished(_ item: JobItem) {
        guard !terminating, item.status.safeToEject else { return }
        NSSound(named: "Glass")?.play()
    }

    private func jobFinished(_ item: JobItem) {
        item.refresh()
        if !terminating {
            if item.isError { NSSound(named: "Basso")?.play() }   // a job the user cancelled is not an error
            NSApp.requestUserAttention(.informationalRequest)
        }
        checkTerminateReady()
    }

    func cancel(_ item: JobItem) {
        item.job.cancel()
        item.refresh()
    }

    func clearFinished() { jobs.removeAll { $0.isFinished && !$0.ejecting } }

    /// "Vysunout kartu" only while the copied card is in its slot per the last scan.
    func canShowEject(_ item: JobItem) -> Bool {
        item.status.safeToEject && !item.ejected && item.canEject
            && QueueRules.cardInDrive(item.job.config.source, copied: item.copiedIdentity, scanned: volumes)
    }

    /// Ejects only the card that was copied: the card at the source now is identified off the main thread and
    /// compared with the one recorded when the copy started; never while another job still needs the card.
    func eject(_ item: JobItem) {
        guard !item.ejecting, !item.ejected else { return }   // a double click must not start a second eject
        let source = item.job.config.source
        let copied = item.copiedIdentity
        let inUse = QueueRules.cardInUse(source, others: jobs.filter { $0 !== item }.map { ($0.job.config.source, $0.status.phase) })
        item.ejecting = true
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let result: (ok: Bool, error: String?)
            if let problem = QueueRules.ejectProblem(copied: copied, current: CardIdentity.of(source), cardInUse: inUse) {
                result = (false, problem)
            } else {
                result = CardEject.eject(source)
            }
            DispatchQueue.main.async {
                item.ejecting = false
                if result.ok {
                    item.ejected = true
                    self?.refreshVolumes()
                } else {
                    self?.message = "Kartu se nepodařilo vysunout: \(result.error ?? "")"
                }
                self?.checkTerminateReady()
            }
        }
    }

    func reveal(_ item: JobItem) {
        guard let p = item.plan?.cardPaths.first else { return }
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)])
    }

    /// The report from the first destination that has it, looked up off the main thread.
    func openReport(_ item: JobItem) {
        guard let plan = item.plan else { return }
        let cards = plan.cardPaths
        let name = plan.cardName
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let path = QueueRules.firstReport(cardPaths: cards, cardName: name)
            DispatchQueue.main.async {
                if let path {
                    NSWorkspace.shared.open(URL(fileURLWithPath: path))
                } else {
                    self?.message = "Report nenalezen na žádném cíli:\n"
                        + cards.map { QueueRules.reportPath(cardPath: $0, cardName: name) }.joined(separator: "\n")
                }
            }
        }
    }

    // MARK: Quit while copying

    /// A job that has not finished, or an eject in flight.
    var hasBusyJobs: Bool { jobs.contains { !$0.isFinished || $0.ejecting } }

    /// Asked by the app delegate. With unfinished jobs: confirm, cancel them all and quit once they have cleaned up.
    func shouldTerminate() -> NSApplication.TerminateReply {
        for j in jobs { j.refresh() }
        guard hasBusyJobs else { return .terminateNow }
        if terminating {
            // An NSAlert, not `message`: the main window (and its alert) may already be closed.
            let notice = NSAlert()
            notice.messageText = "Ukončuji — čekám, až se zrušené úlohy uklidí."
            notice.runModal()
            return .terminateCancel
        }
        if jobs.contains(where: { !$0.isFinished }) {
            let alert = NSAlert()
            alert.messageText = "Probíhá offload. Opravdu ukončit?"
            alert.informativeText = "Rozpracované soubory se smažou, hotové zůstanou."
            alert.addButton(withTitle: "Ukončit")
            alert.addButton(withTitle: "Pokračovat")
            guard alert.runModal() == .alertFirstButtonReturn else { return .terminateCancel }
        }
        terminating = true
        for j in jobs {
            j.job.cancel()
            j.refresh()
        }
        if !hasBusyJobs { return .terminateNow }
        return .terminateCancel   // checkTerminateReady() quits once everything has cleaned up
    }

    private func checkTerminateReady() {
        guard terminating, !hasBusyJobs else { return }
        NSApp.terminate(nil)
    }
}
