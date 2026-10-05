namespace Offload.Core.Tests;

public class BrawCoreTests
{
    // Task 1 — library search
    [Fact]
    public void Library_SearchOrder_ExeDirFirstThenSdkPlayerResolve()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.Equal(
        [
            @"D:\Offload\bin",
            Path.Combine(pf, @"Blackmagic Design\Blackmagic RAW\Blackmagic RAW SDK\Win\Libraries"),
            Path.Combine(pf, @"Blackmagic Design\Blackmagic RAW\Blackmagic RAW Player"),
            Path.Combine(pf, @"Blackmagic Design\DaVinci Resolve"),
        ], BrawLibrary.SearchDirs(@"D:\Offload\bin"));
    }

    [Fact]
    public void Library_Find_FirstExisting()
    {
        using var dir = new TempDir();
        var a = dir.Sub("a");
        var b = dir.Sub("b");
        Assert.Null(BrawLibrary.Find([a, b]));
        File.WriteAllText(Path.Combine(b, "BlackmagicRawAPI.dll"), "x");
        Assert.Equal(Path.Combine(b, "BlackmagicRawAPI.dll"), BrawLibrary.Find([a, b]));
        File.WriteAllText(Path.Combine(a, "BlackmagicRawAPI.dll"), "x");
        Assert.Equal(Path.Combine(a, "BlackmagicRawAPI.dll"), BrawLibrary.Find([a, b]));
    }

    // The library search is injected, so these hold on a PC that has Resolve (or the SDK) installed.
    [Fact]
    public void Backend_AvailableOnlyWithDecoderAndLibrary()
    {
        using var dir = new TempDir();
        var exe = Path.Combine(dir.Path, "braw-decode.exe");
        File.WriteAllText(exe, "x");
        string? lib = null;
        string? searchedIn = null;
        var backend = new BrawBackend(d => { searchedIn = d; return lib; });

        // no decoder (and the library is never even looked for)
        Assert.False(backend.IsAvailable(new ExternalTools { NoSearch = true }));
        Assert.Null(searchedIn);
        // decoder, no library
        Assert.False(backend.IsAvailable(new ExternalTools { BrawDecoder = exe, NoSearch = true }));
        Assert.Equal(dir.Path, searchedIn);
        // both
        lib = Path.Combine(dir.Path, "BlackmagicRawAPI.dll");
        Assert.True(backend.IsAvailable(new ExternalTools { BrawDecoder = exe, NoSearch = true }));
    }

    [Fact]
    public void Backend_DefaultSearch_FindsLibraryNextToDecoder()
    {
        using var dir = new TempDir();
        var exe = Path.Combine(dir.Path, "braw-decode.exe");
        File.WriteAllText(exe, "x");
        File.WriteAllText(Path.Combine(dir.Path, "BlackmagicRawAPI.dll"), "x");
        Assert.True(new BrawBackend().IsAvailable(new ExternalTools { BrawDecoder = exe, NoSearch = true }));
    }
}
