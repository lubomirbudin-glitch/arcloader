using System.Text.Json.Nodes;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Plan 10 end to end: a job with proxies verifies its primary destination through the proxy's reads and the other in the
/// background; "Lze vyjmout" after the copy, "Lze formátovat" only once every copy is verified; the generation's ASC MHL
/// only then; a flipped byte, a vanished file, a cancel, an append — never a false green.
public class VerifyInProxyJobTests
{
    static OffloadOptions Options() => new()
    {
        ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264", CheckpointBlocks = 4,
    };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C001.mov"), seconds: 2, width: 640, height: 360);
        File.WriteAllText(Path.Combine(card, "CLIP", "A001C001.xml"), "<clip/>");
        TestFs.WriteRandom(Path.Combine(card, "SOUND", "A001.wav"), 300_000, 7);
        return card;
    }

    static JobConfig Config(string card, IReadOnlyList<string> dests, TempDir dir, int? primary = null) => new(card, dests)
    {
        Date = "2026-10-08",
        PrimaryDestination = primary,
        Proxy = new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false },
        Outputs = new OutputSettings { DayReport = false, ClipListCsv = false },
        HistoryFolder = dir.Sub("history"),
    };

    static JsonObject ReportJson(string cardPath) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json")))!.AsObject();

    static string[] MhlFiles(string cardPath) =>
        Directory.Exists(Path.Combine(cardPath, "ascmhl")) ? Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl") : [];

    [FfmpegFact]
    public void TwoDestinations_OnePrimaryInProxy_OtherInBackground_FormatOnlyAtTheEnd()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 1), Options());

        Assert.True(job.RunCopy());
        // Stage 1 ended: everything is on both disks — "Lze vyjmout", not "Lze formátovat"; no MHL generation yet.
        var mid = job.Status();
        Assert.True(mid.SafeToEject);
        Assert.False(mid.SafeToFormat);
        foreach (var p in job.Plan!.CardPaths)
        {
            var json = ReportJson(p);
            Assert.True((bool)json["verifyPending"]!);
            Assert.False((bool)json["verified"]!);
            Assert.Empty(MhlFiles(p));
            Assert.False(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
            var record = CardRecord.Load(p);
            Assert.False(record.Verified);          // never green before it is verified
            Assert.Empty(record.FailedFiles);       // and never red either
            Assert.Equal(3, ManifestFile.Files(ManifestFile.Read(p), 1).Count);
        }

        job.RunPost();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.True(s.SafeToFormat);
        var r = job.Report!;
        Assert.True(r.Verified);
        Assert.Null(r.VerifyPending);
        Assert.All(r.Files, f => Assert.All(f.Destinations, d => Assert.Equal("verified", d)));
        Assert.Equal([ReportDestination.ReadBack, ReportDestination.InProxy], r.Destinations.Select(d => d.VerifyMethod));
        Assert.Equal(Msg.ClassicSecondary, r.Destinations[0].VerifyNote);
        Assert.Null(r.Destinations[1].VerifyNote);
        Assert.All(r.Destinations, d => Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}$", d.VolumeSerial));
        Assert.NotNull(r.SafeToFormatSeconds);
        Assert.False(r.ContentWarnings.Count > 0);
        Assert.Equal(ContentResult.Ok, r.Clips.Single().Content!.State);
        foreach (var p in job.Plan.CardPaths)
        {
            Assert.Null(ReportJson(p)["verifyPending"]);
            Assert.Single(MhlFiles(p));
            Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
            Assert.True(CardRecord.Load(p).Verified);
        }
        var entry = Assert.Single(new HistoryStore(Path.Combine(dir.Path, "history")).Load());
        Assert.True(entry.Verified);
        Assert.True(Assert.Single(entry.Generations).Verified);
    }

    [FfmpegFact]
    public void FlippedByteOnThePrimary_FoundByTheProxyRead_Failed_EjectYesFormatNo()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        var clip = Layout.Join(job.Plan!.CardPaths[0], "CLIP/A001C001.mov");
        DiskFlip.Flip(clip, new FileInfo(clip).Length / 2);

        job.RunPost();

        var s = job.Status();
        Assert.True(s.SafeToEject);
        Assert.False(s.SafeToFormat);
        var f = job.Report!.Files.Single(x => x.Path == "CLIP/A001C001.mov");
        Assert.Equal(["failed", "verified"], f.Destinations);
        Assert.StartsWith("HASH MISMATCH: block ", f.Errors[0]);
        var mhl = MhlReader.Read(job.Plan.CardPaths[0])!;
        Assert.True(mhl.Files["CLIP/A001C001.mov"].Failed);
        Assert.False(MhlReader.Read(job.Plan.CardPaths[1])!.Files["CLIP/A001C001.mov"].Failed);
    }

    [FfmpegFact]
    public void FlippedByteOnTheSecondary_FoundInTheBackground()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        var wav = Layout.Join(job.Plan!.CardPaths[1], "SOUND/A001.wav");
        DiskFlip.Flip(wav, 1000);

        job.RunPost();

        Assert.False(job.Status().SafeToFormat);
        Assert.Equal(["verified", "failed"], job.Report!.Files.Single(x => x.Path == "SOUND/A001.wav").Destinations);
    }

    // Spec 10: the same block wrong on both disks — the card is read again and the report says what it gives.
    [FfmpegFact]
    public void SameBlockWrongOnBothDisks_TheCardIsAsked_NoteInTheReport()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options());
        Assert.True(job.RunCopy());
        foreach (var p in job.Plan!.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/A001.wav"), 70_000);

        job.RunPost();

        var notes = job.Report!.VerifyNotes!;
        Assert.Contains(notes, n => n.StartsWith("SOUND/A001.wav: blok 1 nesouhlasí stejně na ", StringComparison.Ordinal) && n.EndsWith(Msg.CardGivesRecorded));
        Assert.Contains(notes[0], job.Status().Warning);
    }

    // A disk that loses the file before it is read: failed there, nothing falsely green.
    [FfmpegFact]
    public void FileGoneOnTheSecondary_FailedThere_FormatNo()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        File.Delete(Layout.Join(job.Plan!.CardPaths[1], "CLIP/A001C001.xml"));

        job.RunPost();

        var f = job.Report!.Files.Single(x => x.Path == "CLIP/A001C001.xml");
        Assert.Equal("failed", f.Destinations[1]);
        Assert.StartsWith("verify: ", f.Errors[1]);
        Assert.False(job.Status().SafeToFormat);
    }

    // Cancelled during stage 2: the generation is still committed (an append and a repair need it), the unread copies
    // stay "written" — unverified, never green; "Zkopírovat znovu" reads them back later (plan 9 R3).
    [FfmpegFact]
    public void CancelledBeforeTheProxies_CommittedButUnverified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], dir), Options());
        Assert.True(job.RunCopy());
        job.Cancel();

        job.RunPost();

        var s = job.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.False(s.SafeToFormat);
        var p = job.Plan!.CardPaths[0];
        Assert.Single(MhlFiles(p));
        Assert.False((bool)ReportJson(p)["verified"]!);
        Assert.Null(ReportJson(p)["verifyPending"]);
        Assert.False(CardRecord.Load(p).Verified);
        Assert.Empty(CardRecord.Load(p).FailedFiles);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void WithoutProxies_OrSwitchedOff_ReadBackInStageOne(bool proxies, bool setting)
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 200_000, 1);
        var config = Config(card, [dir.Sub("d1")], dir);
        config.Proxy.Enabled = proxies;
        config.VerifyInProxy = setting;
        config.AnalyzeClips = false;
        var job = new OffloadJob(config, Options());

        job.Run();

        var r = job.Report!;
        Assert.True(r.Verified);
        Assert.Null(r.VerifyPending);
        Assert.Equal(ReportDestination.ReadBack, r.Destinations[0].VerifyMethod);
        Assert.True(job.Status().SafeToFormat);
    }

    // Spec 7, spec 11: a damaged clip from the camera — the copy is verified (bit for bit), the decoder's warning is
    // reported with its place, "Lze formátovat" stays (formatting asks for a confirmation in the app).
    [FfmpegFact]
    public void DamagedClipFromTheCamera_VerifiedAndContentWarn()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "M4ROOT", "CLIP", "C0001.MP4");
        TestMedia.MakeFaststart(path, seconds: 4);
        var bytes = File.ReadAllBytes(path);
        new Random(5).NextBytes(bytes.AsSpan(bytes.Length / 2, 20_000));
        File.WriteAllBytes(path, bytes);
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], dir), Options());

        job.Run();

        var s = job.Status();
        Assert.True(s.SafeToFormat);
        Assert.Equal(1, s.ContentWarnings);
        Assert.Contains(Msg.ContentWarnings(1), s.Warning);
        var c = Assert.Single(job.Report!.ContentWarnings);
        Assert.Equal("decode", c.Content!.Kind);
        Assert.StartsWith("01:00:0", c.Content.Timecode);
        Assert.Contains("\"content\"", File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    [FfmpegFact]
    public void Append_VerifiedInProxy_CommitsGenerationTwo()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        var first = new OffloadJob(Config(card, [d1], dir), Options());
        first.Run();
        Assert.True(first.Status().SafeToFormat);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C002.mov"), seconds: 1, width: 640, height: 360);

        var config = Config(card, [d1], dir);
        config.AppendInto = first.Plan!.CardPaths;
        var append = new OffloadJob(config, Options());
        append.Run();

        var s = append.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToFormat);
        var p = first.Plan.CardPaths[0];
        Assert.Equal(2, MhlFiles(p).Length);
        Assert.Equal(ReportDestination.InProxy, append.Report!.Destinations[0].VerifyMethod);
        Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
        Assert.True(CardRecord.Load(p).Verified);
    }

    [FfmpegFact]
    public void CacheCheckFails_ThatDiskReadBack_WithTheReason()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options())
        {
            CacheCheck = (path, _) => path.Contains(@"\d1\", StringComparison.OrdinalIgnoreCase) ? Msg.ClassicCacheCheck : null,
        };

        job.Run();

        var r = job.Report!;
        Assert.Equal([ReportDestination.ReadBack, ReportDestination.InProxy], r.Destinations.Select(d => d.VerifyMethod));
        Assert.Equal(Msg.ClassicCacheCheck, r.Destinations[0].VerifyNote);
        Assert.True(job.Status().SafeToFormat);
    }

    // Pozastavit in stage 2: the verification threads stand at the gate too — the job says "Pozastaveno" only then.
    [FfmpegFact]
    public async Task PausedInStageTwo_HoldsThenFinishesVerified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options());
        Assert.True(job.RunCopy());
        job.Pause();
        var post = Task.Run(() => job.RunPost());

        Assert.True(SpinWait.SpinUntil(() => job.Status().PauseHeld, TimeSpan.FromSeconds(30)));
        Assert.False(post.IsCompleted);
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(job.Status().SafeToFormat);
    }
}
