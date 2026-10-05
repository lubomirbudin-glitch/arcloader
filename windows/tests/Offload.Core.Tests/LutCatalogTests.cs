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
}
