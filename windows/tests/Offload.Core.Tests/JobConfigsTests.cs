using Offload.Core.Desktop;
using Offload.Core.History;

namespace Offload.Core.Tests;

/// What a Start in the app makes from the settings (spec 4.1): rulings I1 (a project switch during Start), I2 (an
/// append runs with the earlier backup's project) and M15 (a started job keeps its own copy whatever is switched later).
public class JobConfigsTests
{
    /// "Bez projektu" (H.264 1080, xxh64, verify) + "Film X" (ProRes 422 720, MD5, no verify, ALE, no analysis)
    /// + "Reklama Y" (HEVC 540); the active one is `active`.
    static AppSettings Settings(string? active)
    {
        var s = new AppSettings();
        s.DestRoots = [@"D:\Bez"];
        s.Tools.Ffmpeg = @"C:\Tools\ffmpeg.exe";
        var film = Projects.SaveAs(s, "Film X");
        film.DestRoots = [@"E:\Film"];
        film.Hash = "both";
        film.Verify = false;
        film.AnalyzeClips = false;
        film.Proxy.Codec = ProxyCodec.Prores422;
        film.Proxy.Height = 720;
        film.Outputs.ClipListAle = true;
        var ad = Projects.SaveAs(s, "Reklama Y");
        ad.DestRoots = [@"F:\Reklama"];
        ad.Proxy.Codec = ProxyCodec.Hevc;
        ad.Proxy.Height = 540;
        ad.Outputs.ClipListAle = false;
        ad.Verify = true;
        ad.AnalyzeClips = true;
        Projects.Activate(s, active);
        return s;
    }

