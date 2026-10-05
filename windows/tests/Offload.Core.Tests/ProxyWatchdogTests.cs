using System.Diagnostics;
using System.Text;

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
        var src = SlowFeeder(dir.Path, delaySeconds: 6, out var consumer);
        using var r = new ProxyRenderer(FakeSettings, new ExternalTools { NoSearch = true }.Resolved());
        // first data at 6 s: > 5 s, < 15 s; after it the consumer has 5 s to end (2 s flaked under a loaded suite)
        r.RunFfmpeg(consumer, [], src, null, null, () => false, TimeSpan.FromSeconds(5));
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

    /// A stand-in for braw-decode with a hung Blackmagic RAW SDK: frames never come; `--info` hangs too, or prints a
    /// one-frame 320×180 clip.
    static string FakeBrawDecode(string dir, bool hangInfo)
    {
        var path = Path.Combine(dir, "braw-decode.cmd");
        File.WriteAllText(path, $$"""
            @echo off
            if "%~1"=="--info" goto {{(hangInfo ? "hang" : "info")}}
            :hang
            ping -n 60 127.0.0.1 >nul
            exit /b 0
            :info
            echo {"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":""}
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        return path;
    }

    // Ruling M3: `braw-decode --info` runs with a timeout — a hung SDK gives the clip a note instead of hanging the job.
    [Fact]
    public void BrawInfo_Hung_KilledAfterTheTimeout()
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = FakeBrawDecode(dir.Path, hangInfo: true), NoSearch = true };
        var backend = new BrawBackend(_ => "BlackmagicRawAPI.dll", infoTimeout: TimeSpan.FromSeconds(2));
        var sw = Stopwatch.StartNew();
        var e = Assert.Throws<ToolError>(() => backend.Probe(Path.Combine(dir.Path, "A001.braw"), tools, dir.Path));
        Assert.Equal("braw-decode failed (exit -1): neodpověděl do 2 s — ukončen", e.Message);
        Assert.InRange(sw.Elapsed.TotalSeconds, 1.9, 30);
    }

    [Fact]
    public void BrawInfo_DefaultTimeout() => Assert.Equal(TimeSpan.FromSeconds(60), BrawBackend.DefaultInfoTimeout);

    // Ruling M3: a BRAW thumbnail (the decoder piped into ffmpeg) runs under the stall watchdog like a proxy.
    [Fact]
    public void BrawThumbnail_DecoderHangs_KilledAsStalled()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var consumer = Path.Combine(dir.Path, "ffmpeg.cmd");   // reads its input to the end, prints no progress
        File.WriteAllText(consumer, "@more >nul\r\n@exit /b 0\r\n");
        var tools = new ExternalTools { Ffmpeg = consumer, BrawDecoder = FakeBrawDecode(dir.Path, hangInfo: false), NoSearch = true }.Resolved();
        using var r = new ProxyRenderer(FakeSettings, tools) { StallTimeout = Stall };
        var clip = new Clip { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
        var info = new ClipInfo { Codec = "Blackmagic RAW", Width = 320, Height = 180, FrameRate = "25", Frames = 1, Duration = 0.04 };
        var sw = Stopwatch.StartNew();
        var e = Assert.Throws<FfmpegStalledException>(() => r.Thumbnails(clip, card, info, null));
        Assert.Equal("ffmpeg se zasekl (1 s bez nového snímku)", e.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
    }

    /// Task 6 note: ffmpeg may end before the decoder does (a thumbnail takes one frame; or ffmpeg failed). The pump
    /// then closes the decoder's stdout, so the decoder sees the broken pipe and stops by itself (braw-decode exits 0)
    /// instead of blocking on a full pipe until it is killed.
    [Fact]
    public void ConsumerEndsFirst_FeederSeesTheClosedPipe_ExitsByItself()
    {
        using var dir = new TempDir();
        var stopped = Path.Combine(dir.Path, "stopped.txt");
        // Writes until a write fails, notes it and exits 0, like braw-decode. WriteFile on the raw stdout handle:
        // .NET Framework's console stream and FileStream both swallow a broken pipe.
        var script = $$"""
            Add-Type -Namespace Fake -Name Std -MemberDefinition '[DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int n); [DllImport("kernel32.dll")] public static extern bool WriteFile(IntPtr h, byte[] b, int n, out int written, IntPtr o);'
            $out = [Fake.Std]::GetStdHandle(-11)
            $line = [Text.Encoding]::ASCII.GetBytes(('x' * 1023) + "`n")
            $n = 0
            while ([Fake.Std]::WriteFile($out, $line, $line.Length, [ref]$n, [IntPtr]::Zero)) { }
            Set-Content -LiteralPath '{{stopped}}' -Value stopped
            exit 0
            """;
        var src = new FrameSource
        {
            InputArgs = ["-i", "-"],
            FeederExe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            FeederArgs = ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
            AudioFromInput = false,
        };
        var consumer = Path.Combine(dir.Path, "consumer.cmd");   // takes one line and ends, like ffmpeg with its one frame
        File.WriteAllText(consumer, "@set /p x=\r\n@exit /b 0\r\n");
        using var r = new ProxyRenderer(FakeSettings, new ExternalTools { NoSearch = true }.Resolved());
        r.RunFfmpeg(consumer, [], src, null, null, () => false, TimeSpan.FromSeconds(30));   // a feeder ending with 0 is no error
        Assert.True(SpinWait.SpinUntil(() => File.Exists(stopped), TimeSpan.FromSeconds(10)), "the feeder never saw its stdout closed (it was killed)");
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
