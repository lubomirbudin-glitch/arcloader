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
    public void ProgressWatch_OnlyARisingFrameOrTimeCounts()
    {
        var w = new ProgressWatch();
        // The opening block says nothing has been written yet (encoder lookahead, input probing).
        Assert.False(w.Advanced("frame=0"));
        Assert.False(w.Advanced("fps=0.00"));
        Assert.False(w.Advanced("out_time_us=N/A"));
        Assert.False(w.Advanced("out_time_us=-9223372036854775807"));
        Assert.False(w.Advanced("progress=continue"));
        Assert.True(w.Advanced("frame=1"));
        Assert.True(w.Advanced("out_time_us=40000"));
        // ffmpeg keeps printing the same block every half second while it is stuck.
        Assert.False(w.Advanced("frame=1"));
        Assert.False(w.Advanced("out_time_us=40000"));
        Assert.False(w.Advanced("progress=continue"));
        Assert.True(w.Advanced("frame=2"));
        Assert.True(w.Advanced("progress=end"));
    }

    /// What the fake ffmpeg does after logging its call: sleep without progress, write its output, or fail.
    enum Fake { Stall, Ok, Fail }

    /// A stand-in for ffmpeg: logs every call ("call", then "hw" when it got -hwaccel), then does `hw` or `sw`
    /// (the call with or without -hwaccel). (The log path is taken before the loop: shift moves %0 too.)
    static string FakeFfmpeg(string dir, Fake hw, Fake sw)
    {
        var path = Path.Combine(dir, "ffmpeg.cmd");
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
            if defined HW goto {hw}
            goto {sw}
            :Ok
            echo x>"%LAST%"
            exit /b 0
            :Fail
            echo boom {"{"}HW{"}"} 1>&2
            exit /b 1
            :Stall
            ping -n 120 127.0.0.1 >nul

            """.Replace("{HW}", "%HW%").Replace("\n", "\r\n"));
        return path;
    }

    static ExternalTools FakeTools(string dir, Fake hw, Fake sw) =>
        new ExternalTools { Ffmpeg = FakeFfmpeg(dir, hw, sw), NoSearch = true }.Resolved();

    /// Short; before the first progress the watchdog waits 3× as long, which also gives cmd.exe time to parse its
    /// arguments on a loaded PC.
    static readonly TimeSpan Stall = TimeSpan.FromSeconds(1);

    /// The fake has logged its call with -hwaccel, i.e. it is past its argument loop and sleeping.
    static bool LoggedHw(string dir)
    {
        try { return File.ReadAllLines(Path.Combine(dir, "calls.txt")).Contains("hw"); }
        catch (IOException) { return false; }
    }

    static ClipInfo H264Info => new() { Codec = "h264", Width = 320, Height = 180, Duration = 1, FrameRate = "25/1" };
    static ClipInfo Ffv1Info => new() { Codec = "ffv1", Width = 320, Height = 180, Duration = 1 };

    static string[] Calls(TempDir dir) => File.ReadAllLines(Path.Combine(dir.Path, "calls.txt"));

    [Fact]
    public void Render_Stalled_KilledAndRetriedOnceWithoutHwaccel_ThenFailsWithNote()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var log = new List<string>();
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, Fake.Stall, Fake.Stall)) { StallTimeout = Stall, Log = l => { lock (log) log.Add(l); } };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        var sw = Stopwatch.StartNew();
        var e = Assert.ThrowsAny<Exception>(() => r.Render(VideoClip("a.mov"), card, H264Info, null, output, _ => { }, () => false));
        Assert.Equal("ffmpeg se zasekl (1 s bez nového snímku) i bez hardwarového dekódování; proxy nevznikla", e.Message);
        Assert.Equal(["call", "hw", "call"], Calls(dir));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_PROXY")));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        lock (log)
        {
            Assert.Contains(log, l => l.Contains("with hardware decoding: ffmpeg se zasekl") && l.Contains("retrying in software"));
            Assert.Contains(log, l => l.Contains("software decoding failed too"));
        }
    }

    [Fact]
    public void Render_StalledWithHwaccel_SoftwareRetrySucceeds_AndIsLogged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var log = new List<string>();
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, Fake.Stall, Fake.Ok)) { StallTimeout = Stall, Log = l => { lock (log) log.Add(l); } };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        r.Render(VideoClip("a.mov"), card, H264Info, null, output, _ => { }, () => false);
        Assert.True(File.Exists(output));
        Assert.Equal(["call", "hw", "call"], Calls(dir));
        lock (log) Assert.Contains("proxy a: software decoding succeeded", log);
    }

    [Fact]
    public void Render_HwaccelFails_SoftwareFails_NoteNamesBoth()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, Fake.Fail, Fake.Fail)) { StallTimeout = Stall };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        var e = Assert.ThrowsAny<Exception>(() => r.Render(VideoClip("a.mov"), card, H264Info, null, output, _ => { }, () => false));
        Assert.Equal("ffmpeg failed (exit 1): boom; s hardwarovým dekódováním předtím: ffmpeg failed (exit 1): boom 1", e.Message);
        Assert.Equal(["call", "hw", "call"], Calls(dir));
    }

    [Fact]
    public void Render_SoftwareCodecStalls_NoRetry_NoteWithoutSoftwareClaim()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, Fake.Ok, Fake.Stall)) { StallTimeout = Stall };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        var e = Assert.ThrowsAny<Exception>(() => r.Render(VideoClip("a.mov"), card, Ffv1Info, null, output, _ => { }, () => false));
        Assert.Equal("ffmpeg se zasekl (1 s bez nového snímku); proxy nevznikla", e.Message);
        Assert.Equal(["call"], Calls(dir));
    }

    [Fact]
    public void Render_SoftwareCodec_NoHwaccelAtAll()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, Fake.Stall, Fake.Ok)) { StallTimeout = Stall };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        r.Render(VideoClip("a.mov"), card, Ffv1Info, null, output, _ => { }, () => false);
        Assert.True(File.Exists(output));
        Assert.Equal(["call"], Calls(dir));
    }

    [Fact]
    public void Render_CancelledWhileStuck_NoRetry()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, FakeTools(dir.Path, Fake.Stall, Fake.Ok)) { StallTimeout = TimeSpan.FromSeconds(30) };
        var output = Path.Combine(card, "_PROXY", "a.mov");
        var sw = Stopwatch.StartNew();
        var e = Assert.Throws<ToolError>(() => r.Render(VideoClip("a.mov"), card, H264Info, null, output, _ => { }, () => LoggedHw(dir.Path)));
        Assert.Contains("cancelled", e.Message);
        Assert.Equal(["call", "hw"], Calls(dir));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"took {sw.Elapsed}");
    }

    /// A RAW decoder that needs `delaySeconds` before its first data, piped into a consumer that reads to the end
    /// and prints no progress at all.
    static FrameSource SlowFeeder(string dir, int delaySeconds, out string consumer)
    {
        var feeder = Path.Combine(dir, "feeder.cmd");
        File.WriteAllText(feeder, $"@ping -n {delaySeconds + 1} 127.0.0.1 >nul\r\n@echo data\r\n");
        consumer = Path.Combine(dir, "consumer.cmd");
        File.WriteAllText(consumer, "@more >nul\r\n@exit /b 0\r\n");
        return new FrameSource { InputArgs = ["-i", "-"], FeederExe = feeder, AudioFromInput = false };
    }

    // Review: the clock started at launch, so a slow first frame was killed. Before the first progress 3× the limit
    // applies, and data flowing into ffmpeg is progress.
    [Fact]
    public void SlowFirstData_WithinGrace_NotKilled()
    {
        using var dir = new TempDir();
        var src = SlowFeeder(dir.Path, delaySeconds: 3, out var consumer);
        using var r = new ProxyRenderer(FakeSettings, new ExternalTools { NoSearch = true }.Resolved());
        r.RunFfmpeg(consumer, [], src, null, null, () => false, TimeSpan.FromSeconds(2));   // first data at 3 s: > 2 s, < 6 s
    }

    [Fact]
    public void NoDataAtAll_KilledAfterTheGrace()
    {
        using var dir = new TempDir();
        var src = SlowFeeder(dir.Path, delaySeconds: 60, out var consumer);
        using var r = new ProxyRenderer(FakeSettings, new ExternalTools { NoSearch = true }.Resolved());
        var sw = Stopwatch.StartNew();
        var e = Assert.Throws<FfmpegStalledException>(() => r.RunFfmpeg(consumer, [], src, null, null, () => false, TimeSpan.FromSeconds(1)));
        Assert.Equal("ffmpeg se zasekl (1 s bez nového snímku)", e.Message);
        Assert.InRange(sw.Elapsed.TotalSeconds, 2.8, 20);
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
