using System.Text.Json;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// Spec 4.1, R17: projects are named profiles in settings.json; the top level is "Bez projektu".
public class ProjectsTests
{
    static string Write(TempDir dir, string json)
    {
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void RoundTrip_ProjectsAndActive_EveryProfileKey()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var s = new AppSettings();
        s.DestRoots.Add(@"D:\Bez");
        var film = Projects.SaveAs(s, "  Film X ");
        film.DestRoots = [@"E:\Film", @"F:\Film"];
        film.Verify = false;
        film.AnalyzeClips = false;
        film.Hash = "both";
        film.Naming.SuggestFromClips = false;
        film.Outputs.ClipListAle = true;
        film.Notify.Ntfy.Enabled = true;
        film.Notify.Ntfy.Topic = "film-x-tajne";
        film.Proxy.Enabled = true;
        film.Proxy.ExtraFolder = @"G:\Proxy";
        SettingsStore.Save(s, path);

        var again = SettingsStore.Load(path);
        Assert.Equal("Film X", again.ActiveProject);
        Assert.Equal([@"D:\Bez"], again.DestRoots);
        var p = Assert.Single(again.Projects);
        Assert.Same(p, again.Active);
        Assert.Equal("Film X", p.Name);
        Assert.Equal([@"E:\Film", @"F:\Film"], p.DestRoots);
        Assert.False(p.Verify);
        Assert.False(p.AnalyzeClips);
        Assert.Equal("both", p.Hash);
        Assert.False(p.Naming.SuggestFromClips);
        Assert.True(p.Outputs.ClipListAle);
        Assert.True(p.Notify.Ntfy.Enabled);
        Assert.Equal("film-x-tajne", p.Notify.Ntfy.Topic);
        Assert.Equal(NtfySettings.DefaultServer, p.Notify.Ntfy.Server);
        Assert.True(p.Proxy.Enabled);
        Assert.Equal(@"G:\Proxy", p.Proxy.ExtraFolder);
        // the top level keeps its own values
        Assert.True(again.Verify);
        Assert.False(again.Proxy.Enabled);
        // the name comes first in the project's object (readable by hand)
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("name", doc.RootElement.GetProperty("projects")[0].EnumerateObject().First().Name);
    }

    // Spec 4.1 "přepnutí tam a zpět beze ztráty": each profile keeps its own values.
    [Fact]
    public void SwitchThereAndBack_LosesNothing()
    {
        var s = new AppSettings();
        s.DestRoots.Add(@"D:\A");
        Projects.SaveAs(s, "Film X");
        Assert.Equal([@"D:\A"], s.Active.DestRoots);            // a copy of what was shown
        s.Active.DestRoots.Add(@"E:\B");
        s.Active.Proxy.Height = 720;

        Projects.Activate(s, null);
        Assert.Same(s, s.Active);
        Assert.Equal([@"D:\A"], s.Active.DestRoots);
        Assert.Equal(1080, s.Active.Proxy.Height);

        Projects.Activate(s, "film x");                          // any case
        Assert.Equal("Film X", s.ActiveProject);
        Assert.Equal([@"D:\A", @"E:\B"], s.Active.DestRoots);
        Assert.Equal(720, s.Active.Proxy.Height);

        Projects.Activate(s, Projects.NoProject);
        Assert.Null(s.ActiveProject);
    }

    // Scripts written for 0.8 and older pass the Czech name of the top level.
    [Theory]
    [InlineData("No project")]
    [InlineData("bez projektu")]
    [InlineData(" Bez projektu ")]
    public void OldAndNewNoProjectName_SwitchToTheTopLevel(string name)
    {
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X");
        Assert.True(Projects.IsNoProject(name));
        Projects.Activate(s, name);
        Assert.Null(s.ActiveProject);
    }

    [Fact]
    public void SaveAs_CopiesDeep_NotShared()
    {
        var s = new AppSettings();
        s.Proxy.Enabled = true;
        var p = Projects.SaveAs(s, "Film X");
        p.Proxy.Enabled = false;
        p.Notify.Windows = false;
        Assert.True(s.Proxy.Enabled);
        Assert.True(s.Notify.Windows);
        Assert.NotSame(s.Outputs, p.Outputs);
        Assert.NotSame(s.Naming, p.Naming);
    }

