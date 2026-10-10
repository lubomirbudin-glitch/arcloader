using Offload.Cli;
using Offload.Core.Desktop;
using Offload.Core.Notify;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Plan 10, R5 with rulings I1/I2: every verdict that was green before plan 10 keys off SafeToFormat ("Lze formátovat",
/// everything verified everywhere and written into every folder) — never off "copied" (SafeToEject, "Lze vyjmout").
/// The job card's headline and tone, the notification, the success sound, the CLI exit code and the eject strip's text.
public class VerifyVerdictRulingTests
{
    static readonly string[] Disks = ["RAID_A"];

    static OffloadReport Verified() => Reports.Sample(@"D:\Zaloha\2026-10-08\A001", verified: true, destError: null);

    static JobStatus Status(bool eject, bool format, string? warning = null, JobPhase phase = JobPhase.Done) =>
        new() { Phase = phase, CopySucceeded = true, SafeToEject = eject, SafeToFormat = format, Warning = warning };

    static CardTone Tone(JobStatus s)
    {
        var p = new JobProgress(DateTime.UtcNow);
        p.Update(s, DateTime.UtcNow);
        return p.Tone;
    }

    // Copied everywhere, the verification not done (or its results not written, I1): no green anywhere.
    [Fact]
    public void CopiedButNotVerified_NeverGreen()
    {
        var s = Status(eject: true, format: false);
        var r = Verified();   // even a report that says verified: the job's verdict decides (I1)

        var h = JobCard.Headline("A001", s, r, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — safe to eject, do not format yet"), (h.Kind, h.Title));
        Assert.NotEqual(OutcomeKind.Done, JobOutcomes.Final("A001", s, r)!.Kind);
        Assert.False(JobOutcomes.Chime(s));
        Assert.NotEqual(CardTone.Ok, Tone(s));
        Assert.Equal(1, CopyCommand.ExitCode(new JobConfig("E:\\", ["D:\\Zaloha"]), s, r));
        Assert.DoesNotContain("and verified", Texts.SafeToEject(2));
        Assert.Contains("Format it only after verification", Texts.SafeToEject(2));
    }

    [Fact]
    public void VerifiedEverywhere_Green()
    {
        var s = Status(eject: true, format: true);
        var r = Verified();

        Assert.Equal(JobCardKind.Done, JobCard.Headline("A001", s, r, Disks).Kind);
        Assert.Equal(OutcomeKind.Done, JobOutcomes.Final("A001", s, r)!.Kind);
        Assert.True(JobOutcomes.Chime(s));
        Assert.Equal(CardTone.Ok, Tone(s));
        Assert.Equal(0, CopyCommand.ExitCode(new JobConfig("E:\\", ["D:\\Zaloha"]), s, r));
        Assert.Equal("All files are on 2 disks and verified — the card is safe to eject and format.", Texts.SafeToFormat(2));
    }

    // The decision for ruling C2 + T11 (documented in the report): a destination verified through the Windows cache is
    // verified (SafeToFormat, green verdict), but its note is a warning — an orange line, "vyžaduje pozornost", no chime.
    [Fact]
    public void VerifiedThroughTheCache_GreenVerdict_OrangeNote_NoChime()
    {
        var s = Status(eject: true, format: true, warning: Msg.VerifiedThroughCache("NAS (Z:)", 3));
        var r = Verified();

        Assert.Equal(JobCardKind.Done, JobCard.Headline("A001", s, r, Disks).Kind);
        var o = JobOutcomes.Final("A001", s, r)!;
        Assert.Equal(OutcomeKind.Attention, o.Kind);
        Assert.Contains("Verified by reading through the Windows cache", o.Message);
        Assert.False(JobOutcomes.Chime(s));
    }

    [Fact]
    public void CopyDone_WhileVerifying_SaysFormattingWaits()
    {
        var o = JobOutcomes.CopyDone("A001", Status(eject: true, format: false, phase: JobPhase.Analyzing), proxies: true)!;
        Assert.Equal("Verifying with the proxies — format only after it finishes.", o.Message);
        Assert.Equal("Proxies are still rendering.", JobOutcomes.CopyDone("A001", Status(eject: true, format: true, phase: JobPhase.Proxies), proxies: true)!.Message);
        Assert.Null(JobOutcomes.CopyDone("A001", Status(eject: false, format: false, phase: JobPhase.Proxies), proxies: true));
    }

    [Fact]
    public void Cancelled_CopiedNotVerified_NeverSaysVerified()
    {
        var h = JobCard.Headline("A001", Status(eject: true, format: false, phase: JobPhase.Cancelled), Verified(), Disks);
        Assert.Equal(JobCardKind.Cancelled, h.Kind);
        Assert.DoesNotContain("and verified", h.Sub);
        Assert.Contains("do not format", h.Sub);
    }

    // "Nic nového" — green only when the backup is complete (no warning) — and --no-verify: every file copied is exit 0.
    [Fact]
    public void ExitCode_NothingNew_And_NoVerify()
    {
        var config = new JobConfig("E:\\", ["D:\\Zaloha"]);
        var nothingNew = new JobStatus { Phase = JobPhase.Done, Note = Msg.NothingNew, SafeToFormat = true, SafeToEject = true };
        Assert.Equal(0, CopyCommand.ExitCode(config, nothingNew, null));
        Assert.Equal(1, CopyCommand.ExitCode(config, nothingNew.WithWarning(Msg.UnverifiedEarlier(2)) with { SafeToFormat = false }, null));
        var copied = Reports.Sample(@"D:\Zaloha\2026-10-08\A001", verified: false, destError: null);
        Assert.Equal(0, CopyCommand.ExitCode(new JobConfig("E:\\", ["D:\\Zaloha"]) { Verify = false }, Status(eject: true, format: false), copied));
        Assert.Equal(1, CopyCommand.ExitCode(config, Status(eject: true, format: false), copied));
        Assert.Equal(1, CopyCommand.ExitCode(config, Status(eject: true, format: true, phase: JobPhase.Cancelled), Verified()));
    }
}
