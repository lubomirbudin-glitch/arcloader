using System.Text.Json;
using Offload.Cli;

namespace Offload.Core.Tests;

/// Live test of the Mac version (2026-10-08): hardware DEcoding of H.264/HEVC returned green frames without any error.
/// Proxies decode in software by default (`proxy.hardwareDecode`, off); hardware ENcoding (NVENC/QSV/AMF) stays.
[Collection("Console")]
public class HardwareDecodeTests
{
    static Clip VideoClip(string rel) => new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 1 };

    /// Logs every argument it gets (one per line) and writes its last argument (the output).
    static ExternalTools ArgLogger(string dir)
    {
        var path = Path.Combine(dir, "ffmpeg.cmd");
        File.WriteAllText(path, """
            @echo off
            setlocal
            set "LOG=%~dp0args.txt"
            set LAST=
            :next
            if "%~1"=="" goto run
            >>"%LOG%" echo(%~1
            set "LAST=%~1"
            shift
            goto next
            :run
            echo x>"%LAST%"
            exit /b 0

            """.Replace("\n", "\r\n"));
        return new ExternalTools { Ffmpeg = path, NoSearch = true }.Resolved();
    }

    static ProxySettings Settings(bool hw) => new()
    {
        Enabled = true, Height = 180, Codec = ProxyCodec.ProresProxy, BurnFilename = false, BurnTimecode = false, Audio = false, HardwareDecode = hw,
    };

    static string[] RenderArgs(bool hw, string codec)
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var r = new ProxyRenderer(Settings(hw), ArgLogger(dir.Path));
        r.Render(VideoClip("a.mov"), card, new ClipInfo { Codec = codec, Width = 320, Height = 180, Duration = 1, FrameRate = "25/1" },
            null, Path.Combine(card, "_PROXY", "a.mov"), _ => { }, () => false);
        return File.ReadAllLines(Path.Combine(dir.Path, "args.txt"));
    }

    [Theory]
    [InlineData("h264")]
    [InlineData("hevc")]
    public void Default_H264AndHevc_DecodeInSoftware(string codec) =>
        Assert.DoesNotContain("-hwaccel", RenderArgs(hw: false, codec));

    [Theory]
    [InlineData("h264")]
    [InlineData("hevc")]
    public void Switched_On_H264AndHevc_DecodeInHardware(string codec)
    {
        var args = RenderArgs(hw: true, codec);
        int at = Array.IndexOf(args, "-hwaccel");
        Assert.True(at >= 0);
        Assert.Equal("auto", args[at + 1]);
    }

    // The old rule stays: only H.264/HEVC — FFV1 (hung with -hwaccel auto), ProRes … never, even when switched on.
    [Theory]
    [InlineData("ffv1")]
    [InlineData("prores")]
    public void Switched_On_OtherCodecs_StillSoftware(string codec) =>
        Assert.DoesNotContain("-hwaccel", RenderArgs(hw: true, codec));

    [Fact]
    public void Setting_OffByDefault_AbsentWhileOff_SurvivesSave()
    {
        Assert.False(new ProxySettings().HardwareDecode);
        Assert.DoesNotContain("hardwareDecode", JsonSerializer.Serialize(new ProxySettings()));
        Assert.Contains("\"hardwareDecode\":true", JsonSerializer.Serialize(new ProxySettings { HardwareDecode = true }));
        Assert.True(JsonSerializer.Deserialize<ProxySettings>("""{"hardwareDecode":true}""")!.HardwareDecode);

        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var s = new Offload.Core.Desktop.AppSettings();
        s.Proxy.HardwareDecode = true;
        Offload.Core.Desktop.SettingsStore.Save(s, path);
        Assert.True(Offload.Core.Desktop.SettingsStore.Load(path).Proxy.HardwareDecode);
    }

    [Fact]
    public void Cli_HwDecode()
    {
        Assert.False(CopyConfig().Proxy.HardwareDecode);
        Assert.True(CopyConfig("--hw-decode").Proxy.HardwareDecode);
    }

    static JobConfig CopyConfig(params string[] extra)
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "A001.mov"), 100, 1);
        JobConfig? seen = null;
        var old = CopyCommand.JobFactory;
        var (o, e) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        CopyCommand.JobFactory = c => new OffloadJob(seen = c, AppendFixtures.Options());
        try { Assert.Equal(0, CopyCommand.Create().Parse([card, "-d", dir.Sub("d1"), "--dry-run", "--no-history", .. extra]).Invoke()); }
        finally
        {
            CopyCommand.JobFactory = old;
            Console.SetOut(o);
            Console.SetError(e);
        }
        return seen!;
    }
}
