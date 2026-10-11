using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// `mmMover.exe --demo` (off by default): the states of docs/ui-navrh/screeny, checked through the same Core code the
/// window binds — so the visual check compares the design with what the app really computes.
public class DemoScenarioTests
{
    static readonly string[] Roots = [@"D:\demo\RAID_A", @"F:\demo\SHUTTLE_B"];
    static readonly string[] Disks = ["RAID_A", "SHUTTLE_B"];
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Parse_AndStartupOptions_OffByDefault()
    {
        Assert.Equal([DemoState.Ready, DemoState.Running, DemoState.Finished, DemoState.Settings, DemoState.Verifying],
            new[] { "pripraveno", "probiha", "HOTOVO", "nastaveni", "overuje" }.Select(s => DemoScenario.Parse(s)!.Value));
        Assert.Null(DemoScenario.Parse("x"));
        Assert.Null(StartupOptions.Parse([]).Demo);
        Assert.Equal(DemoState.Running, StartupOptions.Parse(["--demo", "probiha"]).Demo);
        Assert.Null(StartupOptions.Parse(["--demo"]).Demo);   // no state: no demo
        Assert.Empty(StartupOptions.Parse(["--demo", "hotovo"]).Sources);
    }

    [Fact]
    public void Ready_TheCardsOfScreen1()
    {
        var ready = DemoScenario.Ready(@"C:\demo");
        Assert.Equal("24 clips · 1.70 GB · exFAT · reader USB 3 · 5 Gb/s",
            SetupTexts.CardFacts(ready.Insight, ready.Total - ready.Free, ready.Format, ready.Connection, looksLikeCard: true));
        Assert.Equal("ALEXA 35 · LogC4", ready.Insight.Camera!.Text);
        var backedUp = DemoScenario.BackedUp(@"C:\demo", Roots);
        Assert.True(AppendRules.Offer(backedUp.Insight));
        Assert.Equal("3 cards · 412 GB · all verified", HistoryView.Today(DemoScenario.Today(Roots, DateTime.Now), DateTime.Now)!.Summary);
    }

    // Plan 10 (R5): `--demo overuje` shows both verdicts for the screenshots — a card copied and verifying with its
    // proxies ("Lze vyjmout", neutral, "Ověřuje se…"), one with a content defect waiting for a look ("Lze formátovat po
    // kontrole vad", the yellow label with its timecode) and the green one ("Lze formátovat").
    [Fact]
    public void Verifying_BothVerdicts()
    {
        var verifying = DemoScenario.Verifying(@"C:\demo\A036_ARRI", Roots);
        var s = verifying.Status();
        Assert.Equal(JobCardKind.Running, JobCard.Headline("A036_ARRI", s, null, Disks).Kind);
        var (eject, format) = JobCard.Chips(s);
        Assert.Equal(("Safe to eject", ChipTone.Neutral, "Verifying…", ChipTone.Neutral), (eject.Text, eject.Tone, format.Text, format.Tone));
        Assert.Equal(["1 · Copying", "2 · Proxies", "3 · Verification", "4 · Report"], JobCard.Steps(s, verifying.Config, 0.4).Select(x => x.Text));
        Assert.Equal(("Proxy 10 / 24", "A036C010_261007_R2KQ · 40 %"), JobCard.FileLine(s));
        Assert.False(verifying.RunCopy());   // a preview never runs

        var defect = DemoScenario.ContentDefect(@"C:\demo\A037_ARRI", Roots, _ => [0xFF, 0xD8]);
        var h = JobCard.Headline("A037_ARRI", defect.Status(), defect.Report, Disks);
        Assert.Equal((JobCardKind.Problem, "A037_ARRI — check the content defects before formatting"), (h.Kind, h.Title));
        Assert.Equal(("Safe to eject", "Safe to format after checking defects", ChipTone.Warning),
            (JobCard.Chips(defect.Status()).Eject.Text, JobCard.Chips(defect.Status()).Format.Text, JobCard.Chips(defect.Status()).Format.Tone));
        Assert.Equal(("A037C007", "01:00:12:05 · corrupted data", true), JobCard.ContentLines(defect.Report!).Single() is var l ? (l.Clip, l.Label, l.HasPicture) : default);
        Assert.True(JobCard.NeedsContentConfirm(defect.Status(), confirmed: false));
        Assert.Equal((JobCardKind.Done, "A037_ARRI — safe to format"),
            JobCard.Headline("A037_ARRI", defect.Status(), defect.Report, Disks, contentConfirmed: true) is var c ? (c.Kind, c.Title) : default);

        var done = DemoScenario.Done(@"C:\demo\A035_ARRI", Roots, null);
        Assert.Equal(("Safe to eject", ChipTone.Ok, "Safe to format", ChipTone.Ok),
            JobCard.Chips(done.Status()) is var (e2, f2) ? (e2.Text, e2.Tone, f2.Text, f2.Tone) : default);
    }

