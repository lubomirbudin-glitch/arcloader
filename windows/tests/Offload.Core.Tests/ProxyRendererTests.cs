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
        Assert.Equal("ffmpeg nenalezen", none.UnsupportedReason(VideoClip("a.mov")));
        var ff = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true }.Resolved();
        using var some = new ProxyRenderer(new ProxySettings(), ff);
        Assert.Null(some.UnsupportedReason(VideoClip("a.mov")));
        Assert.StartsWith("RED R3D", some.UnsupportedReason(new Clip { Name = "r", Path = "r.R3D", Format = ClipFormat.R3d, Segments = ["r.R3D"] }));
        Assert.StartsWith("Canon", some.UnsupportedReason(new Clip { Name = "c", Path = "c.CRM", Format = ClipFormat.CanonRaw, Segments = ["c.CRM"] }));
        Assert.Equal("neznámý formát", some.UnsupportedReason(new Clip { Name = "u", Path = "u", Format = ClipFormat.Unknown, Segments = ["u"] }));
    }

    // Review focus 3
    [Fact]
    public void FullRangeSource_PreMapped()
    {
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le," + FilterGraph.PreMap + ",lut3d=file=lut-a.cube:interp=tetrahedral,scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p",
            FilterGraph.Thumbnail("lut-a.cube", premap: true));
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le,lut3d=file=look.cube:interp=tetrahedral,scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p",
            FilterGraph.Thumbnail("look.cube", premap: false));
        Assert.Equal("scale=-2:180:flags=bicubic,scale=out_color_matrix=bt601:out_range=pc,format=yuvj420p", FilterGraph.Thumbnail(null, premap: true));
        Assert.Contains("(val*1023/65535-64)/876*65535", FilterGraph.PreMap);
    }

    [Fact]
    public void NeedsPreMap_OnlyBuiltinLutOnFullRangeSource()
    {
        var builtin = new LutUse("auto", "bmd-film-gen5", "n", "r") { File = "a.cube" };
        var braw = new FrameSource { InputArgs = ["-i", "-"], FullRange = true };
        var video = new FrameSource { InputArgs = ["-i", "a.mov"] };
        Assert.True(ProxyRenderer.NeedsPreMap(builtin, braw, new ClipInfo()));
        Assert.True(ProxyRenderer.NeedsPreMap(builtin, video, new ClipInfo { ColorRange = "pc" }));
        Assert.False(ProxyRenderer.NeedsPreMap(builtin, video, new ClipInfo { ColorRange = "tv" }));
        Assert.False(ProxyRenderer.NeedsPreMap(builtin, video, new ClipInfo()));
        Assert.False(ProxyRenderer.NeedsPreMap(CustomLut("look.cube"), braw, new ClipInfo()));
        Assert.False(ProxyRenderer.NeedsPreMap(null, braw, new ClipInfo { ColorRange = "pc" }));
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

        // Grey maps almost onto itself, so also check a level the LUT clearly moves: S-Log3 Y=150 (code ≈ 0.61
        // without a LUT) comes out well above 0.75 through it. Fails if the LUT is dropped.
        var brightPath = Path.Combine(clipDir, "C0002.MP4");
        RunFfmpeg("-f", "lavfi", "-i", "color=c=0x969696:s=320x180:d=1:r=25", "-vf", "format=yuv420p,lutyuv=y=150:u=128:v=128",
            "-c:v", "libx264", "-color_range", "tv", brightPath);
        var bright = new Clip { Name = "C0002", Path = "XDROOT/Clip/C0002.MP4", Format = ClipFormat.Video, Segments = ["XDROOT/Clip/C0002.MP4"], Bytes = 1 };
        var brightOut = Path.Combine(dir.Path, "p2.mov");
        r.Render(bright, card, r.Probe(bright, card), lut, brightOut, _ => { }, () => false);
        Assert.True((MeanLuma(brightOut) - 16) / 219.0 >= 0.75);
        var plainOut = Path.Combine(dir.Path, "p3.mov");
        r.Render(bright, card, r.Probe(bright, card), null, plainOut, _ => { }, () => false);
        Assert.InRange((MeanLuma(plainOut) - 16) / 219.0, 0.58, 0.64);
    }

    /// RGB of a flat first frame, decoded with the given YCbCr matrix and range.
    static (int R, int G, int B) FlatRgb(string file, string matrix, string range)
    {
        var raw = Path.Combine(Path.GetDirectoryName(file)!, $"rgb-{Guid.NewGuid():N}.raw");
        ToolProcess.Run(TestMedia.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", file, "-frames:v", "1",
            "-vf", $"scale=in_color_matrix={matrix}:in_range={range},format=rgb24", "-f", "rawvideo", raw]);
        var b = File.ReadAllBytes(raw);
        int i = b.Length / 6 * 3;  // a pixel in the middle
        return (b[i], b[i + 1], b[i + 2]);
    }

    // Final review 8 — real ffmpeg: pure red through the proxy path (RGB source like the BRAW decoder, and BT.709
    // video; with and without a LUT; H.264 and ProRes) is converted with the BT.709 matrix to legal range and tagged
    // BT.709, so a player decoding it as BT.709 sees red. The BT.601 matrix would leave G ≈ 23.
    [FfmpegFact]
    public void Proxy_Red_Bt709MatrixAndTags()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        RunFfmpeg("-f", "lavfi", "-i", "color=c=red:s=320x180:d=1:r=25", "-pix_fmt", "rgb24", "-c:v", "png", Path.Combine(card, "rgb.mov"));
        RunFfmpeg("-f", "lavfi", "-i", "color=c=red:s=320x180:d=1:r=25", "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p",
            "-c:v", "libx264", "-colorspace", "bt709", "-color_range", "tv", Path.Combine(card, "yuv.mov"));
        var cube = Path.Combine(dir.Path, "look.cube");
        TestMedia.IdentityLut(cube);
        var failures = new List<string>();
        foreach (var codec in new[] { ProxyCodec.H264, ProxyCodec.ProresLT })
        foreach (var name in new[] { "rgb.mov", "yuv.mov" })
        foreach (var withLut in new[] { false, true })
        {
            var settings = new ProxySettings { Enabled = true, Height = 180, Codec = codec, BurnFilename = false, BurnTimecode = false,
                LutMode = withLut ? LutMode.Custom : LutMode.None, LutPath = withLut ? cube : null };
            using var r = new ProxyRenderer(settings, Tools);
            var clip = VideoClip(name);
            var outPath = Path.Combine(dir.Path, $"{codec}-{name}-{withLut}.mov");
            r.Render(clip, card, r.Probe(clip, card), withLut ? CustomLut(cube) : null, outPath, _ => { }, () => false);
            var tags = string.Join(" ", ToolProcess.Run(TestMedia.Ffprobe!, ["-v", "error", "-select_streams", "v:0", "-show_entries",
                "stream=color_space,color_transfer,color_primaries,color_range", "-of", "default=nw=1", outPath])
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            var (red, green, blue) = FlatRgb(outPath, "bt709", "tv");
            if (tags != "color_range=tv color_space=bt709 color_transfer=bt709 color_primaries=bt709" || red < 240 || green > 8 || blue > 8)
                failures.Add($"{codec} {name} lut={withLut}: [{tags}] rgb=({red},{green},{blue})");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // Final review 8 — JPEG (JFIF) is BT.601 full range for every browser and PDF viewer: a BT.709 red clip's
    // thumbnail must decode as red that way (keeping the BT.709 matrix gives R ≈ 230).
    [FfmpegFact]
    public void Thumbnail_Red_JfifBt601()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        RunFfmpeg("-f", "lavfi", "-i", "color=c=red:s=320x180:d=1:r=25", "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p",
            "-c:v", "libx264", "-colorspace", "bt709", "-color_range", "tv", Path.Combine(card, "yuv.mov"));
        RunFfmpeg("-f", "lavfi", "-i", "color=c=red:s=320x180:d=1:r=25", "-pix_fmt", "rgb24", "-c:v", "png", Path.Combine(card, "rgb.mov"));
        var cube = Path.Combine(dir.Path, "look.cube");
        TestMedia.IdentityLut(cube);
        using var r = new ProxyRenderer(new ProxySettings { LutMode = LutMode.Custom, LutPath = cube }, Tools);
        foreach (var name in new[] { "yuv.mov", "rgb.mov" })
        foreach (var lut in new[] { null, CustomLut(cube) })
        {
            var clip = VideoClip(name);
            var thumbs = r.Thumbnails(clip, card, r.Probe(clip, card), lut);
            var jpg = Path.Combine(dir.Path, $"t-{name}-{lut != null}.jpg");
            File.WriteAllBytes(jpg, thumbs[0].Jpeg);
            var (red, green, blue) = FlatRgb(jpg, "bt601", "pc");
            Assert.True(red >= 242 && green <= 8 && blue <= 8, $"{name} lut={lut != null}: rgb=({red},{green},{blue})");
        }
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

    // Spec 3.4: the proxy relinks — start TC and reel name on the timecode track, every audio stream of the source.
    [FfmpegFact]
    public void Render_ReelNameOnTimecodeTrack_EveryAudioStream()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 1, timecode: "01:00:00:00", audioStreams: 2);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360 }, Tools) { ForcedEncoder = "libx264" };
        var clip = VideoClip("a.mov");
        var info = r.Probe(clip, card);
        Assert.Equal([1, 1], info.AudioStreams);
        info.Reel = "A001";
        var output = Path.Combine(card, "_PROXY", "a.mov");

        r.Render(clip, card, info, null, output, _ => { }, () => false);

        var streams = TestMedia.Streams(output);
        var tmcd = Assert.Single(streams, s => s.Type == "data");
        Assert.Equal(("A001", "01:00:00:00"), (tmcd.Tags["reel_name"], tmcd.Tags["timecode"]));
        Assert.Equal([("aac", 1), ("aac", 1)], streams.Where(s => s.Type == "audio").Select(s => (s.Codec, s.Channels)));
        Assert.Equal("A001", FFProbe.Probe(TestMedia.Ffprobe!, output).Reel);
    }

    // Review focus 5: a Sony MXF with 8 mono tracks — the ProRes proxy keeps all 8 (PCM) and gets the reel.
    [FfmpegFact]
    public void Render_SonyMxf_EightMonoTracks()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeMxf(Path.Combine(card, "C0001.MXF"), monoTracks: 8);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360, Codec = ProxyCodec.ProresProxy }, Tools);
        var clip = VideoClip("C0001.MXF");
        var info = r.Probe(clip, card);
        Assert.Equal(8, info.AudioStreams.Count);
        info.Reel = "A001";
        var output = Path.Combine(card, "_PROXY", "C0001.mov");

        r.Render(clip, card, info, null, output, _ => { }, () => false);

        var streams = TestMedia.Streams(output);
        Assert.Equal(Enumerable.Repeat<(string?, int)>(("pcm_s16le", 1), 8), streams.Where(s => s.Type == "audio").Select(s => (s.Codec, s.Channels)));
        var tmcd = Assert.Single(streams, s => s.Type == "data");
        Assert.Equal(("A001", "10:00:00:00"), (tmcd.Tags["reel_name"], tmcd.Tags["timecode"]));
    }

    // Spec 3.4: without timecode there is no timecode track — and so no reel name (Resolve relinks by name).
    [FfmpegFact]
    public void Render_NoTimecode_NoTimecodeTrackNoReel()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 1, timecode: null);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360 }, Tools) { ForcedEncoder = "libx264" };
        var clip = VideoClip("a.mov");
        var info = r.Probe(clip, card);
        Assert.Null(info.Timecode);
        info.Reel = "A001";
        var output = Path.Combine(card, "_PROXY", "a.mov");

        r.Render(clip, card, info, null, output, _ => { }, () => false);

        var streams = TestMedia.Streams(output);
        Assert.DoesNotContain(streams, s => s.Type == "data");
        Assert.DoesNotContain(streams, s => s.Tags.ContainsKey("reel_name"));
        Assert.Single(streams, s => s.Type == "audio");
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
