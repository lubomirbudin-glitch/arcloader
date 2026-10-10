using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// "odhad ~0:08 vč. proxy" (UI návrh 10): only from what this session measured; any unknown part = no estimate.
public class TimeEstimateTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// 1 GB copied in 10 s (100 MB/s) and read back in 5 s (200 MB/s); one 60 s clip.
    static OffloadReport Report(bool verified = true, long bytes = 1_000_000_000) => new()
    {
        CardName = "A001", Date = "2026-10-07", Source = @"E:\", SourceFilesystem = "exFAT",
        Started = T0, CopyFinished = T0.AddSeconds(10), Finished = T0.AddSeconds(15), Verified = verified, TotalBytes = bytes,
        Destinations = [], Files = [], Skipped = [],
        Clips = [new ClipEntry { Clip = new Clip { Name = "C001", Path = "C001.mov", Segments = ["C001.mov"] }, Info = new ClipInfo { Duration = 60 } }],
    };

    static readonly ProxySettings Proxy = new() { Enabled = true };
    static readonly ProxySettings Off = new();

    /// A clip that the careful rate (MOV 50 Mb/s) makes 120 s long.
    static readonly Clip TwoMinutes = new() { Name = "C002", Path = "C002.mov", Segments = ["C002.mov"], Bytes = 750_000_000 };

    [Fact]
    public void NothingLearned_NoEstimate_SizeOnly()
    {
        var m = new RateMemory();
        Assert.Null(m.Estimate(1_000_000_000, [], verifyOn: false, analyze: false, Off));
        Assert.Null(RateMemory.StartText(null, proxies: false));
    }

    [Fact]
    public void CopyVerifyAndProxies_FromTheLastJob()
    {
        var m = new RateMemory();
        m.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(30));   // 0.5 s of work per second of clip

        Assert.Equal(100e6, m.CopyRate);
        Assert.Equal(200e6, m.VerifyRate);
        // 2 GB: 20 s copy + 10 s read-back + 120 s clip × 0.5
        Assert.Equal(TimeSpan.FromSeconds(90), m.Estimate(2_000_000_000, [TwoMinutes], verifyOn: true, analyze: true, Proxy));
        Assert.Equal(TimeSpan.FromSeconds(20), m.Estimate(2_000_000_000, [], verifyOn: false, analyze: false, Off));
        Assert.Equal("estimate ~1:30 incl. proxies", RateMemory.StartText(TimeSpan.FromSeconds(90), proxies: true));
        Assert.Equal("estimate ~0:20", RateMemory.StartText(TimeSpan.FromSeconds(20), proxies: false));
    }

    // Proxies of another height, or analysis only, cost differently: not known = no estimate.
    [Fact]
    public void OtherStage2Settings_NoEstimate()
    {
        var m = new RateMemory();
        m.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(30));
        Assert.Null(m.Estimate(1_000_000_000, [TwoMinutes], true, true, new ProxySettings { Enabled = true, Height = 720 }));
        Assert.Null(m.Estimate(1_000_000_000, [TwoMinutes], true, true, Off));   // analysis only
        Assert.NotNull(m.Estimate(1_000_000_000, [], true, true, Off));          // no clips: no stage 2 to estimate
    }

    [Fact]
    public void VerifyNotMeasured_OrTinyJob_NoEstimate()
    {
        var m = new RateMemory();
        m.Learn(Report(verified: false), analyze: false, Off, null);
        Assert.Null(m.Estimate(1_000_000_000, [], verifyOn: true, analyze: false, Off));
        Assert.NotNull(m.Estimate(1_000_000_000, [], verifyOn: false, analyze: false, Off));
        var tiny = new RateMemory();
        tiny.Learn(Report(bytes: 50_000_000), analyze: false, Off, null);
        Assert.Null(tiny.CopyRate);
    }

    [Fact]
    public void Remaining_WhileCopyingOrVerifying()
    {
        var m = new RateMemory();
        m.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(30));
        Assert.Equal(2 + 5 + 60, m.Remaining(JobPhase.Copying, 2, 1_000_000_000, [TwoMinutes], true, true, Proxy));
        Assert.Equal(2 + 60, m.Remaining(JobPhase.Verifying, 2, 1_000_000_000, [TwoMinutes], true, true, Proxy));
        Assert.Null(m.Remaining(JobPhase.Proxies, null, 1_000_000_000, [TwoMinutes], true, true, Proxy));
        Assert.Null(new RateMemory().Remaining(JobPhase.Copying, 2, 1_000_000_000, [], true, false, Off));
    }

    [Fact]
    public void Stage2Elapsed_WithoutItsPauses()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Copying, PausedTotal = TimeSpan.FromSeconds(5) }, T0);
        p.Update(new JobStatus { Phase = JobPhase.Analyzing, PausedTotal = TimeSpan.FromSeconds(5) }, T0.AddSeconds(100));
        Assert.Null(p.Stage2Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Done, PausedTotal = TimeSpan.FromSeconds(25) }, T0.AddSeconds(200));
        Assert.Equal(TimeSpan.FromSeconds(80), p.Stage2Elapsed);
    }

    // Ruling M2: a cancelled or failed job, a repair and a stage 2 that did not end Done teach nothing.
    [Fact]
    public void CancelledFailedAndRepairedJobs_TeachNothing()
    {
        var cancelled = new RateMemory();
        cancelled.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(30), endedAs: JobPhase.Cancelled);
        Assert.Null(cancelled.CopyRate);
        Assert.Null(cancelled.Estimate(1_000_000_000, [TwoMinutes], true, true, Proxy));

        var failedStage2 = new RateMemory();
        failedStage2.Learn(Report(), analyze: true, Proxy, stage2: TimeSpan.FromSeconds(3), endedAs: JobPhase.Failed);
        Assert.Equal(100e6, failedStage2.CopyRate);   // the copy itself was fine
        Assert.Null(failedStage2.Estimate(1_000_000_000, [TwoMinutes], true, true, Proxy));   // but stage 2 is not known

        var repaired = new RateMemory();
        var repair = Report();
        repair.Kind = OffloadReport.RepairKind;
        repaired.Learn(repair, analyze: false, Off, null);
        Assert.Null(repaired.CopyRate);
    }

    [Fact]
    public void Stage2Elapsed_NullForACancelledOrFailedJob()
    {
        foreach (var end in new[] { JobPhase.Cancelled, JobPhase.Failed })
        {
            var p = new JobProgress(T0);
            p.Update(new JobStatus { Phase = JobPhase.Proxies }, T0);
            p.Update(new JobStatus { Phase = end }, T0.AddSeconds(100));
            Assert.Null(p.Stage2Elapsed);
        }
    }
}
