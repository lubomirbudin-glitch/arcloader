using Offload.Core.Append;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// R12 on real card folders: CardRecord reads the folder's own volume serial, so "ověřeno" (the one definition — history,
/// BackupState, the day report) never takes another disk's column when the drive letters were swapped; and the day
/// report of a card whose verification with the proxies has not ended is never green, then green once it is written.
public class ReadBackSerialFolderTests
{
    static readonly DateTime T = new(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc);

    // The folder is on our disk (serial S) under path P. The report recorded P for another disk (it was that disk's
    // letter then) whose copy was read back, and our disk under another letter, never read back: by its path the folder
    // would be green — by its serial it is not.
    [Fact]
    public void LettersSwapped_FolderTakesItsOwnDisksColumn_NotGreen()
    {
        using var dir = new TempDir();
        var p = dir.Sub("B", "2026-10-08", "A001");
        var serial = NativeIO.VolumeSerialText(p)!;
        Assert.NotNull(serial);
        var other = "Z:" + p[2..];   // the same folder under another letter
        TestFs.WriteRandom(Path.Combine(p, "a.mov"), 10, 1);
        var report = new OffloadReport
        {
            CardName = "A001", Date = "2026-10-08", Source = @"E:\", SourceFilesystem = "exFAT", Verified = false,   // a read-back cancelled on one disk
            Started = T, CopyFinished = T, Finished = T, TotalFiles = 1, TotalBytes = 10,
            Destinations =
            [
                new ReportDestination { Root = Path.GetDirectoryName(Path.GetDirectoryName(p))!, CardPath = p, Filesystem = "NTFS", VolumeSerial = "0BAD-F00D" },
                new ReportDestination { Root = Path.GetDirectoryName(Path.GetDirectoryName(other))!, CardPath = other, Filesystem = "NTFS", VolumeSerial = serial },
            ],
            Files = [new ReportFile { Path = "a.mov", Size = 10, Modified = T, Xxh64 = "0123456789abcdef", Destinations = ["verified", "written"], Errors = [null, null] }],
            Skipped = [],
        };
        report.Write(p, includeHashRecords: false);

        var record = CardRecord.Load(p);
        Assert.Equal(serial, record.VolumeSerial);
        Assert.False(record.Verified);
        Assert.Equal(new BackupState(0, 1), BackupState.Of([p]));
        Assert.Equal(0, DayReport.Load(Path.GetDirectoryName(p)!).VerifiedCards);
    }

    // A verify-in-proxy job: its day report says "VERIFYING" (and the problem line) while stage 2 runs — never
    // "ověřeno" — and "ověřeno" once the verification results are written.
    [FfmpegFact]
    public void DayReport_WhileVerifying_ThenVerified()
    {
        using var dir = new TempDir();
        var card = VerifyInProxyRulingsTests.Card(dir);
        var root = dir.Sub("d1");
        var config = VerifyInProxyRulingsTests.Config(card, [root], dir);
        config.Outputs = new OutputSettings { DayReport = true, ClipListCsv = false };
        var job = new OffloadJob(config, VerifyInProxyRulingsTests.Options());
        Assert.True(job.RunCopy());

        var during = DayReport.Load(Path.Combine(root, "2026-10-08"));
        Assert.Equal(0, during.VerifiedCards);
        Assert.Contains("VERIFYING", DayReport.Html(during));
        Assert.DoesNotContain("<td class=\"ok\">verified</td>", DayReport.Html(during));
        Assert.Contains(DayReport.Problems(during), x => x.Text.StartsWith("the verification with the proxies is still running", StringComparison.Ordinal));

        job.RunPost();

        var after = DayReport.Load(Path.Combine(root, "2026-10-08"));
        Assert.Equal(1, after.VerifiedCards);
        Assert.DoesNotContain("VERIFYING", DayReport.Html(after));
        Assert.Contains("<td class=\"ok\">verified</td>", DayReport.Html(after));
        Assert.DoesNotContain(DayReport.Problems(after), x => x.Text.StartsWith("the verification with the proxies", StringComparison.Ordinal));
    }
}
