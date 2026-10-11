import AppKit
import Combine
import OffloadKit
import SwiftUI

/// Free space, size, filesystem, connection and existence of a destination — probed off the main thread.
struct DestinationStatus: Equatable {
    var free: Int64 = 0
    var total: Int64 = 0
    var format = ""
    var exists = false
    var checking = true
    /// "Thunderbolt", "USB 3 (5 Gb/s)", "NVMe" — resolved once per destination (DriveLinks), nil until known.
    var connection: String?
    /// Which volume it is on (two destinations on one volume add up in the space check) and how messages name it.
    var volume: String?
    var label: String?
}

/// One clip thumbnail of a finished card (decoded off the main thread).
struct ClipThumb: Identifiable {
    let id: Int
    let clip: String
    let image: NSImage?
}

/// One card in the queue; mirrors the job's status for SwiftUI. Main thread only, except `recordCopiedCard`.
final class JobItem: ObservableObject, Identifiable {
    let id = UUID()
    let job: OffloadJob
    let sourceName: String
    /// The source is a whole card volume that may be ejected (from the volume scan when the job was created).
    let canEject: Bool
    /// Queued with "One card at a time": starts only after every earlier job has completely finished.
    var oneAtATime = false
    /// The used space of the card volume when the job was queued (the size before the plan exists), nil for a folder.
    let estimatedBytes: Int64?
    @Published var ejected = false
    @Published var ejecting = false
    /// Task 70: the user has looked at the content defects ("Checked — the card can be formatted").
    @Published var contentConfirmed = false
    @Published private(set) var thumbs: [ClipThumb] = []
    @Published private(set) var moreThumbs = 0
    let progress = JobProgress(now: Date())
    private let lock = NSLock()
    private var copied: String?
    private(set) var startedAt: Date?
    private(set) var finishedAt: Date?
    private var thumbsLoaded = false
#if DEBUG
    /// Snapshot rendering only: the card as it should look, instead of the job's status.
    var preview: JobCardState?
#endif

    /// An append into an earlier backup ("A001 · append").
    var isAppend: Bool { job.config.appendInto != nil }
    /// What this job needs on the destination volumes (from the card's inspection when it was queued); the space check
    /// counts what is still left of it (SpaceCheck.remaining).
    var spaceDemands: [(demand: VolumeDemand, destination: Int?)] = []

    init(job: OffloadJob, sourceName: String, canEject: Bool, estimatedBytes: Int64? = nil) {
        self.job = job
        self.sourceName = sourceName
        self.canEject = canEject
        self.estimatedBytes = estimatedBytes
    }

    var plan: OffloadPlan? { job.plan }
    var report: OffloadReport? { job.report }
    var status: JobStatus { progress.status }
    var isRunning: Bool { progress.isRunning }
    var isFinished: Bool { progress.status.phase.isFinished }
    var isCancelled: Bool { progress.status.phase == .cancelled }
    var phaseProgress: Double { progress.phaseProgress }
    var isError: Bool { progress.isError }

    /// The phase the queue counts by (a snapshot card's own kind in DEBUG previews).
    var queuePhase: JobStatus.Phase {
#if DEBUG
        if let p = preview { return p.phase }
#endif
        return status.phase
    }

    /// Finished as the queue sees it (a snapshot card's own kind in DEBUG previews).
    var queueFinished: Bool { queuePhase.isFinished }

    /// The card recorded right before the copy (`CardIdentity`); nil before or when unknown.
    var copiedIdentity: String? { lock.lock(); defer { lock.unlock() }; return copied }

    /// On the copy queue, right before `runCopy` — never on the main thread (it reads the card's volume).
    func recordCopiedCard() {
        guard canEject else { return }
        let id = CardIdentity.of(job.config.source)
        lock.lock(); copied = id; lock.unlock()
    }

