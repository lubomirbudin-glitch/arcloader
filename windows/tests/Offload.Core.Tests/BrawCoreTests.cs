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

    // Task 2 — thumbnails: braw-decode's fps is the SDK float (23.976) while positions use the exact rate.
    [Fact]
    public void StartFrame_IsRounded()
    {
        const double exact = 24000.0 / 1001;
        Assert.Equal(25, BrawBackend.StartFrame(25 / exact, 23.976f, 50));
        Assert.Equal(49, BrawBackend.StartFrame(49 / exact, 23.976f, 50));
        Assert.Equal(49, BrawBackend.StartFrame(100, 25, 50));
        Assert.Equal(25, BrawBackend.StartFrame(1, 0, 50));   // unknown fps → 25
        Assert.Equal(0, BrawBackend.StartFrame(1, 25, 0));
        Assert.Equal(0, BrawBackend.StartFrame(0, 23.976f, 50));
    }

    // Task 3 — gamma / gamut from --info
    [Fact]
    public void Info_GammaGamutGen_ReachClipInfo()
    {
        var i = BrawBackend.ParseInfo("""{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976000,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K","gamma":"Blackmagic Design Film","gamut":"Blackmagic Design","colorScienceGen":5}""");
        var c = BrawBackend.ToClipInfo(i);
        Assert.Equal(("Blackmagic Design Film", "Blackmagic Design", 5), (c.Gamma, c.Gamut, c.ColorScienceGen));
        // The Mac decoder (and older builds) print no gamma: still parses.
        var old = BrawBackend.ToClipInfo(BrawBackend.ParseInfo("""{"width":1,"height":1,"sourceWidth":1,"sourceHeight":1,"fps":25,"frames":1,"timecode":"","camera":""}"""));
        Assert.Equal((null, null, null), (old.Gamma, old.Gamut, old.ColorScienceGen));
    }
}