    [Fact]
    public void Rename_Active_StaysActive_Delete_Active_GoesToNoProject()
    {
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X");
        Projects.Activate(s, null);
        Projects.SaveAs(s, "Reklama Y");

        Projects.Rename(s, "reklama y", "Reklama Z");
        Assert.Equal("Reklama Z", s.ActiveProject);
        Projects.Rename(s, "Reklama Z", "reklama z");            // only the case changes: allowed
        Assert.Equal("reklama z", s.ActiveProject);

        Projects.Delete(s, "Film X");                             // not the active one
        Assert.Equal("reklama z", s.ActiveProject);
        Projects.Delete(s, "reklama z");
        Assert.Null(s.ActiveProject);
        Assert.Empty(s.Projects);
    }

    [Theory]
    [InlineData("", "The name must not be empty.")]
    [InlineData("   ", "The name must not be empty.")]
    [InlineData("no PROJECT", "\"no PROJECT\" is a reserved name.")]
    [InlineData("bez PROJEKTU", "\"bez PROJEKTU\" is a reserved name.")]   // the old Czech name stays reserved
    [InlineData("film x", "A project with this name already exists.")]
    [InlineData(" Film X ", "A project with this name already exists.")]
    public void Names_Refused_InCzech(string name, string message)
    {
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X");
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => Projects.SaveAs(s, name)).Message);
        Assert.Single(s.Projects);
    }

    [Fact]
    public void Names_LengthLimit_AndRenameOntoAnother()
    {
        var s = new AppSettings();
        Projects.SaveAs(s, new string('x', 60));
        Assert.Equal("The name can have at most 60 characters.", Assert.Throws<ArgumentException>(() => Projects.SaveAs(s, new string('y', 61))).Message);
        Projects.SaveAs(s, "B");
        Assert.Equal("A project with this name already exists.", Assert.Throws<ArgumentException>(() => Projects.Rename(s, "B", new string('x', 60))).Message);
        Assert.Equal("Project \"Nic\" does not exist. Projects: " + new string('x', 60) + ", B.", Assert.Throws<ArgumentException>(() => Projects.Activate(s, "Nic")).Message);
        Assert.Equal("Project \"Nic\" does not exist. No project is saved.", Projects.Unknown("Nic", new AppSettings()));
    }

    // Review focus 2: a hand-edited file — the same name twice (the last wins), an invalid entry, missing sections,
    // keys of a newer version inside a project, an active project that is not there.
    [Fact]
    public void HandEdited_DuplicatesInvalidMissingUnknown()
    {
        using var dir = new TempDir();
        var path = Write(dir, """
            {"destRoots":["D:\\Bez"],"activeProject":"FILM X",
             "projects":[
               {"name":"Film X","destRoots":["E:\\Old"]},
               7, {"destRoots":["X:\\NoName"]}, {"name":"  "}, {"name":"Bez projektu"},
               {"name":"film x","destRoots":["E:\\New"],"proxy":{"codec":"av1"},"future":{"a":1}},
               {"name":"Reklama"}
             ]}
            """);
        var s = SettingsStore.Load(path);
        Assert.Equal(["Film X", "Reklama"], s.Projects.Select(p => p.Name));   // a name given twice: the first is live
        Assert.Equal("Film X", s.ActiveProject);                       // canonical name of the match
        var film = s.Projects[0];
        Assert.Equal([@"E:\Old"], film.DestRoots);
        var reklama = s.Projects[1];
        Assert.True(reklama.Verify);                                    // missing sections: defaults
        Assert.Equal(1080, reklama.Proxy.Height);

        SettingsStore.Save(s, path);
        var first = File.ReadAllBytes(path);
        using var doc = JsonDocument.Parse(first);
        var list = doc.RootElement.GetProperty("projects");
        Assert.Equal(["Film X", "Reklama"], list.EnumerateArray().Take(2).Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(@"E:\Old", list[0].GetProperty("destRoots")[0].GetString());
        // everything else is kept raw, after the readable ones, in the order of the file: the later duplicate (its
        // unparseable section and the key of a newer version too), then the entries without a usable name
        Assert.Equal(7, list.GetArrayLength());
        Assert.Equal(JsonValueKind.Number, list[2].ValueKind);
        Assert.Equal(@"X:\NoName", list[3].GetProperty("destRoots")[0].GetString());
        Assert.Equal("  ", list[4].GetProperty("name").GetString());
        Assert.Equal("Bez projektu", list[5].GetProperty("name").GetString());
        var dup = list[6];
        Assert.Equal("film x", dup.GetProperty("name").GetString());
        Assert.Equal(@"E:\New", dup.GetProperty("destRoots")[0].GetString());
        Assert.Equal("av1", dup.GetProperty("proxy").GetProperty("codec").GetString());
        Assert.Equal(1, dup.GetProperty("future").GetProperty("a").GetInt32());

        SettingsStore.Save(SettingsStore.Load(path), path);              // a second save changes nothing
        Assert.Equal(first, File.ReadAllBytes(path));
    }

    // Review: an activeProject that names a project kept raw stays as written (not nulled by a save); the live profile
    // is "Bez projektu" meanwhile. One that names nothing at all is dropped.
    [Fact]
    public void ActiveProject_NamingARawProject_IsKept()
    {
        using var dir = new TempDir();
        var longName = new string('z', 61);
        var path = Write(dir, "{\"activeProject\":\"" + longName + "\",\"projects\":[{\"name\":\"" + longName + "\"},{\"name\":\"Film X\"}]}");
        var s = SettingsStore.Load(path);
        Assert.Equal(longName, s.ActiveProject);
        Assert.Same(s, s.Active);
        Assert.Null(Projects.ActiveName(s));
        SettingsStore.Save(s, path);
        Assert.Equal(longName, SettingsStore.Load(path).ActiveProject);
        Projects.Activate(s, "Film X");
        SettingsStore.Save(s, path);
        Assert.Equal("Film X", SettingsStore.Load(path).ActiveProject);
    }

    // M3 (ruling): a project of a newer version with a name invalid for this one is not erased by a save; saving
    // twice does not multiply it, and the readable ones stay.
    [Fact]
    public void UnreadableProjectEntries_SurviveSaves_NotDuplicated()
    {
        using var dir = new TempDir();
        var path = Write(dir, "{\"projects\":[{\"name\":\"" + new string('z', 61) + "\",\"verify\":false},{\"name\":\"Film X\"}]}");
        var s = SettingsStore.Load(path);
        Assert.Equal(["Film X"], s.Projects.Select(x => x.Name));
        SettingsStore.Save(s, path);
        SettingsStore.Save(SettingsStore.Load(path), path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = doc.RootElement.GetProperty("projects");
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("Film X", list[0].GetProperty("name").GetString());
        Assert.Equal(61, list[1].GetProperty("name").GetString()!.Length);
        Assert.False(list[1].GetProperty("verify").GetBoolean());
    }

    static void Contains(JsonElement saved, JsonElement original, string where, List<string> lost)
    {
        if (original.ValueKind == JsonValueKind.Object)
        {
            if (saved.ValueKind != JsonValueKind.Object) { lost.Add(where); return; }
            foreach (var p in original.EnumerateObject())
            {
                if (!saved.TryGetProperty(p.Name, out var v)) lost.Add(where + "/" + p.Name);
                else Contains(v, p.Value, where + "/" + p.Name, lost);
            }
        }
        else if (original.ValueKind == JsonValueKind.Array)
        {
            if (saved.ValueKind != JsonValueKind.Array || saved.GetArrayLength() != original.GetArrayLength()) { lost.Add(where); return; }
            for (int i = 0; i < original.GetArrayLength(); i++) Contains(saved[i], original[i], where + "[" + i + "]", lost);
        }
        else if (saved.GetRawText() != original.GetRawText()) lost.Add(where);
    }

    // R24: a 0.3.0 file with keys of a newer version at every level: load, save, reload loses nothing; with projects
    // too; a second save is byte-identical to the first.
    [Fact]
    public void RoundTrip_030File_WithUnknownKeysEverywhere_LosesNothing()
    {
        using var dir = new TempDir();
        const string original = """
            {"destRoots":["D:\\Bez","E:\\Bez"],"verify":false,"analyzeClips":true,"hash":"both","futureTop":{"a":[1,2,{"b":null}]},
             "proxy":{"enabled":true,"height":720,"futureProxy":"x","lutRules":[{"match":"camera","value":"FX6","lut":"sony-slog3-sgamut3cine"}]},
             "tools":{"ffmpeg":"C:\\x\\ffmpeg.exe","futureTool":1},
             "naming":{"suggestFromClips":false,"futureNaming":true},
             "outputs":{"clipListAle":true,"futureOutput":[1]},
             "portNames":{"usb1":"Levý"},
             "notify":{"windows":false,"futureNotify":1,"ntfy":{"enabled":true,"server":"https://n.example","topic":"t","futureNtfy":2}},
             "projects":[{"name":"Film X","destRoots":["F:\\Film"],"futureProject":{"k":"v"},"naming":{"futureNaming":1},"proxy":{"futureProxy":2},
                          "notify":{"ntfy":{"futureNtfy":3}},"outputs":{"futureOutput":4}},
                         {"name":"Ž","proxy":{"codec":"av1"}}, 7, {"name":"Bez projektu"}],
             "activeProject":"Film X"}
            """;
        var path = Write(dir, original);
        SettingsStore.Save(SettingsStore.Load(path), path);
        var bytes1 = File.ReadAllBytes(path);
        SettingsStore.Save(SettingsStore.Load(path), path);
        Assert.Equal(bytes1, File.ReadAllBytes(path));

        using var before = JsonDocument.Parse(original);
        using var after = JsonDocument.Parse(File.ReadAllText(path));
        var lost = new List<string>();
        Contains(after.RootElement, before.RootElement, "", lost);
        Assert.Empty(lost);
        Assert.Equal("Film X", SettingsStore.Load(path).ActiveProject);
    }

    [Fact]
    public void ActiveProjectMissing_IsNoProject()
    {
        using var dir = new TempDir();
        var s = SettingsStore.Load(Write(dir, """{"activeProject":"Gone","projects":[{"name":"Film X"}]}"""));
        Assert.Null(s.ActiveProject);
        Assert.Same(s, s.Active);
    }

    // Spec 4.1: a damaged `projects` section → no projects, the rest of the settings stays; it is written back until
    // a project is saved in the app.
    [Fact]
    public void ProjectsNotAList_RestKept_RawUntilAProjectIsSaved()
    {
        using var dir = new TempDir();
        var path = Write(dir, """{"verify":false,"projects":{"oops":true},"activeProject":"Film X"}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.Verify);
        Assert.Empty(s.Projects);
        Assert.Null(s.ActiveProject);

        SettingsStore.Save(s, path);
        Assert.Contains("\"oops\": true", File.ReadAllText(path));

        var again = SettingsStore.Load(path);
        Projects.SaveAs(again, "Film X");
        SettingsStore.Save(again, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("projects").ValueKind);
        Assert.Equal("Film X", SettingsStore.Load(path).ActiveProject);
    }

    [Fact]
    public void NoProjects_WritesEmptyListAndNull_OlderFileWithoutThemLoads()
    {
        using var dir = new TempDir();
        var s = SettingsStore.Load(Write(dir, """{"verify":false}"""));
        Assert.Empty(s.Projects);
        Assert.Null(s.ActiveProject);
        var path = Path.Combine(dir.Path, "out.json");
        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(0, doc.RootElement.GetProperty("projects").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("activeProject").ValueKind);
    }

    // Spec 4.3, 5.1: notify defaults; a blank server falls back to ntfy.sh, the topic is trimmed.
    [Fact]
    public void Notify_DefaultsAndNormalize()
    {
        using var dir = new TempDir();
        var fresh = SettingsStore.Load(Path.Combine(dir.Path, "none.json"));
        Assert.True(fresh.Notify.Windows);
        Assert.False(fresh.Notify.Ntfy.Enabled);
        Assert.Equal("https://ntfy.sh", fresh.Notify.Ntfy.Server);
        Assert.Equal("", fresh.Notify.Ntfy.Topic);
        var s = SettingsStore.Load(Write(dir, """{"notify":{"windows":false,"ntfy":{"enabled":true,"server":" ","topic":"  abc "}}}"""));
        Assert.False(s.Notify.Windows);
        Assert.True(s.Notify.Ntfy.Enabled);
        Assert.Equal("https://ntfy.sh", s.Notify.Ntfy.Server);
        Assert.Equal("abc", s.Notify.Ntfy.Topic);
    }

    // The queue keeps a clone (spec 4.1): a job's settings never change when the project is switched later.
    [Fact]
    public void ClonedProfileSections_AreIndependentOfTheSwitch()
    {
        var s = new AppSettings();
        Projects.SaveAs(s, "Film X");
        s.Active.Proxy.Height = 720;
        var queued = SettingsStore.Clone(s.Active.Proxy);
        Projects.Activate(s, null);
        s.Active.Proxy.Height = 540;
        Assert.Equal(720, queued.Height);
        Assert.Equal(720, s.Projects[0].Proxy.Height);
    }

    [Fact]
    public void WindowTitleAndDeleteQuestion()
    {
        Assert.Equal("mmMover", Projects.WindowTitle(null));
        Assert.Equal("mmMover — Film X", Projects.WindowTitle("Film X"));
        Assert.Equal("Delete project Film X? The project's settings will be lost; the backups on the disks stay.", Projects.DeleteQuestion("Film X"));
    }
}
