import AppKit
import OffloadKit
import SwiftUI

/// The Hardware window (spec 8a.2, on demand): where the cards and disks are connected, advice, optional measurement,
/// the USB ports with user names. Tests run on one serial queue — one at a time; nothing is ever written to a card.
@MainActor
final class HardwareModel: ObservableObject {
    struct DriveRow: Identifiable, Equatable {
        var id: String { role + "|" + root }
        let role: String
        let root: String
        let description: String
    }

    struct AdviceRow: Identifiable, Equatable {
        let id: Int
        let kind: AdviceKind
        let text: String
    }

    struct MeasureRow: Identifiable, Equatable {
        var id: String { role }
        let role: String
        let root: String
        let read: String
        let write: String
    }

    struct PortRow: Identifiable {
        var id: String { connector.id }
        let connector: UsbConnector
        let label: String
        let device: String
        let drives: String
    }

    struct PortGroup: Identifiable {
        var id: String { controllerId }
        let controllerId: String
        let controller: String
        var ports: [PortRow]
    }

    @Published private(set) var drives: [DriveRow] = []
    @Published private(set) var advice: [AdviceRow] = []
    @Published private(set) var measurements: [MeasureRow] = []
    @Published private(set) var portGroups: [PortGroup] = []
    @Published private(set) var busy = false
    @Published private(set) var stopping = false
    @Published private(set) var status = ""
    @Published private(set) var lastTest: HwTestResult?
    @Published var showInternal = false { didSet { buildPorts() } }
    @Published var askMeasure = false

    private unowned let app: AppModel
    private var result: HwTestResult?
    private var byDrive = false
    private var savedMeasured = false
    private var flag: CancelFlag?
    /// The window was reopened while the previous run was still stopping: a quick test follows once it has ended.
    private var rerunQuick = false
    private let queue = DispatchQueue(label: "app.hwtest", qos: .userInitiated)

    init(app: AppModel) {
        self.app = app
        queue.async { [weak self] in
            let saved = HwTest.load(HwTest.defaultPath)
            DispatchQueue.main.async {
                self?.lastTest = saved
                self?.savedMeasured = !(saved?.measurements.isEmpty ?? true)
            }
        }
    }

    /// "Last check Oct 5, 2026 14:03 · no warnings" / "Not checked yet".
    var summary: String {
        guard let r = lastTest, let warnings = lastWarnings else { return "Not checked yet" }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "MMM d, yyyy H:mm"
        return "Last check \(f.string(from: r.time)) · " + (warnings == 0 ? "no warnings" : Fmt.count(warnings, "warning"))
    }

    /// Warnings of the last check (the top bar's dot: green with none, yellow with some); nil before any check.
    var lastWarnings: Int? { lastTest.map { r in r.advice.filter { $0.kind == .warning }.count } }

    var measureHint: String {
        if byDrive { return "To measure, tick a card or add a destination." }
        if app.copyRunning { return "No measuring during a copy — the test writes 1 GB to the destinations." }
        return ""
    }

    var canMeasure: Bool { !busy && !byDrive && result != nil && !app.copyRunning }
    var canStop: Bool { busy && !stopping }

    var measureQuestion: String {
        let dests = app.hardwareRoles().destinations
        let list = dests.isEmpty ? "   (none)" : dests.map { "   " + $0 }.joined(separator: "\n")
        return "Cards are only read (up to 1 GB each).\nA 1 GB temporary file is written to and read from each destination, then deleted:\n\(list)\n\nDepending on the disks this can take a few minutes."
    }

    /// The window opened (the top bar's Hardware button or ⌘U): a quick test.
    func windowOpened() {
        if busy {
            // Like Windows (one HW run at a time): the previous run (of a closed window) is still finishing.
            rerunQuick = true
            status = "Waiting for the previous check to finish…"
        } else {
            run(measure: false)
        }
    }

    /// The window closed: the run stops (a measurement deletes its temporary file).
    func windowClosed() { stop() }

    func run(measure: Bool) {
        guard !busy else { return }
        if measure && app.copyRunning { return }   // a copy may have started while the question was open
        let roles = app.hardwareRoles()
        let names = app.settings.portNames
        let flag = CancelFlag()
        self.flag = flag
        busy = true
        let running = measure ? "Measuring speed… (may take a few minutes)" : "Checking connections…"
        status = running
        queue.async { [weak self] in
            let (targets, all) = HwTest.targets(cards: roles.cards, destinations: roles.destinations, byDrive: roles.byDrive)
            // Never measure by volume: every disk would get a 1 GB file (the button is off then anyway).
            let r = HwTest.run(cards: roles.cards, destinations: targets, measure: measure && !all, portNames: names,
                               rolesByDrive: all, isCancelled: { flag.isCancelled })
            DispatchQueue.main.async { self?.finished(r, byDrive: all, measured: measure, cancelled: flag.isCancelled) }
        }
    }

