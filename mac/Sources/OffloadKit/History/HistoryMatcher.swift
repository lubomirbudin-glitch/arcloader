import Foundation

/// identical: nothing on the card that the backup lacks (files may have been deleted in the camera — `removed`).
/// newFiles: only additions — they can be appended. changed: a known path has another size or time, or the card shares
/// no path at all with the backup (a clone, or a card emptied and reshot; Windows ruling I3) — only a full copy.
/// notCompared: the fingerprint differs but the entry's file list is missing or damaged.
public enum HistoryMatchKind: Equatable { case identical, newFiles, changed, notCompared }

public struct HistoryMatch {
    public let entry: HistoryEntry
    public let kind: HistoryMatchKind
    public let new: [SourceFile]
    public let newBytes: Int64
    public let changed: [String]
    public let removed: Int

    public init(entry: HistoryEntry, kind: HistoryMatchKind, new: [SourceFile], newBytes: Int64, changed: [String], removed: Int) {
        self.entry = entry; self.kind = kind; self.new = new; self.newBytes = newBytes; self.changed = changed
        self.removed = removed
    }
}

public enum HistoryMatcher {
    /// `candidateFiles`: the entry's file list (only needed when the fingerprints differ). `filesystem`: the card's —
    /// FAT/FAT32 times may differ by whole hours (R3).
    public static func match(_ scan: SourceScan, fingerprint: String, candidate: HistoryEntry, candidateFiles: [SourceFile]?,
                             filesystem: String?) -> HistoryMatch {
        if candidate.fingerprint == fingerprint {
            return HistoryMatch(entry: candidate, kind: .identical, new: [], newBytes: 0, changed: [], removed: 0)
        }
        guard let stored = candidateFiles else {
            return HistoryMatch(entry: candidate, kind: .notCompared, new: [], newBytes: 0, changed: [], removed: 0)
        }
        let local = FileTimes.storesLocalTime(filesystem)
        var known = Dictionary(stored.map { ($0.relPath, $0) }, uniquingKeysWith: { _, b in b })
        let storedCount = known.count
        var added: [SourceFile] = [], changed: [String] = []
        for f in scan.files {
            guard let k = known.removeValue(forKey: f.relPath) else {
                added.append(f)
                continue
            }
            if k.size != f.size || !FileTimes.same(k.modifiedUtc, f.modifiedUtc, localTime: local) { changed.append(f.relPath) }
        }
        // Ruling I3: nothing of the stored list is on the card, yet the card has files — another card, not "new files".
        if storedCount > 0 && known.count == storedCount && !added.isEmpty {
            return HistoryMatch(entry: candidate, kind: .changed, new: [], newBytes: 0, changed: added.map(\.relPath), removed: known.count)
        }
        let kind: HistoryMatchKind = !changed.isEmpty ? .changed : !added.isEmpty ? .newFiles : .identical
        return HistoryMatch(entry: candidate, kind: kind, new: added, newBytes: added.reduce(0) { $0 + $1.size }, changed: changed,
                            removed: known.count)
    }
}