    static HistoryEntry Entry(string? project) => new()
    {
        CardName = "A001",
        Project = project,
        Destinations =
        [
            new HistoryDestination { Root = @"G:\", CardPath = @"G:\2026-10-06\A001" },
            new HistoryDestination { Root = @"H:\", CardPath = @"H:\2026-10-06\A001" },
        ],
    };

    // ── Ruling I2: the profile of an append ────────────────────────────────

    [Fact]
    public void ProfileFor_TheEntrysProject_WhenItStillExists()
    {
        var s = Settings("Reklama Y");
        var (profile, gone) = AppendRules.ProfileFor(s, "film x");
        Assert.Same(s.Projects[0], profile);
        Assert.False(gone);
    }

    [Fact]
    public void ProfileFor_AProjectThatIsGone_BezProjektu_Noted()
    {
        // Fix round 1, item 1: never the project shown now (its settings and ntfy topic may be another client's).
        var s = Settings("Reklama Y");
        var (profile, gone) = AppendRules.ProfileFor(s, "Smazaný film");
        Assert.Same(s, profile);
        Assert.True(gone);
    }

    [Fact]
    public void ProfileFor_RenamedProject_BezProjektu_Noted()
    {
        var s = Settings("Film X");
        Projects.Rename(s, "Film X", "Film X (2)");
        var (profile, gone) = AppendRules.ProfileFor(s, "Film X");
        Assert.Same(s, profile);
        Assert.True(gone);
    }

    [Fact]
    public void GoneNote_ForTheJobCard()
    {
        Assert.Equal("Projekt Film X už neexistuje — doplnění běží s nastavením Bez projektu", AppendRules.GoneNote("Film X"));
    }

    // A card backed up in "Bez projektu" (or before projects existed) is appended with "Bez projektu", whose profile is
    // always there — never with the project shown now (its ntfy topic may be another client's).
    [Fact]
    public void ProfileFor_NoProject_TheBezProjektuProfile()
    {
        var s = Settings("Film X");
        var (profile, gone) = AppendRules.ProfileFor(s, null);
        Assert.Same(s, profile);
        Assert.False(gone);
    }

    [Fact]
    public void Append_RunsWithTheEntrysProject_AndRecordsIt()
    {
        var s = Settings("Reklama Y");
        var c = JobConfigs.Append(s, @"K:\", Entry("Film X"));

        Assert.Equal("Film X", c.Project);
        Assert.Equal([@"G:\", @"H:\"], c.DestRoots);
        Assert.Equal([@"G:\2026-10-06\A001", @"H:\2026-10-06\A001"], c.AppendInto);
        Assert.True(c.AllowSameDevice);
        Assert.False(c.Verify);
        Assert.False(c.AnalyzeClips);
        Assert.Equal((ProxyCodec.Prores422, 720), (c.Proxy.Codec, c.Proxy.Height));
        Assert.True(c.Outputs.ClipListAle);
        Assert.Equal(@"C:\Tools\ffmpeg.exe", c.Tools.Ffmpeg);   // tools are global
        Assert.Equal(HashKind.Xxh64, c.Hashes);                // an append keeps the earlier backup's (the engine reads them)
    }

    [Fact]
    public void Append_ProjectGone_BezProjektuSettings_TheEntrysNameStillRecorded()
    {
        var s = Settings("Reklama Y");
        var c = JobConfigs.Append(s, @"K:\", Entry("Smazaný film"));

        Assert.Equal("Smazaný film", c.Project);
        Assert.Equal((ProxyCodec.H264, 1080), (c.Proxy.Codec, c.Proxy.Height));   // not Reklama's HEVC 540
        Assert.True(c.Verify);
        Assert.False(c.Outputs.ClipListAle);
    }

    [Fact]
    public void Append_NoProject_BezProjektuSettings_NoProjectRecorded()
    {
        var s = Settings("Film X");
        var c = JobConfigs.Append(s, @"K:\", Entry(null));

        Assert.Null(c.Project);
        Assert.Equal((ProxyCodec.H264, 1080), (c.Proxy.Codec, c.Proxy.Height));
        Assert.True(c.Verify);
    }

    // ── Ruling M15: a started job keeps its own copy ───────────────────────

    [Fact]
    public void Full_TheActiveProfile_AndItsName()
    {
        var s = Settings("Film X");
        var c = JobConfigs.Full(s, @"K:\", "A001", "2026-10-07");

        Assert.Equal("Film X", c.Project);
        Assert.Equal([@"E:\Film"], c.DestRoots);
        Assert.Equal(("A001", "2026-10-07"), (c.CardName, c.Date));
        Assert.Equal(HashKind.Both, c.Hashes);
        Assert.False(c.Verify);
        Assert.False(c.AnalyzeClips);
        Assert.Equal((ProxyCodec.Prores422, 720), (c.Proxy.Codec, c.Proxy.Height));
        Assert.True(c.Outputs.ClipListAle);
        Assert.Equal(@"C:\Tools\ffmpeg.exe", c.Tools.Ffmpeg);
        Assert.Null(c.AppendInto);
    }

    [Fact]
    public void Full_SwitchAndEditLater_TheJobKeepsWhatItStartedWith()
    {
        var s = Settings("Film X");
        var c = JobConfigs.Full(s, @"K:\", null, "2026-10-07");
        var a = JobConfigs.Append(s, @"L:\", Entry("Film X"));

        s.Active.Proxy.Height = 2160;
        s.Active.DestRoots.Add(@"Z:\");
        s.Active.Outputs.ClipListAle = false;
        s.Tools.Ffmpeg = @"C:\Jiny\ffmpeg.exe";
        Projects.Activate(s, "Reklama Y");
        s.Active.Proxy.Height = 1080;

        Assert.Equal([@"E:\Film"], c.DestRoots);
        Assert.Equal(720, c.Proxy.Height);
        Assert.True(c.Outputs.ClipListAle);
        Assert.Equal(@"C:\Tools\ffmpeg.exe", c.Tools.Ffmpeg);
        Assert.Equal("Film X", c.Project);
        Assert.Equal(720, a.Proxy.Height);
        Assert.True(a.Outputs.ClipListAle);
        Assert.Equal(@"C:\Tools\ffmpeg.exe", a.Tools.Ffmpeg);
    }

    // Ledger note: activeProject may name a project kept raw (a newer version's) — the job is "Bez projektu" then.
    [Fact]
    public void Full_ActiveProjectNamesNoReadableProject_BezProjektu()
    {
        var s = Settings(null);
        s.ActiveProject = "Projekt z novější verze";
        var c = JobConfigs.Full(s, @"K:\", null, "2026-10-07");

        Assert.Null(c.Project);
        Assert.Equal([@"D:\Bez"], c.DestRoots);
    }

    // ── Ruling I1: a switch during Start's awaits stops it ─────────────────

    [Fact]
    public void ProfileStamp_ValuesEdited_NotChanged()
    {
        var s = Settings("Film X");
        var stamp = ProfileStamp.Of(s);
        s.Active.Proxy.Height = 1080;
        s.Active.DestRoots.Add(@"Z:\");
        Assert.False(stamp.ChangedIn(s));
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("toNoProject")]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("saveAs")]
    public void ProfileStamp_AnotherProfileShown_Changed(string what)
    {
        var s = Settings("Film X");
        var stamp = ProfileStamp.Of(s);
        switch (what)
        {
            case "switch": Projects.Activate(s, "Reklama Y"); break;
            case "toNoProject": Projects.Activate(s, null); break;
            case "rename": Projects.Rename(s, "Film X", "Film Z"); break;
            case "delete": Projects.Delete(s, "Film X"); break;
            case "saveAs": Projects.SaveAs(s, "Film X kopie"); break;
        }
        Assert.True(stamp.ChangedIn(s));
    }

    [Fact]
    public void ProfileStamp_BezProjektu_SaveAs_Changed()
    {
        var s = Settings(null);
        var stamp = ProfileStamp.Of(s);
        Assert.False(stamp.ChangedIn(s));
        Projects.SaveAs(s, "Nový");
        Assert.True(stamp.ChangedIn(s));
    }

    // ── Fix round 1, item 2a: the LUTs and proxy folders a Start really uses ──

    static void CustomLut(SettingsProfile p, string path, string? folder = null)
    {
        p.Proxy.LutMode = LutMode.Custom;
        p.Proxy.LutPath = path;
        p.Proxy.Enabled = true;
        p.Proxy.ExtraFolder = folder;
    }

    [Fact]
    public void StartChecks_FullCopy_TheShownProfilesLutAndFolder()
    {
        var s = Settings("Film X");
        CustomLut(s.Active, @"C:\Luty\film.cube", @"P:\Proxy");

        var checks = AppendRules.StartChecks(s, fullCopies: 1, []);

        Assert.Equal(
            [new StartCheck(StartCheckKind.Lut, @"C:\Luty\film.cube", "Film X", Shown: true),
             new StartCheck(StartCheckKind.ProxyFolder, @"P:\Proxy", "Film X", Shown: true)],
            checks);
    }

    // The shown profile's missing LUT does not block an append-only Start: that LUT is not used.
    [Fact]
    public void StartChecks_AppendsOnly_NotTheShownProfile()
    {
        var s = Settings("Film X");
        CustomLut(s.Active, @"C:\Luty\film.cube", @"P:\Proxy");

        Assert.Empty(AppendRules.StartChecks(s, fullCopies: 0, [Entry("Reklama Y"), Entry(null)]));
    }

    [Fact]
    public void StartChecks_AnAppendsProject_NamedInTheRefusal()
    {
        var s = Settings("Reklama Y");
        CustomLut(s.Projects[0], @"C:\Luty\x.cube", @"Q:\Film proxy");

        var checks = AppendRules.StartChecks(s, fullCopies: 1, [Entry("Film X")]);

        var lut = Assert.Single(checks, c => c.Kind == StartCheckKind.Lut);
        Assert.Equal(new StartCheck(StartCheckKind.Lut, @"C:\Luty\x.cube", "Film X", Shown: false), lut);
        Assert.Equal("LUT projektu Film X nenalezen: x.cube — doplnění karty se nespustí. Vyberte v projektu jiný LUT, nebo ho odeberte.", lut.Problem);
        var folder = Assert.Single(checks, c => c.Kind == StartCheckKind.ProxyFolder);
        Assert.Equal(@"Složka pro proxy projektu Film X není dostupná: Q:\Film proxy — doplnění karty se nespustí.", folder.Problem);
    }

    // The shown profile used by an append (append-only Start) is checked as the shown one: its red text says why.
    [Fact]
    public void StartChecks_AppendInTheShownProject_Shown_Once()
    {
        var s = Settings("Film X");
        CustomLut(s.Active, @"C:\Luty\film.cube");

        var checks = AppendRules.StartChecks(s, fullCopies: 0, [Entry("film x"), Entry("Film X")]);

        Assert.Equal([new StartCheck(StartCheckKind.Lut, @"C:\Luty\film.cube", "Film X", Shown: true)], checks);
    }

    // One file used by several profiles is checked once (any case); the shown profile comes first.
    [Fact]
    public void StartChecks_SameFileInTwoProfiles_Once_ShownFirst()
    {
        var s = Settings("Reklama Y");
        CustomLut(s.Projects[0], @"C:\LUTY\SPOLECNY.cube");
        CustomLut(s.Active, @"C:\Luty\spolecny.cube");

        var checks = AppendRules.StartChecks(s, fullCopies: 1, [Entry("Film X")]);

        Assert.Equal([new StartCheck(StartCheckKind.Lut, @"C:\Luty\spolecny.cube", "Reklama Y", Shown: true)], checks);
    }

    [Fact]
    public void StartChecks_GoneProject_BezProjektu_AndNothingUnused()
    {
        var s = Settings("Film X");
        CustomLut(s, @"C:\Luty\bez.cube");
        s.Proxy.Enabled = false;   // the LUT still counts: the thumbnails (analyzeClips) use it
        s.Projects[1].Proxy.LutMode = LutMode.Custom;
        s.Projects[1].Proxy.LutPath = @"C:\Luty\reklama.cube";
        s.Projects[1].AnalyzeClips = false;   // neither proxies nor thumbnails: no LUT is used

        var checks = AppendRules.StartChecks(s, fullCopies: 0, [Entry("Smazaný"), Entry("Reklama Y")]);

        Assert.Equal([new StartCheck(StartCheckKind.Lut, @"C:\Luty\bez.cube", "Bez projektu", Shown: false)], checks);
    }
}
