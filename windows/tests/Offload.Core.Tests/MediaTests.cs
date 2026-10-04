using System.Text.Json;

namespace Offload.Core.Tests;

public class MediaTests
{
    [Fact]
    public void FrameCrop_CentersEvenSizedFrame()
    {
        var s = new ProxySettings { FrameAspect = 1.66, FrameScale = 0.9 };
        Assert.Equal((306, 108, 3226, 1944), s.FrameCrop(3840, 2160)!.Value);
    }

    [Fact]
    public void FrameCrop_NullWithoutAspect() => Assert.Null(new ProxySettings().FrameCrop(3840, 2160));

    [Fact]
    public void FrameLabel_ShowsAspectAndScale()
    {
        Assert.Equal("1.66 · 90 %", new ProxySettings { FrameAspect = 1.66, FrameScale = 0.9 }.FrameLabel);
        Assert.Equal("2.39", new ProxySettings { FrameAspect = 2.39 }.FrameLabel);
        Assert.Null(new ProxySettings().FrameLabel);
    }

    [Theory]
    [InlineData("24000/1001", "23.976")]
    [InlineData("25/1", "25")]
    [InlineData("50", "50")]
    [InlineData(null, "")]
    public void FpsLabel(string? rate, string expected) => Assert.Equal(expected, new ClipInfo { FrameRate = rate }.FpsLabel);

    [Fact]
    public void DurationAndResolutionLabels()
    {
        var i = new ClipInfo { Duration = 3725.5, Width = 3840, Height = 2160 };
        Assert.Equal("1:02:06", i.DurationLabel);
        Assert.Equal("3840×2160", i.ResolutionLabel);
        Assert.Equal("", new ClipInfo().DurationLabel);
    }

    [Fact]
    public void ProxySettings_MissingKeysKeepDefaults()
    {
        var s = JsonSerializer.Deserialize<ProxySettings>("""{"enabled":true,"codec":"proresLT"}""")!;
        Assert.True(s.Enabled);
        Assert.Equal(ProxyCodec.ProresLT, s.Codec);
        Assert.Equal(1080, s.Height);
        Assert.True(s.BurnFilename);
        Assert.Equal(1.0, s.FrameScale);
    }

    [Fact]
    public void Enums_UseMacWireNames()
    {
        Assert.Equal("\"canonRaw\"", JsonSerializer.Serialize(ClipFormat.CanonRaw));
        Assert.Equal("\"unsupported\"", JsonSerializer.Serialize(ProxyStatus.Unsupported));
        Assert.Equal("\"prores422HQ\"", JsonSerializer.Serialize(ProxyCodec.Prores422HQ));
        Assert.Equal("verified", FileState.Verified.Wire());
    }

    [Fact]
    public void Plan_CardPathsAreRootDateCard()
    {
        var plan = new OffloadPlan
        {
            Source = @"E:\", Date = "2026-10-04", CardName = "A001", DestRoots = [@"D:\x", @"F:\"],
            Scan = new SourceScan { Root = @"E:\", Files = [], Dirs = [], Skipped = [] },
        };
        Assert.Equal([@"D:\x\2026-10-04\A001", @"F:\2026-10-04\A001"], plan.CardPaths);
    }
}
