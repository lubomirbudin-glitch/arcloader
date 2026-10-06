using Offload.Core.History;

namespace Offload.Core.Desktop;

/// The life of the card inspections in the Karty list (spec 2.1 "Vlákna"), without a window: one inspection per
/// source at a time, a request while it runs makes it run once more afterwards, a source that disappears or now holds
/// another card (`Identity`, as `Volumes.NamesToDrop` decides) cancels it and its result is thrown away. UI thread only.
public sealed class CardInspections
{
    sealed class Run(string? identity)
    {
        public readonly string? Identity = identity;
        public readonly CancellationTokenSource Cts = new();
        public bool Again;
    }

    readonly Dictionary<string, Run> running = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (string? Identity, CardInsight Insight)> results = new(StringComparer.OrdinalIgnoreCase);

    static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// A look at `path` (holding the card `identity`) is wanted. The token to start it with, or null when one runs
    /// already — that one then runs once more (and is cancelled at once when it looks at another card).
    public CancellationToken? Start(string path, string? identity)
    {
        if (running.TryGetValue(path, out var r))
        {
            r.Again = true;
            if (!Same(r.Identity, identity)) r.Cts.Cancel();
            return null;
        }
        var run = new Run(identity);
        running[path] = run;
        return run.Cts.Token;
    }

    /// The run for `path` ended; `insight` is null when it was cancelled or failed. Its result is kept only while the
    /// path is still listed and holds the card it looked at (`currentIdentity`). True = start once more.
    public bool Finished(string path, CardInsight? insight, string? currentIdentity, bool stillListed)
    {
        if (!running.Remove(path, out var run)) return false;
        run.Cts.Dispose();
        if (!stillListed)
        {
            results.Remove(path);
            return false;
        }
        bool sameCard = Same(run.Identity, currentIdentity);
        if (!sameCard) results.Remove(path);
        else if (insight != null) results[path] = (run.Identity, insight);
        return run.Again || !sameCard;
    }

    /// The source left the list or holds another card now: cancel its run and forget its result.
    public void Drop(string path)
    {
        if (running.TryGetValue(path, out var r)) r.Cts.Cancel();
        results.Remove(path);
    }

    public void CancelAll()
    {
        foreach (var r in running.Values) r.Cts.Cancel();
    }

    public bool IsRunning(string path) => running.ContainsKey(path);

    /// The last result for the card that is at `path` now, or null.
    public CardInsight? Result(string path, string? identity) =>
        results.TryGetValue(path, out var r) && Same(r.Identity, identity) ? r.Insight : null;

    /// The history line of a card row (spec 2.1 table): "Zjišťuji…" until history is loaded and the first look is
    /// done, then the match (nothing for an unknown card or one that could not be read).
    public static (string? Text, HistoryTone Tone) RowText(bool historyLoaded, bool running, CardInsight? insight)
    {
        if (insight == null) return historyLoaded && !running ? (null, HistoryTone.Neutral) : (HistoryText.Inspecting, HistoryTone.Neutral);
        if (insight.Error != null) return (insight.Error, HistoryTone.Error);
        if (insight.IsEmptied) return HistoryText.Emptied(insight.History!);
        return insight.History is { } m ? HistoryText.Line(m) : (null, HistoryTone.Neutral);
    }
}
