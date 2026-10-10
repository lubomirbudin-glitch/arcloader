namespace Offload.Core.Tests;

public class StageTwoTests
{
    /// Software encoder: these full jobs run beside other proxy tests, and parallel NVENC sessions can collide
    /// (consumer GPUs limit them), which would fail a proxy for a reason that has nothing to do with the test.
    static OffloadOptions Options() => new() { ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264" };

    static string SonyCard(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "CLIP", "C0001.MP4"), seconds: 2, timecode: "01:00:00:00");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "CLIP", "C0002.MP4"), seconds: 2, timecode: "01:00:10:00");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "SUB", "C0001S03.MP4"), seconds: 2, width: 640, height: 360);
        File.WriteAllText(Path.Combine(card, "PRIVATE", "M4ROOT", "MEDIAPRO.XML"), "<x/>");
        return card;
    }

    /// Launches Edge (the report PDF): in the Edge collection (fix round 2, item 9); the other stage-two tests stay parallel.
    [Collection("Edge")]
    public class Pdf
    {
        [FfmpegFact]
        public void Proxies_ThumbnailsAndPdf_OnEveryDestination()
        {
            using var pdfs = PdfReport.PrintHere();
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
            Assert.Equal("libx264", report!.ProxyEncoder); // final review B1; the encoder the test forced
            Assert.Equal(3, report!.Clips.Count);
            Assert.All(report.Clips, c => Assert.Equal(3, c.Thumbnails.Count));
            var sub = report.Clips.Single(c => c.Clip.IsCameraProxy);
            Assert.Equal(ProxyStatus.Skipped, sub.ProxyStatus);
            foreach (var cardPath in job.Plan!.CardPaths)
            {
                Assert.True(File.Exists(Path.Combine(cardPath, "_PROXY", "PRIVATE", "M4ROOT", "CLIP", "C0001.mov")));   // the card's folders (0.5.1)
                Assert.True(File.Exists(Path.Combine(cardPath, "_PROXY", "PRIVATE", "M4ROOT", "CLIP", "C0002.mov")));
                var html = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "A001_report.html"));
                Assert.Contains("<figcaption>01:00:10:00</figcaption>", html);
                if (PdfReport.FindEdge() != null) Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "A001_report.pdf")));
            }
            // The hash list only covers the card's files, not proxies.
            Assert.DoesNotContain("_PROXY", File.ReadAllText(Path.Combine(job.Plan.CardPaths[0], "_REPORT", "A001.xxh64")));
        }
    }

    // Plan 5, task 5: the LUT is chosen per clip (Sony XML read from the destination copy) and recorded in the report.
    [FfmpegFact]
    public void Report_RecordsLutPerClip()
    {
        using var dir = new TempDir();
        var card = SonyCard(dir);
        File.WriteAllText(Path.Combine(card, "PRIVATE", "M4ROOT", "CLIP", "C0001M01.XML"),
            """<NonRealTimeMeta><Item name="CaptureGammaEquation" value="s-log3-cine"/><Item name="CaptureColorPrimaries" value="S-Gamut3Cine"/></NonRealTimeMeta>""");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Date = "2026-10-05",
            AnalyzeClips = true,
            Proxy = new ProxySettings { Enabled = true, Height = 360 },
        }, Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Equal("C0001", report!.Clips[0].Clip.Name);
        Assert.Equal(("auto", "sony-slog3-sgamut3cine"), (report.Clips[0].Lut?.Source, report.Clips[0].Lut?.Id));
        Assert.Equal(ProxyStatus.Done, report.Clips[0].ProxyStatus);
        Assert.Equal(3, report.Clips[0].Thumbnails.Count);
        Assert.Contains("\"lut\"", File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    // Plan 8 fix round 1, item 2b: a custom LUT gone at render time is not silent — every proxy says it has none.
    [FfmpegFact]
    public void CustomLutGone_ProxiesSayTheyHaveNone()
    {
        using var dir = new TempDir();
        var card = SonyCard(dir);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Date = "2026-10-05",
            AnalyzeClips = false,
            Proxy = new ProxySettings { Enabled = true, Height = 360, LutMode = LutMode.Custom, LutPath = Path.Combine(dir.Path, "gone.cube") },
        }, Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var rendered = report!.Clips.Where(c => c.ProxyStatus == ProxyStatus.Done).ToList();
        Assert.Equal(2, rendered.Count);
        Assert.All(rendered, c => Assert.Equal("LUT not found: gone.cube — proxy without a LUT", c.ProxyNote));
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
            PrimaryDestination = 0,   // plan 10: the proxy is rendered on the primary destination — here d1, not the faster one
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
        Assert.StartsWith($"the copy to {paths[1]} failed: ", clip.ProxyNote);
        Assert.Contains(log, l => l.Contains("C0001") && l.Contains(paths[1]));
        Assert.True(File.Exists(Path.Combine(paths[0], "_PROXY", "C0001.mov")));
        Assert.Empty(Directory.GetFiles(Path.Combine(paths[1], "_PROXY"), "*.part"));
    }

    // Proxy folder: every proxy also lands in <folder>\<date>\<card>\, the backups keep theirs in _PROXY.
    [FfmpegFact]
    public void ProxyFolder_GetsACopy()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "C0001.mov"), seconds: 1);
        var extra = dir.Sub("proxies");
        TestFs.WriteRandom(Path.Combine(extra, "2026-10-05", "A001", "C0001.mov"), 100, 4);   // from an earlier run
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Date = "2026-10-05",
            Proxy = new ProxySettings { Enabled = true, Height = 360, ExtraFolder = extra },
        }, Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var clip = Assert.Single(report!.Clips);
        Assert.Equal(ProxyStatus.Done, clip.ProxyStatus);
        Assert.Null(clip.ProxyNote);
        var backup = Path.Combine(job.Plan!.CardPaths[0], "_PROXY", "C0001.mov");
        var copy = Path.Combine(extra, "2026-10-05", "A001", "C0001_2.mov");
        Assert.True(File.Exists(backup));
        Assert.Equal(File.ReadAllBytes(backup), File.ReadAllBytes(copy));
        Assert.Equal(100, new FileInfo(Path.Combine(extra, "2026-10-05", "A001", "C0001.mov")).Length);
        Assert.Equal(extra, report.Proxy!.ExtraFolder);
    }

    // 0.5.1: the card's folders are mirrored in _PROXY and in the proxy folder — idle\1.mp4 and listening\1.mp4 keep
    // their names (relinking by name); only a clash inside one folder (A.mov + A.mp4) gets "_2" and the note.
    [FfmpegFact]
    public void ProxyTree_MirrorsTheCardsFolders()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "idle", "1.mp4"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "listening", "1.mp4"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "listening", "A.mov"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "listening", "A.mp4"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "root.mov"), seconds: 1);
        var extra = dir.Sub("proxies");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-08",
            AnalyzeClips = false,
            Proxy = new ProxySettings { Enabled = true, Height = 360, ExtraFolder = extra },
        }, Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        string? PathOf(string clipPath) => report!.Clips.Single(c => c.Clip.Path == clipPath).ProxyPath;
        Assert.Equal("_PROXY/idle/1.mov", PathOf("idle/1.mp4"));
        Assert.Equal("_PROXY/listening/1.mov", PathOf("listening/1.mp4"));
        Assert.Equal("_PROXY/listening/A.mov", PathOf("listening/A.mov"));
        Assert.Equal("_PROXY/listening/A_2.mov", PathOf("listening/A.mp4"));
        Assert.Equal("_PROXY/root.mov", PathOf("root.mov"));
        Assert.Equal(Msg.ProxyRenamed("A_2"), report!.Clips.Single(c => c.Clip.Path == "listening/A.mp4").ProxyNote);
        Assert.All(report.Clips.Where(c => c.Clip.Path != "listening/A.mp4"), c => Assert.Null(c.ProxyNote));
        foreach (var cardPath in job.Plan!.CardPaths)
            foreach (var c in report.Clips)
                Assert.True(File.Exists(Layout.Join(cardPath, c.ProxyPath!)), c.ProxyPath);
        var day = Path.Combine(extra, "2026-10-08", "A001");
        foreach (var rel in new[] { @"idle\1.mov", @"listening\1.mov", @"listening\A.mov", @"listening\A_2.mov", "root.mov" })
            Assert.True(File.Exists(Path.Combine(day, rel)), rel);
        Assert.Empty(Directory.GetFiles(day, "*.part", SearchOption.AllDirectories));
    }

    // The folder went away mid-job (disconnected disk): a note, never a failed proxy or job.
    [FfmpegFact]
    public void ProxyFolderGone_NoteOnly()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "C0001.mov"), seconds: 1);
        var extra = Path.Combine(dir.Path, "unplugged");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Proxy = new ProxySettings { Enabled = true, Height = 360, ExtraFolder = extra },
        }, Options());
        var log = new List<string>();

        var report = job.Run(log.Add);

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.Equal(1, s.ProxiesDone);
        Assert.Equal(0, s.ProxiesFailed);
        var clip = Assert.Single(report!.Clips);
        Assert.Equal(ProxyStatus.Done, clip.ProxyStatus);
        Assert.Equal($"the copy to {extra} failed: the folder is not available", clip.ProxyNote);
        Assert.Contains(log, l => l.Contains("C0001") && l.Contains(extra));
        Assert.False(Directory.Exists(extra));
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

    // Spec 3.4: the reel of every clip in the report — the camera's tag, else the clip name, else the card folder.
    [FfmpegFact]
    public void Report_RecordsReel_TagNameOrCard()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C003_210101_R1AB.mov"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.mov"), seconds: 1);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "take.mov"), seconds: 1, reel: "TAPE07");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-05", CardName = "KARTA" }, Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Equal(["A001", "KARTA", "TAPE07"], report!.Clips.Select(c => c.Info?.Reel));
        Assert.Contains("\"reel\": \"TAPE07\"", File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json")));
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
        // Wait for the render's own progress: from then on the cancel lands inside ffmpeg (no fixed 300 ms guess
        // that a loaded PC could miss; a cancel before the clip starts would leave it without a proxy status).
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (job.Status() is var st && !(st.Phase == JobPhase.Proxies && st.ClipFraction > 0) && !run.IsCompleted)
        {
            Assert.True(DateTime.UtcNow < deadline, "the proxy stage did not reach the clip within 120 s");
            await Task.Delay(20);
        }
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
        Assert.True(job.Status().SafeToEject);     // R5: copied
        Assert.False(job.Status().SafeToFormat);   // never read back
        Assert.Equal(ProxyStatus.Done, Assert.Single(report!.Clips).ProxyStatus);
    }
}
