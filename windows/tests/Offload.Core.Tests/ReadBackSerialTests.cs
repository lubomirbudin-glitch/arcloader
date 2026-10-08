using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// R12 (plan 10): a destination is matched by its volume serial — a disk under another letter never takes another
/// disk's column (the parked plan 9 finding); the day report says a verification with the proxies has not ended.
public class ReadBackSerialTests
{
    // R12: the disk is found by its serial — the parked plan 9 finding (letters swapped → a false green day report).
    [Fact]
    public void ReadBack_BySerial_LettersSwapped_NeverAnotherDisksColumn()
    {
        var t = new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc);
        var report = new OffloadReport
        {
            CardName = "A001", Date = "2026-10-08", Source = @"E:\", SourceFilesystem = "exFAT", Verified = true,
            Destinations =
            [
                new ReportDestination { Root = @"D:\B", CardPath = @"D:\B\2026-10-08\A001", Filesystem = "NTFS", VolumeSerial = "AAAA-0001" },
                new ReportDestination { Root = @"F:\B", CardPath = @"F:\B\2026-10-08\A001", Filesystem = "NTFS", VolumeSerial = "BBBB-0002" },
            ],
            // D:'s copy was never read back (its read-back was cancelled), F:'s was.
            Files = [new ReportFile { Path = "a.mov", Size = 1, Modified = t, Xxh64 = "0123456789abcdef", Destinations = ["written", "verified"], Errors = [null, null] }],
            Skipped = [],
        };
        var rec = new RecordedFile(report.Files[0], 1, report);
        // The disk that was D: is F: now: by its path it would read the other disk's "verified".
        Assert.True(CardRecord.ReadBack(rec, @"F:\B\2026-10-08\A001"));
        Assert.False(CardRecord.ReadBack(rec, @"F:\B\2026-10-08\A001", "AAAA-0001"));
        Assert.True(CardRecord.ReadBack(rec, @"D:\B\2026-10-08\A001", "BBBB-0002"));
        Assert.Equal(-1, CardRecord.ColumnOf(report, @"F:\B\2026-10-08\A001", "CCCC-0003"));   // an unknown disk: no column
    }

    [Fact]
    public void DayReport_PendingCard_SaysVerifying_NotGreen()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 10_000, 1);
        var root = dir.Sub("d1");
        var engine = new OffloadEngine(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, DeferVerify = true });
        engine.Run(engine.Prepare(card, [root], null, "2026-10-08"));

        var day = DayReport.Load(Path.Combine(root, "2026-10-08"));
        Assert.Contains("OVĚŘUJE SE", DayReport.Html(day));
        Assert.Contains(DayReport.Problems(day), p => p.Text.StartsWith("ověření s proxy ještě běží", StringComparison.Ordinal));
        Assert.Equal(0, day.VerifiedCards);
    }
}
