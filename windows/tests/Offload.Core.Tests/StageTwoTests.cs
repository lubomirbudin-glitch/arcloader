namespace Offload.Core.Tests;

public class StageTwoTests
{
    static OffloadOptions Options() => new() { ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    static string SonyCard(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "CLIP", "C0001.MP4"), seconds: 2, timecode: "01:00:00:00");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "CLIP", "C0002.MP4"), seconds: 2, timecode: "01:00:10:00");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "SUB", "C0001S03.MP4"), seconds: 2, width: 640, height: 360);
        File.WriteAllText(Path.Combine(card, "PRIVATE", "M4ROOT", "MEDIAPRO.XML"), "<x/>");
        return card;
    }

    [FfmpegFact]
    public void Proxies_ThumbnailsAndPdf_OnEveryDestination()
    {
        using var dir = new TempDir();
        var card = SonyCard(dir);
        var config = new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-05",
            Proxy = new ProxySettings { Enabled = true, Height = 360 },
        };
        var job = new OffloadJob(config, Options());

        var report = job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.Equal(2, s.ProxiesDone);
        Assert.Equal(0, s.ProxiesFailed);
        Assert.Contains(report!.ProxyEncoder, Encoders.H264); // final review B1
        Assert.Equal(3, report!.Clips.Count);
        Assert.All(report.Clips, c => Assert.Equal(3, c.Thumbnails.Count));
        var sub = report.Clips.Single(c => c.Clip.IsCameraProxy);
        Assert.Equal(ProxyStatus.Skipped, sub.ProxyStatus);
        foreach (var cardPath in job.Plan!.CardPaths)
        {
            Assert.True(File.Exists(Path.Combine(cardPath, "_PROXY", "C0001.mov")));
            Assert.True(File.Exists(Path.Combine(cardPath, "_PROXY", "C0002.mov")));
            var html = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "A001_report.html"));
            Assert.Contains("<figcaption>01:00:10:00</figcaption>", html);
            if (PdfReport.FindEdge() != null) Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "A001_report.pdf")));
        }
        // The hash list only covers the card's files, not proxies.
        Assert.DoesNotContain("_PROXY", File.ReadAllText(Path.Combine(job.Plan.CardPaths[0], "_REPORT", "A001.xxh64")));
    }

    // Final review A7: a failed copy to another destination keeps the rendered proxy.
    [FfmpegFact]
    public void ProxyCopyFails_RenderedProxyKept()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "C0001.mov"), seconds: 1);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Proxy = new ProxySettings { Enabled = true, Height = 360 },
        }, Options());
        Assert.True(job.RunCopy());
        var paths = job.Plan!.CardPaths;
        Directory.CreateDirectory(Path.Combine(paths[1], "_PROXY", "C0001.mov")); // blocks the copy on d2
        var log = new List<string>();
        job.RunPost(log.Add);

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.Equal(1, s.ProxiesDone);
        Assert.Equal(0, s.ProxiesFailed);
        var clip = Assert.Single(job.Report!.Clips);
        Assert.Equal(ProxyStatus.Done, clip.ProxyStatus);
        Assert.Equal("_PROXY/C0001.mov", clip.ProxyPath);
        Assert.StartsWith($"kopie na {paths[1]} selhala: ", clip.ProxyNote);
        Assert.Contains(log, l => l.Contains("C0001") && l.Contains(paths[1]));
        Assert.True(File.Exists(Path.Combine(paths[0], "_PROXY", "C0001.mov")));
        Assert.Empty(Directory.GetFiles(Path.Combine(paths[1], "_PROXY"), "*.part"));
    }

    [FfmpegFact]
    public void AnalyzeOnly_NoProxies()
    {
        using var dir = new TempDir();
        var card = SonyCard(dir);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-05" }, Options());
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Equal(3, report!.Clips.Count);
        Assert.All(report.Clips, c => Assert.Equal(ProxyStatus.None, c.ProxyStatus));
        Assert.False(Directory.Exists(Path.Combine(job.Plan!.CardPaths[0], "_PROXY")));
    }

    [Fact]
    public void NothingToAnalyze_StillDone()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        File.WriteAllText(Path.Combine(card, "notes.txt"), "x");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { AnalyzeClips = false }, Options());
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Empty(report!.Clips);
    }

    // Review focus 2
    [Fact]
    public void NoTools_OffloadStillSafe()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MP4"), 50_000, 1);
        var config = new JobConfig(card, [dir.Sub("d1")])
        {
            Proxy = new ProxySettings { Enabled = true },
            Tools = new ExternalTools { NoSearch = true },
        };
        var job = new OffloadJob(config, Options());
        var report = job.Run();
        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        var clip = Assert.Single(report!.Clips);
        Assert.Contains("ffprobe not found", clip.ProbeError);
        Assert.Equal(ProxyStatus.Unsupported, clip.ProxyStatus);
        Assert.Equal("ffmpeg not found", clip.ProxyNote);
    }

    // Review focus 3
    [FfmpegFact]
    public void CorruptClip_OthersContinue()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "good.mov"), seconds: 1);
        TestFs.WriteRandom(Path.Combine(card, "bad.mov"), 100_000, 7);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Proxy = new ProxySettings { Enabled = true, Height = 360 } }, Options());
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var bad = report!.Clips.Single(c => c.Clip.Name == "bad");
        Assert.NotNull(bad.ProbeError);
        Assert.Equal(ProxyStatus.Failed, bad.ProxyStatus);
        var good = report.Clips.Single(c => c.Clip.Name == "good");
        Assert.Equal(ProxyStatus.Done, good.ProxyStatus);
        Assert.Equal(1, job.Status().ProxiesFailed);
    }

    // Review focus 4
    [FfmpegFact]
    public async Task CancelDuringProxies()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "long.mov"), seconds: 30, width: 1920, height: 1080);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Proxy = new ProxySettings { Enabled = true, Height = 1080, Codec = ProxyCodec.Prores422HQ },
        }, Options());
        var run = Task.Run(() => job.Run());
        while (job.Status().Phase != JobPhase.Proxies && !run.IsCompleted) await Task.Delay(20);
        await Task.Delay(300);
        job.Cancel();
        await run;
        var s = job.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.True(s.SafeToEject);
        var proxyDir = Path.Combine(job.Plan!.CardPaths[0], "_PROXY");
        Assert.True(!Directory.Exists(proxyDir) || Directory.GetFiles(proxyDir, "*.part").Length == 0);
        Assert.Equal(ProxyStatus.Failed, job.Report!.Clips.Single(c => c.Clip.Name == "long").ProxyStatus);
        Assert.True(!Directory.Exists(proxyDir) || !File.Exists(Path.Combine(proxyDir, "long.mov")));
    }

    [FfmpegFact]
    public void NoVerify_ProxiesFromWrittenCopy()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "clip.mov"), seconds: 1);
        var options = Options();
        options.Verify = false;
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Verify = false, Proxy = new ProxySettings { Enabled = true, Height = 360 } }, options);
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.False(job.Status().SafeToEject);
        Assert.Equal(ProxyStatus.Done, Assert.Single(report!.Clips).ProxyStatus);
    }
}