    [Fact]
    public void Running_CopiesAt661_ThenWaitingAppend()
    {
        double t = 0;
        var job = DemoScenario.Running(@"C:\demo\A035_ARRI", Roots, () => t);
        var p = new JobProgress(T0);
        p.Update(job.Status(), T0.AddSeconds(0.5));   // the first tick of the copy: the baseline
        t = 1;
        p.Update(job.Status(), T0.AddSeconds(1.5));

        Assert.Equal(JobCardKind.Running, JobCard.Headline("A035_ARRI", p.Status, null, Disks).Kind);
        Assert.Equal(0.38, p.PhaseProgress, 2);
        var rows = JobCard.Rows(p, Disks);
        Assert.Equal(["Card read", "RAID_A", "SHUTTLE_B"], rows.Select(r => r.Name));
        Assert.Equal("661 MB/s", rows[0].RateText);
        Assert.Equal("1 · Copying", JobCard.Steps(p.Status, job.Config, p.PhaseProgress)[0].Text);
        Assert.Equal("File 10 / 24", JobCard.FileLine(p.Status).Caption);
        Assert.False(job.RunCopy());   // a preview never runs

        job.Pause();   // the screenshot of a paused card: Pozastavit holds at once
        p.Update(job.Status(), T0.AddSeconds(2.5));
        Assert.Equal("Paused", p.StatusText);
        Assert.Equal("—", JobCard.Rows(p, Disks)[0].RateText);
        job.Resume();
        Assert.False(job.Status().Paused);

        var (waiting, facts) = DemoScenario.WaitingAppend(@"C:\demo\B012_FX6", Roots);
        Assert.Equal(JobPhase.Waiting, waiting.Status().Phase);
        Assert.Equal("2 new clips · 14.2 GB · same destinations", JobCard.Sub(facts));
        job.Cancel();
        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);   // closing the demo window ends it
    }

    [Fact]
    public void Finished_GreenAndYellow()
    {
        var done = DemoScenario.Done(@"C:\demo\A035_ARRI", Roots, _ => [0xFF, 0xD8]);
        var h = JobCard.Headline("A035_ARRI", done.Status(), done.Report, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — safe to format", "All 24 files are on 2 disks and verified (xxHash64)."), (h.Kind, h.Title, h.Sub));
        Assert.Equal(["602 MB/s", "799 MB/s", "24 / 24", "6 min 12 s"], JobCard.Summary(done.Report!, null).Select(f => f.Value));
        Assert.Equal(5, JobCard.Thumbs(done.Report!).Thumbs.Count);

        var bad = DemoScenario.Mismatch(@"C:\demo\B012_FX6", Roots);
        h = JobCard.Headline("B012_FX6", bad.Status(), bad.Report, Disks);
        Assert.Equal((JobCardKind.Problem, "B012_FX6 — do not eject the card yet", "1 file on disk SHUTTLE_B does not match the card. Everything is fine on RAID_A."),
            (h.Kind, h.Title, h.Sub));
        var row = Assert.Single(JobCard.FailedRows(bad.Report!, Disks));
        Assert.Equal((@"XDROOT\Clip\B012C015_261007_9QW1.MXF", "RAID_A ✓", "SHUTTLE_B ✕ hash"), (row.Path, row.Disks[0].Text, row.Disks[1].Text));
        Assert.True(RepairRules.Offer(bad.Status().Phase, bad.Report, cardPresent: true, repairPending: false));
    }
}

/// Ruling I3: the demo is sealed — its own app-data folder and every command that starts work or writes anything off.
public class DemoSealTests
{
    [Fact]
    public void EveryCommandThatWorksOrWrites_IsOffInTheDemo()
    {
        foreach (var state in Enum.GetValues<DemoState>())
            foreach (var command in Enum.GetValues<DemoCommand>())
            {
                Assert.True(DemoGate.Allows(null, command), $"{command}: a normal run is not limited");
                Assert.False(DemoGate.Allows(state, command), $"{state}/{command}: a real job's command must be off in the demo");
            }
        // Only the in-memory buttons of a preview job (it never runs) work, for the screenshot of a paused card.
        var allowedOnPreview = Enum.GetValues<DemoCommand>().Where(c => DemoGate.Allows(DemoState.Running, c, previewJob: true)).ToList();
        Assert.Equal([DemoCommand.Pause, DemoCommand.Resume, DemoCommand.Cancel], allowedOnPreview);
    }

    [Fact]
    public void TheCommands_AreTheOnesOfTheRuling()
    {
        Assert.Equal(["Append", "Cancel", "DayReport", "Eject", "MeasurePorts", "Notify", "Pause", "ProjectEdit", "Repair", "Resume", "SettingsSave", "Start", "SummaryReport", "UpdateCheck", "VerifyBackup"],
            Enum.GetNames<DemoCommand>().Order());
    }

    [Fact]
    public void TheWindowAsksTheGate_ForEveryCommand()
    {
        // The test project does not reference Offload.App, so the wiring is checked in its source: every command is
        // asked about by name in the view-model files.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Offload.App"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = string.Concat(Directory.GetFiles(Path.Combine(dir!.FullName, "src", "Offload.App"), "*.cs").Select(File.ReadAllText));
        foreach (var command in Enum.GetValues<DemoCommand>())
            Assert.True(source.Contains($"DemoCommand.{command}"), $"{command} is not checked in the window");
        Assert.Contains("AppData.UseDemoFolder()", source);
    }

    [Fact]
    public void AppData_DemoFolderWinsOverTheEnvironment()
    {
        Assert.Equal(@"C:\R\mmMover", AppData.Resolve(null, null, @"C:\R"));
        Assert.Equal(@"D:\iso", AppData.Resolve(@"D:\iso", null, @"C:\R"));
        Assert.Equal(@"C:\Temp\Offload-demo-1", AppData.Resolve(@"D:\iso", @"C:\Temp\Offload-demo-1", @"C:\R"));   // MMMOVER_ / ARCLOADER_ / OFFLOAD_SETTINGS_DIR unset or set
        Assert.Equal(@"C:\Temp\Offload-demo-1", AppData.Resolve(null, @"C:\Temp\Offload-demo-1", @"C:\R"));
    }

    [Fact]
    public void APreviewJob_NeverRuns_AndPauseCancelAreInMemory()
    {
        var roots = new[] { @"D:\demo\A", @"F:\demo\B" };
        var job = DemoScenario.Running(@"C:\demo\A035_ARRI", roots, () => 0);
        Assert.True(job.IsPreview);
        Assert.False(job.RunCopy());
        job.RunPost();   // nothing: no exception, no file
    }
}
