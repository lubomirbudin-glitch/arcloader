import AppKit
import Foundation

/// Which card is mounted at a path: the volume UUID (APFS/HFS+), or the one macOS derives from the FAT/exFAT
/// serial number. A card reader mounts every card at the same kind of path; two formatted cards differ here.
public enum CardIdentity {
    /// nil when there is no readable volume at `path`. Touches the drive — never call it on the main thread.
    public static func of(_ path: String) -> String? {
        guard FileManager.default.fileExists(atPath: path),
              let v = try? URL(fileURLWithPath: path).resourceValues(forKeys: [.volumeUUIDStringKey, .volumeCreationDateKey])
        else { return nil }
        if let uuid = v.volumeUUIDString, !uuid.isEmpty { return "uuid:" + uuid }
        if let created = v.volumeCreationDate { return String(format: "created:%.0f", created.timeIntervalSince1970) }
        return nil
    }
}

/// Safely removes a card: `NSWorkspace.unmountAndEjectDevice` unmounts the volume and ejects the media — a card in a
/// reader leaves the reader connected for the next card; a USB disk is ejected as a whole device.
/// A waiting job behind a job paused in stage 1 (copying, flushing, verifying, its report) does not start: copies run one
/// after another (Windows `QueueRules.BlockedByPaused`).
public enum PausedQueue {
    public static let waitingText = "Waiting for a paused job."

    public static func blocked(_ phase: JobStatus.Phase, others: [(phase: JobStatus.Phase, paused: Bool)]) -> Bool {
        phase == .waiting && others.contains { $0.paused && [.copying, .flushing, .verifying, .reporting].contains($0.phase) }
    }
}

public enum CardEject {
    /// Why this volume must not be ejected, or nil. Allowed only for a local, whole, non-system volume that is
    /// ejectable, removable or external (USB, Thunderbolt, SD).
    public static func problem(exists: Bool, isVolumeRoot: Bool, isLocal: Bool, isRootFileSystem: Bool,
                               isInternal: Bool?, isEjectable: Bool?, isRemovable: Bool?) -> String? {
        guard exists else { return "card is not connected" }
        guard isLocal else { return "a network folder is not ejected" }
        guard isVolumeRoot else { return "the source is a folder, not a whole card" }
        if isRootFileSystem { return "the system disk cannot be ejected" }
        if !(isEjectable == true || isRemovable == true || isInternal == false) { return "an internal disk cannot be ejected" }
        return nil
    }

    /// Reads the volume at `source`. Touches the disk — never on the main thread.
    public static func problem(source: String) -> String? {
        let url = URL(fileURLWithPath: source)
        let keys: Set<URLResourceKey> = [.volumeURLKey, .volumeIsLocalKey, .volumeIsRootFileSystemKey, .volumeIsInternalKey,
                                         .volumeIsEjectableKey, .volumeIsRemovableKey]
        guard FileManager.default.fileExists(atPath: source), let v = try? url.resourceValues(forKeys: keys) else {
            return problem(exists: false, isVolumeRoot: false, isLocal: true, isRootFileSystem: false,
                           isInternal: nil, isEjectable: nil, isRemovable: nil)
        }
        let isRoot = v.volume.map { $0.standardizedFileURL.path == url.standardizedFileURL.path } ?? false
        return problem(exists: true, isVolumeRoot: isRoot, isLocal: v.volumeIsLocal ?? true,
                       isRootFileSystem: v.volumeIsRootFileSystem ?? false, isInternal: v.volumeIsInternal,
                       isEjectable: v.volumeIsEjectable, isRemovable: v.volumeIsRemovable)
    }

    /// Never throws; the error completes "Could not eject the card: …". Call off the main thread.
    public static func eject(_ source: String) -> (ok: Bool, error: String?) {
        if let p = problem(source: source) { return (false, p) }
        do {
            try NSWorkspace.shared.unmountAndEjectDevice(at: URL(fileURLWithPath: source))
            return (true, nil)
        } catch {
            return (false, error.localizedDescription)
        }
    }
}

/// Decisions of the queue panel that need no window (port of Windows `Desktop/QueueRules.cs`).
public enum QueueRules {
    public static let otherCardText = "the card in the reader is not the one that was copied"
    public static let inUseText = "another job is still copying from the card — eject it when that copy has finished"

    /// Why "Eject card" must not eject, or nil. `copied` = the card recorded when the copy started,
    /// `current` = the card at the source now (both `CardIdentity`), `cardInUse` = `cardInUse` for the other jobs.
    public static func ejectProblem(copied: String?, current: String?, cardInUse: Bool) -> String? {
        guard let current else { return "card is not connected" }
        guard let copied, copied.caseInsensitiveCompare(current) == .orderedSame else { return otherCardText }
        return cardInUse ? inUseText : nil
    }

    /// Another job still needs the card: it is queued or in stage 1 (copy, flush, verification, report) on the same
    /// volume. Stage 2 (clips, proxies) reads only the destinations, so it does not hold the card.
    public static func cardInUse(_ source: String, others: [(source: String, phase: JobStatus.Phase)]) -> Bool {
        let holding: Set<JobStatus.Phase> = [.waiting, .copying, .flushing, .verifying, .reporting]
        return others.contains { o in holding.contains(o.phase) && (inside(o.source, source) || inside(source, o.source)) }
    }

    /// The last scan shows the copied card at `source` (not gone, not another card in the same slot). Pure.
    public static func cardInDrive(_ source: String, copied: String?, scanned: [VolumeItem]) -> Bool {
        guard let copied else { return false }
        return scanned.contains { $0.path == trimmed(source) && $0.identity?.caseInsensitiveCompare(copied) == .orderedSame }
    }

    /// Measuring writes 1 GB to the destinations — not while a copy writes or verifies them.
    public static func measureBlocked(_ phases: [JobStatus.Phase]) -> Bool {
        phases.contains { $0 == .copying || $0 == .flushing || $0 == .verifying }
    }

    public static func reportPath(cardPath: String, cardName: String) -> String {
        cardPath + "/" + Layout.reportDirName + "/" + cardName + "_report.html"
    }

    /// The HTML report from the first destination that has it (one may be disconnected), or nil.
    /// Touches the destinations — never call it on the main thread.
    public static func firstReport(cardPaths: [String], cardName: String) -> String? {
        cardPaths.map { reportPath(cardPath: $0, cardName: cardName) }.first { FileManager.default.fileExists(atPath: $0) }
    }

    static func trimmed(_ path: String) -> String {
        var p = path
        while p.count > 1 && p.hasSuffix("/") { p.removeLast() }
        return p
    }

    /// `path` is `volume` or inside it (by path; no disk access).
    static func inside(_ path: String, _ volume: String) -> Bool {
        let p = trimmed(path), v = trimmed(volume)
        return p == v || p.hasPrefix(v == "/" ? "/" : v + "/")
    }
}
