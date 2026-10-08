using Offload.Core.Desktop;

namespace Offload.Core.Tests;

// Final review of plan 4: decisions the GUI takes, kept here so they are tested without a window.
public class QueueRulesTests
{
    // ── item 4: eject only the card that was copied ─────────────────────────

    [Fact]
    public void EjectProblem_SameCard_NotInUse_Allowed()
    {
        Assert.Null(QueueRules.EjectProblem(copied: "V1|AB12", current: "v1|ab12", cardInUse: false));
    }

    [Fact]
    public void EjectProblem_OtherCardOrGone_Refused()
    {
        Assert.Equal(QueueRules.OtherCardText, QueueRules.EjectProblem("V1|AB12", "V1|CD34", false));
        Assert.Equal(QueueRules.OtherCardText, QueueRules.EjectProblem(null, "V1|CD34", false));
        Assert.Equal("karta není připojená", QueueRules.EjectProblem("V1|AB12", null, false));
        Assert.Equal("karta v mechanice není ta, která se kopírovala", QueueRules.OtherCardText);
    }

    [Fact]
    public void EjectProblem_CardStillNeededByAnotherJob_Refused()
    {
        Assert.Equal(QueueRules.InUseText, QueueRules.EjectProblem("V1|AB12", "V1|AB12", cardInUse: true));
    }

