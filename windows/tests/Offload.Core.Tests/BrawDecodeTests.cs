using Offload.BrawDecode;

namespace Offload.Core.Tests;

public class BrawDecodeTests
{
    // --- command line (same as the Mac tool) ---

    [Fact]
    public void Options_MacContract()
    {
        Assert.Equal(new Options(true, Scale.Eighth, 0, null, @"E:\A001.braw"), Options.Parse(["--info", "--scale", "eighth", @"E:\A001.braw"]));
        Assert.Equal(new Options(false, Scale.Quarter, 120, 1, "a.braw"), Options.Parse(["--scale", "quarter", "--start", "120", "--count", "1", "a.braw"]));
        Assert.Equal(new Options(false, Scale.Full, 0, null, "a.braw"), Options.Parse(["a.braw"]));
        Assert.Equal(Scale.Half, Options.Parse(["--scale", "half", "a.braw"])!.Scale);
    }

    [Theory]
    [InlineData]
    [InlineData("--info")]
    [InlineData("--scale", "huge", "a.braw")]
    [InlineData("--start", "-1", "a.braw")]
    [InlineData("--count", "x", "a.braw")]
    [InlineData("--scale")]
    [InlineData("--bogus", "a.braw")]
    public void Options_Bad_IsNull(params string[] args) => Assert.Null(Options.Parse(args));

    [Fact]
    public void Options_Range_ClampedToClip()
    {
        Assert.Equal((0UL, 240UL), Options.Parse(["a"])!.Range(240));
        Assert.Equal((239UL, 240UL), Options.Parse(["--start", "239", "--count", "5", "a"])!.Range(240));
        Assert.Equal((120UL, 121UL), Options.Parse(["--start", "120", "--count", "1", "a"])!.Range(240));
        Assert.Equal((300UL, 240UL), Options.Parse(["--start", "300", "a"])!.Range(240));   // nothing to do
        Assert.Equal((5UL, 240UL), Options.Parse(["--start", "5", "--count", "18446744073709551615", "a"])!.Range(240));
    }

    // --- --info JSON ---

    [Fact]
    public void InfoJson_MacKeysThenColour()
    {
        var f = new ClipFacts(1440, 810, 11520, 6480, 23.976f, 240, "01:02:03:04", "URSA Mini Pro 12K", "Blackmagic Design Film", "Blackmagic Design", 5);
        Assert.Equal("""{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5}""" + "\n",
            InfoJson.Format(f));
        var bare = new ClipFacts(1, 2, 3, 4, 25, 0, "", "", null, null, null);
        Assert.Equal("""{"width":1,"height":2,"sourceWidth":3,"sourceHeight":4,"fps":25.000000,"frames":0,"timecode":"","camera":""}""" + "\n",
            InfoJson.Format(bare));
    }

    [Fact]
    public void InfoJson_RoundTripsThroughBrawBackend()
    {
        var f = new ClipFacts(720, 405, 5760, 3240, 29.97f, 10, "10:00:00:00", "Pocket \"6K\" \\ Pro\u0001", "Blackmagic Design Video", null, 5);
        var i = BrawBackend.ParseInfo(InfoJson.Format(f));
        Assert.Equal((720, 405, 5760, 3240, 10), (i.Width, i.Height, i.SourceWidth, i.SourceHeight, i.Frames));
        Assert.Equal(29.97, i.Fps, 3);
        Assert.Equal("Pocket \"6K\" \\ Pro", i.Camera);
        Assert.Equal(("Blackmagic Design Video", null, 5), (i.Gamma, i.Gamut, i.ColorScienceGen));
    }

    // --- frame bytes ---

    [Fact]
    public unsafe void FrameSlot_CopiesExactlyWidthHeightRgb48()
    {
        var padded = new byte[4 * 2 * 6 + 100];   // the SDK pads its buffer at the end
        for (int i = 0; i < padded.Length; i++) padded[i] = (byte)i;
        var slot = new FrameSlot();
        fixed (byte* p = padded) Assert.True(slot.Fill(4, 2, p, (uint)padded.Length));
        Assert.True(slot.Ok);
        Assert.Equal(48, slot.Length);
        Assert.Equal(padded[..48], slot.Data[..slot.Length]);
        fixed (byte* p = padded) Assert.False(new FrameSlot().Fill(4, 2, p, 47));   // short buffer = failure
        Assert.False(new FrameSlot().Fill(4, 2, null, 48));
        Assert.Equal(12288L * 6480 * 6, FrameSlot.ExpectedBytes(12288, 6480));
    }
}
