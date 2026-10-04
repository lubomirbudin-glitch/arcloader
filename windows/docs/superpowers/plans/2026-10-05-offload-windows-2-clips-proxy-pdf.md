# Offload pro Windows — plán 2: analýza klipů, 3 snímky, proxy, RAW, PDF

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Po ověřené kopii (plán 1) aplikace najde klipy, zjistí jejich metadata, vloží do reportu tři snímky na klip (první, prostřední, poslední frame), volitelně vyrenderuje proxy (scale → LUT → burn-in → H.264/HEVC přes NVENC/QSV/AMF/software nebo ProRes) a zapíše PDF report — vše jen z cílových disků, nikdy z karty.

**Architecture:** Port `mac/Sources/OffloadKit/{Media,Proxy,RawBackends,Tools,Job}.swift`. Externí nástroje (ffmpeg, ffprobe, REDline, art-cmd, braw-decode) se hledají jako na Macu a spouštějí přes `ToolProcess`. Filter graph ffmpeg je čistá funkce, která odkazuje jen na relativní jména v pracovní složce rendereru (žádné `C:` ve filtrech). Parsování metadat a sestavování argumentů RAW nástrojů jsou čisté funkce testované bez nástrojů. PDF tiskne Edge v headless režimu. `OffloadJob` dostane stage 2 (`RunPost`).

**Tech Stack:** C# / .NET 10, ffmpeg 8.x (gyan.dev full build, na tomto PC na PATH), Microsoft Edge (headless), xUnit.

**Spec:** `windows/docs/superpowers/specs/2026-10-04-offload-windows-design.md` — oddíly 6 (vč. 6.5a a změny 2026-10-05), 7 (Edge místo WebView2), 9, 10.4–10.5. Mac reference: `mac/Sources/OffloadKit/*.swift`.

## Global Constraints

- Platforma a závislosti beze změny proti plánu 1: `net10.0-windows`, žádný nový NuGet balíček.
- Mac kód (`mac/`) se nemění (Mac část tří snímků je samostatná práce na Macu).
- Klipy, náhledy a proxy se čtou **jen z cílových disků** (první cíl s dobrou kopií všech segmentů), nikdy z karty.
- Selhání analýzy, náhledu, proxy nebo PDF **nikdy neshodí offload** a nemění `SafeToEject` z kopie.
- Filter graphy ffmpeg používají jen relativní jména souborů v pracovní složce rendereru (`look.cube`, `font.ttf`, `label-*.txt`); ffmpeg se spouští s `WorkingDirectory` = ta složka.
- Argumenty procesů vždy přes `ProcessStartInfo.ArgumentList` (žádné skládání příkazové řádky ze stringů).
- Testy, které potřebují ffmpeg nebo Edge, používají `[FfmpegFact]` / `[EdgeFact]` (přeskočí se, když nástroj chybí).
- Texty v reportu česky jako na Macu; hlášky nástrojů a CLI anglicky.
- Každý commit: jeden řádek nahoře v seznamu „plán 2“ v `docs/VYVOJ-WINDOWS.md` (bez SHA); poslední řádek zprávy přesně `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Příkazy `dotnet` ve složce `D:\My\Projekt\offload\windows`. `%TEMP%` je na D:.

## Review Focus

1. **Názvy klipů s mezerami, diakritikou a apostrofem** (`Záběr 'A' 01.mov`) → náhledy i proxy fungují, burn-in ukáže přesný název. Test: Task 6 `Render_NameWithQuotesAndDiacritics`.
2. **ffmpeg/ffprobe chybí** → offload projde, klipy jsou v reportu s poznámkou, proxy „nepodporováno“, žádný pád, `SafeToEject` zůstává. Test: Task 10 `NoTools_OffloadStillSafe`.
3. **Poškozený nebo useknutý video soubor na kartě** → u něj chyba analýzy, ostatní klipy pokračují. Test: Task 10 `CorruptClip_OthersContinue`.
4. **Zrušení během renderu proxy** → v `_PROXY` nezůstane `.part`, úloha skončí „Zrušeno“, kopie zůstává bezpečná k vysunutí. Test: Task 10 `CancelDuringProxies`.
5. **Velmi krátký klip (1 frame)** → náhled se udělá (aspoň první), nic nespadne. Test: Task 6 `Thumbnails_SingleFrameClip`.

---

## Struktura souborů

```
windows/src/Offload.Core/
  ExternalTools.cs     ExternalTools (hledání nástrojů), ToolError
  ToolProcess.cs       spuštění procesu, stdout po řádcích / binárně, konec stderr, kill stromu
  Media.cs             (úprava) Thumbnail, ClipEntry.Thumbnails, ProxyCodec.Wire/TryParseWire
  ClipDetector.cs      ClipDetector, FFProbe
  Timecode.cs          Timecode (platnost, fps z racionálu, posun o framy)
  Encoders.cs          volba HW enkodéru + argumenty kodeků
  FilterGraph.cs       filter graphy pro náhledy a proxy
  FrameSource.cs       odkud jdou snímky do ffmpeg (soubor / roura z dekodéru)
  RawBackends.cs       BRAW, R3D, ARRIRAW, Canon — parsování metadat, argumenty
  ProxyRenderer.cs     probe, 3 náhledy, render proxy, běh ffmpeg
  PdfReport.cs         PDF přes Edge headless
  HtmlReport.cs        (úprava) 3 náhledy s popisky, @page pro tisk
  OffloadJob.cs        (úprava) JobConfig.Tools, stage 2 RunPost, PDF
  Win32.cs             (úprava) StrCmpLogicalW
windows/src/Offload.Cli/
  CopyCommand.cs       (úprava) volby proxy, souhrn klipů, log
  ProgressDisplay.cs   (úprava) řádek pro analýzu a proxy
windows/scripts/build.ps1  (úprava) přibalit ffmpeg/ffprobe
windows/tests/Offload.Core.Tests/
  TestMedia.cs         FfmpegFact, EdgeFact, MakeClip, IdentityLut
  ToolsTests.cs, ClipDetectorTests.cs, TimecodeEncoderTests.cs, FilterGraphTests.cs,
  ProxyRendererTests.cs, RawBackendsTests.cs, PdfReportTests.cs, StageTwoTests.cs
```

---

### Task 1: Externí nástroje a spouštění procesů

**Files:**
- Create: `windows/src/Offload.Core/ExternalTools.cs`, `windows/src/Offload.Core/ToolProcess.cs`
- Test: `windows/tests/Offload.Core.Tests/ToolsTests.cs`

**Interfaces:**
- Produces:
  - `ToolError(string tool, int status, string message) : Exception` — `Tool`, `Status`, `Detail`; `Message` = `"{tool} failed (exit {status}): {poslední 3 neprázdné řádky spojené ' | '}"`.
  - `ExternalTools` — vlastnosti `Ffmpeg`, `Ffprobe`, `Redline`, `ArtCmd`, `BrawDecoder` (string?, JSON `ffmpeg`, `ffprobe`, `redline`, `artCmd`, `brawDecoder`); internal `NoSearch` (bool); `Resolved() -> ExternalTools`; internal static `Find(string exeName, IEnumerable<string> dirs, IEnumerable<string> knownPaths) -> string?`; static `SearchDirs`.
  - `ToolProcess : IDisposable` (internal) — ctor `(string exe, IEnumerable<string> args, string? workingDirectory, bool pipeStdin)`; `Start(Action<string>? onStdoutLine)`; `Stdout` (Stream, binární, jen když `onStdoutLine` je null); `Stdin` (Stream, jen s `pipeStdin`); `StderrTail`; `WaitForExit()`, `WaitForExit(int ms) -> bool`, `ExitCode`, `Kill()`, `Name`; static `Run(string exe, IReadOnlyList<string> args, string? cwd = null) -> string` (stdout; při nenulovém exit kódu nebo nespustitelném exe vyhodí `ToolError`).

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/ToolsTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class ToolsTests
{
    const string Cmd = @"C:\Windows\System32\cmd.exe";

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
        using var p = new ToolProcess(Cmd, ["/c", "echo one & echo two & ping -n 30 127.0.0.1 >nul"], null, pipeStdin: false);
        p.Start(l => { lock (lines) lines.Add(l); });
        for (int i = 0; i < 50 && lines.Count < 2; i++) Thread.Sleep(100);
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
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~ToolsTests"`
Expected: build FAIL — `ToolProcess`, `ToolError`, `ExternalTools` neexistují.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/ExternalTools.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Offload.Core;

public sealed class ToolError(string tool, int status, string message) : Exception(Describe(tool, status, message))
{
    public string Tool { get; } = tool;
    public int Status { get; } = status;
    public string Detail { get; } = message;

    static string Describe(string tool, int status, string message)
    {
        var last = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3);
        return $"{tool} failed (exit {status}): {string.Join(" | ", last)}";
    }
}

/// External tools the clip/proxy stage can use. Every one is optional: a missing tool only disables
/// the formats that need it, never the offload itself.
public sealed class ExternalTools
{
    [JsonPropertyName("ffmpeg")] public string? Ffmpeg { get; set; }
    [JsonPropertyName("ffprobe")] public string? Ffprobe { get; set; }
    [JsonPropertyName("redline")] public string? Redline { get; set; }
    [JsonPropertyName("artCmd")] public string? ArtCmd { get; set; }
    [JsonPropertyName("brawDecoder")] public string? BrawDecoder { get; set; }

    /// Tests only: use the explicit paths and nothing else.
    [JsonIgnore] internal bool NoSearch { get; set; }

    /// Folders searched in order: bin\ next to the app (GUI), the exe's own folder (CLI lives in bin\),
    /// the repo's windows\tools\bin for development builds, %LOCALAPPDATA%\Offload\bin, then PATH.
    public static IReadOnlyList<string> SearchDirs
    {
        get
        {
            var dirs = new List<string>();
            var exeDir = AppContext.BaseDirectory.TrimEnd('\\');
            dirs.Add(Path.Combine(exeDir, "bin"));
            dirs.Add(exeDir);
            var d = exeDir;
            for (int i = 0; i < 6 && Path.GetDirectoryName(d) is string parent; i++)
            {
                d = parent;
                dirs.Add(Path.Combine(d, "tools", "bin"));
            }
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Offload", "bin"));
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return dirs;
        }
    }

    static readonly Dictionary<string, string[]> KnownPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REDline.exe"] =
        [
            @"C:\Program Files\REDCINE-X Professional\REDline.exe",
            @"C:\Program Files\REDCINE-X PRO 64-bit\REDline.exe",
        ],
        ["art-cmd.exe"] =
        [
            @"C:\Program Files\ARRI\ARRI Reference Tool\art-cmd.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Offload", "arri", "art-cmd.exe"),
        ],
    };

    internal static string? Find(string exeName, IEnumerable<string> dirs, IEnumerable<string> knownPaths)
    {
        foreach (var dir in dirs)
        {
            var p = Path.Combine(dir, exeName);
            if (File.Exists(p)) return p;
        }
        return knownPaths.FirstOrDefault(File.Exists);
    }

    /// Fills every tool that is not set explicitly (or whose explicit path no longer exists).
    public ExternalTools Resolved()
    {
        string? Pick(string? explicitPath, string exeName)
        {
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath)) return explicitPath;
            if (NoSearch) return null;
            return Find(exeName, SearchDirs, KnownPaths.GetValueOrDefault(exeName, []));
        }
        return new ExternalTools
        {
            Ffmpeg = Pick(Ffmpeg, "ffmpeg.exe"),
            Ffprobe = Pick(Ffprobe, "ffprobe.exe"),
            Redline = Pick(Redline, "REDline.exe"),
            ArtCmd = Pick(ArtCmd, "art-cmd.exe"),
            BrawDecoder = Pick(BrawDecoder, "braw-decode.exe"),
            NoSearch = NoSearch,
        };
    }
}
```

`windows/src/Offload.Core/ToolProcess.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Offload.Core;

/// Runs a child process: stdout as lines (or as a binary stream), the last 4000 characters of stderr
/// kept for error messages. Dispose kills the whole process tree if it is still running.
internal sealed class ToolProcess : IDisposable
{
    readonly Process process;
    readonly bool pipeStdin;
    readonly StringBuilder stderr = new();
    readonly Lock gate = new();

    public string Name { get; }

    public ToolProcess(string exe, IEnumerable<string> args, string? workingDirectory, bool pipeStdin)
    {
        Name = Path.GetFileNameWithoutExtension(exe);
        this.pipeStdin = pipeStdin;
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
        foreach (var a in args) psi.ArgumentList.Add(a);
        process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (gate)
            {
                stderr.Append(e.Data).Append('\n');
                if (stderr.Length > 4000) stderr.Remove(0, stderr.Length - 4000);
            }
        };
    }

    public string StderrTail
    {
        get { lock (gate) return stderr.ToString(); }
    }

    /// Binary stdout — only when Start was called without a line handler.
    public Stream Stdout => process.StandardOutput.BaseStream;

    public Stream Stdin => process.StandardInput.BaseStream;

    public int ExitCode => process.ExitCode;

    public void Start(Action<string>? onStdoutLine)
    {
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new ToolError(Name, -1, e.Message);
        }
        process.BeginErrorReadLine();
        if (onStdoutLine != null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onStdoutLine(e.Data); };
            process.BeginOutputReadLine();
        }
        if (!pipeStdin) process.StandardInput.Close();
    }

    public void WaitForExit() => process.WaitForExit();

    public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);

    public void Kill()
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    public void Dispose()
    {
        Kill();
        process.Dispose();
    }

    /// Runs to completion and returns stdout.
    public static string Run(string exe, IReadOnlyList<string> args, string? cwd = null)
    {
        using var p = new ToolProcess(exe, args, cwd, pipeStdin: false);
        var output = new StringBuilder();
        p.Start(line => { lock (output) output.Append(line).Append('\n'); });
        p.WaitForExit();
        if (p.ExitCode != 0) throw new ToolError(p.Name, p.ExitCode, p.StderrTail);
        lock (output) return output.ToString();
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~ToolsTests"`
Expected: PASS (7 testů).

- [ ] **Step 5: Commit**

Řádek do `docs/VYVOJ-WINDOWS.md` (nová sekce nahoře `## 2026-10-05 — plán 2 (klipy, proxy, PDF), průběh` s prázdným seznamem, první položka): `- Úkol 1 — hledání externích nástrojů (ffmpeg, REDline, art-cmd, braw-decode) a spouštění procesů`.

```powershell
git add windows docs
git commit -m "Windows: external tools lookup and process runner" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Detekce klipů, ffprobe a testovací média

**Files:**
- Create: `windows/src/Offload.Core/ClipDetector.cs`, `windows/tests/Offload.Core.Tests/TestMedia.cs`
- Modify: `windows/src/Offload.Core/Win32.cs` (přidat `StrCmpLogicalW`)
- Test: `windows/tests/Offload.Core.Tests/ClipDetectorTests.cs`

**Interfaces:**
- Consumes: `SourceFile`, `Layout`, `Clip`, `ClipInfo`, `ClipFormat`, `ToolProcess.Run`, `ExternalTools`.
- Produces: `ClipDetector.Detect(IReadOnlyList<SourceFile>) -> List<Clip>`, internal `ClipDetector.IsCameraProxy(string rel)`; `FFProbe.Parse(string json) -> ClipInfo`, `FFProbe.Probe(string ffprobe, string path) -> ClipInfo`; `Win32.StrCmpLogicalW(string, string) -> int`. Testy: `[FfmpegFact]`, `[EdgeFact]`, `TestMedia.Ffmpeg`, `TestMedia.Ffprobe`, `TestMedia.MakeClip(string path, double seconds = 2, int fps = 25, string timecode = "01:00:00:00", bool audio = true, int width = 1280, int height = 720)`, `TestMedia.IdentityLut(string path)`.

- [ ] **Step 1: Testovací pomocníci**

`windows/tests/Offload.Core.Tests/TestMedia.cs`:

```csharp
namespace Offload.Core.Tests;

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (TestMedia.Ffmpeg == null || TestMedia.Ffprobe == null) Skip = "ffmpeg/ffprobe not found";
    }
}

public sealed class EdgeFactAttribute : FactAttribute
{
    public EdgeFactAttribute()
    {
        if (PdfReport.FindEdge() == null) Skip = "Microsoft Edge not found";
    }
}