    [Theory]
    [InlineData(JobPhase.Waiting, true)]
    [InlineData(JobPhase.Copying, true)]
    [InlineData(JobPhase.Flushing, true)]
    [InlineData(JobPhase.Verifying, true)]
    [InlineData(JobPhase.Reporting, true)]
    [InlineData(JobPhase.Analyzing, false)]   // stage 2 reads only the destinations
    [InlineData(JobPhase.Proxies, false)]
    [InlineData(JobPhase.Done, false)]
    [InlineData(JobPhase.Failed, false)]
    [InlineData(JobPhase.Cancelled, false)]
    public void CardInUse_ByPhaseOfAnotherJobOnTheSameDrive(JobPhase phase, bool inUse)
    {
        Assert.Equal(inUse, QueueRules.CardInUse(@"E:\", [(@"E:\DCIM", phase)]));
        Assert.Equal(inUse, QueueRules.CardInUse(@"e:\", [(@"E:\", phase)]));
        Assert.False(QueueRules.CardInUse(@"E:\", [(@"F:\", phase)]));
    }

    [Fact]
    public void CardInDrive_OnlyWhenTheScanShowsTheCopiedCard()
    {
        List<VolumeItem> scan = [new("A (E:)", @"E:\", 10, 5, "exFAT", true, false, "V1|AB12")];
        Assert.True(QueueRules.CardInDrive(@"E:\", "V1|AB12", scan));
        Assert.False(QueueRules.CardInDrive(@"E:\", "V1|CD34", scan));   // another card in the slot
        Assert.False(QueueRules.CardInDrive(@"E:\", null, scan));        // not known which card was copied
        Assert.False(QueueRules.CardInDrive(@"F:\", "V1|AB12", scan));   // pulled out (the letter is gone)
        Assert.False(QueueRules.CardInDrive(@"E:\", "V1|AB12", [new("A (E:)", @"E:\", 10, 5, "exFAT", true, false)]));
    }

    [Fact]
    public void CardIdentity_SameVolume_Same_Missing_Null()
    {
        using var dir = new TempDir();
        var a = CardIdentity.Of(dir.Path);
        Assert.NotNull(a);
        Assert.Equal(a, CardIdentity.Of(dir.Sub("x")));
        Assert.Contains("|", a);
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = "QRSTUVWXYZ".First(c => !used.Contains(c));
        Assert.Null(CardIdentity.Of($@"{free}:\"));
    }

    [Fact]
    public void Runner_RecordsTheCardOnTheCopyThread()
    {
        using var dir = new TempDir();
        var seen = new List<string>();
        var job = new OffloadJob(new JobConfig(dir.Sub("card"), [dir.Sub("d1")])) { Identify = src => { seen.Add(src); return "ID-" + src.Length; } };
        var runner = new JobQueue.Runner(job, null);
        Assert.Null(runner.SourceIdentity);
        job.Cancel();
        runner.RunCopy();
        Assert.Equal([dir.Sub("card")], seen);
        Assert.Equal("ID-" + dir.Sub("card").Length, runner.SourceIdentity);
    }

    // ── item 9: no measurement while a copy writes ──────────────────────────

    [Fact]
    public void MeasureBlocked_WhileACopyWritesOrVerifies()
    {
        Assert.True(QueueRules.MeasureBlocked([JobPhase.Done, JobPhase.Copying]));
        Assert.True(QueueRules.MeasureBlocked([JobPhase.Flushing]));
        Assert.True(QueueRules.MeasureBlocked([JobPhase.Verifying]));
        Assert.False(QueueRules.MeasureBlocked([JobPhase.Waiting, JobPhase.Proxies, JobPhase.Done, JobPhase.Cancelled]));
        Assert.False(QueueRules.MeasureBlocked([]));
    }

    // Fix round 1, item 3: a finished job whose day report the post thread still makes (ruling I2) keeps the app — and
    // stays in the list (Vymazat dokončené) — until the queue reports it finished; an eject in flight too.
    [Theory]
    [InlineData(JobPhase.Copying, false, false, false, true)]
    [InlineData(JobPhase.Done, false, false, false, false)]
    [InlineData(JobPhase.Done, true, false, false, true)]
    [InlineData(JobPhase.Done, false, true, false, true)]
    [InlineData(JobPhase.Cancelled, false, true, false, true)]
    [InlineData(JobPhase.Failed, false, true, true, false)]
    public void StillNeeded_UntilTheDayReportIsMade(JobPhase phase, bool ejecting, bool dayReportPending, bool leftQueue, bool needed) =>
        Assert.Equal(needed, QueueRules.StillNeeded(phase, ejecting, dayReportPending, leftQueue));

    // Final fix 6: a job that ended in stage 1 alerts when the copy ends, not after its day report queued behind other cards.
    [Theory]
    [InlineData(JobPhase.Done, true)]
    [InlineData(JobPhase.Failed, true)]
    [InlineData(JobPhase.Cancelled, true)]
    [InlineData(JobPhase.Proxies, false)]
    [InlineData(JobPhase.Reporting, false)]
    [InlineData(JobPhase.Copying, false)]
    public void AlertAtCopyEnd_OnlyWhenNoStage2Follows(JobPhase phase, bool alert) =>
        Assert.Equal(alert, QueueRules.AlertAtCopyEnd(phase));

    // ── item 11: the report from the first destination that has it ──────────

    [Fact]
    public void Report_FirstExistingDestinationWins()
    {
        using var dir = new TempDir();
        var cards = new[] { dir.Sub("d1", "2026-10-05", "A001"), dir.Sub("d2", "2026-10-05", "A001"), dir.Sub("d3", "2026-10-05", "A001") };
        Assert.Null(QueueRules.FirstReport(cards, "A001"));
        foreach (var c in cards.Skip(1))
        {
            Directory.CreateDirectory(Path.Combine(c, Layout.ReportDirName));
            File.WriteAllText(QueueRules.ReportPath(c, "A001"), "<html/>");
        }
        Assert.Equal(QueueRules.ReportPath(cards[1], "A001"), QueueRules.FirstReport(cards, "A001"));
        Assert.EndsWith(Path.Combine(Layout.ReportDirName, "A001_report.html"), QueueRules.ReportPath(cards[0], "A001"));
    }

    // ── item 15: an ejected card is not touched again until it arrives anew ──

    [Fact]
    public void RootsFromUnitMask()
    {
        Assert.Equal([@"A:\", @"C:\"], Volumes.RootsFromUnitMask(0b101));
        Assert.Equal([@"E:\"], Volumes.RootsFromUnitMask(1u << 4));
        Assert.Equal([@"Z:\"], Volumes.RootsFromUnitMask(1u << 25));
        Assert.Empty(Volumes.RootsFromUnitMask(0));
        Assert.Empty(Volumes.RootsFromUnitMask(1u << 26));   // no letter beyond Z
    }

    [Fact]
    public void Scan_SkipsEjectedRoots()
    {
        var all = DriveInfo.GetDrives().Select(d => d.Name).ToList();
        Assert.Empty(Volumes.Scan([], skip: all.Select(r => r.ToLowerInvariant()).ToList()));
    }

    // ── item 5: windows fit a small laptop screen ───────────────────────────

    [Fact]
    public void WindowFit_TallerThanWorkArea_ClampedAndCentred()
    {
        var p = WindowFit.Fit(0, 0, 1366, 728, 1240, 860, 940, 640);
        Assert.False(p.Maximize);
        Assert.Equal(1240, p.Width);
        Assert.Equal(728, p.Height);
        Assert.Equal(63, p.Left);
        Assert.Equal(0, p.Top);
    }

    [Fact]
    public void WindowFit_FitsAlready_KeepsSizeAndCentres()
    {
        var p = WindowFit.Fit(0, 0, 1920, 1040, 1240, 860, 940, 640);
        Assert.Equal((false, 1240.0, 860.0, 340.0, 90.0), (p.Maximize, p.Width, p.Height, p.Left, p.Top));
    }

    [Fact]
    public void WindowFit_CentreOnOwner_StaysInsideTheWorkArea()
    {
        // Owner centred near the bottom right: the window is pushed back inside.
        var p = WindowFit.Fit(0, 0, 1366, 728, 880, 760, 640, 480, centerX: 1200, centerY: 600);
        Assert.Equal(728, p.Height);
        Assert.Equal(1366 - 880, p.Left);
        Assert.Equal(0, p.Top);
        // A second monitor's work area with an offset.
        var q = WindowFit.Fit(1920, 0, 1280, 680, 880, 760, 640, 480);
        Assert.Equal((1920 + 200.0, 0.0, 680.0), (q.Left, q.Top, q.Height));
    }

    [Fact]
    public void WindowFit_MinimumDoesNotFit_Maximize()
    {
        Assert.True(WindowFit.Fit(0, 0, 900, 600, 1240, 860, 940, 640).Maximize);
        Assert.True(WindowFit.Fit(0, 0, 1366, 600, 1240, 860, 940, 640).Maximize);
    }

    // ── follow-ups: start during a speed measurement, monitors, destinations ──

    [Fact]
    public void Start_BlockedWhileMeasuring()
    {
        Assert.Equal("Probíhá měření rychlosti — počkejte nebo ho zastavte", QueueRules.StartProblem(measuring: true));
        Assert.Null(QueueRules.StartProblem(measuring: false));
    }

    [Fact]
    public void WorkArea_FromDevicePixels()
    {
        // A second monitor right of a 1920 px one, 200 % scaling: device pixels / 2 = WPF units.
        Assert.Equal((960, 0, 960, 520), WindowFit.FromDevice(1920, 0, 3840, 1040, 0.5, 0.5));
        Assert.Equal((-1920, 0, 1920, 1040), WindowFit.FromDevice(-1920, 0, 0, 1040, 1, 1));
    }

    [Theory]
    [InlineData(@"\\nas\share", null, true)]
    [InlineData(@"Z:\", System.IO.DriveType.Network, true)]
    [InlineData(@"F:\", System.IO.DriveType.Removable, true)]
    [InlineData(@"D:\", System.IO.DriveType.Fixed, false)]
    [InlineData(@"D:\Footage", System.IO.DriveType.Fixed, false)]
    [InlineData(@"Q:\", null, false)]
    public void Destination_MayDropOut_NetworkOrRemovable(string root, System.IO.DriveType? type, bool expected) =>
        Assert.Equal(expected, DestinationHealth.MayDropOut(root, type));

    [Fact]
    public void Destination_Reprobe_MissingAlways_PresentOnlyWhenItMayDropOut()
    {
        Assert.False(DestinationHealth.ShouldReprobe(checking: true, exists: false, mayDropOut: true));   // first probe still running
        Assert.True(DestinationHealth.ShouldReprobe(checking: false, exists: false, mayDropOut: false));
        Assert.True(DestinationHealth.ShouldReprobe(checking: false, exists: true, mayDropOut: true));   // a NAS that may go away
        Assert.False(DestinationHealth.ShouldReprobe(checking: false, exists: true, mayDropOut: false));  // an internal disk
    }

    // The end-of-job notification of a job that ended in stage 1 comes at the copy end, once — not after the day report.
    [Theory]
    [InlineData(JobPhase.Failed, false, true)]
    [InlineData(JobPhase.Done, false, true)]
    [InlineData(JobPhase.Cancelled, false, true)]
    [InlineData(JobPhase.Proxies, false, false)]   // stage 2 still runs: the end is JobFinished
    [InlineData(JobPhase.Failed, true, false)]
    public void Notify_AtCopyEnd_OnlyForJobsThatEndedThere_Once(JobPhase phase, bool notified, bool expected) =>
        Assert.Equal(expected, QueueRules.NotifyAtCopyEnd(phase, notified));

    [Fact]
    public void Notify_AtJobEnd_OnlyWhenNotNotifiedYet()
    {
        Assert.True(QueueRules.NotifyAtJobEnd(notified: false));
        Assert.False(QueueRules.NotifyAtJobEnd(notified: true));
    }

    // Tasks 1–3 review, minor 3: a job paused in stage 1 holds the waiting cards; paused in stage 2 it does not.
    [Fact]
    public void BlockedByPaused_OnlyAPausedStage1()
    {
        Assert.True(QueueRules.BlockedByPaused(JobPhase.Waiting, [(JobPhase.Copying, true)]));
        Assert.True(QueueRules.BlockedByPaused(JobPhase.Waiting, [(JobPhase.Done, false), (JobPhase.Verifying, true)]));
        Assert.False(QueueRules.BlockedByPaused(JobPhase.Waiting, [(JobPhase.Copying, false)]));
        Assert.False(QueueRules.BlockedByPaused(JobPhase.Waiting, [(JobPhase.Proxies, true)]));
        Assert.False(QueueRules.BlockedByPaused(JobPhase.Copying, [(JobPhase.Verifying, true)]));
    }
}
