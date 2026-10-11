import Foundation

/// "Pause" (Windows `PauseGate`, user requirement 2026-10-09 as Windows 0.8.0): the job's workers finish the step they
/// are on — the file being copied (on every destination together) or read back, the clip being analysed or rendered —
/// and then wait here until Resume or a cancel. Nothing is ever stopped inside a file, a clip, a report or an ffmpeg
/// run (no process is suspended): no `.offload-part` is open and no lock (history, day, ASC MHL) is held by a waiting
/// worker; only an append's folder lock stays held for the whole of stage 1, paused or not.
///
/// A worker that does pausable work joins with `work()` and waits with its `Worker`: the job STANDS (`isHeld`, "Paused")
/// only when it is paused, someone waits here and no joined worker is still busy with its step — until then it is still
/// finishing ("Pausing (finishing …)"). `pausedTotal` counts only the time the job stood: the report subtracts it from
/// its times, so the time a long file still took after Pause stays in the speeds.
public final class PauseGate {
    private let cond = NSCondition()
    private let clock: () -> Date
    private var paused = false
    private var frozen = false
    private var holding = 0
    private var busy = 0
    private var standingSince: Date?
    private var total: TimeInterval = 0
    private var holdCount = 0
    private var requestCount = 0

    /// `clock`: now; tests pass a fake one.
    public init(clock: @escaping () -> Date = Date.init) { self.clock = clock }

    /// Pause was asked for and Resume not yet (the job may still be finishing its step).
    public var isPaused: Bool { locked { paused } }

    /// The job really stands: paused, a worker waits here and no joined worker is busy with its step.
    public var isHeld: Bool { locked { standing } }

    /// How many times a worker has really waited here so far.
    public var holds: Int { locked { holdCount } }

    /// How many times Pause was pressed so far (a press while paused, or after the job ended, not counted). The copy
    /// identifies the card again before the next file after ANY pause — also one resumed while it still finished its
    /// file (Windows review I-1, ruling I1).
    public var requests: Int { locked { requestCount } }

    /// Time the job stood paused so far (a pause standing now counts up to now).
    public var pausedTotal: TimeInterval { locked { total + (standingSince.map { clock().timeIntervalSince($0) } ?? 0) } }

    public func pause() {
        locked {
            guard !frozen, !paused else { return }
            requestCount += 1
            paused = true
            recount()
        }
    }

    public func resume() {
        locked {
            guard paused else { return }
            paused = false
            recount()
            cond.broadcast()
        }
    }

    /// The job has ended: a pause standing now is counted up to now and closed, the gate opens and later presses change
    /// nothing — `pausedTotal` stays what the job really stood.
    public func end() {
        locked {
            frozen = true
            paused = false
            recount()
            cond.broadcast()
        }
    }

    /// A worker that does pausable work joins until it leaves: while it is busy with a step (not waiting here) the job
    /// does not stand. Call `leave()` when its work ends (also on a failure).
    public func work() -> Worker {
        locked {
            busy += 1
            recount()
        }
        return Worker(self)
    }

    /// Blocks while paused (a caller that did not join). False when `isCancelled` says so (checked every 100 ms, also
    /// while waiting): the caller stops as on a cancel.
    public func waitWhilePaused(isCancelled: () -> Bool) -> Bool { wait(nil, isCancelled) }

    private var standing: Bool { paused && holding > 0 && busy == 0 }

    private func wait(_ w: Worker?, _ isCancelled: () -> Bool) -> Bool {
        if isCancelled() { return false }
        cond.lock()
        defer { cond.unlock() }
        guard paused else { return true }
        holding += 1
        holdCount += 1
        let wasBusy = w?.busy == true
        if let w { step(w, busy: false) }
        recount()
        defer {
            holding -= 1
            if let w { step(w, busy: wasBusy) }
            recount()
        }
        while paused {
            cond.unlock()
            let cancelled = isCancelled()
            cond.lock()
            if cancelled { return false }
            if !paused { break }
            _ = cond.wait(until: Date().addingTimeInterval(0.1))
        }
        return true
    }

    /// Under the lock.
    private func step(_ w: Worker, busy now: Bool) {
        guard !w.gone, w.busy != now else { return }
        w.busy = now
        busy += now ? 1 : -1
    }

    /// A worker that only waits for others (stage 2 waiting for the verification's readers) is not busy meanwhile: the job
    /// may stand while they wait at the gate.
    fileprivate func setBusy(_ w: Worker, _ now: Bool) {
        locked {
            step(w, busy: now)
            recount()
        }
    }

    fileprivate func leave(_ w: Worker) {
        locked {
            guard !w.gone else { return }
            if w.busy { busy -= 1 }
            w.busy = false
            w.gone = true
            recount()
        }
    }

    /// Starts or ends the standing time when the job starts or stops standing. Under the lock.
    private func recount() {
        let now = standing
        if now && standingSince == nil {
            standingSince = clock()
        } else if !now, let at = standingSince {
            total += clock().timeIntervalSince(at)
            standingSince = nil
        }
    }

    private func locked<T>(_ f: () -> T) -> T {
        cond.lock()
        defer { cond.unlock() }
        return f()
    }

    /// One worker's membership (`work()`). One thread uses it at a time.
    public final class Worker {
        private let owner: PauseGate
        fileprivate var busy = true
        fileprivate var gone = false
        private let waitingFlag = NSLock()
        private var waitingNow = false

        fileprivate init(_ owner: PauseGate) { self.owner = owner }

        /// This worker waits at the gate now.
        public var isHolding: Bool { waitingFlag.lock(); defer { waitingFlag.unlock() }; return waitingNow }

        /// Between steps: blocks while paused; false when `isCancelled` says so (also while waiting).
        public func waitWhilePaused(isCancelled: () -> Bool) -> Bool {
            setWaiting(true)
            defer { setWaiting(false) }
            return owner.wait(self, isCancelled)
        }

        /// The worker's work has ended: the job may stand without it.
        public func leave() { owner.leave(self) }

        /// Runs `f` with this worker not busy (it only waits for other workers meanwhile), then busy again.
        public func whileIdle<T>(_ f: () throws -> T) rethrows -> T {
            owner.setBusy(self, false)
            defer { owner.setBusy(self, true) }
            return try f()
        }

        private func setWaiting(_ v: Bool) { waitingFlag.lock(); waitingNow = v; waitingFlag.unlock() }
    }
}