public static class TestMedia
{
    static readonly ExternalTools Tools = new ExternalTools().Resolved();
    public static string? Ffmpeg => Tools.Ffmpeg;
    public static string? Ffprobe => Tools.Ffprobe;

    /// Test pattern with burnt-in TC track and optional sine audio, H.264 in MOV/MP4 (by extension).
    public static void MakeClip(string path, double seconds = 2, int fps = 25, string timecode = "01:00:00:00",
        bool audio = true, int width = 1280, int height = 720)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s={width}x{height}:r={fps}:d={d}" };
        if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=d={d}"]);
        args.AddRange(["-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "aac", "-shortest"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// 2×2×2 identity 3D LUT.
    public static void IdentityLut(string path) => File.WriteAllText(path,
        "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
}
```

(`PdfReport.FindEdge` vznikne v Task 9; do té doby `EdgeFactAttribute` zakomentovat a v Task 9 odkomentovat — nebo ho přidat až v Task 9. Zvolit to druhé: v tomto kroku `EdgeFactAttribute` vynechat.)

- [ ] **Step 2: Napsat padající testy**

`windows/tests/Offload.Core.Tests/ClipDetectorTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class ClipDetectorTests
{
    static SourceFile F(string rel, long size = 100) => new(rel, size, DateTime.UtcNow);

    [Fact]
    public void Detect_GroupsFormatsAndMarksCameraProxies()
    {
        var clips = ClipDetector.Detect(
        [
            F("_REPORT/x.mov"),
            F("PRIVATE/M4ROOT/CLIP/C0010.MP4"),
            F("PRIVATE/M4ROOT/CLIP/C0002.MP4"),
            F("PRIVATE/M4ROOT/SUB/C0002S03.MP4"),
            F("PRIVATE/M4ROOT/CLIP/C0002M01.XML"),
            F("empty.mov", 0),
            F("A001_C002_0101AB.RDM/A001_C002_0101AB_002.R3D", 200),
            F("A001_C002_0101AB.RDM/A001_C002_0101AB_001.R3D", 300),
            F("ARRI/A_0001C001/A_0001C001.0000001.ari", 10),
            F("ARRI/A_0001C001/A_0001C001.0000000.ari", 10),
            F("BRAW/A001_08151200_C001.braw"),
            F("CRM/A001C001.CRM"),
        ]);
        Assert.Equal(
        [
            ("A001_C002_0101AB", ClipFormat.R3d, "A001_C002_0101AB.RDM/A001_C002_0101AB_001.R3D"),
            ("A_0001C001", ClipFormat.Arriraw, "ARRI/A_0001C001"),
            ("A001_08151200_C001", ClipFormat.Braw, "BRAW/A001_08151200_C001.braw"),
            ("A001C001", ClipFormat.CanonRaw, "CRM/A001C001.CRM"),
            ("C0002", ClipFormat.Video, "PRIVATE/M4ROOT/CLIP/C0002.MP4"),
            ("C0010", ClipFormat.Video, "PRIVATE/M4ROOT/CLIP/C0010.MP4"),
            ("C0002S03", ClipFormat.Video, "PRIVATE/M4ROOT/SUB/C0002S03.MP4"),
        ], clips.Select(c => (c.Name, c.Format, c.Path)));
        var r3d = clips[0];
        Assert.Equal(["A001_C002_0101AB.RDM/A001_C002_0101AB_001.R3D", "A001_C002_0101AB.RDM/A001_C002_0101AB_002.R3D"], r3d.Segments);
        Assert.Equal(500, r3d.Bytes);
        Assert.Equal(2, clips[1].Segments.Count);
        Assert.True(clips[6].IsCameraProxy);
        Assert.False(clips[4].IsCameraProxy);
    }

    [Fact]
    public void Parse_ReadsVideoAudioTimecodeAndCamera()
    {
        const string json = """
            {"streams":[
              {"codec_type":"video","codec_name":"mjpeg","disposition":{"attached_pic":1},"width":320,"height":240},
              {"codec_type":"video","codec_name":"h264","profile":"High","width":1920,"height":1080,
               "avg_frame_rate":"25/1","r_frame_rate":"25/1","nb_frames":"100","duration":"4.000000",
               "disposition":{"attached_pic":0},"tags":{"timecode":"01:00:00:00"}},
              {"codec_type":"audio","channels":2},
              {"codec_type":"audio","channels":2}],
             "format":{"duration":"4.010000","tags":{"com.apple.quicktime.make":"Sony"}}}
            """;
        var i = FFProbe.Parse(json);
        Assert.Equal("h264 High", i.Codec);
        Assert.Equal(1920, i.Width);
        Assert.Equal(1080, i.Height);
        Assert.Equal("25/1", i.FrameRate);
        Assert.Equal(100, i.Frames);
        Assert.Equal(4.01, i.Duration);
        Assert.Equal("01:00:00:00", i.Timecode);
        Assert.Equal(4, i.AudioChannels);
        Assert.Equal("Sony", i.Camera);
    }

    [Fact]
    public void Parse_UndecodableCodecHasNoSize_TimecodeFromAnyStream()
    {
        const string json = """
            {"streams":[{"codec_type":"video","codec_name":"unknown","width":0,"height":0,"avg_frame_rate":"0/0","r_frame_rate":"24/1"},
                        {"codec_type":"data","tags":{"timecode":"10:00:00:00"}}],"format":{}}
            """;
        var i = FFProbe.Parse(json);
        Assert.Null(i.Width);
        Assert.Equal("24/1", i.FrameRate);
        Assert.Equal("10:00:00:00", i.Timecode);
        Assert.Equal(0, i.AudioChannels);
    }

    [FfmpegFact]
    public void Probe_RealClip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "Záběr 'A' 01.mov");
        TestMedia.MakeClip(path, seconds: 4, timecode: "01:00:00:00");
        var i = FFProbe.Probe(TestMedia.Ffprobe!, path);
        Assert.Equal(1280, i.Width);
        Assert.Equal("25/1", i.FrameRate);
        Assert.Equal("01:00:00:00", i.Timecode);
        Assert.True(i.AudioChannels > 0);
        Assert.InRange(i.Duration ?? 0, 3.9, 4.2);
    }
}
```

- [ ] **Step 3: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~ClipDetectorTests"`
Expected: build FAIL — `ClipDetector`, `FFProbe` neexistují.

- [ ] **Step 4: Implementace**

Do `windows/src/Offload.Core/Win32.cs` přidat do třídy `Win32`:

```csharp
    /// Explorer's natural order ("C2" before "C10") — the Windows counterpart of localizedStandardCompare.
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int StrCmpLogicalW(string a, string b);
```

`windows/src/Offload.Core/ClipDetector.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Offload.Core;

public static partial class ClipDetector
{
    static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mov", "mp4", "m4v", "mxf", "mts", "m2ts", "avi", "mkv",
    };

    [GeneratedRegex(@"_\d{3}$")]
    private static partial Regex R3dSegmentSuffix();

    public static List<Clip> Detect(IReadOnlyList<SourceFile> files)
    {
        var clips = new List<Clip>();
        var r3dGroups = new Dictionary<string, List<SourceFile>>();
        var ariGroups = new Dictionary<string, List<SourceFile>>();

        foreach (var f in files)
        {
            var rel = f.RelPath;
            if (rel.StartsWith(Layout.ReportDirName + "/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith(Layout.ProxyDirName + "/", StringComparison.OrdinalIgnoreCase)) continue;
            int slash = rel.LastIndexOf('/');
            var dir = slash < 0 ? "" : rel[..slash];
            var file = rel[(slash + 1)..];
            int dot = file.LastIndexOf('.');
            var ext = dot < 0 ? "" : file[(dot + 1)..].ToLowerInvariant();
            var baseName = dot < 0 ? file : file[..dot];
            switch (ext)
            {
                case "r3d":
                    // A001_C002_0101AB_001.R3D, _002.R3D … are one clip.
                    var key = dir + "/" + R3dSegmentSuffix().Replace(baseName, "");
                    if (!r3dGroups.TryGetValue(key, out var r3d)) r3dGroups[key] = r3d = [];
                    r3d.Add(f);
                    break;
                case "ari":
                    if (!ariGroups.TryGetValue(dir, out var ari)) ariGroups[dir] = ari = [];
                    ari.Add(f);
                    break;
                case "braw":
                    clips.Add(new Clip { Name = baseName, Path = rel, Format = ClipFormat.Braw, Segments = [rel], Bytes = f.Size });
                    break;
                case "crm":
                    clips.Add(new Clip { Name = baseName, Path = rel, Format = ClipFormat.CanonRaw, Segments = [rel], Bytes = f.Size });
                    break;
                default:
                    if (!VideoExtensions.Contains(ext) || f.Size <= 0) break;
                    clips.Add(new Clip { Name = baseName, Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = f.Size, IsCameraProxy = IsCameraProxy(rel) });
                    break;
            }
        }
        foreach (var (key, segs) in r3dGroups)
        {
            var sorted = segs.OrderBy(s => s.RelPath, StringComparer.Ordinal).ToList();
            clips.Add(new Clip
            {
                Name = key[(key.LastIndexOf('/') + 1)..], Path = sorted[0].RelPath, Format = ClipFormat.R3d,
                Segments = sorted.Select(s => s.RelPath).ToList(), Bytes = sorted.Sum(s => s.Size),
            });
        }
        foreach (var (dir, frames) in ariGroups)
        {
            var sorted = frames.OrderBy(s => s.RelPath, StringComparer.Ordinal).ToList();
            var name = dir[(dir.LastIndexOf('/') + 1)..];
            clips.Add(new Clip
            {
                Name = name.Length > 0 ? name : "ARRIRAW", Path = dir, Format = ClipFormat.Arriraw,
                Segments = sorted.Select(s => s.RelPath).ToList(), Bytes = sorted.Sum(s => s.Size),
            });
        }
        clips.Sort((a, b) => Win32.StrCmpLogicalW(a.Path, b.Path));
        return clips;
    }

    /// Low-res files the camera records next to the original (Sony SUB, Panasonic/Canon PROXY folders).
    internal static bool IsCameraProxy(string rel)
    {
        var comps = rel.ToUpperInvariant().Split('/');
        return comps[..^1].Any(c => c is "SUB" or "PROXY");
    }
}

public static class FFProbe
{
    public static ClipInfo Probe(string ffprobe, string path) =>
        Parse(ToolProcess.Run(ffprobe, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path]));

    public static ClipInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var streams = root.TryGetProperty("streams", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().ToList() : [];
        var format = root.TryGetProperty("format", out var fmt) ? fmt : default;
        var info = new ClipInfo();

        var v = streams.FirstOrDefault(x => Str(x, "codec_type") == "video"
            && !(x.TryGetProperty("disposition", out var d) && Int(d, "attached_pic") == 1));
        if (v.ValueKind == JsonValueKind.Object)
        {
            var name = Str(v, "codec_name");
            info.Codec = string.Join(" ", new[] { name, Str(v, "profile") }.Where(x => x != null));
            // ffmpeg reports codecs it cannot decode (ARRIRAW, X-OCN …) as "unknown", 0×0.
            if (name != null && name != "unknown")
            {
                info.Width = Int(v, "width") is int w && w > 0 ? w : null;
                info.Height = Int(v, "height") is int h && h > 0 ? h : null;
            }
            info.FrameRate = Str(v, "avg_frame_rate") is string avg && avg != "0/0" ? avg : Str(v, "r_frame_rate");
            info.Frames = int.TryParse(Str(v, "nb_frames"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            info.Timecode = Str(Tags(v), "timecode");
            info.Duration = Dbl(Str(v, "duration"));
        }
        info.AudioChannels = streams.Where(x => Str(x, "codec_type") == "audio").Sum(x => Int(x, "channels") ?? 0);
        if (Dbl(Str(format, "duration")) is double fd) info.Duration = fd;
        var ftags = Tags(format);
        info.Timecode ??= Str(ftags, "timecode") ?? streams.Select(x => Str(Tags(x), "timecode")).FirstOrDefault(t => t != null);
        info.Camera = new[] { "com.apple.quicktime.make", "com.apple.quicktime.model", "product_name", "company_name" }
            .Select(k => Str(ftags, k)).FirstOrDefault(x => x != null);
        return info;
    }

    static JsonElement Tags(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("tags", out var t) ? t : default;

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;

    static double? Dbl(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
```

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~ClipDetectorTests"`
Expected: PASS (4 testy; `Probe_RealClip` se na tomto PC spustí — ffmpeg je na PATH).

- [ ] **Step 6: Commit**

Řádek do VYVOJ: `- Úkol 2 — detekce klipů (video, R3D segmenty, ARRI sekvence, BRAW, Canon RAW, proxy kamery) a čtení metadat přes ffprobe`.

```powershell
git add windows docs
git commit -m "Windows: clip detection and ffprobe metadata" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Timecode a enkodéry

**Files:**
- Create: `windows/src/Offload.Core/Timecode.cs`, `windows/src/Offload.Core/Encoders.cs`
- Modify: `windows/src/Offload.Core/Media.cs` (`ProxyCodecExtensions`: `Wire`, `TryParseWire`)
- Test: `windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs`

**Interfaces:**
- Produces: `Timecode.IsValid(string?) -> bool`, `Timecode.Fps(string? rational) -> double?`, `Timecode.Add(string tc, long frames, double fps) -> string`; `Encoders.H264`, `Encoders.Hevc` (string[]), `Encoders.Pick(string ffmpeg, ProxyCodec) -> string`, internal `Encoders.Works(string ffmpeg, string encoder) -> bool`, `Encoders.VideoArgs(string encoder, ProxyCodec, int bitrateMbps) -> List<string>`, `Encoders.AudioArgs(ProxyCodec) -> string[]`, `Encoders.PixelFormat(ProxyCodec) -> string`; `ProxyCodec.Wire() -> string`, `ProxyCodecExtensions.TryParseWire(string, out ProxyCodec) -> bool`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/TimecodeEncoderTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class TimecodeEncoderTests
{
    [Theory]
    [InlineData("01:00:00:00", true)]
    [InlineData("01:00:00;29", true)]
    [InlineData("1:00:00:00", false)]
    [InlineData(null, false)]
    public void IsValid(string? tc, bool ok) => Assert.Equal(ok, Timecode.IsValid(tc));

    [Theory]
    [InlineData("24000/1001", 23.976)]
    [InlineData("25/1", 25.0)]
    [InlineData("50", 50.0)]
    public void Fps(string rate, double fps) => Assert.Equal(fps, Timecode.Fps(rate)!.Value, 3);

    [Fact]
    public void Fps_InvalidIsNull()
    {
        Assert.Null(Timecode.Fps(null));
        Assert.Null(Timecode.Fps("0/0"));
    }

    [Theory]
    [InlineData("01:00:00:00", 0, 25, "01:00:00:00")]
    [InlineData("01:00:00:00", 50, 25, "01:00:02:00")]
    [InlineData("01:00:00:00", 99, 25, "01:00:03:24")]
    [InlineData("23:59:59:24", 1, 25, "00:00:00:00")]
    [InlineData("10:00:00;00", 30, 29.97, "10:00:01;00")]
    public void Add(string tc, long frames, double fps, string expected) => Assert.Equal(expected, Timecode.Add(tc, frames, fps));

    [Fact]
    public void VideoArgs_PerEncoder()
    {
        Assert.Equal(["-c:v", "h264_nvenc", "-profile:v", "high", "-rc", "vbr", "-b:v", "10M", "-maxrate", "15M"],
            Encoders.VideoArgs("h264_nvenc", ProxyCodec.H264, 10));
        Assert.Equal(["-c:v", "hevc_amf", "-tag:v", "hvc1", "-b:v", "8M", "-maxrate", "12M"],
            Encoders.VideoArgs("hevc_amf", ProxyCodec.Hevc, 8));
        Assert.Equal(["-c:v", "libx264", "-profile:v", "high", "-preset", "fast", "-b:v", "10M", "-maxrate", "15M", "-bufsize", "20M"],
            Encoders.VideoArgs("libx264", ProxyCodec.H264, 10));
        Assert.Equal(["-c:v", "prores_ks", "-profile:v", "1", "-vendor", "apl0"],
            Encoders.VideoArgs("prores_ks", ProxyCodec.ProresLT, 10));
        Assert.Equal(["-c:a", "pcm_s16le"], Encoders.AudioArgs(ProxyCodec.Prores422HQ));
        Assert.Equal("yuv422p10le", Encoders.PixelFormat(ProxyCodec.ProresProxy));
        Assert.Equal("yuv420p", Encoders.PixelFormat(ProxyCodec.Hevc));
    }

    [Fact]
    public void CodecWireNames_RoundTrip()
    {
        foreach (var c in Enum.GetValues<ProxyCodec>())
        {
            Assert.True(ProxyCodecExtensions.TryParseWire(c.Wire(), out var back));
            Assert.Equal(c, back);
        }
        Assert.False(ProxyCodecExtensions.TryParseWire("vp9", out _));
    }

    [FfmpegFact]
    public void Pick_FindsAWorkingEncoder()
    {
        var h264 = Encoders.Pick(TestMedia.Ffmpeg!, ProxyCodec.H264);
        Assert.Contains(h264, Encoders.H264);
        Assert.True(Encoders.Works(TestMedia.Ffmpeg!, h264));
        Assert.Equal("prores_ks", Encoders.Pick(TestMedia.Ffmpeg!, ProxyCodec.ProresProxy));
        Assert.False(Encoders.Works(TestMedia.Ffmpeg!, "no_such_encoder"));
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~TimecodeEncoderTests"`
Expected: build FAIL — `Timecode`, `Encoders`, `Wire` neexistují.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/Timecode.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Offload.Core;

public static partial class Timecode
{
    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}[:;.]\d{2}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? tc) => tc != null && Pattern().IsMatch(tc);

    /// "24000/1001" → 23.976; "25" → 25; null for anything unusable.
    public static double? Fps(string? rational)
    {
        if (string.IsNullOrEmpty(rational)) return null;
        var parts = rational.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return null;
        double fps = num;
        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) || den == 0) return null;
            fps = num / den;
        }
        return fps > 0 ? fps : null;
    }

    /// Start timecode plus a number of frames, counted at the nominal (rounded) rate and wrapping at 24 h.
    /// Drop-frame timecode is labelled approximately — good enough for a thumbnail caption.
    public static string Add(string tc, long frames, double fps)
    {
        int nominal = Math.Max(1, (int)Math.Round(fps));
        var sep = tc[8];
        long h = long.Parse(tc[..2], CultureInfo.InvariantCulture), m = long.Parse(tc[3..5], CultureInfo.InvariantCulture);
        long s = long.Parse(tc[6..8], CultureInfo.InvariantCulture), f = long.Parse(tc[9..11], CultureInfo.InvariantCulture);
        long perDay = 24L * 3600 * nominal;
        long total = (((h * 3600 + m * 60 + s) * nominal + f + frames) % perDay + perDay) % perDay;
        long ff = total % nominal, secs = total / nominal;
        return string.Create(CultureInfo.InvariantCulture, $"{secs / 3600:00}:{secs / 60 % 60:00}:{secs % 60:00}{sep}{ff:00}");
    }
}
```

`windows/src/Offload.Core/Encoders.cs`:

```csharp
using System.Collections.Concurrent;

