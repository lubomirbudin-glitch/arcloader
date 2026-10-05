import AppKit
import Combine
import OffloadKit
import SwiftUI

struct AppSettings: Codable, Equatable {
    var destRoots: [String] = []
    var verify = true
    var analyzeClips = true
    var proxy = ProxySettings()
    var tools = ExternalTools()
}

/// One card in the queue; mirrors the job's status for SwiftUI.
final class JobItem: ObservableObject, Identifiable {
    let id = UUID()
    let job: OffloadJob
    let sourceName: String
    @Published var status = JobStatus()
    @Published var plan: OffloadPlan?
    @Published var readRate: Double = 0
    @Published var destRates: [Double] = []
    @Published var eta: Double?
    @Published var ejected = false
    var report: OffloadReport? { job.report }

    private var lastBytes: Int64 = 0
    private var lastDest: [Int64] = []
    private var lastTime = Date()
    private var lastPhase: JobStatus.Phase = .waiting

    init(job: OffloadJob, sourceName: String) {
        self.job = job
        self.sourceName = sourceName
    }

    var isRunning: Bool { status.phase != .waiting && !status.phase.isFinished }

    func refresh() {
        let s = job.status()
        let now = Date()
        let dt = now.timeIntervalSince(lastTime)
        if plan == nil, let p = job.plan { plan = p }
        if let c = s.copy, dt > 0.2 {
            let copying = s.phase == .copying
            let bytes = copying ? c.readBytes : (c.verifiedBytes.min() ?? 0)
            let dests = copying ? c.writtenBytes : c.verifiedBytes
            if s.phase != lastPhase || lastDest.count != dests.count {
                lastBytes = bytes
                lastDest = dests
                readRate = 0
                destRates = Array(repeating: 0, count: dests.count)
            } else {
                let smooth = { (old: Double, new: Double) in old == 0 ? new : old * 0.6 + new * 0.4 }
                readRate = smooth(readRate, Double(bytes - lastBytes) / dt)
                destRates = dests.indices.map { smooth(destRates[$0], Double(dests[$0] - lastDest[$0]) / dt) }
                let remaining = Double(c.totalBytes - bytes)
                eta = readRate > 1 ? remaining / readRate : nil
                lastBytes = bytes
                lastDest = dests
            }
            lastTime = now
        }
        lastPhase = s.phase
        status = s
    }

    /// 0…1 for the current phase.
    var phaseProgress: Double {
        switch status.phase {
        case .copying:
            guard let c = status.copy, c.totalBytes > 0 else { return 0 }
            return Double(c.readBytes) / Double(c.totalBytes)
        case .verifying:
            guard let c = status.copy, c.totalBytes > 0 else { return 0 }
            return Double(c.verifiedBytes.reduce(0, +)) / Double(c.totalBytes * Int64(max(1, c.verifiedBytes.count)))
        case .analyzing, .proxies:
            guard status.clipCount > 0 else { return 0 }
            return (Double(status.clipIndex) + status.clipFraction) / Double(status.clipCount)
        case .done: return 1
        default: return 0
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

    private let copyQueue = DispatchQueue(label: "app.copy", qos: .userInitiated)
    private let postQueue = DispatchQueue(label: "app.post", qos: .utility)
    private var timer: Timer?
    private var observers: [NSObjectProtocol] = []

    init() {
        if let data = UserDefaults.standard.data(forKey: "settings"),
           let s = try? JSONDecoder().decode(AppSettings.self, from: data) {
            settings = s
        } else {
            settings = AppSettings()
        }
        tools = settings.tools.resolved()
        refreshVolumes()
        let nc = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didMountNotification, NSWorkspace.didUnmountNotification, NSWorkspace.didRenameVolumeNotification] {
            observers.append(nc.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                Task { @MainActor in self?.refreshVolumes() }
            })
        }
        timer = Timer.scheduledTimer(withTimeInterval: 0.33, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
    }

    private func save() {
        if let data = try? JSONEncoder().encode(settings) { UserDefaults.standard.set(data, forKey: "settings") }
        let t = settings.tools.resolved()
        if t != tools { tools = t }
    }

    private func tick() {
        for j in jobs where j.status.phase != .done || j.isRunning { j.refresh() }
    }

    var dateString: String {
        guard useCustomDate else { return Layout.today() }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: customDate)
    }

    // MARK: Volumes

