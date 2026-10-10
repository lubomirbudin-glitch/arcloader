namespace Offload.Core;

/// "Pause" (UI návrh 9; task 2026-10-09 for 0.8.0): the job's workers finish the step they are on — the file being
/// copied (on every destination together) or read back, the clip being analysed or rendered — and then wait here until
/// Pokračovat or a cancel. Nothing is ever stopped inside a file, a clip, a report, a day report or an ffmpeg run: no
/// `.offload-part` is open and no lock (history, day, ASC MHL) is held by a waiting worker; only an append's or a
/// repair's folder lock stays held for the whole of stage 1, paused or not.
///
/// A worker that does pausable work joins with Work() and waits with its Worker: the job STANDS ("Pozastaveno") only when
/// it is paused, someone waits here and no joined worker is still busy with its step — until then it is still finishing
/// ("Pausing (finishing …)"). `PausedTotal` counts only the time the job stood: the report subtracts it from its
/// times, so the minutes a long file still took after Pozastavit stay in the speeds.
public sealed class PauseGate
{
    readonly Lock gate = new();
    readonly ManualResetEventSlim open = new(true);
    readonly Func<DateTime> clock;
    DateTime? standingSince;
    TimeSpan total;
    int holding;
    int busy;
    long holds;
    long requests;
    bool frozen;

    /// `clock`: UTC now; tests pass a fake one.
    public PauseGate(Func<DateTime>? clock = null) => this.clock = clock ?? (() => DateTime.UtcNow);

    /// Pause was asked for and Resume not yet (the job may still be finishing its step).
    public bool IsPaused => !open.IsSet;

    /// Some worker waits at the gate now (others may still finish their step — see IsStanding).
    public bool IsHolding
    {
        get { lock (gate) return holding > 0; }
    }

    /// The job really stands: paused, a worker waits at the gate and no joined worker is busy with its step.
    public bool IsStanding
    {
        get { lock (gate) return Standing; }
    }

    bool Standing => !open.IsSet && holding > 0 && busy == 0;

    /// How many times a worker has really waited at the gate so far. Ruling I1: the copy compares it before every file
    /// and identifies the card again when a pause held it meanwhile.
    public long Holds => Interlocked.Read(ref holds);

    /// How many times Pozastavit was pressed so far (a press while paused, or after the job ended, not counted). Review
    /// I-1 (ruling I1): the copy identifies the card again before the next file after ANY pause — also one resumed while
    /// it was still finishing its file, when it never waited here.
    public long Requests => Interlocked.Read(ref requests);

    public void Pause()
    {
        lock (gate)
        {
            if (frozen || !open.IsSet) return;
            Interlocked.Increment(ref requests);
            open.Reset();
            Recount();
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (open.IsSet) return;
            open.Set();
            Recount();
        }
    }

    /// The job has ended (minors M1): a pause still standing is counted up to now and closed, the gate opens and later
    /// presses change nothing — PausedTotal stays what the job really stood.
    public void Freeze()
    {
        lock (gate)
        {
            frozen = true;
            open.Set();
            Recount();
        }
    }

    /// Time the job stood paused so far (a pause standing now counts up to now). The time between Pozastavit and the
    /// moment the step in progress finished is work, not pause.
    public TimeSpan PausedTotal
    {
        get
        {
            lock (gate) return total + (standingSince is DateTime at ? clock() - at : TimeSpan.Zero);
        }
    }

    /// A worker that does pausable work joins until it is disposed: while it is busy with a step (not waiting here) the
    /// job does not stand. Dispose it when its work ends (also on a failure) — or when it is given up on (a hung disk).
    public Worker Work()
    {
        lock (gate)
        {
            busy++;
            Recount();
        }
        return new Worker(this);
    }

    /// Blocks while paused (a worker that did not join). False when `ct` is cancelled (also while waiting): the caller
    /// stops as on a cancel.
    public bool WaitWhilePaused(CancellationToken ct) => Wait(ct, null);

    bool Wait(CancellationToken ct, Worker? worker)
    {
        if (ct.IsCancellationRequested) return false;
        if (open.IsSet) return true;
        bool wasBusy;
        lock (gate)
        {
            holding++;
            Interlocked.Increment(ref holds);
            wasBusy = worker?.Busy == true;
            if (worker != null) Step(worker, busyNow: false);
            Recount();
        }
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
            lock (gate)
            {
                holding--;
                // Back to what it was: a worker that waited here while Idle stays idle (review M-5).
                if (worker != null) Step(worker, busyNow: wasBusy);
                Recount();
            }
        }
    }

    /// A joined worker is busy again, or not (waiting here, idle while others work, or gone). Under the lock.
    void Step(Worker w, bool busyNow)
    {
        if (w.Gone || w.Busy == busyNow) return;
        w.Busy = busyNow;
        busy += busyNow ? 1 : -1;
    }

    void Leave(Worker w)
    {
        lock (gate)
        {
            if (w.Gone) return;
            if (w.Busy) busy--;
            w.Busy = false;
            w.Gone = true;
            Recount();
        }
    }

    void Idle(Worker w, bool idle)
    {
        lock (gate)
        {
            Step(w, busyNow: !idle);
            Recount();
        }
    }

    /// Starts or ends the standing time when the job starts or stops standing. Under the lock.
    void Recount()
    {
        bool standing = Standing;
        if (standing && standingSince == null) standingSince = clock();
        else if (!standing && standingSince is DateTime at)
        {
            total += clock() - at;
            standingSince = null;
        }
    }

    /// One worker's membership (Work). Not thread-safe on its own: one thread uses it at a time.
    public sealed class Worker : IDisposable
    {
        readonly PauseGate owner;
        internal bool Busy = true;
        internal bool Gone;
        volatile bool waiting;

        internal Worker(PauseGate owner) => this.owner = owner;

        /// This worker waits at the gate now.
        public bool IsHolding => waiting;

        /// Between steps: blocks while paused; false when `ct` is cancelled (also while waiting).
        public bool WaitWhilePaused(CancellationToken ct)
        {
            waiting = true;
            try { return owner.Wait(ct, this); }
            finally { waiting = false; }
        }

        /// While the returned scope lasts this worker does no pausable work of its own (it waits for other workers): the
        /// job may stand meanwhile.
        public IDisposable Idle()
        {
            owner.Idle(this, idle: true);
            return new Scope(() => owner.Idle(this, idle: false));
        }

        public void Dispose() => owner.Leave(this);
    }

    sealed class Scope(Action end) : IDisposable
    {
        int done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) end();
        }
    }
}
