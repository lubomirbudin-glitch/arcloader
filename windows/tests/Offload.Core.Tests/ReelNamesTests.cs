using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

public class ReelNamesTests
{
    [Theory]
    [InlineData("A001C003_210101_R1AB", "A001")]          // ARRI, Sony Venice, Canon Cinema EOS
    [InlineData("B012C0003", "B012")]                     // four-digit clip number
    [InlineData("A001_C003_0101AB_001", "A001")]          // RED
    [InlineData("A001_08251530_C001", "A001")]            // Blackmagic
    [InlineData("a001c003_210101_r1ab", "A001")]          // any case in, upper case out
    [InlineData("A001C003_230101_R1AB", "A001")]          // ARRI Alexa as shot
    [InlineData("A001_C003_0101AB", "A001")]              // RED as shot
    [InlineData("A001C003_220815_CCDE", "A001")]          // Sony Venice
    [InlineData("A001C003_220815_RAB1", "A001")]          // Canon C300/C500
    [InlineData("MVI_0001", null)]                        // Canon DSLR in DCIM/100CANON
    [InlineData("100CANON", null)]                        // a DCIM folder is not a reel
    [InlineData("Clip_01", null)]
    [InlineData("C0001", null)]                           // Sony XAVC
    [InlineData("A01C003", null)]
    [InlineData("AA001C003", null)]
    [InlineData("P1000123", null)]                        // Panasonic
    [InlineData("DJI_0001", null)]
    [InlineData("IMG_1234", null)]
    public void FromClipName(string name, string? reel) => Assert.Equal(reel, ReelNames.FromClipName(name));

    static Clip C(string name, bool cameraProxy = false) =>
        new() { Name = name, Path = name + ".mov", Format = ClipFormat.Video, Segments = [name + ".mov"], Bytes = 1, IsCameraProxy = cameraProxy };

    [Fact]
    public void OneReel_Suggested_WithHint()
    {
        var (s, note, reels) = ReelSuggestion.FromClips([C("A001C001_210101_R1AB"), C("A001C002_210101_R1AB"), C("README")]);
        Assert.Equal(new NameSuggestion("A001", "podle názvů klipů (A001C001…)"), s);
        Assert.Null(note);
        Assert.Equal(["A001"], reels);
    }

    [Fact]
    public void TwoReels_NoSuggestion_ButANote()
    {
        var (s, note, reels) = ReelSuggestion.FromClips([C("A002C001_x"), C("A001C001_x")]);
        Assert.Null(s);
        Assert.Equal("Na kartě je víc cívek: A001, A002", note);
        Assert.Equal(["A001", "A002"], reels);
    }

    [Fact]
    public void CameraProxies_Ignored()
    {
        var (s, _, _) = ReelSuggestion.FromClips([C("A001C001_x"), C("B007C001_x", cameraProxy: true)]);
        Assert.Equal("A001", s!.Name);
    }

    [Fact]
    public void NoReel_Nothing() => Assert.Equal((null, null), (ReelSuggestion.FromClips([C("C0001")]).Suggestion, ReelSuggestion.FromClips([C("C0001")]).Note));

    [Fact]
    public void MixedReels_ManyClips_NoSuggestion()
    {
        var (s, note, reels) = ReelSuggestion.FromClips([C("A001C001_x"), C("A001C002_x"), C("A001C003_x"), C("B001C001_x")]);
        Assert.Null(s);   // spec: several reels = no suggestion (not the majority)
        Assert.Equal("Na kartě je víc cívek: A001, B001", note);
        Assert.Equal(["A001", "B001"], reels);
    }

    [Fact]
    public void DcimStillPhotosAndPhoneClips_Nothing()
    {
        var (s, note, reels) = ReelSuggestion.FromClips([C("IMG_1234"), C("MVI_0002"), C("DJI_0001"), C("P1000123")]);
        Assert.Null(s);
        Assert.Null(note);
        Assert.Empty(reels);
    }

    [Fact]
    public void Blackmagic_And_Red_OneReel_HintShowsPrefix()
    {
        Assert.Equal("podle názvů klipů (A001_08251530_C001…)", ReelSuggestion.FromClips([C("A001_08251530_C001")]).Suggestion!.Hint);
        Assert.Equal("podle názvů klipů (A001_C003…)", ReelSuggestion.FromClips([C("A001_C003_0101AB")]).Suggestion!.Hint);
    }

