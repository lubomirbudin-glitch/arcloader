using System.Diagnostics;

namespace Offload.Core.Desktop;

/// What the Start button waits for before it decides (spec 2.5, the Task 7–8 review): the inspections of the selected
/// cards — without them a card history knows would be copied again in full with no "Karty už jsou zálohované" prompt —
/// and the fresh free space of the destinations. Always bounded: a card or a disk that does not answer never blocks the start.
public static class StartWait
{
    /// The selected cards whose look is not there yet: no result, and either history is not loaded or the look still runs.
    public static List<string> PendingInspections(IEnumerable<string> selected, Func<string, bool> hasInsight,
        Func<string, bool> inspectionRunning, bool historyLoaded) =>
        selected.Where(p => !hasInsight(p) && (!historyLoaded || inspectionRunning(p))).ToList();

    public static string InspectionStatus(int cards) =>
        cards == 1 ? "Prověřuji kartu, než začnu kopírovat…" : $"Prověřuji karty ({cards}), než začnu kopírovat…";

    public const string FreeSpaceStatus = "Zjišťuji volné místo na cílech…";

    /// Polls `pending` until it reports 0 (true) or `timeout` passes (false — the caller goes on with what is known).
    /// `progress` gets the pending count each round (the status text). Awaited: the UI thread is never blocked.
    public static async Task<bool> UntilAsync(Func<int> pending, Action<int>? progress, TimeSpan timeout, TimeSpan poll,
        CancellationToken ct = default)
    {
        var clock = Stopwatch.StartNew();
        int n;
        while ((n = pending()) > 0)
        {
            if (clock.Elapsed >= timeout) return false;
            progress?.Invoke(n);
            await Task.Delay(poll, ct);
        }
        return true;
    }

    /// True when every task completed within `timeout`; the tasks themselves are never cancelled (a probe keeps its answer).
    public static async Task<bool> AllWithinAsync(IEnumerable<Task> tasks, TimeSpan timeout)
    {
        var all = Task.WhenAll(tasks);
        using var cts = new CancellationTokenSource();
        var done = await Task.WhenAny(all, Task.Delay(timeout, cts.Token)) == all;
        cts.Cancel();
        if (!done) _ = all.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return done && all.IsCompletedSuccessfully;
    }
}

/// The written bytes of the running jobs at the moment the destinations were last probed (spec 2.5, ruling I4). A job's
/// remaining need is its need minus what was already written when the free space was read — never minus what it has
/// written since, which the stale free figure does not yet show. A job not in the snapshot (started later) counts in full.
public sealed class FreeSpaceBaseline<TJob> where TJob : notnull
{
    IReadOnlyDictionary<TJob, long> written = new Dictionary<TJob, long>();

    /// Called once every destination has answered; `snapshot` was taken before the probes began (so it is never too high).
    public void Commit(IReadOnlyDictionary<TJob, long> snapshot) => written = snapshot;

    public long WrittenAtProbe(TJob job) => written.GetValueOrDefault(job);
}
