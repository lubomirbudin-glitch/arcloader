using Offload.Core.Append;

namespace Offload.Core.Tests;

/// Task 15, requirement 3: every refusal of the append reaches the job card (Status.Error) as the engine's Czech text,
/// with nothing written — the GUI shows ErrorText and the CLI prints "Chyba: " + the same text.
public class AppendRefusalJobTests
{
    static OffloadJob Job(string card, IReadOnlyList<string> paths, string history) =>
        new(new JobConfig(card, []) { Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history, AppendInto = paths },
            AppendFixtures.Options());

    static void AssertRefused(OffloadJob job, IReadOnlyList<string> paths, string expected)
    {
        var before = paths.Select(AppendFixtures.Tree).ToList();
        var report = job.Run();
        var s = job.Status();
        Assert.Null(report);
        Assert.Equal(JobPhase.Failed, s.Phase);
        Assert.Equal(expected, s.Error);
        Assert.Null(s.Note);
        Assert.DoesNotContain("Exception", s.Error);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(before[d], paths[d]);
    }

    static string PlannerMessage(string card, IReadOnlyList<string> paths) =>
        Assert.Throws<OffloadError>(() => AppendPlanner.Plan(card, paths)).Message;

    [Fact]
    public void OtherCard()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        Directory.Delete(card, recursive: true);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "B007C001.mov"), 70_000, 9);
        AssertRefused(Job(card, paths, dir.Sub("h")), paths, Msg.AppendOtherCard);
    }

    [Fact]
    public void ChangedFile_Conflict()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var c1 = Path.Combine(card, "CLIP", "A001C001.mov");
        var t = File.GetLastWriteTimeUtc(c1);
        File.AppendAllText(c1, "x");
        File.SetLastWriteTimeUtc(c1, t);
        var expected = PlannerMessage(card, paths);
        Assert.Contains("1 soubor se neshoduje se zálohou", expected);
        AssertRefused(Job(card, paths, dir.Sub("h")), paths, expected);
    }

    [Fact]
    public void UnknownFileOnADestination_Conflict()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        AppendFixtures.NewClip(paths[0], size: 999, seed: 9);   // the new clip's name already on d1, in no report
        var expected = PlannerMessage(card, paths);
        Assert.Contains("žádný report nezná", expected);
        AssertRefused(Job(card, paths, dir.Sub("h")), paths, expected);
    }

    [Fact]
    public void DamagedMhl()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        File.AppendAllText(Directory.GetFiles(Path.Combine(paths[1], "ascmhl"), "0001_*.mhl").Single(), " ");
        AssertRefused(Job(card, paths, dir.Sub("h")), paths, Msg.AppendMhlDamaged(paths[1]));
    }

    [Fact]
    public void ReportsAheadOfTheMhl_I1()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        foreach (var p in paths) File.WriteAllText(Path.Combine(p, "_REPORT", "A001_report_0002.html"), "<html/>");
        AppendFixtures.NewClip(card);
        var expected = PlannerMessage(card, paths);
        Assert.Contains("nesouhlasí reporty s ASC MHL", expected);
        AssertRefused(Job(card, paths, dir.Sub("h")), paths, expected);
    }

    [Fact]
    public void Busy_LockHeldByAnotherAppend()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var job = Job(card, paths, dir.Sub("h"));
        string? error = null;
        // A mutex is owned per thread: hold it here, run the job on another thread.
        using (AppendLock.Acquire(paths))
        {
            var before = paths.Select(AppendFixtures.Tree).ToList();
            var t = new Thread(() => { job.Run(); error = job.Status().Error; });
            t.Start();
            t.Join();
            for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(before[d], paths[d]);
        }
        Assert.Equal(JobPhase.Failed, job.Status().Phase);
        Assert.Contains(error, paths.Select(Msg.AppendBusy));
    }
}
