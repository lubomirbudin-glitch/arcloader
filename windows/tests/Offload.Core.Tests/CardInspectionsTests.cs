using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class CardInspectionsTests
{
    static CardInsight Insight(HistoryMatch? match = null) => new() { Source = @"E:\", History = match };

    [Fact]
    public void OneAtATime_RequestDuringARun_RunsOnceMore()
    {
        var c = new CardInspections();
        Assert.NotNull(c.Start(@"E:\", "card1"));
        Assert.Null(c.Start(@"e:\", "card1"));   // same path, any case
        Assert.Null(c.Start(@"E:\", "card1"));
        Assert.True(c.Finished(@"E:\", Insight(), "card1", stillListed: true));
        Assert.NotNull(c.Result(@"E:\", "card1"));
        Assert.NotNull(c.Start(@"E:\", "card1"));
        Assert.False(c.Finished(@"E:\", Insight(), "card1", stillListed: true));
    }

    // Review focus 1 and spec 2.1: a card swapped in the same reader mid-inspection — the old result is thrown away.
    [Fact]
    public void SwappedCard_CancelsAndDiscards_ThenLooksAgain()
    {
        var c = new CardInspections();
        var token = c.Start(@"E:\", "card1")!.Value;
        c.Drop(@"E:\");
        Assert.Null(c.Start(@"E:\", "card2"));
        Assert.True(token.IsCancellationRequested);
        Assert.True(c.Finished(@"E:\", Insight(), "card2", stillListed: true));   // a late result of card1
        Assert.Null(c.Result(@"E:\", "card2"));
        Assert.Null(c.Result(@"E:\", "card1"));
    }

    [Fact]
    public void Gone_ResultForgotten_NoRerun()
    {
        var c = new CardInspections();
        c.Start(@"E:\", "card1");
        Assert.True(c.Finished(@"E:\", Insight(), "card1", stillListed: true) is false);
        c.Start(@"E:\", "card1");
        c.Drop(@"E:\");
        Assert.False(c.Finished(@"E:\", null, null, stillListed: false));
        Assert.Null(c.Result(@"E:\", "card1"));
        Assert.False(c.IsRunning(@"E:\"));
    }

    [Fact]
    public void Result_OnlyForTheSameCard()
    {
        var c = new CardInspections();
        c.Start(@"E:\", "card1");
        c.Finished(@"E:\", Insight(), "card1", stillListed: true);
        Assert.Null(c.Result(@"E:\", "card2"));
    }

    [Fact]
    public void RowText_InspectingUntilKnown()
    {
        Assert.Equal((HistoryText.Inspecting, HistoryTone.Neutral), CardInspections.RowText(historyLoaded: false, running: false, null));
        Assert.Equal((HistoryText.Inspecting, HistoryTone.Neutral), CardInspections.RowText(true, true, null));
        Assert.Equal(((string?)null, HistoryTone.Neutral), CardInspections.RowText(true, false, null));
        Assert.Equal(((string?)null, HistoryTone.Neutral), CardInspections.RowText(true, false, Insight()));
        var m = new HistoryMatch(HistoryFixtures.Entry(HistoryFixtures.T0), HistoryMatchKind.Identical, [], 0, [], 0);
        Assert.Equal(HistoryTone.Ok, CardInspections.RowText(true, true, Insight(m)).Tone);   // a rerun keeps the last line
    }

    // Ruling: a card emptied in the camera stays Identical with Removed = all; the row says so instead of "ověřeno".
    [Fact]
    public void RowText_EmptiedCard_SaysItIsEmpty()
    {
        var m = new HistoryMatch(HistoryFixtures.Entry(HistoryFixtures.T0), HistoryMatchKind.Identical, [], 0, [], 5);
        var (text, tone) = CardInspections.RowText(true, false, new CardInsight { Source = @"E:\", History = m, FileCount = 0 });
        Assert.StartsWith("Karta je prázdná", text);
        Assert.DoesNotContain("jinak", text);
        Assert.Equal(HistoryTone.Neutral, tone);
        Assert.StartsWith("Zálohováno", CardInspections.RowText(true, false, new CardInsight { Source = @"E:\", History = m, FileCount = 3 }).Text);
    }

    [Fact]
    public void RowText_Error_ShowsTheErrorInRed()
    {
        var (text, tone) = CardInspections.RowText(true, false, new CardInsight { Source = @"E:\", Error = "Karta se nedá přečíst." });
        Assert.Equal(("Karta se nedá přečíst.", HistoryTone.Error), (text, tone));
    }
}
