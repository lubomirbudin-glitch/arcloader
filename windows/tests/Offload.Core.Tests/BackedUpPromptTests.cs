using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class BackedUpPromptTests
{
    static readonly DateTime Finished = new DateTime(2026, 10, 6, 8, 15, 0, DateTimeKind.Local).ToUniversalTime();

    static HistoryMatch Match(string card, HistoryMatchKind kind = HistoryMatchKind.Identical, int newFiles = 0, bool verified = true)
    {
        var e = HistoryFixtures.Entry(Finished, cardName: card, verified: verified);
        e.Date = "2026-10-06";
        return new HistoryMatch(e, kind, Enumerable.Range(0, newFiles).Select(i => new SourceFile($"n{i}", 1, Finished)).ToList(), newFiles, [], 0);
    }

    [Fact]
    public void TwoCards_SpecText()
    {
        var model = BackedUpPrompt.Build([(Match("A001"), "A001"), (Match("A002", HistoryMatchKind.NewFiles, 12), "A002")], "2026-10-06");
        Assert.Equal("Karty už jsou zálohované", model.Title);
        Assert.Equal(
            ["A001 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha (F:), ověřeno", "A002 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha (F:), ověřeno · nové soubory: 12"],
            model.Lines);
        Assert.Equal("Zkopírovat je znovu celé? Vzniknou nové složky (např. A001_2, A002_2).", model.Question);
    }

    [Fact]
    public void OneCard_Singular_OtherDay_NewDateFolder()
    {
        var model = BackedUpPrompt.Build([(Match("A001", verified: false), "A001")], "2026-10-07");
        Assert.Equal("Karta už je zálohovaná", model.Title);
        Assert.Equal(["A001 — 6. 10. 2026 8:15, Zaloha (D:), Zaloha (F:), NEOVĚŘENO"], model.Lines);
        Assert.Equal(@"Zkopírovat ji znovu celou? Vznikne nová složka (např. 2026-10-07\A001).", model.Question);
    }

    [Fact]
    public void Asked_OnlyForKnownCards_NotForChangedOnes()
    {
        Assert.False(BackedUpPrompt.ShouldAsk(null));
        Assert.False(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\" }));
        Assert.True(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\", History = Match("A001") }));
        Assert.True(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\", History = Match("A001", HistoryMatchKind.NotCompared) }));
        Assert.False(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\", History = Match("A001", HistoryMatchKind.Changed) }));
        // An emptied card (Identical, everything removed, no files): nothing to copy, nothing to ask.
        var emptied = Match("A001") with { Removed = 4 };
        Assert.False(BackedUpPrompt.ShouldAsk(new CardInsight { Source = @"E:\", History = emptied, FileCount = 0 }));
    }
}
