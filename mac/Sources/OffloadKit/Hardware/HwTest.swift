import Foundation

public struct DriveMeasurement: Codable, Equatable {
    public let role: String
    public let root: String
    public let readMBps: Double?
    public let writeMBps: Double?
}

public struct HwTestResult: Codable {
    public let time: Date
    public let drives: [RoleLink]
    public let connectors: [UsbConnector]
    public let measurements: [DriveMeasurement]
    public let advice: [Advice]
}

/// A thread-safe stop flag shared by the UI or a signal handler and a running test.
public final class CancelFlag: @unchecked Sendable {
    private let lock = NSLock()
    private var value = false

    public init() {}

    public func cancel() {
        lock.lock(); value = true; lock.unlock()
    }

    public var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return value }
}

/// The hardware check (spec 8a.2): topology and links in a moment; with `measure`, a short read of each card and
/// write + read of each destination (a temporary file, deleted). Nothing is ever written to a card.
public enum HwTest {
    public static var defaultPath: String { AppSupport.folder + "/hwtest.json" }

    static func roles(cards: [String], destinations: [String], rolesByDrive: Bool = false) -> [(role: String, root: String)] {
        var list: [(role: String, root: String)] = cards.enumerated().map { (cards.count == 1 ? RoleLink.card : "\(RoleLink.card) \($0.offset + 1)", $0.element) }
        list += destinations.enumerated().map { (rolesByDrive ? volumeName($0.element) : RoleLink.destination($0.offset + 1), $0.element) }
        return list
    }

    static func volumeName(_ path: String) -> String {
        if let name = (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeNameKey]))?.volumeName, !name.isEmpty,
           (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeURLKey]))?.volume?.path == path {
            return name
        }
        return PortLabels.volumeLabel(path)
    }

    static func isCard(_ role: String) -> Bool { RoleLink.isCardRole(role) }

    /// Whether two paths are on the same volume (unknown → false). A path that does not exist yet counts on the volume
    /// of its nearest existing folder — where it would be created (a new folder on the card is the card).
    public static func sameVolume(_ a: String, _ b: String) -> Bool {
        guard let da = volumeDevice(a), let db = volumeDevice(b) else { return false }
        return da == db
    }

    /// The device of `path`, else of its nearest existing parent; nil when nothing on the way exists.
    static func volumeDevice(_ path: String) -> dev_t? {
        var p = path
        while true {
            if let d = IO.device(of: p) { return d }
            let parent = (p as NSString).deletingLastPathComponent
            if parent.isEmpty || parent == p { return nil }
            p = parent
        }
    }

    /// "/" and every volume under /Volumes — the drives listed when no card and no destination is given.
    public static func allVolumes() -> [String] {
        (FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: nil, options: [.skipHiddenVolumes]) ?? [])
            .map(\.path).filter { $0 == "/" || $0.hasPrefix("/Volumes/") }
    }

    /// Off the main thread: the destinations that exist; with neither cards nor destinations, every volume (byDrive).
    public static func targets(cards: [String], destinations: [String], byDrive: Bool) -> (destinations: [String], byDrive: Bool) {
        let dests = destinations.filter { var d: ObjCBool = false; return FileManager.default.fileExists(atPath: $0, isDirectory: &d) && d.boolValue }
        if !byDrive && cards.count + dests.count > 0 { return (dests, false) }
        return (allVolumes(), true)
    }

    // MB/s, or nil when the timing is unusable (zero / not finite).
    static func mbps(_ line: Bench.Line) -> Double? {
        line.seconds > 0 && line.seconds.isFinite ? Double(line.bytes) / line.seconds / 1e6 : nil
    }

    /// `isCancelled` stops the measurement at once — inside the device being measured too (its temporary file is
    /// deleted, it counts as not measured) — and the rest are skipped. A destination on a card's volume is never
    /// written to (no measurement, a warning).
    public static func run(cards: [String], destinations: [String], measure: Bool, bytesPerDevice: Int64 = 1_000_000_000,
                           portNames: [String: String]? = nil, rolesByDrive: Bool = false,
                           isCancelled: @escaping () -> Bool = { false }) -> HwTestResult {
        let list = HwTest.roles(cards: cards, destinations: destinations, rolesByDrive: rolesByDrive)
        let drives = list.map { RoleLink(role: $0.role, link: DriveLinks.resolve($0.root)) }
        let connectors = UsbScanner.scan().connectors
        let cardRoots = list.filter { isCard($0.role) }.map(\.root)
        let onCard = Set(list.filter { r in !isCard(r.role) && cardRoots.contains { sameVolume($0, r.root) } }.map(\.role))

        var measurements: [DriveMeasurement] = []
        if measure {
            measuring: for r in list {
                if isCancelled() { break }
                do {
                    if isCard(r.role) {
                        let line = try Bench.read(path: r.root, limit: bytesPerDevice, blockSizes: [16 << 20], isCancelled: isCancelled)[0]
                        measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: mbps(line), writeMBps: nil))
                    } else if onCard.contains(r.role) {
                        measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: nil, writeMBps: nil))   // never write to a card
                    } else {
                        let lines = try Bench.write(dir: r.root, size: bytesPerDevice, blockSize: 16 << 20, isCancelled: isCancelled)
                        measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: mbps(lines[1]), writeMBps: mbps(lines[0])))
                    }
                } catch is CancellationError {
                    break measuring   // interrupted: not measured, the rest skipped
                } catch {
                    measurements.append(DriveMeasurement(role: r.role, root: r.root, readMBps: nil, writeMBps: nil))
                }
            }
        }
        return HwTestResult(time: Date(), drives: drives, connectors: connectors, measurements: measurements,
                            advice: advise(drives: drives, connectors: connectors, measurements: measurements, portNames: portNames))
    }

    /// The advice for a test result: a destination on a card's volume, then the Advisor's advice with the measured
    /// pace (cards read, destinations written). The app calls it again after a port is renamed.
    public static func advise(drives: [RoleLink], connectors: [UsbConnector], measurements: [DriveMeasurement],
                              portNames: [String: String]?) -> [Advice] {
        let cards = drives.filter(\.isCard)
        var advice: [Advice] = []
        for d in drives where !d.isCard {
            if let card = cards.first(where: { sameVolume($0.link.root, d.link.root) }) {
                advice.append(Advice(kind: .warning,
                                     text: "\(d.role) is on the same volume as \(card.role) — nothing is written to a card, so writing is not measured; pick another destination drive."))
            }
        }
        var pace: [String: Double] = [:]
        for m in measurements {
            if let v = isCard(m.role) ? m.readMBps : m.writeMBps, v > 0 { pace[m.role] = v }
        }
        advice += Advisor.advise(drives, connectors: connectors, measuredMBps: pace.isEmpty ? nil : pace, portNames: portNames)
        return Advisor.stable(advice)
    }

    /// Atomic (temporary file + rename).
    public static func save(_ result: HwTestResult, to path: String) throws {
        try IO.mkdirs((path as NSString).deletingLastPathComponent)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        enc.dateEncodingStrategy = .iso8601
        try IO.writeFileDurably(path, try enc.encode(result))
    }

    /// nil for a missing, partial or foreign file (never hands out half a result).
    public static func load(_ path: String) -> HwTestResult? {
        guard let data = FileManager.default.contents(atPath: path) else { return nil }
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        return try? dec.decode(HwTestResult.self, from: data)
    }
}
