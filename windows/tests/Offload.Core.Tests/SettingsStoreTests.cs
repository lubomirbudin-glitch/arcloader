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
        }
        finally
        {
            Environment.SetEnvironmentVariable("OFFLOAD_SETTINGS_DIR", old);
        }
        Assert.EndsWith(@"\Offload", AppData.Folder);
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
