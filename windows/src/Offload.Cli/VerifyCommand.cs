using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Offload.Core;

namespace Offload.Cli;

static class VerifyCommand
{
    public static Command Create()
    {
        var cardPath = new Argument<string>("cardPath") { Description = "Card folder (<root>/<date>/<card>)." };
        var cmd = new Command("verify", "Re-verify an existing card folder against its _REPORT hash list.");
        cmd.Arguments.Add(cardPath);
        cmd.SetAction(r =>
        {
            string full;
            try
            {
                full = CliPath.Full(r.GetValue(cardPath)!);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
            return Run(full);
        });
        return cmd;
    }

    static int Run(string cardPath)
    {
        var sw = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        Verifier.Result r;
        try
        {
            r = Verifier.Verify(cardPath, progress: (file, bytes) =>
            {
                if (sw.Elapsed - last < TimeSpan.FromMilliseconds(500)) return;
                last = sw.Elapsed;
                ConsoleLine.Write($"{Format.Bytes(bytes)}  {file}");
            });
        }
        catch (Exception e)
        {
            ConsoleLine.Clear();
            Console.Error.WriteLine(Msg.CliError + e.Message);
            return 1;
        }
        ConsoleLine.Clear();
        double secs = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"Verified {r.Ok.Count} files, {Format.Bytes(r.Bytes)} in {secs.ToString("0.0", CultureInfo.InvariantCulture)} s ({Format.Rate(r.Bytes, secs)})");
        foreach (var m in r.Mismatched) Console.WriteLine($"MISMATCH  {m}");
        foreach (var m in r.Missing) Console.WriteLine($"MISSING   {m}");
        foreach (var m in r.Unreadable) Console.WriteLine($"ERROR     {m}");
        foreach (var m in r.Extra) Console.WriteLine($"EXTRA     {m} (not in hash list)");
        Console.WriteLine(r.Success ? "OK" : "FAILED");
        return r.Success ? 0 : 1;
    }
}
