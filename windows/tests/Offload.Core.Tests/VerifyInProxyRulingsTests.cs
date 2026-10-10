using Offload.Cli;
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Notify;
using Offload.Core.Outputs;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Plan 10 rulings on a real job with proxies: I1 (green only once the results are written into every folder), I4 (the
/// folders stay locked until then), the history and "Dnes" while verifying, appends that mix verified and unverified
/// generations, cancel / pause / an unplugged disk during the proxies and the background verification.
public class VerifyInProxyRulingsTests
{
    internal static OffloadOptions Options() => new()
    {
        ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264", CheckpointBlocks = 4,
    };

    internal static string Card(TempDir dir, double seconds = 2)
    {
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C001.mov"), seconds: seconds, width: 640, height: 360);
        File.WriteAllText(Path.Combine(card, "CLIP", "A001C001.xml"), "<clip/>");
        TestFs.WriteRandom(Path.Combine(card, "SOUND", "A001.wav"), 300_000, 7);
        return card;
    }

    internal static JobConfig Config(string card, IReadOnlyList<string> dests, TempDir dir, int? primary = null) => new(card, dests)
    {
        Date = "2026-10-08",
        PrimaryDestination = primary,
        Proxy = new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false },
        Outputs = new OutputSettings { DayReport = false, ClipListCsv = false },
        HistoryFolder = dir.Sub("history"),
    };

    static string[] Mhls(string cardPath) =>
        Directory.Exists(Path.Combine(cardPath, "ascmhl")) ? Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl") : [];

    static bool Pending(string cardPath) =>
        OffloadReport.FromJson(File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json"))).VerifyPending == true;

    static bool Free(IReadOnlyList<string> paths)
    {
        try
        {
            AppendLock.Acquire(paths).Dispose();
            return true;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.AppendBusy)
        {
            return false;
        }
    }

    static HistoryEntry Entry(TempDir dir) => Assert.Single(new HistoryStore(Path.Combine(dir.Path, "history")).Load());

    // I1: the results could not be written into the second folder — everything is verified, but that folder still says
    // verifyPending: no green anywhere, the folders are let go, the first folder has its generation.
    [FfmpegFact]
    public void CommitFailsInOneFolder_NoGreenAnywhere()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d2 = dir.Sub("d2");
        var config = Config(card, [dir.Sub("d1"), d2], dir);
        var job = new OffloadJob(config, Options())
        {
            BeforeCommit = path => { if (path.StartsWith(d2, StringComparison.OrdinalIgnoreCase)) throw new IOException("disk full"); },
        };

        job.Run();

        var s = job.Status();
        var r = job.Report!;
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(r.SafeToFormat);   // what was read is all good …
        Assert.False(s.SafeToFormat);  // … but the record is not in every folder (I1)
        Assert.True(s.SafeToEject);
        Assert.Contains(Msg.VerifyNotCommitted($"{job.Plan!.CardPaths[1]}: disk full"), s.Warning);
        var (p1, p2) = (job.Plan.CardPaths[0], job.Plan.CardPaths[1]);
        Assert.Single(Mhls(p1));
        Assert.False(Pending(p1));
        Assert.Empty(Mhls(p2));
        Assert.True(Pending(p2));
        Assert.False(CardRecord.Load(p2).Verified);
        Assert.True(BackupState.Of(job.Plan.CardPaths).Unverified > 0);
        Assert.NotEqual(JobCardKind.Done, JobCard.Headline("A001", s, r, ["d1", "d2"]).Kind);
        Assert.NotEqual(OutcomeKind.Done, JobOutcomes.Final("A001", s, r)!.Kind);
        Assert.False(JobOutcomes.Chime(s));
        Assert.Equal(1, CopyCommand.ExitCode(config, s, r));
        var entry = Entry(dir);
        Assert.False(entry.Verified);
        Assert.NotEqual(HistoryTone.Ok, HistoryView.Row(entry).Tone);
        Assert.True(Free(job.Plan.CardPaths));
    }

    // I4: from writing the verifyPending reports until the results are written the job holds its folders — an append or
    // a repair started meanwhile (another window, the CLI) is refused at once, in Czech; the chain gets one generation 1.
    [FfmpegFact]
    public void DuringStageTwo_FoldersHeld_AppendAndRepairRefusedAtOnce()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options());
        Assert.True(job.RunCopy());
        var paths = job.Plan!.CardPaths;
        Assert.True(Pending(paths[0]));

        var busy = Assert.Throws<OffloadError>(() => AppendLock.Acquire(paths));
        Assert.Equal(PreflightCode.AppendBusy, busy.Code);
        Assert.StartsWith("Another job is working with this backup right now", busy.Message);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C002.mov"), seconds: 1, width: 640, height: 360);
        var appendConfig = Config(card, [], dir);
        appendConfig.AppendInto = paths;
        var append = new OffloadJob(appendConfig, Options());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        append.Run();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));   // refused, not waited
        Assert.Equal(JobPhase.Failed, append.Status().Phase);
        Assert.Contains(paths.Select(Msg.AppendBusy), m => m == append.Status().Error);
        var repairConfig = Config(card, [], dir);
        repairConfig.RepairInto = paths;
        var repair = new OffloadJob(repairConfig, Options());
        repair.Run();
        Assert.Equal(JobPhase.Failed, repair.Status().Phase);
        Assert.Contains(paths.Select(Msg.AppendBusy), m => m == repair.Status().Error);
        Assert.True(Pending(paths[0]));   // nobody touched the generation

        job.RunPost();

        Assert.True(job.Status().SafeToFormat);
        Assert.True(Free(paths));
        Assert.All(paths, p => Assert.Equal([1], MhlReader.ReadChain(Path.Combine(p, "ascmhl"))!.Select(c => c.Seq)));
    }

    // A job that reads back in stage 1 (no proxies) never holds the folders after it.
    [Fact]
    public void ReadBackInStageOne_FoldersFreeAfterTheCopy()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 100_000, 1);
        var config = new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-08", AnalyzeClips = false, RecordHistory = false,
            Outputs = new OutputSettings { DayReport = false } };
        var job = new OffloadJob(config, Options());
        Assert.False(job.RunCopy());
        Assert.True(Free(job.Plan!.CardPaths));
        Assert.True(job.Status().SafeToFormat);
    }

    // History and "Dnes" while the proxies verify: never green; green once verified.
    [FfmpegFact]
    public void HistoryAndToday_WhileVerifying_NotGreen_ThenGreen()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], dir), Options());
        Assert.True(job.RunCopy());

        var during = Entry(dir);
        Assert.False(during.Verified);
        Assert.NotEqual(HistoryTone.Ok, HistoryView.Row(during).Tone);
        Assert.DoesNotContain("all verified", HistoryView.Summary([during]));
        Assert.False(job.Status().SafeToFormat);
        Assert.NotEqual(CardTone.Ok, Tone(job.Status()));

        job.RunPost();

        var after = Entry(dir);
        Assert.True(after.Verified);
        Assert.Equal(HistoryTone.Ok, HistoryView.Row(after).Tone);
        Assert.Equal(CardTone.Ok, Tone(job.Status()));
        Assert.True(JobOutcomes.Chime(job.Status()));
    }

    static CardTone Tone(JobStatus s)
    {
        var p = new JobProgress(DateTime.UtcNow);
        p.Update(s, DateTime.UtcNow);
        return p.Tone;
    }

    // Generation 1 cancelled during its verification (committed unverified), generation 2 verified with the proxies:
    // the backup as a whole is not verified — never green.
    [FfmpegFact]
    public void UnverifiedGenerationOne_AppendVerifiedInProxy_NotGreen()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        var first = new OffloadJob(Config(card, [d1], dir), Options());
        Assert.True(first.RunCopy());
        first.Cancel();
        first.RunPost();
        Assert.False(first.Status().SafeToFormat);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C002.mov"), seconds: 1, width: 640, height: 360);
        var config = Config(card, [d1], dir);
        config.AppendInto = first.Plan!.CardPaths;
        var append = new OffloadJob(config, Options());

        append.Run();

        var s = append.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(append.Report!.SafeToFormat);   // its own files are verified
        Assert.False(s.SafeToFormat);               // the backup is not
        Assert.Equal(ReportDestination.InProxy, append.Report.Destinations[0].VerifyMethod);
        Assert.False(CardRecord.Load(first.Plan.CardPaths[0]).Verified);
        Assert.False(Entry(dir).Verified);
        Assert.NotEqual(OutcomeKind.Done, JobOutcomes.Final("A001", s, append.Report)!.Kind);
    }

    // Generation 1 verified, the append's new clip damaged on the disk before its verification: failed, never green.
    [FfmpegFact]
    public void VerifiedGenerationOne_AppendFailsItsVerification_NotGreen()
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
        Assert.True(append.RunCopy());
        var clip = Layout.Join(first.Plan.CardPaths[0], "CLIP/A001C002.mov");
        DiskFlip.Flip(clip, new FileInfo(clip).Length / 3);

        append.RunPost();

        var s = append.Status();
        Assert.False(s.SafeToFormat);
        Assert.Equal(["failed"], append.Report!.Files.Single(f => f.Path == "CLIP/A001C002.mov").Destinations);
        Assert.Equal(["CLIP/A001C002.mov"], CardRecord.Load(first.Plan.CardPaths[0]).FailedFiles);   // read back, and failed
        Assert.Equal(1, BackupState.Of(first.Plan.CardPaths).Failed);
        Assert.False(Entry(dir).Success);
        Assert.Equal(1, CopyCommand.ExitCode(config, s, append.Report));
    }

    // Cancel while a proxy renders and the other disk is read in the background: the generation is committed unverified,
    // the folders are let go, nothing is green.
    [FfmpegFact]
    public async Task CancelledWhileRendering_CommittedUnverified_FoldersFree()
    {
        using var dir = new TempDir();
        var card = Card(dir, seconds: 12);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        var post = Task.Run(() => job.RunPost());
        Assert.True(SpinWait.SpinUntil(() => job.Status().Phase == JobPhase.Proxies, TimeSpan.FromSeconds(60)));
        job.Cancel();
        await post.WaitAsync(TimeSpan.FromSeconds(60));

        var s = job.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.False(s.SafeToFormat);
        Assert.False(job.Report!.Verified);
        foreach (var p in job.Plan!.CardPaths)
        {
            Assert.Single(Mhls(p));
            Assert.False(Pending(p));
            Assert.False(CardRecord.Load(p).Verified);
            Assert.Empty(CardRecord.Load(p).FailedFiles);
        }
        Assert.True(Free(job.Plan.CardPaths));
    }

    // Pozastavit in stage 2 with the verification threads at the gate, then Zrušit: as a cancel.
    [FfmpegFact]
    public async Task PausedThenCancelledInStageTwo_CommittedUnverified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options());
        Assert.True(job.RunCopy());
        job.Pause();
        var post = Task.Run(() => job.RunPost());
        Assert.True(SpinWait.SpinUntil(() => job.Status().PauseHeld, TimeSpan.FromSeconds(30)));
        job.Cancel();
        await post.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        Assert.False(job.Status().SafeToFormat);
        Assert.All(job.Plan!.CardPaths, p => Assert.False(Pending(p)));
        Assert.True(Free(job.Plan.CardPaths));
    }

    // The second disk is unplugged during stage 2 (its path is gone; a file stands where its root was, so nothing can
    // make the folder anew): its copies fail, its folder is never made anew for the record, the first disk is verified
    // and committed, nothing is green, the folders are let go.
    [FfmpegFact]
    public void SecondDiskUnpluggedInStageTwo_FailsThere_NoGreen()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d2 = dir.Sub("d2");
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), d2], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        Directory.Move(d2, d2 + "-gone");
        File.WriteAllText(d2, "unplugged");

        job.RunPost();

        var s = job.Status();
        Assert.False(s.SafeToFormat);
        Assert.Contains(Msg.CardFolderGone, s.Warning);
        Assert.All(job.Report!.Files, f => Assert.Equal("failed", f.Destinations[1]));
        Assert.All(job.Report.Files, f => Assert.Equal("verified", f.Destinations[0]));
        var p1 = job.Plan!.CardPaths[0];
        Assert.Single(Mhls(p1));
        Assert.False(Pending(p1));
        Assert.True(File.Exists(d2));   // nothing made anew there
        Assert.True(Free(job.Plan.CardPaths));
    }
}

