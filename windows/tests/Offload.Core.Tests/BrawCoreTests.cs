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

    // Final fix I1 — braw-decode's exit codes 3/4/5 become a Czech note at the clip; its own line goes to the log.

    const string Library = @"C:\BM\BlackmagicRawAPI.dll";

    /// A stand-in braw-decode: `--info` prints a one-frame 320×180 clip, or (failInfo) fails like the frames do;
    /// decoding frames writes `line` to stderr and exits `code`.
    internal static string FailingDecoder(string dir, int code, string line, bool failInfo)
    {
        var path = Path.Combine(dir, "braw-decode.cmd");
        File.WriteAllText(path, $$"""
            @echo off
            if "%~1"=="--info" goto {{(failInfo ? "fail" : "info")}}
            :fail
            1>&2 echo {{line}}
            exit /b {{code}}
            :info
            echo {"width":320,"height":180,"sourceWidth":320,"sourceHeight":180,"fps":25,"frames":1,"timecode":"","camera":""}
            exit /b 0

            """.ReplaceLineEndings("\r\n"));
        return path;
    }

    [Theory]
    [InlineData(3, "braw-decode: CreateCodec failed (0x80004005; library C:\\BM\\BlackmagicRawAPI.dll)",
        "Blackmagic RAW: cannot use the library C:\\BM\\BlackmagicRawAPI.dll — install the current Blackmagic RAW Player")]
    [InlineData(4, "braw-decode: cannot open D:\\A001.braw (0x80004005; library C:\\BM\\BlackmagicRawAPI.dll)",
        "Blackmagic RAW: cannot open the clip — it is damaged, or the library C:\\BM\\BlackmagicRawAPI.dll is older than the camera (install the current Blackmagic RAW Player)")]
    [InlineData(5, "braw-decode: frame 0 failed",
        "Blackmagic RAW: could not decode a frame of the clip (frame 0 failed)")]
    public void Info_DecoderExitCode_BecomesCzechNote(int code, string line, string note)
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = FailingDecoder(dir.Path, code, line, failInfo: true), NoSearch = true };
        var backend = new BrawBackend(_ => Library);
        var e = Assert.Throws<RawDecoderException>(() => backend.Probe(Path.Combine(dir.Path, "A001.braw"), tools, dir.Path));
        Assert.Equal(note, e.Message);
        // The decoder's own words stay with it (for the log).
        Assert.Equal(code, e.DecoderError.Status);
        Assert.Equal(line, e.DecoderError.Detail.Trim());
    }

    [Fact]
    public void Info_OtherExitCodes_StayAsTheyAre()
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = FailingDecoder(dir.Path, 2, "usage: braw-decode", failInfo: true), NoSearch = true };
        var e = Assert.Throws<ToolError>(() => new BrawBackend(_ => Library).Probe(Path.Combine(dir.Path, "A001.braw"), tools, dir.Path));
        Assert.Equal("braw-decode failed (exit 2): usage: braw-decode", e.Message);
    }

    /// The library vanished after the check: the note names the DLL by its file name.
    [Fact]
    public void Info_NoteWithoutLibraryPath_NamesTheDll()
    {
        using var dir = new TempDir();
        var tools = new ExternalTools { BrawDecoder = FailingDecoder(dir.Path, 3, "braw-decode: BlackmagicRawAPI.dll not found", failInfo: true), NoSearch = true };
        var e = Assert.Throws<RawDecoderException>(() => new BrawBackend(_ => null).Probe(Path.Combine(dir.Path, "A001.braw"), tools, dir.Path));
        Assert.Equal("Blackmagic RAW: cannot use the library BlackmagicRawAPI.dll — install the current Blackmagic RAW Player", e.Message);
    }

    static ProxySettings FakeSettings => new() { Enabled = true, Height = 180, Codec = ProxyCodec.ProresProxy, BurnFilename = false, BurnTimecode = false, Audio = false };
    static Clip BrawClip => new() { Name = "A001", Path = "A001.braw", Format = ClipFormat.Braw, Segments = ["A001.braw"], Bytes = 1 };
    static ClipInfo OneFrame => new() { Codec = "Blackmagic RAW", Width = 320, Height = 180, FrameRate = "25", Frames = 1, Duration = 0.04 };

    /// The decoder fails while ffmpeg reads its frames (a thumbnail): the same note, the decoder's line in the log.
    [Fact]
    public void Feeder_DecoderExitCode_BecomesCzechNote_OriginalLogged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var consumer = Path.Combine(dir.Path, "ffmpeg.cmd");   // reads its input to the end
        File.WriteAllText(consumer, "@more >nul\r\n@exit /b 0\r\n");
        const string line = @"braw-decode: cannot open D:\card\A001.braw (0x80004005; library C:\BM\BlackmagicRawAPI.dll)";
        var tools = new ExternalTools { Ffmpeg = consumer, BrawDecoder = FailingDecoder(dir.Path, 4, line, failInfo: false), NoSearch = true }.Resolved();
        var log = new List<string>();
        using var r = new ProxyRenderer(FakeSettings, tools) { Backends = [new BrawBackend(_ => Library)], Log = l => { lock (log) log.Add(l); } };
        var e = Assert.Throws<RawDecoderException>(() => r.Thumbnails(BrawClip, card, OneFrame, null));
        Assert.StartsWith("Blackmagic RAW: cannot open the clip — it is damaged, or the library C:\\BM\\BlackmagicRawAPI.dll", e.Message);
        lock (log) Assert.Contains($"braw-decode failed (exit 4): {line}", log);
    }

    /// `--info` fails while the clip is probed: the note at the clip, the decoder's line in the log.
    [Fact]
    public void Probe_DecoderExitCode_OriginalLogged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        const string line = @"braw-decode: cannot open D:\card\A001.braw (0x80004005; library C:\BM\BlackmagicRawAPI.dll)";
        var tools = new ExternalTools { BrawDecoder = FailingDecoder(dir.Path, 4, line, failInfo: true), NoSearch = true }.Resolved();
        var log = new List<string>();
        using var r = new ProxyRenderer(FakeSettings, tools) { Backends = [new BrawBackend(_ => Library)], Log = l => { lock (log) log.Add(l); } };
        var e = Assert.Throws<RawDecoderException>(() => r.Probe(BrawClip, card));
        Assert.StartsWith("Blackmagic RAW: cannot open the clip", e.Message);
        lock (log) Assert.Contains($"braw-decode failed (exit 4): {line}", log);
    }
}
