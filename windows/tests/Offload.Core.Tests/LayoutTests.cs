namespace Offload.Core.Tests;

public class LayoutTests
{
    [Theory]
    [InlineData("A001", "A001")]
    [InlineData("  CARD/1:2  ", "CARD_1_2")]
    [InlineData("a<b>c|d?e*f\"g\\h", "a_b_c_d_e_f_g_h")]
    [InlineData("..", "CARD")]
    [InlineData("", "CARD")]
    [InlineData("name. ", "name")]
    [InlineData("CON", "_CON")]
    [InlineData("Natáčení den 1", "Natáčení den 1")]
    public void Sanitize(string input, string expected) => Assert.Equal(expected, Layout.Sanitize(input));

    [Theory]
    [InlineData("clip.mov", true)]
    [InlineData("Záběr 01", true)]
    [InlineData("a:b", false)]
    [InlineData("x.", false)]
    [InlineData("x ", false)]
    [InlineData("NUL.txt", false)]
    [InlineData("tab\there", false)]
    public void InvalidNameReason(string name, bool valid) => Assert.Equal(valid, Layout.InvalidNameReason(name) == null);

    [Fact]
    public void Join_ConvertsReportPathToWindowsPath() =>
        Assert.Equal(@"D:\dst\A\B\c.mov", Layout.Join(@"D:\dst", "A/B/c.mov"));

    [Fact]
    public void CardPath_IsRootDateCard() =>
        Assert.Equal(@"D:\dst\2026-10-04\A001", Layout.CardPath(@"D:\dst", "2026-10-04", "A001"));

    [Fact]
    public void UniqueCardFolder_AddsSuffixFreeOnEveryRoot()
    {
        using var dir = new TempDir();
        var r1 = dir.Sub("r1");
        var r2 = dir.Sub("r2");
        Assert.Equal("A001", Layout.UniqueCardFolder([r1, r2], "2026-10-04", "A001"));
        dir.Sub("r1", "2026-10-04", "A001");
        Assert.Equal("A001_2", Layout.UniqueCardFolder([r1, r2], "2026-10-04", "A001"));
        dir.Sub("r2", "2026-10-04", "A001_2");
        Assert.Equal("A001_3", Layout.UniqueCardFolder([r1, r2], "2026-10-04", "A001"));
    }

    [Fact]
    public void DefaultCardName_OfFolderIsFolderName()
    {
        using var dir = new TempDir();
        Assert.Equal("Card X", Layout.DefaultCardName(dir.Sub("Card X")));
    }

    [Fact]
    public void Today_IsIsoDate() => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", Layout.Today());
}
