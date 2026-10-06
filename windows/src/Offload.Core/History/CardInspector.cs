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
    public string? Error { get; init; }

    /// A card emptied in the camera after the backup: history still knows it (Identical, everything "Removed").
    public bool IsEmptied => FileCount == 0 && History is { Removed: > 0 };
}

public static class CardInspector
{
    /// Reads only (a folder listing — nothing is ever written to the card). Runs off the UI thread; `ct` cancels it when
    /// the card is swapped. `history` null = not loaded (or unreadable): no history match.
    public static CardInsight Inspect(string source, IReadOnlyList<HistoryEntry>? history, HistoryStore? store, CancellationToken ct) =>
        Inspect(source, history, store, ct, CardVolume.Of, CardIdentity.Of, Layout.DriveRootOf);

    /// `volumeOf`, `identify`, `driveRootOf`: the disk reads, replaceable in tests.
    internal static CardInsight Inspect(string source, IReadOnlyList<HistoryEntry>? history, HistoryStore? store, CancellationToken ct,
        Func<string, CardVolume?> volumeOf, Func<string, string?> identify, Func<string, string?> driveRootOf)
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
            return new CardInsight
            {
                Source = source,
                Volume = volume,
                Filesystem = filesystem,
                TotalBytes = scan.TotalBytes,
                FileCount = scan.Files.Count,
                Clips = ClipDetector.Detect(scan.Files),
                Fingerprint = fingerprint,
                Shape = shape,
                History = match,
            };
        }
        catch (Exception e) when (e is IOError or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            // Pulled out mid-scan, or a folder that cannot be listed: no insight, never an exception for the caller.
            return new CardInsight { Source = source, Error = Msg.CardUnreadable(e.Message) };
        }
    }
}
