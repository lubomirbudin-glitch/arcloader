using System.Diagnostics;
using System.Text.Json;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 7, R9: the decoder's verdict while the proxy is made — damaged data and an early end are content_warn with the
/// place; a good clip is content_ok; other ffmpeg warnings are not.
public class ContentCheckTests
{
    static ClipInfo Info(int? frames = 100) => new() { Frames = frames, FrameRate = "25/1", Timecode = "01:00:00:00" };

    [Fact]
    public void FirstDecodeLine_IsTheWarning_AtTheFrameSeenLast()
    {
        var w = new ContentWatch();
        w.Progress("frame=49");
        w.Stderr("[libx264 @ 0000] using cpu capabilities: MMX2 SSE2Fast");   // not about the file
        w.Progress("frame=52");
        w.Stderr("[vist#0:0/h264 @ 0001] [dec:h264 @ 0002] corrupt decoded frame");
        w.Stderr("[h264 @ 0003] Invalid NAL unit size (11597 > 2699).");
        w.Progress("frame=100");

        var r = w.Verdict(Info());

        Assert.True(r.IsWarn);
        Assert.Equal("decode", r.Kind);
        Assert.Equal(52, r.Frame);
        Assert.Equal("01:00:02:02", r.Timecode);
        Assert.EndsWith("corrupt decoded frame", r.Message);
    }

    [Theory]
    [InlineData("[mov @ 01] Non-monotonic DTS; previous: 3000, current: 2000")]
    [InlineData("Application provided invalid, non monotonically increasing dts to muxer in stream 0")]
    public void TimestampLines_Warn(string line)
    {
        var w = new ContentWatch();
        w.Stderr(line);
        w.Progress("frame=100");
        Assert.Equal("timestamps", w.Verdict(Info()).Kind);
    }

    [Fact]
    public void FewerFramesThanTheFileSays_EndsEarly()
    {
        var w = new ContentWatch();
        w.Progress("frame=60");
        var r = w.Verdict(Info(100));
        Assert.Equal(("end", 60L, "01:00:02:10"), (r.Kind, r.Frame!.Value, r.Timecode));
        Assert.Equal("decoded 60 of 100 frames", r.Message);
    }

    [Fact]
    public void FrameCountUnknown_NotCounted_NoTimecode_FrameNumber()
    {
        var w = new ContentWatch();
        w.Progress("frame=10");
        Assert.False(w.Verdict(Info(null)).IsWarn);

        w.Stderr("Decoding error: Invalid data found when processing input");
        Assert.Equal("#10", w.Verdict(new ClipInfo { Frames = null }).Timecode);
    }

    [Fact]
    public void Reset_ForgetsTheHardwareAttempt()
    {
        var w = new ContentWatch();
        w.Stderr("[h264 @ 1] error while decoding MB 3 4");
        w.Reset();
        w.Progress("frame=100");
        Assert.False(w.Verdict(Info()).IsWarn);
    }

    // Spec 7: "Zvukové stopy se dekódují také" — what ffmpeg 8.1 said about AAC packets filled with garbage (measured).
    [Theory]
    [InlineData("[aist#0:1/aac @ 000001e8d4d45f00] [dec:aac @ 000001e8d53fc200] Error submitting packet to decoder: Invalid data found when processing input")]
    [InlineData("[aac @ 000001e8d51b69c0] Number of scalefactor bands in group (54) exceeds limit (49).")]
    [InlineData("[aac @ 000001e8d51b69c0] channel element 2.3 is not allocated")]
    [InlineData("[pcm_s24le @ 01] Invalid PCM packet, data has size 5 but at least a size of 6 was expected")]
    [InlineData("[mp3float @ 01] Header missing")]
    public void AudioDecoderLines_Warn_Audio(string line)
    {
        var w = new ContentWatch();
        w.Progress("frame=30");
        w.Stderr("[aac @ 01] Too many bits 8832.000000 > 6144 per frame requested, clamping to max");   // the proxy's AAC encoder, not the file
        w.Stderr(line);
        w.Progress("frame=100");

        var r = w.Verdict(Info());

        Assert.True(r.IsWarn);
        Assert.Equal("audio", r.Kind);
        Assert.Equal("01:00:01:05", r.Timecode);
        Assert.Equal("poškozený zvuk", ContentResult.KindText(r.Kind));
    }

