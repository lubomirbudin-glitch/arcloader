namespace Offload.Core.Tests;

/// "Pozastavit" (UI návrh, nová věc 9): the gate every worker of a job waits at.
public class PauseGateTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// Task 2026-10-09 (0.8.0): the paused time counts only while the job STANDS — from the moment the last busy worker
    /// reached the gate to Pokračovat. The minutes a long file still took after Pozastavit are work, not pause.
    [Fact]
    public async Task PausedTotal_CountsOnlyWhileTheJobStands_FakeClock()
    {
        var now = T0;
        var gate = new PauseGate(() => now);
        using var copy = gate.Work();
        Assert.False(gate.IsPaused);
        gate.Pause();
        now = now.AddSeconds(5);                      // the copy still finishes its file
        gate.Pause();                                  // a second press changes nothing
        Assert.True(gate.IsPaused);
        Assert.False(gate.IsStanding);
        Assert.Equal(TimeSpan.Zero, gate.PausedTotal);

        var held = TestThreads.Start(() => copy.WaitWhilePaused(CancellationToken.None));
        Assert.True(SpinWait.SpinUntil(() => gate.IsStanding, TimeSpan.FromSeconds(30)));
        now = now.AddSeconds(3);
        Assert.Equal(TimeSpan.FromSeconds(3), gate.PausedTotal);   // standing: counted up to now
        gate.Resume();
        Assert.True(await held.WaitAsync(TimeSpan.FromSeconds(30)));
        now = now.AddSeconds(100);
        gate.Resume();                                 // a resume without a pause changes nothing
        Assert.False(gate.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(3), gate.PausedTotal);
    }

    // Two workers (two destinations read back, or the proxies and a background verification): the job stands only when
    // both reached the gate — one still reading keeps it "Pozastavuji".
    [Fact]
    public async Task TwoWorkers_StandsOnlyWhenBothWait_TimeFromTheSecond()
    {
        var now = T0;
        var gate = new PauseGate(() => now);
        var fast = gate.Work();
        var slow = gate.Work();
        gate.Pause();
        var a = TestThreads.Start(() => fast.WaitWhilePaused(CancellationToken.None));
        Assert.True(SpinWait.SpinUntil(() => gate.IsHolding, TimeSpan.FromSeconds(30)));
        Assert.True(SpinWait.SpinUntil(() => fast.IsHolding, TimeSpan.FromSeconds(30)));
        Assert.False(slow.IsHolding);
        now = now.AddSeconds(60);                      // the slow one finishes its long file
        Assert.False(gate.IsStanding);
        Assert.Equal(TimeSpan.Zero, gate.PausedTotal);

        var b = TestThreads.Start(() => slow.WaitWhilePaused(CancellationToken.None));
        Assert.True(SpinWait.SpinUntil(() => gate.IsStanding, TimeSpan.FromSeconds(30)));
        now = now.AddSeconds(10);
        gate.Resume();
        Assert.True(await a.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(await b.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(10), gate.PausedTotal);
        Assert.Equal(2, gate.Holds);

        // A worker that ends (its destination is done) or waits for the others (Idle) no longer keeps the job going.
        slow.Dispose();
        gate.Pause();
        using (fast.Idle())
        {
            Assert.False(gate.IsStanding);             // nobody waits at the gate: the job is between its steps
            var c = TestThreads.Start(() => gate.WaitWhilePaused(CancellationToken.None));
            Assert.True(SpinWait.SpinUntil(() => gate.IsStanding, TimeSpan.FromSeconds(30)));
            gate.Resume();
            Assert.True(await c.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        fast.Dispose();
        fast.Dispose();                                // twice changes nothing
    }

    // Review I-1: every press of Pozastavit counts (the copy identifies the card again after any of them, also one
    // resumed before the job stood); a second press while paused does not.
    [Fact]
    public void Requests_CountEveryPause_EvenOneNeverStood()
    {
        var gate = new PauseGate();
        Assert.Equal(0, gate.Requests);
        gate.Pause();
        gate.Pause();
        gate.Resume();
        Assert.Equal(1, gate.Requests);
        Assert.Equal(0, gate.Holds);   // nobody waited
        gate.Pause();
        Assert.Equal(2, gate.Requests);
        gate.Freeze();
        gate.Pause();
        Assert.Equal(2, gate.Requests);
    }

    // Review M-5: a worker that waits at the gate while it is idle (waiting for the others) stays idle afterwards — the
    // job may still stand until its Idle scope ends.
    [Fact]
    public async Task WaitInsideIdle_StaysIdleAfterwards()
    {
        var gate = new PauseGate();
        using var worker = gate.Work();
        using (worker.Idle())
        {
            gate.Pause();
            var held = TestThreads.Start(() => worker.WaitWhilePaused(CancellationToken.None));
            Assert.True(SpinWait.SpinUntil(() => gate.IsHolding, TimeSpan.FromSeconds(30)));
            gate.Resume();
            Assert.True(await held.WaitAsync(TimeSpan.FromSeconds(30)));
            gate.Pause();
            var other = TestThreads.Start(() => gate.WaitWhilePaused(CancellationToken.None));
            Assert.True(SpinWait.SpinUntil(() => gate.IsStanding, TimeSpan.FromSeconds(30)));   // still idle: not busy
            gate.Resume();
            Assert.True(await other.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        gate.Pause();
        var third = TestThreads.Start(() => gate.WaitWhilePaused(CancellationToken.None));
        Assert.True(SpinWait.SpinUntil(() => gate.IsHolding, TimeSpan.FromSeconds(30)));
        Assert.False(gate.IsStanding);                 // busy again after the scope
        gate.Resume();
        Assert.True(await third.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    // M1: Freeze ends a standing pause and counts it; later presses change nothing.
    [Fact]
    public async Task Freeze_EndsTheStandingTime()
    {
        var now = T0;
        var gate = new PauseGate(() => now);
        gate.Pause();
        var held = TestThreads.Start(() => gate.WaitWhilePaused(CancellationToken.None));
        Assert.True(SpinWait.SpinUntil(() => gate.IsStanding, TimeSpan.FromSeconds(30)));
        now = now.AddSeconds(4);
        gate.Freeze();
        Assert.True(await held.WaitAsync(TimeSpan.FromSeconds(30)));
        now = now.AddSeconds(50);
        gate.Pause();
        Assert.False(gate.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(4), gate.PausedTotal);
    }

    [Fact]
    public void Open_PassesAtOnce_CancelledNever()
    {
        var gate = new PauseGate();
        Assert.True(gate.WaitWhilePaused(CancellationToken.None));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.False(gate.WaitWhilePaused(cts.Token));
        Assert.False(gate.IsHolding);
        Assert.Equal(0, gate.Holds);                   // nobody waited
    }

    [Fact]
    public async Task Paused_HoldsUntilResume_IsHoldingMeanwhile()
    {
        var gate = new PauseGate();
        gate.Pause();
        var worker = TestThreads.Start(() => gate.WaitWhilePaused(CancellationToken.None));
        Assert.True(SpinWait.SpinUntil(() => gate.IsHolding, TimeSpan.FromSeconds(30)));
        Thread.Sleep(200);
        Assert.False(worker.IsCompleted);
        gate.Resume();
        Assert.True(await worker.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(gate.IsHolding);
        Assert.Equal(1, gate.Holds);                   // one wait that really held (ruling I1 checks the card after it)
    }

    // Cancel while paused (also closing the app, which cancels every job): the worker leaves the gate at once with false.
    [Fact]
    public async Task CancelWhilePaused_ReleasesWithFalse()
    {
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource();
        var worker = TestThreads.Start(() => gate.WaitWhilePaused(cts.Token));
        Assert.True(SpinWait.SpinUntil(() => gate.IsHolding, TimeSpan.FromSeconds(30)));
        cts.Cancel();
        Assert.False(await worker.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(gate.IsPaused);   // the gate itself stays paused; the job ends as cancelled
    }
}
