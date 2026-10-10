using System.Text.Json;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Spec 4.1: the project a job runs in goes into history.json (§5.2) and the report; "next in line" (§2.3) and the day
/// report header (§3.2) follow it.
public class ProjectJobTests
{
    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, string? project, IReadOnlyList<string>? appendInto = null) =>
        new(new JobConfig(card, dests)
        {
            Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history, AppendInto = appendInto, Project = project,
        }, AppendFixtures.Options());

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 20_000, 1);
        return card;
    }

    [Fact]
    public void Project_IntoHistoryAndReport()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var job = Job(Card(dir), [dir.Sub("d1")], history, "Film X");

        job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Equal("Film X", Assert.Single(new HistoryStore(history).Load()).Project);
        var json = File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Film X", doc.RootElement.GetProperty("project").GetString());
        Assert.Equal("Film X", CardRecord.Load(job.Plan.CardPaths[0]).Project);
    }

    // "Bez projektu": the key is absent from the report (the Mac format is unchanged), history says null.
    [Fact]
    public void NoProject_NoKey_HistoryNull()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var job = Job(Card(dir), [dir.Sub("d1")], history, project: null);

        job.Run();

        Assert.Null(Assert.Single(new HistoryStore(history).Load()).Project);
        var json = File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json"));
        Assert.DoesNotContain("\"project\"", json);
        Assert.Null(CardRecord.Load(job.Plan.CardPaths[0]).Project);
    }

    // An append adds a generation to the entry of the earlier backup: that entry's project stays.
    [Fact]
    public void Append_KeepsTheEntrysProject()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history, "Film X");
        full.Run();
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);

        var append = Job(card, [], history, "Reklama Y", appendInto: full.Plan!.CardPaths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.Equal("Film X", entry.Project);
        Assert.Equal([1, 2], entry.Generations.Select(g => g.Seq));
        Assert.Equal("Film X", CardRecord.Load(full.Plan.CardPaths[0]).Project);   // the first generation's
    }

    // M4: the append's own report (generation 2) names the earlier backup's project too, not the one the caller passed.
    [Fact]
    public void Append_ReportRecordsTheEarlierBackupsProject()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history, "Film X");
        full.Run();
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);
        Job(card, [], history, "Reklama Y", appendInto: full.Plan!.CardPaths).Run();

        var record = CardRecord.Load(full.Plan.CardPaths[0]);
        Assert.Equal(2, record.Generations.Count);
        Assert.All(record.Generations, g => Assert.Equal("Film X", g.Report.Project));

        // a card of "Bez projektu" stays so, whatever the append says
        var card2 = dir.Sub("B001");
        TestFs.WriteRandom(Path.Combine(card2, "CLIP", "B001C001.mov"), 20_000, 3);
        var plain = Job(card2, [dir.Sub("d2")], history, null);
        plain.Run();
        AppendFixtures.NewClip(card2, "B001C002.mov", seed: 4);
        Job(card2, [], history, "Reklama Y", appendInto: plain.Plan!.CardPaths).Run();
        Assert.Null(CardRecord.Load(plain.Plan.CardPaths[0]).Project);
    }

    // Review: the first destination whose report is missing does not decide; the next readable one does.
    [Fact]
    public void Append_FirstDestinationReportMissing_SecondDecides()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1"), dir.Sub("d2")], history, "Film X");
        full.Run();
        var paths = full.Plan!.CardPaths;
        Directory.Delete(Path.Combine(paths[0], "_REPORT"), true);
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);

        Assert.Equal("Film X", OffloadJob.EarlierProject(paths, "Reklama Y"));
        Assert.Equal("Reklama Y", OffloadJob.EarlierProject([paths[0]], "Reklama Y"));   // none readable: the fallback
    }

    static HistoryEntry Copied(string cardName, string? project) => new() { CardName = cardName, Date = "2026-10-06", Project = project };

    // Spec 2.3 + 4.1: "next in line" counts the cards of the active project only (its name in any case).
    [Fact]
    public void NextInLine_PerProject()
    {
        HistoryEntry[] h = [Copied("A004", null), Copied("A001", "Film X"), Copied("B007", "Reklama Y")];
        var plain = new CardInsight { Source = @"E:\" };
        Assert.Equal("A005", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS").Name);
        Assert.Equal("A002", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS", project: "FILM X").Name);
        Assert.Equal("B008", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS", project: "Reklama Y").Name);
        Assert.Equal("EOS", CardNaming.Placeholder(true, plain, h, "2026-10-06", "2026-10-06", "EOS", project: "New").Name);
        var r = CardNaming.Resolve(true, [new CardNameInput("E", null, plain, "EOS"), new CardNameInput("F", null, plain, "EOS")],
            h, "2026-10-06", "2026-10-06", "Film X");
        Assert.Equal(("A002", "A003"), (r["E"].Name, r["F"].Name));
    }

    static void Report(string day, string card, string? project)
    {
        var path = Path.Combine(day, card);
        var r = Reports.Sample(path);
        r.Project = project;
        Directory.CreateDirectory(Path.Combine(path, "_REPORT"));
        File.WriteAllText(Path.Combine(path, "_REPORT", "offload_report.json"), r.ToJson());
    }

    // Spec 3.2: the day report names the project only when every card with a readable report has the same one.
    [Fact]
    public void DayReport_ProjectInTheHeader_OnlyWhenAllCardsAgree()
    {
        using var dir = new TempDir();
        var day = dir.Sub("D", "2026-10-06");
        Report(day, "A001", "Film X");
        Report(day, "A002", "film x");
        Directory.CreateDirectory(Path.Combine(day, "A003", "ascmhl"));   // a card without a report does not count
        var model = DayReport.Load(day);
        Assert.Equal("Film X", model.Project);
        Assert.Contains("<span class=\"sub\">Project: Film X</span>", DayReport.Html(model));

        Report(day, "B001", null);                                          // a card of no project (or an older report)
        Assert.Null(DayReport.Load(day).Project);
        Assert.DoesNotContain("Project:", DayReport.Html(DayReport.Load(day)));

        using var other = new TempDir();
        var none = other.Sub("D", "2026-10-06");
        Report(none, "A001", null);
        Assert.Null(DayReport.Load(none).Project);
    }
}
