using Offload.Core.Desktop;

namespace Offload.Core.Tests;

public class SettingsStoreTests
{
    static AppSettings LoadJson(TempDir dir, string json)
    {
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, json);
        return SettingsStore.Load(path);
    }

    [Fact]
    public void Missing_Defaults()
    {
        using var dir = new TempDir();
        var s = SettingsStore.Load(Path.Combine(dir.Path, "settings.json"));
        Assert.Empty(s.DestRoots);
        Assert.True(s.Verify);
        Assert.True(s.AnalyzeClips);
        Assert.False(s.Proxy.Enabled);
        Assert.Equal(1080, s.Proxy.Height);
        Assert.Empty(s.PortNames);
    }

    [Fact]
    public void Corrupt_Defaults()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, "{not json");
        Assert.True(s.Verify);
        Assert.Empty(s.DestRoots);
    }

    // Review focus 2: a Mac-shaped file with missing and unknown keys.
    [Fact]
    public void MacKeys_PartialAndUnknown()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """
            {"destRoots":["D:\\A","d:\\a"," "],"verify":false,"proxy":{"enabled":true,"codec":"proresLT"},
             "tools":{"ffmpeg":"C:\\x\\ffmpeg.exe"},"futureKey":42}
            """);
        Assert.Equal([@"D:\A"], s.DestRoots);
        Assert.False(s.Verify);
        Assert.True(s.AnalyzeClips);
        Assert.True(s.Proxy.Enabled);
        Assert.Equal(ProxyCodec.ProresLT, s.Proxy.Codec);
        Assert.Equal(1080, s.Proxy.Height);
        Assert.True(s.Proxy.BurnTimecode);
        Assert.Equal(@"C:\x\ffmpeg.exe", s.Tools.Ffmpeg);
    }

    // Review focus 2: one bad section never costs the others.
    [Fact]
    public void BadSection_KeepsTheRest()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"destRoots":["E:\\Backup"],"proxy":{"codec":"av1"},"verify":"yes","portNames":{"A#1":"vpředu"}}""");
        Assert.Equal([@"E:\Backup"], s.DestRoots);
        Assert.Equal(ProxyCodec.H264, s.Proxy.Codec);
        Assert.True(s.Verify);
        Assert.Equal("vpředu", s.PortNames["a#1"]);
    }

    // Final review 1: a hand-edited file must never stop the app from starting.
    [Fact]
    public void DuplicateTopLevelKey_LastWins_NeverThrows()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"verify":false,"verify":true,"destRoots":["D:\\A"],"analyzeClips":false}""");
        Assert.True(s.Verify);
        Assert.Equal([@"D:\A"], s.DestRoots);
        Assert.False(s.AnalyzeClips);
    }

    [Fact]
    public void PortNames_KeysDifferingOnlyInCase_LastWins()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"portNames":{"USB#1":"a","usb#1":"b","X#2":null,"Y#3":7,"Z#4":"c"},"verify":false}""");
        Assert.Equal("b", s.PortNames["USB#1"]);
        Assert.Equal("c", s.PortNames["z#4"]);
        Assert.Equal(2, s.PortNames.Count);
        Assert.False(s.Verify);
    }

    [Fact]
    public void PortNamesSetter_CaseCollision_DoesNotThrow()
    {
        var s = new AppSettings { PortNames = new Dictionary<string, string> { ["A#1"] = "x", ["a#1"] = "y" } };
        Assert.Equal("y", s.PortNames["A#1"]);
        Assert.Single(s.PortNames);
    }

    [Fact]
    public void DestRoots_NullAndNonStringEntries_Dropped()
    {
        using var dir = new TempDir();
        Assert.Equal([@"D:\A"], LoadJson(dir, """{"destRoots":[null,"D:\\A"]}""").DestRoots);
        Assert.Equal([@"D:\A", @"E:\B"], LoadJson(dir, """{"destRoots":["D:\\A",5,{"x":1},"E:\\B"]}""").DestRoots);
    }

    [Theory]
    [InlineData("""{"proxy":{"height":720,"height":540}}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""null""")]
    [InlineData("""{"destRoots":"D:\\A","portNames":[1],"tools":5,"proxy":null}""")]
    [InlineData("""{"verify":tru""")]
    public void OddFiles_NeverThrow(string json)
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, json);
        Assert.NotNull(s.Proxy);
        Assert.NotNull(s.Tools);
        Assert.NotNull(s.DestRoots);
    }

    [Fact]
    public void OutOfRange_Clamped()
    {
        using var dir = new TempDir();
        var s = LoadJson(dir, """{"proxy":{"height":999,"bitrateMbps":500,"frameScale":0.1,"frameAspect":-1,"lutPath":""}}""");
        Assert.Equal(1080, s.Proxy.Height);
        Assert.Equal(50, s.Proxy.BitrateMbps);
        Assert.Equal(0.5, s.Proxy.FrameScale);
        Assert.Null(s.Proxy.FrameAspect);
        Assert.Null(s.Proxy.LutPath);
    }

    [Fact]
    public void RoundTrip_PortNamesIgnoreCase_NoTempLeft()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sub", "settings.json");
        var s = new AppSettings { DestRoots = [@"D:\Backup"], Verify = false };
        s.PortNames[@"USB\ROOT_HUB30\4&X#6"] = "přední levý";
        s.Proxy.FrameAspect = 1.85;
        SettingsStore.Save(s, path);
        var back = SettingsStore.Load(path);
        Assert.Equal([@"D:\Backup"], back.DestRoots);
        Assert.False(back.Verify);
        Assert.Equal("přední levý", back.PortNames[@"usb\root_hub30\4&x#6"]);
        Assert.Equal(1.85, back.Proxy.FrameAspect);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("přední levý", File.ReadAllText(path));
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var p = new ProxySettings { LutPath = "a.cube" };
        var c = SettingsStore.Clone(p);
        c.LutPath = "b.cube";
        Assert.Equal("a.cube", p.LutPath);
    }

    // Review focus 4
    [Fact]
    public void ProxyPreflight_MissingLut_Blocks_WhenProxyOrAnalysisUseIt()
    {
        using var dir = new TempDir();
        var lut = Path.Combine(dir.Path, "look.cube");
        var p = new ProxySettings { Enabled = true, LutPath = lut };
        Assert.Contains("look.cube", ProxyPreflight.Blocking(p, analyzeClips: false));
        p.Enabled = false;
        Assert.NotNull(ProxyPreflight.Blocking(p, analyzeClips: true));
        Assert.Null(ProxyPreflight.Blocking(p, analyzeClips: false));
        File.WriteAllText(lut, "LUT_3D_SIZE 2");
        p.Enabled = true;
        Assert.Null(ProxyPreflight.Blocking(p, analyzeClips: true));
    }

    // Final review 3: the GUI checks the LUT off the UI thread and passes the cached answer.
    [Fact]
    public void ProxyPreflight_CachedLutState()
    {
        var p = new ProxySettings { Enabled = true, LutPath = @"Z:\luts\look.cube" };
        Assert.Equal(@"Z:\luts\look.cube", ProxyPreflight.LutInUse(p, analyzeClips: false));
        Assert.Null(ProxyPreflight.Blocking(p, false, lutExists: null));    // not checked yet: Start checks once more
        Assert.Null(ProxyPreflight.Blocking(p, false, lutExists: true));
        Assert.Equal("LUT nenalezen: look.cube — vyberte jiný nebo ho odeberte.", ProxyPreflight.Blocking(p, false, lutExists: false));
        p.Enabled = false;
        Assert.Null(ProxyPreflight.LutInUse(p, analyzeClips: false));
        Assert.Null(ProxyPreflight.Blocking(p, false, lutExists: false));
        Assert.NotNull(ProxyPreflight.LutInUse(p, analyzeClips: true));
        p.LutPath = null;
        Assert.Null(ProxyPreflight.LutInUse(p, analyzeClips: true));
    }

    [Fact]
    public void ProxyPreflight_MissingFfmpeg_WarnsOnly()
    {
        var none = new ExternalTools();
        Assert.NotNull(ProxyPreflight.Warning(new ProxySettings { Enabled = true }, analyzeClips: false, none));
        Assert.NotNull(ProxyPreflight.Warning(new ProxySettings(), analyzeClips: true, none));
        Assert.Null(ProxyPreflight.Warning(new ProxySettings(), analyzeClips: false, none));
        Assert.Null(ProxyPreflight.Warning(new ProxySettings { Enabled = true }, true, new ExternalTools { Ffmpeg = "f", Ffprobe = "p" }));
    }

    [Fact]
    public void AppDataFolder_Override()
    {
        var old = Environment.GetEnvironmentVariable("OFFLOAD_SETTINGS_DIR");
        try
        {
            Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", @"X:\iso");
            Assert.Equal(@"X:\iso", AppData.Folder);
            Assert.Equal(@"X:\iso\settings.json", SettingsStore.DefaultPath);
            Assert.Equal(@"X:\iso\hwtest.json", Hardware.HwTest.DefaultPath);
            // Unset (also when the shell running the tests has it set): %APPDATA%\Offload.
            Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", null);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offload"), AppData.Folder);
            Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", "");
            Assert.EndsWith(@"\Offload", AppData.Folder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", old);
        }
        Assert.Equal(old is { Length: > 0 } ? old : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offload"),
            AppData.Folder);
    }

    [Fact]
    public void Utf8Bom_Preserved()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var json = """{"verify":false,"destRoots":["D:\\X"]}""";
        var bytes = new System.Text.UTF8Encoding(true).GetBytes(json);
        File.WriteAllBytes(path, bytes);
        var s = SettingsStore.Load(path);
        Assert.False(s.Verify);
        Assert.Equal([@"D:\X"], s.DestRoots);
    }

    [Fact]
    public void Clone_PreservesPortNamesCaseInsensitivity()
    {
        var s = new AppSettings();
        s.PortNames[@"A#1"] = "x";
        var c = SettingsStore.Clone(s);
        Assert.Equal("x", c.PortNames[@"a#1"]);
    }
}
