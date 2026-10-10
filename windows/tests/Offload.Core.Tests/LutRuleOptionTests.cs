using Offload.Cli;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// Spec 4.2: CLI `--lut-rule <match>=<value>:<lut|path.cube|none>` and what the rules editor accepts.
[Collection("Console")]
public class LutRuleOptionTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    [Theory]
    [InlineData("camera=FX6:sony-slog3-sgamut3cine", "camera", "FX6", "sony-slog3-sgamut3cine", null)]
    [InlineData("REEL= A0 :none", "reel", "A0", "none", null)]
    [InlineData(@"serial=0123:C:\luts\show.cube", "serial", "0123", null, @"C:\luts\show.cube")]
    [InlineData("folder=A001/*:show.cube", "folder", "A001/*", null, "show.cube")]
    public void Parse_Good(string spec, string match, string value, string? lut, string? path)
    {
        var (rule, error) = LutRules.ParseOption(spec, Cat);
        Assert.Null(error);
        Assert.Equal((match, value, lut, path), (rule!.Match, rule.Value, rule.Lut, rule.LutPath));
    }

    [Theory]
    [InlineData("camera:FX6")]
    [InlineData("=FX6:none")]
    [InlineData("camera=:none")]
    [InlineData("camera=FX6:")]
    [InlineData("lens=35:none")]
    public void Parse_Bad_CzechHelp(string spec)
    {
        var (rule, error) = LutRules.ParseOption(spec, Cat);
        Assert.Null(rule);
        Assert.StartsWith($"Cannot read the LUT rule \"{spec}\" — use <match>=<value>:<lut>", error);
    }

    [Fact]
    public void Parse_UnknownBuiltin_ListsTheIds()
    {
        var (rule, error) = LutRules.ParseOption("camera=FX6:sony-xyz", Cat);
        Assert.Null(rule);
        Assert.StartsWith("Unknown LUT \"sony-xyz\". Use auto, none, a path to a .cube or one of: ", error);
    }

    [Fact]
    public void EditorProblem_FirstIncompleteRow()
    {
        Assert.Null(LutRules.Problem([new LutRule { Value = "FX6", Lut = "none" }]));
        Assert.Equal("Rule 2: fill in the value.", LutRules.Problem([new LutRule { Value = "FX6", Lut = "none" }, new LutRule { Value = " ", Lut = "none" }]));
        Assert.Equal("Rule 1: select a LUT.", LutRules.Problem([new LutRule { Value = "FX6" }]));
    }

    // Ruling I3: a rule of a newer version is shown read-only in the editor and never blocks OK — whatever it holds.
    [Fact]
    public void EditorProblem_IgnoresRulesOfANewerVersion_NumberingKept()
    {
        var future = new LutRule { Match = "lens", Value = "", Lut = null };
        Assert.Null(LutRules.Problem([future, new LutRule { Value = "FX6", Lut = "none" }]));
        Assert.Equal("Rule 3: fill in the value.", LutRules.Problem([future, new LutRule { Value = "FX6", Lut = "none" }, new LutRule { Value = "", Lut = "none" }]));
        // What OK hands back keeps the rule untouched, in its place.
        var kept = LutRules.Valid([new LutRule { Value = " FX6 ", Lut = "none" }, future]);
        Assert.Equal(["FX6", ""], kept.Select(r => r.Value));
        Assert.Same(future, kept[1]);
    }

    [Fact]
    public void PlanLine_CountsTheRules()
    {
        var p = new ProxySettings { Enabled = true, LutRules = [new LutRule { Value = "FX6", Lut = "none" }] };
        Assert.Equal("LUT: auto + 1 own rule(s)", LutOption.PlanLine(p, analyzeClips: false));
    }

    static (int Exit, string Out, string Err, JobConfig? Config) Run(string? settingsDir, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldFactory = CopyCommand.JobFactory;
        var oldPath = CliProject.SettingsPath;
        JobConfig? seen = null;
        Console.SetOut(so);
        Console.SetError(se);
        if (settingsDir != null) CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        CopyCommand.JobFactory = c => new OffloadJob(seen = c, AppendFixtures.Options());
        try { return (CopyCommand.Create().Parse(args).Invoke(), so.ToString(), se.ToString(), seen); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = oldFactory;
            CliProject.SettingsPath = oldPath;
        }
    }

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 1_000, 1);
        return card;
    }

    [Fact]
    public void Cli_RulesInOrder_PathMadeFull_MissingCubeRefused()
    {
        using var dir = new TempDir();
        var cube = Path.Combine(dir.Path, "show.cube");
        TestMedia.IdentityLut(cube);
        var r = Run(null, Card(dir), "-d", dir.Sub("d1"), "--dry-run", "--no-history",
            "--lut-rule", "camera=FX6:sony-slog3-sgamut3cine", "--lut-rule", $"reel=A:{cube}");
        Assert.Equal(0, r.Exit);
        Assert.Equal([("camera", "sony-slog3-sgamut3cine", (string?)null), ("reel", null, cube)],
            r.Config!.Proxy.LutRules.Select(x => (x.Match, x.Lut, x.LutPath)));
        Assert.Contains("LUT: auto + 2 own rule(s)", r.Out);

        var bad = Run(null, Card(dir), "-d", dir.Sub("d2"), "--no-history", "--lut-rule", $"reel=A:{Path.Combine(dir.Path, "gone.cube")}");
        Assert.Equal(1, bad.Exit);
        Assert.Contains("Error: LUT of rule 1 not found: gone.cube", bad.Err);
        var wrong = Run(null, Card(dir), "-d", dir.Sub("d3"), "--no-history", "--lut-rule", "camera:FX6");
        Assert.Equal(1, wrong.Exit);
        Assert.Contains("Error: Cannot read the LUT rule \"camera:FX6\"", wrong.Err);
    }

    // With --project the project's rules are the default; written --lut-rule options replace them.
    [Fact]
    public void Cli_ProjectRules_ReplacedByWrittenOnes()
    {
        using var dir = new TempDir();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.Proxy.LutRules = [new LutRule { Match = "camera", Value = "FX6", Lut = "none" }, new LutRule { Match = "reel", Value = "B", Lut = "none" }];
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));

        var kept = Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d1"), "--dry-run", "--no-history");
        Assert.Equal(["FX6", "B"], kept.Config!.Proxy.LutRules.Select(x => x.Value));
        var replaced = Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d2"), "--dry-run", "--no-history", "--lut-rule", "serial=9:none");
        Assert.Equal(["9"], replaced.Config!.Proxy.LutRules.Select(x => x.Value));
    }

    // Fix round 1: a rule's .cube must exist (as --lut checks), also with --dry-run; the rule number is its position.
    [Fact]
    public void Cli_RuleCube_Missing_Refused_NumberedByPosition()
    {
        using var dir = new TempDir();
        var r = Run(null, Card(dir), "-d", dir.Sub("d1"), "--dry-run", "--no-history",
            "--lut-rule", "camera=FX6:none", "--lut-rule", $"reel=A:{Path.Combine(dir.Path, "gone.cube")}");
        Assert.Equal(1, r.Exit);
        Assert.Contains("Error: LUT of rule 2 not found: gone.cube", r.Err);
    }

    // Fix round 1: rules work in Auto mode only; written with another LUT they are refused, not silently ignored.
    [Theory]
    [InlineData("none")]
    [InlineData("sony-slog3-sgamut3cine")]
    public void Cli_Rules_WithAnotherLut_Refused(string lut)
    {
        using var dir = new TempDir();
        var r = Run(null, Card(dir), "-d", dir.Sub("d1"), "--dry-run", "--no-history", "--lut", lut, "--lut-rule", "camera=FX6:none");
        Assert.Equal(1, r.Exit);
        Assert.Contains("Error: --lut-rule works only with --lut auto", r.Err);
        Assert.Equal(0, Run(null, Card(dir), "-d", dir.Sub("d2"), "--dry-run", "--no-history", "--lut", "auto", "--lut-rule", "camera=FX6:none").Exit);
    }

    [Fact]
    public void Cli_Rules_ProjectInCustomMode_Refused()
    {
        using var dir = new TempDir();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.Proxy.LutMode = LutMode.None;
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));
        var r = Run(folder, Card(dir), "--project", "Film X", "-d", dir.Sub("d1"), "--dry-run", "--no-history", "--lut-rule", "camera=FX6:none");
        Assert.Equal(1, r.Exit);
        Assert.Contains("--lut-rule works only with --lut auto", r.Err);
    }
}