namespace Offload.Core;

/// Picks the proxy encoder this machine can actually run: NVIDIA → Intel → AMD hardware, then software.
public static class Encoders
{
    public static readonly string[] H264 = ["h264_nvenc", "h264_qsv", "h264_amf", "libx264"];
    public static readonly string[] Hevc = ["hevc_nvenc", "hevc_qsv", "hevc_amf", "libx265"];

    static readonly ConcurrentDictionary<(string, ProxyCodec), string> Cache = new();

    public static string Pick(string ffmpeg, ProxyCodec codec)
    {
        if (codec is not (ProxyCodec.H264 or ProxyCodec.Hevc)) return "prores_ks";
        return Cache.GetOrAdd((ffmpeg, codec), key =>
        {
            var candidates = key.Item2 == ProxyCodec.H264 ? H264 : Hevc;
            return candidates.FirstOrDefault(e => Works(ffmpeg, e)) ?? candidates[^1];
        });
    }

    /// One black frame through the encoder — fails fast when the GPU or driver is missing.
    internal static bool Works(string ffmpeg, string encoder)
    {
        try
        {
            ToolProcess.Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i", "color=black:s=256x144",
                "-frames:v", "1", "-c:v", encoder, "-f", "null", "-"]);
            return true;
        }
        catch (ToolError)
        {
            return false;
        }
    }

    public static List<string> VideoArgs(string encoder, ProxyCodec codec, int bitrateMbps)
    {
        if (codec is not (ProxyCodec.H264 or ProxyCodec.Hevc))
        {
            int profile = codec switch { ProxyCodec.ProresProxy => 0, ProxyCodec.ProresLT => 1, ProxyCodec.Prores422 => 2, _ => 3 };
            return ["-c:v", "prores_ks", "-profile:v", profile.ToString(System.Globalization.CultureInfo.InvariantCulture), "-vendor", "apl0"];
        }
        var args = new List<string> { "-c:v", encoder };
        args.AddRange(codec == ProxyCodec.H264 ? ["-profile:v", "high"] : ["-tag:v", "hvc1"]);
        if (encoder.EndsWith("_nvenc", StringComparison.Ordinal)) args.AddRange(["-rc", "vbr"]);
        if (encoder.StartsWith("lib", StringComparison.Ordinal)) args.AddRange(["-preset", "fast"]);
        args.AddRange(["-b:v", $"{bitrateMbps}M", "-maxrate", $"{bitrateMbps * 3 / 2}M"]);
        if (encoder.StartsWith("lib", StringComparison.Ordinal)) args.AddRange(["-bufsize", $"{bitrateMbps * 2}M"]);
        return args;
    }

    public static string[] AudioArgs(ProxyCodec codec) =>
        codec is ProxyCodec.H264 or ProxyCodec.Hevc ? ["-c:a", "aac", "-b:a", "192k"] : ["-c:a", "pcm_s16le"];

    public static string PixelFormat(ProxyCodec codec) =>
        codec is ProxyCodec.H264 or ProxyCodec.Hevc ? "yuv420p" : "yuv422p10le";
}
```

Do `ProxyCodecExtensions` v `windows/src/Offload.Core/Media.cs` přidat:

```csharp
    public static string Wire(this ProxyCodec c) => c switch
    {
        ProxyCodec.H264 => "h264",
        ProxyCodec.Hevc => "hevc",
        ProxyCodec.ProresProxy => "proresProxy",
        ProxyCodec.ProresLT => "proresLT",
        ProxyCodec.Prores422 => "prores422",
        _ => "prores422HQ",
    };

    public static bool TryParseWire(string s, out ProxyCodec codec)
    {
        foreach (var c in Enum.GetValues<ProxyCodec>())
        {
            if (c.Wire() == s) { codec = c; return true; }
        }
        codec = default;
        return false;
    }
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~TimecodeEncoderTests"`
Expected: PASS (17 testů vč. řádků theory; `Pick_FindsAWorkingEncoder` na tomto PC vybere `h264_nvenc`).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 3 — timecode pro popisky náhledů, automatická volba enkodéru (NVENC → QSV → AMF → software)`.

```powershell
git add windows docs
git commit -m "Windows: timecode helpers and hardware encoder selection" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Filter graph

**Files:**
- Create: `windows/src/Offload.Core/FilterGraph.cs`
- Test: `windows/tests/Offload.Core.Tests/FilterGraphTests.cs`

