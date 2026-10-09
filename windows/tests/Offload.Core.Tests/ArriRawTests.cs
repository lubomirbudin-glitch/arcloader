namespace Offload.Core.Tests;

/// ARRIRAW findings from the first live use of the Mac version (2026-10-08/09), with a fake art-cmd that logs its
/// arguments (art-cmd itself is not installed on this PC — the real tool is a manual test).
public class ArriRawTests
{
    static Clip ArriClip(string rel = "A001C001.mxf") =>
        new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Arriraw, Segments = [rel], Bytes = 1 };

    static ProxySettings Settings => new() { Enabled = true, Height = 180, Codec = ProxyCodec.ProresProxy, BurnFilename = false, BurnTimecode = false, Audio = false };

    /// A stand-in for art-cmd: logs each argument on its own line (a "call" line first) into calls.txt beside it, then
    /// `process` writes its --output, `export` copies meta.json (beside it) to its --output.
    internal static string FakeArtCmd(string dir, string process = "Write", string export = "Meta")
    {
        var path = Path.Combine(dir, "art-cmd.cmd");
        File.WriteAllText(path, $$"""
            @echo off
            setlocal
            set "LOG=%~dp0calls.txt"
            set "HERE=%~dp0"
            set OUT=
            set CMD=%~1
            set DUR=
            set TCS=
            set LOOK=
            echo call>>"%LOG%"
            :next
            if "%~1"=="" goto run
            >>"%LOG%" echo(%~1
            if "%~1"=="--output" set "OUT=%~2"
            if "%~1"=="--duration" set DUR=1
            if "%~1"=="--target-colorspace" set TCS=1
            if "%~1"=="--embedded-look" set LOOK=1
            shift
            goto next
            :run
            if "%CMD%"=="export" goto {{export}}
            goto {{process}}
            :Write
            echo intermediate>"%OUT%"
            exit /b 0
            :Meta
            copy /y "%HERE%meta.json" "%OUT%" >nul
            exit /b 0
            :Nothing
            echo [2026-10-05 10:00:00.000] [art] [info] processing
            exit /b 0
            :Fail
            echo [2026-10-05 10:00:00.000] [art] [error] clip cannot be read
            exit /b 3
            :RejectDuration
            if defined DUR exit /b 2
            goto Meta
            :Real
            if defined TCS if defined LOOK (
              echo [2026-10-05 10:00:00.000] [art] [error] target-colorspace argument is only valid for embedded looks with DRT LUTs
              exit /b 0
            )
            echo intermediate>"%OUT%"
            exit /b 0
            :Grow
            type nul>"%OUT%"
            for /l %%i in (1,1,4) do (
              type "%HERE%chunk.bin">>"%OUT%"
              ping -n 2 127.0.0.1 >nul
            )
            exit /b 0

            """.Replace("\n", "\r\n"));
        return path;
    }

    /// A stand-in for ffmpeg: logs its arguments into ffmpeg.txt, reports a finished second, writes its last argument.
    internal static string FakeFfmpeg(string dir)
    {
        var path = Path.Combine(dir, "ffmpeg.cmd");
        File.WriteAllText(path, """
            @echo off
            setlocal
            set "LOG=%~dp0ffmpeg.txt"
            set LAST=
            :next
            if "%~1"=="" goto run
            >>"%LOG%" echo(%~1
            set "LAST=%~1"
            shift
            goto next
            :run
            echo out_time_us=1000000
            echo progress=end
            echo x>"%LAST%"
            exit /b 0

            """.Replace("\n", "\r\n"));
        return path;
    }

    static ExternalTools Tools(string dir, string process = "Write", string export = "Meta") =>
        new ExternalTools { ArtCmd = FakeArtCmd(dir, process, export), Ffmpeg = FakeFfmpeg(dir), NoSearch = true }.Resolved();

    /// Each art-cmd call's arguments.
    static List<List<string>> Calls(string dir)
    {
        var calls = new List<List<string>>();
        foreach (var line in File.ReadAllLines(Path.Combine(dir, "calls.txt")))
        {
            if (line == "call") calls.Add([]);
            else calls[^1].Add(line);
        }
        return calls;
    }

    static void Render(ProxyRenderer r, string card, ClipInfo info, Action<double>? progress = null, LutUse? lut = null) =>
        r.Render(ArriClip(), card, info, lut, Path.Combine(card, "_PROXY", "A001C001.mov"), progress ?? (_ => { }), () => false);

    // --- 2. --desqueeze only for anamorphic clips (it halves art-cmd's speed and does nothing for spherical lenses) ---

    [Fact]
    public void Desqueeze_OnlyForAnamorphicClips()
    {
        var (spherical, _, _) = ArriBackend.Args("in", "out", new ClipInfo { Width = 4608, Height = 3164 }, Settings, new RawLook(false, null), "p.json");
        Assert.DoesNotContain("--desqueeze", spherical);
        Assert.Contains("--output-height", spherical);
        var (unknown, _, _) = ArriBackend.Args("in", "out", new ClipInfo(), Settings, new RawLook(false, null), "p.json");
        Assert.DoesNotContain("--desqueeze", unknown);
        var (anamorphic, _, _) = ArriBackend.Args("in", "out", new ClipInfo { Width = 4608, Height = 3164, Squeeze = 2.0 }, Settings, new RawLook(false, null), "p.json");
        Assert.Contains("--desqueeze", anamorphic);
        var (hawk, _, _) = ArriBackend.Args("in", "out", new ClipInfo { Squeeze = 1.3 }, Settings, new RawLook(false, null), "p.json");
        Assert.Contains("--desqueeze", hawk);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(2.0, true)]
    public void Desqueeze_WhatArtCmdIsGiven(double? squeeze, bool expected)
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(Settings, Tools(dir.Path));
        Render(r, card, new ClipInfo { Codec = "ARRIRAW", Width = 4608, Height = 3164, FrameRate = "24", Frames = 24, Duration = 1, Squeeze = squeeze });
        var process = Assert.Single(Calls(dir.Path));
        Assert.Equal("process", process[0]);
        Assert.Equal(expected, process.Contains("--desqueeze"));
    }
    // --- 6. No LUT: --embedded-look alone; exit code 0 without an output file is an error with a Czech note ---

    [Fact]
    public void NoLut_EmbeddedLookAlone_TheRealRefusalCannotHappen()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(Settings, Tools(dir.Path, process: "Real"));
        Render(r, card, new ClipInfo { Codec = "ARRIRAW", Width = 4608, Height = 3164, FrameRate = "24", Frames = 24, Duration = 1, Camera = "ALEXA 35" });
        Assert.True(File.Exists(Path.Combine(card, "_PROXY", "A001C001.mov")));
        var process = Assert.Single(Calls(dir.Path));
        Assert.Equal("--embedded-look", process[^1]);
        Assert.DoesNotContain("--target-colorspace", process);
    }

    [Fact]
    public void WithLut_TargetColorspaceWithoutEmbeddedLook()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var cube = Path.Combine(dir.Path, "look.cube");
        File.WriteAllText(cube, "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        using var r = new ProxyRenderer(Settings, Tools(dir.Path, process: "Real"));
        Render(r, card, new ClipInfo { Codec = "ARRIRAW", Width = 4608, Height = 3164, FrameRate = "24", Frames = 24, Duration = 1, Camera = "ALEXA 35" },
            lut: new LutUse("custom", null, "look.cube", "vlastní LUT") { File = cube });
        var process = Assert.Single(Calls(dir.Path));
        Assert.Contains("--target-colorspace", process);
        Assert.DoesNotContain("--embedded-look", process);
    }

    [Fact]
    public void ExitZeroWithoutOutput_CzechNote_ToolWordsInTheLog_NoLeftovers()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var log = new List<string>();
        using var r = new ProxyRenderer(Settings, Tools(dir.Path, process: "Nothing")) { Log = l => { lock (log) log.Add(l); } };
        var e = Assert.ThrowsAny<Exception>(() => Render(r, card, new ClipInfo { Codec = "ARRIRAW", Width = 4608, Height = 3164, FrameRate = "24", Frames = 24, Duration = 1 }));
        Assert.Equal("ARRIRAW: art-cmd skončil bez chyby, ale nic nezapsal — proxy nevznikla ([info] processing)", e.Message);
        lock (log) Assert.Contains("art-cmd failed (exit 0): no output file: [info] processing", log);
        Assert.False(File.Exists(Path.Combine(card, "_PROXY", "A001C001.mov")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "ffmpeg.txt")));   // ffmpeg never ran
    }

    [Fact]
    public void NonZeroExit_QuotesTheErrorLine()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(Settings, Tools(dir.Path, process: "Fail"));
        var e = Assert.Throws<ToolError>(() => Render(r, card, new ClipInfo { Codec = "ARRIRAW", Width = 4608, Height = 3164, FrameRate = "24", Frames = 24, Duration = 1 }));
        Assert.Equal("art-cmd failed (exit 3): [error] clip cannot be read", e.Message);
    }

    [Fact]
    public void Redline_ExitZeroWithoutOutput_CzechNote()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var redline = Path.Combine(dir.Path, "REDline.cmd");
        File.WriteAllText(redline, "@echo off\r\necho Loading clip\r\necho Done\r\nexit /b 0\r\n");
        var tools = new ExternalTools { Redline = redline, Ffmpeg = FakeFfmpeg(dir.Path), NoSearch = true }.Resolved();
        using var r = new ProxyRenderer(Settings, tools);
        var clip = new Clip { Name = "A001", Path = "A001.R3D", Format = ClipFormat.R3d, Segments = ["A001.R3D"], Bytes = 1 };
        var e = Assert.ThrowsAny<Exception>(() => r.Render(clip, card, new ClipInfo { Height = 2160, Duration = 1, FrameRate = "24" }, null,
            Path.Combine(card, "_PROXY", "A001.mov"), _ => { }, () => false));
        Assert.Equal("RED R3D: REDline skončil bez chyby, ale nic nezapsal — proxy nevznikla (Loading clip | Done)", e.Message);
    }

    [Fact]
    public void LogTail_ErrorLinesWithoutPrefix_ElseLastThree()
    {
        Assert.Equal("[error] target-colorspace argument is only valid for embedded looks with DRT LUTs",
            RawBackends.LogTail("[2026-10-05 01:00:00.000] [art] [info] start\n[2026-10-05 01:00:00.001] [art] [error] target-colorspace argument is only valid for embedded looks with DRT LUTs\n[2026-10-05 01:00:00.002] [art] [info] done\n"));
        Assert.Equal("[critical] boom | [error] two", RawBackends.LogTail("[t] [a] [critical] boom\r\n[t] [a] [error] two\r\n"));
        Assert.Equal("b | c | d", RawBackends.LogTail("a\nb\n\nc\nd\n"));
        Assert.Equal("", RawBackends.LogTail(""));
    }

    // --- 4. Progress while art-cmd writes the intermediate: 0–80 % from its size, ffmpeg the rest ---

    [Fact]
    public void Progress_FromTheGrowingIntermediate_ThenFfmpeg()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        File.WriteAllBytes(Path.Combine(dir.Path, "chunk.bin"), new byte[25_000]);
        // 10 frames at 180 × 324 (1.8:1) × 0.21 bytes ≈ 122 kB expected; the fake writes 4 × 25 kB, a second apart.
        var info = new ClipInfo { Codec = "ARRIRAW", Width = 1800, Height = 1000, FrameRate = "10", Frames = 10, Duration = 1 };
        Assert.Equal(10 * 180 * 324 * 0.21, IntermediateProgress.ExpectedBytes(info, Settings)!.Value, 3);
        var seen = new List<double>();
        using var r = new ProxyRenderer(Settings, Tools(dir.Path, process: "Grow")) { IntermediatePoll = TimeSpan.FromMilliseconds(100) };
        Render(r, card, info, f => { lock (seen) seen.Add(f); });

        lock (seen)
        {
            var during = seen.Where(f => f > 0 && f < IntermediateProgress.Share).ToList();
            Assert.True(during.Count >= 2, $"progress during art-cmd: {string.Join(", ", seen)}");
            Assert.All(during, f => Assert.True(f <= 0.95 * IntermediateProgress.Share));
            for (int i = 1; i < seen.Count; i++) Assert.True(seen[i] >= seen[i - 1], $"not rising: {string.Join(", ", seen)}");
            Assert.Equal(1.0, seen[^1], 6);   // ffmpeg's end is the clip's end
        }
    }

    [Fact]
    public void ExpectedBytes_DesqueezedOrFrameLines_UnknownWithoutFrames()
    {
        var s = new ProxySettings { Height = 1080 };
        Assert.Equal(24 * 1080 * (1080 * 4608 * 2.0 / 3164) * 0.21, IntermediateProgress.ExpectedBytes(new ClipInfo { Width = 4608, Height = 3164, Frames = 24, Squeeze = 2 }, s)!.Value, 3);
        Assert.Equal(48 * 1080 * (1080 * 1.66) * 0.21, IntermediateProgress.ExpectedBytes(new ClipInfo { Width = 4608, Height = 3164, Duration = 2, FrameRate = "24" }, new ProxySettings { Height = 1080, FrameAspect = 1.66 })!.Value, 3);
        Assert.Null(IntermediateProgress.ExpectedBytes(new ClipInfo { Width = 4608, Height = 3164 }, s));
        Assert.Null(IntermediateProgress.ExpectedBytes(new ClipInfo { Frames = 24 }, s));
    }

    // --- 5. Analysis: art-cmd exports one frame's metadata, ffprobe has a time limit, each tool looks once ---

    const string OneFrameMeta = """
        {"clipBasedMetadata":{"cameraModel":"ALEXA 35","videoCodec":"ARRIRAW","storedSize":{"width":4608,"height":3164},
          "clipDuration":1,"sceneColorEncodingTransferCurve":"LogC4","lensSqueeze":2000,
          "nativePictureEssenceDescriptor":{"file":{"sampleRate":"24/1"}}},
         "frameBasedMetadata":{"frames":[{"timecode":"10:00:00:00"}]}}
        """;

    /// A stand-in for ffprobe: logs its call into ffprobe.txt, then prints probe.json beside it, or hangs.
    static string FakeFfprobe(string dir, bool hang = false)
    {
        File.WriteAllText(Path.Combine(dir, "probe.json"), """
            {"streams":[{"index":0,"codec_type":"video","codec_name":"unknown","r_frame_rate":"24/1","avg_frame_rate":"24/1","duration":"10.000000"}],
             "format":{"format_name":"mxf","duration":"10.000000"}}
            """);
        var path = Path.Combine(dir, "ffprobe.cmd");
        var body = hang ? "ping -n 60 127.0.0.1 >nul" : "type \"%~dp0probe.json\"";
        File.WriteAllText(path, $"""
            @echo off
            echo call>>"%~dp0ffprobe.txt"
            {body}
            exit /b 0

            """.Replace("\n", "\r\n"));
        return path;
    }

    static (string Card, ExternalTools Tools) AnalysisSetup(TempDir dir, string meta, string export = "Meta", bool ffprobeHangs = false, bool artCmd = true)
    {
        var card = dir.Sub("card");
        File.WriteAllText(Path.Combine(card, "A001C001.mxf"), "not really ARRIRAW");
        File.WriteAllText(Path.Combine(dir.Path, "meta.json"), meta);
        var tools = new ExternalTools
        {
            ArtCmd = artCmd ? FakeArtCmd(dir.Path, export: export) : null,
            Ffmpeg = FakeFfmpeg(dir.Path),
            Ffprobe = FakeFfprobe(dir.Path, ffprobeHangs),
            NoSearch = true,
        }.Resolved();
        return (card, tools);
    }

    static Clip Mxf => new() { Name = "A001C001", Path = "A001C001.mxf", Format = ClipFormat.Video, Segments = ["A001C001.mxf"], Bytes = 1 };

    static int CallCount(string dir, string file) => File.Exists(Path.Combine(dir, file)) ? File.ReadAllLines(Path.Combine(dir, file)).Count(l => l == "call") : 0;

    [Fact]
    public void Analysis_OneFrameExport_FramesFromFfprobe_EachToolOnce()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta);   // worst case: the one-frame export says clipDuration 1
        using var r = new ProxyRenderer(Settings, tools);

        var (clip, info) = r.Analyze(Mxf, card);

        Assert.Equal(ClipFormat.Arriraw, clip.Format);
        Assert.Equal((4608, 3164), (info.Width, info.Height));
        Assert.Equal("24", info.FrameRate);
        Assert.Equal(240, info.Frames);
        Assert.Equal(10.0, info.Duration);
        Assert.Equal("10:00:00:00", info.Timecode);
        Assert.Equal("ALEXA 35", info.Camera);
        Assert.Equal("LogC4", info.LogEncoding);
        Assert.Equal(2.0, info.Squeeze);
        var export = Assert.Single(Calls(dir.Path));
        Assert.Equal("export", export[0]);
        int at = export.IndexOf("--duration");
        Assert.True(at > 0, string.Join(" ", export));
        Assert.Equal("1", export[at + 1]);
        Assert.Equal(1, CallCount(dir.Path, "ffprobe.txt"));
    }

    [Fact]
    public void Analysis_ClipDurationOfTheWholeClip_Kept()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta.Replace("\"clipDuration\":1", "\"clipDuration\":241"));
        using var r = new ProxyRenderer(Settings, tools);
        var (_, info) = r.Analyze(Mxf, card);
        Assert.Equal(241, info.Frames);
    }

    /// The frames the scanner found (its segments), not whatever *.ari lies in the folder (a Mac's "._" copies match too).
    [Fact]
    public void Analysis_AriSequence_CountsItsSegments()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta);
        var folder = Path.Combine(card, "A001C002");
        Directory.CreateDirectory(folder);
        for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(folder, $"A001C002.{i:0000000}.ari"), "x");
        File.WriteAllText(Path.Combine(folder, "._A001C002.0000000.ari"), "AppleDouble");
        using var r = new ProxyRenderer(Settings, tools);
        var seq = new Clip
        {
            Name = "A001C002", Path = "A001C002", Format = ClipFormat.Arriraw, Bytes = 5,
            Segments = [.. Enumerable.Range(0, 5).Select(i => $"A001C002/A001C002.{i:0000000}.ari")],
        };

        var (clip, info) = r.Analyze(seq, card);

        Assert.Same(seq, clip);
        Assert.Equal(5, info.Frames);
        Assert.Equal(0, CallCount(dir.Path, "ffprobe.txt"));
        Assert.Contains("--duration", Assert.Single(Calls(dir.Path)));
    }

    [Fact]
    public void Analysis_DurationRefused_FullExport_Logged()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta.Replace("\"clipDuration\":1", "\"clipDuration\":240"), export: "RejectDuration");
        var log = new List<string>();
        using var r = new ProxyRenderer(Settings, tools) { Log = l => { lock (log) log.Add(l); } };

        var (clip, info) = r.Analyze(Mxf, card);

        Assert.Equal(ClipFormat.Arriraw, clip.Format);
        Assert.Equal(240, info.Frames);
        var calls = Calls(dir.Path);
        Assert.Equal(2, calls.Count);
        Assert.Contains("--duration", calls[0]);
        Assert.DoesNotContain("--duration", calls[1]);
        lock (log) Assert.Contains(log, l => l.Contains("art-cmd export --duration 1") && l.Contains("every frame"));
    }

    /// The case the time limit is for, with the worst answer a one-frame export can give (clipDuration 1): ffprobe gave
    /// nothing to count with, so the length is unknown — never 1 frame (00:00:00:01, a bar stuck at 76 %).
    [Fact]
    public void Analysis_FfprobeHangsOnMxf_TimeLimit_ThenTheArriTool_LengthUnknownNotOneFrame()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta, ffprobeHangs: true);
        var log = new List<string>();
        using var r = new ProxyRenderer(Settings, tools) { MxfProbeTimeout = TimeSpan.FromSeconds(1), Log = l => { lock (log) log.Add(l); } };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var (clip, info) = r.Analyze(Mxf, card);

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"took {sw.Elapsed}");
        Assert.Equal(ClipFormat.Arriraw, clip.Format);
        Assert.Equal((4608, 3164), (info.Width, info.Height));
        Assert.Null(info.Frames);
        Assert.Null(info.Duration);
        Assert.Null(IntermediateProgress.ExpectedBytes(info, Settings));   // no estimate from a wrong size
        lock (log) Assert.Contains(log, l => l.Contains("A001C001") && l.Contains("unknown"));
    }

    [Fact]
    public void Analysis_FfprobeHangsOnMxf_ClipDurationOfTheWholeClip_Kept()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta.Replace("\"clipDuration\":1", "\"clipDuration\":240"), ffprobeHangs: true);
        using var r = new ProxyRenderer(Settings, tools) { MxfProbeTimeout = TimeSpan.FromSeconds(1) };
        var (_, info) = r.Analyze(Mxf, card);
        Assert.Equal(240, info.Frames);
        Assert.Equal(10.0, info.Duration);
    }

    /// A clip of unknown length still gets its proxy; progress stays a number between 0 and 1.
    [Fact]
    public void Render_UnknownLength_ProxyMade_ProgressStaysInRange()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var seen = new List<double>();
        using var r = new ProxyRenderer(Settings, Tools(dir.Path)) { IntermediatePoll = TimeSpan.FromMilliseconds(50) };
        Render(r, card, new ClipInfo { Codec = "ARRIRAW", Width = 4608, Height = 3164, FrameRate = "24" }, f => { lock (seen) seen.Add(f); });
        Assert.True(File.Exists(Path.Combine(card, "_PROXY", "A001C001.mov")));
        lock (seen) Assert.All(seen, f => Assert.True(double.IsFinite(f) && f >= 0 && f <= 1, string.Join(", ", seen)));
    }

    [Fact]
    public void Analysis_FfprobeHangsOnMxf_WithoutArtCmd_TimeoutIsTheError()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta, ffprobeHangs: true, artCmd: false);
        using var r = new ProxyRenderer(Settings, tools) { MxfProbeTimeout = TimeSpan.FromSeconds(1) };
        var e = Assert.Throws<ToolError>(() => r.Analyze(Mxf, card));
        Assert.Contains("neodpověděl do 1 s", e.Message);
    }

    [Fact]
    public void Analysis_DecodableMxf_FfprobeOnly()
    {
        using var dir = new TempDir();
        var (card, tools) = AnalysisSetup(dir, OneFrameMeta);
        File.WriteAllText(Path.Combine(dir.Path, "probe.json"), """
            {"streams":[{"codec_type":"video","codec_name":"prores","width":3840,"height":2160,"r_frame_rate":"24/1","avg_frame_rate":"24/1","nb_frames":"240","duration":"10.0"}],"format":{"duration":"10.0"}}
            """);
        using var r = new ProxyRenderer(Settings, tools);
        var (clip, info) = r.Analyze(Mxf, card);
        Assert.Equal(ClipFormat.Video, clip.Format);
        Assert.Equal(3840, info.Width);
        Assert.False(File.Exists(Path.Combine(dir.Path, "calls.txt")));   // art-cmd never asked
    }
}
