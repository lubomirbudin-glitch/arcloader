using Offload.Cli;

namespace Offload.Core.Tests;

/// Plan 7: the CLI options of the outputs — `copy --hash`, `bench --hash`.
[Collection("Console")]
public class CliOutputsTests
{
    static (int Exit, string Out, string Err) Run(System.CommandLine.Command command, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var old = CopyCommand.JobFactory;
        Console.SetOut(so);
        Console.SetError(se);
        CopyCommand.JobFactory = c =>
        {
            var options = AppendFixtures.Options();   // card and destinations share a volume in a test
            options.Hashes = c.Hashes;
            return new OffloadJob(c, options);
        };
        try { return (command.Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
    }

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        return card;
    }

    // Spec 3.5: `--hash both` = xxh64 + MD5 (the .md5 list is there); anything else is a Czech error, exit 1.
    [Fact]
    public void CopyHash_BothWritesMd5_UnknownRefused()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d1 = dir.Sub("d1");

        var ok = Run(CopyCommand.Create(), card, "-d", d1, "--date", "2026-10-07", "--no-history", "--no-analyze", "--hash", "both");

        Assert.Equal(0, ok.Exit);
        Assert.True(File.Exists(Path.Combine(d1, "2026-10-07", "A001", "_REPORT", "A001.md5")));
        Assert.Contains("Hash:        xxh64+md5", ok.Out);

        var bad = Run(CopyCommand.Create(), card, "-d", dir.Sub("d2"), "--no-history", "--hash", "md5");
        Assert.Equal(1, bad.Exit);
        Assert.Contains("Chyba: Neznámý kontrolní součet „md5“ — použijte xxh64 nebo both (xxh64 + MD5).", bad.Err);
    }

    // Spec 3.5: `bench --hash md5` adds the MD5 line; an unknown value is refused in Czech.
    [Fact]
    public void BenchHash_Md5Line_UnknownRefused()
    {
        var md5 = Run(BenchCommand.Create(), "--hash", "md5");
        Assert.Equal(0, md5.Exit);
        Assert.Contains("md5 in RAM", md5.Out);
        Assert.Contains("xxh64 in RAM", md5.Out);
        var bad = Run(BenchCommand.Create(), "--hash", "sha1");
        Assert.Equal(1, bad.Exit);
        Assert.Contains("Neznámý kontrolní součet „sha1“", bad.Err);
    }
}