**Interfaces:**
- Consumes: `ProxySettings`, `Timecode.IsValid`, `Encoders.PixelFormat`.
- Produces: `FilterGraph.LutFile` (`"look.cube"`), `FilterGraph.FontFile` (`"font.ttf"`), `FilterGraph.Thumbnail(bool lut) -> string`, `FilterGraph.BurnIn(ProxySettings, int height, bool hasFont, string? labelFile, string? timecode, string? rate) -> List<string>`, `FilterGraph.Proxy(ProxySettings, int outHeight, bool alreadyCropped, bool lut, bool hasFont, string? labelFile, string? timecode, string? rate) -> string`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/FilterGraphTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class FilterGraphTests
{
    [Fact]
    public void Thumbnail()
    {
        Assert.Equal("scale=-2:180:flags=bicubic,format=yuvj420p", FilterGraph.Thumbnail(lut: false));
        Assert.Equal("scale=-2:180:flags=bicubic,format=rgb48le,lut3d=file=look.cube:interp=tetrahedral,format=yuvj420p", FilterGraph.Thumbnail(lut: true));
    }

    [Fact]
    public void BurnIn_NameAndTimecode()
    {
        var s = new ProxySettings();
        Assert.Equal(
        [
            "drawbox=x=0:y=ih-64:w=iw:h=64:color=black@0.55:t=fill",
            "drawtext=fontfile=font.ttf:fontcolor=white:fontsize=32:y=h-48:textfile=label-1.txt:x=21",
            "drawtext=fontfile=font.ttf:fontcolor=white:fontsize=32:y=h-48:timecode='01\\:00\\:00\\:00':rate=25/1:x=w-tw-21",
        ], FilterGraph.BurnIn(s, 1080, hasFont: true, "label-1.txt", "01:00:00:00", "25/1"));
    }

    [Fact]
    public void BurnIn_InvalidTimecodeStartsAtZero_DropFrameEscaped()
    {
        var s = new ProxySettings { BurnFilename = false };
        Assert.Equal("drawtext=fontfile=font.ttf:fontcolor=white:fontsize=12:y=h-18:timecode='00\\:00\\:00\\:00':rate=24/1:x=w-tw-8",
            FilterGraph.BurnIn(s, 360, true, null, "bad", "24/1")[1]);
        Assert.EndsWith("timecode='10\\:00\\:00\\;00':rate=30000/1001:x=w-tw-8",
            FilterGraph.BurnIn(s, 360, true, null, "10:00:00;00", "30000/1001")[1]);
    }

    [Fact]
    public void BurnIn_NothingWithoutFontOrWhenDisabled()
    {
        Assert.Empty(FilterGraph.BurnIn(new ProxySettings(), 1080, hasFont: false, "l.txt", "01:00:00:00", "25/1"));
        Assert.Empty(FilterGraph.BurnIn(new ProxySettings { BurnFilename = false, BurnTimecode = false }, 1080, true, "l.txt", null, null));
    }

    [Fact]
    public void Proxy_FullChain()
    {
        var s = new ProxySettings { FrameAspect = 1.66, FrameScale = 0.9, BurnTimecode = false };
        Assert.Equal(
            "crop=w='trunc(min(iw*0.9,ih*0.9*1.66)/2)*2':h='trunc(min(iw*0.9,ih*0.9*1.66)/1.66/2)*2'," +
            "scale=-2:720:flags=bicubic,setsar=1,format=rgb48le,lut3d=file=look.cube:interp=tetrahedral," +
            "drawbox=x=0:y=ih-42:w=iw:h=42:color=black@0.55:t=fill," +
            "drawtext=fontfile=font.ttf:fontcolor=white:fontsize=21:y=h-31:textfile=label.txt:x=14,format=yuv420p",
            FilterGraph.Proxy(s, 720, alreadyCropped: false, lut: true, hasFont: true, "label.txt", "01:00:00:00", "25/1"));
    }

    [Fact]
    public void Proxy_AlreadyCroppedProresNoLut()
    {
        var s = new ProxySettings { FrameAspect = 2.39, Codec = ProxyCodec.ProresProxy, BurnFilename = false, BurnTimecode = false };
        Assert.Equal("scale=-2:1080:flags=bicubic,setsar=1,format=yuv422p10le",
            FilterGraph.Proxy(s, 1080, alreadyCropped: true, lut: false, hasFont: true, null, null, null));
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~FilterGraphTests"`
Expected: build FAIL — `FilterGraph` neexistuje.

- [ ] **Step 3: Implementace**

`windows/src/Offload.Core/FilterGraph.cs`:

```csharp
using System.Globalization;

namespace Offload.Core;

/// ffmpeg filter graphs for thumbnails and proxies. They only ever name files in the renderer's work folder
/// by relative name — a Windows path ("C:\…") inside a filter graph would need escaping that breaks easily.
public static class FilterGraph
{
    public const string LutFile = "look.cube";
    public const string FontFile = "font.ttf";

    static string Num(double v) => v.ToString(CultureInfo.InvariantCulture);

    static string Lut => $"format=rgb48le,lut3d=file={LutFile}:interp=tetrahedral";

    /// Small JPEG for the report, with the LUT applied so log footage is readable.
    public static string Thumbnail(bool lut) =>
        string.Join(",", new[] { "scale=-2:180:flags=bicubic", lut ? Lut : null, "format=yuvj420p" }.Where(x => x != null));

    /// Bottom bar: clip name left, source timecode right.
    public static List<string> BurnIn(ProxySettings s, int height, bool hasFont, string? labelFile, string? timecode, string? rate)
    {
        bool name = s.BurnFilename && labelFile != null;
        bool tc = s.BurnTimecode && rate != null;
        if (!hasFont || (!name && !tc)) return [];
        int size = Math.Max(12, height * 3 / 100);
        int margin = Math.Max(8, height * 2 / 100);
        var f = new List<string> { $"drawbox=x=0:y=ih-{size * 2}:w=iw:h={size * 2}:color=black@0.55:t=fill" };
        var common = $"fontfile={FontFile}:fontcolor=white:fontsize={size}:y=h-{size * 3 / 2}";
        if (name) f.Add($"drawtext={common}:textfile={labelFile}:x={margin}");
        if (tc)
        {
            var start = Timecode.IsValid(timecode) ? timecode! : "00:00:00:00";
            var escaped = start.Replace(":", "\\:").Replace(";", "\\;");
            f.Add($"drawtext={common}:timecode='{escaped}':rate={rate}:x=w-tw-{margin}");
        }
        return f;
    }

    public static string Proxy(ProxySettings s, int outHeight, bool alreadyCropped, bool lut, bool hasFont,
        string? labelFile, string? timecode, string? rate)
    {
        var vf = new List<string>();
        if (s.FrameAspect is double aspect && !alreadyCropped)
        {
            // Crop to the frame lines first, sized from whatever resolution the decoder delivers.
            var sc = Num(Math.Min(Math.Max(s.FrameScale, 0.1), 1.0));
            var a = Num(aspect);
            vf.Add($"crop=w='trunc(min(iw*{sc},ih*{sc}*{a})/2)*2':h='trunc(min(iw*{sc},ih*{sc}*{a})/{a}/2)*2'");
        }
        vf.Add($"scale=-2:{outHeight}:flags=bicubic");
        vf.Add("setsar=1");
        if (lut) vf.Add(Lut);
        vf.AddRange(BurnIn(s, outHeight, hasFont, labelFile, timecode, rate));
        vf.Add($"format={Encoders.PixelFormat(s.Codec)}");
        return string.Join(",", vf);
    }
}
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~FilterGraphTests"`
Expected: PASS (6 testů).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 4 — filter graph pro náhledy a proxy (ořez na frame lines, LUT, burn-in názvu a TC) jen s relativními jmény souborů`.

```powershell
git add windows docs
git commit -m "Windows: ffmpeg filter graphs for thumbnails and proxies" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: RAW backendy

**Files:**
- Create: `windows/src/Offload.Core/FrameSource.cs`, `windows/src/Offload.Core/RawBackends.cs`
- Test: `windows/tests/Offload.Core.Tests/RawBackendsTests.cs`

**Interfaces:**
- Consumes: `ClipInfo`, `ClipFormat`, `ProxySettings` (`FrameCrop`), `ExternalTools`, `ToolProcess.Run`, `ToolError`, `Timecode.Fps`.
- Produces:
  - `FrameSource` (internal) — `InputArgs`, `FeederExe`, `FeederArgs`, `AudioFromInput` (default true), `AlreadyCropped`, `Cleanup`, `RemoveIntermediates()`.
  - `IRawBackend` (internal) — `Format`, `MissingHint`, `IsAvailable(ExternalTools)`, `Probe(string path, ExternalTools, string workDir) -> ClipInfo`, `Source(string path, ClipInfo, ProxySettings, ExternalTools, string workDir, double? singleFrameAt) -> FrameSource`.
  - `RawBackends.All` (IReadOnlyList<IRawBackend>), `RawBackends.Rational(double fps) -> string`, `RawBackends.NewestFile(string dir, string ext) -> string?`.
  - `BrawBackend.ParseInfo(string json) -> BrawInfo`, `BrawBackend.ToClipInfo(BrawInfo)`, `BrawBackend.Scale(int sourceHeight, int target) -> string`; `R3dBackend.ParseMeta(string) -> ClipInfo`, `R3dBackend.Args(string input, string outDir, ClipInfo, ProxySettings) -> List<string>`; `ArriBackend.ParseMetadata(string json) -> ClipInfo`, `ArriBackend.ColorSpace(string? lut, ClipInfo) -> string`, `ArriBackend.Args(string input, string output, ClipInfo, ProxySettings, string parametersFile) -> (List<string> Args, string? ParametersJson, bool Cropped)`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/RawBackendsTests.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Offload.Core.Tests;

public class RawBackendsTests
{
    [Theory]
    [InlineData(23.976, "24000/1001")]
    [InlineData(29.97, "30000/1001")]
    [InlineData(25.0, "25")]
    [InlineData(12.5, "12.500")]
    public void Rational(double fps, string expected) => Assert.Equal(expected, RawBackends.Rational(fps));

    [Fact]
    public void Braw_InfoAndScale()
    {
        var i = BrawBackend.ParseInfo("""{"width":1440,"height":810,"sourceWidth":11520,"sourceHeight":6480,"fps":23.976,"frames":240,"timecode":"01:02:03:04","camera":"URSA Mini Pro 12K"}""");
        var c = BrawBackend.ToClipInfo(i);
        Assert.Equal("Blackmagic RAW", c.Codec);
        Assert.Equal(11520, c.Width);
        Assert.Equal("24000/1001", c.FrameRate);
        Assert.Equal(240, c.Frames);
        Assert.Equal(10.01, c.Duration!.Value, 2);
        Assert.Equal("01:02:03:04", c.Timecode);
        Assert.Equal("quarter", BrawBackend.Scale(6480, 1080));
        Assert.Equal("eighth", BrawBackend.Scale(6480, 180));
        Assert.Equal("full", BrawBackend.Scale(1080, 1080));
    }

    [Fact]
    public void R3d_ParseMeta()
    {
        var c = R3dBackend.ParseMeta("Frame Width: 8192\nFrame Height: 4320\nRecord FPS: 23.976\nTotal Frames: 240\nAbs TC: 12:34:56:01\nCamera Model: RED V-RAPTOR\n");
        Assert.Equal("REDCODE RAW", c.Codec);
        Assert.Equal(8192, c.Width);
        Assert.Equal(4320, c.Height);
        Assert.Equal("24000/1001", c.FrameRate);
        Assert.Equal(240, c.Frames);
        Assert.Equal("12:34:56:01", c.Timecode);
        Assert.Equal("RED V-RAPTOR", c.Camera);
    }

    [Fact]
    public void R3d_ArgsPickResolutionAndLogWithLut()
    {
        var info = new ClipInfo { Height = 4320 };
        var args = R3dBackend.Args(@"E:\A.R3D", @"C:\t\r3d", info, new ProxySettings { Height = 1080, LutPath = "x.cube" });
        Assert.Equal(["--i", @"E:\A.R3D", "--outDir", @"C:\t\r3d", "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", "4", "--useMeta",
            "--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"], args);
        Assert.Contains("3", R3dBackend.Args("a", "b", new ClipInfo { Height = 2160 }, new ProxySettings()));
        Assert.Contains("1", R3dBackend.Args("a", "b", new ClipInfo { Height = 1080 }, new ProxySettings()));
    }

    const string ArriJson = """
        {"clipBasedMetadata":{"cameraModel":"ALEXA 35","videoCodec":"ARRIRAW","storedSize":{"width":4608,"height":3164},
          "clipDuration":48,"sceneColorEncodingTransferCurve":"LogC4","lensSqueeze":2000,
          "nativePictureEssenceDescriptor":{"file":{"sampleRate":"24/1"}}},
         "frameBasedMetadata":{"frames":[{"timecode":"10:00:00:00"},{"timecode":"10:00:00:01"}]}}
        """;

    [Fact]
    public void Arri_ParseMetadata()
    {
        var c = ArriBackend.ParseMetadata(ArriJson);
        Assert.Equal("ARRIRAW", c.Codec);
        Assert.Equal(4608, c.Width);
        Assert.Equal(3164, c.Height);
        Assert.Equal("24", c.FrameRate);
        Assert.Equal(48, c.Frames);
        Assert.Equal(2.0, c.Duration);
        Assert.Equal("10:00:00:00", c.Timecode);
        Assert.Equal("ALEXA 35", c.Camera);
        Assert.Equal("LogC4", c.LogEncoding);
        Assert.Equal(2.0, c.Squeeze);
        Assert.Equal(0, c.AudioChannels);
    }

    [Theory]
    [InlineData("ARRI_LogC4-to-Gamma24_Rec709.cube", null, null, "AWG4/D65/LogC4")]
    [InlineData("show_LogC3_709.cube", "LogC4", null, "AWG3/D65/LogC3")]
    [InlineData(null, "LogC4", null, "AWG4/D65/LogC4")]
    [InlineData(null, null, "ALEXA Mini", "AWG3/D65/LogC3")]
    [InlineData(null, null, "ALEXA 35", "AWG4/D65/LogC4")]
    public void Arri_ColorSpace(string? lut, string? log, string? camera, string expected) =>
        Assert.Equal(expected, ArriBackend.ColorSpace(lut, new ClipInfo { LogEncoding = log, Camera = camera }));

    [Fact]
    public void Arri_ArgsWithFrameLinesCropOnSensor()
    {
        var info = new ClipInfo { Width = 4608, Height = 3164 };
        var (args, json, cropped) = ArriBackend.Args(@"E:\c.mxf", @"C:\t\i.mxf", info, new ProxySettings { FrameAspect = 1.66, Height = 1080 }, @"C:\t\p.json");
        Assert.True(cropped);
        Assert.Equal(["process", "--input", @"E:\c.mxf", "--video-codec", "prores422lt", "--deflip", "--output", @"C:\t\i.mxf",
            "--parameters", @"C:\t\p.json", "--target-colorspace", "Rec.709/D65/BT.1886", "--embedded-look"], args);
        var p = JsonNode.Parse(json!)!;
        Assert.Equal(0, (int)p["inputCrop"]!["displayXOffsetOverride"]!);
        Assert.Equal(194, (int)p["inputCrop"]!["displayYOffsetOverride"]!);
        Assert.Equal(4608, (int)p["inputCrop"]!["displayWidthOverride"]!);
        Assert.Equal(2774, (int)p["inputCrop"]!["displayHeightOverride"]!);
        Assert.Equal(1794, (int)p["outputScaling"]!["targetWidth"]!);
        Assert.Equal(1080, (int)p["outputScaling"]!["targetHeight"]!);
    }

    [Fact]
    public void Arri_ArgsWithoutCropDesqueezeAndLog()
    {
        var (args, json, cropped) = ArriBackend.Args("in", "out", new ClipInfo { Camera = "ALEXA 35" }, new ProxySettings { Height = 720, LutPath = "look.cube" }, "p.json");
        Assert.False(cropped);
        Assert.Null(json);
        Assert.Equal(["process", "--input", "in", "--video-codec", "prores422lt", "--deflip", "--output", "out",
            "--desqueeze", "--output-height", "720", "--target-colorspace", "AWG4/D65/LogC4"], args);
    }

    [Fact]
    public void Availability_FollowsTools()
    {
        var none = new ExternalTools { NoSearch = true }.Resolved();
        Assert.All(RawBackends.All, b => Assert.False(b.IsAvailable(none)));
        Assert.Equal([ClipFormat.Braw, ClipFormat.R3d, ClipFormat.Arriraw, ClipFormat.CanonRaw], RawBackends.All.Select(b => b.Format));
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~RawBackendsTests"`
Expected: build FAIL — `RawBackends`, `BrawBackend`, … neexistují.

- [ ] **Step 3: FrameSource**

`windows/src/Offload.Core/FrameSource.cs`:

```csharp
namespace Offload.Core;

/// How frames reach ffmpeg: straight from a file, or piped from a vendor RAW decoder.
internal sealed class FrameSource
{
    public required IReadOnlyList<string> InputArgs { get; init; }
    /// Decoder whose stdout is piped into ffmpeg's stdin (input "-"), or null.
    public string? FeederExe { get; init; }
    public IReadOnlyList<string> FeederArgs { get; init; } = [];
    public bool AudioFromInput { get; init; } = true;
    /// The decoder already cropped to the frame lines (ARRI does it before debayering).
    public bool AlreadyCropped { get; init; }
    /// Intermediate files or folders to delete once ffmpeg is done.
    public IReadOnlyList<string> Cleanup { get; init; } = [];

    public void RemoveIntermediates()
    {
        foreach (var p in Cleanup)
        {
            try
            {
                if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
                else File.Delete(p);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
```

- [ ] **Step 4: RawBackends**

`windows/src/Offload.Core/RawBackends.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Offload.Core;

/// Per-format decoding for formats ffmpeg cannot read itself.
internal interface IRawBackend
{
    ClipFormat Format { get; }
    string MissingHint { get; }
    bool IsAvailable(ExternalTools tools);
    ClipInfo Probe(string path, ExternalTools tools, string workDir);
    /// `singleFrameAt` (seconds) asks for just one frame — used for report thumbnails.
    FrameSource Source(string path, ClipInfo info, ProxySettings settings, ExternalTools tools, string workDir, double? singleFrameAt);
}

/// Vendor decoders for formats ffmpeg cannot read. All of them hand ffmpeg footage in the camera's
/// log encoding, so the same LUT / scale / burn-in chain applies to every camera.
internal static class RawBackends
{
    public static readonly IReadOnlyList<IRawBackend> All = [new BrawBackend(), new R3dBackend(), new ArriBackend(), new CanonRawBackend()];

    /// ffmpeg wants a rational frame rate; cameras report floats.
    public static string Rational(double fps)
    {
        foreach (var (v, r) in new[] { (23.976, "24000/1001"), (29.97, "30000/1001"), (47.952, "48000/1001"), (59.94, "60000/1001"), (119.88, "120000/1001") })
            if (Math.Abs(fps - v) < 0.01) return r;
        return fps == Math.Round(fps) ? ((long)fps).ToString(CultureInfo.InvariantCulture) : fps.ToString("0.000", CultureInfo.InvariantCulture);
    }

    public static string? NewestFile(string dir, string ext) =>
        Directory.Exists(dir)
            ? new DirectoryInfo(dir).GetFiles("*." + ext).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
            : null;

    public static string FreshDir(string workDir, string name)
    {
        var d = Path.Combine(workDir, $"{name}-{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(d);
        return d;
    }
}

// MARK: Blackmagic RAW (braw-decode → raw RGB48 frames on a pipe)

internal sealed record BrawInfo(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("sourceWidth")] int SourceWidth,
    [property: JsonPropertyName("sourceHeight")] int SourceHeight,
    [property: JsonPropertyName("fps")] double Fps,
    [property: JsonPropertyName("frames")] int Frames,
    [property: JsonPropertyName("timecode")] string Timecode,
    [property: JsonPropertyName("camera")] string Camera);

internal sealed class BrawBackend : IRawBackend
{
    public ClipFormat Format => ClipFormat.Braw;
    public string MissingHint => "Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW (SDK, Player nebo DaVinci Resolve)";
    public bool IsAvailable(ExternalTools tools) => tools.BrawDecoder != null;

    public static BrawInfo ParseInfo(string json) => JsonSerializer.Deserialize<BrawInfo>(json)
        ?? throw new ToolError("braw-decode", -1, "unreadable --info output");

    public static ClipInfo ToClipInfo(BrawInfo i) => new()
    {
        Codec = "Blackmagic RAW",
        Width = i.SourceWidth,
        Height = i.SourceHeight,
        FrameRate = RawBackends.Rational(i.Fps),
        Frames = i.Frames,
        Duration = i.Fps > 0 ? i.Frames / i.Fps : null,
        Timecode = string.IsNullOrEmpty(i.Timecode) ? null : i.Timecode,
        Camera = string.IsNullOrEmpty(i.Camera) ? null : i.Camera,
    };

    /// Smallest decode scale that still covers the target height — decoding less is much faster.
    public static string Scale(int sourceHeight, int target)
    {
        foreach (var (name, div) in new[] { ("eighth", 8), ("quarter", 4), ("half", 2) })
            if (sourceHeight / div >= target) return name;
        return "full";
    }

    BrawInfo Info(string path, string scale, ExternalTools tools) =>
        ParseInfo(ToolProcess.Run(tools.BrawDecoder!, ["--info", "--scale", scale, path]));

    public ClipInfo Probe(string path, ExternalTools tools, string workDir) => ToClipInfo(Info(path, "eighth", tools));

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, ExternalTools tools, string workDir, double? singleFrameAt)
    {
        int target = singleFrameAt == null ? settings.Height : 180;
        var scale = Scale(info.Height ?? 2160, target);
        var dims = Info(path, scale, tools);
        var args = new List<string> { "--scale", scale };
        if (singleFrameAt is double t)
        {
            double fps = dims.Fps > 0 ? dims.Fps : 25;
            int start = Math.Min(Math.Max(0, dims.Frames - 1), (int)(t * fps));
            args.AddRange(["--start", start.ToString(CultureInfo.InvariantCulture), "--count", "1"]);
        }
        args.Add(path);
        return new FrameSource
        {
            InputArgs = ["-f", "rawvideo", "-pix_fmt", "rgb48le", "-s", $"{dims.Width}x{dims.Height}", "-r", info.FrameRate ?? "25", "-i", "-"],
            FeederExe = tools.BrawDecoder,
            FeederArgs = args,
            AudioFromInput = false,
        };
    }
}

// MARK: RED (REDline → ProRes LT intermediate)

internal sealed class R3dBackend : IRawBackend
{
    public ClipFormat Format => ClipFormat.R3d;
    public string MissingHint => "RED R3D: nainstalujte REDCINE-X PRO (REDline)";
    public bool IsAvailable(ExternalTools tools) => tools.Redline != null;

    /// `--printMeta 1` prints "Key: value" lines; the key names differ between versions, so match loosely.
    public static ClipInfo ParseMeta(string output)
    {
        var meta = new List<(string Key, string Value)>();
        foreach (var line in output.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var k = line[..colon].Trim().ToLowerInvariant();
            if (meta.Any(m => m.Key == k)) continue;
            meta.Add((k, line[(colon + 1)..].Trim()));
        }
        string? Find(params string[] needles)
        {
            foreach (var n in needles)
            {
                var hit = meta.FirstOrDefault(m => m.Key.Contains(n, StringComparison.Ordinal)).Value;
                if (!string.IsNullOrEmpty(hit)) return hit;
            }
            return null;
        }
        static int? Digits(string? v) => int.TryParse(new string((v ?? "").Where(char.IsAsciiDigit).ToArray()), out var n) ? n : null;

        var c = new ClipInfo { Codec = "REDCODE RAW" };
        c.Width = Digits(Find("frame width", "width"));
        c.Height = Digits(Find("frame height", "height"));
        var fpsText = Find("record fps", "timebase", "fps", "frame rate")?.Split(' ')[0];
        if (double.TryParse(fpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps > 0)
        {
            c.FrameRate = RawBackends.Rational(fps);
            if (Digits(Find("total frames", "frame count", "frames")) is int n)
            {
                c.Frames = n;
                c.Duration = n / fps;
            }
        }
        c.Timecode = Find("abs tc", "absolute timecode", "timecode", "edge tc");
        c.Camera = Find("camera model", "camera");
        return c;
    }

    public static List<string> Args(string input, string outDir, ClipInfo info, ProxySettings s)
    {
        int h = info.Height ?? 4320;
        var res = h / 4 >= s.Height ? "4" : (h / 2 >= s.Height ? "3" : "1");
        var args = new List<string> { "--i", input, "--outDir", outDir, "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", res, "--useMeta" };
        // The LUT expects log: IPP2, REDWideGamutRGB / Log3G10.
        if (s.LutPath != null) args.AddRange(["--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"]);
        return args;
    }

    public ClipInfo Probe(string path, ExternalTools tools, string workDir) =>
        ParseMeta(ToolProcess.Run(tools.Redline!, ["--i", path, "--printMeta", "1"]));

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, ExternalTools tools, string workDir, double? singleFrameAt)
    {
        // Thumbnails come from the finished proxy instead (one REDline run per clip is enough).
        if (singleFrameAt != null) throw new ToolError("REDline", -1, "thumbnail from proxy");
        var dir = RawBackends.FreshDir(workDir, "r3d");
        ToolProcess.Run(tools.Redline!, Args(path, dir, info, settings));
        var mov = RawBackends.NewestFile(dir, "mov") ?? throw new ToolError("REDline", -1, $"no output in {dir}");
        return new FrameSource { InputArgs = ["-i", mov], Cleanup = [dir] };
    }
}

// MARK: ARRIRAW (ARRI Reference Tool CLI → ProRes LT MXF intermediate)

internal sealed class ArriBackend : IRawBackend
{
    public ClipFormat Format => ClipFormat.Arriraw;
    public string MissingHint => "ARRIRAW: nainstalujte ARRI Reference Tool (art-cmd)";
    public bool IsAvailable(ExternalTools tools) => tools.ArtCmd != null;

    /// `art-cmd export` writes the clip's metadata as JSON: metadata sets per clip, plus one entry per frame.
    public static ClipInfo ParseMetadata(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) throw new ToolError("art-cmd", -1, "unreadable metadata");
        // Only the first frame matters (start timecode); the rest would just slow the walk down.
        if (root["frameBasedMetadata"] is JsonObject fb && fb["frames"] is JsonArray frames && frames.Count > 1)
            fb["frames"] = new JsonArray(frames[0]?.DeepClone());

        var flat = new List<(string Key, JsonValue Value)>();
        var seen = new HashSet<string>();
        void Walk(JsonNode? node, string prefix)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (k, v) in o) Walk(v, prefix + k + ".");
                    break;
                case JsonArray a:
                    foreach (var x in a) Walk(x, prefix);
                    break;
                case JsonValue v:
                    var key = prefix.Length > 0 ? prefix[..^1] : prefix;
                    if (seen.Add(key)) flat.Add((key, v));
                    break;
            }
        }
        Walk(root, "");

        JsonValue? Value(string suffix) => flat.FirstOrDefault(kv => kv.Key.EndsWith(suffix, StringComparison.Ordinal)).Value;
        string? Str(string suffix) => Value(suffix) is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
        int? Int(string suffix)
        {
            var v = Value(suffix);
            if (v == null) return null;
            if (v.GetValueKind() == JsonValueKind.Number) return (int)v.GetValue<double>();
            return int.TryParse(Str(suffix), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        }

        var c = new ClipInfo
        {
            Codec = Str("videoCodec") ?? "ARRIRAW",
            Width = Int("storedSize.width") ?? Int("displayRect.width"),
            Height = Int("storedSize.height") ?? Int("displayRect.height"),
            Frames = Int("clipDuration"),
            Timecode = Str("frames.timecode"),
            Camera = Str("cameraModel"),
            LogEncoding = Str("sceneColorEncodingTransferCurve"),
        };
        if (Str("nativePictureEssenceDescriptor.file.sampleRate") is string rate)
            c.FrameRate = rate.EndsWith("/1", StringComparison.Ordinal) ? rate[..^2] : rate;
        if (c.Frames is int n && Timecode.Fps(c.FrameRate) is double fps) c.Duration = n / fps;
        // Lens squeeze; ARRI writes it as a factor or in thousandths depending on the field.
        var squeeze = flat.FirstOrDefault(kv => kv.Key.Contains("squeeze", StringComparison.OrdinalIgnoreCase) && kv.Value.GetValueKind() == JsonValueKind.Number).Value;
        if (squeeze != null)
        {
            var v = squeeze.GetValue<double>();
            v = v > 10 ? v / 1000 : v;
            if (v > 1.01) c.Squeeze = v;
        }
        c.AudioChannels = flat.Any(kv => kv.Key.EndsWith("nativeSoundEssenceDescriptorList.blockAlign", StringComparison.Ordinal)) ? 2 : 0;
        return c;
    }

    /// The LUT decides which log it needs when its name says so; otherwise the clip's own log encoding
    /// from metadata (ALEXA 35 family → LogC4, older ALEXAs → LogC3).
    public static string ColorSpace(string? lut, ClipInfo info)
    {
        var name = Path.GetFileName(lut ?? "").ToLowerInvariant();
        if (name.Contains("logc4")) return "AWG4/D65/LogC4";
        if (name.Contains("logc3") || name.Contains("logc ") || name.Contains("logc_") || name.Contains("logc-")) return "AWG3/D65/LogC3";
        if (info.LogEncoding?.ToLowerInvariant() is string log) return log.Contains('4') ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3";
        return (info.Camera ?? "").ToLowerInvariant().Contains("35") ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3";
    }

    public static (List<string> Args, string? ParametersJson, bool Cropped) Args(string input, string output, ClipInfo c, ProxySettings s, string parametersFile)
    {
        var args = new List<string> { "process", "--input", input, "--video-codec", "prores422lt", "--deflip", "--output", output };
        string? json = null;
        double sq = c.Squeeze ?? 1;
        // Frame lines are defined on the desqueezed picture: compute the crop there, then map it back to sensor pixels.
        if (c.Width is int w && c.Height is int h && s.FrameCrop((int)(w * sq), h) is (int fx, int fy, int fw, int fh))
        {
            int cx = (int)(fx / sq) & ~1, cw = (int)(fw / sq) & ~1;
            int outH = s.Height & ~1;  // same height for every proxy, whatever the crop
            int outW = (int)Math.Round(outH * (double)cw * sq / fh / 2, MidpointRounding.AwayFromZero) * 2;
            json = new JsonObject
            {
                ["inputCrop"] = new JsonObject
                {
                    ["displayXOffsetOverride"] = cx, ["displayYOffsetOverride"] = fy,
                    ["displayWidthOverride"] = cw, ["displayHeightOverride"] = fh,
                },
                ["outputScaling"] = new JsonObject { ["targetWidth"] = outW, ["targetHeight"] = outH },
            }.ToJsonString();
            args.AddRange(["--parameters", parametersFile]);
        }
        else
        {
            args.AddRange(["--desqueeze", "--output-height", s.Height.ToString(CultureInfo.InvariantCulture)]);
        }
        if (s.LutPath != null) args.AddRange(["--target-colorspace", ColorSpace(s.LutPath, c)]);
        else args.AddRange(["--target-colorspace", "Rec.709/D65/BT.1886", "--embedded-look"]);
        return (args, json, json != null);
    }

    public ClipInfo Probe(string path, ExternalTools tools, string workDir)
    {
        var dir = RawBackends.FreshDir(workDir, "art-meta");
        try
        {
            var json = Path.Combine(dir, "meta.json");
            // art-cmd writes art.log into its working directory — never let that be the card or a destination.
            ToolProcess.Run(tools.ArtCmd!, ["export", "--input", path, "--output", json, "--skip-audio", "--skip-look"], dir);
            return ParseMetadata(File.ReadAllText(json));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, ExternalTools tools, string workDir, double? singleFrameAt)
    {
        if (singleFrameAt != null) throw new ToolError("art-cmd", -1, "thumbnail from proxy");
        var dir = RawBackends.FreshDir(workDir, "ari");
        var output = Path.Combine(dir, "intermediate.mxf");
        var parameters = Path.Combine(dir, "parameters.json");
        var (args, json, cropped) = Args(path, output, info, settings, parameters);
        if (json != null) File.WriteAllText(parameters, json);
        ToolProcess.Run(tools.ArtCmd!, args, dir);
        if (!File.Exists(output)) throw new ToolError("art-cmd", -1, "no output");
        return new FrameSource { InputArgs = ["-i", output], AlreadyCropped = cropped, Cleanup = [dir] };
    }
}

// MARK: Canon Cinema RAW Light — no command-line decoder exists

internal sealed class CanonRawBackend : IRawBackend
{
    public ClipFormat Format => ClipFormat.CanonRaw;
    public string MissingHint => "Canon Cinema RAW Light: pro Windows neexistuje dekodér pro příkazovou řádku — zkopírováno a ověřeno, proxy přeskočeno";
    public bool IsAvailable(ExternalTools tools) => false;
    public ClipInfo Probe(string path, ExternalTools tools, string workDir) => throw new ToolError("Canon RAW", -1, MissingHint);
    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, ExternalTools tools, string workDir, double? singleFrameAt) =>
        throw new ToolError("Canon RAW", -1, MissingHint);
}
```

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~RawBackendsTests"`
Expected: PASS (17 testů vč. řádků theory).

- [ ] **Step 6: Commit**

Řádek do VYVOJ: `- Úkol 5 — RAW backendy: Blackmagic RAW, RED (REDline), ARRIRAW (art-cmd), Canon RAW jen kopie; metadata a parametry testované bez nástrojů`.

```powershell
git add windows docs
git commit -m "Windows: RAW backends (BRAW, R3D, ARRIRAW, Canon) — metadata parsing and decoder arguments" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: ProxyRenderer — 3 snímky a render proxy

**Files:**
- Create: `windows/src/Offload.Core/ProxyRenderer.cs`
- Modify: `windows/src/Offload.Core/Media.cs` (record `Thumbnail`; `ClipEntry.Thumbnail` → `Thumbnails`)
- Modify: `windows/src/Offload.Core/HtmlReport.cs` (dočasně: v `AppendClips` použít první z `Thumbnails`, aby se build nerozbil; plné 3 snímky dělá Task 7)
- Modify: `windows/tests/Offload.Core.Tests/HtmlReportTests.cs` (`Thumbnail = [1, 2, 3]` → `Thumbnails = [new([1, 2, 3], "01:00:00:00")]`)
- Test: `windows/tests/Offload.Core.Tests/ProxyRendererTests.cs`

**Interfaces:**
- Consumes: Tasks 1–5, `Layout.Join`, `NativeIO.Mkdirs/Move/Delete`.
- Produces:
  - `public sealed record Thumbnail(byte[] Jpeg, string Label)`; `ClipEntry.Thumbnails` (`List<Thumbnail>`, `[JsonIgnore]`, default prázdný).
  - `ProxyRenderer(ProxySettings settings, ExternalTools tools) : IDisposable` — `Settings`, `Tools`, `UnsupportedReason(Clip) -> string?`, `Probe(Clip, string cardPath) -> ClipInfo`, `ReclassifyUndecodableMxf(Clip, string cardPath) -> (Clip Clip, ClipInfo Info)?`, `Thumbnails(Clip, string cardPath, ClipInfo, bool applyLut = true) -> List<Thumbnail>`, `Render(Clip, string cardPath, ClipInfo, string output, Action<double> progress, Func<bool> isCancelled)`; internal static `FramePositions(ClipInfo) -> IReadOnlyList<FramePosition>`, `Label(ClipInfo, FramePosition) -> string`; internal `record FramePosition(double Seconds, long Frame, bool FromEnd)`.

- [ ] **Step 1: Model náhledů**

V `windows/src/Offload.Core/Media.cs` nahradit v `ClipEntry` řádky

```csharp
    /// JPEG; embedded in the HTML report only.
    [JsonIgnore] public byte[]? Thumbnail { get; set; }
```

za

```csharp
    /// First, middle and last frame (JPEG + caption); embedded in the HTML/PDF report only.
    [JsonIgnore] public List<Thumbnail> Thumbnails { get; set; } = [];
```

a na konec souboru přidat:

```csharp
/// One report thumbnail: a small JPEG and its caption (timecode, or frame number when the clip has no TC).
public sealed record Thumbnail(byte[] Jpeg, string Label);
```

V `HtmlReport.AppendClips` nahradit výraz pro `img`:

```csharp
            var img = c.Thumbnails.Count > 0
                ? $"<img class=\"thumb\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(c.Thumbnails[0].Jpeg)}\" alt=\"\">"
                : "<div class=\"nothumb\"></div>";
```

V `HtmlReportTests.Clips_RenderRowWithThumbnailAndProxyStatus` nahradit `Thumbnail = [1, 2, 3],` za `Thumbnails = [new([1, 2, 3], "01:00:00:00")],`.

Run: `dotnet test` → PASS (jen refaktor).

- [ ] **Step 2: Napsat padající testy**

`windows/tests/Offload.Core.Tests/ProxyRendererTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class ProxyRendererTests
{
    static ExternalTools Tools => new ExternalTools().Resolved();
    static Clip VideoClip(string rel) => new() { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 1 };

    [Fact]
    public void FramePositions_FirstMiddleLast()
    {
        var pos = ProxyRenderer.FramePositions(new ClipInfo { FrameRate = "25/1", Duration = 4, Frames = 100 });
        Assert.Equal([new FramePosition(0, 0, false), new FramePosition(2, 50, false), new FramePosition(3.96, 99, true)], pos);
        var info = new ClipInfo { FrameRate = "25/1", Duration = 4, Frames = 100, Timecode = "01:00:00:00" };
        Assert.Equal(["01:00:00:00", "01:00:02:00", "01:00:03:24"], ProxyRenderer.FramePositions(info).Select(p => ProxyRenderer.Label(info, p)));
        var noTc = new ClipInfo { FrameRate = "25/1", Duration = 4, Frames = 100 };
        Assert.Equal("#50", ProxyRenderer.Label(noTc, ProxyRenderer.FramePositions(noTc)[1]));
    }

    [Fact]
    public void FramePositions_UnknownLengthOrSingleFrame_OnlyFirst()
    {
        Assert.Single(ProxyRenderer.FramePositions(new ClipInfo()));
        Assert.Single(ProxyRenderer.FramePositions(new ClipInfo { FrameRate = "25/1", Duration = 0.04, Frames = 1 }));
    }

    [Fact]
    public void UnsupportedReason()
    {
        using var none = new ProxyRenderer(new ProxySettings(), new ExternalTools { NoSearch = true }.Resolved());
        Assert.Equal("ffmpeg not found", none.UnsupportedReason(VideoClip("a.mov")));
        var ff = new ExternalTools { Ffmpeg = @"C:\Windows\System32\cmd.exe", NoSearch = true }.Resolved();
        using var some = new ProxyRenderer(new ProxySettings(), ff);
        Assert.Null(some.UnsupportedReason(VideoClip("a.mov")));
        Assert.StartsWith("RED R3D", some.UnsupportedReason(new Clip { Name = "r", Path = "r.R3D", Format = ClipFormat.R3d, Segments = ["r.R3D"] }));
        Assert.StartsWith("Canon", some.UnsupportedReason(new Clip { Name = "c", Path = "c.CRM", Format = ClipFormat.CanonRaw, Segments = ["c.CRM"] }));
        Assert.Equal("unknown format", some.UnsupportedReason(new Clip { Name = "u", Path = "u", Format = ClipFormat.Unknown, Segments = ["u"] }));
    }

    [FfmpegFact]
    public void Thumbnails_ThreeJpegsWithTimecodeLabels()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "C0001.mov"), seconds: 4, timecode: "01:00:00:00");
        TestMedia.IdentityLut(Path.Combine(dir.Path, "look.cube"));
        using var r = new ProxyRenderer(new ProxySettings { LutPath = Path.Combine(dir.Path, "look.cube") }, Tools);
        var clip = VideoClip("CLIP/C0001.mov");
        var info = r.Probe(clip, card);
        var thumbs = r.Thumbnails(clip, card, info);
        Assert.Equal(3, thumbs.Count);
        Assert.All(thumbs, t => Assert.Equal(new byte[] { 0xFF, 0xD8 }, t.Jpeg[..2]));
        Assert.Equal("01:00:00:00", thumbs[0].Label);
        Assert.Equal("01:00:03:24", thumbs[2].Label);
    }

    // Review focus 5
    [FfmpegFact]
    public void Thumbnails_SingleFrameClip()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "one.mov"), seconds: 0.04, audio: false);
        using var r = new ProxyRenderer(new ProxySettings(), Tools);
        var clip = VideoClip("one.mov");
        var thumbs = r.Thumbnails(clip, card, r.Probe(clip, card));
        Assert.NotEmpty(thumbs);
    }

    // Review focus 1
    [FfmpegFact]
    public void Render_NameWithQuotesAndDiacritics()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var rel = "Záběr 'A' 01.mov";
        TestMedia.MakeClip(Path.Combine(card, rel), seconds: 2, timecode: "10:00:00:00");
        TestMedia.IdentityLut(Path.Combine(dir.Path, "look.cube"));
        var settings = new ProxySettings { Enabled = true, Height = 360, FrameAspect = 1.85, LutPath = Path.Combine(dir.Path, "look.cube") };
        using var r = new ProxyRenderer(settings, Tools);
        var clip = VideoClip(rel);
        var info = r.Probe(clip, card);
        var output = Path.Combine(card, "_PROXY", "Záběr 'A' 01.mov");
        var progress = new List<double>();
        r.Render(clip, card, info, output, f => { lock (progress) progress.Add(f); }, () => false);
        var p = FFProbe.Probe(TestMedia.Ffprobe!, output);
        Assert.Equal(360, p.Height);
        Assert.StartsWith("h264", p.Codec);
        Assert.Equal("10:00:00:00", p.Timecode);
        Assert.True(p.AudioChannels > 0);
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_PROXY"), "*.part"));
        lock (progress) Assert.NotEmpty(progress);
    }

    [FfmpegFact]
    public void Render_ProresProxy()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "a.mov"), seconds: 1);
        using var r = new ProxyRenderer(new ProxySettings { Height = 360, Codec = ProxyCodec.ProresProxy }, Tools);
        var clip = VideoClip("a.mov");
        var output = Path.Combine(card, "_PROXY", "a.mov");
        r.Render(clip, card, r.Probe(clip, card), output, _ => { }, () => false);
        Assert.Equal("prores", FFProbe.Probe(TestMedia.Ffprobe!, output).Codec!.Split(' ')[0]);
    }

    [FfmpegFact]
    public void Render_Cancelled_LeavesNothing()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestMedia.MakeClip(Path.Combine(card, "long.mov"), seconds: 20, width: 1920, height: 1080);
        using var r = new ProxyRenderer(new ProxySettings { Height = 1080, Codec = ProxyCodec.Prores422HQ }, Tools);
        var clip = VideoClip("long.mov");
        var output = Path.Combine(card, "_PROXY", "long.mov");
        bool cancel = false;
        var e = Assert.Throws<ToolError>(() => r.Render(clip, card, r.Probe(clip, card), output, _ => cancel = true, () => cancel));
        Assert.Contains("cancelled", e.Message);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "_PROXY")));
    }
}
```

- [ ] **Step 3: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~ProxyRendererTests"`
Expected: build FAIL — `ProxyRenderer`, `FramePosition` neexistují.

