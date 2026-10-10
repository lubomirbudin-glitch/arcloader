using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

/// The card tile's line and the start bar's size (UI návrh obr. 1).
public class CardTileTests
{
    static Clip Clip(int i) => new() { Name = $"A035C{i:000}", Path = $"A035C{i:000}.mxf", Segments = [$"A035C{i:000}.mxf"] };

    [Fact]
    public void Facts_FromTheLook_ElseTheVolume()
    {
        var insight = new CardInsight { Source = @"E:\", TotalBytes = 1_700_000_000, FileCount = 30, Clips = Enumerable.Range(1, 24).Select(Clip).ToList() };
        Assert.Equal("24 clips · 1.70 GB · exFAT · reader USB 3 · 5 Gb/s", SetupTexts.CardFacts(insight, 1_800_000_000, "exFAT", "reader USB 3 · 5 Gb/s", true));
        Assert.Equal("1.80 GB · exFAT", SetupTexts.CardFacts(null, 1_800_000_000, "exFAT", null, true));   // not looked at yet
        Assert.Equal("3 files · 850 MB · NTFS · does not look like a card",
            SetupTexts.CardFacts(new CardInsight { Source = @"F:\", TotalBytes = 850_000_000, FileCount = 3 }, null, "NTFS", null, false));
        Assert.Equal("1.80 GB", SetupTexts.CardFacts(new CardInsight { Source = @"E:\", Error = "x" }, 1_800_000_000, null, null, true));
    }

    [Fact]
    public void Total_NullWhileOneIsUnknown()
    {
        Assert.Equal(30, SetupTexts.Total([10, 20]));
        Assert.Null(SetupTexts.Total([10, null]));
        Assert.Equal(0, SetupTexts.Total([]));
    }
}
