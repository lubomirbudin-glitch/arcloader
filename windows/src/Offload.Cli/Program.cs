using System.CommandLine;
using System.Globalization;
using System.Text;

namespace Offload.Cli;

static class Program
{
    static readonly string[] Commands = ["copy", "append", "verify", "day-report", "summary-report", "bench", "ports", "luts"];

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Parser errors of System.CommandLine (unknown option ...) in Czech: the package ships a "cs" satellite.
        var cs = CultureInfo.GetCultureInfo("cs-CZ");
        CultureInfo.DefaultThreadCurrentUICulture = cs;
        CultureInfo.CurrentUICulture = cs;
        // Like the Mac CLI, `copy` is the default command: `offload E:\ -d D:\ -d F:\` works.
        if (args.Length > 0 && !Commands.Contains(args[0]) && !args[0].StartsWith('-')) args = ["copy", .. args];
        var root = new RootCommand("Camera card offload: read once, write to several disks, verify with xxh64.");
        root.Subcommands.Add(CopyCommand.Create());
        root.Subcommands.Add(AppendCommand.Create());
        root.Subcommands.Add(VerifyCommand.Create());
        root.Subcommands.Add(DayReportCommand.Create());
        root.Subcommands.Add(SummaryReportCommand.Create());
        root.Subcommands.Add(BenchCommand.Create());
        root.Subcommands.Add(PortsCommand.Create());
        root.Subcommands.Add(LutsCommand.Create());
        return root.Parse(args).Invoke();
    }
}