- [ ] **Step 4: Implementace**

`windows/src/Offload.Core/ProxyRenderer.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Offload.Core;

internal sealed record FramePosition(double Seconds, long Frame, bool FromEnd);

/// Probes clips, makes the report thumbnails (first, middle, last frame) and renders proxies.
/// Works in its own temp folder; ffmpeg runs there so filter graphs only use relative names.
public sealed class ProxyRenderer : IDisposable
{
    static readonly string[] FontCandidates = [@"C:\Windows\Fonts\consola.ttf", @"C:\Windows\Fonts\cour.ttf"];

    readonly Dictionary<ClipFormat, IRawBackend> backends = RawBackends.All.ToDictionary(b => b.Format);
    readonly string workDir;
    readonly bool hasLut;
    readonly bool hasFont;

    public ProxySettings Settings { get; }
    public ExternalTools Tools { get; }

    public ProxyRenderer(ProxySettings settings, ExternalTools tools)
    {
        Settings = settings;
        Tools = tools;
        workDir = Path.Combine(Path.GetTempPath(), "offload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        if (!string.IsNullOrEmpty(settings.LutPath) && File.Exists(settings.LutPath))
        {
            File.Copy(settings.LutPath, Path.Combine(workDir, FilterGraph.LutFile));
            hasLut = true;
        }
        if (FontCandidates.FirstOrDefault(File.Exists) is string font)
        {
            File.Copy(font, Path.Combine(workDir, FilterGraph.FontFile));
            hasFont = true;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(workDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// null when the clip can be rendered, otherwise the reason it cannot.
    public string? UnsupportedReason(Clip clip)
    {
        if (Tools.Ffmpeg == null) return "ffmpeg not found";
        return clip.Format switch
        {
            ClipFormat.Video => null,
            ClipFormat.Unknown => "unknown format",
            _ => backends.TryGetValue(clip.Format, out var b)
                ? (b.IsAvailable(Tools) ? null : b.MissingHint)
                : $"{clip.Format.Label()}: no decoder available",
        };
    }

    public ClipInfo Probe(Clip clip, string cardPath)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
        {
            var ffprobe = Tools.Ffprobe ?? throw new ToolError("ffprobe", -1, "ffprobe not found");
            return FFProbe.Probe(ffprobe, path);
        }
        if (!backends.TryGetValue(clip.Format, out var b) || !b.IsAvailable(Tools))
            throw new ToolError(clip.Format.Label(), -1, UnsupportedReason(clip) ?? "");
        return b.Probe(path, Tools, workDir);
    }

    /// MXF is a container for many codecs; ARRIRAW/ARRICORE inside it only the ARRI tool can read.
    public (Clip Clip, ClipInfo Info)? ReclassifyUndecodableMxf(Clip clip, string cardPath)
    {
        if (clip.Format != ClipFormat.Video || !clip.Path.EndsWith(".mxf", StringComparison.OrdinalIgnoreCase)) return null;
        var arri = backends[ClipFormat.Arriraw];
        if (!arri.IsAvailable(Tools)) return null;
        try
        {
            var info = arri.Probe(Layout.Join(cardPath, clip.Path), Tools, workDir);
            if (info.Width == null) return null;
            return (new Clip { Name = clip.Name, Path = clip.Path, Format = ClipFormat.Arriraw, Segments = clip.Segments, Bytes = clip.Bytes, IsCameraProxy = clip.IsCameraProxy }, info);
        }
        catch (ToolError)
        {
            return null;
        }
    }

    internal static IReadOnlyList<FramePosition> FramePositions(ClipInfo info)
    {
        var first = new FramePosition(0, 0, false);
        if (info.Duration is not double duration || duration <= 0 || Timecode.Fps(info.FrameRate) is not double fps) return [first];
        long frames = info.Frames ?? (long)Math.Round(duration * fps);
        if (frames <= 1) return [first];
        long middle = frames / 2, last = frames - 1;
        return [first, new FramePosition(middle / fps, middle, false), new FramePosition(last / fps, last, true)];
    }

    internal static string Label(ClipInfo info, FramePosition p) =>
        Timecode.IsValid(info.Timecode) && Timecode.Fps(info.FrameRate) is double fps
            ? Timecode.Add(info.Timecode!, p.Frame, fps)
            : $"#{p.Frame}";

    FrameSource SourceFor(Clip clip, string cardPath, ClipInfo info, double? singleFrameAt, bool forRender)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
            return new FrameSource { InputArgs = forRender ? ["-hwaccel", "auto", "-i", path] : ["-i", path] };
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, "no decoder");
        return b.Source(path, info, Settings, Tools, workDir, singleFrameAt);
    }

    /// First, middle and last frame as small JPEGs with the LUT applied, so log footage is readable.
    public List<Thumbnail> Thumbnails(Clip clip, string cardPath, ClipInfo info, bool applyLut = true)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        var vf = FilterGraph.Thumbnail(hasLut && applyLut);
        var result = new List<Thumbnail>();
        foreach (var pos in FramePositions(info))
        {
            var src = SourceFor(clip, cardPath, info, pos.Seconds, forRender: false);
            var output = Path.Combine(workDir, $"thumb-{Guid.NewGuid():N}.jpg");
            try
            {
                bool video = clip.Format == ClipFormat.Video;
                var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
                if (src.FeederExe == null) args.Add("-nostdin");
                if (video) args.AddRange(pos.FromEnd ? ["-sseof", "-1"] : ["-ss", pos.Seconds.ToString("0.000", CultureInfo.InvariantCulture)]);
                args.AddRange(src.InputArgs);
                // From the end: decode the last second and keep overwriting the image — the final write is the last frame.
                args.AddRange(video && pos.FromEnd ? ["-vf", vf, "-update", "1"] : ["-frames:v", "1", "-vf", vf]);
                args.AddRange(["-q:v", "5", output]);
                RunFfmpeg(ffmpeg, args, src, null, null, () => false);
                result.Add(new Thumbnail(File.ReadAllBytes(output), Label(info, pos)));
            }
            finally
            {
                src.RemoveIntermediates();
                NativeIO.Delete(output);
            }
        }
        return result;
    }

    public void Render(Clip clip, string cardPath, ClipInfo info, string output, Action<double> progress, Func<bool> isCancelled)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        var src = SourceFor(clip, cardPath, info, null, forRender: true);
        string? label = null;
        try
        {
            int outHeight = Math.Min(Settings.Height, info.Height ?? Settings.Height) & ~1;
            if (Settings.BurnFilename && hasFont)
            {
                label = $"label-{Guid.NewGuid().ToString("N")[..8]}.txt";
                File.WriteAllText(Path.Combine(workDir, label), clip.Name, new UTF8Encoding(false));
            }
            var vf = FilterGraph.Proxy(Settings, outHeight, src.AlreadyCropped, hasLut, hasFont, label, info.Timecode, info.FrameRate);

            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-nostats", "-progress", "pipe:1" };
            if (src.FeederExe == null) args.Add("-nostdin");
            args.AddRange(src.InputArgs);
            args.AddRange(["-map", "0:v:0"]);
            if (Settings.Audio && info.AudioChannels > 0 && src.AudioFromInput)
            {
                args.AddRange(["-map", "0:a:0?"]);
                args.AddRange(Encoders.AudioArgs(Settings.Codec));
            }
            args.AddRange(["-vf", vf]);
            args.AddRange(Encoders.VideoArgs(Encoders.Pick(ffmpeg, Settings.Codec), Settings.Codec, Settings.BitrateMbps));
            if (Timecode.IsValid(info.Timecode)) args.AddRange(["-timecode", info.Timecode!]);

            var dir = Path.GetDirectoryName(output)!;
            NativeIO.Mkdirs(dir);
            var part = Path.Combine(dir, "." + Path.GetFileName(output) + ".part");
            args.AddRange(["-f", "mov", part]);
            try
            {
                RunFfmpeg(ffmpeg, args, src, info.Duration, progress, isCancelled);
                NativeIO.Move(part, output, replace: true);
            }
            catch
            {
                NativeIO.Delete(part);
                throw;
            }
        }
        finally
        {
            src.RemoveIntermediates();
            if (label != null) NativeIO.Delete(Path.Combine(workDir, label));
        }
    }

    void RunFfmpeg(string ffmpeg, List<string> args, FrameSource src, double? duration, Action<double>? progress, Func<bool> isCancelled)
    {
        using var ff = new ToolProcess(ffmpeg, args, workDir, pipeStdin: src.FeederExe != null);
        using var feeder = src.FeederExe != null ? new ToolProcess(src.FeederExe, src.FeederArgs, workDir, pipeStdin: false) : null;
        Thread? pump = null;
        feeder?.Start(null);
        ff.Start(line =>
        {
            if (progress == null || duration is not double d || d <= 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
            if (double.TryParse(line["out_time_us=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var us))
                progress(Math.Min(1, us / 1_000_000 / d));
        });
        if (feeder != null)
        {
            pump = new Thread(() =>
            {
                try { feeder.Stdout.CopyTo(ff.Stdin, 4 << 20); }
                catch (IOException) { /* ffmpeg closed its input (done or cancelled) */ }
                finally { try { ff.Stdin.Close(); } catch (IOException) { } }
            }) { IsBackground = true };
            pump.Start();
        }
        while (!ff.WaitForExit(200))
        {
            if (!isCancelled()) continue;
            ff.Kill();
            feeder?.Kill();
            ff.WaitForExit();
            throw new ToolError("ffmpeg", -1, "cancelled");
        }
        ff.WaitForExit();
        if (feeder != null)
        {
            if (!feeder.WaitForExit(2000)) feeder.Kill();
            pump!.Join(2000);
        }
        if (ff.ExitCode != 0) throw new ToolError("ffmpeg", ff.ExitCode, ff.StderrTail);
        if (feeder != null && feeder.WaitForExit(0) && feeder.ExitCode is int code && code != 0 && code != -1)
            throw new ToolError(feeder.Name, code, feeder.StderrTail);
    }
}
```

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~ProxyRendererTests"`
Expected: PASS (8 testů; FfmpegFact testy na tomto PC běží a render jde přes `h264_nvenc`).

