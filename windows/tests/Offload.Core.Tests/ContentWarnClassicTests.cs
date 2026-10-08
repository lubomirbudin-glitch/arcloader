using Offload.Cli;
using Offload.Core.Desktop;
using Offload.Core.Notify;
using Offload.Core.Outputs;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Final review, item 1: the decoder's content warnings reach the job's status also when the copy was verified the
/// classic way (verify-in-proxy off, --classic-verify) or not at all (verification off, proxies on) — never a
/// "hotovo — kartu lze formátovat" nor the chime before the user has looked at the damaged clip.
public class ContentWarnClassicTests
{
    internal static OffloadOptions Options(bool verify = true) => new()
    {
        Verify = verify, ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264", CheckpointBlocks = 4,
    };

    /// A clip whose packets were damaged on the card (as VerifyInProxyJobTests.DamagedClipFromTheCamera does it).
    internal static string DamagedCard(TempDir dir)
    {
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "M4ROOT", "CLIP", "C0001.MP4");
        TestMedia.MakeFaststart(path, seconds: 4);
        var bytes = File.ReadAllBytes(path);
        new Random(5).NextBytes(bytes.AsSpan(bytes.Length / 2, 20_000));
        File.WriteAllBytes(path, bytes);
        return card;
    }

    internal static JobConfig Config(string card, string dest, TempDir dir, bool verify, bool verifyInProxy) => new(card, [dest])
    {
        Date = "2026-10-08",
        Verify = verify,
        VerifyInProxy = verifyInProxy,
        Proxy = new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false },
        Outputs = new OutputSettings { DayReport = false, ClipListCsv = false },
        HistoryFolder = dir.Sub("history"),
    };

    [FfmpegFact]
    public void ClassicVerify_DamagedClip_WarningAndConfirmation_NoHotovoNoChime()
    {
        using var dir = new TempDir();
        var card = DamagedCard(dir);
        var job = new OffloadJob(Config(card, dir.Sub("d1"), dir, verify: true, verifyInProxy: false), Options());

        job.Run();

        var s = job.Status();
        var r = job.Report!;
        Assert.Null(r.VerifyPending);                       // verified in stage 1, the classic way
        Assert.True(r.Verified);
        Assert.True(s.SafeToFormat);
        Assert.Equal(1, s.ContentWarnings);
        Assert.Contains(Msg.ContentWarnings(1), s.Warning);
        Assert.Single(r.ContentWarnings);
        Assert.True(JobCard.NeedsContentConfirm(s, confirmed: false));
        Assert.False(JobOutcomes.Chime(s));
        var outcome = JobOutcomes.Final("A001", s, r)!;
        Assert.Equal(OutcomeKind.Attention, outcome.Kind);
        Assert.DoesNotContain("kartu lze formátovat", outcome.Message);
        Assert.DoesNotContain("hotovo", outcome.Title);
    }

    [FfmpegFact]
    public void VerifyOff_ProxiesOn_DamagedClip_WarningStillThere()
    {
        using var dir = new TempDir();
        var card = DamagedCard(dir);
        var job = new OffloadJob(Config(card, dir.Sub("d1"), dir, verify: false, verifyInProxy: true), Options(verify: false));

        job.Run();

        var s = job.Status();
        Assert.False(s.SafeToFormat);
        Assert.Equal(1, s.ContentWarnings);
        Assert.Contains(Msg.ContentWarnings(1), s.Warning);
        Assert.False(JobOutcomes.Chime(s));
        Assert.NotEqual(OutcomeKind.Done, JobOutcomes.Final("A001", s, job.Report)!.Kind);
    }
}

/// Item 1 through the CLI (`offload copy --classic-verify`): the warning is printed.
[Collection("Console")]
public class ContentWarnClassicCliTests
{
    [FfmpegFact]
    public void ClassicVerify_DamagedClip_CliPrintsTheWarning()
    {
        using var dir = new TempDir();
        var card = ContentWarnClassicTests.DamagedCard(dir);
        var config = ContentWarnClassicTests.Config(card, dir.Sub("d1"), dir, verify: true, verifyInProxy: false);
        var o = Console.Out;
        var e = Console.Error;
        var old = CopyCommand.JobFactory;
        var so = new StringWriter();
        Console.SetOut(so);
        Console.SetError(new StringWriter());
        CopyCommand.JobFactory = c => new OffloadJob(c, ContentWarnClassicTests.Options());
        try { CopyCommand.RunJob(config, dryRun: false); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
        Assert.Contains("Varování: " + Msg.ContentWarnings(1), so.ToString());
    }
}
