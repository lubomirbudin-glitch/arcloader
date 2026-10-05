namespace Offload.Core.Tests;

public class TimecodeEncoderTests
{
    [Theory]
    [InlineData("01:00:00:00", true)]
    [InlineData("01:00:00;29", true)]
    [InlineData("1:00:00:00", false)]
    [InlineData(null, false)]
    public void IsValid(string? tc, bool ok) => Assert.Equal(ok, Timecode.IsValid(tc));

    [Theory]
    [InlineData("24000/1001", 23.976)]
    [InlineData("25/1", 25.0)]
    [InlineData("50", 50.0)]
    public void Fps(string rate, double fps) => Assert.Equal(fps, Timecode.Fps(rate)!.Value, 3);

    [Fact]
    public void Fps_InvalidIsNull()
    {
        Assert.Null(Timecode.Fps(null));
        Assert.Null(Timecode.Fps("0/0"));
    }

    [Theory]
    [InlineData("01:00:00:00", 0, 25, "01:00:00:00")]
    [InlineData("01:00:00:00", 50, 25, "01:00:02:00")]
    [InlineData("01:00:00:00", 99, 25, "01:00:03:24")]
    [InlineData("23:59:59:24", 1, 25, "00:00:00:00")]
    [InlineData("10:00:00;00", 30, 29.97, "10:00:01;00")]
    public void Add(string tc, long frames, double fps, string expected) => Assert.Equal(expected, Timecode.Add(tc, frames, fps));

    [Fact]
    public void VideoArgs_PerEncoder()
    {
        Assert.Equal(["-c:v", "h264_nvenc", "-profile:v", "high", "-rc", "vbr", "-b:v", "10M", "-maxrate", "15M"],
            Encoders.VideoArgs("h264_nvenc", ProxyCodec.H264, 10));
        Assert.Equal(["-c:v", "hevc_amf", "-tag:v", "hvc1", "-b:v", "8M", "-maxrate", "12M"],
            Encoders.VideoArgs("hevc_amf", ProxyCodec.Hevc, 8));
        Assert.Equal(["-c:v", "libx264", "-profile:v", "high", "-preset", "fast", "-b:v", "10M", "-maxrate", "15M", "-bufsize", "20M"],
            Encoders.VideoArgs("libx264", ProxyCodec.H264, 10));
        Assert.Equal(["-c:v", "prores_ks", "-profile:v", "1", "-vendor", "apl0"],
            Encoders.VideoArgs("prores_ks", ProxyCodec.ProresLT, 10));
        Assert.Equal(["-c:a", "pcm_s16le"], Encoders.AudioArgs(ProxyCodec.Prores422HQ));
        Assert.Equal("yuv422p10le", Encoders.PixelFormat(ProxyCodec.ProresProxy));
        Assert.Equal("yuv420p", Encoders.PixelFormat(ProxyCodec.Hevc));
    }

    [Fact]
    public void CodecWireNames_RoundTrip()
    {
        foreach (var c in Enum.GetValues<ProxyCodec>())
        {
            Assert.True(ProxyCodecExtensions.TryParseWire(c.Wire(), out var back));
            Assert.Equal(c, back);
        }
        Assert.False(ProxyCodecExtensions.TryParseWire("vp9", out _));
    }

    [FfmpegFact]
    public void Pick_FindsAWorkingEncoder()
    {
        var h264 = Encoders.Pick(TestMedia.Ffmpeg!, ProxyCodec.H264);
        Assert.Contains(h264, Encoders.H264);
        Assert.True(Encoders.Works(TestMedia.Ffmpeg!, h264));
        Assert.Equal("prores_ks", Encoders.Pick(TestMedia.Ffmpeg!, ProxyCodec.ProresProxy));
        Assert.False(Encoders.Works(TestMedia.Ffmpeg!, "no_such_encoder"));
    }

    // Final review C2
    [FfmpegFact]
    public void Pick_FallsBackPastAFailingEncoder()
    {
        Assert.Equal("libx264", Encoders.Pick(TestMedia.Ffmpeg!, ProxyCodec.H264, ["no_such_encoder", "libx264"]));
    }
}
