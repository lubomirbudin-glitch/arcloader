using System.CommandLine;
using Offload.Cli;

namespace Offload.Core.Tests;

/// Console and the job factory are process-wide: these tests run one at a time.
[CollectionDefinition("Console", DisableParallelization = true)]
public class ConsoleCollection;

/// Task 15 fix round 1, item 3: the exit codes and lines of `offload append` / RunJob.
[Collection("Console")]
public class CliAppendTests
{
    static (int Exit, string Out, string Err) Run(Func<int> action)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var old = CopyCommand.JobFactory;
        Console.SetOut(so);
        Console.SetError(se);
        CopyCommand.JobFactory = c => new OffloadJob(c, AppendFixtures.Options());
        try { return (action(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
    }

    static JobConfig Config(string card, IReadOnlyList<string> into, string history) =>
        new(card, []) { Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history, AppendInto = into };

    [Fact]
    public void NothingNew_Exit0_WithTheNote()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var r = Run(() => CopyCommand.RunJob(Config(card, paths, dir.Sub("h")), dryRun: false));
        Assert.Equal(0, r.Exit);
        Assert.Contains(Msg.NothingNew, r.Out);
        Assert.DoesNotContain("Error", r.Out + r.Err);
    }

    [Fact]
    public void NothingNew_DryRun_Exit0()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var r = Run(() => CopyCommand.RunJob(Config(card, paths, dir.Sub("h")), dryRun: true));
        Assert.Equal(0, r.Exit);
        Assert.Contains(Msg.NothingNew, r.Out);
    }

    [Fact]
    public void Append_Exit0_PlanLineInCzech_ReportOfGeneration2()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var r = Run(() => CopyCommand.RunJob(Config(card, paths, dir.Sub("h")), dryRun: false));
        Assert.Equal(0, r.Exit);
        Assert.Contains("Append to card A001 (generation 2), plan (may still change): 1 file · ", r.Out);
        Assert.DoesNotContain(" files", r.Out);
        Assert.Contains("A001_report_0002.html", r.Out);
    }

    [Fact]
    public void Refusal_Exit1_WithTheCzechText()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        Directory.Delete(card, recursive: true);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "B007C001.mov"), 70_000, 9);
        var r = Run(() => CopyCommand.RunJob(Config(card, paths, dir.Sub("h")), dryRun: false));
        Assert.Equal(1, r.Exit);
        Assert.Contains(Msg.CliError + Msg.AppendOtherCard, r.Err + r.Out);
    }

    [Fact]
    public void RefusalDuringTheRun_Exit1_BusyLock()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        int exit = 0; string text = "";
        using (Offload.Core.Append.AppendLock.Acquire(paths))
        {
            var t = new Thread(() => { var r = Run(() => CopyCommand.RunJob(Config(card, paths, dir.Sub("h")), dryRun: false)); exit = r.Exit; text = r.Out + r.Err; });
            t.Start();
            t.Join();
        }
        Assert.Equal(1, exit);
        Assert.Contains(Msg.CliError, text);
        Assert.Contains(paths.Select(Msg.AppendBusy).First(m => text.Contains(m)), text);
    }

    [Fact]
    public void WrongInto_Exit1()
    {
        using var dir = new TempDir();
        var (card, _) = AppendFixtures.CopiedCard(dir);
        var missing = Path.Combine(dir.Path, "nope", "2026-10-06", "A001");
        var r = Run(() => AppendCommand.Create().Parse([card, "--into", missing, "--no-history", "--no-analyze"]).Invoke());
        Assert.Equal(1, r.Exit);
        Assert.Contains(Msg.CliError + "The earlier backup is not available", r.Err);
    }

    [Fact]
    public void NoInto_NoHistory_Exit1()
    {
        using var dir = new TempDir();
        var (card, _) = AppendFixtures.CopiedCard(dir);
        var r = Run(() => AppendCommand.Create().Parse([card, "--no-history", "--no-analyze"]).Invoke());
        Assert.Equal(1, r.Exit);
        Assert.Contains(Msg.CliError + Msg.CardNotInHistory, r.Err);
    }

    [Fact]
    public void PlanLine_OfAFullCopy_Unchanged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 1000, 1);
        var plan = new OffloadEngine(AppendFixtures.Options()).Prepare(card, [dir.Sub("d1")], null, "2026-10-06");
        Assert.StartsWith("Card A001: 1 files, ", CopyCommand.PlanLine(plan));
    }
}
