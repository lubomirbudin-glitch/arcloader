using Offload.Core.Space;

namespace Offload.Core.Tests;

public class SpaceEstimatorTests
{
    static Clip C(string path, long bytes, ClipFormat format = ClipFormat.Video, bool cameraProxy = false) =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = format, Segments = [path], Bytes = bytes, IsCameraProxy = cameraProxy };

    static readonly ExternalTools All = new() { Ffmpeg = "f", Ffprobe = "p", BrawDecoder = "b", Redline = "r", ArtCmd = "a" };

    [Fact]
    public void CardAndReport_Formulas()
    {
        Assert.Equal(1000 + 2 * 65536 + 67_108_864, SpaceEstimator.CardBytes(1000, 2));
        Assert.Equal((32L << 20) + 3 * (1L << 20), SpaceEstimator.ReportBytes(3));
    }

    [Theory]
    [InlineData("A.MOV", ClipFormat.Video, 50)]
    [InlineData("A.mp4", ClipFormat.Video, 50)]
    [InlineData("A.MXF", ClipFormat.Video, 100)]
    [InlineData("A.braw", ClipFormat.Braw, 150)]
    [InlineData("A.R3D", ClipFormat.R3d, 200)]
    [InlineData("A.CRM", ClipFormat.CanonRaw, 500)]
    [InlineData("A", ClipFormat.Arriraw, 1000)]
    public void SourceRate_Table(string path, ClipFormat format, double mbps) => Assert.Equal(mbps, SpaceEstimator.SourceMbps(C(path, 1, format)));

    [Fact]
    public void Proxy_H264_FromBytesBeforeTheCopy()
    {
        var p = new ProxySettings { Enabled = true, Codec = ProxyCodec.H264, BitrateMbps = 10 };
        // 625 MB of MOV at 50 Mb/s = 100 s; proxy 10 × 1.1 + 0.128 (one assumed stereo AAC stream) = 11.128 Mb/s → 139.1 MB
        Assert.Equal(139_100_000, SpaceEstimator.ProxyBytes([C("A.MOV", 625_000_000)], p, _ => true));
        p.Audio = false;
        Assert.Equal(137_500_000, SpaceEstimator.ProxyBytes([C("A.MOV", 625_000_000)], p, _ => true));
    }

    [Fact]
    public void Proxy_ProRes_ScaledByHeight_AfterAnalysis()
    {
        var p = new ProxySettings { Enabled = true, Codec = ProxyCodec.ProresProxy, Height = 540 };
        var clip = C("A.MOV", 1);
        var info = new ClipInfo { Duration = 80, Height = 2160, AudioChannels = 2 };
        // 45 Mb/s × (540/1080)² = 11.25 + 2 ch × 0.768 PCM (ruling M6) = 12.786 Mb/s × 80 s = 127.86 MB
        Assert.Equal(127_860_000, SpaceEstimator.ProxyBytes([clip], p, _ => true, _ => info));
        Assert.Equal(220, SpaceEstimator.ProxyMbps(new ProxySettings { Codec = ProxyCodec.Prores422HQ }, 0, 1080));
    }

    // R14: every audio stream becomes a proxy track — 8 mono tracks of a Sony MXF are 8 x 0.2 Mb/s.
    [Fact]
    public void Proxy_H264_EveryAudioStreamCounts()
    {
        var p = new ProxySettings { Enabled = true, Codec = ProxyCodec.H264, BitrateMbps = 10 };
        var info = new ClipInfo { Duration = 100, Height = 1080, AudioChannels = 8, AudioStreams = [1, 1, 1, 1, 1, 1, 1, 1] };
        // (10 x 1.1 + 0.128 x 8) Mb/s x 100 s = 12.024 Mb/s x 100 s = 150.3 MB (AAC floor of 128 kb/s a stream, ruling M6)
        Assert.Equal(150_300_000, SpaceEstimator.ProxyBytes([C("C0001.MXF", 1)], p, _ => true, _ => info));
    }

    // Ruling M6: audio is counted per stream and per channel - AAC at the real bitrate (64 kb/s a channel, 128-512k),
    // ProRes 16-bit PCM at 48 kHz (0.768 Mb/s a channel). The 8 mono PCM tracks of a Sony MXF were left out before.
    [Fact]
    public void Proxy_AudioPerStream_AacKbps_AndProResPcm()
    {
        var aac = new ProxySettings { Enabled = true, Codec = ProxyCodec.H264, BitrateMbps = 10 };
        var info = new ClipInfo { Duration = 100, Height = 1080, AudioChannels = 9, AudioStreams = [1, 2, 6] };
        // 11 + (0.128 + 0.128 + 0.384) = 11.64 Mb/s x 100 s = 145.5 MB
        Assert.Equal(145_500_000, SpaceEstimator.ProxyBytes([C("A.MOV", 1)], aac, _ => true, _ => info));

        var prores = new ProxySettings { Enabled = true, Codec = ProxyCodec.ProresProxy, Height = 1080 };
        var mxf = new ClipInfo { Duration = 100, Height = 1080, AudioChannels = 8, AudioStreams = [1, 1, 1, 1, 1, 1, 1, 1] };
        // 45 + 8 x 0.768 = 51.144 Mb/s x 100 s = 639.3 MB
        Assert.Equal(639_300_000, SpaceEstimator.ProxyBytes([C("C0001.MXF", 1)], prores, _ => true, _ => mxf));
        // No audio asked for: no audio term.
        prores.Audio = false;
        Assert.Equal(562_500_000, SpaceEstimator.ProxyBytes([C("C0001.MXF", 1)], prores, _ => true, _ => mxf));
        // A probed clip without the stream list (RAW intermediate): one stream of its channels.
        var raw = new ClipInfo { Duration = 100, Height = 1080, AudioChannels = 2 };
        prores.Audio = true;
        Assert.Equal(45 * 12.5e6 + 2 * 0.768 * 12.5e6, SpaceEstimator.ProxyBytes([C("C0001.MXF", 1)], prores, _ => true, _ => raw));
    }

    [Fact]
    public void Proxy_OffOrSkippedClips_Zero()
    {
        var off = new ProxySettings { Enabled = false };
        Assert.Equal(0, SpaceEstimator.ProxyBytes([C("A.MOV", 1_000_000)], off, _ => true));
        var on = new ProxySettings { Enabled = true };
        Assert.Equal(0, SpaceEstimator.ProxyBytes([C("SUB/A.MP4", 1_000_000, cameraProxy: true)], on, _ => true));
        Assert.Equal(0, SpaceEstimator.ProxyBytes([C("A.CRM", 1_000_000, ClipFormat.CanonRaw)], on, c => SpaceEstimator.Renderable(c, All)));
        Assert.False(SpaceEstimator.Renderable(C("A.MOV", 1), new ExternalTools()));
        Assert.True(SpaceEstimator.Renderable(C("A.braw", 1, ClipFormat.Braw), All));
    }

    [Fact]
    public void Extra_PerDestination()
    {
        var extra = new SpaceExtra(Proxy: 30, Report: 2, ProxyOnAllDestinations: false, ProxyFolder: null);
        Assert.Equal(new SpaceNeed(100, 30, 2), extra.ForDestination(0, 100));
        Assert.Equal(new SpaceNeed(100, 0, 2), extra.ForDestination(1, 100));
        Assert.Equal(new SpaceNeed(100, 30, 2), (extra with { ProxyOnAllDestinations = true }).ForDestination(1, 100));
    }
}

