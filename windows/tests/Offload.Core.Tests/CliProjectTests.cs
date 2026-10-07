using Offload.Cli;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// Spec 4.1 CLI: `--project <název>` takes a saved project as the defaults; options written on the command line win.
[Collection("Console")]
public class CliProjectTests
{
    /// Runs a command with settings.json in `settingsDir`; `seen` gets the job's config (the job really runs unless --dry-run).
    static (int Exit, string Out, string Err) Run(string settingsDir, List<JobConfig> seen, System.CommandLine.Command command, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldFactory = CopyCommand.JobFactory;
        var oldPath = CliProject.SettingsPath;
        Console.SetOut(so);
        Console.SetError(se);
        CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        CopyCommand.JobFactory = c =>
        {
            seen.Add(c);
            var options = AppendFixtures.Options();   // card and destinations share a volume in a test
            options.Hashes = c.Hashes;
            options.Verify = c.Verify;
            return new OffloadJob(c, options);
        };
        try { return (command.Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = oldFactory;
            CliProject.SettingsPath = oldPath;
        }
    }

    /// settings.json with project "Film X": destination `dest`, MD5, no verify, ProRes 422 at 720 px, ALE, no names from clips.
    static string Settings(TempDir dir, string dest, bool suggestFromClips = false)
    {
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.DestRoots = [dest];
        p.Hash = "both";
        p.Verify = false;
        p.AnalyzeClips = false;
        p.Proxy.Codec = ProxyCodec.Prores422;
        p.Proxy.Height = 720;
        p.Outputs.ClipListAle = true;
        p.Naming.SuggestFromClips = suggestFromClips;
        Projects.Activate(s, null);
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));
        return folder;
    }

    static string Card(TempDir dir, string name = "A001")
    {
        var card = dir.Sub(name);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        return card;
    }

    [Fact]
    public void Project_GivesTheDefaults_ReportNamesIt()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        var settings = Settings(dir, d1);
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "--project", "film x", "--date", "2026-10-07", "--no-history");

        Assert.Equal(0, r.Exit);
        Assert.Contains("Projekt: Film X", r.Out);
        var c = Assert.Single(seen);
        Assert.Equal([d1], c.DestRoots);
        Assert.Equal(HashKind.Both, c.Hashes);
        Assert.False(c.Verify);
        Assert.False(c.AnalyzeClips);
        Assert.Equal((ProxyCodec.Prores422, 720), (c.Proxy.Codec, c.Proxy.Height));
        Assert.True(c.Outputs.ClipListAle);
        Assert.Equal("Film X", c.Project);
        var report = Path.Combine(d1, "2026-10-07", "A001", "_REPORT");
        Assert.True(File.Exists(Path.Combine(report, "A001.md5")));
        Assert.True(File.Exists(Path.Combine(report, "A001_clips.ale")));
        Assert.Contains("\"project\": \"Film X\"", File.ReadAllText(Path.Combine(report, "offload_report.json")));
    }

    // Spec 4.1 test "přednost voleb": --project + --proxy-codec — the option wins, the rest stays the project's;
    // -d replaces the project's destinations, --hash and the flags win too.
    [Fact]
    public void WrittenOptionsWin()
    {
        using var dir = new TempDir();
        var settings = Settings(dir, dir.Sub("d1"));
        var other = dir.Sub("other");
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "--project", "Film X", "--proxy-codec", "hevc", "-d", other,
            "--hash", "xxh64", "--no-clip-list", "--dry-run", "--no-history");

        Assert.Equal(0, r.Exit);
        var c = Assert.Single(seen);
        Assert.Equal(ProxyCodec.Hevc, c.Proxy.Codec);
        Assert.Equal(720, c.Proxy.Height);                 // not written: the project's
        Assert.Equal([other], c.DestRoots);
        Assert.Equal(HashKind.Xxh64, c.Hashes);
        Assert.False(c.Outputs.ClipListCsv);
        Assert.True(c.Outputs.ClipListAle);                // not written: the project's
        Assert.False(c.Verify);
    }

    [Fact]
    public void UnknownProject_Exit1_ListsTheProjects()
    {
        using var dir = new TempDir();
        var settings = Settings(dir, dir.Sub("d1"));
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "--project", "Reklama", "--no-history");

        Assert.Equal(1, r.Exit);
        Assert.Contains("Chyba: Projekt „Reklama“ neexistuje. Projekty: Film X.", r.Err);
        Assert.Empty(seen);
    }

    // Without --project nothing changes: the CLI defaults, not "Bez projektu" from settings.json.
    [Fact]
    public void NoProject_CliDefaults()
    {
        using var dir = new TempDir();
        var settings = Settings(dir, dir.Sub("d1"));
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, CopyCommand.Create(), Card(dir), "-d", dir.Sub("d2"), "--dry-run", "--no-history");

        Assert.Equal(0, r.Exit);
        var c = Assert.Single(seen);
        Assert.Null(c.Project);
        Assert.True(c.Verify);
        Assert.Equal(HashKind.Xxh64, c.Hashes);
        Assert.Equal((ProxyCodec.H264, 1080, LutMode.Auto), (c.Proxy.Codec, c.Proxy.Height, c.Proxy.LutMode));
        Assert.DoesNotContain("Projekt:", r.Out);
    }

    // naming.suggestFromClips of the project acts as --name auto; --name still wins.
    [Fact]
    public void ProjectNaming_NameFromClips_UnlessNameGiven()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        var settings = Settings(dir, d1, suggestFromClips: true);
        var card = Card(dir, "EOS_DIGITAL");
        var seen = new List<JobConfig>();

        Assert.Equal(0, Run(settings, seen, CopyCommand.Create(), card, "--project", "Film X", "--date", "2026-10-07", "--no-history").Exit);
        Assert.Equal(0, Run(settings, seen, CopyCommand.Create(), card, "--project", "Film X", "--date", "2026-10-07", "--no-history", "--name", "B007").Exit);

        Assert.True(Directory.Exists(Path.Combine(d1, "2026-10-07", "A001")));
        Assert.True(Directory.Exists(Path.Combine(d1, "2026-10-07", "B007")));
        Assert.False(Directory.Exists(Path.Combine(d1, "2026-10-07", "EOS_DIGITAL")));
    }

    // A project whose custom LUT is gone stops before anything is copied (as the app's red text does).
    [Fact]
    public void ProjectsMissingLut_Refused()
    {
        using var dir = new TempDir();
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        var p = Projects.SaveAs(s, "Film X");
        p.DestRoots = [dir.Sub("d1")];
        p.Proxy.LutMode = LutMode.Custom;
        p.Proxy.LutPath = Path.Combine(dir.Path, "gone.cube");
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));
        var seen = new List<JobConfig>();

        var r = Run(folder, seen, CopyCommand.Create(), Card(dir), "--project", "Film X", "--no-history");

        Assert.Equal(1, r.Exit);
        Assert.Contains("LUT nenalezen: gone.cube", r.Err);
        Assert.Empty(seen);
    }

    // Ruling M4: an append records the project of the earlier backup; --project only gives the settings.
    [Fact]
    public void Append_ProjectGivesTheSettings_ReportKeepsTheEarlierBackupsProject()
    {
        using var dir = new TempDir();
        var d1 = dir.Sub("d1");
        var folder = dir.Sub("appdata");
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X").DestRoots = [d1];
        var ad = Projects.SaveAs(s, "Reklama Y");
        ad.Verify = false;
        ad.AnalyzeClips = false;
        ad.Outputs.ClipListAle = true;
        SettingsStore.Save(s, Path.Combine(folder, "settings.json"));
        var card = Card(dir);
        var seen = new List<JobConfig>();

        Assert.Equal(0, Run(folder, seen, CopyCommand.Create(), card, "--project", "Film X", "--date", "2026-10-07", "--no-analyze", "--no-history").Exit);
        AppendFixtures.NewClip(card);
        var into = Path.Combine(d1, "2026-10-07", "A001");
        var r = Run(folder, seen, AppendCommand.Create(), card, "--into", into, "--project", "Reklama Y", "--no-history");

        Assert.Equal(0, r.Exit);
        Assert.Contains("Projekt: Reklama Y", r.Out);
        var c = seen[1];
        Assert.False(c.Verify);
        Assert.False(c.AnalyzeClips);
        Assert.True(c.Outputs.ClipListAle);
        var report = Path.Combine(into, "_REPORT");
        Assert.True(File.Exists(Path.Combine(report, "A001_clips.ale")));
        Assert.Contains("\"project\": \"Film X\"", File.ReadAllText(Path.Combine(report, "offload_report_0002.json")));
    }

    [Fact]
    public void Append_UnknownProject_Exit1()
    {
        using var dir = new TempDir();
        var settings = Settings(dir, dir.Sub("d1"));
        var seen = new List<JobConfig>();

        var r = Run(settings, seen, AppendCommand.Create(), Card(dir), "--into", dir.Sub("x"), "--project", "Reklama", "--no-history");

        Assert.Equal(1, r.Exit);
        Assert.Contains("Chyba: Projekt „Reklama“ neexistuje. Projekty: Film X.", r.Err);
        Assert.Empty(seen);
    }
}