    /// Stop, or the window closed: stops at once; "Stopping…" until the run has really ended.
    func stop() {
        guard busy, !stopping, let flag else { return }
        stopping = true
        rerunQuick = false
        flag.cancel()
        status = "Stopping…"
    }

    /// On quit (mirrors Windows `MainViewModel.WaitForHardware`): stop a measurement and wait — at most `timeout` s —
    /// until the hardware queue is idle, so `Bench.write` deletes its 1 GB temporary file before the process exits.
    /// Cancelling is checked per 16 MB block, so this is quick; the bound only guards against a hung disk.
    func stopAndWait(timeout: TimeInterval = 10) {
        flag?.cancel()
        rerunQuick = false
        let idle = DispatchSemaphore(value: 0)
        queue.async { idle.signal() }   // runs after the current test has returned (temporary file deleted)
        if idle.wait(timeout: .now() + timeout) == .timedOut { NSLog("%@", "hwtest: still running at exit") }
    }

    private func finished(_ r: HwTestResult, byDrive: Bool, measured: Bool, cancelled: Bool) {
        defer {
            if rerunQuick {
                rerunQuick = false
                run(measure: false)
            }
        }
        busy = false
        stopping = false
        result = r
        self.byDrive = byDrive
        status = cancelled && measured ? "Measurement stopped — showing what was measured." : ""
        rebuild()
        guard !cancelled else { return }
        lastTest = r
        // A quick test never replaces a saved measurement on disk (only the summary shows it).
        let hasMeasurement = !r.measurements.isEmpty
        if !hasMeasurement && savedMeasured { return }
        savedMeasured = hasMeasurement
        queue.async {
            do { try HwTest.save(r, to: HwTest.defaultPath) } catch { NSLog("%@", "hwtest save: \(error)") }
        }
    }

    private func rebuild() {
        guard let r = result else { return }
        measurements = r.measurements.map { MeasureRow(role: $0.role, root: $0.root, read: mb($0.readMBps), write: mb($0.writeMBps)) }
        rebuildNamed()
        buildPorts()
    }

    /// What shows port names: the drive lines and the advice.
    private func rebuildNamed() {
        guard let r = result else { return }
        let names = app.settings.portNames
        drives = r.drives.map { DriveRow(role: $0.role, root: $0.link.root, description: $0.link.describe(r.connectors, names)) }
        advice = HwTest.advise(drives: r.drives, connectors: r.connectors, measurements: r.measurements, portNames: names)
            .enumerated().map { AdviceRow(id: $0.offset, kind: $0.element.kind, text: $0.element.text) }
    }

    private func buildPorts() {
        guard let r = result else {
            portGroups = []
            return
        }
        var groups: [PortGroup] = []
        for c in r.connectors where c.userVisible || showInternal {
            let device = c.device.map { "\($0.name) — \(UsbSpeeds.label($0.link))" } ?? "free"
            let here = r.drives.filter { d in d.link.usb.map { c.has($0.connectorId) } ?? false }.map(\.role).joined(separator: ", ")
            let row = PortRow(connector: c, label: c.defaultLabel, device: device, drives: here)
            if let i = groups.firstIndex(where: { $0.controllerId == c.controllerId }) {
                groups[i].ports.append(row)
            } else {
                groups.append(PortGroup(controllerId: c.controllerId, controller: PortLabels.shortController(c.controllerName), ports: [row]))
            }
        }
        portGroups = groups
    }

    /// The user's name of a socket (its id, then the other half of a USB 3 hub).
    func portName(_ c: UsbConnector) -> String {
        let names = app.settings.portNames
        return ([c.id] + c.altIds).compactMap { names[$0] }.first { !$0.trimmingCharacters(in: .whitespaces).isEmpty } ?? ""
    }

    /// One name per socket, saved under its id; the drive lines and advice follow.
    func rename(_ c: UsbConnector, _ name: String) {
        var names = app.settings.portNames
        for alt in c.altIds { names[alt] = nil }
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        names[c.id] = trimmed.isEmpty ? nil : trimmed
        if names != app.settings.portNames { app.settings.portNames = names }
        rebuildNamed()
    }

    private func mb(_ v: Double?) -> String { v.map { String(format: "%.0f MB/s", $0) } ?? "—" }
}
