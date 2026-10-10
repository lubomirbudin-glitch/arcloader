using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// The card's timers while paused: no rate, no ETA, no brake once the job stands (while it still finishes its file it is
/// measured as ever); after Pokračovat everything is measured from the resume. The texts: "Pozastavuji (dokončuji …)",
/// then "Pozastaveno".
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

    // Task 2026-10-09: Pozastavit lets the file or clip in progress finish — measured as ever meanwhile.
    [Fact]
    public void Pausing_StillFinishingTheFile_RatesGoOn()
    {
        var p = new JobProgress(T0);
        p.Update(Copy(0), T0.AddSeconds(0.5));
        p.Update(Copy(100_000_000), T0.AddSeconds(1.5));
        p.Update(Copy(200_000_000, paused: true) with { PauseStep = "soubor A001C002.mov" }, T0.AddSeconds(2.5));
        Assert.Equal(100e6, p.ReadRate, 1);
        Assert.NotNull(p.Eta);
        Assert.Equal(("Pozastavuji", "Pozastavuji (dokončuji soubor A001C002.mov…)"), (p.StatusText, p.PauseText));
        Assert.All(Offload.Core.Desktop.JobCard.Rows(p, ["D1", "D2"]), r => Assert.NotEqual("—", r.RateText));
    }

    [Fact]
    public void PauseTexts_ByPhase()
    {
        var p = new JobProgress(T0);
        p.Update(Copy(1, paused: true), T0);
        Assert.Equal(("Pozastavuji", "Pozastavuji…"), (p.StatusText, p.PauseText));   // the step unknown (flush, report)
        p.Update(new JobStatus { Phase = JobPhase.Proxies, Paused = true, ClipName = "A001C003", ClipCount = 5, ClipIndex = 2, PauseStep = "proxy klipu A001C003" }, T0);
        Assert.Equal("Pozastavuji (dokončuji proxy klipu A001C003…)", p.PauseText);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, Paused = true, PauseHeld = true }, T0);
        Assert.Equal(("Pozastaveno", "Pozastaveno — Pokračovat naváže dalším klipem."), (p.StatusText, p.PauseText));
        p.Update(new JobStatus { Phase = JobPhase.Verifying, Paused = true, PauseHeld = true }, T0);
        Assert.Equal(("Pozastaveno", "Pozastaveno — Pokračovat naváže dalším souborem."), (p.StatusText, p.PauseText));
        p.Update(new JobStatus { Phase = JobPhase.Analyzing }, T0);
        Assert.Null(p.PauseText);
    }

    // What "Pozastavuji (dokončuji …)" names, by phase.
    [Fact]
    public void Finishing_NamesTheFileOrClipInProgress()
    {
        var copy = Copy(1, paused: true);
        Assert.Equal("soubor A001C002.mov", OffloadJob.Finishing(copy, false, []));
        JobStatus Verify(params string?[] files) => new() { Phase = JobPhase.Verifying, Copy = copy.Copy! with { Phase = CopyPhase.Verify, VerifyingFiles = files } };
        Assert.Equal("soubor A001C002.mov", OffloadJob.Finishing(Verify("CLIP/A001C002.mov", null, "CLIP/A001C002.mov"), false, []));
        Assert.Equal("soubory A001C002.mov, A001.wav", OffloadJob.Finishing(Verify("CLIP/A001C002.mov", "SOUND/A001.wav"), false, []));
        Assert.Null(OffloadJob.Finishing(Verify(null, null), false, []));
        var analyzing = new JobStatus { Phase = JobPhase.Analyzing, ClipName = "A001C003" };
        Assert.Equal("klip A001C003", OffloadJob.Finishing(analyzing, false, ["SOUND/A001.wav"]));
        Assert.Equal("soubor A001.wav", OffloadJob.Finishing(analyzing, true, ["SOUND/A001.wav"]));   // the clip is done: the verification's file
        Assert.Equal("proxy klipu A001C003", OffloadJob.Finishing(analyzing with { Phase = JobPhase.Proxies }, false, []));
        Assert.Null(OffloadJob.Finishing(analyzing with { Phase = JobPhase.Proxies }, true, []));
        Assert.Equal("soubor b.wav", OffloadJob.Finishing(new JobStatus { Phase = JobPhase.Finishing }, true, ["SOUND/b.wav"]));
        Assert.Null(OffloadJob.Finishing(new JobStatus { Phase = JobPhase.Flushing, Copy = copy.Copy }, false, []));
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
