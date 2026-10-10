using Offload.Core;

namespace Offload.Core.Tests;

public class LutCatalogTests
{
    [Fact]
    public void RepoCatalog_Has11ValidLuts()
    {
        var cat = LutCatalog.Load(LutCatalog.DefaultFolder);
        Assert.NotNull(cat.Folder);
        Assert.Equal(11, cat.Luts.Count);
        Assert.All(cat.Luts, l => Assert.True(LutCatalog.IsValidCube(l.Path), l.Id));
        Assert.Equal("Sony S-Log3 / S-Gamut3.Cine → Rec.709", cat.Find("sony-slog3-sgamut3cine")!.Name);
        Assert.Null(cat.Find("nope"));
    }

    // Review focus 4
    [Fact]
    public void Catalog_MissingOrBroken_Empty()
    {
        using var dir = new TempDir();
        Assert.Empty(LutCatalog.Load(Path.Combine(dir.Path, "missing")).Luts);
        Assert.Empty(LutCatalog.Load(null).Luts);
        File.WriteAllText(Path.Combine(dir.Path, "luts.json"), "{not json");
        Assert.Empty(LutCatalog.Load(dir.Path).Luts);
        File.WriteAllText(Path.Combine(dir.Path, "luts.json"), """[{"id":"x","name":"X","maker":"M","file":"x.cube","curve":"c","gamut":"g"}]""");
        Assert.Empty(LutCatalog.Load(dir.Path).Luts);   // listed file does not exist
    }

    [Fact]
    public void IsValidCube()
    {
        using var dir = new TempDir();
        var ok = Path.Combine(dir.Path, "ok.cube");
        File.WriteAllLines(ok, ["TITLE \"t\"", "LUT_3D_SIZE 2", "0 0 0", "1 0 0", "0 1 0", "1 1 0", "0 0 1", "1 0 1", "0 1 1", "1 1 1"]);
        Assert.True(LutCatalog.IsValidCube(ok));
        var bad = Path.Combine(dir.Path, "bad.cube");
        File.WriteAllLines(bad, ["LUT_3D_SIZE 2", "0 0 0"]);
        Assert.False(LutCatalog.IsValidCube(bad));
        Assert.False(LutCatalog.IsValidCube(Path.Combine(dir.Path, "missing.cube")));
    }

    internal static readonly string[] Cube2 = ["TITLE \"vendor\"", "LUT_3D_SIZE 2", "0 0 0", "1 0 0", "0 1 0", "1 1 0", "0 0 1", "1 0 1", "0 1 1", "1 1 1"];

    // Mac 572f059: the user's own <id>.cube (e.g. ARRI's official Rec.709 LUT) wins over the built-in approximation.
    [Fact]
    public void UserOverride_ReplacesTheBuiltinWithTheSameId()
    {
        using var dir = new TempDir();
        var own = Path.Combine(dir.Path, "arri-logc4-awg4.cube");
        File.WriteAllLines(own, Cube2);
        File.WriteAllLines(Path.Combine(dir.Path, "sony-slog3-sgamut3.cube"), ["LUT_3D_SIZE 2", "0 0 0"]);   // broken: ignored
        File.WriteAllLines(Path.Combine(dir.Path, "my-look.cube"), Cube2);                                  // no such id: ignored
        var builtin = LutCatalog.Load(LutCatalog.DefaultFolder);
        var cat = builtin.WithUserOverrides(dir.Path);

        Assert.Equal(11, cat.Luts.Count);
        Assert.Equal(builtin.Folder, cat.Folder);
        var arri = cat.Find("arri-logc4-awg4")!;
        Assert.Equal(own, arri.Path);
        Assert.True(arri.UserFile);
        Assert.Equal("ARRI LogC4 / AWG4 → Rec.709 (custom file)", arri.Name);
        foreach (var l in cat.Luts.Where(l => l.Id != "arri-logc4-awg4"))
        {
            Assert.Equal(builtin.Find(l.Id), l);
            Assert.False(l.UserFile);
        }
        Assert.Null(cat.Find("my-look"));
        Assert.Equal(builtin.Luts, builtin.WithUserOverrides(Path.Combine(dir.Path, "missing")).Luts);
        Assert.Equal(builtin.Luts, builtin.WithUserOverrides(null).Luts);
    }

    [Fact]
    public void UserOverride_InTheReport_NameAndReason()
    {
        using var dir = new TempDir();
        var own = Path.Combine(dir.Path, "arri-logc4-awg4.cube");
        File.WriteAllLines(own, Cube2);
        var cat = LutCatalog.Load(LutCatalog.DefaultFolder).WithUserOverrides(dir.Path);
        var clip = new Clip { Name = "A009C002_261008_R2PK", Path = "A009C002_261008_R2PK.mxf", Format = ClipFormat.Arriraw, Segments = ["A009C002_261008_R2PK.mxf"], Bytes = 1 };
        var info = new ClipInfo { Camera = "ALEXA 35", LogEncoding = "LogC4" };

        var auto = LutResolver.Resolve(clip, info, @"X:\A009C002_261008_R2PK.mxf", new ProxySettings { LutMode = LutMode.Auto }, cat);
        Assert.Equal(("auto", "arri-logc4-awg4", own), (auto.Source, auto.Id, auto.File));
        Assert.EndsWith("(custom file)", auto.Name);
        Assert.Equal($"ARRIRAW (LogC4); file {own}", auto.Reason);
        Assert.True(auto.IsBuiltin);   // same input convention as the LUT it replaces (pre-map, art-cmd LogC4)

        var chosen = LutResolver.Resolve(clip, info, "", new ProxySettings { LutMode = LutMode.Builtin, LutBuiltin = "arri-logc4-awg4" }, cat);
        Assert.Equal(own, chosen.File);
        Assert.EndsWith($"; file {own}", chosen.Reason);

        var rule = LutRules.Use(1, new LutRule { Lut = "arri-logc4-awg4" }, cat);
        Assert.Equal(own, rule.File);
        Assert.EndsWith($"; file {own}", rule.Reason);
        Assert.EndsWith("(custom file)", rule.Name);
    }
}
