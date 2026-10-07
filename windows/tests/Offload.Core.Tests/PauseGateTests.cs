namespace Offload.Core.Tests;

/// "Pozastavit" (UI návrh, nová věc 9): the gate every worker of a job waits at.
public class PauseGateTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PausedTotal_CountsFromPauseToResume_FakeClock()
    {
        var now = T0;
        var gate = new PauseGate(() => now);
        Assert.False(gate.IsPaused);
        gate.Pause();
        now = now.AddSeconds(5);
        gate.Pause();                                  // a second press changes nothing
        Assert.True(gate.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(5), gate.PausedTotal);   // still paused: counted up to now
        now = now.AddSeconds(3);
        gate.Resume();
        now = now.AddSeconds(100);
        gate.Resume();                                 // a resume without a pause changes nothing
        Assert.False(gate.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(8), gate.PausedTotal);
        gate.Pause();
        now = now.AddSeconds(2);
        gate.Resume();
        Assert.Equal(TimeSpan.FromSeconds(10), gate.PausedTotal);
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
