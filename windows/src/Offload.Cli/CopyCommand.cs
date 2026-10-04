using System.CommandLine;
using Offload.Core;

namespace Offload.Cli;

static class CopyCommand
{
    public static Command Create()
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", Required = true };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label)." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
        var buffers = new Option<int>("--buffers") { Description = "Blocks in flight.", DefaultValueFactory = _ => 16 };
        var noVerify = new Option<bool>("--no-verify") { Description = "Skip read-back verification (not recommended)." };
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun }) cmd.Options.Add(o);
        cmd.SetAction(r =>
        {
            JobConfig config;
            try
            {
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), r.GetValue(dest)!.Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name),
                    Date = r.GetValue(date),
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                };
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
            return Run(config, r.GetValue(dryRun));
        });
        return cmd;
    }

    static int Run(JobConfig config, bool dryRun)
    {
        var job = new OffloadJob(config);
        OffloadPlan plan;
        try
        {
            plan = job.Prepare();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            return 1;
        }
        Console.WriteLine($"Card {plan.CardName}: {plan.Scan.Files.Count} files, {Format.Bytes(plan.Scan.TotalBytes)}");
        foreach (var p in plan.CardPaths) Console.WriteLine($"  → {p}");
        if (dryRun) return 0;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("\nCancelling — finishing current block, partial files will be removed…");
            job.Cancel();
        };

        OffloadReport? report;
        using (new ProgressDisplay(job)) report = job.Run();

        var status = job.Status();
        if (report != null)
        {
            Console.WriteLine();
            Console.Write(report.Summary);
            Console.WriteLine($"Report: {Path.Combine(plan.CardPaths[0], Layout.ReportDirName, plan.CardName + "_report.html")}");
        }
        if (status.Error != null) Console.WriteLine($"ERROR: {status.Error}");
        return status.Phase == JobPhase.Done && report?.Success == true ? 0 : 1;
    }
}
