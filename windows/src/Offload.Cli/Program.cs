using System.CommandLine;
using System.Text;

namespace Offload.Cli;

static class Program
{
    static readonly string[] Commands = ["copy", "verify", "bench"];

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Like the Mac CLI, `copy` is the default command: `offload E:\ -d D:\ -d F:\` works.
        if (args.Length > 0 && !Commands.Contains(args[0]) && !args[0].StartsWith('-')) args = ["copy", .. args];
        var root = new RootCommand("Camera card offload: read once, write to several disks, verify with xxh64.");
        root.Subcommands.Add(CopyCommand.Create());
        root.Subcommands.Add(VerifyCommand.Create());
        root.Subcommands.Add(BenchCommand.Create());
        return root.Parse(args).Invoke();
    }
}
