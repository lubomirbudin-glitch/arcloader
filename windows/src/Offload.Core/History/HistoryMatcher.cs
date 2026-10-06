namespace Offload.Core.History;

/// Identical: nothing on the card that the backup lacks (files may have been deleted in the camera — `Removed`).
/// NewFiles: only additions — they can be appended (spec 2.2). Changed: a known path has another size or time, or the
/// card shares no path at all with the backup (a clone, or a card emptied and reshot; ruling I3) — only a full copy.
/// NotCompared: the fingerprint differs but the entry's file list is missing or damaged.
public enum HistoryMatchKind { Identical, NewFiles, Changed, NotCompared }

public sealed record HistoryMatch(HistoryEntry Entry, HistoryMatchKind Kind, IReadOnlyList<SourceFile> New, long NewBytes,
    IReadOnlyList<string> Changed, int Removed);

public static class HistoryMatcher
{
    /// Spec 2.1, steps 2–3. `candidateFiles`: the entry's file list (only needed when the fingerprints differ).
    /// `filesystem`: the card's — FAT/FAT32 times may differ by whole hours (R3).
    public static HistoryMatch Match(SourceScan scan, string fingerprint, HistoryEntry candidate,
        IReadOnlyList<SourceFile>? candidateFiles, string? filesystem)
    {
        if (candidate.Fingerprint == fingerprint) return new(candidate, HistoryMatchKind.Identical, [], 0, [], 0);
        if (candidateFiles == null) return new(candidate, HistoryMatchKind.NotCompared, [], 0, [], 0);
        bool localTime = FileTimes.StoresLocalTime(filesystem);
        var known = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var f in candidateFiles) known[f.RelPath] = f;
        int storedCount = known.Count;
        var added = new List<SourceFile>();
        var changed = new List<string>();
        foreach (var f in scan.Files)
        {
            if (!known.Remove(f.RelPath, out var k)) added.Add(f);
            else if (k.Size != f.Size || !FileTimes.Same(k.ModifiedUtc, f.ModifiedUtc, localTime)) changed.Add(f.RelPath);
        }
        // Ruling I3: nothing of the stored list is on the card, yet the card has files — another card, not "new files".
        if (storedCount > 0 && known.Count == storedCount && added.Count > 0)
            return new(candidate, HistoryMatchKind.Changed, [], 0, added.Select(f => f.RelPath).ToList(), known.Count);
        var kind = changed.Count > 0 ? HistoryMatchKind.Changed : added.Count > 0 ? HistoryMatchKind.NewFiles : HistoryMatchKind.Identical;
        return new(candidate, kind, added, added.Sum(f => f.Size), changed, known.Count);
    }
}
