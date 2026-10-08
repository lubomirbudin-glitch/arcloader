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

    @Published private(set) var chainLines: [String] = []
    private var links: [RoleLink]?
    private var connectors: [UsbConnector]?
    private var portNames: [String: String] = [:]
    private var chainRequested = false

    func refresh() {
        objectWillChange.send()
        progress.update(job.status(), now: Date())
        updateChain()
    }

    /// Where the card and each destination are connected — resolved once, off the main thread, when copying starts.
    func resolveChainIfNeeded(portNames names: [String: String]) {
        guard !chainRequested, let plan = job.plan, status.phase == .copying else { return }
        chainRequested = true
        let config = job.config
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var l = [RoleLink(role: "Karta", link: DriveLinks.resolve(plan.source))]
            l += plan.destRoots.enumerated().map { RoleLink(role: "Cíl \($0.offset + 1)", link: DriveLinks.resolve($0.element)) }
            let c = UsbScanner.scan().connectors
            DispatchQueue.main.async {
                guard let self else { return }
                self.links = l
                self.connectors = c
                self.portNames = names
                self.progress.attachBrakeMeter(BrakeMeter(chunkBytes: Int64(config.chunkMB) << 20, bufferCount: config.buffers,
                                                          linkMBps: l.map(ChainView.linkCeiling)))
            }
        }
    }

    /// The chain with live throughput and the brake (spec 8a.3) while copying or verifying; unchanged lines are kept.
    private func updateChain() {
        var lines: [String] = []
        if let links, status.phase == .copying || status.phase == .verifying {
            let rates = [status.phase == .copying ? progress.readRate / 1e6 : 0] + progress.destRates.map { $0 / 1e6 }
            let chain = ChainView.build(links, mbps: rates, connectors: connectors, portNames: portNames)
            lines = ChainView.lines(chain, brake: status.phase == .copying ? progress.brake : nil)
        }
        if lines != chainLines { chainLines = lines }
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
    @Published private(set) var folderExists: Bool?
    /// The LUT and proxy folder are being checked right before a start (at most 5 s each).
    @Published private(set) var starting = false
    /// The built-in LUTs (the app bundle's Resources/luts; already loaded by SettingsStore.decode at launch).
    let luts = LutCatalog.shared
    private var folderChecked: String?
    private var probedFolder: String?
    private var lutChecked: String?
    private var probedRoots: [String] = []
    private var probedLut: String?
    /// One destination check in flight, at most one more requested — a hung NAS never stacks a check every 5 s.
    private var probing = false
    private var probeAgain = false

    private let copyQueue = DispatchQueue(label: "app.copy", qos: .userInitiated)
    private let postQueue = DispatchQueue(label: "app.post", qos: .userInitiated)  // proxies: the user waits for dailies
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
        if settings.destRoots != probedRoots
            || ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips) != probedLut
            || ProxyPreflight.extraFolderInUse(settings.proxy) != probedFolder {
            refreshDestinations()
        }
    }

    private func tick() {
        for j in jobs where !j.isFinished {
            j.refresh()
            j.resolveChainIfNeeded(portNames: settings.portNames)
        }
        // A copy started or ended: the Hardware window's measure button and hint follow.
        let running = copyRunning
        if running != shownCopyRunning {
            shownCopyRunning = running
            hardware.objectWillChange.send()
        }
    }

    private var _hardware: HardwareModel?
    /// Created on first use (the Hardware section or window); `waitForHardware` never creates it.
    var hardware: HardwareModel {
        if let h = _hardware { return h }
        let h = HardwareModel(app: self)
        _hardware = h
        return h
    }

    /// Before the process exits: a running measurement stops and deletes its temporary file.
    func waitForHardware() { _hardware?.stopAndWait(timeout: 10) }
    private var shownCopyRunning = false

    /// Who takes part in the HW test: the ticked cards and the destinations; with neither, every volume (byDrive).
    /// No disk is touched: destinations come from the probed status (one still being checked counts).
    func hardwareRoles() -> (cards: [String], destinations: [String], byDrive: Bool) {
        let cards = (volumes.map(\.path) + manualSources).filter { selectedSources.contains($0) }
        let dests = settings.destRoots.filter { destStatus[$0].map { $0.exists || $0.checking } ?? true }
        return cards.count + dests.count > 0 ? (cards, dests, false) : ([], [], true)
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

    /// Free space / filesystem / existence of every destination, whether the custom LUT and the proxy folder exist —
    /// every 5 s, after a change and from the refresh button. Never on the main thread.
    func refreshDestinations() {
        if probing {
            probeAgain = true   // runs once the check in flight returns (it may hang on a network disk)
            return
        }
        probing = true
        let roots = settings.destRoots
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        probedRoots = roots
        probedLut = lut
        probedFolder = folder
        destQueue.async { [weak self] in
            var found: [String: DestinationStatus] = [:]
            for r in roots { found[r] = AppModel.probe(r) }
            let lutOK = lut.map { FileManager.default.fileExists(atPath: $0) }
            let folderOK = folder.map { ProxyPreflight.isDirectory($0) }
            DispatchQueue.main.async {
                guard let self else { return }
                self.destStatus = found
                self.lutExists = lutOK
                self.lutChecked = lut
                self.folderExists = folderOK
                self.folderChecked = folder
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

    /// A missing proxy folder (disconnected disk) blocks the start — from the last background check; nil until checked.
    var proxyFolderProblem: String? {
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        return ProxyPreflight.extraFolderBlocking(settings.proxy, folderExists: folder == folderChecked ? folderExists : nil)
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

    /// The .cube dialog; a picked file switches to custom mode. False when cancelled (nothing changes).
    @discardableResult
    func pickLUT() -> Bool {
        let panel = NSOpenPanel()
        panel.title = "Vyber LUT (.cube)"
        panel.allowedContentTypes = [.init(filenameExtension: "cube")!]
        guard panel.runModal() == .OK, let url = panel.url else { return false }
        settings.proxy.lutPath = url.path
        settings.proxy.lutMode = .custom
        return true
    }

    /// The LUT picker's value: "auto", a built-in id, "none" or "custom". An unknown built-in id shows as auto (like Windows).
    var lutChoice: String {
        switch LutResolver.effectiveMode(settings.proxy) {
        case .auto: return "auto"
        case .off: return "none"
        case .custom: return "custom"
        case .builtin:
            let id = settings.proxy.lutBuiltin ?? ""
            return luts.find(id) != nil ? id : "auto"
        }
    }

    func setLutChoice(_ choice: String) {
        switch choice {
        case "auto":
            settings.proxy.lutMode = .auto
        case "none":
            settings.proxy.lutMode = .off
        case "custom":
            if (settings.proxy.lutPath ?? "").isEmpty {
                // Not inside the Picker's own update: the panel opens right after; Cancel keeps the previous choice.
                DispatchQueue.main.async { [weak self] in
                    guard let self else { return }
                    if !self.pickLUT() { self.objectWillChange.send() }
                }
                return
            }
            settings.proxy.lutMode = .custom
        default:
            settings.proxy.lutBuiltin = choice
            settings.proxy.lutMode = .builtin
        }
    }

    func pickProxyFolder() {
        guard let path = pickFolder(title: "Vyber složku pro proxy") else { return }
        settings.proxy.extraFolder = path
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
        !terminating && !starting && !selectedSources.isEmpty && !settings.destRoots.isEmpty
            && settings.destRoots.allSatisfy { destStatus[$0]?.exists == true }
            && lutProblem == nil && proxyFolderProblem == nil
    }

    /// A copy writes or verifies the destinations — no speed measurement now.
    var copyRunning: Bool { QueueRules.measureBlocked(jobs.map(\.status.phase)) }

    /// The custom LUT and the proxy folder are checked once more right before the start (they may sit on a disk that
    /// just went away) — off the main thread, at most 5 s each; meanwhile the start button is disabled.
    func start() {
        guard canStart else { return }
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        guard lut != nil || folder != nil else { return startJobs() }
        starting = true
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let lutOK = lut.map { path in AppModel.answer(within: 5) { FileManager.default.fileExists(atPath: path) } }
            let folderOK = folder.map { path in AppModel.answer(within: 5) { ProxyPreflight.isDirectory(path) } }
            DispatchQueue.main.async {
                guard let self else { return }
                self.starting = false
                // Settings may have changed meanwhile: only an answer for what is still in use counts.
                if let lut, lut == ProxyPreflight.lutInUse(self.settings.proxy, analyzeClips: self.settings.analyzeClips) {
                    self.lutExists = lutOK
                    self.lutChecked = lut
                }
                if let folder, folder == ProxyPreflight.extraFolderInUse(self.settings.proxy) {
                    self.folderExists = folderOK
                    self.folderChecked = folder
                }
                if self.canStart { self.startJobs() }   // otherwise the red text says why
            }
        }
    }

    /// `check` on its own thread; false when it has not answered within `seconds` (a hung network disk).
    nonisolated static func answer(within seconds: Double, _ check: @escaping () -> Bool) -> Bool {
        final class Box: @unchecked Sendable { var value = false }
        let box = Box()
        let done = DispatchSemaphore(value: 0)
        DispatchQueue.global(qos: .userInitiated).async {
            box.value = check()
            done.signal()
        }
        return done.wait(timeout: .now() + seconds) == .success && box.value
    }

    private func startJobs() {
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
