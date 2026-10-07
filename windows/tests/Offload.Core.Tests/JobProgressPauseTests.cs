using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// The card's timers while paused: no rate, no ETA, no brake; after Pokračovat everything is measured from the resume.
public class JobProgressPauseTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    static JobStatus Copy(long read, bool paused = false, bool held = false) => new()
    {
        Phase = JobPhase.Copying, Paused = paused, PauseHeld = held,
        Copy = new ProgressSnapshot(CopyPhase.Copy, 1_000_000_000, 10, 1, "CLIP/A001C002.mov", read, [read, read], [0, 0], 0, paused, held),
    };

    [Fact]
    public void Paused_NoRateNoEta_ResumeMeasuresFromTheResume()
    {
        var p = new JobProgress(T0);
        p.Update(Copy(0), T0.AddSeconds(0.5));
        p.Update(Copy(100_000_000), T0.AddSeconds(1.5));
        Assert.Equal(100e6, p.ReadRate, 1);

        for (int i = 0; i < 60; i++) p.Update(Copy(100_000_000, paused: true, held: true), T0.AddSeconds(2 + i));
        Assert.Equal(0, p.ReadRate);
        Assert.Equal([0.0, 0.0], p.DestRates);
        Assert.Null(p.Eta);
        Assert.Equal("Pozastaveno", p.StatusText);

        p.Update(Copy(150_000_000), T0.AddSeconds(61.5));   // half a second after the resume
        Assert.Equal(100e6, p.ReadRate, 1);                 // not 50 MB over the minute of the pause
        Assert.Equal(850e6 / 100e6, p.Eta!.Value, 3);
        Assert.Equal("Kopírování", p.StatusText);
    }

    [Fact]
    public void PauseTexts_ByPhase()
    {
        var p = new JobProgress(T0);
        p.Update(Copy(1, paused: true), T0);
        Assert.Equal(("Pozastavuje se", "Pozastavuje se…"), (p.StatusText, p.PauseText));
        p.Update(new JobStatus { Phase = JobPhase.Proxies, Paused = true, ClipName = "A001C003", ClipCount = 5, ClipIndex = 2 }, T0);
        Assert.Equal("Pozastaví se po proxy klipu A001C003…", p.PauseText);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, Paused = true, ClipName = "A001C003", ClipCount = 5, ClipIndex = 2 }, T0);
        Assert.Equal("Pozastaví se po klipu A001C003…", p.PauseText);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, Paused = true, PauseHeld = true }, T0);
        Assert.Equal(("Pozastaveno", "Pozastaveno — Pokračovat spustí úlohu od místa, kde stojí."), (p.StatusText, p.PauseText));
        p.Update(new JobStatus { Phase = JobPhase.Analyzing }, T0);
        Assert.Null(p.PauseText);
    }

    // The brake meter must not see the pause: its window is forgotten and it answers again only after half a window.
    [Fact]
    public void Brake_ForgottenWhilePaused()
    {
        var meter = new BrakeMeter(16L << 20, 16);
        for (int i = 0; i <= 4; i++) meter.Add(i, i * 400_000_000L, [i * 400_000_000L]);
        Assert.NotNull(meter.Add(5, 2_000_000_000L, [2_000_000_000L]));
        meter.Reset();
        Assert.Null(meter.Add(65, 2_000_000_000L, [2_000_000_000L]));
        Assert.Null(meter.Add(66, 2_400_000_000L, [2_400_000_000L]));
        Assert.NotNull(meter.Add(67, 2_800_000_000L, [2_800_000_000L]));
    }
}
