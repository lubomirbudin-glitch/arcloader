using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class AppendTicksTests
{
    static readonly CardVolume Vol = new("card1", "1A2B3C4D", 1000, "A001", "exFAT");

    static CardInsight Offer(string id = "e1", HistoryMatchKind kind = HistoryMatchKind.NewFiles, string? error = null)
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0);
        e.Id = id;
        return new CardInsight
        {
            Source = @"E:\", Volume = Vol, AppendReachable = true, Error = error,
            History = new HistoryMatch(e, kind, [new SourceFile("n", 1, HistoryFixtures.T0)], 1, [], 0),
        };
    }

    [Fact]
    public void Tick_OnlyWhereOffered()
    {
        var t = new AppendTicks();
        Assert.False(t.Tick("E", Offer(kind: HistoryMatchKind.Changed)));
        Assert.False(t.Wants("E"));
        Assert.True(t.Tick("E", Offer()));
        Assert.True(t.Chosen("E", Offer()));
    }

    [Fact]
    public void Classify_Append_WhenStillOffered()
    {
        var t = new AppendTicks();
        t.Tick("E", Offer());
        Assert.Equal(StartKind.Append, t.Classify("E", Offer()).Kind);
        Assert.Equal(StartKind.Full, t.Classify("G", Offer()).Kind);   // never ticked
    }

    [Theory]
    [InlineData(HistoryMatchKind.Changed)]
    [InlineData(HistoryMatchKind.Identical)]
    public void Classify_TickedButOfferGone_IsNotAFullCopy(HistoryMatchKind now)
    {
        var t = new AppendTicks();
        t.Tick("E", Offer());
        var (kind, name) = t.Classify("E", Offer(kind: now));
        Assert.Equal(StartKind.AppendGone, kind);
        Assert.Equal("A001", name);
        // told once: the next Start sees an ordinary card
        Assert.Equal(StartKind.Full, t.Classify("E", Offer(kind: now)).Kind);
    }

    [Fact]
    public void Classify_ErrorOrNoInsight_IsAppendGone()
    {
        var t = new AppendTicks();
        t.Tick("E", Offer());
        Assert.Equal(StartKind.AppendGone, t.Classify("E", Offer(error: "x")).Kind);
        t.Tick("E", Offer());
        Assert.Equal(StartKind.AppendGone, t.Classify("E", null).Kind);
    }

    [Fact]
    public void Prune_RemovesTheTick_AndItNeverComesBack()
    {
        var t = new AppendTicks();
        t.Tick("E", Offer());
        Assert.Equal(["E"], t.Prune(_ => Offer(kind: HistoryMatchKind.Changed)));
        // the offer returns (the card changed back): the tick does not
        Assert.False(t.Chosen("E", Offer()));
        Assert.Empty(t.Prune(_ => Offer()));
        Assert.False(t.Chosen("E", Offer()));
    }

    [Fact]
    public void Classify_OneCard_DoesNotTouchTheOthers()
    {
        var t = new AppendTicks();
        t.Tick("E", Offer());
        t.Tick("G", Offer());
        t.Classify("E", Offer(kind: HistoryMatchKind.Changed));
        Assert.True(t.Chosen("G", Offer()));
    }

    [Fact]
    public void Tick_NeverPointsAtAnotherHistoryEntry()
    {
        var t = new AppendTicks();
        t.Tick("E", Offer("e1"));
        Assert.False(t.Chosen("E", Offer("e2")));
        Assert.Equal(StartKind.AppendGone, t.Classify("E", Offer("e2")).Kind);
    }

    [Fact]
    public void UntickAndDrop_ForgetEverything()
    {
        var t = new AppendTicks();
        t.Tick("E", Offer());
        t.Untick("E");
        Assert.False(t.Wants("E"));
        t.Tick("E", Offer());
        t.Prune(_ => null);
        Assert.True(t.Wants("E"));   // gone, waiting for Start to say so
        t.Drop("E");
        Assert.False(t.Wants("E"));
    }

    [Fact]
    public void GoneMessage_Czech()
    {
        Assert.Equal("Karta A001 se změnila — doplnit už nejde. Zkontrolujte ji a spusťte znovu.", AppendTicks.GoneMessage(["A001"]));
        Assert.StartsWith("Karty A001, A002 se změnily", AppendTicks.GoneMessage(["A001", "A002"]));
    }
}
