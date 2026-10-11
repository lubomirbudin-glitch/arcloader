namespace Offload.Core.Tests;

public class ToolsTests
{
    const string Cmd = @"C:\Windows\System32\cmd.exe";

    // Final review D1
    [Fact]
    public void SearchDirs_ToolsBinWalkOnlyForDevelopmentBuilds()
    {
        Assert.True(ExternalTools.IsDevBuild(@"D:\offload\windows\src\Offload.Cli\bin\Debug\net10.0-windows\"));
        Assert.True(ExternalTools.IsDevBuild(@"D:\offload\windows\src\Offload.Cli\BIN\release\net10.0-windows\win-x64\"));
        Assert.False(ExternalTools.IsDevBuild(@"C:\Program Files\Offload\bin\"));
        Assert.False(ExternalTools.IsDevBuild(@"D:\offload\windows\build\Offload\bin\"));
        // The tests themselves run from bin\Debug, so they still find windows\tools\bin.
        Assert.Contains(ExternalTools.SearchDirs, d => d.EndsWith(@"\tools\bin", StringComparison.OrdinalIgnoreCase));
    }

    // The renames (0.10.0, 0.11.0): %LOCALAPPDATA%\mmMover\bin first, then the folders of earlier versions, newest first.
    [Fact]
    public void SearchDirs_LocalAppData_NewNameFirst_ThenArcLoader_ThenOffload()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dirs = ExternalTools.SearchDirs.ToList();
        int At(string name) => dirs.FindIndex(d => string.Equals(d, Path.Combine(local, name, "bin"), StringComparison.OrdinalIgnoreCase));
        Assert.True(At("mmMover") >= 0);
        Assert.True(At("mmMover") < At("ArcLoader"));
        Assert.True(At("ArcLoader") < At("Offload"));
    }

    [Fact]
    public void Run_ReturnsStdout() => Assert.Equal("hello\n", ToolProcess.Run(Cmd, ["/c", "echo hello"]));

    [Fact]
    public void Run_NonZeroExit_ThrowsWithStderrTail()
    {
        var e = Assert.Throws<ToolError>(() => ToolProcess.Run(Cmd, ["/c", "echo first 1>&2 & echo boom 1>&2 & exit 3"]));
        Assert.Equal(3, e.Status);
        Assert.Equal("cmd", e.Tool);
        Assert.StartsWith("cmd failed (exit 3): ", e.Message);
        Assert.Contains("boom", e.Message);
    }

    [Fact]
    public void Run_MissingExe_ThrowsToolError()
    {
        var e = Assert.Throws<ToolError>(() => ToolProcess.Run(@"C:\nope\missing.exe", []));
        Assert.Equal(-1, e.Status);
    }

    [Fact]
    public void Run_UsesWorkingDirectory()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.Path + "\n", ToolProcess.Run(Cmd, ["/c", "cd"], dir.Path));
    }

    [Fact]
    public void Start_StreamsLines_AndKillStopsTheProcess()
    {
        var lines = new List<string>();
        // Event, not a sleep loop: under a loaded full suite the two lines took longer than the old 5 s.
        using var twoLines = new ManualResetEventSlim();
        using var p = new ToolProcess(Cmd, ["/c", "echo one & echo two & ping -n 60 127.0.0.1 >nul"], null, pipeStdin: false);
        p.Start(l => { lock (lines) { lines.Add(l); if (lines.Count >= 2) twoLines.Set(); } });
        Assert.True(twoLines.Wait(TimeSpan.FromSeconds(30)), "the two lines did not arrive within 30 s");
        p.Kill();
        Assert.True(p.WaitForExit(5000));
        lock (lines) Assert.Equal(["one ", "two "], lines.Take(2));
    }

    [Fact]
    public void Find_SearchesDirsThenKnownPaths()
    {
        using var dir = new TempDir();
        var a = dir.Sub("a");
        var b = dir.Sub("b");
        var known = Path.Combine(dir.Sub("known"), "tool.exe");
        File.WriteAllText(known, "");
        Assert.Equal(known, ExternalTools.Find("tool.exe", [a, b], [known]));
        var inB = Path.Combine(b, "tool.exe");
        File.WriteAllText(inB, "");
        Assert.Equal(inB, ExternalTools.Find("tool.exe", [a, b], [known]));
        Assert.Null(ExternalTools.Find("other.exe", [a, b], []));
    }

    [Fact]
    public void Resolved_KeepsExistingExplicitPath_DropsMissingOne()
    {
        using var dir = new TempDir();
        var ff = Path.Combine(dir.Path, "my-ffmpeg.exe");
        File.WriteAllText(ff, "");
        var t = new ExternalTools { Ffmpeg = ff, Ffprobe = @"C:\nope\ffprobe.exe", NoSearch = true }.Resolved();
        Assert.Equal(ff, t.Ffmpeg);
        Assert.Null(t.Ffprobe);
        Assert.Null(t.Redline);
    }

    // The last stderr line written right before the exit must be in StderrTail once the stream is drained.
    [Fact]
    public void DrainStderr_LastLineIsThere_AfterExit()
    {
        for (int i = 0; i < 40; i++)
        {
            using var p = new ToolProcess(Cmd, ["/c", "ping -n 2 127.0.0.1 >nul & echo last line 1>&2 & exit /b 4"], null, pipeStdin: false);
            p.Start(null);
            Assert.True(p.WaitForExit(30000));
            p.DrainStderr();
            Assert.Contains("last line", p.StderrTail);
        }
    }
}
