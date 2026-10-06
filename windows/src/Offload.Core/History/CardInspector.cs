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
}

public static class CardInspector
{
    /// Reads only (a folder listing — nothing is ever written to the card). Runs off the UI thread; `ct` cancels it when
    /// the card is swapped. `history` null = not loaded (or unreadable): no history match.
    public static CardInsight Inspect(string source, IReadOnlyList<HistoryEntry>? history, HistoryStore? store, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var volume = CardVolume.Of(source);
            var filesystem = volume?.Filesystem ?? NativeIO.FilesystemType(source);
            var scan = Scanner.Scan(source, ct);
            var (fingerprint, shape) = CardFingerprint.Compute(scan);
            ct.ThrowIfCancellationRequested();
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
        catch (IOError e)
        {
            // Pulled out mid-scan, or a folder that cannot be listed: no insight, never an exception for the caller.
            return new CardInsight { Source = source, Error = Msg.CardUnreadable(e.Message) };
        }
    }
}
