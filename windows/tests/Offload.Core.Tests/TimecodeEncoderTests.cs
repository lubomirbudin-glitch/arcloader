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
    // Review focus 1: drop-frame counts as the camera does — no ;00/;01 (;00–;03 at 59.94) at a minute but every tenth.
    [InlineData("00:00:59;29", 1, 29.97, "00:01:00;02")]
    [InlineData("00:09:59;29", 1, 29.97, "00:10:00;00")]
    [InlineData("01:00:00;00", 17982, 29.97, "01:10:00;00")]
    [InlineData("23:59:59;29", 1, 29.97, "00:00:00;00")]
    [InlineData("00:00:59;59", 1, 59.94, "00:01:00;04")]
    [InlineData("00:00:59;29", 1, 30, "00:01:00;00")]   // ";" at exactly 30 fps: no frames dropped
    public void Add(string tc, long frames, double fps, string expected) => Assert.Equal(expected, Timecode.Add(tc, frames, fps));

    [Theory]
    [InlineData(310, 25, "00:00:12:10")]
    [InlineData(48, 23.976, "00:00:02:00")]
    [InlineData(2, 29.97, "00:00:00:02")]
    public void Length_NominalFrames(long frames, double fps, string expected) => Assert.Equal(expected, Timecode.Length(frames, fps));

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
        Assert.Equal("yuv422p10le", Encoders.PixelFormat(ProxyCodec.ProresProxy));
        Assert.Equal("yuv420p", Encoders.PixelFormat(ProxyCodec.Hevc));
    }

    // Spec 3.4, R14: every audio stream — AAC 64 kb/s per channel of each stream (128k-512k), ProRes 16-bit PCM.
    [Fact]
    public void AudioArgs_EveryStream()
    {
        Assert.Equal(["-c:a", "pcm_s16le"], Encoders.AudioArgs(ProxyCodec.Prores422HQ, [1, 1, 1, 1]));
        Assert.Equal(["-c:a", "aac", "-b:a:0", "128k", "-b:a:1", "128k", "-b:a:2", "384k", "-b:a:3", "512k"],
            Encoders.AudioArgs(ProxyCodec.H264, [1, 2, 6, 16]));
        Assert.Equal(["-c:a", "aac", "-b:a:0", "128k"], Encoders.AudioArgs(ProxyCodec.Hevc, [0]));
        Assert.Equal(["-c:a", "aac", "-b:a", "192k"], Encoders.AudioArgs(ProxyCodec.H264, []));
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
