namespace Offload.Core;

/// "Pozastavit" (UI návrh, nová věc 9): the job's workers stop at their next safe point — before the next 16 MB block
/// of the card or of a read-back, before the next file, before the next clip of stage 2 — and wait until Resume or
/// cancel. Nothing is ever stopped in the middle of a block, a report, a day report or an ffmpeg run, so no lock
/// (history, day, ASC MHL) is held by a waiting worker; only an append's or a repair's folder lock stays held for the
/// whole of stage 1, paused or not. `PausedTotal` (from Pause to Resume) is what the report subtracts from its times.
public sealed class PauseGate
{
    readonly Lock gate = new();
    readonly ManualResetEventSlim open = new(true);
    readonly Func<DateTime> clock;
    DateTime? pausedAt;
    TimeSpan total;
    int holding;
    long holds;
    bool frozen;

    /// `clock`: UTC now; tests pass a fake one.
    public PauseGate(Func<DateTime>? clock = null) => this.clock = clock ?? (() => DateTime.UtcNow);

    /// Pause was asked for and Resume not yet.
    public bool IsPaused => !open.IsSet;

    /// A worker is waiting at the gate now — the job has really stopped (until then the UI says "Pozastavuje se…").
    public bool IsHolding => Volatile.Read(ref holding) > 0;

    /// How many times a worker has really waited at the gate so far. Ruling I1: the copy compares it before every file
    /// and identifies the card again when a pause held it meanwhile.
    public long Holds => Interlocked.Read(ref holds);

    public void Pause()
    {
        lock (gate)
        {
            if (frozen || !open.IsSet) return;
            pausedAt = clock();
            open.Reset();
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (open.IsSet) return;
            total += clock() - pausedAt!.Value;
            pausedAt = null;
            open.Set();
        }
    }

    /// The job has ended (minors M1): a pause still on is counted up to now and closed, the gate opens and later presses
    /// change nothing — PausedTotal stays what the job really stood.
    public void Freeze()
    {
        lock (gate)
        {
            frozen = true;
            if (pausedAt is DateTime at) total += clock() - at;
            pausedAt = null;
            open.Set();
        }
    }

    /// Time between Pause and Resume so far (a pause still on counts up to now).
    public TimeSpan PausedTotal
    {
        get
        {
            lock (gate) return total + (pausedAt is DateTime at ? clock() - at : TimeSpan.Zero);
        }
    }

    /// Blocks while paused. False when `ct` is cancelled (also while waiting): the caller stops as on a cancel.
    public bool WaitWhilePaused(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        if (open.IsSet) return true;
        Interlocked.Increment(ref holding);
        Interlocked.Increment(ref holds);
        try
        {
            open.Wait(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref holding);
        }
    }
}
