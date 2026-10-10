using System.Xml.Linq;
using Offload.Core.Desktop;
using Offload.Core.Notify;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R5 (plan 10): "Safe to eject" and "Safe to format" — never merged — on the job card, its chips, its steps and the
/// notifications; spec 7: content warnings wait for a confirmation.
public class VerifyVerdictTests
{
    static readonly string[] Disks = ["RAID_A", "SHUTTLE_B"];
    const string Card = @"D:\Zaloha\2026-10-08\A001";

    static OffloadReport Report(bool verified = true) => Reports.Sample(Card, verified: verified);

    [Fact]
    public void Headline_CopiedNotVerified_EjectYesFormatNo()
    {
        var s = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = false, Warning = Msg.UnverifiedEarlier(2) };
        var h = JobCard.Headline("A001", s, Report(), Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — safe to eject, do not format yet"), (h.Kind, h.Title));
    }

    [Fact]
    public void Headline_VerifiedEverywhere_Green_LzeFormatovat()
    {
        var s = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = true };
        var h = JobCard.Headline("A001", s, Report(), Disks);
        Assert.Equal((JobCardKind.Done, "A001 — safe to format"), (h.Kind, h.Title));
    }

    // Spec 7: content warnings keep the card yellow until the user confirms them.
    [Fact]
    public void Headline_ContentWarnings_YellowUntilConfirmed()
    {
        var s = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = true, ContentWarnings = 2 };
        var h = JobCard.Headline("A001", s, Report(), Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — check the content defects before formatting", Msg.ContentWarnings(2)), (h.Kind, h.Title, h.Sub));
        Assert.Equal(JobCardKind.Done, JobCard.Headline("A001", s, Report(), Disks, contentConfirmed: true).Kind);
    }

    [Fact]
    public void Headline_CancelledAfterTheCopy_SaysEjectButNotFormat()
    {
        var s = new JobStatus { Phase = JobPhase.Cancelled, SafeToEject = true };
        Assert.StartsWith("The copy is done but not verified — the card is safe to eject", JobCard.Headline("A001", s, Report(false), Disks).Sub);
    }

    // Ruling I2: green (ChipTone.Ok) only where the card may be formatted — "Safe to eject" alone is neutral, also while
    // content warnings wait for a look.
    [Theory]
    [InlineData(JobPhase.Proxies, true, false, 0, false, "Safe to eject", ChipTone.Neutral, "Verifying…", ChipTone.Neutral)]
    [InlineData(JobPhase.Finishing, true, false, 0, false, "Safe to eject", ChipTone.Neutral, "Verifying…", ChipTone.Neutral)]
    [InlineData(JobPhase.Copying, false, false, 0, false, "Do not eject yet", ChipTone.Neutral, "Do not format yet", ChipTone.Neutral)]
    [InlineData(JobPhase.Done, true, true, 0, false, "Safe to eject", ChipTone.Ok, "Safe to format", ChipTone.Ok)]
    [InlineData(JobPhase.Done, true, true, 1, false, "Safe to eject", ChipTone.Neutral, "Safe to format after checking defects", ChipTone.Warning)]
    [InlineData(JobPhase.Done, true, true, 1, true, "Safe to eject", ChipTone.Ok, "Safe to format", ChipTone.Ok)]
    [InlineData(JobPhase.Done, true, false, 0, false, "Safe to eject", ChipTone.Neutral, "Do not format", ChipTone.Warning)]
    [InlineData(JobPhase.Done, false, false, 0, false, "Do not eject", ChipTone.Error, "Do not format", ChipTone.Error)]
    public void Chips_TwoVerdicts_NeverMerged(JobPhase phase, bool eject, bool format, int warnings, bool confirmed,
        string ejectText, ChipTone ejectTone, string formatText, ChipTone formatTone)
    {
        var (e, f) = JobCard.Chips(new JobStatus { Phase = phase, SafeToEject = eject, SafeToFormat = format, ContentWarnings = warnings }, confirmed);
        Assert.Equal((ejectText, ejectTone, formatText, formatTone), (e.Text, e.Tone, f.Text, f.Tone));
    }

    [Fact]
    public void Steps_VerifyInProxy_VerificationAfterTheProxies()
    {
        var config = new JobConfig("E:\\", ["D:\\"]) { Proxy = new ProxySettings { Enabled = true } };
        var steps = JobCard.Steps(new JobStatus { Phase = JobPhase.Finishing }, config, 0.5);
        Assert.Equal(["1 · Copying", "2 · Proxies", "3 · Verification", "4 · Report"], steps.Select(s => s.Text));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Active, StepState.Pending], steps.Select(s => s.State));
        config.VerifyInProxy = false;
        Assert.Equal(["1 · Copying", "2 · Verification", "3 · Proxies", "4 · Report"], JobCard.Steps(new JobStatus { Phase = JobPhase.Proxies }, config, 0).Select(s => s.Text));
        Assert.Equal(("Finishing verification", "1.00 GB / 2.00 GB"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Finishing, VerifiedBytes = 1_000_000_000, VerifyTotal = 2_000_000_000 }));
    }

    [Fact]
    public void Notifications_EjectAtCopyEnd_DoneOnlyWhenFormatSafe()
    {
        var copied = new JobStatus { Phase = JobPhase.Proxies, SafeToEject = true };
        var eject = JobOutcomes.CopyDone("A001", copied, proxies: true)!;
        Assert.Equal(("Offload: A001 — safe to eject", "Verifying with the proxies — format only after it finishes."), (eject.Title, eject.Message));
        Assert.False(eject.ForPhone);

        var done = JobOutcomes.Final("A001", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true }, Report())!;
        Assert.Equal(OutcomeKind.Done, done.Kind);
        Assert.StartsWith("Verified on 1 disk — the card is safe to format.", done.Message);
        var notYet = JobOutcomes.Final("A001", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = false }, Report())!;
        Assert.Equal(OutcomeKind.Attention, notYet.Kind);
    }

    // Spec 7, 9: every content warning of the finished card — the clip, a yellow label with the place (timecode) and the
    // kind of the defect, and whether there is a picture of it; the confirmation is offered only on a card that may be
    // formatted otherwise, and only until it is given.
    [Fact]
    public void ContentWarnings_LabelWithTheTimecode_ConfirmBeforeFormat()
    {
        var r = Report();
        r.Clips =
        [
            Entry("A001C003", new ContentResult { State = ContentResult.Warn, Kind = "decode", Timecode = "01:00:02:13", Jpeg = [0xFF, 0xD8] }),
            Entry("A001C004", new ContentResult { State = ContentResult.Ok }),
            Entry("A001C005", null),
            Entry("A001C006", new ContentResult { State = ContentResult.Warn, Kind = "end" }),
        ];
        Assert.Equal([("A001C003", "01:00:02:13 · corrupted data", true), ("A001C006", "clip ends early", false)],
            JobCard.ContentLines(r).Select(l => (l.Clip, l.Label, l.HasPicture)));
        Assert.Equal("Checked — the card is safe to format", JobCard.ContentConfirmText);

        var s = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = true, ContentWarnings = 2 };
        Assert.True(JobCard.NeedsContentConfirm(s, confirmed: false));
        Assert.False(JobCard.NeedsContentConfirm(s, confirmed: true));
        Assert.False(JobCard.NeedsContentConfirm(s with { SafeToFormat = false }, confirmed: false));   // not formattable anyway
        Assert.False(JobCard.NeedsContentConfirm(s with { Phase = JobPhase.Proxies }, confirmed: false));
        Assert.False(JobCard.NeedsContentConfirm(s with { ContentWarnings = 0 }, confirmed: false));
    }

    static ClipEntry Entry(string name, ContentResult? content) =>
        new() { Clip = new Clip { Name = name, Path = $"CLIP/{name}.mov", Segments = [$"CLIP/{name}.mov"] }, Content = content };

    static string App(string file)
    {
        var d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Directory.Build.props"))) d = Path.GetDirectoryName(d.TrimEnd('\\'));
        return Path.Combine(d ?? throw new InvalidOperationException("Directory.Build.props not found"), "src", "Offload.App", file);
    }

    // Ruling I2 (carried M-b): no green button at "Safe to eject" — "Vysunout kartu" is green only on the green (Done) card,
    // which is shown for SafeToFormat alone; the verdict chips are green only for ChipTone.Ok (Chips: SafeToFormat).
    [Fact]
    public void QueuePanel_GreenOnlyForFormatSafe()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(App("QueuePanel.xaml"));
        string? Template(XElement e) => e.Ancestors().FirstOrDefault(a => a.Name.LocalName == "DataTemplate" && a.Attribute(x + "Key") != null)?.Attribute(x + "Key")?.Value;
        var eject = doc.Descendants().Where(e => e.Name.LocalName == "Button" && (string?)e.Attribute("AutomationProperties.AutomationId") == "EjectButton").ToList();
        Assert.Equal(["CancelledCard", "DoneCard", "RunningCard"], eject.Select(Template).Order());
        foreach (var b in eject.Where(b => Template(b) != "DoneCard"))
            Assert.DoesNotContain("Success", (string?)b.Attribute("Style") ?? "");
        Assert.Contains("Success", (string?)eject.Single(b => Template(b) == "DoneCard").Attribute("Style"));

        var chip = doc.Descendants().Single(e => e.Name.LocalName == "Style" && e.Attribute(x + "Key")?.Value == "VerdictChip");
        var green = chip.Descendants().Where(e => e.Name.LocalName == "Setter" && ((string?)e.Attribute("Value") ?? "").Contains("Success"))
            .Select(e => (string?)e.Parent!.Attribute("Value")).ToList();
        Assert.Equal(["Ok"], green);
        Assert.Equal(JobCardKind.Done, JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true }, Report(), Disks).Kind);
        Assert.NotEqual(JobCardKind.Done, JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, SafeToEject = true }, Report(), Disks).Kind);
    }
}