- [ ] **Step 6: Commit**

Řádek do VYVOJ: `- Úkol 6 — renderer: 3 snímky na klip (první, prostřední, poslední) s popiskem TC, render proxy s LUT a burn-in, zrušení bez zbytků`.

```powershell
git add windows docs
git commit -m "Windows: proxy renderer — three thumbnails per clip and proxy rendering" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: HTML report se třemi snímky

**Files:**
- Modify: `windows/src/Offload.Core/HtmlReport.cs`
- Test: `windows/tests/Offload.Core.Tests/HtmlReportTests.cs`

**Interfaces:**
- Consumes: `ClipEntry.Thumbnails` (Task 6).
- Produces: v řádku klipu blok `<div class="thumbs">` se třemi `<figure>` (img + `<figcaption>` s popiskem); tiskové CSS s `@page{size:A4 landscape;margin:28pt}`.

- [ ] **Step 1: Upravit test (padající)**

V `HtmlReportTests.Clips_RenderRowWithThumbnailAndProxyStatus` nahradit `Thumbnails = [new([1, 2, 3], "01:00:00:00")],` za

```csharp
            Thumbnails = [new([1, 2, 3], "01:00:00:00"), new([4, 5, 6], "01:00:06:00"), new([7, 8, 9], "01:00:11:24")],
```

a místo `Assert.Contains("src=\"data:image/jpeg;base64,AQID\"", html);` dát:

```csharp
        Assert.Contains("<div class=\"thumbs\"><figure><img class=\"thumb\" src=\"data:image/jpeg;base64,AQID\" alt=\"\"><figcaption>01:00:00:00</figcaption></figure>", html);
        Assert.Contains("base64,BAUG\" alt=\"\"><figcaption>01:00:06:00</figcaption>", html);
        Assert.Contains("base64,BwgJ\" alt=\"\"><figcaption>01:00:11:24</figcaption></figure></div>", html);
```

Přidat test:

```csharp
    [Fact]
    public void Print_HasA4LandscapePage() =>
        Assert.Contains("@page{size:A4 landscape;margin:28pt}", HtmlReport.Render(Reports.Sample(Card), forPrint: true));
```

Run: `dotnet test --filter "FullyQualifiedName~HtmlReportTests"` → FAIL (thumbs/@page chybí).

- [ ] **Step 2: Implementace**

V `HtmlReport.cs`:
- `PrintCss` začít `"@page{size:A4 landscape;margin:28pt}"` (zbytek beze změny) a na konec přidat `".thumbs figure img{width:96px;height:54px}"`.
- do `BaseCss` za řádek `.nothumb{…}` přidat řádek:
  `".thumbs{display:flex;gap:4px}.thumbs figure{margin:0}.thumbs figcaption{font:10px ui-monospace,Consolas,monospace;color:var(--muted);text-align:center}\n" +`
- v `AppendClips` výraz `img`:

```csharp
            var img = c.Thumbnails.Count > 0
                ? "<div class=\"thumbs\">" + string.Concat(c.Thumbnails.Select(t =>
                    $"<figure><img class=\"thumb\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(t.Jpeg)}\" alt=\"\"><figcaption>{Esc(t.Label)}</figcaption></figure>")) + "</div>"
                : "<div class=\"nothumb\"></div>";
```

- [ ] **Step 3: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~HtmlReportTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

Řádek do VYVOJ: `- Úkol 7 — HTML report: tři snímky na klip s popisky TC, stránka A4 na šířku pro tisk`.

```powershell
git add windows docs
git commit -m "Windows: HTML report shows first, middle and last frame per clip" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: PDF přes Edge

