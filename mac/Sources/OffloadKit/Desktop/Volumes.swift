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

    /// Can be written to (a read-only card or disk image never becomes a destination).
    public let isWritable: Bool

    public init(name: String, path: String, total: Int64, free: Int64, format: String, looksLikeCard: Bool,
                isDestination: Bool, identity: String? = nil, ejectProblem: String? = nil, isWritable: Bool = true) {
        self.isWritable = isWritable
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

    /// The key a volume is remembered by when the user removes it from the destinations.
    public static func volumeKey(_ v: VolumeItem) -> String { v.identity ?? v.name }

    /// The volume a destination lives on ("/Volumes/X/3ND" → "/Volumes/X"), or nil for a path off /Volumes.
    public static func volumeRoot(of path: String) -> String? {
        let c = path.split(separator: "/", omittingEmptySubsequences: true)
        guard c.count >= 2, c[0] == "Volumes" else { return nil }
        return "/Volumes/" + c[1]
    }

    /// Destinations after a volume scan with automatic destinations on: destinations on volumes that are no longer
    /// mounted drop out; connected external drives that can be written to, do not look like a card, are not a source
    /// and were not removed by the user join — under the same subfolder the other drive destinations use (e.g. "3ND").
    /// Destinations off /Volumes (a folder on this Mac) are never touched.
    public static func autoDestinations(current: [String], found: [VolumeItem], mounted: Set<String>,
                                        ignored: Set<String>, sources: Set<String>) -> [String] {
        var result = current.filter { root in
            guard let vol = volumeRoot(of: root) else { return true }
            return mounted.contains(vol)
        }
        let rests = Set(result.compactMap { root -> String? in
            guard let vol = volumeRoot(of: root) else { return nil }
            return String(root.dropFirst(vol.count))
        })
        let sub = rests.count == 1 ? rests.first! : ""
        for v in found where v.isWritable && !v.looksLikeCard && !sources.contains(v.path)
            && !ignored.contains(volumeKey(v)) && !ignored.contains(v.name) {
            guard !result.contains(where: { volumeRoot(of: $0) == v.path }) else { continue }
            result.append(v.path + sub)
        }
        return result
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
                                    isDestination: isDest, identity: CardIdentity.of(path), ejectProblem: problem,
                                    isWritable: FileManager.default.isWritableFile(atPath: path)))
        }
        return found.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }
}
