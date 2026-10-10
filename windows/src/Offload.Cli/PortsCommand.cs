using System.CommandLine;
using System.Globalization;
using Offload.Core;
using Offload.Core.Hardware;

namespace Offload.Cli;

static class PortsCommand
{
    public static Command Create()
    {
        var measure = new Option<bool>("--measure") { Description = "Also measure card read and destination write/read speed (temporary file, deleted)." };
        var card = new Option<string[]>("--card") { Description = "Card or reader to check (repeatable).", DefaultValueFactory = _ => [] };
        var dest = new Option<string[]>("--dest") { Description = "Destination to check (repeatable).", DefaultValueFactory = _ => [] };
        var gb = new Option<double>("--gb") { Description = "Amount of data per device when measuring, in GB.", DefaultValueFactory = _ => 1, CustomParser = a => CliParse.Parse(a) ?? 1 };
        var cmd = new Command("ports", "Show USB ports, where cards and disks are connected and how fast, with advice.");
        foreach (var o in new Option[] { measure, card, dest, gb }) cmd.Options.Add(o);
        cmd.SetAction(r =>
        {
            using var cancel = new CancellationTokenSource();
            ConsoleCancelEventHandler onCtrlC = (_, e) =>
            {
                // Stops the measurement at once; the process must stay until the temporary file is deleted.
                e.Cancel = true;
                if (cancel.IsCancellationRequested) return;
                Console.Error.WriteLine("\nInterrupted — stopping the measurement and deleting the temporary file…");
                cancel.Cancel();
            };
            Console.CancelKeyPress += onCtrlC;
            try
            {
                var cards = (r.GetValue(card) ?? []).Select(CliPath.Full).ToList();
                var dests = (r.GetValue(dest) ?? []).Select(CliPath.Full).ToList();
                foreach (var d in dests)
                    if (cards.FirstOrDefault(c => HwTest.SameVolume(c, d)) is string c)
                        throw new ArgumentException($"destination {d} is on the same volume as card {c} — nothing is ever written to a card");
                bool byDrive = false;
                if (cards.Count == 0 && dests.Count == 0)
                {
                    // No roles given: every ready drive, labelled by its letter ("E:").
                    if (r.GetValue(measure)) throw new ArgumentException("--measure needs --card and/or --dest");
                    dests = DriveInfo.GetDrives()
                        .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                        .Select(d => d.RootDirectory.FullName).ToList();
                    byDrive = true;
                }
                var result = HwTest.Run(cards, dests, r.GetValue(measure), (long)(r.GetValue(gb) * 1_000_000_000), rolesByDrive: byDrive,
                    cancel: cancel.Token);
                Print(result);
                if (cancel.IsCancellationRequested)
                {
                    Console.WriteLine();
                    Console.WriteLine("Measurement interrupted — the result is not saved.");
                    return 1;
                }
                try
                {
                    HwTest.Save(result, HwTest.DefaultPath);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Warning: the test result could not be saved: {e.Message}");
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= onCtrlC;
            }
        });
        return cmd;
    }

    static void Print(HwTestResult r)
    {
        Console.WriteLine("Connections:");
        foreach (var d in r.Drives)
        {
            // Without roles the role is the drive letter: no second column repeating it.
            var root = d.Link.Root.TrimEnd('\\');
            var where = d.Role == root ? $"{d.Role,-8}" : $"{d.Role,-8} {d.Link.Root,-6}";
            Console.WriteLine($"  {where} {d.Link.Describe(r.Connectors)}");
        }
        foreach (var group in r.Connectors.Where(c => c.UserVisible).GroupBy(c => c.ControllerId))
        {
            Console.WriteLine();
            Console.WriteLine($"USB ports ({group.First().ControllerName}):");
            foreach (var c in group) Console.WriteLine($"  {PortLabels.ListRow(c, r.Drives)}");
        }
        if (r.Measurements.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Measurement:");
            foreach (var m in r.Measurements)
                Console.WriteLine($"  {m.Role,-8} {m.Root,-6} read {Mb(m.ReadMBps)}  write {Mb(m.WriteMBps)}");
        }
        Console.WriteLine();
        Console.WriteLine("Advice:");
        foreach (var a in r.Advice) Console.WriteLine($"  {(a.Kind == AdviceKind.Warning ? "!" : a.Kind == AdviceKind.Info ? "i" : "·")} {a.Text}");
    }

    static string Mb(double? v) => v is double x ? x.ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "—";
}