**Files:**
- Create: `windows/src/Offload.Core/PdfReport.cs`
- Modify: `windows/tests/Offload.Core.Tests/TestMedia.cs` (přidat `EdgeFactAttribute` z Task 2 Step 1)
- Test: `windows/tests/Offload.Core.Tests/PdfReportTests.cs`

**Interfaces:**
- Consumes: `HtmlReport.Render(report, forPrint: true)`, `ToolProcess`, `ToolError`, `NativeIO.Move/Delete`.
- Produces: `PdfReport.FindEdge() -> string?`, `PdfReport.Write(OffloadReport report, string path, int timeoutSeconds = 60)` (vyhazuje `ToolError`, když Edge chybí nebo tisk selže).

- [ ] **Step 1: EdgeFact**

Do `TestMedia.cs` přidat třídu `EdgeFactAttribute` přesně podle Task 2 Step 1.

- [ ] **Step 2: Napsat padající testy**

`windows/tests/Offload.Core.Tests/PdfReportTests.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core.Tests;

public class PdfReportTests
{
    [EdgeFact]
    public void Write_A4LandscapePdf_EvenForLargeReports()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-05", "A001");
        var report = Reports.Sample(card);
        var rnd = new Random(1);
        for (int i = 0; i < 40; i++)
        {
            var jpeg = new byte[20_000];
            rnd.NextBytes(jpeg);
            report.Clips.Add(new ClipEntry
            {
                Clip = new Clip { Name = $"C{i:000}", Path = $"CLIP/C{i:000}.MP4", Format = ClipFormat.Video, Segments = [$"CLIP/C{i:000}.MP4"], Bytes = 1 },
                Thumbnails = [new(jpeg, "01:00:00:00"), new(jpeg, "01:00:01:00"), new(jpeg, "01:00:02:00")],
            });
        }
        Assert.True(HtmlReport.Render(report, forPrint: true).Length > 2_000_000);
        var pdf = Path.Combine(card, "_REPORT", "A001_report.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);

        PdfReport.Write(report, pdf);

        var bytes = File.ReadAllBytes(pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        var box = Regex.Match(Encoding.Latin1.GetString(bytes), @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)");
        Assert.True(box.Success);
        Assert.InRange(double.Parse(box.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 840, 844);
        Assert.InRange(double.Parse(box.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 593, 597);
        Assert.Equal(["A001_report.pdf"], Directory.GetFiles(Path.GetDirectoryName(pdf)!).Select(Path.GetFileName));
    }

    [Fact]
    public void FindEdge_ReturnsExistingFileOrNull()
    {
        var edge = PdfReport.FindEdge();
        Assert.True(edge == null || File.Exists(edge));
    }
}
```

- [ ] **Step 3: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~PdfReportTests"`
Expected: build FAIL — `PdfReport` neexistuje.

- [ ] **Step 4: Implementace**

`windows/src/Offload.Core/PdfReport.cs`:

```csharp
using System.Text;

namespace Offload.Core;

/// Prints the HTML report into an A4 landscape PDF with Microsoft Edge in headless mode — the same engine
/// as WebView2, present on every Windows 10/11, no window and no extra package. Page size and margins come
/// from the report's @page rule.
public static class PdfReport
{
    public static string? FindEdge()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void Write(OffloadReport report, string path, int timeoutSeconds = 60)
    {
        var edge = FindEdge() ?? throw new ToolError("Edge", -1, "Microsoft Edge not found — PDF skipped");
        var work = Path.Combine(Path.GetTempPath(), "offload-pdf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".tmp");
        try
        {
            // A file, not a data URL: the report with thumbnails is easily several MB.
            var html = Path.Combine(work, "report.html");
            File.WriteAllText(html, HtmlReport.Render(report, forPrint: true), new UTF8Encoding(false));
            NativeIO.Delete(tmp);
            using var p = new ToolProcess(edge,
            [
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                $"--user-data-dir={Path.Combine(work, "profile")}",  // never touches the user's own Edge profile
                "--no-pdf-header-footer", $"--print-to-pdf={tmp}", new Uri(html).AbsoluteUri,
            ], work, pipeStdin: false);
            p.Start(_ => { });
            if (!p.WaitForExit(timeoutSeconds * 1000))
            {
                p.Kill();
                throw new ToolError("Edge", -1, $"PDF: report did not print in {timeoutSeconds} s");
            }
            var ok = File.Exists(tmp) && new FileInfo(tmp).Length > 4 && File.ReadAllBytes(tmp)[..4] is [0x25, 0x50, 0x44, 0x46];
            if (!ok) throw new ToolError("Edge", p.ExitCode, "PDF: printing did not produce a file\n" + p.StderrTail);
            NativeIO.Move(tmp, path, replace: true);
        }
        finally
        {
            NativeIO.Delete(tmp);
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
```

- [ ] **Step 5: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~PdfReportTests"`
Expected: PASS (2 testy; Edge je na tomto PC).

- [ ] **Step 6: Commit**

Řádek do VYVOJ: `- Úkol 8 — PDF report přes Microsoft Edge (headless), A4 na šířku, funguje i z příkazové řádky`.

```powershell
git add windows docs
git commit -m "Windows: PDF report via headless Edge" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Stage 2 v OffloadJob

**Files:**
- Modify: `windows/src/Offload.Core/OffloadJob.cs`
- Test: `windows/tests/Offload.Core.Tests/StageTwoTests.cs`

**Interfaces:**
- Consumes: `ClipDetector`, `ProxyRenderer`, `PdfReport`, `OffloadReport.Write`, `Layout`, `NativeIO`.
- Produces: `JobConfig.Tools` (`ExternalTools`, default `new()`); `OffloadJob.Run(Action<string>? log = null) -> OffloadReport?`; `RunCopy(Action<string>? log = null) -> bool` (true = má následovat stage 2); `RunPost(Action<string>? log = null)`. Stav během stage 2: `Phase` Analyzing/Proxies, `ClipIndex`, `ClipCount`, `ClipName`, `ClipFraction`, `ProxiesDone`, `ProxiesFailed`.

- [ ] **Step 1: Napsat padající testy**

`windows/tests/Offload.Core.Tests/StageTwoTests.cs`:

```csharp
namespace Offload.Core.Tests;

public class StageTwoTests
{
    static OffloadOptions Options() => new() { ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    static string SonyCard(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "CLIP", "C0001.MP4"), seconds: 2, timecode: "01:00:00:00");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "CLIP", "C0002.MP4"), seconds: 2, timecode: "01:00:10:00");
        TestMedia.MakeClip(Path.Combine(card, "PRIVATE", "M4ROOT", "SUB", "C0001S03.MP4"), seconds: 2, width: 640, height: 360);
        File.WriteAllText(Path.Combine(card, "PRIVATE", "M4ROOT", "MEDIAPRO.XML"), "<x/>");
        return card;
    }

    [FfmpegFact]
    public void Proxies_ThumbnailsAndPdf_OnEveryDestination()
    {
        using var dir = new TempDir();
        var card = SonyCard(dir);
        var config = new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-05",
            Proxy = new ProxySettings { Enabled = true, Height = 360 },
        };
        var job = new OffloadJob(config, Options());

        var report = job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.Equal(2, s.ProxiesDone);
        Assert.Equal(0, s.ProxiesFailed);
        Assert.Equal(3, report!.Clips.Count);
        Assert.All(report.Clips, c => Assert.Equal(3, c.Thumbnails.Count));
        var sub = report.Clips.Single(c => c.Clip.IsCameraProxy);
        Assert.Equal(ProxyStatus.Skipped, sub.ProxyStatus);
        foreach (var cardPath in job.Plan!.CardPaths)
        {
            Assert.True(File.Exists(Path.Combine(cardPath, "_PROXY", "C0001.mov")));
            Assert.True(File.Exists(Path.Combine(cardPath, "_PROXY", "C0002.mov")));
            var html = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "A001_report.html"));
            Assert.Contains("<figcaption>01:00:10:00</figcaption>", html);
            if (PdfReport.FindEdge() != null) Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "A001_report.pdf")));
        }
        // The hash list only covers the card's files, not proxies.
        Assert.DoesNotContain("_PROXY", File.ReadAllText(Path.Combine(job.Plan.CardPaths[0], "_REPORT", "A001.xxh64")));
    }

    [FfmpegFact]
    public void AnalyzeOnly_NoProxies()
    {
        using var dir = new TempDir();
        var card = SonyCard(dir);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-05" }, Options());
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Equal(3, report!.Clips.Count);
        Assert.All(report.Clips, c => Assert.Equal(ProxyStatus.None, c.ProxyStatus));
        Assert.False(Directory.Exists(Path.Combine(job.Plan!.CardPaths[0], "_PROXY")));
    }

    [Fact]
    public void NothingToAnalyze_StillDone()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        File.WriteAllText(Path.Combine(card, "notes.txt"), "x");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { AnalyzeClips = false }, Options());
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Empty(report!.Clips);
    }

    // Review focus 2
    [Fact]
    public void NoTools_OffloadStillSafe()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "C0001.MP4"), 50_000, 1);
        var config = new JobConfig(card, [dir.Sub("d1")])
        {
            Proxy = new ProxySettings { Enabled = true },
            Tools = new ExternalTools { NoSearch = true },
        };
        var job = new OffloadJob(config, Options());
        var report = job.Run();
        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        var clip = Assert.Single(report!.Clips);
        Assert.Contains("ffprobe not found", clip.ProbeError);
        Assert.Equal(ProxyStatus.Unsupported, clip.ProxyStatus);
        Assert.Equal("ffmpeg not found", clip.ProxyNote);
    }

    // Review focus 3
    [FfmpegFact]
    public void CorruptClip_OthersContinue()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "good.mov"), seconds: 1);
        TestFs.WriteRandom(Path.Combine(card, "bad.mov"), 100_000, 7);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Proxy = new ProxySettings { Enabled = true, Height = 360 } }, Options());
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var bad = report!.Clips.Single(c => c.Clip.Name == "bad");
        Assert.NotNull(bad.ProbeError);
        Assert.Equal(ProxyStatus.Failed, bad.ProxyStatus);
        var good = report.Clips.Single(c => c.Clip.Name == "good");
        Assert.Equal(ProxyStatus.Done, good.ProxyStatus);
        Assert.Equal(1, job.Status().ProxiesFailed);
    }

    // Review focus 4
    [FfmpegFact]
    public async Task CancelDuringProxies()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "long.mov"), seconds: 30, width: 1920, height: 1080);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")])
        {
            Proxy = new ProxySettings { Enabled = true, Height = 1080, Codec = ProxyCodec.Prores422HQ },
        }, Options());
        var run = Task.Run(() => job.Run());
        while (job.Status().Phase != JobPhase.Proxies && !run.IsCompleted) await Task.Delay(20);
        await Task.Delay(300);
        job.Cancel();
        await run;
        var s = job.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.True(s.SafeToEject);
        var proxyDir = Path.Combine(job.Plan!.CardPaths[0], "_PROXY");
        Assert.True(!Directory.Exists(proxyDir) || Directory.GetFiles(proxyDir, "*.part").Length == 0);
    }
}
```

- [ ] **Step 2: Spustit testy, ověřit selhání**

Run: `dotnet test --filter "FullyQualifiedName~StageTwoTests"`
Expected: build FAIL — `JobConfig.Tools`, `Run(log)` neexistují.

- [ ] **Step 3: Implementace**

V `windows/src/Offload.Core/OffloadJob.cs`:

1. Do `JobConfig` přidat: `public ExternalTools Tools { get; set; } = new();` a komentář u `AnalyzeClips` změnit na `/// Probe clips and put thumbnails + metadata into the report (stage 2).`
2. Komentář třídy: `/// One card: copy → verify → report → clip analysis → optional proxies → PDF. Used by both the CLI and the app.`
3. Nahradit `Run` a `RunCopy` a přidat zbytek (`using System.Diagnostics;` nahoru):

