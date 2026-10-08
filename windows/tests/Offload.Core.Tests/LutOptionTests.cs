using Offload.Core;

namespace Offload.Core.Tests;

public class LutOptionTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Fact]
    public void Parse()
    {
        Assert.Equal((LutMode.Auto, null, null, null, null), LutOption.Parse(null, null, Cat));
        Assert.Equal((LutMode.Auto, null, null, "dji-dlog-dgamut", null), LutOption.Parse("auto", "dji-dlog-dgamut", Cat));
        Assert.Equal((LutMode.None, null, null, null, null), LutOption.Parse("none", null, Cat));
        Assert.Equal((LutMode.Builtin, "sony-slog3-sgamut3", null, null, null), LutOption.Parse("sony-slog3-sgamut3", null, Cat));
        Assert.Equal(LutMode.Custom, LutOption.Parse(@"C:\luts\show.cube", null, Cat).Mode);
        Assert.Equal(@"C:\luts\show.cube", LutOption.Parse(@"C:\luts\show.cube", null, Cat).Path);
        Assert.StartsWith("Neznámý LUT „nope“", LutOption.Parse("nope", null, Cat).Error);
        Assert.StartsWith("Neznámý záložní LUT „nope“", LutOption.Parse("auto", "nope", Cat).Error);
        Assert.Null(LutOption.Parse("auto", "none", Cat).Fallback);
    }

    // The CLI prints the LUT line only when it matters: proxies or clip analysis (thumbnails) are on.
    [Fact]
    public void PlanLine_OnlyWithProxiesOrAnalysis()
    {
        var auto = new ProxySettings { LutMode = LutMode.Auto, LutFallback = "dji-dlog-dgamut" };
        Assert.Null(LutOption.PlanLine(auto, analyzeClips: false));
        Assert.Equal("LUT: auto (fallback dji-dlog-dgamut)", LutOption.PlanLine(auto, analyzeClips: true));
        auto.Enabled = true;
        Assert.Equal("LUT: auto (fallback dji-dlog-dgamut)", LutOption.PlanLine(auto, analyzeClips: false));
        Assert.Equal("LUT: auto", LutOption.PlanLine(new ProxySettings { Enabled = true }, analyzeClips: false));
        Assert.Equal("LUT: none", LutOption.PlanLine(new ProxySettings { Enabled = true, LutMode = LutMode.None }, analyzeClips: false));
        Assert.Equal("LUT: sony-slog3-sgamut3", LutOption.PlanLine(new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "sony-slog3-sgamut3" }, analyzeClips: true));
        Assert.Equal(@"LUT: C:\luts\look.cube", LutOption.PlanLine(new ProxySettings { LutMode = LutMode.Custom, LutPath = @"C:\luts\look.cube" }, analyzeClips: true));
    }
}
