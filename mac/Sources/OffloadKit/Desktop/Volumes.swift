import Foundation

/// A mounted volume offered in the Karty section. `identity` (`CardIdentity`) tells two cards in the same slot
/// apart; `ejectProblem` (`CardEject.problem`) is nil when "Vysunout kartu" may eject it.
public struct VolumeItem: Identifiable, Hashable {
    public var id: String { path }
    public let name: String
    public let path: String
    public let total: Int64
    public let free: Int64
    public let format: String
    public let looksLikeCard: Bool
    public let isDestination: Bool
    public let identity: String?
    public let ejectProblem: String?

    public init(name: String, path: String, total: Int64, free: Int64, format: String, looksLikeCard: Bool,
                isDestination: Bool, identity: String? = nil, ejectProblem: String? = nil) {
        self.name = name
        self.path = path
        self.total = total
        self.free = free
        self.format = format
        self.looksLikeCard = looksLikeCard
        self.isDestination = isDestination
        self.identity = identity
        self.ejectProblem = ejectProblem
    }
}

public enum Volumes {
    static let markers: Set<String> = ["DCIM", "PRIVATE", "CLIP", "CONTENTS", "XDROOT", "M4ROOT", "AVF_INFO", "CANONMSC", "BRAW", "MISC"]
    static let mediaExt: Set<String> = ["braw", "r3d", "rdm", "rdc", "mxf", "mov", "mp4", "ari", "crm"]

    /// Typical structure of camera media: known top-level folders, or clips at most one folder down
    /// (ARRI/RED/Blackmagic keep each clip or reel in its own folder). Moved from the app, same rules.
    public static func looksLikeCameraCard(_ path: String) -> Bool {
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

    /// The selection after a rescan: a card that just appeared (a new path) is ticked; a source that disappeared or
    /// now holds a destination drops out; manual folders stay.
    public static func selectionAfterRefresh(previousPaths: [String], found: [VolumeItem], selected: Set<String>,
                                             manual: [String]) -> Set<String> {
        let previous = Set(previousPaths)
        var result = selected
        for v in found where v.looksLikeCard && !previous.contains(v.path) { result.insert(v.path) }
        return result.filter { p in found.contains { $0.path == p && !$0.isDestination } || manual.contains(p) }
    }

    /// Card candidates under /Volumes (external, ejectable or removable; never the startup volume), sorted by name.
    /// Touches every mounted volume — never call it on the main thread.
    public static func scan(destRoots: [String]) -> [VolumeItem] {
        let keys: [URLResourceKey] = [.volumeNameKey, .volumeIsInternalKey, .volumeIsEjectableKey, .volumeIsRemovableKey,
                                      .volumeTotalCapacityKey, .volumeAvailableCapacityKey, .volumeIsRootFileSystemKey,
                                      .volumeIsLocalKey]
        let urls = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) ?? []
        var found: [VolumeItem] = []
        for url in urls {
            guard let v = try? url.resourceValues(forKeys: Set(keys)) else { continue }
            if v.volumeIsRootFileSystem == true { continue }
            let external = v.volumeIsInternal == false || v.volumeIsEjectable == true || v.volumeIsRemovable == true
            guard external, url.path.hasPrefix("/Volumes/") else { continue }
            let path = url.path
            let isDest = destRoots.contains { $0 == path || $0.hasPrefix(path + "/") }
            let problem = CardEject.problem(exists: true, isVolumeRoot: true, isLocal: v.volumeIsLocal ?? true,
                                            isRootFileSystem: false, isInternal: v.volumeIsInternal,
                                            isEjectable: v.volumeIsEjectable, isRemovable: v.volumeIsRemovable)
            found.append(VolumeItem(name: v.volumeName ?? url.lastPathComponent, path: path,
                                    total: Int64(v.volumeTotalCapacity ?? 0), free: Int64(v.volumeAvailableCapacity ?? 0),
                                    format: IO.filesystemType(path), looksLikeCard: !isDest && looksLikeCameraCard(path),
                                    isDestination: isDest, identity: CardIdentity.of(path), ejectProblem: problem))
        }
        return found.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }
}
