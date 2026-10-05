namespace Offload.Core.Tests;

public class ProxyRendererTests
{
    static ExternalTools Tools => new ExternalTools().Resolved();
    static LutUse CustomLut(string path) => new("custom", null, "look.cube", "vlastní LUT") { File = path };
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
    public void SeekSeconds_MiddleHalfFrameEarly_LastOneSecondBefore()
    {
        var info = new ClipInfo { FrameRate = "24000/1001", Duration = 2.085417, Frames = 50 };
        var pos = ProxyRenderer.FramePositions(info);
        Assert.Equal(["0.000000", "1.021854", "1.043708"], pos.Select(p => ProxyRenderer.SeekSeconds(info, p)));
        var shortClip = new ClipInfo { FrameRate = "25/1", Duration = 0.12, Frames = 3 };
        Assert.Equal(["0.000000", "0.020000", "0.000000"], ProxyRenderer.FramePositions(shortClip).Select(p => ProxyRenderer.SeekSeconds(shortClip, p)));
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

    // Review focus 3
    [Fact]
    public void FullRangeSource_PreMapped()
    {
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le," + FilterGraph.PreMap + ",lut3d=file=lut-a.cube:interp=tetrahedral,format=yuvj420p",
            FilterGraph.Thumbnail("lut-a.cube", premap: true));
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le,lut3d=file=look.cube:interp=tetrahedral,format=yuvj420p",
            FilterGraph.Thumbnail("look.cube", premap: false));
        Assert.Equal("scale=-2:180:flags=bicubic,format=yuvj420p", FilterGraph.Thumbnail(null, premap: true));
        Assert.Contains("(val*1023/65535-64)/876*65535", FilterGraph.PreMap);
    }

    [Fact]
    public void LookFor_LogWhenAnyLut_ArriSpaceFromBuiltin()
    {
        var info = new ClipInfo { Camera = "ALEXA Mini" };
        Assert.Equal(new RawLook(false, null), ProxyRenderer.LookFor(null, info));
        Assert.Equal(new RawLook(false, null), ProxyRenderer.LookFor(LutUse.None("x"), info));
        Assert.Equal(new RawLook(true, "AWG4/D65/LogC4"), ProxyRenderer.LookFor(new LutUse("auto", "arri-logc4-awg4", "n", "r") { File = "a.cube" }, info));
        Assert.Equal(new RawLook(true, "AWG3/D65/LogC3"), ProxyRenderer.LookFor(new LutUse("auto", "arri-logc3-awg3", "n", "r") { File = "a.cube" }, info));
        Assert.Equal(new RawLook(true, null), ProxyRenderer.LookFor(new LutUse("auto", "red-log3g10-rwg", "n", "r") { File = "a.cube" }, info));
        Assert.Equal(new RawLook(true, "AWG4/D65/LogC4"), ProxyRenderer.LookFor(CustomLut(@"C:\l\show_LogC4_709.cube"), info));
        Assert.Equal(new RawLook(true, "AWG3/D65/LogC3"), ProxyRenderer.LookFor(CustomLut(@"C:\l\look.cube"), info));
    }

    static void RunFfmpeg(params string[] args) =>
        ToolProcess.Run(TestMedia.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", .. args]);

    /// Mean Y (8-bit code values, as stored) of the first frame, via signalstats.
    static double MeanLuma(string video)
    {
        var dir = Path.GetDirectoryName(video)!;
        var stats = $"yavg-{Guid.NewGuid():N}.txt";
        ToolProcess.Run(TestMedia.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", video,
            "-vf", $"signalstats,metadata=print:key=lavfi.signalstats.YAVG:file={stats}", "-frames:v", "1", "-f", "null", "-"], dir);
        var line = File.ReadAllLines(Path.Combine(dir, stats)).First(l => l.StartsWith("lavfi.signalstats.YAVG=", StringComparison.Ordinal));
        return double.Parse(line["lavfi.signalstats.YAVG=".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    // Review focus 3 — real ffmpeg: an S-Log3 grey clip with its Sony XML renders a proxy whose grey is ~0.42.
    [FfmpegFact]
    public void Proxy_SLog3Grey_Is042()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var clipDir = Path.Combine(card, "XDROOT", "Clip");
        Directory.CreateDirectory(clipDir);
        var clipPath = Path.Combine(clipDir, "C0001.MP4");
        // S-Log3 grey = 10-bit code 420 → 8-bit legal Y ≈ 105; neutral chroma.
        RunFfmpeg("-f", "lavfi", "-i", "color=c=0x696969:s=320x180:d=1:r=25", "-vf", "format=yuv420p,lutyuv=y=105:u=128:v=128",
            "-c:v", "libx264", "-color_range", "tv", clipPath);
        File.WriteAllText(Path.Combine(clipDir, "C0001M01.XML"),
            """<NonRealTimeMeta><Item name="CaptureGammaEquation" value="s-log3-cine"/><Item name="CaptureColorPrimaries" value="S-Gamut3Cine"/></NonRealTimeMeta>""");
        var settings = new ProxySettings { Enabled = true, Height = 180, LutMode = LutMode.Auto, BurnFilename = false, BurnTimecode = false };
        using var r = new ProxyRenderer(settings, Tools);
        var clip = new Clip { Name = "C0001", Path = "XDROOT/Clip/C0001.MP4", Format = ClipFormat.Video, Segments = ["XDROOT/Clip/C0001.MP4"], Bytes = 1 };
        var info = r.Probe(clip, card);
        var lut = LutResolver.Resolve(clip, info, clipPath, settings, LutCatalog.Default);
        Assert.Equal("sony-slog3-sgamut3cine", lut.Id);
        var outPath = Path.Combine(dir.Path, "p.mov");
        r.Render(clip, card, info, lut, outPath, _ => { }, () => false);
        var y = MeanLuma(outPath);
        var code = (y - 16) / 219.0;
        Assert.InRange(code, 0.39, 0.45);
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
        var thumbs = r.Thumbnails(clip, card, info, CustomLut(Path.Combine(dir.Path, "look.cube")));
        Assert.Equal(3, thumbs.Count);
        Assert.All(thumbs, t => Assert.Equal(new byte[] { 0xFF, 0xD8 }, t.Jpeg[..2]));
        Assert.Equal("01:00:00:00", thumbs[0].Label);
        Assert.Equal("01:00:02:00", thumbs[1].Label);
        Assert.Equal("01:00:03:24", thumbs[2].Label);
    }

    /// Mean grey level of a JPEG, decoded by ffmpeg.
    static double MeanGrey(string ffmpeg, string jpeg, string workDir)
    {
        var raw = Path.Combine(workDir, $"grey-{Guid.NewGuid():N}.raw");
        ToolProcess.Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", jpeg, "-f", "rawvideo", "-pix_fmt", "gray", raw]);
        var bytes = File.ReadAllBytes(raw);
        return bytes.Average(b => (double)b);
    }

    // Final review A1
    [FfmpegFact]
    public void Thumbnails_MiddleFrameMatchesCaption_24fps()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var file = Path.Combine(card, "ramp.mov");
        var ffmpeg = TestMedia.Ffmpeg!;
        // Every frame is one flat grey level, 8 steps above the previous one: a frame off is plain to see.
        ToolProcess.Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24",
            "-vf", "geq=lum='mod(N*8,256)':cb=128:cr=128", "-frames:v", "50", "-c:v", "libx264", "-pix_fmt", "yuv420p", file]);
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        var clip = VideoClip("ramp.mov");
        var info = r.Probe(clip, card);
        var middle = ProxyRenderer.FramePositions(info)[1];
        Assert.Equal(25, middle.Frame);
        var thumbs = r.Thumbnails(clip, card, info, null);
        Assert.Equal(3, thumbs.Count);
        var thumb = Path.Combine(dir.Path, "thumb.jpg");
        File.WriteAllBytes(thumb, thumbs[1].Jpeg);
        double actual = MeanGrey(ffmpeg, thumb, dir.Path);

        // Decode-exact reference: the frame picked by number, through the same thumbnail filter.
        double Reference(long n)
        {
            var jpg = Path.Combine(dir.Path, $"ref-{n}.jpg");
            ToolProcess.Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file,
                "-vf", $"select='eq(n,{n})',{FilterGraph.Thumbnail(null, premap: false)}", "-frames:v", "1", "-q:v", "5", jpg]);
            return MeanGrey(ffmpeg, jpg, dir.Path);
        }
        double exact = Reference(25), next = Reference(26), previous = Reference(24);
        Assert.True(Math.Abs(actual - exact) < 2, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"thumbnail grey {actual:0.0}, frame 25 {exact:0.0}, frame 26 {next:0.0}, frame 24 {previous:0.0}"));
    }

    // Final review A3
    [FfmpegFact]
    public void Thumbnails_AudioLongerThanVideo_StillThree()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var file = Path.Combine(card, "long-audio.mov");
        ToolProcess.Run(TestMedia.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=4", "-f", "lavfi", "-i", "sine=d=6",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", file]);
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        var clip = VideoClip("long-audio.mov");
        var info = r.Probe(clip, card);
        var thumbs = r.Thumbnails(clip, card, info, null);
        Assert.Equal(3, thumbs.Count);
        Assert.Equal(ProxyRenderer.Label(info, ProxyRenderer.FramePositions(info)[2]), thumbs[2].Label);
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
        var thumbs = r.Thumbnails(clip, card, r.Probe(clip, card), null);
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
        r.Render(clip, card, info, CustomLut(Path.Combine(dir.Path, "look.cube")), output, f => { lock (progress) progress.Add(f); }, () => false);
        var p = FFProbe.Probe(TestMedia.Ffprobe!, output);
        Assert.Equal(360, p.Height);
        Assert.StartsWith("h264", p.Codec);
        Assert.Equal("10:00:00:00", p.Timecode);
        Assert.True(p.AudioChannels > 0);
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_PROXY"), "*.part"));
        lock (progress) Assert.NotEmpty(progress);

        // Final review A2: "%" would be a drawtext expansion ("Stray %") without expansion=none.
        var percent = "Take 100% A.mov";
        TestMedia.MakeClip(Path.Combine(card, percent), seconds: 1, timecode: "10:00:00:00");
        var percentClip = VideoClip(percent);
        var percentOut = Path.Combine(card, "_PROXY", percent);
        r.Render(percentClip, card, r.Probe(percentClip, card), CustomLut(Path.Combine(dir.Path, "look.cube")), percentOut, _ => { }, () => false);
        Assert.True(new FileInfo(percentOut).Length > 0);
    }

    // Final review C1
    [FfmpegFact]
    public void Render_Hevc()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 1);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360, Codec = ProxyCodec.Hevc }, Tools);
        var clip = VideoClip("a.mov");
        var output = Path.Combine(card, "_PROXY", "a.mov");
        r.Render(clip, card, r.Probe(clip, card), null, output, _ => { }, () => false);
        Assert.Equal("hevc", FFProbe.Probe(TestMedia.Ffprobe!, output).Codec!.Split(' ')[0]);
        var tag = ToolProcess.Run(TestMedia.Ffprobe!, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_tag_string", "-of", "csv=p=0", output]);
        Assert.Equal("hvc1", tag.Trim());
    }

    // Final review A4
    [FfmpegFact]
    public void Thumbnails_Cancelled_NoneAndNoError()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 2);
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        var clip = VideoClip("a.mov");
        var info = r.Probe(clip, card);
        Assert.Empty(r.Thumbnails(clip, card, info, null, isCancelled: () => true));
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
        r.Render(clip, card, r.Probe(clip, card), null, output, _ => { }, () => false);
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
        var e = Assert.Throws<ToolError>(() => r.Render(clip, card, r.Probe(clip, card), null, output, _ => Volatile.Write(ref cancel, 1), () => Volatile.Read(ref cancel) == 1));
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
