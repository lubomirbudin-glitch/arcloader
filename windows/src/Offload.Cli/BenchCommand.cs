using System.CommandLine;
using Offload.Core;

namespace Offload.Cli;

static class BenchCommand
{
    public static Command Create()
    {
        var read = new Option<string?>("--read") { Description = "Card or file to read from." };
        var write = new Option<string[]>("--write") { Description = "Destination folder to test write speed (repeatable).", DefaultValueFactory = _ => [] };
        var gb = new Option<double>("--gb") { Description = "Amount of data per test in GB.", DefaultValueFactory = _ => 2, CustomParser = a => CliParse.Parse(a) ?? 2 };
        var cmd = new Command("bench", "Measure card read speed and disk write/read speed.");
        cmd.Options.Add(read);
        cmd.Options.Add(write);
        cmd.Options.Add(gb);
        cmd.SetAction(r =>
        {
            long bytes = (long)(r.GetValue(gb) * 1_000_000_000);
            try
            {
                Console.WriteLine("CPU");
                Print(Bench.Hash(256 << 20));
                if (r.GetValue(read) is string rd)
                {
                    Console.WriteLine($"Read {rd}");
                    foreach (var l in Bench.Read(CliPath.Full(rd), bytes, [64 << 10, 1 << 20, 4 << 20, 16 << 20])) Print(l);
                }
                foreach (var w in r.GetValue(write) ?? [])
                {
                    Console.WriteLine($"Write {w}");
                    foreach (var l in Bench.Write(CliPath.Full(w), bytes, 16 << 20)) Print(l);
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

    static void Print(Bench.Line l) => Console.WriteLine($"  {l.Label,-22} {Format.Bytes(l.Bytes),-10} {l.Rate}");
}