    [Fact]
    public void AnAudioDefectAfterAVideoOne_TheFirstIsReported()
    {
        var w = new ContentWatch();
        w.Progress("frame=10");
        w.Stderr("[vist#0:0/h264 @ 0001] [dec:h264 @ 0002] corrupt decoded frame");
        w.Progress("frame=20");
        w.Stderr("[aist#0:1/aac @ 01] [dec:aac @ 02] Error submitting packet to decoder: Invalid data found when processing input");
        var r = w.Verdict(Info());
        Assert.Equal(("decode", 10L), (r.Kind, r.Frame!.Value));
    }

    [Fact]
    public void KindTexts_Czech()
    {
        Assert.Equal("poškozená data", ContentResult.KindText("decode"));
        Assert.Equal("chybné časové značky", ContentResult.KindText("timestamps"));
        Assert.Equal("snímky navíc", ContentResult.KindText("frames"));
        Assert.Equal("klip končí předčasně", ContentResult.KindText("end"));
        Assert.Equal("vada obsahu", ContentResult.KindText(null));
    }

    // Spec 7: content_warn is not a failure of the copy and never blocks safe_to_format; it is listed with its place.
    [Fact]
    public void ContentWarn_NeverFailsTheCopy_NorBlocksSafeToFormat_InTheReportJson()
    {
        using var dir = new TempDir();
        var report = Reports.Sample(dir.Sub("A001"));
        var w = new ContentWatch();
        w.Progress("frame=52");
        w.Stderr("[h264 @ 1] corrupt decoded frame");
        var warn = w.Verdict(Info());
        warn.Jpeg = [0xFF, 0xD8];
        var good = new ContentWatch();
        good.Progress("frame=100");
        report.Clips =
        [
            new ClipEntry { Clip = new Clip { Name = "A", Path = "CLIP/A.mov", Format = ClipFormat.Video, Segments = ["CLIP/A.mov"] }, Content = warn },
            new ClipEntry { Clip = new Clip { Name = "B", Path = "CLIP/B.mov", Format = ClipFormat.Video, Segments = ["CLIP/B.mov"] }, Content = good.Verdict(Info()) },
            new ClipEntry { Clip = new Clip { Name = "C", Path = "CLIP/C.mov", Format = ClipFormat.Video, Segments = ["CLIP/C.mov"] } },
        ];

        Assert.True(report.Success);
        Assert.True(report.SafeToFormat);
        Assert.Equal("A", Assert.Single(report.ContentWarnings).Clip.Name);

        using var doc = JsonDocument.Parse(report.ToJson());
        var clips = doc.RootElement.GetProperty("clips");
        var c0 = clips[0].GetProperty("content");
        Assert.Equal("warn", c0.GetProperty("state").GetString());
        Assert.Equal("decode", c0.GetProperty("kind").GetString());
        Assert.Equal(52, c0.GetProperty("frame").GetInt64());
        Assert.Equal("01:00:02:02", c0.GetProperty("timecode").GetString());
        Assert.False(c0.TryGetProperty("jpeg", out _));
        Assert.False(c0.TryGetProperty("Jpeg", out _));
        var c1 = clips[1].GetProperty("content");
        Assert.Equal("ok", c1.GetProperty("state").GetString());
        Assert.False(c1.TryGetProperty("kind", out _));
        Assert.Equal(100, c1.GetProperty("framesDecoded").GetInt64());
        Assert.False(clips[2].TryGetProperty("content", out _));

        var back = OffloadReport.FromJson(report.ToJson());
        Assert.Equal("01:00:02:02", back.Clips[0].Content!.Timecode);
        Assert.True(back.SafeToFormat);
    }