    [Fact]
    public void ArriRawSequence_ByFolderName()
    {
        using var dir = new TempDir();
        var seq = Path.Combine(dir.Path, "A002C005_230101_R1AB");
        Directory.CreateDirectory(seq);
        for (int i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(seq, $"A002C005_230101_R1AB_{i:000000}.ari"), [1]);
        var clips = ClipDetector.Detect(Scanner.Scan(dir.Path, CancellationToken.None).Files);
        Assert.Equal("A002", ReelSuggestion.FromClips(clips).Suggestion?.Name);
    }

    static HistoryEntry Copied(string cardName, string date, string? project = null)
    {
        var e = HistoryFixtures.Entry(HistoryFixtures.T0, cardName: cardName);
        e.Date = date;
        e.Project = project;
        return e;
    }

    [Fact]
    public void Next_OneCameraLetter()
    {
        HistoryEntry[] h = [Copied("A001", "2026-10-06"), Copied("A002", "2026-10-06"), Copied("A001_2", "2026-10-06"), Copied("A009", "2026-10-05")];
        Assert.Equal(new NameSuggestion("A003", "další v řadě po A002 (dnes)"), ReelSuggestion.Next(h, "2026-10-06", null, today: "2026-10-06"));
        Assert.Equal(new NameSuggestion("A010", "další v řadě po A009 (2026-10-05)"), ReelSuggestion.Next(h, "2026-10-05", null, today: "2026-10-06"));
    }

    [Fact]
    public void Next_TwoLetters_OtherDay_OtherProject_Nothing()
    {
        HistoryEntry[] h = [Copied("A001", "2026-10-06"), Copied("B001", "2026-10-06"), Copied("C005", "2026-10-07", project: "Film X")];
        Assert.Null(ReelSuggestion.Next(h, "2026-10-06", null, "2026-10-06"));
        Assert.Null(ReelSuggestion.Next(h, "2026-10-08", null, "2026-10-06"));
        Assert.Null(ReelSuggestion.Next(h, "2026-10-07", null, "2026-10-06"));
        Assert.Equal("C006", ReelSuggestion.Next(h, "2026-10-07", "Film X", "2026-10-06")!.Name);
    }

    [Fact]
    public void Placeholder_ClipsThenNextThenLabel()
    {
        var withReel = new CardInsight { Source = @"E:\", Name = new NameSuggestion("A001", "podle názvů klipů (A001C001…)") };
        var plain = new CardInsight { Source = @"E:\" };
        HistoryEntry[] h = [Copied("A004", "2026-10-06")];
        Assert.Equal(("A001", "podle názvů klipů (A001C001…)"), CardNaming.Placeholder(true, withReel, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        Assert.Equal(("A005", "další v řadě po A004 (dnes)"), CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        Assert.Equal(("EOS_DIGITAL", (string?)null), CardNaming.Placeholder(false, withReel, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        Assert.Equal(("EOS_DIGITAL", (string?)null), CardNaming.Placeholder(true, null, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
        var known = plain with { History = new HistoryMatch(h[0], HistoryMatchKind.Identical, [], 0, [], 0) };
        Assert.Equal("EOS_DIGITAL", CardNaming.Placeholder(true, known, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL").Name);   // a known card is not "next"
        var twoReels = plain with { NameNote = "Na kartě je víc cívek: A001, A002" };
        Assert.Equal(("EOS_DIGITAL", "Na kartě je víc cívek: A001, A002"), CardNaming.Placeholder(true, twoReels, h, "2026-10-06", "2026-10-06", "EOS_DIGITAL"));
    }

    [Fact]
    public void Setting_DefaultOn_RoundTrip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        Assert.True(SettingsStore.Load(path).Naming.SuggestFromClips);
        File.WriteAllText(path, """{"naming":{"suggestFromClips":false}}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.Naming.SuggestFromClips);
        Assert.Null(s.Extra);   // "naming" is a known key now
        SettingsStore.Save(s, path);
        Assert.Contains("\"suggestFromClips\": false", File.ReadAllText(path));
    }
}
