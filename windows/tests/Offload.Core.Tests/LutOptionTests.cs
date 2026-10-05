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
        Assert.StartsWith("Unknown LUT", LutOption.Parse("nope", null, Cat).Error);
        Assert.StartsWith("Unknown fallback LUT", LutOption.Parse("auto", "nope", Cat).Error);
        Assert.Null(LutOption.Parse("auto", "none", Cat).Fallback);
    }
}
