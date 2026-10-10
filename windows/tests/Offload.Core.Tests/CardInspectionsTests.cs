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

    // Retry storm: a failed or cancelled run is remembered as an error, so InspectMissing leaves the card alone.
    [Fact]
    public void FailedRun_KeptAsError_NotRetried_SwapStillReinspects()
    {
        var c = new CardInspections();
        c.Start(@"E:\", "card1");
        Assert.False(c.Finished(@"E:\", null, "card1", stillListed: true));
        Assert.Equal(Offload.Core.Msg.CardInspectFailed, c.Result(@"E:\", "card1")!.Error);
        Assert.Null(c.Result(@"E:\", "card2"));   // another card: looked at again
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
        Assert.StartsWith("The card is empty", text);
        Assert.DoesNotContain("differ", text);
        Assert.Equal(HistoryTone.Neutral, tone);
        Assert.StartsWith("Backed up", CardInspections.RowText(true, false, new CardInsight { Source = @"E:\", History = m, FileCount = 3 }).Text);
    }

    [Fact]
    public void RowText_Error_ShowsTheErrorInRed()
    {
        var (text, tone) = CardInspections.RowText(true, false, new CardInsight { Source = @"E:\", Error = "Cannot read the card." });
        Assert.Equal(("Cannot read the card.", HistoryTone.Error), (text, tone));
    }

    // Minor M11: the camera label is set after the look is published, and only while that look is still the card's.
    [Fact]
    public void SetCamera_OnlyOnTheLookItWasProbedFrom()
    {
        var inspections = new CardInspections();
        var camera = new CameraLabel("ALEXA 35", "LogC4");
        inspections.Start(@"E:", "CARD-A");
        var looked = new CardInsight { Source = @"E:" };
        inspections.Finished(@"E:", looked, "CARD-A", stillListed: true);

        Assert.True(inspections.SetCamera(@"E:", looked, camera));
        Assert.Equal(camera, inspections.Result(@"E:", "CARD-A")!.Camera);
        Assert.False(inspections.SetCamera(@"E:", looked, camera));   // the result is a new record now: a late answer is dropped

        // A newer look replaced it: a late probe of the old look does nothing.
        var stale = inspections.Result(@"E:", "CARD-A")!;
        inspections.Start(@"E:", "CARD-A");
        var newer = new CardInsight { Source = @"E:" };
        inspections.Finished(@"E:", newer, "CARD-A", stillListed: true);
        Assert.False(inspections.SetCamera(@"E:", stale, camera));
        Assert.Null(inspections.Result(@"E:", "CARD-A")!.Camera);

        // The source left: nothing is kept; no label: nothing changes.
        inspections.Drop(@"E:");
        Assert.False(inspections.SetCamera(@"E:", newer, camera));
        Assert.False(inspections.SetCamera(@"F:", newer, null));
    }
}