```csharp
    /// Runs the whole job synchronously. Never throws: the outcome is in Status() and the returned report.
    public OffloadReport? Run(Action<string>? log = null)
    {
        if (RunCopy(log)) RunPost(log);
        return Report;
    }

    /// Stage 1 — card → disks + verification. Returns true when stage 2 should follow.
    /// After this the card is no longer needed, so the app can start the next card while stage 2 runs.
    public bool RunCopy(Action<string>? log = null)
    {
        using var awake = NativeIO.KeepAwake();
        try
        {
            var plan = Prepare();
            Update(s => s with { Phase = JobPhase.Copying });
            var report = Engine.Run(plan);
            Report = report;
            Update(s => s with { SafeToEject = report.SafeToEject, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                Update(s => s with { Phase = JobPhase.Cancelled });
                return false;
            }
            if (!Config.AnalyzeClips && !Config.Proxy.Enabled)
            {
                WritePdfReports(report, plan, log);
                Update(s => s with { Phase = JobPhase.Done });
                return false;
            }
            Update(s => s with { Phase = JobPhase.Analyzing });
            return true;
        }
        catch (OperationCanceledException)
        {
            Update(s => s with { Phase = JobPhase.Cancelled });
            return false;
        }
        catch (Exception e)
        {
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
            return false;
        }
    }

    /// Stage 2 — clip analysis, thumbnails, optional proxies, PDF. Reads only from the destinations.
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it).
    public void RunPost(Action<string>? log = null)
    {
        if (Plan is not { } plan || Report is not { } report) return;
        using var awake = NativeIO.KeepAwake();
        try
        {
            var final = AnalyzeAndRender(plan, report, log);
            Report = final;
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        catch (Exception e)
        {
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
        }
    }

    OffloadReport AnalyzeAndRender(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        var clips = ClipDetector.Detect(plan.Scan.Files);
        if (clips.Count == 0) return report;
        using var renderer = new ProxyRenderer(Config.Proxy, Config.Tools.Resolved());
        var cardPaths = plan.CardPaths;
        var fileIndex = report.Files.Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i);
        bool Good(string state) => state == "verified" || (!report.Verified && state == "written");
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card.
        int? GoodDest(Clip clip)
        {
            for (int d = 0; d < cardPaths.Count; d++)
                if (clip.Segments.All(seg => fileIndex.TryGetValue(seg, out var fi) && Good(report.Files[fi].Destinations[d]))) return d;
            return null;
        }

        var entries = clips.Select(c => new ClipEntry { Clip = c }).ToList();
        Update(s => s with { Phase = JobPhase.Analyzing, ClipCount = entries.Count });
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled) break;
            var clip = entries[i].Clip;
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
            if (GoodDest(clip) is not int d)
            {
                entries[i].ProbeError = "no verified copy";
                continue;
            }
            if (clip.Format != ClipFormat.Video && renderer.UnsupportedReason(clip) is string reason)
            {
                entries[i].ProbeError = reason;
                continue;
            }
            try
            {
                var info = renderer.Probe(clip, cardPaths[d]);
                if (clip.Format == ClipFormat.Video && info.Width == null)
                {
                    if (renderer.ReclassifyUndecodableMxf(clip, cardPaths[d]) is { } arri)
                    {
                        entries[i].Clip = arri.Clip;
                        info = arri.Info;
                    }
                    else
                    {
                        entries[i].Info = info;
                        entries[i].ProbeError = "kodek nepodporován ffmpegem (např. Sony X-OCN) — zkopírováno a ověřeno, bez proxy";
                        continue;
                    }
                }
                entries[i].Info = info;
                entries[i].Thumbnails = TryThumbnails(renderer, entries[i].Clip, cardPaths[d], info, applyLut: true, log);
            }
            catch (Exception e)
            {
                entries[i].ProbeError = e.Message;
                log?.Invoke($"probe {clip.Name}: {e.Message}");
            }
        }
        report.Clips = entries;
        report.Proxy = Config.Proxy;
        RewriteReports(report, plan);

        if (!Config.Proxy.Enabled || IsCancelled) return report;
        Update(s => s with { Phase = JobPhase.Proxies, ClipCount = entries.Count });
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsCancelled) break;
            var clip = entries[i].Clip;  // may have been reclassified during analysis (MXF → ARRIRAW)
            int index = i;
            Update(s => s with { ClipIndex = index, ClipName = clip.Name, ClipFraction = 0 });
            if (clip.IsCameraProxy && Config.Proxy.SkipCameraProxies)
            {
                entries[i].ProxyStatus = ProxyStatus.Skipped;
                continue;
            }
            var unsupported = renderer.UnsupportedReason(clip)
                ?? (entries[i].Info is { Width: null } ? entries[i].ProbeError : null);
            if (unsupported != null)
            {
                entries[i].ProxyStatus = ProxyStatus.Unsupported;
                entries[i].ProxyNote = unsupported;
                continue;
            }
            if (GoodDest(clip) is not int d || entries[i].Info is not { } info)
            {
                entries[i].ProxyStatus = ProxyStatus.Failed;
                entries[i].ProxyNote = entries[i].ProbeError ?? "no verified copy";
                Update(s => s with { ProxiesFailed = s.ProxiesFailed + 1 });
                continue;
            }
            var name = clip.Name;
            for (int n = 2; !usedNames.Add(name); n++) name = $"{clip.Name}_{n}";
            var rel = Layout.ProxyDirName + "/" + name + ".mov";
            var sw = Stopwatch.StartNew();
            try
            {
                renderer.Render(clip, cardPaths[d], info, Layout.Join(cardPaths[d], rel),
                    f => Update(s => s with { ClipFraction = f }), () => IsCancelled);
                if (Config.Proxy.CopyToAllDestinations)
                {
                    for (int o = 0; o < cardPaths.Count; o++)
                        if (o != d) CopyFile(Layout.Join(cardPaths[d], rel), Layout.Join(cardPaths[o], rel));
                }
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                if (entries[i].Thumbnails.Count == 0)
                {
                    // RAW formats rendered via an intermediate: take the thumbnails from the finished proxy.
                    var proxyClip = new Clip { Name = name, Path = rel, Format = ClipFormat.Video, Segments = [rel], Bytes = 0 };
                    entries[i].Thumbnails = TryThumbnails(renderer, proxyClip, cardPaths[d], info, applyLut: false, log);
                }
                entries[i].ProxySeconds = sw.Elapsed.TotalSeconds;
                Update(s => s with { ProxiesDone = s.ProxiesDone + 1 });
            }
            catch (Exception e)
            {
                entries[i].ProxyStatus = ProxyStatus.Failed;
                entries[i].ProxyNote = e.Message;
                Update(s => s with { ProxiesFailed = s.ProxiesFailed + 1 });
                log?.Invoke($"proxy {clip.Name}: {e.Message}");
            }
        }
        report.Clips = entries;
        RewriteReports(report, plan);
        return report;
    }

    static List<Thumbnail> TryThumbnails(ProxyRenderer renderer, Clip clip, string cardPath, ClipInfo info, bool applyLut, Action<string>? log)
    {
        try
        {
            return renderer.Thumbnails(clip, cardPath, info, applyLut);
        }
        catch (Exception e)
        {
            log?.Invoke($"thumbnails {clip.Name}: {e.Message}");
            return [];
        }
    }

    /// PDF copy of the report on every destination. Rendered once, then copied; a failure here never fails the offload.
    static void WritePdfReports(OffloadReport report, OffloadPlan plan, Action<string>? log)
    {
        var name = $"{plan.CardName}_report.pdf";
        var first = Path.Combine(plan.CardPaths[0], Layout.ReportDirName, name);
        try
        {
            PdfReport.Write(report, first);
            foreach (var other in plan.CardPaths.Skip(1))
            {
                try { CopyFile(first, Path.Combine(other, Layout.ReportDirName, name)); }
                catch (Exception e) { log?.Invoke($"PDF copy to {other}: {e.Message}"); }
            }
        }
        catch (Exception e)
        {
            // The HTML report is already on disk; the PDF is a convenience copy.
            log?.Invoke($"PDF report failed: {e.Message}");
        }
    }

    static void RewriteReports(OffloadReport report, OffloadPlan plan)
    {
        foreach (var path in plan.CardPaths)
        {
            try { report.Write(path, includeHashRecords: false); }
            catch (Exception) { /* the first report is already there; this only adds clip details */ }
        }
    }

    static void CopyFile(string src, string dst)
    {
        var dir = Path.GetDirectoryName(dst)!;
        NativeIO.Mkdirs(dir);
        var part = Path.Combine(dir, "." + Path.GetFileName(dst) + ".part");
        NativeIO.Delete(part);
        File.Copy(src, part);
        NativeIO.Move(part, dst, replace: true);
    }
```

- [ ] **Step 4: Spustit testy**

Run: `dotnet test --filter "FullyQualifiedName~StageTwoTests|FullyQualifiedName~JobTests"`
Expected: PASS (StageTwoTests 6 + JobTests 5).

- [ ] **Step 5: Celá sada**

Run: `dotnet test`
Expected: PASS, 0 selhání.

- [ ] **Step 6: Commit**

Řádek do VYVOJ: `- Úkol 9 — fáze 2 úlohy: analýza klipů, 3 snímky, proxy na všechny cíle, PDF; selhání nikdy nezmění výsledek kopie`.

```powershell
git add windows docs
git commit -m "Windows: job stage 2 — clip analysis, thumbnails, proxies, PDF" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: CLI — volby proxy a souhrn klipů

**Files:**
- Modify: `windows/src/Offload.Cli/CopyCommand.cs`, `windows/src/Offload.Cli/ProgressDisplay.cs`

**Interfaces:**
- Consumes: `JobConfig` (`AnalyzeClips`, `Proxy`, `Tools`), `ProxyCodecExtensions.TryParseWire`, `OffloadJob.Run(log)`, `JobStatus` (Clip*, Proxies*), `ExternalTools.Resolved()`.
- Produces: volby jako Mac CLI: `--no-analyze`, `--proxy`, `--lut <file>`, `--proxy-height <n>` (1080), `--frame-aspect <x>`, `--frame-scale <percent>` (100), `--proxy-codec <name>` (h264), `--proxy-bitrate <mbps>` (10), `--no-burn-in`.

- [ ] **Step 1: Volby a konfigurace**

V `CopyCommand.Create` přidat volby (po `dryRun`):

```csharp
        var noAnalyze = new Option<bool>("--no-analyze") { Description = "Skip clip analysis (metadata + thumbnails in the report)." };
        var proxy = new Option<bool>("--proxy") { Description = "Render proxies after the copy is verified." };
        var lut = new Option<string?>("--lut") { Description = "LUT (.cube) applied to proxies and thumbnails." };
        var proxyHeight = new Option<int>("--proxy-height") { Description = "Proxy height in pixels.", DefaultValueFactory = _ => 1080 };
        var frameAspect = new Option<double?>("--frame-aspect") { Description = "Crop proxies to the frame lines: aspect ratio, e.g. 1.66." };
        var frameScale = new Option<double>("--frame-scale") { Description = "Frame lines size in % of the image (90 = 10 % overscan).", DefaultValueFactory = _ => 100 };
        var proxyCodec = new Option<string>("--proxy-codec") { Description = "Proxy codec: h264, hevc, proresProxy, proresLT, prores422, prores422HQ.", DefaultValueFactory = _ => "h264" };
        var proxyBitrate = new Option<int>("--proxy-bitrate") { Description = "Proxy bitrate (Mb/s) for H.264/HEVC.", DefaultValueFactory = _ => 10 };
        var noBurnIn = new Option<bool>("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
```

přidat je do `foreach (var o in new Option[] { … })` a v `SetAction` uvnitř `try` po vytvoření `config`:

```csharp
                if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
                    throw new ArgumentException($"Unknown proxy codec {r.GetValue(proxyCodec)}");
                var lutPath = r.GetValue(lut) is string l ? CliPath.Full(l) : null;
                if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException($"LUT not found: {lutPath}");
                config.AnalyzeClips = !r.GetValue(noAnalyze);
                config.Proxy = new ProxySettings
                {
                    Enabled = r.GetValue(proxy),
                    LutPath = lutPath,
                    Height = r.GetValue(proxyHeight),
                    Codec = codec,
                    BitrateMbps = r.GetValue(proxyBitrate),
                    FrameAspect = r.GetValue(frameAspect),
                    FrameScale = r.GetValue(frameScale) / 100,
                    BurnFilename = !r.GetValue(noBurnIn),
                    BurnTimecode = !r.GetValue(noBurnIn),
                };
```

- [ ] **Step 2: Běh a souhrn**

V `CopyCommand.Run`:
- po výpisu cílů (`→ …`) přidat:

```csharp
        if (config.AnalyzeClips || config.Proxy.Enabled)
            Console.WriteLine($"  ffmpeg: {config.Tools.Resolved().Ffmpeg ?? "NOT FOUND"}");
```

- `report = job.Run();` nahradit `report = job.Run(message => Console.Error.WriteLine(message));`
- za `Console.Write(report.Summary);` přidat:

```csharp
            if (report.Clips.Count > 0)
            {
                var done = report.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done);
                Console.WriteLine($"Clips: {report.Clips.Count}" + (config.Proxy.Enabled ? $"  proxies OK {done}, failed {status.ProxiesFailed}" : ""));
                foreach (var c in report.Clips.Where(c => c.ProxyStatus is ProxyStatus.Failed or ProxyStatus.Unsupported))
                    Console.WriteLine($"  {c.Clip.Name}: {c.ProxyStatus.ToString().ToLowerInvariant()} — {c.ProxyNote}");
            }
```

(`status` je potřeba získat před tímto blokem — přesunout `var status = job.Status();` nad `if (report != null)`, což už tak je.)

- [ ] **Step 3: Průběh analýzy a proxy**

V `ProgressDisplay.Tick` nahradit větev `else { line = st.Phase.ToString().ToLowerInvariant(); }` za:

```csharp
            else if (st.Phase is JobPhase.Analyzing or JobPhase.Proxies)
            {
                line = $"{st.Phase.ToString().ToLowerInvariant()} {st.ClipIndex + 1}/{st.ClipCount}  {st.ClipFraction * 100,3:0}%  {st.ClipName}";
                if (st.SafeToEject) line += "  (card safe to eject)";
            }
            else
            {
                line = st.Phase.ToString().ToLowerInvariant();
            }
```

- [ ] **Step 4: Sestavit a ručně ověřit (C: → D:, E:)**

```powershell
dotnet build
$src = "C:\Users\PC\AppData\Local\Temp\offload-p2\A001"
New-Item -ItemType Directory -Force "$src\PRIVATE\M4ROOT\CLIP" | Out-Null
foreach ($i in 1..3) { ffmpeg -hide_banner -loglevel error -nostdin -y -f lavfi -i "testsrc2=s=3840x2160:r=25:d=5" -f lavfi -i "sine=d=5" -timecode "0$i:00:00:00" -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest "$src\PRIVATE\M4ROOT\CLIP\C000$i.MP4" }
New-Item -ItemType Directory -Force D:\offload-p2, E:\offload-p2 | Out-Null
$exe = "src\Offload.Cli\bin\Debug\net10.0-windows\offload.exe"
& $exe $src -d D:\offload-p2 -d E:\offload-p2 --proxy --proxy-height 720 --frame-aspect 1.85; "exit $LASTEXITCODE"
& $exe copy $src -d D:\offload-p2 --proxy-codec vp9; "exit $LASTEXITCODE"
```

Expected:
- první příkaz: řádek `ffmpeg: …ffmpeg.exe`, souhrn `OFFLOAD REPORT — OK`, `Clips: 3  proxies OK 3, failed 0`, exit 0; v `D:\offload-p2\<datum>\A001\_PROXY\` i na E: tři `.mov` (720p, burn-in); `_REPORT\A001_report.pdf` na obou; HTML report s třemi snímky na klip.
- druhý: `Error: Unknown proxy codec vp9`, exit 1.

Výstupy vložit do reportu. Složky nechat (Task 11 je použije a uklidí).

- [ ] **Step 5: Commit**

Řádek do VYVOJ: `- Úkol 10 — CLI: volby proxy (--proxy, --lut, --proxy-codec, --frame-aspect …) a souhrn klipů`.

```powershell
git add windows docs
git commit -m "Windows: CLI proxy options and clip summary" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Balíček s ffmpeg, dokumentace, úklid

**Files:**
- Modify: `windows/scripts/build.ps1`, `windows/README.md`, `docs/ROZDILY.md`, `docs/VYVOJ-WINDOWS.md`

**Interfaces:**
- Produces: `windows\build\Offload\bin\` obsahuje `offload.exe`, `ffmpeg.exe`, `ffprobe.exe`.

- [ ] **Step 1: build.ps1**

Za řádek `if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }` vložit:

```powershell
# ffmpeg/ffprobe: windows\tools\bin first (pinned copy), otherwise whatever is on PATH.
foreach ($t in 'ffmpeg.exe', 'ffprobe.exe') {
    $local = Join-Path $root "tools\bin\$t"
    $src = if (Test-Path $local) { $local } else { (Get-Command $t -ErrorAction SilentlyContinue).Source }
    if ($src) { Copy-Item $src (Join-Path $out 'bin') } else { Write-Warning "$t not found — proxies and thumbnails will need it at runtime" }
}
```

a komentář nahoře změnit na `# Builds windows\build\Offload: bin\offload.exe (self-contained, single file) + bin\ffmpeg.exe, ffprobe.exe.`

Run: `powershell -ExecutionPolicy Bypass -File scripts\build.ps1; Get-ChildItem build\Offload\bin | Select-Object Name`
Expected: `ffmpeg.exe`, `ffprobe.exe`, `offload.exe`.

- [ ] **Step 2: Ověřit sestavenou verzi**

```powershell
$src = "C:\Users\PC\AppData\Local\Temp\offload-p2\A001"
& build\Offload\bin\offload.exe $src -d D:\offload-p2 -d E:\offload-p2 --proxy --proxy-codec hevc --proxy-height 540; "exit $LASTEXITCODE"
```

Expected: exit 0, `Clips: 3  proxies OK 3, failed 0`, druhá sada (`A001_2`). Ve výpisu není chyba „ffmpeg not found“ (ffmpeg z `bin\` vedle exe).

- [ ] **Step 3: Dokumentace**

- `windows/README.md`: tabulka Stav — řádek `Analýza klipů, 3 snímky, proxy, PDF` → `hotovo (plán 2)`; řádek RAW: `R3D/ARRI: kód hotový, neověřeno se skutečnými nástroji (REDline, art-cmd nejsou nainstalované); BRAW: plán 3`. Do „Použití“ přidat příklad:
  `offload E:\ -d D:\Zaloha -d F:\Zaloha --proxy --lut look.cube --frame-aspect 1.85   # + proxy s LUT a výřezem`.
  Přidat odstavec: „PDF tiskne Microsoft Edge v režimu bez okna (je ve Windows vždy). ffmpeg/ffprobe jsou v `bin\` vedle `offload.exe` (build gyan.dev, licence GPL — pro vlastní použití v pořádku, při dalším šíření je třeba přiložit licenci a zdroj ffmpeg).“
- `docs/ROZDILY.md`: Windows sloupec — PDF ✅ (Edge headless), Analýza klipů ✅, Náhledy ✅ 3 snímky (Mac: ⏳), Proxy ✅ (NVENC/QSV/AMF/software), RED R3D a ARRIRAW „✅ kód, neověřeno s nástroji“; v tabulce technologií řádek PDF „Edge headless `--print-to-pdf`“ místo WebView2; do „Záměrné rozdíly“: „R3D/ARRI bez proxy zatím nemají náhledy (stejně jako na Macu)“. Datum „Stav k 2026-10-05“.
- `docs/VYVOJ-WINDOWS.md`: tabulka plánů — plán 2 `hotovo`; nahoru do seznamu plánu 2: `- Úkol 11 — balíček s ffmpeg/ffprobe, dokumentace`.

- [ ] **Step 4: Úklid**

```powershell
Remove-Item -Recurse -Force D:\offload-p2, E:\offload-p2, C:\Users\PC\AppData\Local\Temp\offload-p2
```

Pokud nástroj mazání odmítne, vypsat v reportu, co zůstalo.

- [ ] **Step 5: Všechny testy a commit**

Run: `dotnet test`
Expected: PASS, 0 selhání.

```powershell
git add windows docs
git commit -m "Windows: bundle ffmpeg, docs for clips, proxies and PDF" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
