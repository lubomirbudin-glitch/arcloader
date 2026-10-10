using System.Diagnostics;

namespace Offload.Core.Desktop;

/// What the Start button waits for before it decides (spec 2.5, the Task 7–8 review): the inspections of the selected
/// cards — without them a card history knows would be copied again in full with no "Cards already backed up" prompt —
/// and the fresh free space of the destinations. Always bounded: a card or a disk that does not answer never blocks the start.
public static class StartWait
{
    /// The selected cards whose look is not there yet: no result, and either history is not loaded or the look still runs.
    public static List<string> PendingInspections(IEnumerable<string> selected, Func<string, bool> hasInsight,
        Func<string, bool> inspectionRunning, bool historyLoaded) =>
        selected.Where(p => !hasInsight(p) && (!historyLoaded || inspectionRunning(p))).ToList();

    public static string InspectionStatus(int cards) =>
        cards == 1 ? "Checking the card before the copy starts…" : $"Checking the cards ({cards}) before the copy starts…";

    /// Ruling S1: the inspection wait ran out. The question about the cards still being looked at (default No).
    public static string UnansweredQuestion(IReadOnlyList<string> cardNames) =>
        cardNames.Count == 1
            ? $"Card {cardNames[0]} is still being checked — it is not known yet whether it is backed up. Start a full copy anyway?"
            : $"Cards {string.Join(", ", cardNames)} are still being checked — it is not known yet whether they are backed up. Start a full copy anyway?";

    /// Whether Start goes on after the inspection wait: always when it finished; otherwise only when the user says Yes
    /// to the question about the cards still pending. `ask` returns true for Yes.
    public static bool ContinueAfterWait(bool looked, IReadOnlyList<string> pendingNames, Func<string, bool> ask) =>
        looked || pendingNames.Count == 0 || ask(UnansweredQuestion(pendingNames));

    public const string FreeSpaceStatus = "Checking free space on the destinations…";

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
    public static async Task<bool> AllWithinAsync(IEnumerable<Task> tasks, TimeSpan timeout, CancellationToken ct = default)
    {
        var all = Task.WhenAll(tasks);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var done = await Task.WhenAny(all, Task.Delay(timeout, cts.Token)) == all;
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
        if (!done) _ = all.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return done && all.IsCompletedSuccessfully;
    }
}

/// The periodic health check re-reads the free space only while a space problem is shown (a fixed disk is otherwise
/// probed once, so freeing space would never clear the red text) and never while an earlier read is still running.
public sealed class FreeSpaceReprobe
{
    int running;

    /// True = the caller starts the probe now and calls End() when it is over.
    public bool TryBegin(bool spaceProblemShown) => spaceProblemShown && Interlocked.CompareExchange(ref running, 1, 0) == 0;

    public void End() => Volatile.Write(ref running, 0);
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
