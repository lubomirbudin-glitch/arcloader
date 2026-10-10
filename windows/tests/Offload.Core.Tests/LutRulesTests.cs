using System.Text.Json;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// Spec 4.2, R18: own LUT rules — tried top to bottom, only in Auto mode, before the camera is recognised.
public class LutRulesTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    static Clip VideoClip(string path = "XDROOT/Clip/C0001.MP4") =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = ClipFormat.Video, Segments = [path], Bytes = 1 };

    static LutRule Rule(string match, string value, string? lut = null, string? path = null) => new() { Match = match, Value = value, Lut = lut, LutPath = path };

    static ProxySettings Auto(params LutRule[] rules) => new() { Enabled = true, LutMode = LutMode.Auto, LutRules = [.. rules] };

    [Fact]
    public void EveryMatch_AndTheReason()
    {
        var info = new ClipInfo { Camera = "Sony ILME-FX6V", CameraSerial = "0123456", Reel = "A007" };
        var clip = VideoClip("XDROOT/Clip/C0001.MP4");
        (string Match, string Value, string Reason)[] cases =
        [
            ("camera", "fx6", "rule 1: camera contains \"fx6\""),
            ("serial", " 0123456 ", "rule 1: camera serial number \"0123456\""),
            ("reel", "A", "rule 1: reel starts with \"A\""),
            ("reel", "a00", "rule 1: reel starts with \"a00\""),
            ("folder", "a001/xdroot/*", "rule 1: folder matches \"a001/xdroot/*\""),
            ("folder", "A00?/*/Clip", "rule 1: folder matches \"A00?/*/Clip\""),
        ];
        foreach (var (match, value, reason) in cases)
        {
            var use = LutResolver.Resolve(clip, info, @"X:\none\C0001.MP4", Auto(Rule(match, value, "arri-logc4-awg4")), Cat, "A001");
            Assert.Equal(("rule", "arri-logc4-awg4", reason), (use.Source, use.Id, use.Reason));
            Assert.True(File.Exists(use.File));
        }
    }

    [Fact]
    public void NoMatch_FallsThroughToAuto()
    {
        var info = new ClipInfo { Camera = "ARRI ALEXA 35", CameraSerial = "35-1", Reel = "B001" };
        var s = Auto(Rule("camera", "FX6", "none"), Rule("serial", "35", "none"), Rule("reel", "A", "none"),
            Rule("folder", "A001/CLIP", "none"), Rule("folder", "A001", "none"));
        var use = LutResolver.Resolve(VideoClip("XDROOT/Clip/C0001.MP4"), info, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal(("auto", "arri-logc4-awg4"), (use.Source, use.Id));
    }

    // The order is the priority; "none" decides too (no LUT, and no camera detection after it).
    [Fact]
    public void FirstMatchWins_NoneDecides()
    {
        var info = new ClipInfo { Camera = "ARRI ALEXA 35", Reel = "A001" };
        var s = Auto(Rule("camera", "Sony", "sony-slog3-sgamut3"), Rule("reel", "A", "none"), Rule("camera", "ALEXA", "arri-logc3-awg3"));
        var use = LutResolver.Resolve(VideoClip(), info, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal(("none", null, "rule 2: reel starts with \"A\""), (use.Source, use.Id, use.Reason));
        Assert.Null(use.File);
    }

    [Fact]
    public void OwnCube_IsTheFile()
    {
        using var dir = new TempDir();
        var cube = Path.Combine(dir.Path, "show.cube");
        TestMedia.IdentityLut(cube);
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo { Camera = "FX6" }, @"X:\none\C0001.MP4", Auto(Rule("camera", "FX6", path: cube)), Cat, "A001");
        Assert.Equal(("rule", null, "show.cube", cube), (use.Source, use.Id, use.Name, use.File));
        Assert.False(use.IsBuiltin);
    }

    // R18: rules only in Auto mode — a chosen built-in, custom or no LUT is the user's explicit choice.
    [Theory]
    [InlineData(LutMode.None, "none")]
    [InlineData(LutMode.Builtin, "builtin")]
    public void OnlyInAutoMode(LutMode mode, string source)
    {
        var s = Auto(Rule("camera", "FX6", "arri-logc4-awg4"));
        s.LutMode = mode;
        s.LutBuiltin = "sony-slog3-sgamut3";
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo { Camera = "FX6" }, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal(source, use.Source);
    }

    // Unknown serial number: a "serial" rule never matches. Without a reel tag the reel comes from the name / card.
    [Fact]
    public void UnknownSerial_NeverMatches_ReelFromCardFolder()
    {
        var s = Auto(Rule("serial", "123", "none"), Rule("reel", "B0", "arri-logc4-awg4"));
        var use = LutResolver.Resolve(VideoClip("XDROOT/Clip/C0001.MP4"), new ClipInfo(), @"X:\none\C0001.MP4", s, Cat, "B007");
        Assert.Equal(("rule", "rule 2: reel starts with \"B0\""), (use.Source, use.Reason));
    }

    [Fact]
    public void BuiltinMissingFromTheCatalog_NoLut_Said()
    {
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo { Camera = "FX6" }, @"X:\none\C0001.MP4", Auto(Rule("camera", "FX6", "gone-id")), Cat, "A001");
        Assert.Equal(("none", "rule 1: camera contains \"FX6\" — built-in LUT not found"), (use.Source, use.Reason));
    }

    // Spec 4.2: an invalid rule (no value, both or neither LUT) is dropped when the settings are read. Ruling I3: a rule
    // with a match this version does not know (a newer version's) is NOT dropped — kept in place, never used.
    [Fact]
    public void InvalidRules_DroppedOnLoad_ValidKeptInOrder_FutureMatchKept()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
            {"proxy":{"lutRules":[
              {"match":"camera","value":" FX6 ","lut":"sony-slog3-sgamut3cine","lens":"35mm"},
              {"match":"camera","value":"","lut":"none"},
              {"match":"camera","value":"X","lut":"none","lutPath":"C:\\a.cube"},
              {"match":"camera","value":"X"},
              {"match":"lens","value":"X","lut":"none"},
              null,
              {"match":"folder","value":"A001/*","lutPath":"C:\\show.cube"}
            ]}}
            """);
        var s = SettingsStore.Load(path);
        Assert.Equal([("camera", "FX6"), ("lens", "X"), ("folder", "A001/*")], s.Proxy.LutRules.Select(r => (r.Match, r.Value)));
        Assert.Equal(@"C:\show.cube", s.Proxy.LutRules[2].LutPath);

        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rules = doc.RootElement.GetProperty("proxy").GetProperty("lutRules");
        Assert.Equal(3, rules.GetArrayLength());
        Assert.Equal("35mm", rules[0].GetProperty("lens").GetString());   // a newer version's key inside a rule stays
        Assert.False(rules[0].TryGetProperty("lutPath", out _));
        Assert.Equal("lens", rules[1].GetProperty("match").GetString());
    }

    // Ruling I3: a file with a future match type survives load, save and load byte-stable — whatever shape its rule has.
    [Fact]
    public void FutureMatch_SurvivesLoadSaveLoad_ByteStable()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
            {"proxy":{"lutRules":[
              {"match":"camera","value":"FX6","lut":"none"},
              {"match":"lens","pattern":"35mm","lut":"none","weight":{"a":[1,2]}},
              {"match":"reel","value":"A","lutPath":"C:\\show.cube"}
            ]}}
            """);
        var s = SettingsStore.Load(path);
        SettingsStore.Save(s, path);
        var first = File.ReadAllBytes(path);
        Assert.Contains("\"pattern\": \"35mm\"", File.ReadAllText(path));
        Assert.DoesNotContain("\"value\":\"\"", File.ReadAllText(path));   // nothing invented for the rule we do not know

        SettingsStore.Save(SettingsStore.Load(path), path);
        Assert.Equal(first, File.ReadAllBytes(path));
        Assert.Equal(3, SettingsStore.Clone(SettingsStore.Load(path).Proxy).LutRules.Count);
    }

    // Ruling I3: the unknown rule never matches, and the numbering stays positional.
    [Fact]
    public void FutureMatch_NeverMatches_NumberingPositional()
    {
        var info = new ClipInfo { Camera = "Sony FX6", Reel = "A001" };
        var s = Auto(Rule("lens", "FX6", "none"), Rule("camera", "FX6", "arri-logc4-awg4"));
        var use = LutResolver.Resolve(VideoClip(), info, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal(("rule", "rule 2: camera contains \"FX6\""), (use.Source, use.Reason));
        Assert.False(LutRules.IsValid(s.LutRules[0]));
        Assert.True(LutRules.IsFuture(s.LutRules[0]));
        Assert.False(LutRules.IsFuture(s.LutRules[1]));
        Assert.Equal([new LutFile(@"C:\z.cube", 2)],
            ProxyPreflight.LutFilesInUse(Auto(Rule("lens", "X", path: @"C:\gone.cube"), Rule("camera", "X", path: @"C:\z.cube")), analyzeClips: false));
    }

    // M9: a rule of the wrong JSON type is kept on its own; the rest of the proxy section stays usable.
    [Fact]
    public void MalformedRuleItem_DoesNotMakeTheProxySectionRaw()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """
            {"proxy":{"height":720,"lutRules":[7,{"match":"camera","value":5,"lut":"none"},{"match":"camera","value":"FX6","lut":"none"}]}}
            """);
        var s = SettingsStore.Load(path);
        Assert.Equal(720, s.Proxy.Height);
        Assert.Null(s.RawSections?.GetValueOrDefault("proxy"));
        Assert.Equal(3, s.Proxy.LutRules.Count);
        Assert.Equal(("camera", "FX6"), (s.Proxy.LutRules[2].Match, s.Proxy.LutRules[2].Value));
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo { Camera = "FX6" }, @"X:\none\C0001.MP4", s.Proxy, Cat, "A001");
        Assert.Equal("rule 3: camera contains \"FX6\"", use.Reason);

        s.Proxy.Height = 1080;
        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rules = doc.RootElement.GetProperty("proxy").GetProperty("lutRules");
        Assert.Equal(1080, doc.RootElement.GetProperty("proxy").GetProperty("height").GetInt32());
        Assert.Equal(7, rules[0].GetInt32());
        Assert.Equal(5, rules[1].GetProperty("value").GetInt32());
    }

    // M10: a glob that runs into the regex timeout is no match, never an exception out of LutResolver.
    [Fact]
    public void GlobTimeout_NoMatch_NoException()
    {
        var clip = VideoClip("CLIP/" + new string('a', 4000) + "/C0001.MP4");
        var s = Auto(Rule("folder", "*a*a*a*a*a*a*a*a*a*a*b", "none"));
        var use = LutResolver.Resolve(clip, new ClipInfo { Camera = "ARRI ALEXA 35" }, @"X:\none\C0001.MP4", s, Cat, "A001");
        Assert.Equal("auto", use.Source);
    }

    [Fact]
    public void NoRules_NoKey_InSettingsOrReport()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        SettingsStore.Save(new AppSettings(), path);
        Assert.DoesNotContain("lutRules", File.ReadAllText(path));
        var clone = SettingsStore.Clone(Auto(Rule("reel", "A", "none")));
        Assert.Single(clone.LutRules);
    }

    // Spec 4.2: a rule's own .cube that is missing blocks the start — "LUT z pravidla 2 nenalezen: show.cube".
    [Fact]
    public void Preflight_RuleCube_Missing_Blocks_OnlyInAutoAndWhenUsed()
    {
        using var dir = new TempDir();
        var there = Path.Combine(dir.Path, "there.cube");
        TestMedia.IdentityLut(there);
        var s = Auto(Rule("camera", "FX6", path: there), Rule("reel", "A", "none"), Rule("folder", "*", path: Path.Combine(dir.Path, "show.cube")));
        Assert.Equal([new LutFile(there, 1), new LutFile(Path.Combine(dir.Path, "show.cube"), 3)], ProxyPreflight.LutFilesInUse(s, analyzeClips: false));
        Assert.Equal("LUT of rule 3 not found: show.cube", ProxyPreflight.Blocking(s, analyzeClips: false));

        s.Enabled = false;
        Assert.Null(ProxyPreflight.Blocking(s, analyzeClips: false));       // nothing uses a LUT
        Assert.NotNull(ProxyPreflight.Blocking(s, analyzeClips: true));     // thumbnails do
        s.LutMode = LutMode.None;
        Assert.Empty(ProxyPreflight.LutFilesInUse(s, analyzeClips: true));
        s.LutMode = LutMode.Custom;
        s.LutPath = Path.Combine(dir.Path, "gone.cube");
        Assert.Equal("LUT not found: gone.cube — select another one or remove it.", ProxyPreflight.Blocking(s, analyzeClips: true));
    }

    [Fact]
    public void FolderOf_CardAndClipFolder()
    {
        Assert.Equal("A001/CLIP", LutRules.FolderOf(VideoClip("CLIP/A001C001.mov"), "A001"));
        Assert.Equal("A001/PRIVATE/M4ROOT/CLIP", LutRules.FolderOf(VideoClip("PRIVATE/M4ROOT/CLIP/C0001.MP4"), "A001"));
        Assert.Equal("A001", LutRules.FolderOf(VideoClip("A001C001.mov"), "A001"));
    }

    // Spec 4.2: the camera serial number — Sony XML `Device serialNo`, ffprobe tags.
    [Fact]
    public void CameraSerial_SonyXmlAndFfprobeTags()
    {
        using var dir = new TempDir();
        var clip = Path.Combine(dir.Path, "C0001.MP4");
        File.WriteAllText(clip, "x");
        File.WriteAllText(Path.Combine(dir.Path, "C0001M01.XML"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
              <Device manufacturer="Sony" modelName="ILME-FX6V" serialNo=" 5012345 "/>
            </NonRealTimeMeta>
            """);
        Assert.Equal("5012345", SonyXml.Serial(clip));
        Assert.Null(SonyXml.Serial(Path.Combine(dir.Path, "C0002.MP4")));

        var apple = FFProbe.Parse("""{"streams":[],"format":{"tags":{"com.apple.quicktime.camera.identifier":"BMPCC6K-1234"}}}""");
        Assert.Equal("BMPCC6K-1234", apple.CameraSerial);
        var stream = FFProbe.Parse("""{"streams":[{"codec_type":"video","tags":{"Camera_Serial_Number":" 77 "}}],"format":{}}""");
        Assert.Equal("77", stream.CameraSerial);
        Assert.Null(FFProbe.Parse("""{"streams":[],"format":{"tags":{"camera_serial_number":"  "}}}""").CameraSerial);
    }

    [Fact]
    public void CameraSerial_InTheReportOnlyWhenKnown()
    {
        Assert.Contains("\"cameraSerial\":\"5012345\"", JsonSerializer.Serialize(new ClipInfo { CameraSerial = "5012345" }));
        Assert.DoesNotContain("cameraSerial", JsonSerializer.Serialize(new ClipInfo()));
    }

    // The job hands the card folder and the reel to the rules: a folder rule and a reel rule decide two clips of one card
    // (report `lut.source = "rule"` with the reason), a third clip falls through to the camera detection.
    [FfmpegFact]
    public void Job_RulesDecide_ReportSaysRule()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "B", "A001C001.mov"), seconds: 1, audio: false, width: 320, height: 180);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "B002C001.mov"), seconds: 1, audio: false, width: 320, height: 180);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.mov"), seconds: 1, audio: false, width: 320, height: 180);
        var proxy = Auto(Rule("folder", "A001/B", "arri-logc3-awg3"), Rule("reel", "B", "none"));
        proxy.Enabled = false;
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-07", RecordHistory = false, Proxy = proxy },
            AppendFixtures.Options());

        var report = job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var luts = report!.Clips.ToDictionary(c => c.Clip.Name, c => c.Lut!);
        Assert.Equal(("rule", "rule 1: folder matches \"A001/B\""), (luts["A001C001"].Source, luts["A001C001"].Reason));
        Assert.Equal(("none", "rule 2: reel starts with \"B\""), (luts["B002C001"].Source, luts["B002C001"].Reason));
        Assert.NotEqual("rule", luts["C0001"].Source);
        Assert.Contains("\"source\": \"rule\"", File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    // Spec 4.2: two rules with two own .cube files in one job — each gets its own file in the renderer's work folder.
    [Fact]
    public void Renderer_TwoOwnCubes_TwoFiles_OnceEach()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "a", "show.cube");
        var b = Path.Combine(dir.Path, "b", "show.cube");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!);
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        TestMedia.IdentityLut(a);
        File.WriteAllText(b, "TITLE \"b\"\nLUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        var logs = new List<string>();
        using var r = new ProxyRenderer(new ProxySettings(), new ExternalTools { NoSearch = true }) { Log = logs.Add };

        var na = r.PrepareLut(new LutUse("rule", null, "show.cube", "x") { File = a });
        var nb = r.PrepareLut(new LutUse("rule", null, "show.cube", "y") { File = b });
        var again = r.PrepareLut(new LutUse("custom", null, "show.cube", "z") { File = a.ToUpperInvariant() });
        var gone = r.PrepareLut(new LutUse("rule", null, "gone.cube", "z") { File = Path.Combine(dir.Path, "gone.cube") });

        Assert.Matches("^lut-custom-[0-9a-f]{8}\\.cube$", na!);
        Assert.NotEqual(na, nb);
        Assert.Equal(na, again);
        Assert.Null(gone);
        Assert.Contains(logs, l => l.Contains("gone.cube"));
        Assert.Equal("lut-arri-logc4-awg4.cube", r.PrepareLut(new LutUse("rule", "arri-logc4-awg4", "x", "y") { File = Cat.Find("arri-logc4-awg4")!.Path }));
    }

    // Task 3-4 note: the render-time "LUT nenalezen … proxy bez LUTu" also applies to a rule-chosen custom LUT.
    [Fact]
    public void Renderer_RuleCubeGone_ProxyNoteSaysSo_OnceLogged()
    {
        using var dir = new TempDir();
        var there = Path.Combine(dir.Path, "there.cube");
        TestMedia.IdentityLut(there);
        var gone = Path.Combine(dir.Path, "gone.cube");
        var logs = new List<string>();
        using var r = new ProxyRenderer(new ProxySettings(), new ExternalTools { NoSearch = true }) { Log = logs.Add };

        Assert.Equal("LUT not found: gone.cube — proxy without a LUT", r.LutNote(new LutUse("rule", null, "gone.cube", "x") { File = gone }));
        Assert.Equal("LUT not found: gone.cube — proxy without a LUT", r.LutNote(new LutUse("rule", null, "gone.cube", "x") { File = gone }));
        Assert.Null(r.LutNote(new LutUse("rule", null, "there.cube", "x") { File = there }));
        Assert.Null(r.LutNote(new LutUse("rule", "arri-logc4-awg4", "x", "y") { File = gone }));   // built-in
        Assert.Single(logs, l => l.Contains("gone.cube"));
    }
}