public class SpacePlannerTests
{
    const long GB = 1_000_000_000;

    static VolumeDemand D(string volume, string label, long card, long proxy = 0, long report = 0) => new(volume, label, new SpaceNeed(card, proxy, report));

    [Fact]
    public void TwoCardsOneDestination_AddUp()
    {
        var problems = SpacePlanner.Check([D("v1", "Zaloha (D:)", 200 * GB), D("v1", "Zaloha (D:)", 180 * GB, 30 * GB, 2 * GB)],
            new Dictionary<string, long> { ["v1"] = 398 * GB });
        var p = Assert.Single(problems);
        Assert.Equal(SpaceSeverity.Blocking, p.Severity);
        Assert.Equal("Not enough space on destination Zaloha (D:): needs 412 GB (cards 380 GB, proxies ~30 GB, report 2 GB), free 398 GB.", p.Text);
    }

    // Review focus 5: two destinations on one volume (--allow-same-device) + the proxy folder on it too.
    [Fact]
    public void SameVolume_DestinationsAndProxyFolder_AddUp()
    {
        var problems = SpacePlanner.Check(
            [D("v1", "Zaloha (D:)", 100 * GB, 10 * GB), D("v1", "Zaloha2 (D:)", 100 * GB), D("v1", @"D:\Proxy", 0, 10 * GB)],
            new Dictionary<string, long> { ["v1"] = 215 * GB });
        var p = Assert.Single(problems);
        Assert.Equal(SpaceSeverity.Blocking, p.Severity);
        Assert.Equal(@"Zaloha (D:), Zaloha2 (D:), D:\Proxy", p.Label);
        Assert.Equal(220 * GB, p.Need.Total);
    }

    [Fact]
    public void FivePercentBoundary_Warning()
    {
        var need = new[] { D("v1", "Zaloha (D:)", 100 * GB) };
        Assert.Empty(SpacePlanner.Check(need, new Dictionary<string, long> { ["v1"] = 105 * GB }));   // exactly 5 % left
        var w = Assert.Single(SpacePlanner.Check(need, new Dictionary<string, long> { ["v1"] = 104 * GB }));
        Assert.Equal(SpaceSeverity.Warning, w.Severity);
        Assert.Equal("Only 4 GB will be left on destination Zaloha (D:) after the offload — less than the 5 % reserve, and the proxy estimate is approximate.", w.Text);
    }

