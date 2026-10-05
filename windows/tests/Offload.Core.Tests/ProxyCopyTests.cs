namespace Offload.Core.Tests;

/// The extra copy of each proxy into the user's proxy folder: `<folder>\<date>\<card>\<name>.mov`, never overwriting.
public class ProxyCopyTests
{
    static string Proxy(TempDir dir, int seed = 1)
    {
        var path = Path.Combine(dir.Path, "d1", "2026-10-05", "A001", "_PROXY", "C0001.mov");
        TestFs.WriteRandom(path, 5000, seed);
        return path;
    }

    [Fact]
    public void Copies_IntoDateAndCardFolder()
    {
        using var dir = new TempDir();
        var src = Proxy(dir);
        var extra = dir.Sub("proxies");

        var (path, error) = ProxyCopy.ToExtraFolder(src, extra, "2026-10-05", "A001", "C0001.mov");

        Assert.Null(error);
        Assert.Equal(Path.Combine(extra, "2026-10-05", "A001", "C0001.mov"), path);
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(path!));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.part"));
    }

    [Fact]
    public void ExistingName_GetsSuffix_NeverOverwrites()
    {
        using var dir = new TempDir();
        var src = Proxy(dir);
        var extra = dir.Sub("proxies");
        var target = Path.Combine(extra, "2026-10-05", "A001");
        var old = TestFs.WriteRandom(Path.Combine(target, "C0001.mov"), 100, 9);

        var (second, e2) = ProxyCopy.ToExtraFolder(src, extra, "2026-10-05", "A001", "C0001.mov");
        var (third, e3) = ProxyCopy.ToExtraFolder(src, extra, "2026-10-05", "A001", "C0001.mov");

        Assert.Null(e2);
        Assert.Null(e3);
        Assert.Equal(Path.Combine(target, "C0001_2.mov"), second);
        Assert.Equal(Path.Combine(target, "C0001_3.mov"), third);
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(target, "C0001.mov")));
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(third!));
    }

    [Fact]
    public void MissingFolder_ErrorNotThrow_NothingCreated()
    {
        using var dir = new TempDir();
        var src = Proxy(dir);
        var extra = Path.Combine(dir.Path, "disconnected");

        var (path, error) = ProxyCopy.ToExtraFolder(src, extra, "2026-10-05", "A001", "C0001.mov");

        Assert.Null(path);
        Assert.Equal("složka není dostupná", error);
        Assert.False(Directory.Exists(extra));
    }

    [Fact]
    public void FailedCopy_ErrorAndNoTempLeft()
    {
        using var dir = new TempDir();
        var src = Proxy(dir);
        var extra = dir.Sub("proxies");
        var target = Path.Combine(extra, "2026-10-05", "A001");
        TestFs.WriteRandom(Path.Combine(target, ".C0001.mov.part"), 10, 3);   // left behind by a crash

        string? path, error;
        using (new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.None))   // source cannot be read
            (path, error) = ProxyCopy.ToExtraFolder(src, extra, "2026-10-05", "A001", "C0001.mov");

        Assert.Null(path);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public void StaleTemp_Replaced()
    {
        using var dir = new TempDir();
        var src = Proxy(dir);
        var extra = dir.Sub("proxies");
        var target = Path.Combine(extra, "2026-10-05", "A001");
        TestFs.WriteRandom(Path.Combine(target, ".C0001.mov.part"), 10, 3);

        var (path, error) = ProxyCopy.ToExtraFolder(src, extra, "2026-10-05", "A001", "C0001.mov");

        Assert.Null(error);
        Assert.Equal(Path.Combine(target, "C0001.mov"), path);
        Assert.Equal(path, Assert.Single(Directory.GetFiles(target)));
    }
}
