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
        var hash = new Option<string>("--hash") { Description = "Checksums to measure: xxh64 (default) or md5 (xxh64 + MD5, as a copy with --hash both).", DefaultValueFactory = _ => "xxh64" };
        var cmd = new Command("bench", "Measure card read speed and disk write/read speed.");
        cmd.Options.Add(read);
        cmd.Options.Add(write);
        cmd.Options.Add(gb);
        cmd.Options.Add(hash);
        cmd.SetAction(r =>
        {
            long bytes = (long)(r.GetValue(gb) * 1_000_000_000);
            var kinds = r.GetValue(hash) switch
            {
                "xxh64" => HashKind.Xxh64,
                "md5" or "both" => HashKind.Both,
                _ => (HashKind?)null,
            };
            if (kinds is not HashKind chosen)
            {
                Console.Error.WriteLine(Msg.CliError + Msg.UnknownHash(r.GetValue(hash)));
                return 1;
            }
            try
            {
                Console.WriteLine("CPU");
                Print(Bench.Hash(256 << 20));
                if (chosen.HasFlag(HashKind.Md5)) Print(Bench.Hash(256 << 20, "md5"));
                if (r.GetValue(read) is string rd)
                {
                    Console.WriteLine($"Read {rd}");
                    foreach (var l in Bench.Read(CliPath.Full(rd), bytes, [64 << 10, 1 << 20, 4 << 20, 16 << 20])) Print(l);
                }
                foreach (var w in r.GetValue(write) ?? [])
                {
                    Console.WriteLine($"Write {w}");
                    foreach (var l in Bench.Write(CliPath.Full(w), bytes, 16 << 20, kinds: chosen)) Print(l);
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
        });
        return cmd;
    }

    static void Print(Bench.Line l) => Console.WriteLine($"  {l.Label,-22} {Format.Bytes(l.Bytes),-10} {l.Rate}");
}
