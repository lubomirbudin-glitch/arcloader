using Offload.Core.Append;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// Adaptations of plan 9 task 6 to the fixed tree: the eject verdict is the job's own (shared with history), a repair
/// report lists only the repaired files, a waiting card says it stands behind a paused one.
public class JobCardStateTests
{
    static readonly string[] Disks = ["RAID_A", "SHUTTLE_B"];
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    static OffloadReport Report(bool verified = true, string? kind = null, params ReportFile[] files) => new()
    {
        CardName = "A001", Date = "2026-10-07", Source = @"E:\", SourceFilesystem = "exFAT", Started = T0, CopyFinished = T0, Finished = T0,
        Verified = verified, Kind = kind, TotalFiles = files.Length,
        Destinations = [new ReportDestination { Root = @"D:\RAID_A", CardPath = @"D:\RAID_A\x", Filesystem = "NTFS" }, new ReportDestination { Root = @"F:\SHUTTLE_B", CardPath = @"F:\SHUTTLE_B\x", Filesystem = "NTFS" }],
        Files = files, Skipped = [],
    };

    static ReportFile F(string a, string b, string? err = null) => new() { Path = "A.mxf", Size = 1, Destinations = [a, b], Errors = [null, err] };

    [Fact]
    public void GoodReport_ButBackupNotSafe_StaysYellow_WithTheWhy()
    {
        var r = Report(true, null, F("verified", "verified"));
        var notSafe = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = false }.WithWarning("2 soubory z dřívější zálohy nebyly ověřeny.");
        var h = JobCard.Headline("A001", notSafe, r, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — kartu zatím nevysunujte", "2 soubory z dřívější zálohy nebyly ověřeny."), (h.Kind, h.Title, h.Sub));
        h = JobCard.Headline("A001", notSafe, r, Disks, new BackupState(0, 2));
        Assert.Equal("2 soubory dřívější zálohy nebylo ověřeno.", h.Sub);
        h = JobCard.Headline("A001", notSafe, r, Disks, new BackupState(1, 0));
        Assert.Equal("V záloze 1 soubor stále nesouhlasí.", h.Sub);
    }

    [Fact]
    public void Repair_Green_SaysTheWholeBackup_NotTheRepairedFiles()
    {
        var r = Report(true, OffloadReport.RepairKind, F("verified", "verified"));
        var h = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true }, r, Disks, new BackupState(0, 0));
        Assert.Equal((JobCardKind.Done, "Všechny soubory zálohy jsou teď na všech discích a ověřené."), (h.Kind, h.Sub));
        var still = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, CopySucceeded = false }, Report(true, OffloadReport.RepairKind, F("verified", "failed", "boom")), Disks, new BackupState(1, 0));
        Assert.Equal((JobCardKind.Problem, "V záloze 1 soubor stále nesouhlasí."), (still.Kind, still.Sub));
    }

    [Fact]
    public void Waiting_BehindAPausedJob()
    {
        Assert.Equal("Čeká ve frontě", JobCard.Headline("A", new JobStatus(), null, Disks).Sub);
        var h = JobCard.Headline("A", new JobStatus(), null, Disks, blockedByPaused: true);
        Assert.Equal((JobCardKind.Waiting, "čeká na pozastavenou úlohu"), (h.Kind, h.Sub));
    }

    [Fact]
    public void FailedRows_UseTheReportsOwnGoodCopyDefinition()
    {
        // Unverified report: "written" is a good copy; verified report: it is not.
        var unverified = Report(false, null, F("written", "failed", "boom"));
        Assert.Equal(["RAID_A ✓", "SHUTTLE_B ✕ zápis"], JobCard.FailedRows(unverified, Disks)[0].Disks.Select(d => d.Text));
        Assert.True(unverified.IsGood("written"));
        Assert.False(Report(true).IsGood("written"));
    }

    [Fact]
    public void NothingNew_OnAnUnverifiedBackup_IsNotGreen()
    {
        var unsafeDone = new JobStatus { Phase = JobPhase.Done, SafeToEject = false, Note = "Nic nového." }.WithWarning("3 soubory dřívější zálohy nebyly ověřeny.");
        var h = JobCard.Headline("A001", unsafeDone, null, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — kartu zatím nevysunujte", "3 soubory dřívější zálohy nebyly ověřeny."), (h.Kind, h.Title, h.Sub));
        Assert.Equal("2 soubory dřívější zálohy nebylo ověřeno.", JobCard.Headline("A001", unsafeDone, null, Disks, new BackupState(0, 2)).Sub);
        // The really complete backup stays green.
        Assert.Equal((JobCardKind.Done, "A001 — záloha je úplná"),
            (JobCard.Headline("A001", unsafeDone with { SafeToEject = true }, null, Disks).Kind, JobCard.Headline("A001", unsafeDone with { SafeToEject = true }, null, Disks).Title));
    }

    [Fact]
    public void Stage2Failed_AfterAGoodCopy_KeepsTheCopySafeWording_AndSaysTheProcessingFailed()
    {
        var r = Report(true, null, F("verified", "verified"));
        var h = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Failed, CopySucceeded = true, SafeToEject = true, Error = "ffmpeg spadl" }, r, Disks);
        Assert.Equal((JobCardKind.Done, "A001 — kartu lze vysunout"), (h.Kind, h.Title));
        Assert.Contains("Zpracování po kopii ale selhalo: ffmpeg spadl", h.Sub);
        Assert.StartsWith("Soubor je na 2 discích", h.Sub);
    }

    [Fact]
    public void RepairReadBack_UsesTheBytesReallyReadBack_AsTheDenominator()
    {
        // Repair: disk 1 gets one 100 B file written (and read back); disk 2 keeps its copy, nothing to read back there.
        var snap = new ProgressSnapshot(CopyPhase.Verify, 1000, 10, 0, "", 100, [100, 0], [50, 0], 0, PlannedBytes: [100, 0], VerifyPlannedBytes: [100, 0]);
        Assert.Equal(100, snap.ToVerifyTotal);
        Assert.Equal(100, snap.ToVerify(0));
        Assert.Equal(0, snap.ToVerify(1));
        var p = new JobStatus { Phase = JobPhase.Verifying, Copy = snap };
        Assert.Equal("50 B / 100 B", JobCard.FileLine(p).Value.Replace(" ", " "));
        // Before the read-back starts (-1) it falls back to what is written.
        Assert.Equal(100, (snap with { VerifyPlannedBytes = [-1, -1] }).ToVerify(0));
    }
}