    [Fact]
    public void UnknownFree_NeverBlocks_OtherVolumesSeparately()
    {
        var problems = SpacePlanner.Check([D("v1", "A", 10 * GB), D("v2", "B", 10 * GB)], new Dictionary<string, long> { ["v2"] = 5 * GB });
        Assert.Equal("B", Assert.Single(problems).Label);
    }
}

/// Spec 2.5 in the engine and the job: proxies count before the copy; before the render they are counted again.
public class SpaceEngineTests
{
    [Fact]
    public void NotEnoughForTheProxies_StopsBeforeCopying()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MOV"), 1_000_000, 1);
        long cardNeed = SpaceEstimator.CardBytes(1_000_000, 1);
        long report = SpaceEstimator.ReportBytes(1);
        var options = new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, FreeSpace = _ => cardNeed + report };
        var config = new JobConfig(card, [dir.Sub("d1")]) { Proxy = new ProxySettings { Enabled = true }, Tools = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true } };
        var job = new OffloadJob(config, options);

        job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Contains("Not enough space", s.Error);
        Assert.Contains("proxies ~", s.Error);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "d1", Layout.Today())));   // nothing written
    }

    // Spec 2.5 CLI: the --dry-run breakdown and the 5 % warning come with the plan.
    [Fact]
    public void Plan_HasThePerVolumeLines_AndTheLowSpaceWarning()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MOV"), 1_000_000, 1);
        long need = SpaceEstimator.CardBytes(1_000_000, 1);
        var options = new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, FreeSpace = _ => need + need / 50 };
        var d1 = dir.Sub("d1");

        var plan = new OffloadEngine(options).Prepare(card, [d1], null, "2026-10-06");

        var line = Assert.Single(plan.Space);
        var label = Offload.Core.History.HistoryText.DiskName(d1);
        Assert.Equal((label, need, need + need / 50), (line.Label, line.Need.Total, line.Free));
        Assert.StartsWith("Only ", Assert.Single(plan.SpaceWarnings));
        Assert.Contains($" will be left on destination {label} after the offload", Assert.Single(plan.SpaceWarnings));
        Assert.Equal($"Space: {label} needs 68 MB (card 68 MB), free 70 MB", Msg.SpaceLine(line.Label, line.Need, line.Free));
    }


    // Review focus 5 in the engine: two destinations on one volume (AllowSameDevice) + the proxy folder on it too.
    [Fact]
    public void SameVolume_TwoDestinationsAndProxyFolder_AddUpInTheEngine()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MOV"), 1_000_000, 1);
        long c = SpaceEstimator.CardBytes(1_000_000, 1);
        var extra = new SpaceExtra(Proxy: 10_000_000, Report: 5_000_000, ProxyOnAllDestinations: true, ProxyFolder: dir.Sub("proxies"));
        long total = 2 * (c + 10_000_000 + 5_000_000) + 10_000_000;
        OffloadEngine Engine(long free) => new(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, FreeSpace = _ => free });
        string[] dests = [dir.Sub("d1"), dir.Sub("d2")];

        var e = Assert.Throws<OffloadError>(() => Engine(total - 1).Prepare(card, dests, null, "2026-10-06", _ => extra));
        Assert.Equal(PreflightCode.NoSpace, e.Code);
        Assert.Contains($"needs {Format.BytesShort(total)}", e.Message);

        var plan = Engine(total).Prepare(card, dests, null, "2026-10-06", _ => extra);
        Assert.Equal(total, Assert.Single(plan.Space).Need.Total);
    }

    [Fact]
    public void CardAlone_Fits_WithoutProxies()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MOV"), 1_000_000, 1);
        var options = new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true,
            FreeSpace = _ => SpaceEstimator.CardBytes(1_000_000, 1) + SpaceEstimator.ReportBytes(1) };
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { AnalyzeClips = false }, options);
        job.Prepare();
        Assert.NotNull(job.Plan);
    }

    // Spec 2.5: the disk filled up during the copy — the proxies are skipped, the copy stays verified.
    [FfmpegFact]
    public void ProxiesSkipped_WhenTheDiskFilledMeanwhile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.MP4"), seconds: 1);
        long free = long.MaxValue / 4;
        var options = new OffloadOptions
        {
            ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264",
            FreeSpace = _ => free, BeforeFileCopy = _ => free = 0,
        };
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Proxy = new ProxySettings { Enabled = true, Height = 360 } }, options);

        var report = job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.StartsWith("Proxies were not rendered: not enough space on d1 (", s.Warning);
        Assert.Contains(") (needs ~", s.Warning);
        Assert.Equal(ProxyStatus.Skipped, Assert.Single(report!.Clips).ProxyStatus);
        Assert.False(Directory.Exists(Path.Combine(job.Plan!.CardPaths[0], "_PROXY")));
    }
}