/// I1 through the CLI: a commit that fails in one folder exits 1 (`offload copy` with proxies).
[Collection("Console")]
public class VerifyInProxyCliExitTests
{
    [FfmpegFact]
    public void CommitFailsInOneFolder_Exit1()
    {
        using var dir = new TempDir();
        var card = VerifyInProxyRulingsTests.Card(dir);
        var d2 = dir.Sub("d2");
        var config = VerifyInProxyRulingsTests.Config(card, [dir.Sub("d1"), d2], dir);
        var o = Console.Out;
        var e = Console.Error;
        var old = CopyCommand.JobFactory;
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        CopyCommand.JobFactory = c => new OffloadJob(c, VerifyInProxyRulingsTests.Options())
        {
            BeforeCommit = path => { if (path.StartsWith(d2, StringComparison.OrdinalIgnoreCase)) throw new IOException("disk full"); },
        };
        int exit;
        try { exit = CopyCommand.RunJob(config, dryRun: false); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
        Assert.Equal(1, exit);

        // The same card without the failure: exit 0.
        using var dir2 = new TempDir();
        var card2 = VerifyInProxyRulingsTests.Card(dir2);
        CopyCommand.JobFactory = c => new OffloadJob(c, VerifyInProxyRulingsTests.Options());
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        try { exit = CopyCommand.RunJob(VerifyInProxyRulingsTests.Config(card2, [dir2.Sub("d1"), dir2.Sub("d2")], dir2), dryRun: false); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
        Assert.Equal(0, exit);
    }
}