    func refreshVolumes() {
        let keys: [URLResourceKey] = [.volumeNameKey, .volumeIsInternalKey, .volumeIsEjectableKey, .volumeIsRemovableKey,
                                      .volumeTotalCapacityKey, .volumeAvailableCapacityKey, .volumeIsRootFileSystemKey]
        let urls = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) ?? []
        var found: [VolumeItem] = []
        for url in urls {
            guard let v = try? url.resourceValues(forKeys: Set(keys)) else { continue }
            if v.volumeIsRootFileSystem == true { continue }
            let external = v.volumeIsInternal == false || v.volumeIsEjectable == true || v.volumeIsRemovable == true
            guard external, url.path.hasPrefix("/Volumes/") else { continue }
            let path = url.path
            let isDest = settings.destRoots.contains { $0 == path || $0.hasPrefix(path + "/") }
            found.append(VolumeItem(name: v.volumeName ?? url.lastPathComponent, path: path,
                                    total: Int64(v.volumeTotalCapacity ?? 0), free: Int64(v.volumeAvailableCapacity ?? 0),
                                    format: filesystemName(path), looksLikeCard: !isDest && looksLikeCameraCard(path),
                                    isDestination: isDest))
        }
        let newCards = found.filter { $0.looksLikeCard && !volumes.contains($0) }.map(\.path)
        volumes = found.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        selectedSources.formUnion(newCards)
        selectedSources = selectedSources.filter { p in volumes.contains { $0.path == p && !$0.isDestination } || manualSources.contains(p) }
    }

    private func filesystemName(_ path: String) -> String {
        var fs = statfs()
        guard statfs(path, &fs) == 0 else { return "" }
        return withUnsafeBytes(of: fs.f_fstypename) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
    }

    /// Typical structure of camera media: known top-level folders, or clips at most two levels deep
    /// (ARRI/RED/Blackmagic keep each clip or reel in its own folder).
    private func looksLikeCameraCard(_ path: String) -> Bool {
        let markers: Set<String> = ["DCIM", "PRIVATE", "CLIP", "CONTENTS", "XDROOT", "M4ROOT", "AVF_INFO", "CANONMSC", "BRAW", "MISC"]
        let mediaExt: Set<String> = ["braw", "r3d", "rdm", "rdc", "mxf", "mov", "mp4", "ari", "crm"]
        let fm = FileManager.default
        guard let items = try? fm.contentsOfDirectory(atPath: path) else { return false }
        if items.contains(where: { markers.contains($0.uppercased()) }) { return true }
        func hasMedia(_ names: [String]) -> Bool { names.contains { mediaExt.contains(($0 as NSString).pathExtension.lowercased()) } }
        if hasMedia(items) { return true }
        for dir in items.prefix(50) where !dir.hasPrefix(".") {
            if let sub = try? fm.contentsOfDirectory(atPath: path + "/" + dir), hasMedia(sub) { return true }
        }
        return false
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

    func destinationInfo(_ path: String) -> (free: Int64, format: String, exists: Bool) {
        var isDir: ObjCBool = false
        let exists = FileManager.default.fileExists(atPath: path, isDirectory: &isDir) && isDir.boolValue
        var fs = statfs()
        guard exists, statfs(path, &fs) == 0 else { return (0, "", false) }
        let fmt = withUnsafeBytes(of: fs.f_fstypename) { String(cString: $0.bindMemory(to: CChar.self).baseAddress!) }
        return (Int64(fs.f_bavail) * Int64(fs.f_bsize), fmt, true)
    }

    func sourceName(_ path: String) -> String {
        cardNames[path].flatMap { $0.isEmpty ? nil : $0 } ?? Layout.defaultCardName(for: path)
    }

    // MARK: Jobs

    var canStart: Bool {
        !selectedSources.isEmpty && !settings.destRoots.isEmpty
            && settings.destRoots.allSatisfy { destinationInfo($0).exists }
    }

    func start() {
        let sources = (volumes.map(\.path) + manualSources).filter { selectedSources.contains($0) }
        for src in sources {
            var config = JobConfig(source: src, destRoots: settings.destRoots)
            config.cardName = cardNames[src].flatMap { $0.isEmpty ? nil : $0 }
            config.date = dateString
            config.verify = settings.verify
            config.analyzeClips = settings.analyzeClips
            config.proxy = settings.proxy
            config.tools = settings.tools
            let item = JobItem(job: OffloadJob(config: config), sourceName: sourceName(src))
            jobs.append(item)
            selectedSources.remove(src)
            // Copies run one after another (they share the destination disks); post-processing runs
            // in parallel on its own queue, so the next card starts copying while proxies render.
            copyQueue.async { [weak self] in
                guard !item.job.isCancelled else { return }
                let needsPost = item.job.runCopy()
                DispatchQueue.main.async { item.refresh(); self?.copyFinished(item) }
                if needsPost {
                    self?.postQueue.async {
                        item.job.runPost()
                        DispatchQueue.main.async { item.refresh(); self?.jobFinished(item) }
                    }
                } else {
                    DispatchQueue.main.async { self?.jobFinished(item) }
                }
            }
        }
    }

    private func copyFinished(_ item: JobItem) {
        guard item.status.safeToEject else { return }
        NSSound(named: "Glass")?.play()
    }

    private func jobFinished(_ item: JobItem) {
        item.refresh()
        if item.status.phase == .failed || item.status.copySucceeded == false {
            NSSound(named: "Basso")?.play()
        }
        NSApp.requestUserAttention(.informationalRequest)
    }

    func cancel(_ item: JobItem) { item.job.cancel() }

    func clearFinished() { jobs.removeAll { $0.status.phase.isFinished } }

    func eject(_ item: JobItem) {
        let url = URL(fileURLWithPath: item.job.config.source)
        do {
            try NSWorkspace.shared.unmountAndEjectDevice(at: url)
            item.ejected = true
        } catch {
            message = "Kartu se nepodařilo vysunout: \(error.localizedDescription)"
        }
    }

    func reveal(_ item: JobItem) {
        guard let p = item.plan?.cardPaths.first else { return }
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)])
    }

    func openReport(_ item: JobItem) {
        guard let plan = item.plan else { return }
        let path = "\(plan.cardPaths[0])/\(Layout.reportDirName)/\(plan.cardName)_report.html"
        NSWorkspace.shared.open(URL(fileURLWithPath: path))
    }
}
