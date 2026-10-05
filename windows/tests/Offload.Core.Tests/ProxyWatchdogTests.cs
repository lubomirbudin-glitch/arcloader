using System.Diagnostics;

namespace Offload.Core.Tests;

/// Hardware decoding only where it helps, and a stalled ffmpeg never hangs the job (ffmpeg 8.1 with -hwaccel auto on FFV1
/// hung forever on Vulkan VK_ERROR_DEVICE_LOST).
public class ProxyWatchdogTests
{
    static Clip VideoClip(string rel) => new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 1 };

    /// ProRes: the encoder is not probed, so the fake ffmpeg below only ever sees the render itself.
    static ProxySettings FakeSettings => new() { Enabled = true, Height = 180, Codec = ProxyCodec.ProresProxy, BurnFilename = false, BurnTimecode = false, Audio = false };

    [Theory]
    [InlineData("h264", true)]
    [InlineData("hevc", true)]
    [InlineData("HEVC", true)]
    [InlineData("h264 (High)", true)]
    [InlineData("ffv1", false)]
    [InlineData("prores", false)]
    [InlineData("mjpeg", false)]
    [InlineData("dnxhd", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HardwareDecoding_OnlyH264AndHevc(string? codec, bool expected) =>
        Assert.Equal(expected, ProxyRenderer.UseHardwareDecoding(codec));

    [Fact]
    public void ProgressWatch_OnlyANewFrameOrTimeCounts()
    {
        var w = new ProgressWatch();
        Assert.True(w.Advanced("frame=0"));
        Assert.False(w.Advanced("fps=0.00"));
        Assert.True(w.Advanced("out_time_us=N/A"));
        Assert.False(w.Advanced("progress=continue"));
        // ffmpeg keeps printing the same block every half second while it is stuck.
        Assert.False(w.Advanced("frame=0"));
        Assert.False(w.Advanced("out_time_us=N/A"));
        Assert.False(w.Advanced("progress=continue"));
        Assert.True(w.Advanced("frame=1"));
        Assert.True(w.Advanced("out_time_us=40000"));
        Assert.True(w.Advanced("progress=end"));
    }

    /// A stand-in for ffmpeg: logs every call ("call", then "hw" when it got -hwaccel) and never prints progress — it
    /// just sleeps. (The log path is taken before the loop: shift moves %0 too.) With `softwareWorks` a call without -hwaccel writes its output (the last argument) and exits 0.
    static string FakeFfmpeg(string dir, bool softwareWorks)
    {
        var path = Path.Combine(dir, "ffmpeg.cmd");
        var software = softwareWorks ? "if not defined HW (echo x>\"%LAST%\"& exit /b 0)" : "rem";
        File.WriteAllText(path, $"""
            @echo off
            setlocal
            set HW=
            set LAST=
            set "LOG=%~dp0calls.txt"
            echo call>>"%LOG%"
            :next
            if "%~1"=="" goto run
            if "%~1"=="-hwaccel" set HW=1
            set "LAST=%~1"
            shift
            goto next
            :run
            if defined HW echo hw>>"%LOG%"
            {software}
            ping -n 120 127.0.0.1 >nul

            """.Replace("\n", "\r\n"));
        return path;
    }

    static ExternalTools FakeTools(string dir, bool softwareWorks) =>
        new ExternalTools { Ffmpeg = FakeFfmpeg(dir, softwareWorks), NoSearch = true }.Resolved();

    static ClipInfo H264Info => new() { Codec = "h264", Width = 320, Height = 180, Duration = 1, FrameRate = "25/1" };

    [Fact]
    public void Render_Stalled_KilledAndRetriedOnceWithoutHwaccel_ThenFailsWithNote()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, softwareWorks: false)) { StallTimeout = TimeSpan.FromSeconds(1) };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        var sw = Stopwatch.StartNew();
        var e = Assert.ThrowsAny<Exception>(() => r.Render(VideoClip("a.mov"), card, H264Info, null, output, _ => { }, () => false));
        Assert.StartsWith("ffmpeg se zasekl", e.Message);
        Assert.Contains("bez hardwarového dekódování", e.Message);
        Assert.Equal(["call", "hw", "call"], File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_PROXY")));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
    }

    [Fact]
    public void Render_StalledWithHwaccel_SoftwareRetrySucceeds()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, softwareWorks: true)) { StallTimeout = TimeSpan.FromSeconds(1) };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        r.Render(VideoClip("a.mov"), card, H264Info, null, output, _ => { }, () => false);
        Assert.True(File.Exists(output));
        Assert.Equal(["call", "hw", "call"], File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")));
    }

    [Fact]
    public void Render_SoftwareCodec_NoHwaccelAtAll()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, softwareWorks: true)) { StallTimeout = TimeSpan.FromSeconds(1) };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        r.Render(VideoClip("a.mov"), card, new ClipInfo { Codec = "ffv1", Width = 320, Height = 180, Duration = 1 }, null, output, _ => { }, () => false);
        Assert.True(File.Exists(output));
        Assert.Equal(["call"], File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")));
    }

    [Fact]
    public void Render_CancelledWhileStuck_NoRetry()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, softwareWorks: true)) { StallTimeout = TimeSpan.FromSeconds(30) };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        var sw = Stopwatch.StartNew();
        var e = Assert.Throws<ToolError>(() => r.Render(VideoClip("a.mov"), card, H264Info, null, output, _ => { },
            () => sw.Elapsed > TimeSpan.FromMilliseconds(500)));
        Assert.Contains("cancelled", e.Message);
        Assert.Equal(["call", "hw"], File.ReadAllLines(Path.Combine(dir.Path, "calls.txt")));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
    }

    // The real thing: an FFV1 clip (the codec that hung with -hwaccel auto) renders a proxy and never hangs.
    [FfmpegFact]
    public void Render_Ffv1_Completes()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        ToolProcess.Run(TestMedia.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=1",
            "-c:v", "ffv1", "-pix_fmt", "yuv422p10le", Path.Combine(card, "a.mkv")]);
        using var r = new ProxyRenderer(new ProxySettings { Enabled = true, Height = 180 }, new ExternalTools().Resolved());
        var clip = VideoClip("a.mkv");
        var info = r.Probe(clip, card);
        Assert.StartsWith("ffv1", info.Codec);
        var output = Path.Combine(card, "_PROXY", "a.mov");
        var render = Task.Run(() => r.Render(clip, card, info, null, output, _ => { }, () => false));
        Assert.True(render.Wait(TimeSpan.FromSeconds(60)), "FFV1 proxy render did not finish in 60 s");
        Assert.True(new FileInfo(output).Length > 0);
    }
}
