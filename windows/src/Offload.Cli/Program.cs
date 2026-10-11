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
        // English only: the parser errors of System.CommandLine (unknown option ...) in English whatever the PC's language.
        var en = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = en;
        CultureInfo.CurrentUICulture = en;
        // 0.11.0: the settings and history of an earlier version (%APPDATA%\ArcLoader, else %APPDATA%\Offload) copied to
        // %APPDATA%\mmMover once, before any command reads them; the note goes to stderr (stdout stays the command's own).
        Offload.Core.Desktop.AppData.MigrateFromOlder(line => Console.Error.WriteLine($"{Offload.Core.Product.Cli}: {line}"));
        // Like the Mac CLI, `copy` is the default command: `mmmover E:\ -d D:\ -d F:\` works.
        if (args.Length > 0 && !Commands.Contains(args[0]) && !args[0].StartsWith('-')) args = ["copy", .. args];
        var root = new RootCommand($"{Offload.Core.Product.Name}: camera card offload — read once, write to several disks, verify with xxh64.");
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
