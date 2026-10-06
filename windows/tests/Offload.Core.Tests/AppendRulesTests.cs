using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class AppendRulesTests
{
    static readonly CardVolume Vol = new("card1", "1A2B3C4D", 256_060_514_304, "A001", "exFAT");

    static CardInsight With(HistoryMatchKind kind, int newFiles = 12, long newBytes = 3_200_000_000, bool reachable = true, CardVolume? volume = null, bool noVolume = false)
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0);
        e.Destinations = [new HistoryDestination { Root = @"D:\Zaloha", CardPath = @"D:\Zaloha\2026-10-06\A001_2" },
                          new HistoryDestination { Root = @"F:\Zaloha", CardPath = @"F:\Zaloha\2026-10-06\A001_2" }];
        var added = Enumerable.Range(0, newFiles).Select(i => new SourceFile($"n{i}", 1, HistoryFixtures.T0)).ToList();
        return new CardInsight
        {
            Source = @"E:\",
            Volume = noVolume ? null : volume ?? Vol,
            AppendReachable = reachable,
            History = new HistoryMatch(e, kind, added, newBytes, [], 0),
        };
    }

    [Fact]
    public void Offered_OnlyForNewFiles()
    {
        Assert.True(AppendRules.Offer(With(HistoryMatchKind.NewFiles)));
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.Identical)));
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.Changed)));
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.NotCompared)));
        Assert.False(AppendRules.Offer(null));
        Assert.False(AppendRules.Offer(new CardInsight { Source = @"E:\", Volume = Vol }));   // no history
    }

    [Fact]
    public void NotOffered_WhenTheEarlierDestinationsAreNotReachable()
    {
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.NewFiles, reachable: false)));
    }

    [Fact]
    public void NotOffered_ForAFolderMatchedOnlyByShape()
    {
        Assert.False(AppendRules.Offer(With(HistoryMatchKind.NewFiles, noVolume: true)));
    }

    [Fact]
    public void NotOffered_ForAnErroredInsight()
    {
        var i = With(HistoryMatchKind.NewFiles) with { Error = "x" };
        Assert.False(AppendRules.Offer(i));
    }

    [Fact]
    public void NotOffered_WithoutDestinations()
    {
        var i = With(HistoryMatchKind.NewFiles);
        i.History!.Entry.Destinations = [];
        Assert.False(AppendRules.Offer(i));
    }

    [Fact]
    public void Texts_OfSpec22()
    {
        var m = With(HistoryMatchKind.NewFiles).History!;
        Assert.Equal("Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)", AppendRules.CheckboxText(m));
        Assert.Equal(@"Složka: 2026-10-06\A001_2 na 2 cílech", AppendRules.FolderText(m.Entry));
        Assert.Equal("A001 · doplnění", AppendRules.Title("A001"));
        Assert.False(AppendRules.NeedsDestinations(0));
        Assert.True(AppendRules.NeedsDestinations(1));
    }

    [Fact]
    public void ReportPath_OfAnAppend()
    {
        Assert.Equal(@"D:\Z\A001\_REPORT\A001_report.html", QueueRules.ReportPath(@"D:\Z\A001", "A001"));
        Assert.Equal(@"D:\Z\A001\_REPORT\A001_report_0002.html", QueueRules.ReportPath(@"D:\Z\A001", "A001", 2));
    }

    // Requirement 1: a card with the append ticked is never asked about ("Karty už jsou zálohované").
    [Fact]
    public void AppendTickedCard_IsLeftOutOfTheBackedUpPrompt()
    {
        var append = With(HistoryMatchKind.NewFiles);
        var other = With(HistoryMatchKind.Identical);
        var insights = new Dictionary<string, CardInsight> { [@"E:\"] = append, [@"G:\"] = other };
        Assert.Equal([@"E:\", @"G:\"], BackedUpPrompt.CardsToAsk(insights.Keys, p => insights[p], _ => false));
        Assert.Equal([@"G:\"], BackedUpPrompt.CardsToAsk(insights.Keys, p => insights[p], p => p == @"E:\"));
    }

    [Fact]
    public void FullCopies_AreTheSelectedCardsWithoutAppend()
    {
        Assert.Equal(1, AppendRules.FullCopies([@"E:\", @"G:\"], p => p == @"E:\"));
        Assert.Equal(0, AppendRules.FullCopies([@"E:\"], _ => true));
    }
}