    /// Where the card and each destination sit, with live throughput (copy and verify only) — the speed table's tooltips
    /// and the "slowest link" sentence.
    private(set) var chain: [ChainElement]?
    private var links: [RoleLink]?
    private var connectors: [UsbConnector]?
    private var portNames: [String: String] = [:]
    private var chainRequested = false

    func refresh() {
        objectWillChange.send()
        progress.update(job.status(), now: Date())
        let phase = status.phase
        if startedAt == nil && phase != .waiting && phase != .cancelled { startedAt = Date() }
        if finishedAt == nil && phase.isFinished { finishedAt = Date() }
        updateChain()
        loadThumbsIfNeeded()
    }

    /// Where the card and each destination are connected — resolved once, off the main thread, when copying starts.
    func resolveChainIfNeeded(portNames names: [String: String]) {
        guard !chainRequested, let plan = job.plan, status.phase == .copying else { return }
        chainRequested = true
        let config = job.config
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var l = [RoleLink(role: RoleLink.card, link: DriveLinks.resolve(plan.source))]
            l += plan.destRoots.enumerated().map { RoleLink(role: RoleLink.destination($0.offset + 1), link: DriveLinks.resolve($0.element)) }
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

    private func updateChain() {
        let stage2Verify = status.verifyInProxy && !status.verifyDoneByDest.isEmpty
            && [.analyzing, .proxies, .finishingVerification].contains(status.phase)
        guard let links, status.phase == .copying || status.phase == .verifying || stage2Verify else {
            chain = nil
            return
        }
        let dest = stage2Verify ? progress.verifyRates : progress.destRates
        let rates = [status.phase == .copying ? progress.readRate / 1e6 : 0] + dest.map { $0 / 1e6 }
        chain = ChainView.build(links, mbps: rates, connectors: connectors, portNames: portNames)
    }

    /// The first five clips with a thumbnail (the middle frame of three) — decoded once, off the main thread.
    private func loadThumbsIfNeeded() {
        guard !thumbsLoaded, isFinished, let r = report else { return }
        thumbsLoaded = true
        let clips = r.clips.filter { !$0.clip.isCameraProxy }
        let picked = Array(clips.filter { !$0.thumbnails.isEmpty }.prefix(5))
        guard !picked.isEmpty else { return }
        let more = clips.count - picked.count
        let input = picked.map { ($0.clip.name, $0.thumbnails[$0.thumbnails.count == 3 ? 1 : 0].jpeg) }
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let thumbs = input.enumerated().map { ClipThumb(id: $0.offset, clip: $0.element.0, image: NSImage(data: $0.element.1)) }
            DispatchQueue.main.async {
                self?.thumbs = thumbs
                self?.moreThumbs = more
            }
        }
    }

    /// The names the destinations are shown with ("RAID_A"; the volume root as it is).
    var destNames: [String] { job.config.destRoots.map(AppModel.diskName) }
}

/// The sections of the settings page (also what a click on a "3 · After copy" row opens).
enum SettingsSection: String, CaseIterable, Identifiable {
    case folders, verify, analysis, proxy, report, decoders, hardware
    var id: String { rawValue }

