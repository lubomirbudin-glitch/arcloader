namespace Offload.Core.Tests;

public class ProxyRendererTests
{
    static ExternalTools Tools => new ExternalTools().Resolved();
    static Clip VideoClip(string rel) => new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 1 };

    [Fact]
    public void FramePositions_FirstMiddleLast()
    {
        var pos = ProxyRenderer.FramePositions(new ClipInfo { FrameRate = "25/1", Duration = 4, Frames = 100 });
        Assert.Equal([new FramePosition(0, 0, false), new FramePosition(2, 50, false), new FramePosition(3.96, 99, true)], pos);
        var info = new ClipInfo { FrameRate = "25/1", Duration = 4, Frames = 100, Timecode = "01:00:00:00" };
        Assert.Equal(["01:00:00:00", "01:00:02:00", "01:00:03:24"], ProxyRenderer.FramePositions(info).Select(p => ProxyRenderer.Label(info, p)));
        var noTc = new ClipInfo { FrameRate = "25/1", Duration = 4, Frames = 100 };
        Assert.Equal("#50", ProxyRenderer.Label(noTc, ProxyRenderer.FramePositions(noTc)[1]));
    }

    [Fact]
    public void FramePositions_UnknownLengthOrSingleFrame_OnlyFirst()
    {
        Assert.Single(ProxyRenderer.FramePositions(new ClipInfo()));
        Assert.Single(ProxyRenderer.FramePositions(new ClipInfo { FrameRate = "25/1", Duration = 0.04, Frames = 1 }));
        var two = ProxyRenderer.FramePositions(new ClipInfo { FrameRate = "25/1", Duration = 0.08, Frames = 2 });
        Assert.Equal([0L, 1L], two.Select(p => p.Frame));
    }

    [Fact]
    public void UnsupportedReason()
    {
        using var none = new ProxyRenderer(new ProxySettings(), new ExternalTools { NoSearch = true }.Resolved());
        Assert.Equal("ffmpeg not found", none.UnsupportedReason(VideoClip("a.mov")));
        var ff = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true }.Resolved();
        using var some = new ProxyRenderer(new ProxySettings(), ff);
        Assert.Null(some.UnsupportedReason(VideoClip("a.mov")));
        Assert.StartsWith("RED R3D", some.UnsupportedReason(new Clip { Name = "r", Path = "r.R3D", Format = ClipFormat.R3d, Segments = ["r.R3D"] }));
        Assert.StartsWith("Canon", some.UnsupportedReason(new Clip { Name = "c", Path = "c.CRM", Format = ClipFormat.CanonRaw, Segments = ["c.CRM"] }));
        Assert.Equal("unknown format", some.UnsupportedReason(new Clip { Name = "u", Path = "u", Format = ClipFormat.Unknown, Segments = ["u"] }));
    }

    [FfmpegFact]
    public void Thumbnails_ThreeJpegsWithTimecodeLabels()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.mov"), seconds: 4, timecode: "01:00:00:00");
        TestMedia.IdentityLut(Path.Combine(dir.Path, "look.cube"));
        using var r = new ProxyRenderer(new ProxySettings { LutPath = Path.Combine(dir.Path, "look.cube") }, Tools);
        var clip = VideoClip("CLIP/C0001.mov");
        var info = r.Probe(clip, card);
        var thumbs = r.Thumbnails(clip, card, info);
        Assert.Equal(3, thumbs.Count);
        Assert.All(thumbs, t => Assert.Equal(new byte[] { 0xFF, 0xD8 }, t.Jpeg[..2]));
        Assert.Equal("01:00:00:00", thumbs[0].Label);
        Assert.Equal("01:00:02:00", thumbs[1].Label);
        Assert.Equal("01:00:03:24", thumbs[2].Label);
    }

    // Review focus 5
    [FfmpegFact]
    public void Thumbnails_SingleFrameClip()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "one.mov"), seconds: 0.04, audio: false);
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        var clip = VideoClip("one.mov");
        var thumbs = r.Thumbnails(clip, card, r.Probe(clip, card));
        Assert.Single(thumbs);
    }

    // Review focus 1
    [FfmpegFact]
    public void Render_NameWithQuotesAndDiacritics()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var rel = "Záběr 'A' 01.mov";
        TestMedia.MakeClip(Path.Combine(card, rel), seconds: 2, timecode: "10:00:00:00");
        TestMedia.IdentityLut(Path.Combine(dir.Path, "look.cube"));
        var settings = new ProxySettings { Enabled = true, Height = 360, FrameAspect = 1.85, LutPath = Path.Combine(dir.Path, "look.cube") };
        using var r = new ProxyRenderer(settings, Tools);
        var clip = VideoClip(rel);
        var info = r.Probe(clip, card);
        var output = Path.Combine(card, "_PROXY", "Záběr 'A' 01.mov");
        var progress = new List<double>();
        r.Render(clip, card, info, output, f => { lock (progress) progress.Add(f); }, () => false);
        var p = FFProbe.Probe(TestMedia.Ffprobe!, output);
        Assert.Equal(360, p.Height);
        Assert.StartsWith("h264", p.Codec);
        Assert.Equal("10:00:00:00", p.Timecode);
        Assert.True(p.AudioChannels > 0);
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_PROXY"), "*.part"));
        lock (progress) Assert.NotEmpty(progress);
    }

    [FfmpegFact]
    public void Render_ProresProxy()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 1);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360, Codec = ProxyCodec.ProresProxy }, Tools);
        var clip = VideoClip("a.mov");
        var output = Path.Combine(card, "_PROXY", "a.mov");
        r.Render(clip, card, r.Probe(clip, card), output, _ => { }, () => false);
        Assert.Equal("prores", FFProbe.Probe(TestMedia.Ffprobe!, output).Codec!.Split(' ')[0]);
    }

    [FfmpegFact]
    public void Render_Cancelled_LeavesNothing()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "long.mov"), seconds: 20, width: 1920, height: 1080);
        using var r = new ProxyRenderer(new ProxySettings { Height = 1080, Codec = ProxyCodec.Prores422HQ }, Tools);
        var clip = VideoClip("long.mov");
        var output = Path.Combine(card, "_PROXY", "long.mov");
        int cancel = 0;
        var e = Assert.Throws<ToolError>(() => r.Render(clip, card, r.Probe(clip, card), output, _ => Volatile.Write(ref cancel, 1), () => Volatile.Read(ref cancel) == 1));
        Assert.Contains("cancelled", e.Message);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_PROXY")));
    }

    static FrameSource TestSrc(int seconds) => new()
    {
        InputArgs = ["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "320x180", "-r", "25", "-i", "-"],
        FeederExe = TestMedia.Ffmpeg,
        FeederArgs = ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=" + seconds, "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
        AudioFromInput = false,
    };

    static List<string> ConsumerArgs(FrameSource src, string output)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-nostats", "-progress", "pipe:1" };
        args.AddRange(src.InputArgs);
        args.AddRange(["-c:v", "libx264", "-f", "mp4", output]);
        return args;
    }

    [FfmpegFact]
    public void Feeder_RenderCompletes()
    {
        using var dir = new TempDir();
        var output = Path.Combine(dir.Path, "out.mp4");
        var src = TestSrc(2);
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        r.RunFfmpeg(TestMedia.Ffmpeg!, ConsumerArgs(src, output), src, 2, _ => { }, () => false);
        Assert.True(new FileInfo(output).Length > 0);
    }

    [FfmpegFact]
    public void Feeder_CancelRepeatedly_NoCrash()
    {
        using var dir = new TempDir();
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        for (int i = 0; i < 5; i++)
        {
            var src = TestSrc(60);
            var output = Path.Combine(dir.Path, $"out{i}.mp4");
            int cancel = 0;
            var e = Assert.Throws<ToolError>(() => r.RunFfmpeg(TestMedia.Ffmpeg!, ConsumerArgs(src, output), src, 60,
                _ => Volatile.Write(ref cancel, 1), () => Volatile.Read(ref cancel) == 1));
            Assert.Contains("cancelled", e.Message);
        }
        Thread.Sleep(300); // a late exception on a background thread would have killed the test host by now
    }

    [FfmpegFact]
    public void Feeder_Fails_ReportsFeederError()
    {
        using var dir = new TempDir();
        var output = Path.Combine(dir.Path, "out.mp4");
        var src = new FrameSource
        {
            InputArgs = ["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "320x180", "-r", "25", "-i", "-"],
            FeederExe = TestMedia.Ffmpeg,
            FeederArgs = ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", "no-such-input-file.xyz", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
            AudioFromInput = false,
        };
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        var e = Assert.Throws<ToolError>(() => r.RunFfmpeg(TestMedia.Ffmpeg!, ConsumerArgs(src, output), src, null, null, () => false));
        Assert.Contains("no-such-input-file", e.Detail);
    }
}
