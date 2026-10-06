namespace Offload.Core.Tests;

public class CardRecordTests
{
    [Fact]
    public void Generations_Merged_NewestWins()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var full = Reports.Sample(card);
        full.Write(card, includeHashRecords: false);
        var append = Reports.Sample(card);
        append.Generation = 2;
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), append.ToJson());

        var r = CardRecord.Load(card);

        Assert.Null(r.Error);
        Assert.False(r.ReportMissing);
        Assert.Equal([1, 2], r.Generations.Select(g => g.Generation));
        Assert.Equal(2, r.Files["CLIP/A&B.mov"].Generation);
        Assert.Equal(0, CardRecord.DestinationIndex(full, card, 5));
    }

    [Fact]
    public void Missing_Broken_AndABrokenAppendReport()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Assert.True(CardRecord.Load(card).ReportMissing);
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), "{broken");
        Assert.NotNull(CardRecord.Load(card).Error);

        Reports.Sample(card).Write(card, includeHashRecords: false);
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report_0002.json"), "{broken");
        var r = CardRecord.Load(card);
        Assert.Null(r.Error);
        Assert.Single(r.Generations);
        Assert.StartsWith("offload_report_0002.json: ", Assert.Single(r.Problems));
    }

    [Fact]
    public void DestinationIndex_ByPath_OnlyOne_OrPosition()
    {
        var two = Reports.Sample(@"D:\Zaloha\2026-10-04\A001");
        Assert.Equal(0, CardRecord.DestinationIndex(two, @"d:\zaloha\2026-10-04\A001\", 3));   // the only destination
    }

    // Ruling I1: every "_NNNN" report file counts, also one without its JSON (an append that stopped half way).
    [Fact]
    public void HighestReportNumber_CountsEveryNumberedReportFile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        Assert.Equal(1, CardRecord.Load(card).HighestReportNumber);

        File.WriteAllText(Path.Combine(card, "_REPORT", "A001_report_0003.html"), "<html/>");
        Assert.Equal(3, CardRecord.Load(card).HighestReportNumber);
    }
}
