using Offload.Core;

namespace Offload.Core.Tests;

public class LutResolverTests
{
    static readonly LutCatalog Cat = LutCatalog.Load(LutCatalog.DefaultFolder);

    static Clip VideoClip(string path = "XDROOT/Clip/C0001.MP4") =>
        new() { Name = Path.GetFileNameWithoutExtension(path), Path = path, Format = ClipFormat.Video, Segments = [path], Bytes = 1 };

    static string SonyXml(TempDir dir, string clipRel, string gamma, string primaries)
    {
        var clip = Path.Combine(dir.Path, clipRel.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(clip)!);
        File.WriteAllText(clip, "x");
        var xml = Path.Combine(Path.GetDirectoryName(clip)!, Path.GetFileNameWithoutExtension(clip) + "M01.XML");
        File.WriteAllText(xml, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <NonRealTimeMeta xmlns="urn:schemas-professionalDisc:nonRealTimeMeta:ver.2.10">
              <AcquisitionRecord><Group name="CameraUnitMetadataSet">
                <Item name="CaptureGammaEquation" value="{gamma}"/>
                <Item name="CaptureColorPrimaries" value="{primaries}"/>
              </Group></AcquisitionRecord>
            </NonRealTimeMeta>
            """);
        return clip;
    }

    static ProxySettings Auto(string? fallback = null) => new() { Enabled = true, LutMode = LutMode.Auto, LutFallback = fallback };

    [Theory]
    [InlineData("s-log3-cine", "S-Gamut3Cine", "sony-slog3-sgamut3cine")]
    [InlineData("s-log3", "S-Gamut3", "sony-slog3-sgamut3")]
    [InlineData("s-log3-cine", "", "sony-slog3-sgamut3cine")]
    public void Sony_SLog3_FromXml(string gamma, string primaries, string id)
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "XDROOT/Clip/C0001.MP4", gamma, primaries);
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo(), clip, Auto(), Cat);
        Assert.Equal(("auto", id), (use.Source, use.Id));
        Assert.Contains(gamma, use.Reason);
        Assert.True(File.Exists(use.File));
    }

    // Review focus 1
    [Fact]
    public void Sony_Rec709_NoLut()
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "PRIVATE/M4ROOT/CLIP/C0002.MP4", "rec709", "rec709");
        var use = LutResolver.Resolve(VideoClip("PRIVATE/M4ROOT/CLIP/C0002.MP4"), new ClipInfo(), clip, Auto("sony-slog3-sgamut3cine"), Cat);
        Assert.Equal("none", use.Source);
        Assert.Null(use.File);
        Assert.Contains("rec709", use.Reason);
    }

    [Theory]
    [InlineData("ARRI ALEXA 35", "arri-logc4-awg4")]
    [InlineData("ALEXA Mini LF", "arri-logc3-awg3")]
    [InlineData("AMIRA", "arri-logc3-awg3")]
    public void Arri_FromCameraModel(string camera, string id)
    {
        var use = LutResolver.Resolve(VideoClip("A001C001.mov"), new ClipInfo { Camera = camera }, @"Z:\none\A001C001.mov", Auto(), Cat);
        Assert.Equal(id, use.Id);
    }

    [Fact]
    public void Arriraw_UsesLogEncoding()
    {
        var clip = new Clip { Name = "A1", Path = "A1/A1.ari", Format = ClipFormat.Arriraw, Segments = ["A1/A1.ari"], Bytes = 1 };
        Assert.Equal("arri-logc4-awg4", LutResolver.Resolve(clip, new ClipInfo { LogEncoding = "LogC4" }, @"Z:\A1", Auto(), Cat).Id);
        Assert.Equal("arri-logc3-awg3", LutResolver.Resolve(clip, new ClipInfo { Camera = "ALEXA Mini" }, @"Z:\A1", Auto(), Cat).Id);
    }

    [Fact]
    public void Braw_And_R3d()
    {
        var braw = new Clip { Name = "B", Path = "B.braw", Format = ClipFormat.Braw, Segments = ["B.braw"], Bytes = 1 };
        var r3d = new Clip { Name = "R", Path = "R.RDC/R_001.R3D", Format = ClipFormat.R3d, Segments = ["R.RDC/R_001.R3D"], Bytes = 1 };
        Assert.Equal("bmd-film-gen5", LutResolver.Resolve(braw, null, @"Z:\B.braw", Auto(), Cat).Id);
        Assert.Equal("red-log3g10-rwg", LutResolver.Resolve(r3d, null, @"Z:\R", Auto(), Cat).Id);
    }

    // Review focus 1
    [Fact]
    public void Unknown_UsesFallback()
    {
        var clip = VideoClip("DCIM/100CANON/MVI_0001.MP4");
        var none = LutResolver.Resolve(clip, new ClipInfo { Camera = "Canon EOS R5" }, @"Z:\x.mp4", Auto(), Cat);
        Assert.Equal("none", none.Source);
        Assert.Equal("kamera nerozpoznána", none.Reason);
        var fb = LutResolver.Resolve(clip, new ClipInfo(), @"Z:\x.mp4", Auto("canon-clog3-cinemagamut"), Cat);
        Assert.Equal(("fallback", "canon-clog3-cinemagamut"), (fb.Source, fb.Id));
        Assert.Equal("kamera nerozpoznána — záložní LUT", fb.Reason);
    }

    [Fact]
    public void Modes_Builtin_Custom_None()
    {
        var clip = VideoClip();
        var b = LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "dji-dlog-dgamut" }, Cat);
        Assert.Equal(("builtin", "dji-dlog-dgamut", "vybraný v nastavení"), (b.Source, b.Id, b.Reason));
        var c = LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutMode = LutMode.Custom, LutPath = @"C:\luts\show.cube" }, Cat);
        Assert.Equal(("custom", null, "show.cube", @"C:\luts\show.cube"), (c.Source, c.Id, c.Name, c.File));
        var n = LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutMode = LutMode.None }, Cat);
        Assert.Equal(("none", "bez LUT"), (n.Source, n.Name));
        // No mode stored (older settings / code): a LutPath means custom, otherwise automatic.
        Assert.Equal("custom", LutResolver.Resolve(clip, null, @"Z:\x", new ProxySettings { LutPath = @"C:\l.cube" }, Cat).Source);
    }

    // Review focus 4
    [Fact]
    public void Auto_NoCatalog_NoLut()
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "XDROOT/Clip/C0001.MP4", "s-log3-cine", "S-Gamut3Cine");
        var use = LutResolver.Resolve(VideoClip(), null, clip, Auto(), LutCatalog.Load(null));
        Assert.Equal("none", use.Source);
        Assert.Equal("vestavěné LUTy chybí", use.Reason);
    }

    [Fact]
    public void ClipDetector_ReadsColorRange()
    {
        var info = FFProbe.Parse("""{"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"color_range":"pc"}],"format":{}}""");
        Assert.Equal("pc", info.ColorRange);
    }

    [Theory]
    [InlineData("ALEXA Mini LF 35mm", "arri-logc3-awg3")]
    [InlineData("ARRI ALEXA 35", "arri-logc4-awg4")]
    [InlineData("ALEXA35", "arri-logc4-awg4")]
    public void Arri_Alexa35_WordMatch(string camera, string id)
    {
        Assert.Equal(id, LutResolver.Resolve(VideoClip("A.mov"), new ClipInfo { Camera = camera }, @"Z:\none\A.mov", Auto(), Cat).Id);
        var raw = new Clip { Name = "A", Path = "A/A.ari", Format = ClipFormat.Arriraw, Segments = ["A/A.ari"], Bytes = 1 };
        Assert.Equal(id, LutResolver.Resolve(raw, new ClipInfo { Camera = camera }, @"Z:\A", Auto(), Cat).Id);
    }

    [Fact]
    public void Empty_LutPath_IsNotCustom()
    {
        Assert.Equal(LutMode.Auto, LutResolver.EffectiveMode(new ProxySettings { LutPath = "" }));
        Assert.Equal("none", LutResolver.Resolve(VideoClip(), null, @"Z:\x", new ProxySettings { LutMode = LutMode.Custom, LutPath = "" }, Cat).Source);
    }

    [Theory]
    [InlineData("s-log2")]
    [InlineData("hlg2")]
    [InlineData("s-cinetone")]
    public void Sony_OtherGamma_NoLut(string gamma)
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "XDROOT/Clip/C0003.MP4", gamma, "rec709");
        var use = LutResolver.Resolve(VideoClip(), new ClipInfo(), clip, Auto("sony-slog3-sgamut3cine"), Cat);
        Assert.Equal("none", use.Source);
    }

    [Fact]
    public void Sony_UpperCase_And_HyphenatedPrimaries()
    {
        using var dir = new TempDir();
        var clip = SonyXml(dir, "XDROOT/Clip/C0004.MP4", "S-LOG3-CINE", "s-gamut3-cine");
        Assert.Equal("sony-slog3-sgamut3cine", LutResolver.Resolve(VideoClip(), new ClipInfo(), clip, Auto(), Cat).Id);
    }

    [Fact]
    public void Json_RoundTrip()
    {
        var o = new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        Assert.Contains("\"lutMode\":\"auto\"", System.Text.Json.JsonSerializer.Serialize(new ProxySettings { LutMode = LutMode.Auto }, o));
        var use = LutResolver.Resolve(VideoClip(), null, @"Z:\x", new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "dji-dlog-dgamut" }, Cat);
        Assert.NotNull(use.File);
        var json = System.Text.Json.JsonSerializer.Serialize(use, o);
        Assert.DoesNotContain("ile", json);
        Assert.DoesNotContain(".cube", json);
        Assert.Contains("\"source\":\"builtin\"", json);
    }
}
