using Offload.Core.Desktop;

namespace Offload.Core.History;

/// What one look at a card found (spec 2.1): size, clips, fingerprint and the history entry it matches. One scan serves
/// the history line, the card name suggestion (2.3) and the space check (2.5). `Error`: the card could not be read.
public sealed record CardInsight
{
    public required string Source { get; init; }
    /// Null for a folder that is not a drive root.
    public CardVolume? Volume { get; init; }
    public string? Filesystem { get; init; }
    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
    public IReadOnlyList<Clip> Clips { get; init; } = [];
    public string Fingerprint { get; init; } = "";
    public string Shape { get; init; } = "";
    public HistoryMatch? History { get; init; }
    /// New files found and every card folder of the earlier backup exists now (spec 2.2: the append is offered only
    /// then). Looked up with the scan, off the UI thread; a destination connected later shows after the next look.
    public bool AppendReachable { get; init; }
    /// The folder name the clips suggest (spec 2.3), or null.
    public NameSuggestion? Name { get; init; }
    /// "Na kartě je víc cívek: A001, A002" when the clips disagree.
    public string? NameNote { get; init; }
    /// "ALEXA 35 · LogC4" from one clip (CameraHints, UI návrh 6); null when unknown or not looked at. It comes from a
    /// follow-up step (`insight with { Camera = … }`), so the inspection itself is published without waiting for ffprobe (M11).
    public CameraLabel? Camera { get; init; }
    public string? Error { get; init; }

    /// A card emptied in the camera after the backup: history still knows it (Identical, everything "Removed").
    public bool IsEmptied => FileCount == 0 && History is { Removed: > 0 };
}

public static class CardInspector
{
    /// Reads only (a folder listing — nothing is ever written to the card). Runs off the UI thread; `ct` cancels it when
    /// the card is swapped. `history` null = not loaded (or unreadable): no history match.
    public static CardInsight Inspect(string source, IReadOnlyList<HistoryEntry>? history, HistoryStore? store, CancellationToken ct) =>
        Inspect(source, history, store, ct, CardVolume.Of, CardIdentity.Of, Layout.DriveRootOf, Directory.Exists);

    /// `volumeOf`, `identify`, `driveRootOf`: the disk reads, replaceable in tests.
    internal static CardInsight Inspect(string source, IReadOnlyList<HistoryEntry>? history, HistoryStore? store, CancellationToken ct,
        Func<string, CardVolume?> volumeOf, Func<string, string?> identify, Func<string, string?> driveRootOf,
        Func<string, bool>? directoryExists = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var driveRoot = driveRootOf(source);
            var volume = volumeOf(source);
            // A drive root whose volume cannot be read is a card that cannot be read — not a folder to match by shape.
            if (driveRoot != null && volume == null) return new CardInsight { Source = source, Error = Msg.CardVolumeUnreadable };
            var filesystem = volume?.Filesystem ?? NativeIO.FilesystemType(source);
            var scan = Scanner.Scan(source, ct);
            var (fingerprint, shape) = CardFingerprint.Compute(scan);
            ct.ThrowIfCancellationRequested();
            // Another card in the same reader while it was scanned: what was read belongs to neither.
            if (driveRoot != null && volume!.Identity != null && !string.Equals(identify(driveRoot), volume.Identity, StringComparison.OrdinalIgnoreCase))
                return new CardInsight { Source = source, Error = Msg.CardSwapped };
            HistoryMatch? match = null;
            if (history != null && HistoryStore.FindForCard(history, volume?.Key, shape) is { } candidate)
            {
                var list = candidate.Fingerprint == fingerprint ? null : store?.ReadFileList(candidate);
                match = HistoryMatcher.Match(scan, fingerprint, candidate, list, filesystem);
            }
            // Fix round 1, item 1 (ruling I5): a backup the app never finished is reachable for "Dokončit přerušenou
            // zálohu" also with nothing new on the card.
            bool reachable = volume != null
                             && match is { Kind: HistoryMatchKind.NewFiles } or { Kind: HistoryMatchKind.Identical, Entry.Unfinished: true }
                             && match.Entry.Destinations.Count > 0
                             && match.Entry.Destinations.All(d => d.CardPath != "" && (directoryExists ?? Directory.Exists)(d.CardPath));
            ct.ThrowIfCancellationRequested();
            var clips = ClipDetector.Detect(scan.Files);
            var (name, note, _) = ReelSuggestion.FromClips(clips);
            return new CardInsight
            {
                Source = source,
                Volume = volume,
                Filesystem = filesystem,
                TotalBytes = scan.TotalBytes,
                FileCount = scan.Files.Count,
                Clips = clips,
                Fingerprint = fingerprint,
                Shape = shape,
                History = match,
                AppendReachable = reachable,
                Name = name,
                NameNote = note,
            };
        }
        catch (Exception e) when (e is IOError or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            // Pulled out mid-scan, or a folder that cannot be listed: no insight, never an exception for the caller.
            return new CardInsight { Source = source, Error = Msg.CardUnreadable(e.Message) };
        }
    }
}