    static ProxyRenderer Renderer() => new(new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false }, new ExternalTools().Resolved())
    {
        ForcedEncoder = "libx264",
    };

    static ContentResult RenderAndCheck(string card, string rel)
    {
        using var renderer = Renderer();
        var clip = new Clip { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel] };
        var info = renderer.Probe(clip, card);
        var watch = new ContentWatch();
        renderer.Render(clip, card, info, null, Path.Combine(card, "_PROXY", clip.Name + ".mov"), _ => { }, () => false, content: watch);
        var verdict = watch.Verdict(info);
        if (verdict.IsWarn) verdict.Jpeg = renderer.Snapshot(clip, card, info, verdict.Frame ?? 0);
        return verdict;
    }

    [FfmpegFact]
    public void GoodClip_ContentOk_WithTheFrameCount()
    {
        using var dir = new TempDir();
        TestMedia.MakeFaststart(Path.Combine(dir.Path, "good.mp4"));
        var r = RenderAndCheck(dir.Path, "good.mp4");
        Assert.False(r.IsWarn);
        Assert.Equal(50, r.FramesDecoded);
        Assert.Equal(50, r.FramesExpected);
    }

    [FfmpegFact]
    public void GoodClipWithAudio_ContentOk()
    {
        using var dir = new TempDir();
        TestMedia.MakeClip(Path.Combine(dir.Path, "good.mp4"), seconds: 2, width: 640, height: 360);
        var r = RenderAndCheck(dir.Path, "good.mp4");
        Assert.False(r.IsWarn, r.Message);
    }

    // Spec 11: a damaged file from the camera — the copy is bit-exact, the decoder says where it is broken.
    [FfmpegFact]
    public void GarbageInTheMiddle_Warn_Decode_WithAPicture()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "bad.mp4");
        TestMedia.MakeFaststart(path, seconds: 4);
        var bytes = File.ReadAllBytes(path);
        new Random(5).NextBytes(bytes.AsSpan(bytes.Length / 2, 20_000));
        File.WriteAllBytes(path, bytes);

        var r = RenderAndCheck(dir.Path, "bad.mp4");

        Assert.True(r.IsWarn);
        Assert.Equal("decode", r.Kind);
        Assert.StartsWith("01:00:0", r.Timecode);
        Assert.NotNull(r.Jpeg);
        Assert.Equal(0xFF, r.Jpeg![0]);   // a JPEG
    }

    // Spec 7: the audio of the proxy is decoded too — garbage in AAC packets only (the picture intact) is a warning.
    [FfmpegFact]
    public void GarbageInTheAudioOnly_Warn_Audio()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sound.mp4");
        TestMedia.MakeClip(path, seconds: 4, width: 640, height: 360);
        var packets = ToolProcess.Run(TestMedia.Ffprobe!, ["-v", "error", "-select_streams", "a", "-show_entries", "packet=pos,size", "-of", "csv=p=0", path])
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToArray())
            .ToList();
        Assert.True(packets.Count > 100);
        var bytes = File.ReadAllBytes(path);
        var random = new Random(5);
        foreach (var p in packets.Skip(80).Take(20))
        {
            // csv order follows the entries as ffprobe lists them: size, pos
            long size = p[0], pos = p[1];
            random.NextBytes(bytes.AsSpan((int)pos, (int)size));
        }
        File.WriteAllBytes(path, bytes);

        var r = RenderAndCheck(dir.Path, "sound.mp4");

        Assert.True(r.IsWarn);
        Assert.Equal("audio", r.Kind);
        Assert.StartsWith("01:00:0", r.Timecode);
    }

    [FfmpegFact]
    public void CutOffClip_Warn()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "cut.mp4");
        TestMedia.MakeFaststart(path, seconds: 4);
        using (var f = new FileStream(path, FileMode.Open)) f.SetLength(f.Length * 6 / 10);

        var r = RenderAndCheck(dir.Path, "cut.mp4");

        Assert.True(r.IsWarn);
        Assert.Contains(r.Kind, new[] { "decode", "end" });
        Assert.True(r.FramesDecoded < 100);
    }

    // ----- with a fake ffmpeg: the hardware retry and the stall watchdog -----

    static Clip VideoClip(string rel) => new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 1 };

    /// ProRes: the encoder is not probed, the fake below only ever sees the render itself.
    static ProxySettings FakeSettings => new() { Enabled = true, Height = 180, Codec = ProxyCodec.ProresProxy, BurnFilename = false, BurnTimecode = false, Audio = false };

    /// A stand-in for ffmpeg: with -hwaccel it runs `hw`, without it `sw` (cmd labels below).
    static ExternalTools Fake(string dir, string hw, string sw)
    {
        var path = Path.Combine(dir, "ffmpeg.cmd");
        File.WriteAllText(path, $"""
            @echo off
            setlocal
            set HW=
            set LAST=
            :next
            if "%~1"=="" goto run
            if "%~1"=="-hwaccel" set HW=1
            set "LAST=%~1"
            shift
            goto next
            :run
            if defined HW goto {hw}
            goto {sw}
            :WarnFail
            echo [h264 @ 0001] corrupt decoded frame 1>&2
            exit /b 1
            :Clean
            echo frame=100
            echo progress=end
            echo x>"%LAST%"
            exit /b 0
            :WarnForever
            echo [h264 @ 0001] corrupt decoded frame 1>&2
            ping -n 2 127.0.0.1 >nul
            goto WarnForever

            """.Replace("\n", "\r\n"));
        return new ExternalTools { Ffmpeg = path, NoSearch = true }.Resolved();
    }

    // The hardware decoder's complaint is not the file's verdict: the clean software retry decides (Reset).
    [Fact]
    public void HardwareAttemptComplained_SoftwareRetryClean_ContentOk()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, Fake(dir.Path, "WarnFail", "Clean")) { StallTimeout = TimeSpan.FromSeconds(30) };
        var watch = new ContentWatch();
        var info = new ClipInfo { Codec = "h264", Width = 320, Height = 180, Duration = 4, FrameRate = "25/1", Frames = 100, Timecode = "01:00:00:00" };

        r.Render(VideoClip("a.mov"), card, info, null, Path.Combine(card, "_PROXY", "a.mov"), _ => { }, () => false, content: watch);

        var verdict = watch.Verdict(info);
        Assert.False(verdict.IsWarn, verdict.Message);
        Assert.Equal(100, verdict.FramesDecoded);
    }

    // The stall watchdog: decoder warnings on stderr are not progress — an ffmpeg that only keeps complaining is killed.
    [Fact]
    public void DecoderWarningsAreNotProgress_StalledRenderStillKilled()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(FakeSettings, Fake(dir.Path, "WarnForever", "WarnForever")) { StallTimeout = TimeSpan.FromSeconds(1) };
        var watch = new ContentWatch();
        var info = new ClipInfo { Codec = "ffv1", Width = 320, Height = 180, Duration = 4, FrameRate = "25/1", Timecode = "01:00:00:00" };
        var sw = Stopwatch.StartNew();

        var e = Assert.ThrowsAny<Exception>(() => r.Render(VideoClip("a.mov"), card, info, null, Path.Combine(card, "_PROXY", "a.mov"), _ => { }, () => false, content: watch));

        Assert.Equal("ffmpeg se zasekl (1 s bez nového snímku); proxy nevznikla", e.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        Assert.Equal("decode", watch.Verdict(info).Kind);   // what it said is still known (the job keeps no verdict for a failed proxy)
    }

    // Without a content watch the proxy is rendered as before (-loglevel error); with one at warning level.
    [Fact]
    public void LogLevel_WarningOnlyWithTheContentCheck()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var argsFile = Path.Combine(dir.Path, "args.txt");
        var path = Path.Combine(dir.Path, "ffmpeg.cmd");
        File.WriteAllText(path, "@echo off\r\necho %*>>\"%~dp0args.txt\"\r\nexit /b 1\r\n");
        using var r = new ProxyRenderer(FakeSettings, new ExternalTools { Ffmpeg = path, NoSearch = true }.Resolved());
        var info = new ClipInfo { Codec = "ffv1", Width = 320, Height = 180, Duration = 1, FrameRate = "25/1" };

        Assert.ThrowsAny<Exception>(() => r.Render(VideoClip("a.mov"), card, info, null, Path.Combine(card, "_PROXY", "a.mov"), _ => { }, () => false));
        Assert.ThrowsAny<Exception>(() => r.Render(VideoClip("a.mov"), card, info, null, Path.Combine(card, "_PROXY", "a.mov"), _ => { }, () => false, content: new ContentWatch()));

        var lines = File.ReadAllLines(argsFile);
        Assert.Contains("-loglevel error", lines[0]);
        Assert.Contains("-loglevel warning", lines[1]);
    }
}
