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
}
