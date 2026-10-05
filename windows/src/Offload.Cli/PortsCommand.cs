using System.CommandLine;
using System.Globalization;
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
            try
            {
                var cards = (r.GetValue(card) ?? []).Select(CliPath.Full).ToList();
                var dests = (r.GetValue(dest) ?? []).Select(CliPath.Full).ToList();
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
                var result = HwTest.Run(cards, dests, r.GetValue(measure), (long)(r.GetValue(gb) * 1_000_000_000), rolesByDrive: byDrive);
                Print(result);
                try
                {
                    HwTest.Save(result, HwTest.DefaultPath);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Varování: výsledek testu nešlo uložit: {e.Message}");
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
        });
        return cmd;
    }

    static void Print(HwTestResult r)
    {
        Console.WriteLine("Připojení:");
        foreach (var d in r.Drives) Console.WriteLine($"  {d.Role,-8} {d.Link.Root,-6} {d.Link.Description}");
        Console.WriteLine();
        Console.WriteLine("USB porty:");
        foreach (var c in r.Connectors.Where(c => c.UserVisible))
            Console.WriteLine($"  {c.DefaultLabel,-22} {(c.Device is { } dev ? $"{dev.Name} — {UsbSpeeds.Label(dev.Link)}" : "volný")}");
        if (r.Measurements.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Měření:");
            foreach (var m in r.Measurements)
                Console.WriteLine($"  {m.Role,-8} {m.Root,-6} čtení {Mb(m.ReadMBps)}  zápis {Mb(m.WriteMBps)}");
        }
        Console.WriteLine();
        Console.WriteLine("Doporučení:");
        foreach (var a in r.Advice) Console.WriteLine($"  {(a.Kind == AdviceKind.Warning ? "!" : a.Kind == AdviceKind.Info ? "i" : "·")} {a.Text}");
    }

    static string Mb(double? v) => v is double x ? x.ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "—";
}
