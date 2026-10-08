using System.Text.Json;
using Offload.Core.Desktop;
using Offload.Core.Space;

namespace Offload.Core.Tests;

/// 0.5.1, `proxy.skipLighterSources`: an H.264/HEVC original that is no bigger than the proxy frame and no heavier than
/// the proxy's data rate gets no proxy — the proxy would only be bigger (a real card: H.264 1.3–21 Mb/s against a
/// 10 Mb/s proxy, some proxies 3.7× the original). Other codecs (ProRes, DNx, RAW, MJPEG …) always get one.
public class LighterSourceTests
{
    static Clip V(string path = "idle/1.mp4", ClipFormat format = ClipFormat.Video) =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = format, Segments = [path], Bytes = 1 };

    static ClipInfo H264(double mbps, int width = 1920, int height = 1080, string codec = "h264 High") =>
        new() { Codec = codec, Width = width, Height = height, VideoBitrate = (long)(mbps * 1e6), Duration = 10 };

    static readonly ProxySettings P = new() { Enabled = true, Height = 1080, Codec = ProxyCodec.H264, BitrateMbps = 10 };

    [Fact]
    public void Probe_VideoBitrate_FromTheStream_ElseFormatMinusAudio()
    {
        var stream = FFProbe.Parse("""
            {"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"bit_rate":"5234000"},
                        {"codec_type":"audio","codec_name":"aac","channels":2,"bit_rate":"128000"}],
             "format":{"duration":"10.0","bit_rate":"5400000"}}
            """);
        Assert.Equal(5_234_000, stream.VideoBitrate);
        // MPEG-TS (AVCHD .MTS) has no rate per stream: the container's minus the audio streams'.
        var ts = FFProbe.Parse("""
            {"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080},
                        {"codec_type":"audio","codec_name":"ac3","channels":2,"bit_rate":"256000"}],
             "format":{"duration":"10.0","bit_rate":"17256000"}}
            """);
        Assert.Equal(17_000_000, ts.VideoBitrate);
        Assert.Null(FFProbe.Parse("""{"streams":[{"codec_type":"video","codec_name":"h264"}],"format":{}}""").VideoBitrate);
    }

    [Fact]
    public void Lighter_H264AndHevc_AtMostTheProxyRateAndFrame()
    {
        Assert.Equal("bez proxy — originál je lehčí (5,2 Mb/s ≤ 10 Mb/s)", LighterSource.Note(V(), H264(5.2), P));
        Assert.Equal("bez proxy — originál je lehčí (10 Mb/s ≤ 10 Mb/s)", LighterSource.Note(V(), H264(10), P));   // equal is lighter
        Assert.Equal("bez proxy — originál je lehčí (1,3 Mb/s ≤ 10 Mb/s)", LighterSource.Note(V(), H264(1.3, 1280, 720, "hevc Main 10"), P));
        Assert.Null(LighterSource.Note(V(), H264(10.1), P));                                     // heavier
        Assert.Equal(Msg.ProxyLighter(5.2, 10), LighterSource.Note(V(), H264(5.2), P));
    }

    // Ruling (0.5.1): an original bigger than the proxy frame in either axis always gets a proxy, however low its data
    // rate — decoding 4K long-GOP H.264/HEVC is what makes the timeline stutter, and the proxy is the edit size.
    [Fact]
    public void BiggerThanTheProxyFrame_AlwaysGetsOne()
    {
        Assert.Null(LighterSource.Note(V(), H264(5, 3840, 2160), P));
        Assert.Null(LighterSource.Note(V(), H264(5, 1920, 1081), P));
        Assert.Null(LighterSource.Note(V(), H264(5, 2048, 1080), P));                            // wider than 16:9 at 1080
        Assert.Null(LighterSource.Note(V(), H264(5, 1080, 1920), P));                            // portrait phone clip
        Assert.Null(LighterSource.Note(V(), H264(2, 1920, 1080), new ProxySettings { Enabled = true, Height = 720, BitrateMbps = 10 }));
        Assert.NotNull(LighterSource.Note(V(), H264(2, 1280, 720), new ProxySettings { Enabled = true, Height = 720, BitrateMbps = 10 }));
    }

    [Theory]
    [InlineData("prores HQ")]
    [InlineData("dnxhd DNXHR SQ")]
    [InlineData("mjpeg Baseline")]
    [InlineData("mpeg2video 4:2:2")]
    [InlineData(null)]
    public void OtherCodecs_AlwaysGetOne(string? codec) => Assert.Null(LighterSource.Note(V(), H264(2, codec: codec!), P));

    [Fact]
    public void Raw_UnknownRateOrSize_OrSwitchedOff_GetOne()
    {
        Assert.Null(LighterSource.Note(V("A.braw", ClipFormat.Braw), H264(2), P));
        Assert.Null(LighterSource.Note(V(), new ClipInfo { Codec = "h264 High", Width = 1920, Height = 1080 }, P));   // rate unknown
        Assert.Null(LighterSource.Note(V(), new ClipInfo { Codec = "h264 High", VideoBitrate = 1_000_000 }, P));     // size unknown
        Assert.Null(LighterSource.Note(V(), H264(2), new ProxySettings { Enabled = true, SkipLighterSources = false }));
    }

    // Controller ruling (0.5.1): only a long-GOP proxy (H.264/HEVC) is skipped. A ProRes proxy was picked for smooth
    // editing — a lighter long-GOP original still gets it, at any rate.
    [Theory]
    [InlineData(ProxyCodec.ProresProxy)]
    [InlineData(ProxyCodec.ProresLT)]
    [InlineData(ProxyCodec.Prores422)]
    [InlineData(ProxyCodec.Prores422HQ)]
    public void ProResProxy_AlwaysRendered(ProxyCodec codec)
    {
        var prores = new ProxySettings { Enabled = true, Height = 1080, Codec = codec, BitrateMbps = 10 };
        Assert.Null(LighterSource.Note(V(), H264(1.3), prores));
        Assert.Null(LighterSource.Note(V(), H264(1.3, codec: "hevc Main"), prores));
        Assert.True(SpaceEstimator.ProxyBytes([V()], prores, _ => true, _ => H264(1.3)) > 0);
        Assert.Equal("bez proxy — originál je lehčí (1,3 Mb/s ≤ 10 Mb/s)",
            LighterSource.Note(V(), H264(1.3), new ProxySettings { Enabled = true, Codec = ProxyCodec.Hevc, BitrateMbps = 10 }));
    }

    // The space check counts no proxy for such a clip (after the analysis: before it, the rate is not known).
    [Fact]
    public void Space_NoProxyForALighterOriginal()
    {
        var clip = V();
        Assert.Equal(0, SpaceEstimator.ProxyBytes([clip], P, _ => true, _ => H264(5)));
        Assert.True(SpaceEstimator.ProxyBytes([clip], P, _ => true, _ => H264(15)) > 0);
        Assert.True(SpaceEstimator.ProxyBytes([clip], new ProxySettings { Enabled = true, SkipLighterSources = false }, _ => true, _ => H264(5)) > 0);
        Assert.True(SpaceEstimator.ProxyBytes([clip], P, _ => true) > 0);
    }

    [Fact]
    public void Setting_DefaultOn_OlderFilesOn_WrittenToReportAndSettings()
    {
        Assert.True(new ProxySettings().SkipLighterSources);
        Assert.True(JsonSerializer.Deserialize<ProxySettings>("""{"enabled":true}""")!.SkipLighterSources);
        Assert.False(JsonSerializer.Deserialize<ProxySettings>("""{"skipLighterSources":false}""")!.SkipLighterSources);
        Assert.Contains("\"skipLighterSources\":true", JsonSerializer.Serialize(new ProxySettings()));
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{"proxy":{"enabled":true}}""");
        var s = SettingsStore.Load(path);
        Assert.True(s.Proxy.SkipLighterSources);
        s.Proxy.SkipLighterSources = false;
        SettingsStore.Save(s, path);
        Assert.False(SettingsStore.Load(path).Proxy.SkipLighterSources);
    }

    static ClipEntry Lighter() => new() { Clip = V(), ProxyStatus = ProxyStatus.Skipped, ProxyNote = Msg.ProxyLighter(5.2, 10) };

    [Fact]
    public void Entry_KnowsItsOriginalIsLighter()
    {
        Assert.True(Lighter().OriginalLighter);
        Assert.False(new ClipEntry { Clip = V(), ProxyStatus = ProxyStatus.Skipped }.OriginalLighter);
        Assert.False(new ClipEntry { Clip = V(), ProxyStatus = ProxyStatus.Skipped, ProxyNote = "Na disku není místo" }.OriginalLighter);
    }

    // Neutral, not a failure: "bez proxy" in grey, the reason as a grey note (not red).
    [Fact]
    public void Html_NeutralStatusAndNote()
    {
        var r = Reports.Sample(@"D:\2026-10-08\A001");
        r.Clips.Add(Lighter());
        var html = HtmlReport.Render(r);
        Assert.Contains("<td class=\"st muted\">bez proxy</td>", html);
        Assert.Contains("<div class=\"note\">bez proxy — originál je lehčí (5,2 Mb/s ≤ 10 Mb/s)</div>", html);
        Assert.DoesNotContain("<div class=\"path err\">bez proxy", html);
    }

    // Job card "Proxy x / y" and the summary line: such a clip is not one the proxies still owe.
    [Fact]
    public void Summaries_CountItApart()
    {
        var r = Reports.Sample(@"D:\2026-10-08\A001");
        r.Proxy = new ProxySettings { Enabled = true };
        r.Clips.Add(new ClipEntry { Clip = V("a.mov"), ProxyStatus = ProxyStatus.Done });
        r.Clips.Add(Lighter());
        Assert.Equal("1 / 1", JobCard.Summary(r, TimeSpan.FromSeconds(1)).Single(f => f.Label == "Proxy").Value);
        Assert.EndsWith("proxy 1, 1 bez proxy (lehčí originál)", JobProgress.SummaryText(r));
    }

    [Fact]
    public void DayReport_ProxyColumnSaysHowManyHaveNone()
    {
        using var dir = new TempDir();
        var card = dir.Sub("D", "2026-10-08", "A001");
        var r = Reports.Sample(card);
        r.Clips = [new ClipEntry { Clip = V("a.mov"), ProxyStatus = ProxyStatus.Done }, Lighter()];
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), r.ToJson());

        var day = Offload.Core.Outputs.DayReport.Load(Path.GetDirectoryName(card)!);

        Assert.Equal(1, day.Cards[0].ProxiesLighter);
        Assert.Contains("<td>1 / 0 · 1 bez proxy</td>", Offload.Core.Outputs.DayReport.Html(day));
    }
}

/// The whole job: no proxy for the lighter original (thumbnails still made, the status neutral), a proxy for the bigger
/// one; `--proxy-always` / SkipLighterSources = false renders every one.
public class LighterSourceJobTests
{
    static OffloadOptions Options() => new() { ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264" };

    [FfmpegFact]
    public void Job_LighterOriginal_NoProxy_ThumbnailsStill()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "idle", "small.mp4"), seconds: 1, width: 1280, height: 720);
        TestMedia.MakeClip(Path.Combine(card, "idle", "big.mp4"), seconds: 1, width: 1920, height: 1080);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Date = "2026-10-08",
            Proxy = new ProxySettings { Enabled = true, Height = 720, BitrateMbps = 40 },
        }, Options());
        var log = new List<string>();

        var report = job.Run(log.Add);

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.Null(s.Warning);
        Assert.Equal((1, 0), (s.ProxiesDone, s.ProxiesFailed));
        var small = report!.Clips.Single(c => c.Clip.Name == "small");
        Assert.Equal(ProxyStatus.Skipped, small.ProxyStatus);
        Assert.StartsWith("bez proxy — originál je lehčí (", small.ProxyNote);
        Assert.EndsWith(" Mb/s ≤ 40 Mb/s)", small.ProxyNote);
        Assert.True(small.OriginalLighter);
        Assert.Null(small.ProxyPath);
        Assert.Equal(3, small.Thumbnails.Count);
        var cardPath = job.Plan!.CardPaths[0];
        Assert.False(File.Exists(Path.Combine(cardPath, "_PROXY", "idle", "small.mov")));
        Assert.Equal(ProxyStatus.Done, report.Clips.Single(c => c.Clip.Name == "big").ProxyStatus);
        Assert.True(File.Exists(Path.Combine(cardPath, "_PROXY", "idle", "big.mov")));
        Assert.Contains("\"skipLighterSources\": true", File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json")));

        var always = new OffloadJob(new JobConfig(card, [dir.Sub("d2")])
        {
            Date = "2026-10-08",
            AnalyzeClips = false,
            Proxy = new ProxySettings { Enabled = true, Height = 720, BitrateMbps = 40, SkipLighterSources = false },
        }, Options());
        var all = always.Run();
        Assert.All(all!.Clips, c => Assert.Equal(ProxyStatus.Done, c.ProxyStatus));
    }
}

/// `--proxy-always`: the CLI's way to switch skipLighterSources off (also over a project's setting).
[Collection("Console")]
public class LighterSourceCliTests
{
    static JobConfig? Run(params string[] args)
    {
        JobConfig? seen = null;
        var o = Console.Out;
        var e = Console.Error;
        var old = Offload.Cli.CopyCommand.JobFactory;
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        Offload.Cli.CopyCommand.JobFactory = c => { seen = c; return new OffloadJob(c, AppendFixtures.Options()); };
        try { Offload.Cli.CopyCommand.Create().Parse(args).Invoke(); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            Offload.Cli.CopyCommand.JobFactory = old;
        }
        return seen;
    }

    [Fact]
    public void ProxyAlways_SwitchesTheSkipOff()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        File.WriteAllText(Path.Combine(card, "notes.txt"), "x");
        string[] common = [card, "-d", dir.Sub("d1"), "--date", "2026-10-08", "--no-history", "--no-day-report", "--proxy"];

        Assert.True(Run(common)!.Proxy.SkipLighterSources);
        Assert.False(Run([.. common, "--proxy-always"])!.Proxy.SkipLighterSources);
    }
}