    var title: String {
        switch self {
        case .folders: return "Folders"
        case .verify: return "Verify"
        case .analysis: return "Analysis"
        case .proxy: return "Proxy"
        case .report: return "Report"
        case .decoders: return "Decoders"
        case .hardware: return "Hardware"
        }
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
    /// The settings page shows instead of the two columns (nil = main view).
    @Published var settingsSection: SettingsSection?
    @Published private(set) var destStatus: [String: DestinationStatus] = [:]
    /// How each card volume is connected ("USB 3 (5 Gb/s)", "SD reader"), resolved once per mount off the main thread.
    @Published private(set) var volumeConnections: [String: String] = [:]
    @Published private(set) var lutExists: Bool?
    @Published private(set) var terminating = false
    @Published private(set) var folderExists: Bool?
    /// The LUT and proxy folder are being checked right before a start (at most 5 s each).
    @Published var starting = false
    /// The card history (history.json), loaded off the main thread; nil until it is.
    @Published var history: [HistoryEntry]?
    /// One look at every card / folder in step 1 (history match, size, clips, the name from the clips).
    @Published var insights: [String: CardInsight] = [:]
    /// Cards ticked for "Append" (only the new clips into the earlier backup).
    @Published var appendSelected: Set<String> = []
    /// "Save a verification report" for Verify backup (on by default).
    @Published var saveVerifyReport = true
    /// Verify backup items of the queue.
    @Published var verifies: [VerifyItem] = []
    /// The right column's tab: the queue or the card history.
    @Published var queueTab: QueueTab = .queue
    /// History ids ticked for the summary report (in "Today" and "Card history" alike).
    @Published var summarySelection: Set<String> = []
    /// A summary report is being made (off the main thread).
    @Published var makingSummary = false
    /// "ALEXA 35 · LogC4" per card in step 1, from one clip (CameraHints), once known.
    @Published var cameraLabels: [String: String] = [:]
    /// What this session's jobs really took: the time estimate in the start bar (memory only).
    let rates = RateMemory()
    /// Day reports being written (after a job or on request) — quitting waits for them.
    @Published var dayReportsRunning = 0
    /// Day reports, one at a time, off the main thread (the PDF prints on the main run loop meanwhile).
    let dayQueue = DispatchQueue(label: "app.day", qos: .userInitiated)
    var inspecting: Set<String> = []
    var inspectAgain: Set<String> = []
    let inspectQueue = DispatchQueue(label: "app.inspect", qos: .userInitiated, attributes: .concurrent)
    /// The built-in LUTs (the app bundle's Resources/luts; already loaded by SettingsStore.decode at launch).
    let luts = LutCatalog.shared
    /// Snapshot rendering: no disks, no timers, nothing saved.
    let isPreview: Bool
    private var folderChecked: String?
    private var probedFolder: String?
    private var lutChecked: String?
    private var probedRoots: [String] = []
    private var probedLut: String?
    /// One destination check in flight, at most one more requested — a hung NAS never stacks a check every 5 s.
    private var probing = false
    private var probeAgain = false
    /// Destination connections already resolved (they do not change while the disk stays mounted).
    private var destConnections: [String: String] = [:]

    let copyQueue = DispatchQueue(label: "app.copy", qos: .userInitiated)
    private let postQueue = DispatchQueue(label: "app.post", qos: .userInitiated)  // proxies: the user waits for dailies
    /// Volume scans (cards). Separate from destination checks, so a hung network destination never blocks them.
    private let probeQueue = DispatchQueue(label: "app.probe", qos: .userInitiated)
    /// Destination and LUT checks; a hung network disk stalls only this queue, never the window or the card scan.
    private let destQueue = DispatchQueue(label: "app.dest", qos: .utility)
    private var timers: [Timer] = []
    private var observers: [NSObjectProtocol] = []

    init(preview: Bool = false) {
        isPreview = preview
        settings = preview ? AppSettings() : SettingsStore.decode(UserDefaults.standard.data(forKey: SettingsStore.defaultsKey))
        tools = settings.tools.resolved()
        guard !preview else { return }
        loadHistory()
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
        guard !isPreview else { return }
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
        for v in verifies where !v.isFinished { v.refresh() }
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
    /// Created on first use (the top bar's Hardware dot or the window); `waitForHardware` never creates it.
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

    // MARK: Settings page

    func openSettings(_ section: SettingsSection = .folders) { settingsSection = section }
    func closeSettings() { settingsSection = nil }

    // MARK: Volumes and destinations (always off the main thread)

    func refreshVolumes() {
        guard !isPreview else { return }
        let roots = settings.destRoots
        let known = volumeConnections
        probeQueue.async { [weak self] in
            let found = Volumes.scan(destRoots: roots)
            let mounted = Set((FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: []) ?? [])
                .map(\.path))
            var connections: [String: String] = [:]
            for v in found where !v.isDestination {
                connections[v.path] = known[v.path] ?? AppModel.connectionLabel(DriveLinks.resolve(v.path))
            }
            DispatchQueue.main.async { self?.applyVolumes(found, mounted: mounted, connections: connections) }
        }
    }

    private func applyVolumes(_ found: [VolumeItem], mounted: Set<String>, connections: [String: String]) {
        if settings.autoDestinations {
            let next = Volumes.autoDestinations(current: settings.destRoots, found: found, mounted: mounted,
                                                ignored: Set(settings.ignoredVolumes),
                                                sources: selectedSources.union(manualSources))
            if next != settings.destRoots {
                settings.destRoots = next
                refreshVolumes()   // the new destinations are no longer card candidates
                return
            }
        }
        selectedSources = Volumes.selectionAfterRefresh(previousPaths: volumes.map(\.path), found: found,
                                                        selected: selectedSources, manual: manualSources)
        volumes = found
        if connections != volumeConnections { volumeConnections = connections }
        syncInspections()
        for j in jobs { j.objectWillChange.send() }   // "Eject card" follows the scan
    }

    /// Free space / size / filesystem / existence of every destination, whether the custom LUT and the proxy folder
    /// exist — every 5 s, after a change and from the refresh button. Never on the main thread.
    func refreshDestinations() {
        guard !isPreview else { return }
        if probing {
            probeAgain = true   // runs once the check in flight returns (it may hang on a network disk)
            return
        }
        probing = true
        let roots = settings.destRoots
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        let knownLinks = destConnections
        probedRoots = roots
        probedLut = lut
        probedFolder = folder
        destQueue.async { [weak self] in
            var found: [String: DestinationStatus] = [:]
            for r in roots {
                var s = AppModel.probe(r)
                if s.exists { s.connection = knownLinks[r] ?? AppModel.connectionLabel(DriveLinks.resolve(r)) }
                found[r] = s
            }
            let lutOK = lut.map { FileManager.default.fileExists(atPath: $0) }
            let folderOK = folder.map { ProxyPreflight.isDirectory($0) }
            DispatchQueue.main.async {
                guard let self else { return }
                self.destStatus = found
                // A disk that went away forgets its connection (another disk may mount there next).
                self.destConnections = found.compactMapValues(\.connection)
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
        var s = DestinationStatus(free: Int64(fs.f_bavail) * Int64(fs.f_bsize), total: Int64(fs.f_blocks) * Int64(fs.f_bsize),
                                  format: format, exists: true, checking: false)
        s.volume = SpacePlanner.volumeKey(path)
        s.label = HistoryText.diskName(path)
        return s
    }

    /// "Thunderbolt", "USB 3 (5 Gb/s)", "NVMe"…: the USB link speed, else the bus.
    nonisolated static func connectionLabel(_ link: DriveLink) -> String? {
        if let u = link.usb { return UsbSpeeds.label(u.link) }
        return link.bus == .unknown ? nil : StorageBuses.label(link.bus)
    }

    /// The name a destination is shown with: its folder's name ("RAID_A"), "/" as it is.
    nonisolated static func diskName(_ root: String) -> String {
        let n = (root as NSString).lastPathComponent
        return n.isEmpty ? root : n
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
        guard let path = pickFolder(title: "Choose a card or a folder with footage") else { return }
        if !manualSources.contains(path) { manualSources.append(path) }
        selectedSources.insert(path)
        syncInspections()
    }

    func removeManualSource(_ path: String) {
        manualSources.removeAll { $0 == path }
        selectedSources.remove(path)
        syncInspections()
    }

    func addDestination() {
        guard let path = pickFolder(title: "Choose a destination folder (root)") else { return }
        if !settings.destRoots.contains(path) { settings.destRoots.append(path) }
        if let vol = Volumes.volumeRoot(of: path) {   // added by hand again: automatic again
            let key = volumes.first { $0.path == vol }.map(Volumes.volumeKey) ?? (vol as NSString).lastPathComponent
            settings.ignoredVolumes.removeAll { $0 == key || $0 == (vol as NSString).lastPathComponent }
        }
        refreshVolumes()
    }

    func removeDestination(_ path: String) {
        // Removed by hand: remember the drive so it is not added automatically again.
        if let vol = Volumes.volumeRoot(of: path) {
            let key = volumes.first { $0.path == vol }.map(Volumes.volumeKey) ?? (vol as NSString).lastPathComponent
            if !settings.ignoredVolumes.contains(key) { settings.ignoredVolumes.append(key) }
        }
        settings.destRoots.removeAll { $0 == path }
        refreshVolumes()
    }

    /// The .cube dialog; a picked file switches to custom mode. False when cancelled (nothing changes).
    @discardableResult
    func pickLUT() -> Bool {
        let panel = NSOpenPanel()
        panel.title = "Choose a LUT (.cube)"
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
        guard let path = pickFolder(title: "Choose a folder for proxies") else { return }
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

    /// The card folder name: the typed name, else the suggestion (clips, next in line), else the default name.
    func sourceName(_ path: String) -> String {
        cardNames(for: selectedInOrder.contains(path) ? selectedInOrder : selectedInOrder + [path])[path]?.name ?? defaultName(path)
    }

    /// The default card folder name without touching the disk (main thread): the scanned volume's name, else the
    /// folder's name — what `Layout.defaultCardName(for:)` gives, which the engine itself still calls off the main thread.
    func defaultName(_ path: String) -> String {
        Layout.sanitize(volumes.first { $0.path == path }?.name ?? (path as NSString).lastPathComponent)
    }

    func customName(_ path: String) -> String? {
        let t = cardNames[path]?.trimmingCharacters(in: .whitespaces) ?? ""
        return t.isEmpty ? nil : t
    }

    /// The ticked sources in the order they are shown (volumes, then folders).
    var selectedInOrder: [String] { (volumes.map(\.path) + manualSources).filter { selectedSources.contains($0) } }

    /// The used space of a ticked card volume (what it will copy, at most); nil for a folder (not scanned on the main thread).
    func usedBytes(_ path: String) -> Int64? {
        volumes.first { $0.path == path }.map { max(0, $0.total - $0.free) }
    }

    /// What the ticked cards take, when every one is a scanned volume; nil otherwise.
    var selectedBytes: Int64? {
        var sum: Int64 = 0
        for p in selectedInOrder {
            guard let b = usedBytes(p) else { return nil }
            sum += b
        }
        return sum
    }

    // MARK: Jobs

    var canStart: Bool {
        !terminating && !starting && !selectedSources.isEmpty
            && (!needsDestinations || (!settings.destRoots.isEmpty && settings.destRoots.allSatisfy { destStatus[$0]?.exists == true }))
            && lutProblem == nil && proxyFolderProblem == nil
            && !spaceProblems.contains { $0.severity == .blocking }
    }

    /// A selection made only of appends goes to the destinations in history: step 2 may then be empty.
    var needsDestinations: Bool { selectedInOrder.contains { !appendChosen($0) } }

    /// Why Start is disabled, in one line (nil when it can start or nothing is ticked yet).
    var startProblem: String? {
        if terminating { return nil }
        if let p = lutProblem { return p }
        if let p = proxyFolderProblem { return p }
        if selectedSources.isEmpty { return nil }
        if !needsDestinations { return nil }
        if settings.destRoots.isEmpty { return "Add a destination disk." }
        if let missing = settings.destRoots.first(where: { destStatus[$0].map { !$0.exists && !$0.checking } ?? false }) {
            return "\(AppModel.diskName(missing)) is not connected."
        }
        return nil
    }

    /// A copy writes or verifies the destinations — no speed measurement now.
    var copyRunning: Bool { QueueRules.measureBlocked(jobs.map(\.status.phase)) || verifies.contains { $0.isRunning } }

    /// Running, paused (really standing), waiting: the top bar's pill and the queue's count.
    var queueCounts: (running: Int, paused: Int, waiting: Int) {
        var running = 0, paused = 0, waiting = 0
        for j in jobs {
            let p = j.queuePhase
            if p == .waiting { waiting += 1 } else if !p.isFinished { if j.status.pauseHeld { paused += 1 } else { running += 1 } }
        }
        for v in verifies where !v.isFinished {
            if v.isRunning { running += 1 } else { waiting += 1 }
        }
        return (running, paused, waiting)
    }

    /// A waiting job behind a job paused in its copy does not start (copies run one after another).
    func waitsForPausedJob(_ item: JobItem) -> Bool {
        PausedQueue.blocked(item.queuePhase, others: jobs.filter { $0 !== item }.map { ($0.queuePhase, $0.status.paused) })
    }

    /// "One card at a time": a waiting job behind one that is still running (its proxies, verification or report).
    func waitsForPreviousJob(_ item: JobItem) -> Bool {
        item.oneAtATime && item.queuePhase == .waiting
            && jobs.contains { $0 !== item && !$0.status.phase.isFinished && $0.queuePhase != .waiting }
    }

    /// "Add to queue" while a card waits for or is in its copy — a new one then waits.
    var startQueues: Bool {
        jobs.contains { [.waiting, .copying, .flushing, .verifying].contains($0.queuePhase) }
            || (settings.oneJobAtATime && jobs.contains { !$0.status.phase.isFinished })
    }

    /// The custom LUT and the proxy folder are checked once more right before the start (they may sit on a disk that
    /// just went away) — off the main thread, at most 5 s each; meanwhile the start button is disabled.
    func start() {
        guard canStart, !isPreview else { return }
        let lut = ProxyPreflight.lutInUse(settings.proxy, analyzeClips: settings.analyzeClips)
        let folder = ProxyPreflight.extraFolderInUse(settings.proxy)
        guard lut != nil || folder != nil else { return startAfterInspections() }
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
                if self.canStart { self.startAfterInspections() }   // otherwise the red text says why
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

    /// Queues one job: copies run one after another (they share the destination disks); post-processing runs in
    /// parallel on its own queue, so the next card starts copying while proxies render — or, with "One card at a time",
    /// on the copy queue itself, so the next card waits until this one is completely finished.
    func enqueue(_ item: JobItem) {
        jobs.append(item)
        let oneAtATime = settings.oneJobAtATime
        item.oneAtATime = oneAtATime
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
            if needsPost && oneAtATime {
                item.job.runPost()
                DispatchQueue.main.async {
                    self?.jobFinished(item)
                    self?.writeDayReports(item)
                }
            } else if needsPost {
                self?.postQueue.async {
                    item.job.runPost()
                    DispatchQueue.main.async {
                        self?.jobFinished(item)
                        self?.writeDayReports(item)
                    }
                }
            } else {
                DispatchQueue.main.async {
                    self?.jobFinished(item)
                    self?.writeDayReports(item)
                }
            }
        }
    }

    private func copyFinished(_ item: JobItem) {
        // Spec D.22.7: the success sound only when the card may be formatted (copied alone is not a success).
        guard !terminating, JobProgress.chime(item.status) else { return }
        NSSound(named: "Glass")?.play()
    }

    func jobFinished(_ item: JobItem) {
        item.refresh()
        loadHistory()   // the card's line follows ("Backed up … — verified, safe to format")
        // The next estimate (Windows ruling M2: a cancelled job or an unfinished stage 2 teaches nothing).
        if let r = item.report {
            rates.learn(r, analyze: item.job.config.analyzeClips, proxy: item.job.config.proxy,
                        stage2: item.progress.stage2Elapsed, cancelled: item.isCancelled, done: item.status.phase == .done)
        }
        // Verify-in-proxy: the card is safe to format only now, after the proxies (spec D.22.7 — the chime with it).
        if !terminating, item.status.verifyInProxy, JobProgress.chime(item.status) { NSSound(named: "Glass")?.play() }
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

    /// Pause: the file or clip in progress finishes, then the job stands (the Mac stays awake meanwhile).
    func pause(_ item: JobItem) {
        guard item.isRunning else { return }
        item.job.pause()
        item.refresh()
    }

    func resume(_ item: JobItem) {
        item.job.resume()
        item.refresh()
    }

    func clearFinished() {
        jobs.removeAll { $0.isFinished && !$0.ejecting }
        verifies.removeAll { $0.isFinished }
    }

    /// "Eject card" only while the copied card is in its slot per the last scan.
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
                    self?.message = "Couldn't eject the card: \(result.error ?? "")"
                }
                self?.checkTerminateReady()
            }
        }
    }

    func reveal(_ item: JobItem) {
        guard let p = item.plan?.cardPaths.first else { return }
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)])
    }

    /// The proxy folder of the first destination, in Finder (looked up off the main thread).
    func revealProxies(_ item: JobItem) {
        guard let card = item.plan?.cardPaths.first else { return }
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let proxies = (card as NSString).appendingPathComponent(Layout.proxyDirName)
            let target = FileManager.default.fileExists(atPath: proxies) ? proxies : card
            DispatchQueue.main.async {
                if FileManager.default.fileExists(atPath: target) {
                    NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: target)])
                } else {
                    self?.message = "Folder not found: \(target)"
                }
            }
        }
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
                    self?.message = "Report not found on any destination:\n"
                        + cards.map { QueueRules.reportPath(cardPath: $0, cardName: name) }.joined(separator: "\n")
                }
            }
        }
    }

    // MARK: Quit while copying

    /// A job that has not finished, or an eject in flight.
    var hasBusyJobs: Bool {
        jobs.contains { !$0.isFinished || $0.ejecting } || verifies.contains { !$0.isFinished } || dayReportsRunning > 0 || makingSummary
    }

    /// Asked by the app delegate. With unfinished jobs: confirm, cancel them all and quit once they have cleaned up.
    func shouldTerminate() -> NSApplication.TerminateReply {
        for j in jobs { j.refresh() }
        for v in verifies { v.refresh() }
        guard hasBusyJobs else { return .terminateNow }
        if terminating {
            // An NSAlert, not `message`: the main window (and its alert) may already be closed.
            let notice = NSAlert()
            notice.messageText = "Quitting — waiting for the cancelled jobs to clean up."
            notice.runModal()
            return .terminateCancel
        }
        if jobs.contains(where: { !$0.isFinished }) || verifies.contains(where: { !$0.isFinished }) {
            let alert = NSAlert()
            alert.messageText = "An offload is running. Quit anyway?"
            alert.informativeText = "Files in progress are deleted; finished files stay."
            alert.addButton(withTitle: "Quit")
            alert.addButton(withTitle: "Keep running")
            guard alert.runModal() == .alertFirstButtonReturn else { return .terminateCancel }
        }
        terminating = true
        for j in jobs {
            j.job.cancel()
            j.refresh()
        }
        for v in verifies {
            v.job.cancel()
            v.refresh()
        }
        if !hasBusyJobs { return .terminateNow }
        return .terminateCancel   // checkTerminateReady() quits once everything has cleaned up
    }

    func checkTerminateReady() {
        guard terminating, !hasBusyJobs else { return }
        NSApp.terminate(nil)
    }

#if DEBUG
    /// Snapshot rendering: what the disk probes would have found.
    func previewState(volumes: [VolumeItem], connections: [String: String], destinations: [String: DestinationStatus]) {
        self.volumes = volumes
        volumeConnections = connections
        destStatus = destinations
    }
#endif
}

import SwiftUI

extension AppModel {
    /// The window's look: dark by default (docs/ui-navrh), light, or nil = follow the system.
    var colorScheme: ColorScheme? {
        switch settings.appearance {
        case "light": return .light
        case "system": return nil
        default: return .dark
        }
    }
}
