namespace Offload.Core.Tests;

/// The pause in the report: `copyPausedSeconds` / `verifyPausedSeconds` (absent when never paused), speeds without it.
public class ReportPauseTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    static OffloadPlan Plan() => new()
    {
        Source = @"E:\", Date = "2026-10-07", CardName = "A001", DestRoots = [@"D:\dst"],
        Scan = new SourceScan { Root = @"E:\", Files = [new SourceFile("CLIP/A001C001.mov", 1_000_000_000, T0)], Dirs = ["CLIP"], Skipped = [] },
    };

    static OffloadReport Build(TimeSpan copyPaused, TimeSpan verifyPaused) =>
        OffloadReport.Build(Plan(), [new FileHashes(1UL, null)], [null], [[new DestFileResult(FileState.Verified, null)]], [true],
            verified: true, T0, T0.AddSeconds(40), T0.AddSeconds(50), copyPaused, verifyPaused);

    [Fact]
    public void Paused_KeysRoundTrip_SpeedsWithoutThePause_TxtAndHtmlSayIt()
    {
        var r = Build(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(4.04));

        Assert.Equal(30, r.CopyPausedSeconds);
        Assert.Equal(4.0, r.VerifyPausedSeconds);
        Assert.Equal(10, r.CopySeconds, 3);
        Assert.Equal(6, r.VerifySeconds, 3);
        Assert.Equal(34, r.PausedSeconds, 3);
        var again = OffloadReport.FromJson(r.ToJson());
        Assert.Equal((30.0, 4.0), (again.CopyPausedSeconds!.Value, again.VerifyPausedSeconds!.Value));
        Assert.Contains("Copy:        10.0 s  100 MB/s\n", r.Summary);
        Assert.Contains("Paused:      34.0 s (not in the speeds)\n", r.Summary);
        Assert.Contains("pozastaveno · mimo rychlosti", HtmlReport.Render(r));
    }

    [Fact]
    public void NeverPaused_NoKeys_NoLine()
    {
        var r = Build(TimeSpan.Zero, TimeSpan.Zero);

        Assert.DoesNotContain("PausedSeconds", r.ToJson());
        Assert.DoesNotContain("Paused:", r.Summary);
        Assert.DoesNotContain("pozastaveno", HtmlReport.Render(r));
        Assert.Equal(40, r.CopySeconds, 3);
    }

    // Ruling I1: the file error of a card swapped while paused stays English in the JSON, Czech in the HTML.
    [Fact]
    public void CardChangedWhilePaused_CzechInHtml()
    {
        Assert.Equal("karta se během pozastavení vyměnila nebo vytáhla", HtmlReport.ErrorText(OffloadEngine.CardChangedWhilePaused));
        Assert.Equal("card changed while paused", OffloadEngine.CardChangedWhilePaused);
    }
}
