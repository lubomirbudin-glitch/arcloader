using System.CommandLine;
using Offload.Core;

namespace Offload.Cli;

static class LutsCommand
{
    public static Command Create()
    {
        var cmd = new Command("luts", "List the built-in LUTs (ids for --lut and --lut-fallback).");
        cmd.SetAction(_ =>
        {
            var cat = LutCatalog.Default;
            if (cat.Luts.Count == 0) Console.Error.WriteLine("No built-in LUTs found (luts folder missing).");
            foreach (var l in cat.Luts) Console.WriteLine($"{l.Id} — {l.Name}");
            return 0;
        });
        return cmd;
    }
}
