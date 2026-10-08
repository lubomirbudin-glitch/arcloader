# Offload pro Windows — plán 10: Verify-in-Proxy (úkoly 10–18) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pokračování plánu 10 — obsahová kontrola dekodérem, relace ověření, napojení na úlohu („Lze vyjmout“ × „Lze formátovat“), navázání po pádu, sériové číslo svazku, UI, nastavení, reporty, dokumentace a verze 0.6.0.

**Architecture:** Viz první soubor plánu `windows/docs/superpowers/plans/2026-10-08-offload-windows-10-verify-in-proxy.md` (úkoly 1–9 jsou hotové, než začne úkol 10).

**Tech Stack:** C# / .NET 10, WPF, xUnit, `System.IO.Hashing` 10.0.12 (už v projektu), ffmpeg 8.1.

**Spec:** `windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md`

## Global Constraints

Platí celé sekce **Global Constraints**, **Review Focus**, **Ověřeno před plánem** a **Rozhodnutí** z prvního souboru plánu (základ `main` ef25348, žádný nový NuGet, česky, UI vlákno nečeká, na kartu se nezapisuje, nic se nepřepisuje — R7, `mac/` beze změny, GUI ani `offload.exe` se nespouští, testy jen v `TempDir` a na `127.0.0.1`, `-warnaserror` + celá sada po každém úkolu, řádek do `docs/VYVOJ-WINDOWS.md`, commit přes `.git/COMMIT_MSG_PLAN10` s `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).

## Review Focus

Body 1–8 prvního souboru; v tomto souboru je vlastní: 1 (úkol 12), 5 (úkoly 11–13), 6 (úkol 12), 7 (úkoly 11–12), 8 (úkol 14).

---

### Task 10: Obsahová kontrola dekodérem při proxy (spec 7, R9)

Při tvorbě proxy Offload poslouchá, co říká dekodér: ffmpeg běží s `-loglevel warning` (v ffmpeg 8.1 je „corrupt
decoded frame“ varování, ne chyba — naměřeno) a `ContentWatch` čte stderr a průběh: první hláška dekódování, časových
značek, nebo méně snímků, než soubor slibuje (`nb_frames`), dá `content_warn` se snímkem a timecode. Výsledek jde do
`clips[].content` (`ok` / `warn`, druh, snímek, TC, hláška, počty snímků) a `OffloadReport.ContentWarnings`. Vadný
klip z kamery je pořád přesná kopie karty — kontrola obsahu ověření kopie nemění, jen žádá potvrzení před formátováním
(úkoly 12 a 15). `Snapshot` vytáhne malý JPEG blízko vadného místa (náhled místa vady, spec 9). Neúspěch s hardwarovým
dekodérem, po kterém se render opakuje softwarově, se zapomene (`Reset`).

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/ContentCheck.cs`
- Modify: `windows/src/Offload.Core/ToolProcess.cs:15-20, 43-48`
- Modify: `windows/src/Offload.Core/ProxyRenderer.cs:2-7, 344-358, 385-391, 398-406, 421-427, 435-441, 463-469, 489-497, 511-516`
- Modify: `windows/src/Offload.Core/Media.cs:139-144`
- Modify: `windows/src/Offload.Core/OffloadReport.cs:124-129`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/ContentCheckTests.cs` (nový)

**Interfaces:**
- Consumes: `ToolProcess`, `ProxyRenderer.RenderOnce/RunFfmpeg`, `ClipInfo.FrameCount/FrameRate`, `Timecode`.
- Produces:
  - `ContentResult` (`Ok = "ok"`, `Warn = "warn"`, `State`, `Kind`, `Frame`, `Timecode`, `Message`, `FramesDecoded`, `FramesExpected`, `IsWarn`, `Jpeg` [JsonIgnore], `static KindText(kind)`).
  - `ContentWatch`: `Progress(line)`, `Stderr(line)`, `Reset()`, `Verdict(ClipInfo)`.
  - `ToolProcess.StderrLine` (`Action<string>?`), `ProxyRenderer.Render(…, string? inputUrl = null, ContentWatch? content = null)`, `ProxyRenderer.Snapshot(clip, cardPath, info, frame)` → `byte[]?`.
  - `ClipEntry.Content` (`"content"`), `OffloadReport.ContentWarnings`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/ContentCheckTests.cs` (nový soubor)

```csharp
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 7, R9: the decoder's verdict while the proxy is made — damaged data and an early end are content_warn with the
/// place; a good clip is content_ok; other ffmpeg warnings are not.
public class ContentCheckTests
{
    static ClipInfo Info(int? frames = 100) => new() { Frames = frames, FrameRate = "25/1", Timecode = "01:00:00:00" };

    [Fact]
    public void FirstDecodeLine_IsTheWarning_AtTheFrameSeenLast()
    {
        var w = new ContentWatch();
        w.Progress("frame=49");
        w.Stderr("[libx264 @ 0000] using cpu capabilities: MMX2 SSE2Fast");   // not about the file
        w.Progress("frame=52");
        w.Stderr("[vist#0:0/h264 @ 0001] [dec:h264 @ 0002] corrupt decoded frame");
        w.Stderr("[h264 @ 0003] Invalid NAL unit size (11597 > 2699).");
        w.Progress("frame=100");

        var r = w.Verdict(Info());

        Assert.True(r.IsWarn);
        Assert.Equal("decode", r.Kind);
        Assert.Equal(52, r.Frame);
        Assert.Equal("01:00:02:02", r.Timecode);
        Assert.EndsWith("corrupt decoded frame", r.Message);
    }

    [Theory]
    [InlineData("[mov @ 01] Non-monotonic DTS; previous: 3000, current: 2000")]
    [InlineData("Application provided invalid, non monotonically increasing dts to muxer in stream 0")]
    public void TimestampLines_Warn(string line)
    {
        var w = new ContentWatch();
        w.Stderr(line);
        w.Progress("frame=100");
        Assert.Equal("timestamps", w.Verdict(Info()).Kind);
    }

    [Fact]
    public void FewerFramesThanTheFileSays_EndsEarly()
    {
        var w = new ContentWatch();
        w.Progress("frame=60");
        var r = w.Verdict(Info(100));
        Assert.Equal(("end", 60L, "01:00:02:10"), (r.Kind, r.Frame!.Value, r.Timecode));
        Assert.Equal("decoded 60 of 100 frames", r.Message);
    }

    [Fact]
    public void FrameCountUnknown_NotCounted_NoTimecode_FrameNumber()
    {
        var w = new ContentWatch();
        w.Progress("frame=10");
        Assert.False(w.Verdict(Info(null)).IsWarn);

        w.Stderr("Decoding error: Invalid data found when processing input");
        Assert.Equal("#10", w.Verdict(new ClipInfo { Frames = null }).Timecode);
    }

    [Fact]
    public void Reset_ForgetsTheHardwareAttempt()
    {
        var w = new ContentWatch();
        w.Stderr("[h264 @ 1] error while decoding MB 3 4");
        w.Reset();
        w.Progress("frame=100");
        Assert.False(w.Verdict(Info()).IsWarn);
    }

    static ProxyRenderer Renderer() => new(new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false }, new ExternalTools().Resolved())
    {
        ForcedEncoder = "libx264",
    };

    static ContentResult RenderAndCheck(string card, string rel)
    {
        using var renderer = Renderer();
        var clip = new Clip { Name = Path.GetFileNameWithoutExtension(rel), Path = rel, Format = ClipFormat.Video, Segments = [rel] };
        var info = renderer.Probe(clip, card);
        var watch = new ContentWatch();
        renderer.Render(clip, card, info, null, Path.Combine(card, "_PROXY", clip.Name + ".mov"), _ => { }, () => false, content: watch);
        var verdict = watch.Verdict(info);
        if (verdict.IsWarn) verdict.Jpeg = renderer.Snapshot(clip, card, info, verdict.Frame ?? 0);
        return verdict;
    }

    [FfmpegFact]
    public void GoodClip_ContentOk_WithTheFrameCount()
    {
        using var dir = new TempDir();
        TestMedia.MakeFaststart(Path.Combine(dir.Path, "good.mp4"));
        var r = RenderAndCheck(dir.Path, "good.mp4");
        Assert.False(r.IsWarn);
        Assert.Equal(50, r.FramesDecoded);
        Assert.Equal(50, r.FramesExpected);
    }

    // Spec 11: a damaged file from the camera — the copy is bit-exact, the decoder says where it is broken.
    [FfmpegFact]
    public void GarbageInTheMiddle_Warn_Decode_WithAPicture()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "bad.mp4");
        TestMedia.MakeFaststart(path, seconds: 4);
        var bytes = File.ReadAllBytes(path);
        new Random(5).NextBytes(bytes.AsSpan(bytes.Length / 2, 20_000));
        File.WriteAllBytes(path, bytes);

        var r = RenderAndCheck(dir.Path, "bad.mp4");

        Assert.True(r.IsWarn);
        Assert.Equal("decode", r.Kind);
        Assert.StartsWith("01:00:0", r.Timecode);
        Assert.NotNull(r.Jpeg);
        Assert.Equal(0xFF, r.Jpeg![0]);   // a JPEG
    }

    [FfmpegFact]
    public void CutOffClip_Warn()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "cut.mp4");
        TestMedia.MakeFaststart(path, seconds: 4);
        using (var f = new FileStream(path, FileMode.Open)) f.SetLength(f.Length * 6 / 10);

        var r = RenderAndCheck(dir.Path, "cut.mp4");

        Assert.True(r.IsWarn);
        Assert.Contains(r.Kind, new[] { "decode", "end" });
        Assert.True(r.FramesDecoded < 100);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ContentCheckTests|FullyQualifiedName~ProxyRendererTests|FullyQualifiedName~TapRenderTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0246: The type or namespace name 'ContentResult' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/ContentCheck.cs` (nový soubor)

```csharp
using System.Globalization;
using System.Text.Json.Serialization;

namespace Offload.Core.VerifyInProxy;

/// Spec 7 (content_ok / content_warn), report key `clips[].content`: what the decoder said while it made the proxy. A
/// warning is about the file itself (the copy matches the card bit for bit): it never fails the copy and never blocks
/// "Lze formátovat", but formatting needs an explicit confirmation (spec 7). Absent for a clip without a proxy.
public sealed class ContentResult
{
    public const string Ok = "ok";
    public const string Warn = "warn";

    /// "ok" or "warn".
    [JsonPropertyName("state")] public string State { get; init; } = Ok;
    /// "decode" (the decoder reported damaged data), "timestamps" (missing or repeated time stamps), "frames" (more frames
    /// than the file says), "end" (fewer: the clip ends early). Null when ok.
    [JsonPropertyName("kind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; init; }
    /// The frame near the first defect (approximate: ffmpeg's progress is reported every half second).
    [JsonPropertyName("frame"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Frame { get; init; }
    /// That frame as the clip's timecode ("01:00:02:13"), or "#<frame>" without one.
    [JsonPropertyName("timecode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Timecode { get; init; }
    /// The decoder's first line about it (English, as ffmpeg wrote it), or the frame counts.
    [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; init; }
    [JsonPropertyName("framesDecoded"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? FramesDecoded { get; init; }
    [JsonPropertyName("framesExpected"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? FramesExpected { get; init; }
    /// The picture at that place (spec 9 "náhled místa vady"), embedded in the HTML report only.
    [JsonIgnore] public byte[]? Jpeg { get; set; }

    [JsonIgnore] public bool IsWarn => State == Warn;

    /// "poškozená data", "chybné časové značky", "snímky navíc", "klip končí předčasně".
    public static string KindText(string? kind) => kind switch
    {
        "decode" => "poškozená data",
        "timestamps" => "chybné časové značky",
        "frames" => "snímky navíc",
        "end" => "klip končí předčasně",
        _ => "vada obsahu",
    };
}

/// Watches one ffmpeg proxy run (R9: never -xerror, it would stop the proxy): the stderr lines at -loglevel warning and
/// the `frame=` counter of -progress. Lines that are about damaged data or time stamps are a warning; any other line
/// (encoder notes, deprecated pixel formats …) is not. Thread-safe: stdout and stderr are read on two threads.
public sealed class ContentWatch
{
    /// ffmpeg 8.1's own words, measured on a clip with garbage in its mdat and on a truncated faststart MP4 (plan 10).
    static readonly string[] DecodeWords =
    [
        "corrupt decoded frame", "decoding error", "error while decoding", "concealing", "invalid nal", "corrupt input packet",
        "packet corrupt", "invalid data found", "missing picture", "error splitting", "partial file", "decode_slice",
    ];

    static readonly string[] TimestampWords = ["non-monotonic", "non monotonically", "invalid timestamp", "timestamps are unset", "invalid dts"];

    readonly Lock gate = new();
    long frame;
    string? kind, message;
    long? at;

    /// A line of -progress (stdout).
    public void Progress(string line)
    {
        if (!line.StartsWith("frame=", StringComparison.Ordinal)
            || !long.TryParse(line["frame=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return;
        lock (gate) frame = Math.Max(frame, n);
    }

    /// A line of stderr.
    public void Stderr(string line)
    {
        var lower = line.ToLowerInvariant();
        string? k = DecodeWords.Any(lower.Contains) ? "decode" : TimestampWords.Any(lower.Contains) ? "timestamps" : null;
        if (k == null) return;
        lock (gate)
        {
            if (kind != null) return;   // the first defect is the one reported
            kind = k;
            message = line.Trim();
            at = frame;
        }
    }

    /// A retry of the clip (in software after the hardware decoder failed): what the first attempt saw is forgotten.
    public void Reset()
    {
        lock (gate)
        {
            frame = 0;
            kind = message = null;
            at = null;
        }
    }

    /// The verdict once ffmpeg ended: the first defect line; else the frame count against the file's own (nb_frames —
    /// a clip whose container does not say it is not counted, a guess from the duration would warn about good files).
    public ContentResult Verdict(ClipInfo info)
    {
        lock (gate)
        {
            long? expected = info.Frames is int n && n > 0 ? n : null;
            string? k = kind, msg = message;
            long? where = at;
            if (k == null && expected is long e && frame < e - 1)
                (k, msg, where) = ("end", $"decoded {frame} of {e} frames", frame);
            else if (k == null && expected is long e2 && frame > e2 + 1)
                (k, msg, where) = ("frames", $"decoded {frame} of {e2} frames", e2);
            if (k == null) return new ContentResult { FramesDecoded = frame, FramesExpected = expected };
            return new ContentResult
            {
                State = ContentResult.Warn, Kind = k, Message = msg, Frame = where, Timecode = TimecodeAt(info, where ?? 0),
                FramesDecoded = frame, FramesExpected = expected,
            };
        }
    }

    static string TimecodeAt(ClipInfo info, long frame) =>
        Timecode.IsValid(info.Timecode) && Timecode.Fps(info.FrameRate) is double fps ? Timecode.Add(info.Timecode!, frame, fps) : $"#{frame}";
}
```

#### `windows/src/Offload.Core/ToolProcess.cs`

Najít (1/2, řádky 15–20):

```csharp
    readonly Lock gate = new();

    public string Name { get; }

    public ToolProcess(string exe, IEnumerable<string> args, string? workingDirectory, bool pipeStdin)
    {
```

Nahradit:

```csharp
    readonly Lock gate = new();

    public string Name { get; }
    /// Every stderr line as it comes (plan 10: the proxy's content check), besides the tail kept for errors.
    public Action<string>? StderrLine { get; set; }

    public ToolProcess(string exe, IEnumerable<string> args, string? workingDirectory, bool pipeStdin)
    {
```

Najít (2/2, řádky 43–48):

```csharp
                stderr.Append(e.Data).Append('\n');
                if (stderr.Length > 4000) stderr.Remove(0, stderr.Length - 4000);
            }
        };
    }

```

Nahradit:

```csharp
                stderr.Append(e.Data).Append('\n');
                if (stderr.Length > 4000) stderr.Remove(0, stderr.Length - 4000);
            }
            StderrLine?.Invoke(e.Data);
        };
    }

```

#### `windows/src/Offload.Core/ProxyRenderer.cs`

Najít (1/9, řádky 2–7):

```csharp
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core;

```

Nahradit:

```csharp
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Offload.Core.VerifyInProxy;

namespace Offload.Core;

```

Najít (2/9, řádky 344–358):

```csharp
        return result;
    }

    /// The tool name of a ToolError raised by braw-decode (its file name without the extension).
    bool IsBrawDecoder(string tool) =>
        Tools.BrawDecoder is string exe && string.Equals(tool, Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase);

    /// Renders the proxy. A render that stalls (no progress for StallTimeout) is killed. With hardware decoding the
    /// clip is then retried once in software — as is one that failed with it; without, the stall fails the proxy.
    /// `inputUrl` (plan 10, mode A): a video clip is read from the tap (RangeServer) instead of from its file.
    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled,
        string? inputUrl = null)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, Msg.FfmpegNotFound);
        var lutFile = PrepareLut(lut);
```

Nahradit:

```csharp
        return result;
    }

    /// Spec 9 "náhled místa vady": the picture near `frame` of a video clip as a small JPEG (read from its file on
    /// `cardPath`); null when ffmpeg gives none — a damaged place may not decode at all.
    public byte[]? Snapshot(Clip clip, string cardPath, ClipInfo info, long frame)
    {
        if (Tools.Ffmpeg is not string ffmpeg || clip.Format != ClipFormat.Video) return null;
        var output = Path.Combine(workDir, $"defect-{Guid.NewGuid():N}.jpg");
        try
        {
            double seconds = Timecode.Fps(info.FrameRate) is double fps && fps > 0 ? frame / fps : 0;
            var at = Math.Max(0, seconds - 0.5).ToString("0.000000", CultureInfo.InvariantCulture);
            ToolProcess.Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-ss", at, "-i", Layout.Join(cardPath, clip.Path),
                "-frames:v", "1", "-vf", "scale=-2:180", "-q:v", "5", output], workDir, TimeSpan.FromSeconds(60));
            return File.Exists(output) ? File.ReadAllBytes(output) : null;
        }
        catch (Exception e) when (e is ToolError or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            NativeIO.Delete(output);
        }
    }

    /// The tool name of a ToolError raised by braw-decode (its file name without the extension).
    bool IsBrawDecoder(string tool) =>
        Tools.BrawDecoder is string exe && string.Equals(tool, Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase);

    /// Renders the proxy. A render that stalls (no progress for StallTimeout) is killed. With hardware decoding the
    /// clip is then retried once in software — as is one that failed with it; without, the stall fails the proxy.
    /// `inputUrl` (plan 10, mode A): a video clip is read from the tap (RangeServer) instead of from its file. `content`
    /// (spec 7, R9): watches the decoder — ffmpeg then logs at warning level.
    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled,
        string? inputUrl = null, ContentWatch? content = null)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, Msg.FfmpegNotFound);
        var lutFile = PrepareLut(lut);
```

Najít (3/9, řádky 385–391):

```csharp
        Exception first;
        try
        {
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel, inputUrl);
            return;
        }
        catch (FfmpegStalledException e) when (!hwaccel && !isCancelled())
```

Nahradit:

```csharp
        Exception first;
        try
        {
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel, inputUrl, content);
            return;
        }
        catch (FfmpegStalledException e) when (!hwaccel && !isCancelled())
```

Najít (4/9, řádky 398–406):

```csharp
        }
        if (isCancelled()) throw new ToolError("ffmpeg", -1, "cancelled");
        Log?.Invoke($"proxy {clip.Name}: with hardware decoding: {first.Message} — retrying in software");
        try
        {
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel: false, inputUrl);
            Log?.Invoke($"proxy {clip.Name}: software decoding succeeded");
        }
        catch (Exception e) when (e is FfmpegStalledException or ToolError && !isCancelled())
```

Nahradit:

```csharp
        }
        if (isCancelled()) throw new ToolError("ffmpeg", -1, "cancelled");
        Log?.Invoke($"proxy {clip.Name}: with hardware decoding: {first.Message} — retrying in software");
        content?.Reset();   // what the hardware decoder said is not the file's verdict
        try
        {
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel: false, inputUrl, content);
            Log?.Invoke($"proxy {clip.Name}: software decoding succeeded");
        }
        catch (Exception e) when (e is FfmpegStalledException or ToolError && !isCancelled())
```

Najít (5/9, řádky 421–427):

```csharp
        + (retriedInSoftware ? " i bez hardwarového dekódování; proxy nevznikla" : "");

    void RenderOnce(string ffmpeg, Clip clip, string cardPath, ClipInfo info, LutUse? lut, string? lutFile, string output,
        Action<double> progress, Func<bool> isCancelled, bool hwaccel, string? inputUrl)
    {
        var src = SourceFor(clip, cardPath, info, LookFor(lutFile != null ? lut : null, info), null, hwaccel, inputUrl);
        string? label = null;
```

Nahradit:

```csharp
        + (retriedInSoftware ? " i bez hardwarového dekódování; proxy nevznikla" : "");

    void RenderOnce(string ffmpeg, Clip clip, string cardPath, ClipInfo info, LutUse? lut, string? lutFile, string output,
        Action<double> progress, Func<bool> isCancelled, bool hwaccel, string? inputUrl, ContentWatch? content)
    {
        var src = SourceFor(clip, cardPath, info, LookFor(lutFile != null ? lut : null, info), null, hwaccel, inputUrl);
        string? label = null;
```

Najít (6/9, řádky 435–441):

```csharp
            }
            var vf = FilterGraph.Proxy(Settings, outHeight, src.AlreadyCropped, lutFile, NeedsPreMap(lut, src, info), hasFont, label, info.Timecode, info.FrameRate);

            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-nostats", "-progress", "pipe:1" };
            if (src.FeederExe == null) args.Add("-nostdin");
            args.AddRange(src.InputArgs);
            args.AddRange(["-map", "0:v:0"]);
```

Nahradit:

```csharp
            }
            var vf = FilterGraph.Proxy(Settings, outHeight, src.AlreadyCropped, lutFile, NeedsPreMap(lut, src, info), hasFont, label, info.Timecode, info.FrameRate);

            // Warning level with the content check: "corrupt decoded frame" is a warning in ffmpeg 8.1 (plan 10 measurement).
            var args = new List<string> { "-hide_banner", "-loglevel", content != null ? "warning" : "error", "-y", "-nostats", "-progress", "pipe:1" };
            if (src.FeederExe == null) args.Add("-nostdin");
            args.AddRange(src.InputArgs);
            args.AddRange(["-map", "0:v:0"]);
```

Najít (7/9, řádky 463–469):

```csharp
            args.AddRange(["-f", "mov", part]);
            try
            {
                RunFfmpeg(ffmpeg, args, src, info.Duration, progress, isCancelled, StallTimeout);
                if (part.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (Path.Exists(output)) throw new IOError("rename", output, 80 /* ERROR_FILE_EXISTS */);
```

Nahradit:

```csharp
            args.AddRange(["-f", "mov", part]);
            try
            {
                RunFfmpeg(ffmpeg, args, src, info.Duration, progress, isCancelled, StallTimeout, content);
                if (part.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (Path.Exists(output)) throw new IOError("rename", output, 80 /* ERROR_FILE_EXISTS */);
```

Najít (8/9, řádky 489–497):

```csharp
    /// reported no new frame or output time and taken no data from the feeder for that long (a stuck ffmpeg keeps
    /// repeating the same progress block); 3× as long before the first sign of progress.
    internal void RunFfmpeg(string ffmpeg, List<string> args, FrameSource src, double? duration, Action<double>? progress, Func<bool> isCancelled,
        TimeSpan? stallAfter = null)
    {
        using var ff = new ToolProcess(ffmpeg, args, workDir, pipeStdin: src.FeederExe != null);
        using var feeder = src.FeederExe != null ? new ToolProcess(src.FeederExe, src.FeederArgs, workDir, pipeStdin: false) : null;
        Thread? pump = null;
        Stream? input = null, sink = null;
```

Nahradit:

```csharp
    /// reported no new frame or output time and taken no data from the feeder for that long (a stuck ffmpeg keeps
    /// repeating the same progress block); 3× as long before the first sign of progress.
    internal void RunFfmpeg(string ffmpeg, List<string> args, FrameSource src, double? duration, Action<double>? progress, Func<bool> isCancelled,
        TimeSpan? stallAfter = null, ContentWatch? content = null)
    {
        using var ff = new ToolProcess(ffmpeg, args, workDir, pipeStdin: src.FeederExe != null) { StderrLine = content == null ? null : content.Stderr };
        using var feeder = src.FeederExe != null ? new ToolProcess(src.FeederExe, src.FeederArgs, workDir, pipeStdin: false) : null;
        Thread? pump = null;
        Stream? input = null, sink = null;
```

Najít (9/9, řádky 511–516):

```csharp
            feeder?.Start(null);
            ff.Start(line =>
            {
                if (watch.Advanced(line)) Advance();
                if (progress == null || duration is not double d || d <= 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
                if (double.TryParse(line["out_time_us=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var us))
```

Nahradit:

```csharp
            feeder?.Start(null);
            ff.Start(line =>
            {
                content?.Progress(line);
                if (watch.Advanced(line)) Advance();
                if (progress == null || duration is not double d || d <= 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
                if (double.TryParse(line["out_time_us=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var us))
```

#### `windows/src/Offload.Core/Media.cs`

Najít (1/1, řádky 139–144):

```csharp
        ProxyStatus == ProxyStatus.Skipped && ProxyNote?.StartsWith(Msg.ProxyLighterPrefix, StringComparison.Ordinal) == true;
    /// First, middle and last frame (JPEG + caption); embedded in the HTML/PDF report only.
    [JsonIgnore] public List<Thumbnail> Thumbnails { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<ProxyCodec>))]
```

Nahradit:

```csharp
        ProxyStatus == ProxyStatus.Skipped && ProxyNote?.StartsWith(Msg.ProxyLighterPrefix, StringComparison.Ordinal) == true;
    /// First, middle and last frame (JPEG + caption); embedded in the HTML/PDF report only.
    [JsonIgnore] public List<Thumbnail> Thumbnails { get; set; } = [];
    /// What the decoder said while making the proxy (plan 10, spec 7): content_ok / content_warn. Absent without a proxy
    /// and in older reports; the Mac ignores it.
    [JsonPropertyName("content"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public VerifyInProxy.ContentResult? Content { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ProxyCodec>))]
```

#### `windows/src/Offload.Core/OffloadReport.cs`

Najít (1/1, řádky 124–129):

```csharp
    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was verified there (spec 2 safe_to_format for this generation).
    [JsonIgnore] public bool SafeToFormat => Success && Verified;

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<FileHashes> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
```

Nahradit:

```csharp
    [JsonIgnore] public bool Success => FailedFiles.Count == 0;
    /// Every file is on every destination and was verified there (spec 2 safe_to_format for this generation).
    [JsonIgnore] public bool SafeToFormat => Success && Verified;
    /// Spec 7: the clips whose decoder reported a defect (content_warn) — the copy matches the card, the file may not.
    [JsonIgnore] public IReadOnlyList<ClipEntry> ContentWarnings => Clips.Where(c => c.Content?.IsWarn == true).ToList();

    public static OffloadReport Build(OffloadPlan plan, IReadOnlyList<FileHashes> hashes, IReadOnlyList<string?> sourceErrors,
        IReadOnlyList<DestFileResult[]> destResults, IReadOnlyList<bool> unbuffered, bool verified,
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ContentCheckTests|FullyQualifiedName~ProxyRendererTests|FullyQualifiedName~TapRenderTests"`

Expected: PASS — `Failed: 0, Passed: 41, Skipped: 0, Total: 41` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1596` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 10: obsahová kontrola dekodérem při proxy (`ContentWatch`: hlášky dekódování a časových značek, méně snímků, než soubor slibuje) → `clips[].content` ok/warn se snímkem a TC, náhled místa vady (JPEG).
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: decoder content check while rendering proxies (plan 10, task 10)

ffmpeg logs at warning level while rendering a proxy; ContentWatch reads stderr and progress
(decode and timestamp errors, fewer frames than the file says) and gives content ok/warn with the
frame and timecode into clips[].content; Snapshot makes a small JPEG of the defect.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Media.cs windows\src\Offload.Core\OffloadReport.cs windows\src\Offload.Core\ProxyRenderer.cs windows\src\Offload.Core\ToolProcess.cs windows\src\Offload.Core\VerifyInProxy\ContentCheck.cs windows\tests\Offload.Core.Tests\ContentCheckTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 11: VerifySession: primární cíl, čtení na pozadí, dorovnání a poznámky (spec 6, 10)

Jedna relace ověření na úlohu. Primární cíl = zvolený (`--primary`, nastavení), jinak **nejrychlejší** podle
rychlosti zápisu změřené při kopii — jen z cílů, které smí ověřit během proxy (kontrola z úkolu 5; ostatní mají důvod
v `ClassicWhy`). Ostatní cíle čte vlákno na pozadí (jedno na svazek, `THREAD_MODE_BACKGROUND_BEGIN` = nízká priorita
I/O, spec 6), primární na pozadí čte jen soubory bez proxy a jen když tap nečte (nesoupeří s ffmpeg o disk).
`Prepare(clip, dest)` dá pro proxy z primárního cíle URL tapu (režim A) nebo soubor ověří a ohřeje (režim B).
`Finish()` dočte, co nikdo nepřečetl, a vyhodnotí: blok, který napodruhé souhlasí = poznámka „Nestabilní čtení“;
stejný blok špatně na víc cílech → karta se přečte znovu a poznámka řekne, kde vada vznikla (spec 10). Selhaný soubor
se nikdy nepřepisuje (R7) — jde do opravy. `WithVerification` z výsledků udělá konečný report (stavy, metoda a důvod
po cílech, poznámky, čas do „lze formátovat“).

**Files:**
- Create: `windows/src/Offload.Core/VerifyInProxy/VerifySession.cs`
- Create: `windows/src/Offload.Core/VerifyInProxy/OffloadReport.Verify.cs`
- Modify: `windows/src/Offload.Core/Win32.cs:56-61`
- Modify: `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs:12`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/VerifySessionTests.cs` (nový)

**Interfaces:**
- Consumes: `BlockVerifier`, `TapFile`, `BlockReads`, `RangeServer`, `CacheWarm`, `TapRegistry` (úkoly 2–9), `PauseGate`, `OffloadPlan`, `OffloadReport`.
- Produces:
  - `TapLease(string? url, Action release) : IDisposable` (`Url`).
  - `VerifySession(OffloadPlan plan, OffloadReport stage1, IReadOnlyList<ulong[]?> blocks, int blockSize, IReadOnlyList<double?> rates, int? preferred, IReadOnlyList<string?> classicWhy, PauseGate pauses, CancellationToken ct, Func<int, int, ulong?> readCardBlock)`: `Primary`, `ClassicWhy`, `Results`, `Notes`, `Reading`, `VerifiedBytes`, `ToVerify`, `StartBackground()`, `Prepare(Clip, int dest)` → `TapLease?`, `Finish()`, `static ReadCardBlock(path, block, blockSize)`.
  - `OffloadReport.WithVerification(results, verified, primary, classicWhy, notes, finished, verifyPaused)`.
  - `Win32.BackgroundIo()` (`IDisposable`).
  - `Msg.UnstableRead`, `SameBlockWrong`, `CardNotReadAgain`, `CardGivesRecorded`, `CardGivesWhatDisksHave`, `CardGivesOther`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/VerifySessionTests.cs` (nový soubor)

```csharp
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Plan 10, the verification of stage 2 without a job (spec 3, 5, 6, 10): which disk is the primary one, what the
/// background reads, mode A and B through Prepare, the fill-in in Finish, the notes of spec 10, the report it gives.
public class VerifySessionTests
{
    const int Block = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, DeferVerify = true };

    /// A deferred copy of `files` onto two destinations: what stage 2 starts from.
    static (OffloadEngine Engine, OffloadPlan Plan, OffloadReport Report, string Card) Copy(TempDir dir, params (string Rel, int Size)[] files)
    {
        var card = dir.Sub("A001");
        foreach (var (rel, size) in files) TestFs.WriteRandom(Layout.Join(card, rel), size, size);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-08");
        return (engine, plan, engine.Run(plan), card);
    }

    static VerifySession Session(OffloadEngine e, OffloadPlan plan, OffloadReport report, int? preferred = null, string?[]? why = null,
        double?[]? rates = null, CancellationToken ct = default, Func<int, int, ulong?>? card = null) =>
        new(plan, report, e.Blocks, Block, rates ?? [1, 1], preferred, why ?? [null, null], new PauseGate(), ct, card ?? ((_, _) => null));

    static int Index(OffloadPlan p, string rel) => p.Scan.Files.ToList().FindIndex(f => f.RelPath == rel);

    [Fact]
    public void Primary_TheFastestThatMayBe_OrTheChosenOne()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("a.mov", 1000));

        using (var s = Session(e, plan, report, rates: [100, 200])) Assert.Equal(1, s.Primary);
        using (var s = Session(e, plan, report, preferred: 0, rates: [100, 200])) Assert.Equal(0, s.Primary);
        using (var s = Session(e, plan, report, why: [null, Msg.ClassicNetwork], rates: [100, 200]))
        {
            Assert.Equal(0, s.Primary);
            Assert.Equal([null, Msg.ClassicNetwork], s.ClassicWhy);
        }
        using (var s = Session(e, plan, report, preferred: 1, why: [null, Msg.ClassicNetwork])) Assert.Equal(0, s.Primary);   // not allowed: the fastest allowed
        using (var s = Session(e, plan, report, why: [Msg.ClassicNetwork, Msg.ClassicCacheCheck]))
        {
            Assert.Equal(-1, s.Primary);
            Assert.Equal([Msg.ClassicNetwork, Msg.ClassicCacheCheck], s.ClassicWhy);
        }
        using (var s = Session(e, plan, report, preferred: 0)) Assert.Equal([null, Msg.ClassicSecondary], s.ClassicWhy);
    }

    // Without any proxy: the background reads the other disk and the primary's files no proxy reads; Finish reads on the
    // primary what a proxy would have read.
    [Fact]
    public void NoProxy_BackgroundAndFillIn_VerifyEverything()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5), ("SOUND/b.wav", 2 * Block));
        using var s = Session(e, plan, report, preferred: 0);

        s.StartBackground();
        s.Finish();

        Assert.All(s.Results, d => Assert.All(d, r => Assert.Equal(FileState.Verified, r.State)));
        Assert.Equal(s.ToVerify, s.VerifiedBytes);
        Assert.Empty(s.Notes);
    }

    // Mode A: the URL serves the primary's copy, every byte read through it is verified.
    [Fact]
    public async Task ModeA_PrepareGivesTheTap_ReadThroughItIsVerified()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 3 * Block + 5));
        using var s = Session(e, plan, report, preferred: 1);
        var clip = new Clip { Name = "a", Path = "CLIP/a.mov", Format = ClipFormat.Video, Segments = ["CLIP/a.mov"] };
        Assert.Null(s.Prepare(clip, 0));   // the other disk: no tap

        byte[] got;
        using (var lease = s.Prepare(clip, 1)!)
        {
            using var http = new HttpClient();
            got = await http.GetByteArrayAsync(lease.Url);
        }
        s.Finish();

        Assert.Equal(File.ReadAllBytes(Layout.Join(plan.CardPaths[1], "CLIP/a.mov")), got);
        Assert.Equal(FileState.Verified, s.Results[1][Index(plan, "CLIP/a.mov")].State);
        Assert.Equal(FileState.Written, s.Results[0][Index(plan, "CLIP/a.mov")].State);   // never started: no background
    }

    // Mode B: Prepare reads the RAW clip's files through the cache (verified) before the SDK would decode them.
    [Fact]
    public void ModeB_PrepareWarmsAndVerifies()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("A001_0001.braw", 2 * Block + 9));
        using var s = Session(e, plan, report, preferred: 0);
        var clip = new Clip { Name = "A001_0001", Path = "A001_0001.braw", Format = ClipFormat.Braw, Segments = ["A001_0001.braw"] };

        using (var lease = s.Prepare(clip, 0)) Assert.Null(lease!.Url);
        File.Delete(Layout.Join(plan.CardPaths[0], "A001_0001.braw"));   // Finish must not need to read it again
        s.Finish();

        Assert.Equal(FileState.Verified, s.Results[0][0].State);
    }

    [Fact]
    public void FlippedByteOnTheSecondary_Failed_WithTheBlock()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 3 * Block));
        DiskFlip.Flip(Layout.Join(plan.CardPaths[1], "SOUND/b.wav"), Block + 3);
        using var s = Session(e, plan, report, preferred: 0);

        s.StartBackground();
        s.Finish();

        Assert.Equal(FileState.Verified, s.Results[0][0].State);
        Assert.Equal(FileState.Failed, s.Results[1][0].State);
        Assert.StartsWith("HASH MISMATCH: block 1 dest ", s.Results[1][0].Error);
    }

    // Spec 10: the same block wrong on both disks — the card is asked again and the note says what it gives.
    [Fact]
    public void SameBlockOnBothDisks_TheCardIsReadAgain()
    {
        using var dir = new TempDir();
        var (e, plan, report, card) = Copy(dir, ("SOUND/b.wav", 3 * Block));
        foreach (var p in plan.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/b.wav"), 2 * Block + 1);
        using var s = Session(e, plan, report, preferred: 0,
            card: (file, block) => VerifySession.ReadCardBlock(Layout.Join(card, plan.Scan.Files[file].RelPath), block, Block));

        s.StartBackground();
        s.Finish();

        var note = Assert.Single(s.Notes);
        Assert.StartsWith("SOUND/b.wav: blok 2 nesouhlasí stejně na d1", note);
        Assert.EndsWith(Msg.CardGivesRecorded, note);
    }

    [Fact]
    public void CardGone_TheNoteSaysSo()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block));
        foreach (var p in plan.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/b.wav"), 7);
        using var s = Session(e, plan, report, preferred: 0);

        s.StartBackground();
        s.Finish();

        Assert.EndsWith(Msg.CardNotReadAgain, Assert.Single(s.Notes));
    }

    [Fact]
    public void Cancelled_NothingRead_CopiesStayWritten()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("SOUND/b.wav", 2 * Block));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var s = Session(e, plan, report, preferred: 0, ct: cts.Token);

        s.StartBackground();
        s.Finish();

        Assert.All(s.Results, d => Assert.Equal(FileState.Written, d[0].State));
    }

    // The report of stage 2: the verdicts, the method per destination, no verifyPending; a copy left "written" in a
    // verified report is not "verified" — nor in the ASC MHL (spec 9: only verified files go in).
    [Fact]
    public void WithVerification_TheFinalReport_AndItsCommit()
    {
        using var dir = new TempDir();
        var (e, plan, report, _) = Copy(dir, ("CLIP/a.mov", 2 * Block), ("SOUND/b.wav", Block));
        using var s = Session(e, plan, report, preferred: 1);
        s.StartBackground();
        s.Finish();
        var results = s.Results.Select(d => d.ToArray()).ToList();
        results[0][Index(plan, "SOUND/b.wav")] = new DestFileResult(FileState.Written, null);   // as if a read never ended

        var final = report.WithVerification(results, verified: true, s.Primary, s.ClassicWhy, ["poznámka"], DateTime.UtcNow, TimeSpan.Zero);

        Assert.Null(final.VerifyPending);
        Assert.True(final.Verified);
        Assert.Equal([ReportDestination.ReadBack, ReportDestination.InProxy], final.Destinations.Select(d => d.VerifyMethod));
        Assert.Equal(Msg.ClassicSecondary, final.Destinations[0].VerifyNote);
        Assert.Equal((1, 1), (final.Destinations[0].FilesOK, final.Destinations[0].FilesFailed));
        Assert.Equal(["poznámka"], final.VerifyNotes);
        Assert.False(final.SafeToFormat);
        Assert.Null(final.SafeToFormatSeconds);
        Assert.True(report.VerifyPending);   // the stage-1 report is a different object, unchanged
        foreach (var p in plan.CardPaths) final.Commit(p);
        var mhl = Verification.MhlReader.Read(plan.CardPaths[0])!;
        Assert.False(mhl.Files.ContainsKey("SOUND/b.wav"));
        Assert.True(mhl.Files.ContainsKey("CLIP/a.mov"));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifySessionTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0246: The type or namespace name 'VerifySession' could not be found (are you missing a using directive or an assembly reference?)`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/VerifyInProxy/VerifySession.cs` (nový soubor)

```csharp
using System.IO.Hashing;

namespace Offload.Core.VerifyInProxy;

/// What the proxy loop gets for one clip from the session: mode A — the URL ffmpeg reads the clip from; mode B — nothing
/// (the clip's files were read and verified, the SDK finds them in the cache). Dispose when the render ended.
public sealed class TapLease(string? url, Action release) : IDisposable
{
    int released;
    public string? Url => url;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0) release();
    }
}

/// One job's verification in stage 2 when verify-in-proxy runs (spec 3, 5, 6; R1: part of OffloadJob's stage 2, not a
/// second engine). The primary destination (the fastest one that may be verified this way, or the one the user chose)
/// is verified by what the proxies read (mode A: RangeServer + TapFile; mode B: CacheWarm), then the fill-in reads what
/// they did not; every other destination is read in the background — one thread per volume, low I/O priority; the
/// primary's files that no proxy reads (sidecars, audio, other segments) are read on the primary volume's thread between
/// two proxies. Pause and cancel are honoured before every block. A destination that may not be verified from reads
/// that might come from the cache (UnbufferedCheck) is read in the background like a secondary one.
public sealed class VerifySession : IDisposable
{
    readonly OffloadPlan plan;
    readonly IReadOnlyList<SourceFile> files;
    readonly IReadOnlyList<string> cardPaths;
    readonly BlockManifest?[] manifests;
    readonly DestFileResult[][] results;
    readonly BlockVerifier?[][] verifiers;
    readonly PauseGate pauses;
    readonly CancellationToken ct;
    readonly Func<int, int, ulong?> cardBlock;
    readonly ManualResetEventSlim tapIdle = new(true);
    readonly List<Thread> threads = [];
    readonly long[] verifiedBytes;
    readonly HashSet<string> proxyFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Lock gate = new();
    RangeServer? server;
    int active;

    /// The destination verified through the proxies; -1 when none may be (every one is read back).
    public int Primary { get; }
    /// Per destination: why it is read back although verify-in-proxy runs (Czech, the report's verifyNote); null for the
    /// primary.
    public IReadOnlyList<string?> ClassicWhy { get; }
    /// Per destination, per file: the verdict — the copy's states ("written", "failed") until the reads decide.
    public IReadOnlyList<DestFileResult[]> Results => results;
    /// Spec 10, Czech: unstable reads and blocks wrong on several destinations, for the report.
    public List<string> Notes { get; } = [];
    /// A verification thread is reading now (a pause stands only when none is).
    public bool Reading => Volatile.Read(ref active) > 0;
    /// Bytes verified so far per destination, and what there is to verify (the "Dokončení ověření" bar).
    public long[] VerifiedBytes => Enumerable.Range(0, verifiedBytes.Length).Select(d => Interlocked.Read(ref verifiedBytes[d])).ToArray();
    public long[] ToVerify { get; }

    /// `stage1`: the copy's report (states "written"/"failed" per destination); `blocks`: per file the card's block hashes
    /// (OffloadEngine.Blocks); `unbuffered`/`rates`: per destination (OffloadEngine); `preferred`: the user's primary
    /// destination (0-based) or null = the fastest; `classicWhy`: per destination a reason to read it back (UnbufferedCheck
    /// or the setting) or null; `cardBlock`: the XXH3-64 of block b of file i read from the card again — null when the card
    /// is not the copied one any more (spec 10).
    public VerifySession(OffloadPlan plan, OffloadReport stage1, IReadOnlyList<ulong[]?> blocks, int blockSize, IReadOnlyList<double?> rates,
        int? preferred, IReadOnlyList<string?> classicWhy, PauseGate pauses, CancellationToken ct, Func<int, int, ulong?> cardBlock)
    {
        this.plan = plan;
        files = plan.Scan.Files;
        cardPaths = plan.CardPaths;
        this.pauses = pauses;
        this.ct = ct;
        this.cardBlock = cardBlock;
        int dests = cardPaths.Count;
        manifests = files.Select((f, i) => blocks[i] is { } b ? new BlockManifest(f.Size, blockSize, b) : null).ToArray();
        results = Enumerable.Range(0, dests).Select(d => files.Select((_, i) => StateOf(stage1.Files[i], d)).ToArray()).ToArray();
        verifiers = Enumerable.Range(0, dests).Select(_ => new BlockVerifier?[files.Count]).ToArray();
        verifiedBytes = new long[dests];
        ToVerify = Enumerable.Range(0, dests).Select(d => files.Where((_, i) => results[d][i].State == FileState.Written).Sum(f => f.Size)).ToArray();
        var eligible = Enumerable.Range(0, dests).Where(d => classicWhy[d] == null).ToList();
        Primary = preferred is int p && eligible.Contains(p) ? p
            : eligible.Count == 0 ? -1 : eligible.OrderByDescending(d => rates[d] ?? 0).ThenBy(d => d).First();
        ClassicWhy = Enumerable.Range(0, dests).Select(d => d == Primary ? null : classicWhy[d] ?? Msg.ClassicSecondary).ToList();
        if (Primary >= 0)
        {
            foreach (var clip in ClipDetector.Detect(files))
            {
                var mode = TapRegistry.ModeFor(clip);
                if (mode == TapMode.Http) proxyFiles.Add(clip.Path);
                else if (mode == TapMode.CacheWarm) proxyFiles.UnionWith(clip.Segments);
            }
        }
    }

    static DestFileResult StateOf(ReportFile f, int d) => (d < f.Destinations.Count ? f.Destinations[d] : "pending") switch
    {
        "written" when f.SourceError == null => new DestFileResult(FileState.Written, null),
        "verified" => new DestFileResult(FileState.Verified, null),
        "failed" => new DestFileResult(FileState.Failed, d < f.Errors.Count ? f.Errors[d] : null),
        _ => new DestFileResult(FileState.Pending, null),
    };

    /// Starts the background reads: every destination but the primary in full, and the primary's files no proxy reads —
    /// one thread per volume; the primary volume's thread waits while a proxy reads the primary.
    public void StartBackground()
    {
        var byVolume = new Dictionary<string, List<(int Dest, int File)>>(StringComparer.OrdinalIgnoreCase);
        string? primaryVolume = Primary >= 0 ? VolumeOf(Primary) : null;
        for (int d = 0; d < cardPaths.Count; d++)
        {
            var volume = VolumeOf(d);
            for (int i = 0; i < files.Count; i++)
            {
                if (results[d][i].State != FileState.Written || manifests[i] == null) continue;
                if (d == Primary && proxyFiles.Contains(files[i].RelPath)) continue;   // the proxy reads it (or the fill-in)
                (byVolume.TryGetValue(volume, out var list) ? list : byVolume[volume] = []).Add((d, i));
            }
        }
        foreach (var (volume, work) in byVolume)
        {
            bool gated = string.Equals(volume, primaryVolume, StringComparison.OrdinalIgnoreCase);
            var t = new Thread(() => Background(work, gated)) { IsBackground = true, Name = "offload.verify." + work[0].Dest };
            threads.Add(t);
            t.Start();
        }
    }

    string VolumeOf(int d) => NativeIO.VolumeId(cardPaths[d]) ?? cardPaths[d];

    void Background(List<(int Dest, int File)> work, bool gated)
    {
        Win32.BackgroundIo(true);
        Interlocked.Increment(ref active);
        try
        {
            foreach (var (d, i) in work)
            {
                if (ct.IsCancellationRequested) return;
                Verify(d, i, gated);
            }
        }
        finally
        {
            Interlocked.Decrement(ref active);
            Win32.BackgroundIo(false);
        }
    }

    /// Reads what destination `d`'s copy of file `i` still lacks and records the verdict; a cancel leaves it "written".
    void Verify(int d, int i, bool gated)
    {
        BlockVerifier v;
        lock (gate) v = verifiers[d][i] ??= new BlockVerifier(manifests[i]!);
        try
        {
            var outcome = BlockReads.Complete(Layout.Join(cardPaths[d], files[i].RelPath), v, () => Before(gated), n => Interlocked.Add(ref verifiedBytes[d], n), ct);
            lock (gate) results[d][i] = BlockReads.Result(outcome, v);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException)
        {
            lock (gate) results[d][i] = new DestFileResult(FileState.Failed, "verify: " + e.Message);
        }
    }

    /// Before every block: wait while a proxy reads the primary (`gated`), then at the pause gate; false = cancelled.
    bool Before(bool gated)
    {
        Interlocked.Decrement(ref active);
        try
        {
            if (gated) WaitHandle.WaitAny([tapIdle.WaitHandle, ct.WaitHandle]);
            return !ct.IsCancellationRequested && pauses.WaitWhilePaused(ct);
        }
        finally
        {
            Interlocked.Increment(ref active);
        }
    }

    /// Before the proxy of `clip` renders from destination `dest`: mode A gives the URL to read it from; mode B reads its
    /// files now (verified, then in the cache for the SDK). Null when the proxy reads another destination, the clip is
    /// not read through the session, or its copy here is not "written" — the fill-in verifies it then.
    public TapLease? Prepare(Clip clip, int dest)
    {
        if (dest != Primary || ct.IsCancellationRequested) return null;
        var mode = TapRegistry.ModeFor(clip);
        if (mode == TapMode.Http && FileIndex(clip.Path) is int i && Writable(i))
        {
            BlockVerifier v;
            lock (gate) v = verifiers[Primary][i] ??= new BlockVerifier(manifests[i]!);
            TapFile tap;
            try { tap = new TapFile(Layout.Join(cardPaths[Primary], clip.Path), v); }
            catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { return null; }
            lock (gate) server ??= new RangeServer();
            var url = server.Register(tap, Path.GetFileName(clip.Path));
            tapIdle.Reset();
            return new TapLease(url, () =>
            {
                server.Unregister(url);
                tap.Dispose();
                Interlocked.Add(ref verifiedBytes[Primary], v.VerifiedBytes);
                tapIdle.Set();
            });
        }
        if (mode == TapMode.CacheWarm)
        {
            var segments = clip.Segments.Select(FileIndex).OfType<int>().Where(Writable).ToList();
            if (segments.Count == 0 || !CacheWarm.Fits(segments.Sum(s => files[s].Size))) return null;
            tapIdle.Reset();
            try
            {
                foreach (var s in segments)
                {
                    BlockVerifier v;
                    lock (gate) v = verifiers[Primary][s] ??= new BlockVerifier(manifests[s]!);
                    try
                    {
                        CacheWarm.VerifyAndWarm(Layout.Join(cardPaths[Primary], files[s].RelPath), v, () => Before(gated: false),
                            n => Interlocked.Add(ref verifiedBytes[Primary], n), ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { }   // the fill-in decides
                }
            }
            finally
            {
                tapIdle.Set();
            }
            return new TapLease(null, () => { });
        }
        return null;
    }

    int? FileIndex(string rel)
    {
        for (int i = 0; i < files.Count; i++)
            if (string.Equals(files[i].RelPath, rel, StringComparison.OrdinalIgnoreCase)) return i;
        return null;
    }

    bool Writable(int i) => Primary >= 0 && results[Primary][i].State == FileState.Written && manifests[i] != null;

    /// After the proxies (spec 6): the fill-in reads on the primary what the proxies did not (the files a proxy was meant to
    /// read — the others are the background thread's), the background threads finish, then the notes of spec 10 —
    /// unstable reads, and a block wrong on several destinations compared with the card.
    public void Finish()
    {
        tapIdle.Set();
        if (Primary >= 0)
        {
            Interlocked.Increment(ref active);
            try
            {
                for (int i = 0; i < files.Count && !ct.IsCancellationRequested; i++)
                    if (proxyFiles.Contains(files[i].RelPath) && Writable(i)) Verify(Primary, i, gated: false);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
        foreach (var t in threads) t.Join();
        Annotate();
    }

    void Annotate()
    {
        for (int i = 0; i < files.Count; i++)
        {
            var path = files[i].RelPath;
            for (int d = 0; d < cardPaths.Count; d++)
                if (verifiers[d][i] is { UnstableBlocks: { Count: > 0 } unstable })
                    Notes.Add(Msg.UnstableRead(path, DiskOf(d), unstable));
            var wrong = new Dictionary<int, List<int>>();
            for (int d = 0; d < cardPaths.Count; d++)
                if (verifiers[d][i]?.Finish() is { Kind: BlockOutcomeKind.Mismatch, Block: >= 0 } m)
                    (wrong.TryGetValue(m.Block, out var list) ? list : wrong[m.Block] = []).Add(d);
            foreach (var (block, dests) in wrong.Where(w => w.Value.Count > 1))
            {
                var recorded = manifests[i]!.Blocks[block];
                var disks = dests.Select(d => verifiers[d][i]!.DiskHash(block)).Distinct().ToList();
                ulong? card;
                try { card = ct.IsCancellationRequested ? null : cardBlock(i, block); }
                catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { card = null; }
                Notes.Add(Msg.SameBlockWrong(path, block, string.Join(", ", dests.Select(DiskOf)), card switch
                {
                    null => Msg.CardNotReadAgain,
                    ulong c when c == recorded => Msg.CardGivesRecorded,
                    ulong c when disks.Count == 1 && c == disks[0] => Msg.CardGivesWhatDisksHave,
                    _ => Msg.CardGivesOther,
                }));
            }
        }
    }

    string DiskOf(int d) => History.HistoryText.DiskName(plan.DestRoots[d]);

    /// The XXH3-64 of block `block` (of `blockSize` bytes) of `path` on the card, read past the file cache.
    public static ulong ReadCardBlock(string path, int block, int blockSize)
    {
        using var reader = new FileReader(path);
        using var buffer = new AlignedBuffer(blockSize);
        int n = reader.Read(buffer.Span, (long)block * blockSize);
        return XxHash3.HashToUInt64(buffer.Span[..n]);
    }

    public void Dispose()
    {
        tapIdle.Set();
        foreach (var t in threads) t.Join();
        server?.Dispose();
        tapIdle.Dispose();
    }
}
```

#### `windows/src/Offload.Core/VerifyInProxy/OffloadReport.Verify.cs` (nový soubor)

```csharp
namespace Offload.Core;

public sealed partial class OffloadReport
{
    /// Plan 10: this report — written at the end of the copy with verifyPending — once the verification of stage 2 ended,
    /// as a new object (whoever holds this one, the UI, never sees it half changed). `results`: per destination, per file;
    /// `verified`: the verification ran to its end (false = cancelled: unread copies stay "written", as after a cancelled
    /// read-back); `primary`: the destination verified through the proxies (-1: none); `classicWhy`: per destination why it
    /// was read back instead.
    public OffloadReport WithVerification(IReadOnlyList<DestFileResult[]> results, bool verified, int primary, IReadOnlyList<string?> classicWhy,
        IReadOnlyList<string> notes, DateTime finished, TimeSpan verifyPaused)
    {
        var r = FromJson(ToJson());
        r.Clips = Clips;   // the thumbnails and the defect pictures are not in the JSON
        bool IsOk(FileState s) => s == FileState.Verified || (!verified && s == FileState.Written);
        for (int i = 0; i < r.Files.Count; i++)
        {
            var f = r.Files[i];
            f.Destinations = results.Select(d => d[i].State.Wire()).ToList();
            f.Errors = results.Select((d, n) => d[i].Error ?? (d[i].State == FileState.Failed && n < Files[i].Errors.Count ? Files[i].Errors[n] : null)).ToList();
        }
        for (int d = 0; d < r.Destinations.Count; d++)
        {
            var dest = r.Destinations[d];
            dest.FilesOK = results[d].Count(x => IsOk(x.State));
            dest.FilesFailed = results[d].Count(x => !IsOk(x.State));
            dest.VerifyMethod = !verified ? null : d == primary ? ReportDestination.InProxy : ReportDestination.ReadBack;
            dest.VerifyNote = verified && d != primary ? classicWhy[d] : null;
        }
        r.Verified = verified;
        r.VerifyPending = null;
        r.Finished = finished;
        r.VerifyPausedSeconds = verifyPaused > TimeSpan.Zero ? Math.Round(verifyPaused.TotalSeconds, 1) : null;
        r.SafeToFormatSeconds = r.SafeToFormat
            ? Math.Round(Math.Max(0, (finished - r.Started).TotalSeconds - r.PausedSeconds), 1) : null;
        r.VerifyNotes = notes.Count > 0 ? [.. notes] : null;
        return r;
    }
}
```

#### `windows/src/Offload.Core/Win32.cs`

Najít (1/1, řádky 56–61):

```csharp
    [LibraryImport("kernel32.dll")]
    internal static partial uint SetThreadExecutionState(uint flags);

    /// Explorer's natural order ("C2" before "C10") — the Windows counterpart of localizedStandardCompare.
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int StrCmpLogicalW(string a, string b);
```

Nahradit:

```csharp
    [LibraryImport("kernel32.dll")]
    internal static partial uint SetThreadExecutionState(uint flags);

    const int ThreadModeBackgroundBegin = 0x00010000;
    const int ThreadModeBackgroundEnd = 0x00020000;

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadPriority(IntPtr thread, int priority);

    /// Plan 10 (spec 6): the calling thread's disk reads at background priority (low I/O priority), or back to normal —
    /// the background verification never slows the proxy's reads. Best effort.
    internal static void BackgroundIo(bool on) => SetThreadPriority(GetCurrentThread(), on ? ThreadModeBackgroundBegin : ThreadModeBackgroundEnd);

    /// Explorer's natural order ("C2" before "C10") — the Windows counterpart of localizedStandardCompare.
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int StrCmpLogicalW(string a, string b);
```

#### `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs`

Najít (1/1, řádek 12):

```csharp
    public const string ClassicSecondary = "další cíl — ověřeno čtením na pozadí";
```

Nahradit:

```csharp
    public const string ClassicSecondary = "další cíl — ověřeno čtením na pozadí";

    // Spec 10 notes (report "verifyNotes", the job's warning).
    public static string UnstableRead(string path, string disk, IReadOnlyList<int> blocks) =>
        $"Nestabilní čtení: {path} na {disk} (blok {string.Join(", ", blocks)}) dal při jednom čtení jiná data — při dalším souhlasil s kartou.";
    public static string SameBlockWrong(string path, int block, string disks, string card) =>
        $"{path}: blok {block} nesouhlasí stejně na {disks} — {card}";
    public const string CardNotReadAgain = "kartu se nepodařilo přečíst znovu (už není ve čtečce?) — vložte ji a spusťte Zkopírovat znovu.";
    public const string CardGivesRecorded = "karta teď dává zaznamenaná data: vada vznikla při zápisu na disky.";
    public const string CardGivesWhatDisksHave = "karta teď dává stejná data jako disky: podezření na chybné čtení karty při kopii.";
    public const string CardGivesOther = "karta teď dává jiná data než při kopii i než disky: podezření na vadnou kartu nebo čtečku.";
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifySessionTests"`

Expected: PASS — `Failed: 0, Passed: 9, Skipped: 0, Total: 9` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1605` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 11: `VerifySession` — primární cíl (zvolený, jinak nejrychlejší při kopii), čtení ostatních cílů na pozadí s nízkou prioritou I/O, tap/ohřátí pro proxy, dorovnání a poznámky ke spec 10 (nestabilní čtení, stejný blok na víc cílech → nové čtení karty); `OffloadReport.WithVerification`.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: verify session with primary, background readers and fill-in (plan 10, task 11)

VerifySession picks the primary (chosen, else the fastest writer that may be verified in proxy),
reads the other destinations on background-priority threads, gives the proxy a tap (mode A) or a
warmed file (mode B), fills in the rest and writes the notes of spec 10 (unstable reads, the same
block wrong on several disks with the card read again). WithVerification builds the final report.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\VerifyInProxy\Msg.VerifyInProxy.cs windows\src\Offload.Core\VerifyInProxy\OffloadReport.Verify.cs windows\src\Offload.Core\VerifyInProxy\VerifySession.cs windows\src\Offload.Core\Win32.cs windows\tests\Offload.Core.Tests\VerifySessionTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 12: Úloha: ověření během proxy, „Lze vyjmout“ × „Lze formátovat“, zápis generace po ověření (R5, R11, spec 6)

`OffloadJob` spojí díly: s proxy, ověřením a ffmpeg (a mimo opravu) nastaví `DeferVerify`, po kopii je karta
**Lze vyjmout** (`SafeToEject` = vše zapsané na všech cílech), ale ne **Lze formátovat** (`SafeToFormat` = vše ověřené
na všech cílech, u doplnění a opravy celá záloha). 2. fáze spustí `VerifySession` (kontrola cache pro každý cíl), proxy
čtou primární cíl přes tap, po proxy je fáze `Finishing` („Dokončení ověření“: dočtení a čekání na ostatní disky).
Výsledek se zapíše do složek karet (`OffloadReport.Commit`) pod zámkem složek (`AppendLock`, čeká až 30 s na jiné
doplnění; neúspěch = varování „Výsledek ověření se nepodařilo zapsat…“, generaci dokončí doplnění — R6). Zrušení
zastaví i ověření; zapsané kopie zůstanou zapsané a neověřené. Pauza ve 2. fázi drží i vlákna ověření. Bez proxy,
s vypnutou volbou nebo pro opravu zůstává zpětné čtení hned po kopii (R11). Testy, které čekaly „nelze vysunout“ u
neověřené kopie, teď čekají „lze vyjmout, nelze formátovat“ (R5).

**Files:**
- Modify: `windows/src/Offload.Core/OffloadJob.cs:4-9, 44-52, 67-72, 86-93, 155-160, 251-256, 272-279, 413-419, 664-671, 690-712, 837-845, 963-970, 134-136, 345-348, 360, 365, 372, 451-452`
- Modify: `windows/src/Offload.Core/OffloadEngine.cs:35-45`
- Modify: `windows/src/Offload.Core/Desktop/JobProgress.cs:130-135, 186-191`
- Modify: `windows/src/Offload.Core/Desktop/SpaceCheck.cs:55-60`
- Modify: `windows/src/Offload.Core/Desktop/JobCard.cs:135-140, 147-153`
- Modify: `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs:22`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/VerifyInProxyJobTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/AppendEngineTests.cs:314-323`
- Test: `windows/tests/Offload.Core.Tests/RepairJobTests.cs:184-190, 196-204, 409-415`
- Test: `windows/tests/Offload.Core.Tests/StageTwoTests.cs:114-119, 358-364`

**Interfaces:**
- Consumes: `VerifySession` (úkol 11), `OffloadReport.Commit` (úkol 6), `ContentWatch` (úkol 10), `UnbufferedCheck` (úkol 5), `OffloadEngine.Blocks/WriteRates/WrittenUnbuffered` (úkol 4).
- Produces:
  - `JobConfig.VerifyInProxy` (výchozí `true`), `JobConfig.PrimaryDestination` (`int?`).
  - `JobPhase.Finishing` („Dokončení ověření“); `JobStatus.SafeToFormat`, `ContentWarnings`, `VerifiedBytes`, `VerifyTotal`; `JobStatus.SafeToEject` = R5 „Lze vyjmout“.
  - `OffloadJob.CacheCheck`, `CommitLockWait` (internal, testy); `OffloadEngine.Cancellation`, `CardStillThere()` (internal).
  - `Msg.VerifyNotCommitted(detail)`, `Msg.ContentWarnings(n)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/VerifyInProxyJobTests.cs` (nový soubor)

```csharp
using System.Text.Json.Nodes;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Plan 10 end to end: a job with proxies verifies its primary destination through the proxy's reads and the other in the
/// background; "Lze vyjmout" after the copy, "Lze formátovat" only once every copy is verified; the generation's ASC MHL
/// only then; a flipped byte, a vanished file, a cancel, an append — never a false green.
public class VerifyInProxyJobTests
{
    static OffloadOptions Options() => new()
    {
        ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264", CheckpointBlocks = 4,
    };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C001.mov"), seconds: 2, width: 640, height: 360);
        File.WriteAllText(Path.Combine(card, "CLIP", "A001C001.xml"), "<clip/>");
        TestFs.WriteRandom(Path.Combine(card, "SOUND", "A001.wav"), 300_000, 7);
        return card;
    }

    static JobConfig Config(string card, IReadOnlyList<string> dests, TempDir dir, int? primary = null) => new(card, dests)
    {
        Date = "2026-10-08",
        PrimaryDestination = primary,
        Proxy = new ProxySettings { Enabled = true, Height = 240, BurnFilename = false, BurnTimecode = false },
        Outputs = new OutputSettings { DayReport = false, ClipListCsv = false },
        HistoryFolder = dir.Sub("history"),
    };

    static JsonObject ReportJson(string cardPath) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json")))!.AsObject();

    static string[] MhlFiles(string cardPath) =>
        Directory.Exists(Path.Combine(cardPath, "ascmhl")) ? Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl") : [];

    [FfmpegFact]
    public void TwoDestinations_OnePrimaryInProxy_OtherInBackground_FormatOnlyAtTheEnd()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 1), Options());

        Assert.True(job.RunCopy());
        // Stage 1 ended: everything is on both disks — "Lze vyjmout", not "Lze formátovat"; no MHL generation yet.
        var mid = job.Status();
        Assert.True(mid.SafeToEject);
        Assert.False(mid.SafeToFormat);
        foreach (var p in job.Plan!.CardPaths)
        {
            var json = ReportJson(p);
            Assert.True((bool)json["verifyPending"]!);
            Assert.False((bool)json["verified"]!);
            Assert.Empty(MhlFiles(p));
            Assert.False(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
            var record = CardRecord.Load(p);
            Assert.False(record.Verified);          // never green before it is verified
            Assert.Empty(record.FailedFiles);       // and never red either
            Assert.Equal(3, ManifestFile.Files(ManifestFile.Read(p), 1).Count);
        }

        job.RunPost();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.True(s.SafeToFormat);
        var r = job.Report!;
        Assert.True(r.Verified);
        Assert.Null(r.VerifyPending);
        Assert.All(r.Files, f => Assert.All(f.Destinations, d => Assert.Equal("verified", d)));
        Assert.Equal([ReportDestination.ReadBack, ReportDestination.InProxy], r.Destinations.Select(d => d.VerifyMethod));
        Assert.Equal(Msg.ClassicSecondary, r.Destinations[0].VerifyNote);
        Assert.Null(r.Destinations[1].VerifyNote);
        Assert.All(r.Destinations, d => Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}$", d.VolumeSerial));
        Assert.NotNull(r.SafeToFormatSeconds);
        Assert.False(r.ContentWarnings.Count > 0);
        Assert.Equal(ContentResult.Ok, r.Clips.Single().Content!.State);
        foreach (var p in job.Plan.CardPaths)
        {
            Assert.Null(ReportJson(p)["verifyPending"]);
            Assert.Single(MhlFiles(p));
            Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
            Assert.True(CardRecord.Load(p).Verified);
        }
        var entry = Assert.Single(new HistoryStore(Path.Combine(dir.Path, "history")).Load());
        Assert.True(entry.Verified);
        Assert.True(Assert.Single(entry.Generations).Verified);
    }

    [FfmpegFact]
    public void FlippedByteOnThePrimary_FoundByTheProxyRead_Failed_EjectYesFormatNo()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        var clip = Layout.Join(job.Plan!.CardPaths[0], "CLIP/A001C001.mov");
        DiskFlip.Flip(clip, new FileInfo(clip).Length / 2);

        job.RunPost();

        var s = job.Status();
        Assert.True(s.SafeToEject);
        Assert.False(s.SafeToFormat);
        var f = job.Report!.Files.Single(x => x.Path == "CLIP/A001C001.mov");
        Assert.Equal(["failed", "verified"], f.Destinations);
        Assert.StartsWith("HASH MISMATCH: block ", f.Errors[0]);
        var mhl = MhlReader.Read(job.Plan.CardPaths[0])!;
        Assert.True(mhl.Files["CLIP/A001C001.mov"].Failed);
        Assert.False(MhlReader.Read(job.Plan.CardPaths[1])!.Files["CLIP/A001C001.mov"].Failed);
    }

    [FfmpegFact]
    public void FlippedByteOnTheSecondary_FoundInTheBackground()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        var wav = Layout.Join(job.Plan!.CardPaths[1], "SOUND/A001.wav");
        DiskFlip.Flip(wav, 1000);

        job.RunPost();

        Assert.False(job.Status().SafeToFormat);
        Assert.Equal(["verified", "failed"], job.Report!.Files.Single(x => x.Path == "SOUND/A001.wav").Destinations);
    }

    // Spec 10: the same block wrong on both disks — the card is read again and the report says what it gives.
    [FfmpegFact]
    public void SameBlockWrongOnBothDisks_TheCardIsAsked_NoteInTheReport()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options());
        Assert.True(job.RunCopy());
        foreach (var p in job.Plan!.CardPaths) DiskFlip.Flip(Layout.Join(p, "SOUND/A001.wav"), 70_000);

        job.RunPost();

        var notes = job.Report!.VerifyNotes!;
        Assert.Contains(notes, n => n.StartsWith("SOUND/A001.wav: blok 1 nesouhlasí stejně na ", StringComparison.Ordinal) && n.EndsWith(Msg.CardGivesRecorded));
        Assert.Contains(notes[0], job.Status().Warning);
    }

    // A disk that loses the file before it is read: failed there, nothing falsely green.
    [FfmpegFact]
    public void FileGoneOnTheSecondary_FailedThere_FormatNo()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir, primary: 0), Options());
        Assert.True(job.RunCopy());
        File.Delete(Layout.Join(job.Plan!.CardPaths[1], "CLIP/A001C001.xml"));

        job.RunPost();

        var f = job.Report!.Files.Single(x => x.Path == "CLIP/A001C001.xml");
        Assert.Equal("failed", f.Destinations[1]);
        Assert.StartsWith("verify: ", f.Errors[1]);
        Assert.False(job.Status().SafeToFormat);
    }

    // Cancelled during stage 2: the generation is still committed (an append and a repair need it), the unread copies
    // stay "written" — unverified, never green; "Zkopírovat znovu" reads them back later (plan 9 R3).
    [FfmpegFact]
    public void CancelledBeforeTheProxies_CommittedButUnverified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], dir), Options());
        Assert.True(job.RunCopy());
        job.Cancel();

        job.RunPost();

        var s = job.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.False(s.SafeToFormat);
        var p = job.Plan!.CardPaths[0];
        Assert.Single(MhlFiles(p));
        Assert.False((bool)ReportJson(p)["verified"]!);
        Assert.Null(ReportJson(p)["verifyPending"]);
        Assert.False(CardRecord.Load(p).Verified);
        Assert.Empty(CardRecord.Load(p).FailedFiles);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void WithoutProxies_OrSwitchedOff_ReadBackInStageOne(bool proxies, bool setting)
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 200_000, 1);
        var config = Config(card, [dir.Sub("d1")], dir);
        config.Proxy.Enabled = proxies;
        config.VerifyInProxy = setting;
        config.AnalyzeClips = false;
        var job = new OffloadJob(config, Options());

        job.Run();

        var r = job.Report!;
        Assert.True(r.Verified);
        Assert.Null(r.VerifyPending);
        Assert.Equal(ReportDestination.ReadBack, r.Destinations[0].VerifyMethod);
        Assert.True(job.Status().SafeToFormat);
    }

    // Spec 7, spec 11: a damaged clip from the camera — the copy is verified (bit for bit), the decoder's warning is
    // reported with its place, "Lze formátovat" stays (formatting asks for a confirmation in the app).
    [FfmpegFact]
    public void DamagedClipFromTheCamera_VerifiedAndContentWarn()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var path = Path.Combine(card, "M4ROOT", "CLIP", "C0001.MP4");
        TestMedia.MakeFaststart(path, seconds: 4);
        var bytes = File.ReadAllBytes(path);
        new Random(5).NextBytes(bytes.AsSpan(bytes.Length / 2, 20_000));
        File.WriteAllBytes(path, bytes);
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], dir), Options());

        job.Run();

        var s = job.Status();
        Assert.True(s.SafeToFormat);
        Assert.Equal(1, s.ContentWarnings);
        Assert.Contains(Msg.ContentWarnings(1), s.Warning);
        var c = Assert.Single(job.Report!.ContentWarnings);
        Assert.Equal("decode", c.Content!.Kind);
        Assert.StartsWith("01:00:0", c.Content.Timecode);
        Assert.Contains("\"content\"", File.ReadAllText(Path.Combine(job.Plan!.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    [FfmpegFact]
    public void Append_VerifiedInProxy_CommitsGenerationTwo()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var d1 = dir.Sub("d1");
        var first = new OffloadJob(Config(card, [d1], dir), Options());
        first.Run();
        Assert.True(first.Status().SafeToFormat);
        TestMedia.MakeClip(Path.Combine(card, "CLIP", "A001C002.mov"), seconds: 1, width: 640, height: 360);

        var config = Config(card, [d1], dir);
        config.AppendInto = first.Plan!.CardPaths;
        var append = new OffloadJob(config, Options());
        append.Run();

        var s = append.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToFormat);
        var p = first.Plan.CardPaths[0];
        Assert.Equal(2, MhlFiles(p).Length);
        Assert.Equal(ReportDestination.InProxy, append.Report!.Destinations[0].VerifyMethod);
        Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
        Assert.True(CardRecord.Load(p).Verified);
    }

    [FfmpegFact]
    public void CacheCheckFails_ThatDiskReadBack_WithTheReason()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options())
        {
            CacheCheck = (path, _) => path.Contains(@"\d1\", StringComparison.OrdinalIgnoreCase) ? Msg.ClassicCacheCheck : null,
        };

        job.Run();

        var r = job.Report!;
        Assert.Equal([ReportDestination.ReadBack, ReportDestination.InProxy], r.Destinations.Select(d => d.VerifyMethod));
        Assert.Equal(Msg.ClassicCacheCheck, r.Destinations[0].VerifyNote);
        Assert.True(job.Status().SafeToFormat);
    }

    // Pozastavit in stage 2: the verification threads stand at the gate too — the job says "Pozastaveno" only then.
    [FfmpegFact]
    public async Task PausedInStageTwo_HoldsThenFinishesVerified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var job = new OffloadJob(Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), Options());
        Assert.True(job.RunCopy());
        job.Pause();
        var post = Task.Run(() => job.RunPost());

        Assert.True(SpinWait.SpinUntil(() => job.Status().PauseHeld, TimeSpan.FromSeconds(30)));
        Assert.False(post.IsCompleted);
        job.Resume();
        await post.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(job.Status().SafeToFormat);
    }
}
```

#### `windows/tests/Offload.Core.Tests/AppendEngineTests.cs`

Najít (1/1, řádky 314–323):

```csharp

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal("2 soubory z dřívější zálohy nebyly ověřeny — doporučeno Ověřit zálohu.", append.Status().Warning);
        // Plan 9 fix round 1, item 1 (as ruling I2 for a repair): the new files are verified, the card is not —
        // no "kartu lze vysunout".
        Assert.True(append.Report!.SafeToFormat);
        Assert.False(append.Status().SafeToEject);
    }

    // Fix round 1, item 1: two appends into the same card folders at once (GUI + CLI). The second refuses before it
```

Nahradit:

```csharp

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal("2 soubory z dřívější zálohy nebyly ověřeny — doporučeno Ověřit zálohu.", append.Status().Warning);
        // Plan 9 fix round 1, item 1 (as ruling I2 for a repair): the new files are verified, the card is not — R5 (plan
        // 10): every file is on the disk, so "Lze vyjmout", but never "Lze formátovat".
        Assert.True(append.Report!.SafeToFormat);
        Assert.True(append.Status().SafeToEject);
        Assert.False(append.Status().SafeToFormat);
    }

    // Fix round 1, item 1: two appends into the same card folders at once (GUI + CLI). The second refuses before it
```

#### `windows/tests/Offload.Core.Tests/RepairJobTests.cs`

Najít (1/3, řádky 184–190):

```csharp
    // Ruling I2 on its own: a repair of a backup whose copy the repair could not read back (its read-back cancelled)
    // is not safe to eject; the warning says why.
    [Fact]
    public void RepairCancelledDuringVerify_NotSafeToEject_Warning()
    {
        using var dir = new TempDir();
        var failed = UnverifiedJobWithAWriteError(dir, dir.Sub("history"));
```

Nahradit:

```csharp
    // Ruling I2 on its own: a repair of a backup whose copy the repair could not read back (its read-back cancelled)
    // is not safe to eject; the warning says why.
    [Fact]
    public void RepairCancelledDuringVerify_EjectYes_FormatNo_Warning()
    {
        using var dir = new TempDir();
        var failed = UnverifiedJobWithAWriteError(dir, dir.Sub("history"));
```

Najít (2/3, řádky 196–204):

```csharp

        var s = repair.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.False(s.SafeToEject);
        Assert.Equal(Msg.UnverifiedEarlier(2), s.Warning);
        Assert.Null(JobOutcomes.CopyDone("A001", s with { Phase = JobPhase.Analyzing }, proxies: false));
    }

    // Fix round 1, item 1: a clip shot onto the card after the backup is in no report — the repair fixes what failed,
```

Nahradit:

```csharp

        var s = repair.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        // R5 (plan 10): the repaired copies are written — the card may be taken out — but not read back: never "Lze
        // formátovat".
        Assert.True(s.SafeToEject);
        Assert.False(s.SafeToFormat);
        Assert.Equal(Msg.UnverifiedEarlier(2), s.Warning);
    }

    // Fix round 1, item 1: a clip shot onto the card after the backup is in no report — the repair fixes what failed,
```

Najít (3/3, řádky 409–415):

```csharp
        append.Run();

        Assert.True(append.Report!.SafeToFormat);
        Assert.False(append.Status().SafeToEject);
        Assert.False(Assert.Single(new HistoryStore(history).Load()).Verified);
        Assert.All(paths, p => Assert.False(CardRecord.Load(p).Verified));
        Assert.All(append.DayReports, p => Assert.Contains("NEOVĚŘENO", File.ReadAllText(p)));
```

Nahradit:

```csharp
        append.Run();

        Assert.True(append.Report!.SafeToFormat);
        Assert.True(append.Status().SafeToEject);     // R5: every file is on the disks
        Assert.False(append.Status().SafeToFormat);   // but the cancelled append's file was never read back
        Assert.False(Assert.Single(new HistoryStore(history).Load()).Verified);
        Assert.All(paths, p => Assert.False(CardRecord.Load(p).Verified));
        Assert.All(append.DayReports, p => Assert.Contains("NEOVĚŘENO", File.ReadAllText(p)));
```

#### `windows/tests/Offload.Core.Tests/StageTwoTests.cs`

Najít (1/2, řádky 114–119):

```csharp
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Proxy = new ProxySettings { Enabled = true, Height = 360 },
        }, Options());
        Assert.True(job.RunCopy());
        var paths = job.Plan!.CardPaths;
```

Nahradit:

```csharp
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Proxy = new ProxySettings { Enabled = true, Height = 360 },
            PrimaryDestination = 0,   // plan 10: the proxy is rendered on the primary destination — here d1, not the faster one
        }, Options());
        Assert.True(job.RunCopy());
        var paths = job.Plan!.CardPaths;
```

Najít (2/2, řádky 358–364):

```csharp
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Verify = false, Proxy = new ProxySettings { Enabled = true, Height = 360 } }, options);
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.False(job.Status().SafeToEject);
        Assert.Equal(ProxyStatus.Done, Assert.Single(report!.Clips).ProxyStatus);
    }
}
```

Nahradit:

```csharp
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Verify = false, Proxy = new ProxySettings { Enabled = true, Height = 360 } }, options);
        var report = job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Status().SafeToEject);     // R5: copied
        Assert.False(job.Status().SafeToFormat);   // never read back
        Assert.Equal(ProxyStatus.Done, Assert.Single(report!.Clips).ProxyStatus);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifyInProxyJobTests|FullyQualifiedName~StageTwoTests|FullyQualifiedName~RepairJobTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~JobTests|FullyQualifiedName~JobPauseTests|FullyQualifiedName~HistoryJobTests|FullyQualifiedName~VerifyJobTests|FullyQualifiedName~JobProgressTests|FullyQualifiedName~SpaceCheckTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS1061: 'JobStatus' does not contain a definition for 'SafeToFormat' and no accessible extension method 'SafeToFormat' accepting a first argument of type 'JobStatus' could be found (are you missing a using directive or an assembly reference?)`; `CS0117: 'JobConfig' does not contain a definition for 'PrimaryDestination'`; `CS1061: 'JobConfig' does not contain a definition for 'VerifyInProxy' and no accessible extension method 'VerifyInProxy' accepting a first argument of type 'JobConfig' could be found (are you missing a using directive or an assembly reference?)` a další chyby téhož druhu (všechny jen v testech tohoto úkolu).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/OffloadJob.cs`

Najít (1/18, řádky 4–9):

```csharp
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Space;

namespace Offload.Core;

```

Nahradit:

```csharp
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Space;
using Offload.Core.VerifyInProxy;

namespace Offload.Core;

```

Najít (2/18, řádky 44–52):

```csharp
    public string? RepairCardIdentity { get; set; }
    /// Where the end of the job is announced (spec 4.3): the copy taken when the job was started.
    public Notify.NotifySettings Notify { get; set; } = new();
}

public enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Done, Failed, Cancelled }

public static class JobPhaseExtensions
{
```

Nahradit:

```csharp
    public string? RepairCardIdentity { get; set; }
    /// Where the end of the job is announced (spec 4.3): the copy taken when the job was started.
    public Notify.NotifySettings Notify { get; set; } = new();
    /// Plan 10 (R11): verify the primary destination from the proxies' reads and the others in the background, while
    /// the proxies render — on when proxies and verification are; settings "Ověřit primární disk během proxy", CLI
    /// --classic-verify turns it off (the read-back of every destination right after the copy, as before 0.6.0).
    public bool VerifyInProxy { get; set; } = true;
    /// Plan 10 (spec 6): the destination the proxies read and verify (0-based, CLI --primary N); null = the fastest one
    /// measured during the copy.
    public int? PrimaryDestination { get; set; }
}

/// `Finishing` (plan 10): after the proxies, the verification reads what they did not and waits for the other disks.
public enum JobPhase { Waiting, Copying, Flushing, Verifying, Reporting, Analyzing, Proxies, Finishing, Done, Failed, Cancelled }

public static class JobPhaseExtensions
{
```

Najít (3/18, řádky 67–72):

```csharp
        JobPhase.Reporting => "Report",
        JobPhase.Analyzing => "Analýza klipů",
        JobPhase.Proxies => "Render proxy",
        JobPhase.Done => "Hotovo",
        JobPhase.Failed => "Chyba",
        _ => "Zrušeno",
```

Nahradit:

```csharp
        JobPhase.Reporting => "Report",
        JobPhase.Analyzing => "Analýza klipů",
        JobPhase.Proxies => "Render proxy",
        JobPhase.Finishing => "Dokončení ověření",
        JobPhase.Done => "Hotovo",
        JobPhase.Failed => "Chyba",
        _ => "Zrušeno",
```

Najít (4/18, řádky 86–93):

```csharp
    public double ClipFraction { get; init; }
    public int ProxiesDone { get; init; }
    public int ProxiesFailed { get; init; }
    /// All files are on all destinations and verified — the card may be ejected even while proxies render.
    public bool SafeToEject { get; init; }
    public bool? CopySucceeded { get; init; }
    public string? Error { get; init; }
    /// Information for the job card, e.g. "Nic nového — záloha je úplná."
```

Nahradit:

```csharp
    public double ClipFraction { get; init; }
    public int ProxiesDone { get; init; }
    public int ProxiesFailed { get; init; }
    /// R5 "Lze vyjmout": every file is copied onto every destination (written and flushed) — the card may be taken out,
    /// also while the verification still runs. Not "Lze formátovat".
    public bool SafeToEject { get; init; }
    /// R5 "Lze formátovat": every file verified on every destination, none failed (and the whole backup so, for an append
    /// or a repair) — the one green verdict of a card (spec 2). With verify-in-proxy only at the end of stage 2.
    public bool SafeToFormat { get; init; }
    /// Spec 7: clips whose decoder reported a defect (content_warn) — formatting needs the user's confirmation.
    public int ContentWarnings { get; init; }
    /// Plan 10 "Dokončení ověření": bytes verified so far and to verify (all destinations).
    public long VerifiedBytes { get; init; }
    public long VerifyTotal { get; init; }
    public bool? CopySucceeded { get; init; }
    public string? Error { get; init; }
    /// Information for the job card, e.g. "Nic nového — záloha je úplná."
```

Najít (5/18, řádky 155–160):

```csharp
    internal Func<IDisposable> KeepAwake { get; init; } = NativeIO.KeepAwake;
    /// The whole backup's state after "Nic k opravě" (tests replace it to make it unreadable).
    internal Func<IReadOnlyList<string>, BackupState> ReadBackupState { get; init; } = BackupState.Of;

    public OffloadJob(JobConfig config)
        : this(config, new OffloadOptions
```

Nahradit:

```csharp
    internal Func<IDisposable> KeepAwake { get; init; } = NativeIO.KeepAwake;
    /// The whole backup's state after "Nic k opravě" (tests replace it to make it unreadable).
    internal Func<IReadOnlyList<string>, BackupState> ReadBackupState { get; init; } = BackupState.Of;
    /// Tests: UnbufferedCheck.Problem — whether a destination may be verified from the proxies' reads (spec 8, R8).
    internal Func<string, bool, string?> CacheCheck { get; init; } = UnbufferedCheck.Problem;
    /// Tests: how long stage 2 tries to take the card folders' lock before it commits the verified generation.
    internal TimeSpan CommitLockWait { get; init; } = TimeSpan.FromSeconds(30);

    public OffloadJob(JobConfig config)
        : this(config, new OffloadOptions
```

Najít (6/18, řádky 251–256):

```csharp
        bool wasStarted;
        lock (gate) { s = state; wasStarted = started; }
        bool stage1 = s.Phase is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting;
        if (stage1)
        {
            var snap = Engine.Snapshot();
```

Nahradit:

```csharp
        bool wasStarted;
        lock (gate) { s = state; wasStarted = started; }
        bool stage1 = s.Phase is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting;
        if (s.Phase == JobPhase.Finishing && session is { } verifying)
            s = s with { VerifiedBytes = verifying.VerifiedBytes.Sum(), VerifyTotal = verifying.ToVerify.Sum() };
        if (stage1)
        {
            var snap = Engine.Snapshot();
```

Najít (7/18, řádky 272–279):

```csharp
        if (s.Phase == JobPhase.Waiting && !wasStarted && IsCancelled) s = s with { Phase = JobPhase.Cancelled };
        var pauses = Engine.Pauses;
        bool running = s.Phase != JobPhase.Waiting && !s.Phase.IsFinished();
        // Stage 1 stands only when the destinations are idle too (the engine's snapshot says so — fix round 1).
        bool held = stage1 ? s.Copy!.PauseHeld : pauses.IsHolding;
        return s with { Paused = running && pauses.IsPaused, PauseHeld = running && held, PausedTotal = pauses.PausedTotal };
    }

```

Nahradit:

```csharp
        if (s.Phase == JobPhase.Waiting && !wasStarted && IsCancelled) s = s with { Phase = JobPhase.Cancelled };
        var pauses = Engine.Pauses;
        bool running = s.Phase != JobPhase.Waiting && !s.Phase.IsFinished();
        // Stage 1 stands only when the destinations are idle too (the engine's snapshot says so — fix round 1); stage 2 only
        // when no verification thread is mid-block (plan 10).
        bool held = stage1 ? s.Copy!.PauseHeld : pauses.IsHolding && session?.Reading != true;
        return s with { Paused = running && pauses.IsPaused, PauseHeld = running && held, PausedTotal = pauses.PausedTotal };
    }

```

Najít (8/18, řádky 413–419):

```csharp
                log?.Invoke($"backup state: {x.Message}");
                Update(s => s.WithWarning(Msg.BackupStateUnreadable(x.Message)));
            }
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message, SafeToEject = s.Warning == null });
            return false;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
```

Nahradit:

```csharp
                log?.Invoke($"backup state: {x.Message}");
                Update(s => s.WithWarning(Msg.BackupStateUnreadable(x.Message)));
            }
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message, SafeToEject = s.Warning == null, SafeToFormat = s.Warning == null });
            return false;
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
```

Najít (9/18, řádky 664–671):

```csharp
        store.Load().FirstOrDefault(e => e.Destinations.Any(d => cardPaths.Any(p =>
            string.Equals(d.CardPath.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))));

    /// Stage 2 — clip analysis, thumbnails, optional proxies, PDF. Reads only from the destinations.
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it).
    public void RunPost(Action<string>? log = null)
    {
        if (preview != null) return;
```

Nahradit:

```csharp
        store.Load().FirstOrDefault(e => e.Destinations.Any(d => cardPaths.Any(p =>
            string.Equals(d.CardPath.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))));

    /// Plan 10 (R11): whether this job verifies with the proxies — proxies and verification on, verify-in-proxy not switched
    /// off, ffmpeg found, and not a repair (a repair has no stage 2 and always reads back, ruling M16).
    bool VerifiesInProxy(OffloadPlan plan) =>
        Config.Verify && Config.VerifyInProxy && Config.Proxy.Enabled && plan.Mode != PlanMode.Repair && Config.Tools.Resolved().Ffmpeg != null;

    /// Stage 2 — clip analysis, thumbnails, optional proxies, PDF. Reads only from the destinations.
    /// A failure here never changes the copy's result (SafeToEject stays as stage 1 left it). With verify-in-proxy the
    /// verification ends here too (FinishVerification) — also when the analysis or the proxies failed or were cancelled.
    public void RunPost(Action<string>? log = null)
    {
        if (preview != null) return;
```

Najít (10/18, řádky 690–712):

```csharp
        }
        if (Plan is not { } plan || Report is not { } report) return;
        using var awake = KeepAwake();
        try
        {
            var final = AnalyzeAndRender(plan, report, log);
            Report = final;
            Engine.WaitWhilePaused();   // before the PDF and the day report (a cancel meanwhile still writes them)
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        catch (Exception e)
        {
            log?.Invoke($"stage 2 failed: {e.Message}");
            WriteClipLists(plan, log);   // the generation must not be missing from the lists
            WritePdfReports(report, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = JobPhase.Failed, Error = e.Message });
        }
    }

```

Nahradit:

```csharp
        }
        if (Plan is not { } plan || Report is not { } report) return;
        using var awake = KeepAwake();
        using var verifying = session = StartVerification(plan, report);
        OffloadReport final = report;
        Exception? failed = null;
        try
        {
            final = AnalyzeAndRender(plan, report, log);
        }
        catch (Exception e)
        {
            failed = e;
            log?.Invoke($"stage 2 failed: {e.Message}");
        }
        if (verifying != null) final = FinishVerification(verifying, plan, final, log);
        Report = final;
        if (failed == null)
        {
            Engine.WaitWhilePaused();   // before the PDF and the day report (a cancel meanwhile still writes them)
            Update(s => s with { Phase = JobPhase.Reporting });
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = IsCancelled ? JobPhase.Cancelled : JobPhase.Done });
        }
        else
        {
            WriteClipLists(plan, log);   // the generation must not be missing from the lists
            WritePdfReports(final, plan, log);
            RegenerateDayReports(plan, log);
            Update(s => s with { Phase = JobPhase.Failed, Error = failed.Message });
        }
    }

    /// Plan 10: the session of a copy whose reports say verifyPending — every destination's cache check first (spec 8),
    /// then the background reads start; null when this job verified in stage 1.
    VerifySession? StartVerification(OffloadPlan plan, OffloadReport report)
    {
        if (report.VerifyPending != true) return null;
        var paths = plan.CardPaths;
        var why = paths.Select((p, d) => Config.VerifyInProxy ? CacheCheck(p, d < Engine.WrittenUnbuffered.Count && Engine.WrittenUnbuffered[d]) : Msg.ClassicSetting).ToList();
        var s = new VerifySession(plan, report, Engine.Blocks, Engine.Options.ChunkSize, Engine.WriteRates, Config.PrimaryDestination, why,
            Engine.Pauses, Engine.Cancellation, (file, block) => Engine.CardStillThere() != false   // a folder source has no card to lose
                ? VerifySession.ReadCardBlock(Layout.Join(plan.Source, plan.Scan.Files[file].RelPath), block, Engine.Options.ChunkSize)
                : null);
        s.StartBackground();
        return s;
    }

    /// Plan 10, the end of the verification (spec 6): the fill-in and the background reads end, the report gets the
    /// verdicts and is committed — reports, ASC MHL generation, hash lists (OffloadReport.Commit) — under the card folders'
    /// lock, history learns it, and the card's verdict "Lze formátovat" is decided. Never throws: a commit that fails
    /// leaves that folder's generation uncommitted (an append or "Zkopírovat znovu" resumes it, R6) and says so.
    OffloadReport FinishVerification(VerifySession s, OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        Update(st => st with { Phase = JobPhase.Finishing });
        try { s.Finish(); }
        catch (Exception e) { log?.Invoke($"verification: {e.Message}"); }
        var final = report.WithVerification(s.Results, !IsCancelled, s.Primary, s.ClassicWhy, s.Notes, DateTime.UtcNow,
            Engine.Pauses.PausedTotal - pausedAtCopyEnd);
        foreach (var note in s.Notes) log?.Invoke(note);
        AppendLock? folders = null;
        var deadline = DateTime.UtcNow + CommitLockWait;
        while (folders == null)
        {
            try { folders = AppendLock.Acquire(plan.CardPaths); }
            catch (OffloadError e) when (e.Code == PreflightCode.AppendBusy && DateTime.UtcNow < deadline) { Thread.Sleep(500); }
            catch (OffloadError e)
            {
                Update(st => st.WithWarning(Msg.VerifyNotCommitted(e.Message)));
                return final;
            }
        }
        using (folders)
        {
            foreach (var path in plan.CardPaths)
            {
                try { final.Commit(path); }
                catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException or OffloadError)
                {
                    log?.Invoke($"commit {path}: {e.Message}");
                    Update(st => st.WithWarning(Msg.VerifyNotCommitted($"{path}: {e.Message}")));
                }
            }
            RecordVerified(plan, final, log);
        }
        bool safe = final.SafeToFormat && (plan.Mode != PlanMode.Append || plan.UnverifiedEarlier == 0);
        int warnings = final.ContentWarnings.Count;
        if (warnings > 0) Update(st => st.WithWarning(Msg.ContentWarnings(warnings)));
        if (s.Notes.Count > 0) Update(st => st.WithWarning(s.Notes[0]));
        Update(st => st with { SafeToFormat = safe, ContentWarnings = warnings });
        return final;
    }

    /// History learns the verification (fix round 2, item C: as the card folders' reports say it now).
    void RecordVerified(OffloadPlan plan, OffloadReport report, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        try
        {
            var store = Store(log);
            if (EntryOf(store, plan.CardPaths) is not { } found) return;
            var whole = BackupState.Of(plan.CardPaths);
            store.Update(found.Id, existing =>
            {
                if (existing == null) return null;
                Apply(existing, whole);
                existing.Finished = report.Finished;
                foreach (var g in existing.Generations.Where(g => g.Seq == plan.Generation)) g.Verified = report.Verified;
                return existing;
            });
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
        }
    }

```

Najít (11/18, řádky 837–845):

```csharp
        var cardPaths = plan.CardPaths;
        var fileIndex = report.Files.Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i);
        bool Good(string state) => state == "verified" || (!report.Verified && state == "written");
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card.
        int? GoodDest(Clip clip)
        {
            for (int d = 0; d < cardPaths.Count; d++)
                if (clip.Segments.All(seg => fileIndex.TryGetValue(seg, out var fi) && Good(report.Files[fi].Destinations[d]))) return d;
            return null;
```

Nahradit:

```csharp
        var cardPaths = plan.CardPaths;
        var fileIndex = report.Files.Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i);
        bool Good(string state) => state == "verified" || (!report.Verified && state == "written");
        var verifying = session;
        /// First destination that holds a good copy of every segment — clips are read from disk, never from the card. With
        /// verify-in-proxy the primary destination first (its copy is verified by this read, spec 5).
        int? GoodDest(Clip clip)
        {
            if (verifying is { Primary: >= 0 and var p } && clip.Segments.All(seg => fileIndex.TryGetValue(seg, out var fi) && Good(report.Files[fi].Destinations[p])))
                return p;
            for (int d = 0; d < cardPaths.Count; d++)
                if (clip.Segments.All(seg => fileIndex.TryGetValue(seg, out var fi) && Good(report.Files[fi].Destinations[d]))) return d;
            return null;
```

Najít (12/18, řádky 963–970):

```csharp
            var sw = Stopwatch.StartNew();
            try
            {
                renderer.Render(clip, cardPaths[d], info, entries[i].Lut, Layout.Join(cardPaths[d], rel),
                    f => Update(s => s with { ClipFraction = f }), () => IsCancelled);
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                // Spec 3.4: relinking goes by name - a proxy that had to be renamed must be attached by hand.
```

Nahradit:

```csharp
            var sw = Stopwatch.StartNew();
            try
            {
                // Plan 10: the primary's copy is verified by this read (mode A: ffmpeg reads through the tap; mode B: read
                // and verified before the SDK); spec 7: the decoder's words are the clip's content verdict.
                var content = new ContentWatch();
                using (var lease = verifying?.Prepare(clip, d))
                    renderer.Render(clip, cardPaths[d], info, entries[i].Lut, Layout.Join(cardPaths[d], rel),
                        f => Update(s => s with { ClipFraction = f }), () => IsCancelled, lease?.Url, content);
                entries[i].Content = content.Verdict(info);
                if (entries[i].Content!.IsWarn) entries[i].Content!.Jpeg = renderer.Snapshot(clip, cardPaths[d], info, entries[i].Content!.Frame ?? 0);
                entries[i].ProxyStatus = ProxyStatus.Done;
                entries[i].ProxyPath = rel;
                // Spec 3.4: relinking goes by name - a proxy that had to be renamed must be attached by hand.
```

Najít (13/18, řádky 134–136):

```csharp
    AppendPlan? appendPlan;

    /// Set on a worker thread
```

Nahradit:

```csharp
    AppendPlan? appendPlan;
    /// Plan 10: the verification running with the proxies (stage 2), null otherwise; the paused time when the copy ended.
    volatile VerifySession? session;
    TimeSpan pausedAtCopyEnd;

    /// Set on a worker thread
```

Najít (14/18, řádky 345–348):

```csharp
            copying = true;
            var report = Engine.Run(plan);
            Report = report;
            bool safe = report.SafeToFormat;
```

Nahradit:

```csharp
            copying = true;
            // Plan 10 (R11): the read-back waits for stage 2, where the proxies read the copies.
            Engine.Options.DeferVerify = VerifiesInProxy(plan);
            var report = Engine.Run(plan);
            Report = report;
            pausedAtCopyEnd = Engine.Pauses.PausedTotal;
            // R5: "Lze formátovat" only verified everywhere; "Lze vyjmout" once everything is on every disk.
            bool safe = report.SafeToFormat;
            bool allCopied = report.Success;
```

Najít (15/18, řádek 360):

```csharp
                safe &= w.Failed == 0 && w.Unverified == 0;
```

Nahradit:

```csharp
                safe &= w.Failed == 0 && w.Unverified == 0;
                allCopied &= w.Failed == 0;
```

Najít (16/18, řádek 365):

```csharp
                safe &= plan.NotInBackup == 0;
```

Nahradit:

```csharp
                safe &= plan.NotInBackup == 0;
                allCopied &= plan.NotInBackup == 0;
```

Najít (17/18, řádek 372):

```csharp
            Update(s => s with { SafeToEject = safe, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
```

Nahradit:

```csharp
            Update(s => s with { SafeToEject = allCopied, SafeToFormat = safe, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
```

Najít (18/18, řádky 451–452):

```csharp
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message, SafeToEject = s.Warning == null });
            return false;
```

Nahradit:

```csharp
            // Conservative (no false green): any warning keeps both verdicts off, as before plan 10.
            Update(s => s with { Phase = JobPhase.Done, Note = e.Message, SafeToEject = s.Warning == null, SafeToFormat = s.Warning == null });
            return false;
```

#### `windows/src/Offload.Core/OffloadEngine.cs`

Najít (1/1, řádky 35–45):

```csharp
    /// Waits while paused; false when the job was cancelled meanwhile (stage 2 uses it before every clip).
    public bool WaitWhilePaused() => Pauses.WaitWhilePaused(cts.Token);

    /// After Run (plan 10): per file, the XXH3-64 of its blocks as the card gave them (null when it was not read to the
    /// end); per destination, its write speed and whether it was written past the file cache.
    public ulong[]?[] Blocks { get; private set; } = [];
    public IReadOnlyList<double?> WriteRates { get; private set; } = [];
    public IReadOnlyList<bool> WrittenUnbuffered { get; private set; } = [];

    /// The file error (report JSON, English like every file error) of the files left out because the card in the drive
    /// was not the copied one after a pause (ruling I1); the HTML says it in Czech.
```

Nahradit:

```csharp
    /// Waits while paused; false when the job was cancelled meanwhile (stage 2 uses it before every clip).
    public bool WaitWhilePaused() => Pauses.WaitWhilePaused(cts.Token);

    /// Cancelled with Cancel(): the verification of stage 2 stops on it too (plan 10).
    internal CancellationToken Cancellation => cts.Token;

    /// After Run (plan 10): per file, the XXH3-64 of its blocks as the card gave them (null when it was not read to the
    /// end); per destination, its write speed and whether it was written past the file cache.
    public ulong[]?[] Blocks { get; private set; } = [];
    public IReadOnlyList<double?> WriteRates { get; private set; } = [];
    public IReadOnlyList<bool> WrittenUnbuffered { get; private set; } = [];

    /// Ruling I1, asked again in stage 2 (plan 10, spec 10): is the copied card still in the drive? Null = never asked.
    internal bool? CardStillThere() => SameCard is null ? null : StillSameCard();

    /// The file error (report JSON, English like every file error) of the files left out because the card in the drive
    /// was not the copied one after a pause (ruling I1); the HTML says it in Czech.
```

#### `windows/src/Offload.Core/Desktop/JobProgress.cs`

Najít (1/2, řádky 130–135):

```csharp
        { Phase: JobPhase.Verifying, Copy: { TotalBytes: > 0 } v } =>
            v.ToVerifyTotal == 0 ? 1 : v.VerifiedBytes.Sum() / (double)v.ToVerifyTotal,
        { Phase: JobPhase.Analyzing or JobPhase.Proxies, ClipCount: > 0 } s => (s.ClipIndex + s.ClipFraction) / s.ClipCount,
        { Phase: JobPhase.Done } => 1,
        _ => 0,
    };
```

Nahradit:

```csharp
        { Phase: JobPhase.Verifying, Copy: { TotalBytes: > 0 } v } =>
            v.ToVerifyTotal == 0 ? 1 : v.VerifiedBytes.Sum() / (double)v.ToVerifyTotal,
        { Phase: JobPhase.Analyzing or JobPhase.Proxies, ClipCount: > 0 } s => (s.ClipIndex + s.ClipFraction) / s.ClipCount,
        { Phase: JobPhase.Finishing, VerifyTotal: > 0 } f => (double)f.VerifiedBytes / f.VerifyTotal,
        { Phase: JobPhase.Done } => 1,
        _ => 0,
    };
```

Najít (2/2, řádky 186–191):

```csharp
                JobPhase.Analyzing => $"Klip {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName}",
                JobPhase.Proxies =>
                    $"Proxy {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %",
                _ => s.Phase.Label() + "…",
            };
        }
```

Nahradit:

```csharp
                JobPhase.Analyzing => $"Klip {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName}",
                JobPhase.Proxies =>
                    $"Proxy {s.ClipIndex + 1}/{s.ClipCount} · {s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %",
                JobPhase.Finishing => $"Ověřeno {Format.BytesCs3(s.VerifiedBytes)} / {Format.BytesCs3(s.VerifyTotal)}",
                _ => s.Phase.Label() + "…",
            };
        }
```

#### `windows/src/Offload.Core/Desktop/SpaceCheck.cs`

Najít (1/1, řádky 55–60):

```csharp
        JobPhase.Waiting => need,
        JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting => need with { Card = Math.Max(0, need.Card - written) },
        JobPhase.Analyzing or JobPhase.Proxies => need with { Card = 0 },
        _ => SpaceNeed.None,
    };
}
```

Nahradit:

```csharp
        JobPhase.Waiting => need,
        JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting => need with { Card = Math.Max(0, need.Card - written) },
        JobPhase.Analyzing or JobPhase.Proxies => need with { Card = 0 },
        JobPhase.Finishing => need with { Card = 0, Proxy = 0 },   // plan 10: the proxies are done, the report is left
        _ => SpaceNeed.None,
    };
}
```

#### `windows/src/Offload.Core/Desktop/JobCard.cs`

Najít (1/2, řádky 135–140):

```csharp
            JobPhase.Copying when c != null => ($"Soubor {Math.Min(c.FileIndex + 1, c.TotalFiles)} / {c.TotalFiles}", Path.GetFileName(c.CurrentFile)),
            JobPhase.Flushing => ("Zápis cache disků", ""),
            JobPhase.Verifying when c != null => ("Ověřování", $"{Format.BytesCs3(c.VerifiedBytes.Sum())} / {Format.BytesCs3(c.ToVerifyTotal)}"),
            JobPhase.Analyzing => ($"Klip {s.ClipIndex + 1} / {s.ClipCount}", s.ClipName),
            JobPhase.Proxies => ($"Proxy {s.ClipIndex + 1} / {s.ClipCount}", $"{s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %"),
            JobPhase.Reporting => ("Report", ""),
```

Nahradit:

```csharp
            JobPhase.Copying when c != null => ($"Soubor {Math.Min(c.FileIndex + 1, c.TotalFiles)} / {c.TotalFiles}", Path.GetFileName(c.CurrentFile)),
            JobPhase.Flushing => ("Zápis cache disků", ""),
            JobPhase.Verifying when c != null => ("Ověřování", $"{Format.BytesCs3(c.VerifiedBytes.Sum())} / {Format.BytesCs3(c.ToVerifyTotal)}"),
            JobPhase.Finishing => ("Dokončení ověření", $"{Format.BytesCs3(s.VerifiedBytes)} / {Format.BytesCs3(s.VerifyTotal)}"),
            JobPhase.Analyzing => ($"Klip {s.ClipIndex + 1} / {s.ClipCount}", s.ClipName),
            JobPhase.Proxies => ($"Proxy {s.ClipIndex + 1} / {s.ClipCount}", $"{s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %"),
            JobPhase.Reporting => ("Report", ""),
```

Najít (2/2, řádky 147–153):

```csharp
    public static string? Remaining(JobPhase phase, double? phaseSeconds, double? totalSeconds)
    {
        if (phaseSeconds is not double t) return totalSeconds is double only ? $"zbývá ~{JobProgress.EtaText(only)} celkem" : null;
        var what = phase switch { JobPhase.Copying => "kopie", JobPhase.Verifying => "ověření", _ => phase.Label().ToLower(Cs) };
        var text = $"zbývá ~{JobProgress.EtaText(t)} {what}";
        return totalSeconds is double total && total > t ? $"{text} · ~{JobProgress.EtaText(total)} celkem" : text;
    }
```

Nahradit:

```csharp
    public static string? Remaining(JobPhase phase, double? phaseSeconds, double? totalSeconds)
    {
        if (phaseSeconds is not double t) return totalSeconds is double only ? $"zbývá ~{JobProgress.EtaText(only)} celkem" : null;
        var what = phase switch { JobPhase.Copying => "kopie", JobPhase.Verifying or JobPhase.Finishing => "ověření", _ => phase.Label().ToLower(Cs) };
        var text = $"zbývá ~{JobProgress.EtaText(t)} {what}";
        return totalSeconds is double total && total > t ? $"{text} · ~{JobProgress.EtaText(total)} celkem" : text;
    }
```

#### `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs`

Najít (1/1, řádek 22):

```csharp
    public const string CardGivesOther = "karta teď dává jiná data než při kopii i než disky: podezření na vadnou kartu nebo čtečku.";
```

Nahradit:

```csharp
    public const string CardGivesOther = "karta teď dává jiná data než při kopii i než disky: podezření na vadnou kartu nebo čtečku.";

    /// The job's warning when the verified generation could not be written into a card folder (it stays uncommitted:
    /// an append or "Zkopírovat znovu" finishes it, R6).
    public static string VerifyNotCommitted(string detail) =>
        $"Výsledek ověření se nepodařilo zapsat ({detail}) — kartu zatím neformátujte, spusťte Zkopírovat znovu.";

    /// Spec 7: "2 klipy mají vadu obsahu — před formátováním je zkontrolujte (kopie souhlasí s kartou)."
    public static string ContentWarnings(int n) =>
        $"{n} {Desktop.Texts.Plural(n, "klip má", "klipy mají", "klipů má")} vadu obsahu — před formátováním {Desktop.Texts.Plural(n, "ho", "je", "je")} zkontrolujte (kopie souhlasí s kartou).";
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifyInProxyJobTests|FullyQualifiedName~StageTwoTests|FullyQualifiedName~RepairJobTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~JobTests|FullyQualifiedName~JobPauseTests|FullyQualifiedName~HistoryJobTests|FullyQualifiedName~VerifyJobTests|FullyQualifiedName~JobProgressTests|FullyQualifiedName~SpaceCheckTests"`

Expected: PASS — `Failed: 0, Passed: 157, Skipped: 0, Total: 157` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1617` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 12: úloha ověřuje během proxy — po kopii „Lze vyjmout“, „Lze formátovat“ až po ověření všech cílů; fáze „Dokončení ověření“; zápis ověřené generace pod zámkem složek; zrušení zastaví i ověření; bez proxy, s vypnutou volbou a u opravy zpětné čtení jako dosud (R11).
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: verify-in-proxy job, eject vs format verdicts, commit after verify (plan 10, task 12)

With proxies and verification on, the job defers the read-back: after the copy the card can be
removed (SafeToEject) but not formatted (SafeToFormat); stage 2 verifies through the proxies and
the background readers, Finishing waits for the rest and commits the generation under the folder
lock. Cancel stops verification too; repair and no-proxy jobs read back as before.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Desktop\JobCard.cs windows\src\Offload.Core\Desktop\JobProgress.cs windows\src\Offload.Core\Desktop\SpaceCheck.cs windows\src\Offload.Core\OffloadEngine.cs windows\src\Offload.Core\OffloadJob.cs windows\src\Offload.Core\VerifyInProxy\Msg.VerifyInProxy.cs windows\tests\Offload.Core.Tests\AppendEngineTests.cs windows\tests\Offload.Core.Tests\RepairJobTests.cs windows\tests\Offload.Core.Tests\StageTwoTests.cs windows\tests\Offload.Core.Tests\VerifyInProxyJobTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 13: Navázání po pádu z manifestu a nedokončená záloha v historii (R6)

Když aplikace skončí uprostřed (pád, vypnutí PC, zavření během ověření), záloha nemá potvrzenou generaci, ale
manifest ví, co je na discích. Doplnění (`offload append`, tlačítko Doplnit) ji **dokončí** místo číslování další
generace: `ResumePlanner.Find` najde nedokončenou generaci G (report G s `verifyPending`, nebo žádný report a záznamy
v manifestu; všechny složky musí souhlasit, jinak „nesouhlasí s ostatními cíli“), `Decide` pro každý soubor řekne:
celý zapsaný a zaznamenaný → nečte se z karty znovu (hashe z manifestu, ověří se); na disku bez záznamu → přečte se
jen hash z karty (`HashOnly`) a kopie se ověří; rozepsaný `.offload-part` s kontrolním bodem → kopie pokračuje za ním
(bloky z disku se znovu zkontrolují proti kontrolnímu bodu, karta se čte až od něj); jinak se kopíruje od začátku.
Nikdy se nic nepřepíše (R7): soubor, který na disku je a manifest ho nezná, je konflikt jako dosud. Oprava
nedokončenou zálohu odmítne. Historie zná kopii od startu (záznam „nedokončeno“; zrušená kopie ho smaže), dotaz
„Karty už jsou zálohované“ se u nedokončené neptá a nabídka zní „Dokončit přerušenou zálohu“.

**Files:**
- Create: `windows/src/Offload.Core/Append/ResumePlanner.cs`
- Modify: `windows/src/Offload.Core/Append/AppendPlanner.cs:24-29, 54-66, 76-82, 109-115, 119-124, 127-141, 162-169, 190-196, 207-213`
- Modify: `windows/src/Offload.Core/Append/RepairPlanner.cs:61-66`
- Modify: `windows/src/Offload.Core/CardRecord.cs:62-68, 95-103`
- Modify: `windows/src/Offload.Core/DestWriter.cs:73-79, 84-90`
- Modify: `windows/src/Offload.Core/NativeIO.cs:20-25`
- Modify: `windows/src/Offload.Core/Models.cs:98-103`
- Modify: `windows/src/Offload.Core/OffloadEngine.Run.cs:1-3, 52-58, 88-94, 115-120, 176-182, 305-320, 362-367`
- Modify: `windows/src/Offload.Core/OffloadEngine.cs:121-126`
- Modify: `windows/src/Offload.Core/OffloadReport.cs:144-150`
- Modify: `windows/src/Offload.Core/OffloadJob.cs:135-140, 195-201, 345-350, 376-381, 459-464, 517-522, 562-573, 596-602`
- Modify: `windows/src/Offload.Core/History/HistoryEntry.cs:60-65`
- Modify: `windows/src/Offload.Core/History/HistoryStore.cs:77-82`
- Modify: `windows/src/Offload.Core/History/HistoryText.cs:40-41`
- Modify: `windows/src/Offload.Core/Desktop/HistoryView.cs:23`
- Modify: `windows/src/Offload.Core/Desktop/AppendRules.cs:12-20`
- Modify: `windows/src/Offload.Core/Desktop/BackedUpPrompt.cs:16-22`
- Modify: `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs:27-29`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/ResumeTests.cs` (nový)

**Interfaces:**
- Consumes: `ManifestFile` (úkol 3), kontrolní body `DestWriter` (úkol 4), `AppendPlanner`, `CardRecord.Load`, `HistoryStore`.
- Produces:
  - `Unfinished` (`Generation`, `Manifests`, `FileOf`, `PartOf`, `Owns`, `Mentions`), `ResumePlanner.Find(cardPaths)`, `ResumePlanner.Decide(…)` → `(Write, Known, HashOnly, Resume)`.
  - `AppendPlan.Resuming/Write/Known/HashOnly/Resume`, `OffloadPlan.Resuming/Known/Resume/HashOnly`; `AppendPlanner.CheckBackup(cardPaths, Unfinished? unfinished = null)`; `CardRecord.Load(cardPath, int? maxGeneration = null)`.
  - `DestWriter.Begin(SourceFile, PartCheckpoint? resume = null)`, `NativeIO.OpenResume(path)`.
  - `HistoryEntry.Unfinished`, `HistoryStore.Remove(id)`; `Msg.ResumeDiverged`, `Msg.RepairUnfinished`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/ResumeTests.cs` (nový soubor)

```csharp
using System.IO.Hashing;
using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// R6: a copy the app never finished — crashed mid-copy (only the manifest says what is on the disk) or mid-verification
/// (its reports say verifyPending) — is completed by an append: complete files are not copied again, a part goes on from
/// its checkpoint, nothing on the disks is overwritten, the result is verified and committed like any copy.
public class ResumeTests
{
    const int Block = 64 * 1024;
    static readonly DateTime T = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    static OffloadOptions Options() => new() { ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, CheckpointBlocks = 1 };

    static byte[] CardFile(string card, string rel, int size, int seed)
    {
        var data = TestFs.WriteRandom(Layout.Join(card, rel), size, seed);
        File.SetLastWriteTimeUtc(Layout.Join(card, rel), T);
        return data;
    }

    static ulong[] Blocks(byte[] data) => Enumerable.Range(0, BlockManifest.CountFor(data.Length, Block))
        .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, data.Length - b * Block)))).ToArray();

    static ManifestRecord FileRecord(string rel, byte[] data)
    {
        var x128 = new XxHash128();
        x128.Append(data);
        return new ManifestRecord
        {
            Path = rel, Size = data.Length, Modified = T, Xxh64 = Format.Hex64(XxHash64.HashToUInt64(data)),
            Xxh128 = Convert.ToHexStringLower(x128.GetCurrentHash()), BlockSize = Block, Blocks = ManifestRecord.Hex(Blocks(data)),
        };
    }

    /// What a copy that crashed leaves: A in its place with its record, B's first `partBlocks` blocks in its part with a
    /// checkpoint, nothing of C — and no report, no ASC MHL.
    static string CrashedCopy(TempDir dir, string root, byte[] a, byte[] b, int partBlocks)
    {
        var cardPath = Path.Combine(root, "2026-10-08", "A001");
        Directory.CreateDirectory(Path.Combine(cardPath, "_REPORT"));
        Directory.CreateDirectory(Path.Combine(cardPath, "CLIP"));
        File.WriteAllBytes(Path.Combine(cardPath, "CLIP", "A.mov"), a);
        File.SetLastWriteTimeUtc(Path.Combine(cardPath, "CLIP", "A.mov"), T);
        ManifestFile.Append(cardPath, FileRecord("CLIP/A.mov", a));
        File.WriteAllBytes(Path.Combine(cardPath, "CLIP", ".B.mov.offload-part"), b[..(partBlocks * Block)]);
        ManifestFile.Append(cardPath, new ManifestRecord
        {
            Kind = ManifestRecord.PartKind, Path = "CLIP/B.mov", Size = b.Length, Modified = T, BlockSize = Block,
            Blocks = ManifestRecord.Hex(Blocks(b)[..partBlocks]),
        });
        return cardPath;
    }

    [Fact]
    public void CrashMidCopy_AppendResumes_CompleteFileNotRead_PartGoesOn()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", 3 * Block + 10, 1);
        var b = CardFile(card, "CLIP/B.mov", 5 * Block + 20, 2);
        var c = CardFile(card, "CLIP/C.mov", 2 * Block, 3);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 3);
        var read = new List<string>();
        var options = Options();
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, HistoryFolder = dir.Sub("h"),
            Outputs = new Outputs.OutputSettings { DayReport = false } }, options);
        options.BeforeFileCopy = i => read.Add(job.Plan!.Scan.Files[i].RelPath);

        job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToFormat);
        var plan = job.Plan!;
        Assert.True(plan.Resuming);
        Assert.Equal(1, plan.Generation);
        Assert.Equal(["CLIP/B.mov", "CLIP/C.mov"], read);                 // A is not read from the card again
        Assert.Equal(3, plan.Resume![plan.Scan.Files.ToList().FindIndex(f => f.RelPath == "CLIP/B.mov")]!.Blocks.Length);
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "A.mov")));
        Assert.Equal(b, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "B.mov")));
        Assert.Equal(c, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "C.mov")));
        Assert.Empty(Directory.GetFiles(cardPath, "*.offload-part", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "offload_report.json")));
        Assert.Null(job.Report!.Files[0].Rewritten);                       // a resume is no repair
        Assert.True(BackupVerifier.Verify(cardPath, chunkSize: 4096).Success);
        Assert.True(CardRecord.Load(cardPath).Verified);
        Assert.Equal(HistoryGeneration.Full, Assert.Single(Assert.Single(new HistoryStore(Path.Combine(dir.Path, "h")).Load()).Generations).Kind);
    }

    // The part's blocks feed the hashes from the disk, not from the card (each checked against its checkpoint): the card
    // is read only from where the part ends.
    [Fact]
    public void Resume_PrefixFromThePart_CardReadOnlyAfterIt()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 4 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 2);
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, RecordHistory = false,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, Options());

        job.Run();

        Assert.True(job.Status().SafeToFormat);
        Assert.Equal(4L * Block, job.Status().Copy!.ReadBytes);   // B: 2 blocks from the part + 2 from the card; A not read
    }

    // The checkpoint does not match the part any more (the part changed since): the file fails, nothing is trusted.
    [Fact]
    public void Resume_PartDiffersFromItsCheckpoint_FileFails_NeverVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 4 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 2);
        var part = Path.Combine(cardPath, "CLIP", ".B.mov.offload-part");
        var bytes = File.ReadAllBytes(part);
        bytes[5] ^= 1;
        File.WriteAllBytes(part, bytes);
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, RecordHistory = false,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, Options());

        job.Run();

        var f = job.Report!.Files.Single(x => x.Path == "CLIP/B.mov");
        Assert.StartsWith("resume: ", f.SourceError);
        Assert.False(job.Status().SafeToFormat);
        Assert.False(File.Exists(Path.Combine(cardPath, "CLIP", "B.mov")));
    }

    // Crashed during the verification (verifyPending, no MHL): the append verifies and commits it — the card is not read.
    [Fact]
    public void PendingVerification_AppendFinishesIt_WithoutTheCard()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        CardFile(card, "CLIP/A.mov", 3 * Block, 1);
        CardFile(card, "CLIP/B.mov", 2 * Block + 7, 2);
        var options = Options();
        options.DeferVerify = true;   // as a verify-in-proxy job whose stage 2 never ran
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");
        var report = engine.Run(plan);
        Assert.True(report.VerifyPending);
        var cardPath = plan.CardPaths[0];
        Assert.Throws<OffloadError>(() => AppendPlanner.CheckBackup([cardPath]));   // as before: no MHL
        var read = new List<int>();
        var resume = Options();
        resume.BeforeFileCopy = read.Add;
        var job = new OffloadJob(new JobConfig(card, []) { AppendInto = [cardPath], AnalyzeClips = false, RecordHistory = false,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, resume);

        job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(job.Status().SafeToFormat);
        Assert.Empty(read);
        Assert.Equal((1, true), (job.Plan!.Generation, job.Plan.Resuming));
        Assert.Single(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl"));
        Assert.Null(OffloadReport.FromJson(File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json"))).VerifyPending);
        Assert.True(BackupVerifier.Verify(cardPath, chunkSize: 4096).Success);
    }

    // Nothing of the card is someone else's: a file in its place that the manifest does not name stops the resume.
    [Fact]
    public void Resume_FileWithoutRecord_Conflict_NothingWritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 2 * Block, 2);
        CardFile(card, "CLIP/C.mov", Block, 3);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 1);
        File.WriteAllBytes(Path.Combine(cardPath, "CLIP", "C.mov"), new byte[Block]);

        var e = Assert.Throws<OffloadError>(() => AppendPlanner.Plan(card, [cardPath], CancellationToken.None, sourceFilesystem: "NTFS"));
        Assert.Equal(PreflightCode.AppendConflict, e.Code);
        Assert.Equal(new byte[Block], File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "C.mov")));
    }

    [Fact]
    public void Repair_OfAnUnfinishedBackup_Refused()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 2 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 1);

        var e = Assert.Throws<OffloadError>(() => RepairPlanner.Plan(card, [cardPath]));
        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Equal(Msg.ForRepair(Msg.RepairUnfinished), e.Message);
    }

    [Fact]
    public void Find_OneFolderUnfinished_OtherNot_Diverged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 2 * Block, 2);
        var crashed = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 1);
        var engine = new OffloadEngine(Options());
        var whole = engine.Run(engine.Prepare(card, [dir.Sub("d2")], null, "2026-10-08")).Destinations[0].CardPath;

        var e = Assert.Throws<OffloadError>(() => ResumePlanner.Find([crashed, whole]));
        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.Null(ResumePlanner.Find([whole]));
    }

    [Fact]
    public void History_StartEntryUnfinished_ThenTheCopysEntry_CancelLeavesNone()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        CardFile(card, "CLIP/A.mov", Block, 1);
        CardFile(card, "CLIP/B.mov", Block, 2);
        var history = dir.Sub("h");
        var options = Options();
        HistoryEntry? during = null;
        options.BeforeFileCopy = i => { if (i == 1) during = Assert.Single(new HistoryStore(history).Load()); };
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-08", AnalyzeClips = false, HistoryFolder = history,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, options);

        job.Run();

        Assert.True(during!.Unfinished);
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.False(entry.Unfinished);
        Assert.Equal(during.Id, entry.Id);
        Assert.True(entry.Success && entry.Verified);

        var history2 = dir.Sub("h2");
        var cancel = Options();
        var cancelled = new OffloadJob(new JobConfig(card, [dir.Sub("d2")]) { Date = "2026-10-08", AnalyzeClips = false, HistoryFolder = history2,
            Outputs = new Outputs.OutputSettings { DayReport = false } }, cancel);
        cancel.BeforeFileCopy = _ => cancelled.Cancel();
        cancelled.Run();
        Assert.Empty(new HistoryStore(history2).Load());
    }

    [Fact]
    public void Texts_UnfinishedEntry()
    {
        var e = new HistoryEntry { Id = "x", CardName = "A001", Unfinished = true, Finished = T, Destinations = [new() { Root = @"D:\Zaloha", CardPath = @"D:\Zaloha\2026-10-08\A001" }] };
        var m = new HistoryMatch(e, HistoryMatchKind.NewFiles, [new SourceFile("a", 10, T)], 1_000_000, [], 0);
        Assert.Equal(("Nedokončená záloha 8. 10. 2026 " + T.ToLocalTime().ToString("H:mm") + " na Zaloha (D:) — kopie se přerušila, lze ji dokončit", HistoryTone.Warning),
            HistoryText.Line(m));
        Assert.StartsWith("Dokončit přerušenou zálohu (1 soubor · ", AppendRules.CheckboxText(m));
        Assert.False(BackedUpPrompt.ShouldAsk(new CardInsight { Source = "E:\\", History = m }));
        Assert.Equal(("! nedokončeno", HistoryTone.Warning), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ResumeTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~RepairPlannerTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~HistoryStoreTests|FullyQualifiedName~HistoryTextTests|FullyQualifiedName~AppendRulesTests|FullyQualifiedName~BackedUpPromptTests|FullyQualifiedName~ManifestRecordingTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS1061: 'OffloadPlan' does not contain a definition for 'Resuming' and no accessible extension method 'Resuming' accepting a first argument of type 'OffloadPlan' could be found (are you missing a using directive or an assembly reference?)`; `CS1061: 'OffloadPlan' does not contain a definition for 'Resume' and no accessible extension method 'Resume' accepting a first argument of type 'OffloadPlan' could be found (are you missing a using directive or an assembly reference?)`; `CS0117: 'Msg' does not contain a definition for 'RepairUnfinished'` a další chyby téhož druhu (všechny jen v testech tohoto úkolu).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Append/ResumePlanner.cs` (nový soubor)

```csharp
using Offload.Core.History;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Append;

/// R6: a generation an earlier run started in these card folders and never committed to the ASC MHL chain — the app ended
/// in the middle of the copy (no report for it yet) or of the verification with the proxies (its reports say
/// verifyPending). `Manifests`: per card folder, what its manifest says (every generation).
public sealed class Unfinished(int generation, IReadOnlyList<IReadOnlyList<ManifestRecord>> manifests)
{
    public int Generation => generation;
    public IReadOnlyList<IReadOnlyList<ManifestRecord>> Manifests => manifests;
    readonly IReadOnlyDictionary<string, ManifestRecord>[] files = manifests.Select(m => ManifestFile.Files(m, generation)).ToArray();
    readonly IReadOnlyDictionary<string, PartCheckpoint>[] parts = manifests.Select(m => ManifestFile.Parts(m, generation)).ToArray();

    /// Folder `d`'s `file` record of `f` for this generation, when it is the card's file (size, time).
    public ManifestRecord? FileOf(int d, SourceFile f, bool localTime) =>
        files[d].TryGetValue(f.RelPath, out var r) && r.Size == f.Size && FileTimes.Close(r.Modified, f.ModifiedUtc, localTime) ? r : null;

    /// Folder `d`'s checkpoint of `f` for this generation, when it is the card's file (size, time).
    public PartCheckpoint? PartOf(int d, SourceFile f, bool localTime) =>
        parts[d].TryGetValue(f.RelPath, out var p) && p.Size == f.Size && FileTimes.Close(p.Modified, f.ModifiedUtc, localTime) ? p : null;

    /// The run that never committed wrote `f` there: a file at its place with the card's size whose record (or checkpoint —
    /// it was renamed into place before its record was written) names the card's file.
    public bool Owns(int d, SourceFile f, long sizeOnDisk, bool localTime) =>
        sizeOnDisk == f.Size && (FileOf(d, f, localTime) != null || PartOf(d, f, localTime) != null);

    /// The manifest of some folder mentions `f` (the card is the one that run copied — ruling I3 for a resume).
    public bool Mentions(SourceFile f) => files.Any(x => x.ContainsKey(f.RelPath)) || parts.Any(x => x.ContainsKey(f.RelPath));
}

/// R6, tied into the append (plan 6): finds an unfinished generation and decides, per card file, what a resume does —
/// complete copies are never copied again, a part on the disk goes on from its checkpoint. Reads only.
public static class ResumePlanner
{
    /// The unfinished generation of the card folders, or null when every one's newest generation is committed (then the
    /// append plans as before). Refuses (AppendDiverged) folders that disagree.
    public static Unfinished? Find(IReadOnlyList<string> cardPaths)
    {
        var states = cardPaths.Select(p => (Path: p, Committed: Committed(p), Records: ManifestFile.Read(p))).ToList();
        // A folder is open at the generation after its committed one when that generation's report says verifyPending,
        // or there is none and its manifest has records of it.
        static bool Open((string Path, int Committed, IReadOnlyList<ManifestRecord> Records) s, int g) =>
            Pending(s.Path, g) || (!ReportExists(s.Path, g) && s.Records.Any(r => r.Generation == g));
        var open = states.FirstOrDefault(s => Open(s, s.Committed + 1));
        if (open.Path == null) return null;
        int g = open.Committed + 1;
        // Every folder must stand right before that generation, with no committed report of it.
        foreach (var s in states)
            if (s.Committed != g - 1 || (ReportExists(s.Path, g) && !Pending(s.Path, g)))
                throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.ResumeDiverged(s.Path, g));
        return new Unfinished(g, states.Select(s => s.Records).ToList());
    }

    /// The newest committed generation: the ASC MHL chain's (0 without one).
    static int Committed(string cardPath) =>
        MhlReader.ReadChain(Path.Combine(cardPath, Mhl.DirName)) is { Count: > 0 } chain ? chain.Max(c => c.Seq) : 0;

    static string ReportPath(string cardPath, int generation) => Path.Combine(cardPath, Layout.ReportDirName, OffloadReport.JsonName(generation));

    static bool ReportExists(string cardPath, int generation) => File.Exists(ReportPath(cardPath, generation));

    /// The report of `generation` says its verification never ended (plan 10: verifyPending).
    static bool Pending(string cardPath, int generation)
    {
        try
        {
            return ReportExists(cardPath, generation) && OffloadReport.FromJson(File.ReadAllText(ReportPath(cardPath, generation))).VerifyPending == true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// What a resume does with each card file of the unfinished generation (`files`, in copy order; `present[i][d]`: on
    /// the disk at its place): written where it is missing; not read at all when every folder has it with a record
    /// (Known); read only for its hash when every folder has it without one (HashOnly); resumed from the checkpoint every
    /// folder it is written to has — the shortest one, and only as far as all of them agree.
    public static (IReadOnlyList<bool[]> Write, IReadOnlyList<ManifestRecord?> Known, IReadOnlyList<bool> HashOnly, IReadOnlyList<PartCheckpoint?> Resume)
        Decide(Unfinished u, IReadOnlyList<string> cardPaths, IReadOnlyList<SourceFile> files, IReadOnlyList<bool[]> present, IReadOnlyList<bool> localTime)
    {
        var write = new List<bool[]>();
        var known = new List<ManifestRecord?>();
        var hashOnly = new List<bool>();
        var resume = new List<PartCheckpoint?>();
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var mask = present[i].Select(p => !p).ToArray();
            write.Add(mask);
            var records = Enumerable.Range(0, cardPaths.Count).Select(d => u.FileOf(d, f, localTime[d])).ToList();
            bool everywhere = mask.All(m => !m);
            bool sameRecord = records.All(r => r != null) && records.Select(r => r!.Xxh64).Distinct().Count() == 1;
            known.Add(everywhere && sameRecord ? records[0] : null);
            hashOnly.Add(everywhere && !sameRecord);
            resume.Add(mask.Any(m => m) ? CommonCheckpoint(u, cardPaths, f, mask, localTime) : null);
        }
        return (write, known, hashOnly, resume);
    }

    static PartCheckpoint? CommonCheckpoint(Unfinished u, IReadOnlyList<string> cardPaths, SourceFile f, bool[] mask, IReadOnlyList<bool> localTime)
    {
        PartCheckpoint? common = null;
        for (int d = 0; d < cardPaths.Count; d++)
        {
            if (!mask[d]) continue;
            var part = new FileInfo(Layout.Join(cardPaths[d], DestWriter.PartName(f.RelPath)));
            if (u.PartOf(d, f, localTime[d]) is not { Blocks.Length: > 0 } p || !part.Exists || part.Length < (long)p.Blocks.Length * p.BlockSize) return null;
            if (common == null) { common = p; continue; }
            if (common.BlockSize != p.BlockSize) return null;
            int same = 0;
            while (same < Math.Min(common.Blocks.Length, p.Blocks.Length) && common.Blocks[same] == p.Blocks[same]) same++;
            if (same == 0) return null;
            common = common with { Blocks = common.Blocks[..same] };
        }
        return common;
    }
}
```

#### `windows/src/Offload.Core/Append/AppendPlanner.cs`

Najít (1/9, řádky 24–29):

```csharp
    public int UnverifiedEarlier { get; init; }
    /// The hash types of the earlier backup — an append uses them, not the current setting (spec 3.5).
    public IReadOnlyList<string> HashAlgorithms { get; init; } = ["xxh64"];
}

/// Pure decisions over the card and the content of the card folders (R4): the truth is what is on the destinations —
```

Nahradit:

```csharp
    public int UnverifiedEarlier { get; init; }
    /// The hash types of the earlier backup — an append uses them, not the current setting (spec 3.5).
    public IReadOnlyList<string> HashAlgorithms { get; init; } = ["xxh64"];
    /// R6 (plan 10): this append completes generation `Generation`, which an earlier run never committed — NewFiles are
    /// then that generation's files with what ResumePlanner.Decide found for each (aligned with NewFiles).
    public bool Resuming { get; init; }
    public IReadOnlyList<bool[]>? Write { get; init; }
    public IReadOnlyList<VerifyInProxy.ManifestRecord?>? Known { get; init; }
    public IReadOnlyList<bool>? HashOnly { get; init; }
    public IReadOnlyList<VerifyInProxy.PartCheckpoint?>? Resume { get; init; }
}

/// Pure decisions over the card and the content of the card folders (R4): the truth is what is on the destinations —
```

Najít (2/9, řádky 54–66):

```csharp

    /// Steps 0 and 1: one backup `<root>\<date>\<card>` with the same date and name everywhere, every report readable,
    /// every ASC MHL chain intact and of the same length, and the numbered reports in agreement with it. Reads only.
    internal static Backup CheckBackup(IReadOnlyList<string> cardPaths)
    {
        if (cardPaths.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        var paths = cardPaths.Select(p => Path.GetFullPath(p).TrimEnd('\\')).ToList();

        // 0. One backup: `<root>\<date>\<card>` with the same date and name everywhere — the copy goes exactly there.
        var (roots, date, cardName) = Split(paths);

        // 1. Every earlier backup is there, with its report and its ASC MHL chain (R7).
        var records = new List<CardRecord>();
```

Nahradit:

```csharp

    /// Steps 0 and 1: one backup `<root>\<date>\<card>` with the same date and name everywhere, every report readable,
    /// every ASC MHL chain intact and of the same length, and the numbered reports in agreement with it. Reads only.
    /// `unfinished` (R6): the generation a resume completes — only the generations before it are checked (generation 1:
    /// nothing is committed yet, the folders need no report).
    internal static Backup CheckBackup(IReadOnlyList<string> cardPaths, Unfinished? unfinished = null)
    {
        if (cardPaths.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        var paths = cardPaths.Select(p => Path.GetFullPath(p).TrimEnd('\\')).ToList();

        // 0. One backup: `<root>\<date>\<card>` with the same date and name everywhere — the copy goes exactly there.
        var (roots, date, cardName) = Split(paths);
        if (unfinished is { Generation: 1 })
        {
            foreach (var p in paths)
                if (!Directory.Exists(p)) throw OffloadError.Preflight(PreflightCode.AppendUnavailable, Msg.AppendUnavailable(p));
            return new Backup(paths, roots, date, cardName, paths.Select(p => new CardRecord { CardPath = p }).ToList(), 1);
        }

        // 1. Every earlier backup is there, with its report and its ASC MHL chain (R7).
        var records = new List<CardRecord>();
```

Najít (3/9, řádky 76–82):

```csharp
        foreach (var cardPath in paths)
        {
            if (!Directory.Exists(cardPath)) throw OffloadError.Preflight(PreflightCode.AppendUnavailable, Msg.AppendUnavailable(cardPath));
            var record = CardRecord.Load(cardPath);
            if (record.ReportMissing) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendNoReport(cardPath));
            if (record.Error != null) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendBadReport(cardPath, record.Error));
            // An append report nobody can read: the files it recorded would look like someone else's.
```

Nahradit:

```csharp
        foreach (var cardPath in paths)
        {
            if (!Directory.Exists(cardPath)) throw OffloadError.Preflight(PreflightCode.AppendUnavailable, Msg.AppendUnavailable(cardPath));
            var record = CardRecord.Load(cardPath, unfinished?.Generation - 1);
            if (record.ReportMissing) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendNoReport(cardPath));
            if (record.Error != null) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendBadReport(cardPath, record.Error));
            // An append report nobody can read: the files it recorded would look like someone else's.
```

Najít (4/9, řádky 109–115):

```csharp
    internal static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem,
        Func<string, string>? destFilesystem = null)
    {
        var (paths, roots, date, cardName, records, generation) = CheckBackup(cardPaths);

        // 2. The card, compared with every backup: a known file must be the same on the card, in the report and on disk.
        var scan = Scanner.Scan(source, ct);
```

Nahradit:

```csharp
    internal static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem,
        Func<string, string>? destFilesystem = null)
    {
        // R6 (plan 10): a generation an earlier run never committed is completed by this append, not numbered after it.
        var unfinished = ResumePlanner.Find(cardPaths);
        var (paths, roots, date, cardName, records, generation) = CheckBackup(cardPaths, unfinished);

        // 2. The card, compared with every backup: a known file must be the same on the card, in the report and on disk.
        var scan = Scanner.Scan(source, ct);
```

Najít (5/9, řádky 119–124):

```csharp
        // Ruling I5: a FAT destination shifts its own file times after a change of time, whatever the card is.
        var diskLocalTime = paths.Select(p => cardLocalTime || FileTimes.StoresLocalTime(destFilesystem?.Invoke(p) ?? NativeIO.FilesystemType(p))).ToList();
        var newFiles = new List<SourceFile>();
        var conflicts = new List<string>();
        int unverified = 0, known = 0;
        foreach (var f in scan.Files)
```

Nahradit:

```csharp
        // Ruling I5: a FAT destination shifts its own file times after a change of time, whatever the card is.
        var diskLocalTime = paths.Select(p => cardLocalTime || FileTimes.StoresLocalTime(destFilesystem?.Invoke(p) ?? NativeIO.FilesystemType(p))).ToList();
        var newFiles = new List<SourceFile>();
        var presence = new List<bool[]>();
        var conflicts = new List<string>();
        int unverified = 0, known = 0;
        foreach (var f in scan.Files)
```

Najít (6/9, řádky 127–141):

```csharp
            ct.ThrowIfCancellationRequested();
            int present = 0, goodInRecord = 0;
            bool unverifiedFile = false;
            for (int d = 0; d < paths.Count; d++)
            {
                var onDisk = new FileInfo(Layout.Join(paths[d], f.RelPath));
                bool exists = onDisk.Exists;
                if (exists) present++;
                if (!records[d].Files.TryGetValue(f.RelPath, out var rec))
                {
                    // Not in any report but on the disk: someone else's file — never overwritten, never trusted.
                    if (exists) conflicts.Add(Msg.AppendUnknownFile(Disk(paths[d]), f.RelPath));
                    continue;
                }
                int column = CardRecord.DestinationIndex(rec.Report, paths[d], d);
```

Nahradit:

```csharp
            ct.ThrowIfCancellationRequested();
            int present = 0, goodInRecord = 0;
            bool unverifiedFile = false;
            var here = new bool[paths.Count];
            for (int d = 0; d < paths.Count; d++)
            {
                var onDisk = new FileInfo(Layout.Join(paths[d], f.RelPath));
                bool exists = onDisk.Exists;
                if (exists) present++;
                here[d] = exists;
                if (!records[d].Files.TryGetValue(f.RelPath, out var rec))
                {
                    // Not in any report but on the disk: someone else's file — never overwritten, never trusted. Unless the
                    // unfinished run wrote it (R6): its manifest names the card's file there.
                    if (exists && unfinished?.Owns(d, f, onDisk.Length, diskLocalTime[d]) != true)
                        conflicts.Add(Msg.AppendUnknownFile(Disk(paths[d]), f.RelPath));
                    continue;
                }
                int column = CardRecord.DestinationIndex(rec.Report, paths[d], d);
```

Najít (7/9, řádky 162–169):

```csharp
                    conflicts.Add(Conflict(f, Disk(paths[d]), onDisk.Length, onDisk.LastWriteTimeUtc));
            }
            if (unverifiedFile) unverified++;
            if (goodInRecord > 0) known++;
            if (goodInRecord == 0 && present == 0) newFiles.Add(f);
            else if (goodInRecord > 0 && goodInRecord < paths.Count && present < paths.Count)
                throw OffloadError.Preflight(PreflightCode.AppendDiverged,
                    Msg.AppendDiverged($"{f.RelPath} je jen na {string.Join(", ", paths.Where(p => File.Exists(Layout.Join(p, f.RelPath))).Select(Disk))}"));
```

Nahradit:

```csharp
                    conflicts.Add(Conflict(f, Disk(paths[d]), onDisk.Length, onDisk.LastWriteTimeUtc));
            }
            if (unverifiedFile) unverified++;
            if (goodInRecord > 0 || unfinished?.Mentions(f) == true) known++;
            // R6: a file of the unfinished generation on some disks (its copies there are the run's own — else a conflict
            // above) is part of this generation too.
            if (goodInRecord == 0 && (present == 0 || unfinished != null))
            {
                newFiles.Add(f);
                presence.Add(here);
            }
            else if (goodInRecord > 0 && goodInRecord < paths.Count && present < paths.Count)
                throw OffloadError.Preflight(PreflightCode.AppendDiverged,
                    Msg.AppendDiverged($"{f.RelPath} je jen na {string.Join(", ", paths.Where(p => File.Exists(Layout.Join(p, f.RelPath))).Select(Disk))}"));
```

Najít (8/9, řádky 190–196):

```csharp
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var first = records[0].Generations[0].Report;
        return new AppendPlan
        {
            Source = Path.GetFullPath(source),
```

Nahradit:

```csharp
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var first = records[0].Generations.FirstOrDefault()?.Report;
        var resumed = unfinished == null ? default
            : ResumePlanner.Decide(unfinished, paths, newFiles, presence, diskLocalTime);
        // Generation 1 resumed: no report yet — the hashes the run used, as its manifest says (MD5 when it has it).
        var algorithms = first?.Algorithms ?? (unfinished!.Manifests.Any(m => m.Any(r => r.Md5 != null)) ? ["xxh64", "md5"] : ["xxh64"]);
        return new AppendPlan
        {
            Source = Path.GetFullPath(source),
```

Najít (9/9, řádky 207–213):

```csharp
            NewFiles = newFiles,
            NewDirs = newDirs,
            UnverifiedEarlier = unverified,
            HashAlgorithms = first.Algorithms,
        };
    }

```

Nahradit:

```csharp
            NewFiles = newFiles,
            NewDirs = newDirs,
            UnverifiedEarlier = unverified,
            HashAlgorithms = algorithms,
            Resuming = unfinished != null,
            Write = unfinished == null ? null : resumed.Write,
            Known = unfinished == null ? null : resumed.Known,
            HashOnly = unfinished == null ? null : resumed.HashOnly,
            Resume = unfinished == null ? null : resumed.Resume,
        };
    }

```

#### `windows/src/Offload.Core/Append/RepairPlanner.cs`

Najít (1/1, řádky 61–66):

```csharp
        AppendPlanner.Backup backup;
        try
        {
            backup = AppendPlanner.CheckBackup(cardPaths);
        }
        catch (OffloadError e) when (e.Code is PreflightCode code)
```

Nahradit:

```csharp
        AppendPlanner.Backup backup;
        try
        {
            // R6: a backup an earlier run never finished is completed by an append first, never repaired.
            if (ResumePlanner.Find(cardPaths) != null) throw OffloadError.Preflight(PreflightCode.RepairConflict, Msg.RepairUnfinished);
            backup = AppendPlanner.CheckBackup(cardPaths);
        }
        catch (OffloadError e) when (e.Code is PreflightCode code)
```

#### `windows/src/Offload.Core/CardRecord.cs`

Najít (1/2, řádky 62–68):

```csharp
    [GeneratedRegex(@"_report_(\d{4})\.(json|txt|html|pdf)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedReportFile();

    public static CardRecord Load(string cardPath)
    {
        var dir = Path.Combine(cardPath, Layout.ReportDirName);
        var main = Path.Combine(dir, MainReport);
```

Nahradit:

```csharp
    [GeneratedRegex(@"_report_(\d{4})\.(json|txt|html|pdf)$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedReportFile();

    /// `maxGeneration` (R6): generations above it are left out — the unfinished one a resume completes (its reports, if
    /// any, and its numbered files are not the backup's record yet).
    public static CardRecord Load(string cardPath, int? maxGeneration = null)
    {
        var dir = Path.Combine(cardPath, Layout.ReportDirName);
        var main = Path.Combine(dir, MainReport);
```

Najít (2/2, řádky 95–103):

```csharp
        foreach (var file in reportFiles)
        {
            var name = Path.GetFileName(file);
            if (NumberedReportFile().Match(name) is { Success: true } n)
                highest = Math.Max(highest, int.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture));
            if (AppendReport().Match(name) is not { Success: true } m) continue;
            try
            {
                var r = Read(file);
```

Nahradit:

```csharp
        foreach (var file in reportFiles)
        {
            var name = Path.GetFileName(file);
            if (NumberedReportFile().Match(name) is { Success: true } n && int.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture) is var number
                && number <= (maxGeneration ?? int.MaxValue))
                highest = Math.Max(highest, number);
            if (AppendReport().Match(name) is not { Success: true } m) continue;
            if (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) > (maxGeneration ?? int.MaxValue)) continue;
            try
            {
                var r = Read(file);
```

#### `windows/src/Offload.Core/DestWriter.cs`

Najít (1/2, řádky 73–79):

```csharp
        return slash < 0 ? name : rel[..slash] + "/" + name;
    }

    public void Begin(SourceFile file) => worker.Post(() =>
    {
        currentError = null;
        current = file;
```

Nahradit:

```csharp
        return slash < 0 ? name : rel[..slash] + "/" + name;
    }

    /// `resume` (R6): the first blocks of the file already in its `.offload-part` from an earlier run (checked against the
    /// manifest's checkpoint by the planner) — the part is opened as it is and written on from there.
    public void Begin(SourceFile file, PartCheckpoint? resume = null) => worker.Post(() =>
    {
        currentError = null;
        current = file;
```

Najít (2/2, řádky 84–90):

```csharp
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
        try
        {
            handle = OpenPart(file.Size);
            File.SetAttributes(partPath, FileAttributes.Hidden);
        }
        catch (Exception e)
```

Nahradit:

```csharp
        partPath = Layout.Join(CardPath, PartName(file.RelPath));
        try
        {
            if (resume != null && Unbuffered)
            {
                handle = NativeIO.OpenResume(partPath);
                align = NativeIO.SectorSize(handle);
                blockHashes.AddRange(resume.Blocks);
                checkpointed = resume.Blocks.Length;
            }
            else handle = OpenPart(file.Size);
            File.SetAttributes(partPath, FileAttributes.Hidden);
        }
        catch (Exception e)
```

#### `windows/src/Offload.Core/NativeIO.cs`

Najít (1/1, řádky 20–25):

```csharp
    public static SafeFileHandle OpenWrite(string path, bool unbuffered, long preallocate = 0) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
            unbuffered ? NoBuffering | FileOptions.WriteThrough : FileOptions.None, preallocate));

    public static SafeFileHandle OpenExisting(string path) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.None));
```

Nahradit:

```csharp
    public static SafeFileHandle OpenWrite(string path, bool unbuffered, long preallocate = 0) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
            unbuffered ? NoBuffering | FileOptions.WriteThrough : FileOptions.None, preallocate));

    /// An existing file for unbuffered, written-through writes — a copy resumed in its `.offload-part` (R6); kept as it is.
    public static SafeFileHandle OpenResume(string path) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, NoBuffering | FileOptions.WriteThrough));

    public static SafeFileHandle OpenExisting(string path) =>
        Wrap("open", path, () => File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.None));
```

#### `windows/src/Offload.Core/Models.cs`

Najít (1/1, řádky 98–103):

```csharp
    public IReadOnlyList<bool[]>? ReadBackBefore { get; init; }
    /// Repair: card files that no report of the backup records (fix round 1, item 1) — the card is not safe to eject.
    public int NotInBackup { get; init; }

    /// File `file` gets written to destination `dest`.
    public bool Writes(int file, int dest) => Write?[file][dest] ?? true;
```

Nahradit:

```csharp
    public IReadOnlyList<bool[]>? ReadBackBefore { get; init; }
    /// Repair: card files that no report of the backup records (fix round 1, item 1) — the card is not safe to eject.
    public int NotInBackup { get; init; }
    /// R6 resume: this copy completes generation `Generation`, which an earlier run started and never committed to the ASC
    /// MHL chain (it crashed, or its verification never ended) — its reports, if any, are replaced, never created anew.
    public bool Resuming { get; init; }
    /// R6 resume: per file, the manifest record of an earlier run that copied it completely onto every destination — not
    /// read from the card again (its hashes and blocks are the record's); null = copied now.
    public IReadOnlyList<VerifyInProxy.ManifestRecord?>? Known { get; init; }
    /// R6 resume: per file, the checkpoint its `.offload-part` on every destination it goes to resumes from; null = from 0.
    public IReadOnlyList<VerifyInProxy.PartCheckpoint?>? Resume { get; init; }
    /// R6 resume: per file, nothing is written (it is in its place on every destination with no record) but the card is
    /// read for its hashes — the verification then checks the copies against them.
    public IReadOnlyList<bool>? HashOnly { get; init; }

    /// File `file` gets written to destination `dest`.
    public bool Writes(int file, int dest) => Write?[file][dest] ?? true;
```

#### `windows/src/Offload.Core/OffloadEngine.Run.cs`

Najít (1/7, řádky 1–3):

```csharp
using Offload.Core.VerifyInProxy;

namespace Offload.Core;
```

Nahradit:

```csharp
using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core;
```

Najít (2/7, řádky 52–58):

```csharp
                // also when the repair is cancelled before this file: its good copy must never look unwritten.
                var targets = ws.Where(w => plan.Writes(i, w.Index)).ToArray();
                foreach (var w in ws.Except(targets)) w.Keep(i);
                if (targets.Length == 0) continue;   // nothing to copy: the card is not read for it
                if (IsCancelled)
                {
                    sourceErrors[i] = "cancelled";
```

Nahradit:

```csharp
                // also when the repair is cancelled before this file: its good copy must never look unwritten.
                var targets = ws.Where(w => plan.Writes(i, w.Index)).ToArray();
                foreach (var w in ws.Except(targets)) w.Keep(i);
                // Nothing to copy: the card is not read for it — unless only its hash is wanted (R6: a file a crashed copy
                // finished on the disks but never recorded).
                bool hashOnly = plan.HashOnly?[i] == true;
                if (targets.Length == 0 && !hashOnly) continue;
                if (IsCancelled)
                {
                    sourceErrors[i] = "cancelled";
```

Najít (3/7, řádky 88–94):

```csharp
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                var failure = CopyFile(plan.Source, f, targets, hashers, pool);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
```

Nahradit:

```csharp
                    fileIndex = i;
                    currentFile = f.RelPath;
                }
                // R6: resumed only when every destination it goes to can (written past the cache — a buffered one starts at 0).
                var resume = plan.Resume?[i] is { } r && targets.Length > 0 && targets.All(w => w.Unbuffered) ? r : null;
                var failure = CopyFile(plan.Source, f, targets, hashers, pool, resume);
                if (failure != null)
                {
                    sourceErrors[i] = failure;
```

Najít (4/7, řádky 115–120):

```csharp
            recorder?.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);
            var blocks = (ulong[]?[])hashers[0].Blocks.Clone();
            // A hash that threw leaves the file without it: the file failed, also when verification is off.
            for (int i = 0; i < files.Count; i++)
            {
```

Nahradit:

```csharp
            recorder?.Wait();
            var hashes = SourceHasher.Combine(hashers, files.Count);
            var blocks = (ulong[]?[])hashers[0].Blocks.Clone();
            // R6: a file the earlier run copied completely is not read again — its hashes are the manifest's.
            for (int i = 0; plan.Known != null && i < files.Count; i++)
            {
                if (plan.Known[i] is not { } known) continue;
                hashes[i] = known.Hashes() ?? hashes[i];
                blocks[i] = known.BlockValues() ?? blocks[i];
            }
            // A hash that threw leaves the file without it: the file failed, also when verification is off.
            for (int i = 0; i < files.Count; i++)
            {
```

Najít (5/7, řádky 176–182):

```csharp
            foreach (var path in cardPaths)
            {
                // Deferred (plan 10): the reports now, the ASC MHL and the hash lists once verified (Commit, stage 2).
                try { report.Write(path, includeHashRecords: !defer, createNew: defer ? plan.Generation > 1 : null); }
                catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { reportErrors.Add(e.Message); }
            }
            SetPhase(CopyPhase.Done);
```

Nahradit:

```csharp
            foreach (var path in cardPaths)
            {
                // Deferred (plan 10): the reports now, the ASC MHL and the hash lists once verified (Commit, stage 2).
                // A resume (R6) completes a generation whose reports an earlier run may have written: they are replaced.
                try { report.Write(path, includeHashRecords: !defer, createNew: plan.Resuming ? false : defer ? plan.Generation > 1 : null); }
                catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException) { reportErrors.Add(e.Message); }
            }
            SetPhase(CopyPhase.Done);
```

Najít (6/7, řádky 305–320):

```csharp
    }

    /// Reads one file block by block and hands every block to the hashers and all writers. Returns the failure, or null.
    string? CopyFile(string sourceRoot, SourceFile f, DestWriter[] ws, IReadOnlyList<SourceHasher> hashers, BufferPool pool)
    {
        var src = Layout.Join(sourceRoot, f.RelPath);
        foreach (var w in ws) w.Begin(f);
        foreach (var h in hashers) h.Begin();
        FileReader reader;
        try { reader = new FileReader(src); }
        catch (Exception e) { return e.Message; }

        long offset = 0;
        using (reader)
        {
            // A pause waits here, between blocks and before taking a buffer: the writers drain what they hold and idle.
```

Nahradit:

```csharp
    }

    /// Reads one file block by block and hands every block to the hashers and all writers. Returns the failure, or null.
    /// `resume` (R6): its first blocks are already in every destination's `.offload-part` — the hashers get them from
    /// the first destination's part (each block checked against the checkpoint again), the card is read from there on.
    string? CopyFile(string sourceRoot, SourceFile f, DestWriter[] ws, IReadOnlyList<SourceHasher> hashers, BufferPool pool, PartCheckpoint? resume = null)
    {
        var src = Layout.Join(sourceRoot, f.RelPath);
        foreach (var h in hashers) h.Begin();
        long offset = 0;
        if (resume != null)
        {
            // Read (and closed) before the writers open the part for writing: a part open for writing is not shared.
            try { offset = FeedFromPart(Layout.Join(ws[0].CardPath, DestWriter.PartName(f.RelPath)), resume, hashers, pool); }
            catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException)
            {
                // The part is not what its checkpoint says: the writers start the file afresh and the caller's Abort removes
                // it — the next resume starts at 0.
                foreach (var w in ws) w.Begin(f);
                return "resume: " + e.Message;
            }
        }
        foreach (var w in ws) w.Begin(f, resume);
        FileReader reader;
        try { reader = new FileReader(src); }
        catch (Exception e) { return e.Message; }

        using (reader)
        {
            // A pause waits here, between blocks and before taking a buffer: the writers drain what they hold and idle.
```

Najít (7/7, řádky 362–367):

```csharp
        return null;
    }

    /// Card readers have bad moments; a failed read gets a few more tries before the file is given up.
    int ReadWithRetries(FileReader reader, AlignedBuffer buf, long offset)
    {
```

Nahradit:

```csharp
        return null;
    }

    /// R6: the checkpointed blocks of a part, read straight from the disk, into the hashers only; returns where the card
    /// read goes on. A block that is not what the checkpoint says (changed since the planner looked) fails the file.
    long FeedFromPart(string part, PartCheckpoint resume, IReadOnlyList<SourceHasher> hashers, BufferPool pool)
    {
        using var disk = new DiskBlocks(part);
        long offset = 0;
        for (int b = 0; b < resume.Blocks.Length; b++)
        {
            var buf = pool.Take();
            int n;
            try { n = disk.Read(offset, buf, Options.ChunkSize); }
            catch
            {
                pool.Give(buf);
                throw;
            }
            if (n != Options.ChunkSize || XxHash3.HashToUInt64(buf.Span[..n]) != resume.Blocks[b])
            {
                pool.Give(buf);
                throw new IOException($"block {b} of {part} is not the checkpoint's");
            }
            var chunk = new Chunk(buf, n, offset, hashers.Count, pool);
            foreach (var h in hashers) h.Update(chunk);
            offset += n;
            Interlocked.Add(ref readBytes, n);
        }
        return offset;
    }

    /// Card readers have bad moments; a failed read gets a few more tries before the file is given up.
    int ReadWithRetries(FileReader reader, AlignedBuffer buf, long offset)
    {
```

#### `windows/src/Offload.Core/OffloadEngine.cs`

Najít (1/1, řádky 121–126):

```csharp
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
            Space = space, SpaceWarnings = warnings,
            Hashes = HashKinds.FromNames(a.HashAlgorithms),   // the earlier backup's, not the current setting (spec 3.5)
        };
    }

```

Nahradit:

```csharp
            Mode = PlanMode.Append, Generation = a.Generation, FullScan = a.FullScan, UnverifiedEarlier = a.UnverifiedEarlier,
            Space = space, SpaceWarnings = warnings,
            Hashes = HashKinds.FromNames(a.HashAlgorithms),   // the earlier backup's, not the current setting (spec 3.5)
            // R6: completing an unfinished generation — what is on the disks already is not written again.
            Resuming = a.Resuming, Write = a.Write, Known = a.Known, HashOnly = a.HashOnly,
            Resume = a.Resume?.Select(r => r?.BlockSize == Options.ChunkSize ? r : null).ToList(),
        };
    }

```

#### `windows/src/Offload.Core/OffloadReport.cs`

Najít (1/1, řádky 144–150):

```csharp
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
            Rewritten = plan.Write?[i].ToList(),
        }).ToList();
        var cardPaths = plan.CardPaths;
        var dests = plan.DestRoots.Select((root, d) => new ReportDestination
```

Nahradit:

```csharp
            SourceError = sourceErrors[i],
            Destinations = destResults.Select(r => r[i].State.Wire()).ToList(),
            Errors = destResults.Select(r => r[i].Error).ToList(),
            Rewritten = plan.Mode == PlanMode.Repair ? plan.Write?[i].ToList() : null,   // a resume's masks are no repair
        }).ToList();
        var cardPaths = plan.CardPaths;
        var dests = plan.DestRoots.Select((root, d) => new ReportDestination
```

#### `windows/src/Offload.Core/OffloadJob.cs`

Najít (1/8, řádky 135–140):

```csharp
    /// Plan 10: the verification running with the proxies (stage 2), null otherwise; the paused time when the copy ended.
    volatile VerifySession? session;
    TimeSpan pausedAtCopyEnd;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
```

Nahradit:

```csharp
    /// Plan 10: the verification running with the proxies (stage 2), null otherwise; the paused time when the copy ended.
    volatile VerifySession? session;
    TimeSpan pausedAtCopyEnd;
    /// R6: the history entry a full copy wrote when it started ("unfinished"), finished by RecordHistory.
    string? startEntry;

    /// Set on a worker thread, read by the app's UI thread.
    public OffloadPlan? Plan { get => planValue; private set => planValue = value; }
```

Najít (2/8, řádky 195–201):

```csharp
    /// none can (M4).
    public static string? EarlierProject(IReadOnlyList<string> cardPaths, string? fallback)
    {
        var earlier = cardPaths.Select(CardRecord.Load).FirstOrDefault(r => r.Generations.Count > 0);
        return earlier != null ? earlier.Project : fallback;
    }

```

Nahradit:

```csharp
    /// none can (M4).
    public static string? EarlierProject(IReadOnlyList<string> cardPaths, string? fallback)
    {
        var earlier = cardPaths.Select(p => CardRecord.Load(p)).FirstOrDefault(r => r.Generations.Count > 0);
        return earlier != null ? earlier.Project : fallback;
    }

```

Najít (3/8, řádky 345–350):

```csharp
            if (plan.UnverifiedEarlier > 0) Update(s => s.WithWarning(Msg.UnverifiedEarlier(plan.UnverifiedEarlier)));
            Update(s => s with { Phase = JobPhase.Copying });
            copying = true;
            // Plan 10 (R11): the read-back waits for stage 2, where the proxies read the copies.
            Engine.Options.DeferVerify = VerifiesInProxy(plan);
            var report = Engine.Run(plan);
```

Nahradit:

```csharp
            if (plan.UnverifiedEarlier > 0) Update(s => s.WithWarning(Msg.UnverifiedEarlier(plan.UnverifiedEarlier)));
            Update(s => s with { Phase = JobPhase.Copying });
            copying = true;
            // R6: history knows the copy from its start — one the app never finishes can be resumed from its card.
            if (plan.Mode == PlanMode.Full) RecordStart(plan, log);
            // Plan 10 (R11): the read-back waits for stage 2, where the proxies read the copies.
            Engine.Options.DeferVerify = VerifiesInProxy(plan);
            var report = Engine.Run(plan);
```

Najít (4/8, řádky 376–381):

```csharp
            Update(s => s with { SafeToEject = allCopied, SafeToFormat = safe, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                // Marked before the phase changes: whoever sees it finished also sees the day report pending (fix round 1, item 3).
                var cancelled = DayReportNext();   // the card shows as not complete (spec 3.2); not recorded in history
                Update(s => s with { Phase = JobPhase.Cancelled });
```

Nahradit:

```csharp
            Update(s => s with { SafeToEject = allCopied, SafeToFormat = safe, CopySucceeded = report.Success, Copy = Engine.Snapshot() });
            if (IsCancelled)
            {
                ForgetStart(log);   // a cancelled copy is not recorded in history (as before plan 10)
                // Marked before the phase changes: whoever sees it finished also sees the day report pending (fix round 1, item 3).
                var cancelled = DayReportNext();   // the card shows as not complete (spec 3.2); not recorded in history
                Update(s => s with { Phase = JobPhase.Cancelled });
```

Najít (5/8, řádky 459–464):

```csharp
        }
        catch (OperationCanceledException)
        {
            var next = copying && DayReportNext();
            Update(s => s with { Phase = JobPhase.Cancelled });
            return next;
```

Nahradit:

```csharp
        }
        catch (OperationCanceledException)
        {
            ForgetStart(log);
            var next = copying && DayReportNext();
            Update(s => s with { Phase = JobPhase.Cancelled });
            return next;
```

Najít (6/8, řádky 517–522):

```csharp

    HistoryStore Store(Action<string>? log) => Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job. An append adds
    /// its generation to the entry of the card folder it went into (spec 5.2) — or starts one when history does not
    /// know the backup — under one hold of the history lock (ruling I2).
```

Nahradit:

```csharp

    HistoryStore Store(Action<string>? log) => Config.HistoryFolder is string folder ? new HistoryStore(folder, log) : HistoryStore.Default(log);

    /// R6: the entry of a full copy as it starts — "unfinished", no files yet; RecordHistory replaces it at the end.
    void RecordStart(OffloadPlan plan, Action<string>? log)
    {
        if (!Config.RecordHistory) return;
        try
        {
            var store = Store(log);
            var id = HistoryStore.NewId(DateTime.UtcNow);
            var cardPaths = plan.CardPaths;
            var entry = new HistoryEntry
            {
                Id = id, Source = plan.Source, CardName = plan.CardName, Date = plan.Date, Project = plan.Project, Unfinished = true,
                Finished = DateTime.UtcNow, HashAlgorithms = [.. HashKinds.Names(plan.Hashes)], Card = cardVolume?.ToHistoryCard(),
                Destinations = plan.DestRoots.Select((root, i) => new HistoryDestination { Root = root, CardPath = cardPaths[i] }).ToList(),
            };
            if (store.UpdateWithFiles(id, _ => (entry, []))) startEntry = id;
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
        }
    }

    /// The start entry of a copy that was cancelled goes again.
    void ForgetStart(Action<string>? log)
    {
        if (startEntry is not string id) return;
        try { Store(log).Remove(id); }
        catch (Exception e) { log?.Invoke($"history: {e.Message}"); }
        startEntry = null;
    }

    /// The finished stage 1 (with failed files too) goes into history.json; this never fails the job. An append adds
    /// its generation to the entry of the card folder it went into (spec 5.2) — or starts one when history does not
    /// know the backup — under one hold of the history lock (ruling I2).
```

Najít (7/8, řádky 562–573):

```csharp
            var (fingerprint, shape) = CardFingerprint.Compute(card);
            var cardPaths = plan.CardPaths;
            // The id only finds the entry; everything about it is read and changed under the lock below.
            var id = (plan.Mode == PlanMode.Append ? EntryOf(store, cardPaths)?.Id : null) ?? HistoryStore.NewId(report.Finished);
            // Fix round 2, item C (ruling R2): verified as the card folders' reports say it now — the one definition the
            // day report uses too (a generation cancelled during its read-back is not in history, but is on the disks).
            var whole = BackupState.Of(cardPaths);
            store.UpdateWithFiles(id, existing =>
            {
                var entry = existing ?? new HistoryEntry
                {
                    Id = id,
```

Nahradit:

```csharp
            var (fingerprint, shape) = CardFingerprint.Compute(card);
            var cardPaths = plan.CardPaths;
            // The id only finds the entry; everything about it is read and changed under the lock below.
            var id = (plan.Mode == PlanMode.Append ? EntryOf(store, cardPaths)?.Id : startEntry) ?? HistoryStore.NewId(report.Finished);
            // Fix round 2, item C (ruling R2): verified as the card folders' reports say it now — the one definition the
            // day report uses too (a generation cancelled during its read-back is not in history, but is on the disks).
            var whole = BackupState.Of(cardPaths);
            store.UpdateWithFiles(id, existing =>
            {
                if (existing is { Unfinished: true })
                {
                    // R6: the start entry (or a resumed copy's) becomes the copy's entry.
                    existing.Unfinished = false;
                    existing.Success = true;
                    existing.Verified = true;
                    existing.FailedFiles = null;
                }
                var entry = existing ?? new HistoryEntry
                {
                    Id = id,
```

Najít (8/8, řádky 596–602):

```csharp
                entry.Generations.Add(new HistoryGeneration
                {
                    Seq = plan.Generation,
                    Kind = plan.Mode == PlanMode.Append ? HistoryGeneration.Append : HistoryGeneration.Full,
                    Finished = report.Finished,
                    Files = plan.Scan.Files.Count,
                    Bytes = plan.Scan.TotalBytes,
```

Nahradit:

```csharp
                entry.Generations.Add(new HistoryGeneration
                {
                    Seq = plan.Generation,
                    Kind = plan.Generation == 1 ? HistoryGeneration.Full : HistoryGeneration.Append,   // a resumed full copy is "full"
                    Finished = report.Finished,
                    Files = plan.Scan.Files.Count,
                    Bytes = plan.Scan.TotalBytes,
```

#### `windows/src/Offload.Core/History/HistoryEntry.cs`

Najít (1/1, řádky 60–65):

```csharp
    [JsonPropertyName("destinations")] public List<HistoryDestination> Destinations { get; set; } = [];
    [JsonPropertyName("generations")] public List<HistoryGeneration> Generations { get; set; } = [];
    [JsonPropertyName("reels")] public List<string> Reels { get; set; } = [];
}

/// How a card volume is recognised again (R1): the file system's serial number plus the capacity. The volume GUID is
```

Nahradit:

```csharp
    [JsonPropertyName("destinations")] public List<HistoryDestination> Destinations { get; set; } = [];
    [JsonPropertyName("generations")] public List<HistoryGeneration> Generations { get; set; } = [];
    [JsonPropertyName("reels")] public List<string> Reels { get; set; } = [];
    /// R6 (plan 10): a full copy that started and has not ended yet — written when the copy starts, so a copy the app
    /// never finished (it crashed, the PC went off) is known: its card offers "Dokončit přerušenou zálohu" (an append
    /// that resumes it). Its file list is empty until the copy ends. Absent when false.
    [JsonPropertyName("unfinished"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Unfinished { get; set; }
}

/// How a card volume is recognised again (R1): the file system's serial number plus the capacity. The volume GUID is
```

#### `windows/src/Offload.Core/History/HistoryStore.cs`

Najít (1/1, řádky 77–82):

```csharp
    /// disk failure) — logged. Keep `modify` quick: the lock is held meanwhile.
    public bool Update(string id, Func<HistoryEntry?, HistoryEntry?> modify, IReadOnlyList<SourceFile>? files = null) =>
        UpdateWithFiles(id, e => modify(e) is { } changed ? (changed, files) : null);

    /// Update whose `modify` also returns the file list (null = keep the stored one) — e.g. the union with the stored
    /// list (ReadFileList), read under the same hold of the lock (an append, spec 5.2). A null result changes nothing.
```

Nahradit:

```csharp
    /// disk failure) — logged. Keep `modify` quick: the lock is held meanwhile.
    public bool Update(string id, Func<HistoryEntry?, HistoryEntry?> modify, IReadOnlyList<SourceFile>? files = null) =>
        UpdateWithFiles(id, e => modify(e) is { } changed ? (changed, files) : null);

    /// Removes entry `id` and its file list (plan 10: the start entry of a full copy that was cancelled). False when it was
    /// not there or nothing could be written — logged.
    public bool Remove(string id)
    {
        if (!SafeId().IsMatch(id)) return false;
        try
        {
            using var held = Lock();
            if (held == null)
            {
                log?.Invoke($"history: busy for {LockTimeout.TotalSeconds:0} s — the entry {id} is not removed");
                return false;
            }
            var (entries, version) = ReadFile(repair: true);
            if (version > CurrentVersion || entries.RemoveAll(e => e.Id == id) == 0) return false;
            if (FileListPath(id) is string list) NativeIO.Delete(list);
            NativeIO.WriteFileReplacing(FilePath, JsonSerializer.SerializeToUtf8Bytes(new HistoryFile { Entries = entries }, Json));
            return true;
        }
        catch (Exception e)
        {
            log?.Invoke($"history: {e.Message}");
            return false;
        }
    }

    /// Update whose `modify` also returns the file list (null = keep the stored one) — e.g. the union with the stored
    /// list (ReadFileList), read under the same hold of the lock (an append, spec 5.2). A null result changes nothing.
```

#### `windows/src/Offload.Core/History/HistoryText.cs`

Najít (1/1, řádky 40–41):

```csharp
        var disks = string.Join(", ", e.Destinations.Select(d => fullPaths ? d.Root : DiskName(d.Root)));
        var head = $"Zálohováno {When(e.Finished)} na {disks}";
```

Nahradit:

```csharp
        var disks = string.Join(", ", e.Destinations.Select(d => fullPaths ? d.Root : DiskName(d.Root)));
        // R6: a copy the app never finished — it can be resumed (Dokončit přerušenou zálohu).
        if (e.Unfinished) return ($"Nedokončená záloha {When(e.Finished)} na {disks} — kopie se přerušila, lze ji dokončit", HistoryTone.Warning);
        var head = $"Zálohováno {When(e.Finished)} na {disks}";
```

#### `windows/src/Offload.Core/Desktop/HistoryView.cs`

Najít (1/1, řádek 23):

```csharp
        var (status, tone) = !e.Success
```

Nahradit:

```csharp
        var (status, tone) = e.Unfinished ? ("! nedokončeno", HistoryTone.Warning)
            : !e.Success
```

#### `windows/src/Offload.Core/Desktop/AppendRules.cs`

Najít (1/1, řádky 12–20):

```csharp
        insight is { Error: null, Volume: not null, AppendReachable: true, History: { Kind: HistoryMatchKind.NewFiles } m }
        && m.Entry.Destinations.Count > 0;

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)"
    public static string CheckboxText(HistoryMatch m) =>
        $"Doplnit jen nové klipy do dřívější zálohy ({m.New.Count} {Texts.Plural(m.New.Count, "soubor", "soubory", "souborů")} · {Format.BytesCs(m.NewBytes)})";

    /// "Složka: 2026-10-06\A001 na 2 cílech" — taken from the backup's own card folders (the name may be "A001_2").
    public static string FolderText(HistoryEntry e)
```

Nahradit:

```csharp
        insight is { Error: null, Volume: not null, AppendReachable: true, History: { Kind: HistoryMatchKind.NewFiles } m }
        && m.Entry.Destinations.Count > 0;

    /// "Doplnit jen nové klipy do dřívější zálohy (12 souborů · 3,2 GB)"; R6: "Dokončit přerušenou zálohu (…)" for a copy
    /// the app never finished (what is on the disks already is not copied again).
    public static string CheckboxText(HistoryMatch m) => m.Entry.Unfinished
        ? $"Dokončit přerušenou zálohu ({m.New.Count} {Texts.Plural(m.New.Count, "soubor", "soubory", "souborů")} · {Format.BytesCs(m.NewBytes)})"
        : $"Doplnit jen nové klipy do dřívější zálohy ({m.New.Count} {Texts.Plural(m.New.Count, "soubor", "soubory", "souborů")} · {Format.BytesCs(m.NewBytes)})";

    /// "Složka: 2026-10-06\A001 na 2 cílech" — taken from the backup's own card folders (the name may be "A001_2").
    public static string FolderText(HistoryEntry e)
```

#### `windows/src/Offload.Core/Desktop/BackedUpPrompt.cs`

Najít (1/1, řádky 16–22):

```csharp
    /// A selected card that history knows is asked about before a full copy — except one that changed since: its own
    /// line already says that only a full copy is possible — and
    /// not for a card emptied in the camera (nothing on it to copy).
    public static bool ShouldAsk(CardInsight? insight) => insight is { IsEmptied: false, History: { Kind: not HistoryMatchKind.Changed } };

    /// The selected cards the prompt asks about: backed up, and not ticked for the append (spec 2.2 — those add only the
    /// new files, there is no full copy to warn about).
```

Nahradit:

```csharp
    /// A selected card that history knows is asked about before a full copy — except one that changed since: its own
    /// line already says that only a full copy is possible — and
    /// not for a card emptied in the camera (nothing on it to copy).
    public static bool ShouldAsk(CardInsight? insight) =>
        insight is { IsEmptied: false, History: { Kind: not HistoryMatchKind.Changed, Entry.Unfinished: false } };   // R6: an unfinished copy is no backup

    /// The selected cards the prompt asks about: backed up, and not ticked for the append (spec 2.2 — those add only the
    /// new files, there is no full copy to warn about).
```

#### `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs`

Najít (1/1, řádky 27–29):

```csharp
        $"Výsledek ověření se nepodařilo zapsat ({detail}) — kartu zatím neformátujte, spusťte Zkopírovat znovu.";

    /// Spec 7:
```

Nahradit:

```csharp
        $"Výsledek ověření se nepodařilo zapsat ({detail}) — kartu zatím neformátujte, spusťte Zkopírovat znovu.";

    // R6 (resume).
    public static string ResumeDiverged(string cardPath, int generation) =>
        $"{cardPath}: nedokončená záloha (generace {generation}) nesouhlasí s ostatními cíli — zkontrolujte ji ručně.";
    public const string RepairUnfinished = "Záloha nebyla dokončena (aplikace skončila během kopie nebo ověření) — dokončete ji nejdřív tlačítkem Doplnit (offload append).";

    /// Spec 7:
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ResumeTests|FullyQualifiedName~AppendEngineTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~RepairPlannerTests|FullyQualifiedName~RepairEngineTests|FullyQualifiedName~HistoryStoreTests|FullyQualifiedName~HistoryTextTests|FullyQualifiedName~AppendRulesTests|FullyQualifiedName~BackedUpPromptTests|FullyQualifiedName~ManifestRecordingTests"`

Expected: PASS — `Failed: 0, Passed: 134, Skipped: 0, Total: 134` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1626` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 13: navázání po pádu (R6) — doplnění dokončí nepotvrzenou generaci podle manifestu (celé soubory se z karty nečtou, rozepsaný soubor pokračuje za kontrolním bodem, nic se nepřepíše); oprava ji odmítne; historie zná kopii od startu („nedokončeno“).
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: resume an unfinished backup from the manifest, unfinished history entry (plan 10, task 13)

An append completes a generation that a crash or an unfinished verification never committed:
recorded files are not read from the card again, a part continues after its checkpoint, files on
disk without a record are hashed and verified, nothing is overwritten. Repair refuses such a
backup. History records a full copy when it starts (unfinished) and removes it on cancel.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Append\AppendPlanner.cs windows\src\Offload.Core\Append\RepairPlanner.cs windows\src\Offload.Core\Append\ResumePlanner.cs windows\src\Offload.Core\CardRecord.cs windows\src\Offload.Core\Desktop\AppendRules.cs windows\src\Offload.Core\Desktop\BackedUpPrompt.cs windows\src\Offload.Core\Desktop\HistoryView.cs windows\src\Offload.Core\DestWriter.cs windows\src\Offload.Core\History\HistoryEntry.cs windows\src\Offload.Core\History\HistoryStore.cs windows\src\Offload.Core\History\HistoryText.cs windows\src\Offload.Core\Models.cs windows\src\Offload.Core\NativeIO.cs windows\src\Offload.Core\OffloadEngine.Run.cs windows\src\Offload.Core\OffloadEngine.cs windows\src\Offload.Core\OffloadJob.cs windows\src\Offload.Core\OffloadReport.cs windows\src\Offload.Core\VerifyInProxy\Msg.VerifyInProxy.cs windows\tests\Offload.Core.Tests\ResumeTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 14: Sériové číslo svazku pro „ověřeno“ a denní report „ověřuje se“ (R12)

Jedna definice „ověřeno“ (`CardRecord.ReadBack`, ruling R2 plánu 6) bere sloupec cíle podle cesty — disk pod
jiným písmenem (vyměněné D: a E:) by mohl vzít sloupec jiného disku. Report teď nese `volumeSerial` každého cíle
(úkol 6) a `CardRecord` čte sériové číslo složky; když ho report má, sloupec je ten se stejným svazkem **a** stejnou
cestou pod kořenem, nejednoznačnost = -1 (pak musí souhlasit všechny sloupce, jako u reportu z Macu). Starší reporty
beze změny. Denní report u karty, jejíž ověření s proxy ještě neskončilo (nebo se přerušilo), píše „OVĚŘUJE SE“ a mezi
problémy „ověření s proxy ještě běží, nebo se přerušilo — kartu zatím neformátujte“, nikdy zeleně.

**Files:**
- Modify: `windows/src/Offload.Core/CardRecord.cs:42-48, 143-148, 176-184, 188-200`
- Modify: `windows/src/Offload.Core/Append/AppendPlanner.cs:153-159`
- Modify: `windows/src/Offload.Core/Append/RepairPlanner.cs:78-84, 109-115`
- Modify: `windows/src/Offload.Core/Append/BackupState.cs:15-21`
- Modify: `windows/src/Offload.Core/Outputs/DayReport.cs:175-181, 231-237`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/ReadBackSerialTests.cs` (nový)

**Interfaces:**
- Consumes: `ReportDestination.VolumeSerial` (úkol 6), `NativeIO.VolumeSerialText` (úkol 5), `OffloadReport.VerifyPending`.
- Produces:
  - `CardRecord.VolumeSerial`, `CardRecord.VerifyPending`; `CardRecord.ReadBack(rec, cardPath, string? serial = null)`, `ColumnOf(report, cardPath, string? serial = null)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/ReadBackSerialTests.cs` (nový soubor)

```csharp
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// R12 (plan 10): a destination is matched by its volume serial — a disk under another letter never takes another
/// disk's column (the parked plan 9 finding); the day report says a verification with the proxies has not ended.
public class ReadBackSerialTests
{
    // R12: the disk is found by its serial — the parked plan 9 finding (letters swapped → a false green day report).
    [Fact]
    public void ReadBack_BySerial_LettersSwapped_NeverAnotherDisksColumn()
    {
        var t = new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc);
        var report = new OffloadReport
        {
            CardName = "A001", Date = "2026-10-08", Source = @"E:\", SourceFilesystem = "exFAT", Verified = true,
            Destinations =
            [
                new ReportDestination { Root = @"D:\B", CardPath = @"D:\B\2026-10-08\A001", Filesystem = "NTFS", VolumeSerial = "AAAA-0001" },
                new ReportDestination { Root = @"F:\B", CardPath = @"F:\B\2026-10-08\A001", Filesystem = "NTFS", VolumeSerial = "BBBB-0002" },
            ],
            // D:'s copy was never read back (its read-back was cancelled), F:'s was.
            Files = [new ReportFile { Path = "a.mov", Size = 1, Modified = t, Xxh64 = "0123456789abcdef", Destinations = ["written", "verified"], Errors = [null, null] }],
            Skipped = [],
        };
        var rec = new RecordedFile(report.Files[0], 1, report);
        // The disk that was D: is F: now: by its path it would read the other disk's "verified".
        Assert.True(CardRecord.ReadBack(rec, @"F:\B\2026-10-08\A001"));
        Assert.False(CardRecord.ReadBack(rec, @"F:\B\2026-10-08\A001", "AAAA-0001"));
        Assert.True(CardRecord.ReadBack(rec, @"D:\B\2026-10-08\A001", "BBBB-0002"));
        Assert.Equal(-1, CardRecord.ColumnOf(report, @"F:\B\2026-10-08\A001", "CCCC-0003"));   // an unknown disk: no column
    }

    [Fact]
    public void DayReport_PendingCard_SaysVerifying_NotGreen()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 10_000, 1);
        var root = dir.Sub("d1");
        var engine = new OffloadEngine(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, DeferVerify = true });
        engine.Run(engine.Prepare(card, [root], null, "2026-10-08"));

        var day = DayReport.Load(Path.Combine(root, "2026-10-08"));
        Assert.Contains("OVĚŘUJE SE", DayReport.Html(day));
        Assert.Contains(DayReport.Problems(day), p => p.Text.StartsWith("ověření s proxy ještě běží", StringComparison.Ordinal));
        Assert.Equal(0, day.VerifiedCards);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ReadBackSerialTests|FullyQualifiedName~CardRecordTests|FullyQualifiedName~DayReportTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~RepairPlannerTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS1501: No overload for method 'ReadBack' takes 3 arguments`; `CS1501: No overload for method 'ColumnOf' takes 3 arguments`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/CardRecord.cs`

Najít (1/4, řádky 42–48):

```csharp
    /// one read back no longer leaves the card unverified for ever. A folder whose reports list no file: every report
    /// was read back.
    public bool Verified => Generations.Count > 0 && Problems.Count == 0
        && (Files.Count == 0 ? Generations.All(g => g.Report.Verified) : Files.Values.All(f => ReadBack(f, CardPath)));
    /// Paths whose newest record failed (the card or a destination), sorted.
    public IReadOnlyList<string> FailedFiles =>
        Files.Values.Where(f => f.Report.IsFailed(f.File)).Select(f => f.File.Path).Order(StringComparer.Ordinal).ToList();
```

Nahradit:

```csharp
    /// one read back no longer leaves the card unverified for ever. A folder whose reports list no file: every report
    /// was read back.
    public bool Verified => Generations.Count > 0 && Problems.Count == 0
        && (Files.Count == 0 ? Generations.All(g => g.Report.Verified) : Files.Values.All(f => ReadBack(f, CardPath, VolumeSerial)));
    /// R12 (plan 10): the serial of the volume the folder is on now (Load reads it), null when unknown — a report that
    /// names its destinations' serials is matched by it, so a disk under another letter never takes another's column.
    public string? VolumeSerial { get; init; }
    /// Plan 10: a generation's verification with the proxies has not ended (its report says verifyPending).
    public bool VerifyPending => Generations.Any(g => g.Report.VerifyPending == true);
    /// Paths whose newest record failed (the card or a destination), sorted.
    public IReadOnlyList<string> FailedFiles =>
        Files.Values.Where(f => f.Report.IsFailed(f.File)).Select(f => f.File.Path).Order(StringComparer.Ordinal).ToList();
```

Najít (2/4, řádky 143–148):

```csharp
        return new CardRecord
        {
            CardPath = cardPath, Generations = generations, Files = files, Clips = clips, Problems = problems, HighestReportNumber = highest,
        };
    }

```

Nahradit:

```csharp
        return new CardRecord
        {
            CardPath = cardPath, Generations = generations, Files = files, Clips = clips, Problems = problems, HighestReportNumber = highest,
            VolumeSerial = NativeIO.VolumeSerialText(cardPath),
        };
    }

```

Najít (3/4, řádky 176–184):

```csharp
    /// was not read back (cancelled, made without verification), is not. The column is the report destination whose card
    /// path is this folder (ColumnOf); when none is (a Mac report, the disk under another letter) every column must say
    /// so — never falsely green.
    public static bool ReadBack(RecordedFile rec, string cardPath)
    {
        int column = ColumnOf(rec.Report, cardPath);
        var states = column >= 0
            ? [column < rec.File.Destinations.Count ? rec.File.Destinations[column] : "pending"]
            : rec.File.Destinations.Count > 0 ? rec.File.Destinations : ["pending"];
```

Nahradit:

```csharp
    /// was not read back (cancelled, made without verification), is not. The column is the report destination whose card
    /// path is this folder (ColumnOf); when none is (a Mac report, the disk under another letter) every column must say
    /// so — never falsely green.
    /// `serial` (R12): the folder's volume serial now — with it, a report that recorded serials is matched by the disk.
    public static bool ReadBack(RecordedFile rec, string cardPath, string? serial = null)
    {
        int column = ColumnOf(rec.Report, cardPath, serial);
        var states = column >= 0
            ? [column < rec.File.Destinations.Count ? rec.File.Destinations[column] : "pending"]
            : rec.File.Destinations.Count > 0 ? rec.File.Destinations : ["pending"];
```

Najít (4/4, řádky 188–200):

```csharp

    /// The column of `report` whose destination card path is `cardPath` (full path, any case, trailing separator), or
    /// -1 when none is. A path that is not a full Windows path (a Mac `/Volumes/…`) is compared as written.
    public static int ColumnOf(OffloadReport report, string cardPath)
    {
        var me = Normal(cardPath);
        for (int i = 0; i < report.Destinations.Count; i++)
            if (string.Equals(Normal(report.Destinations[i].CardPath), me, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    static string Normal(string path)
    {
```

Nahradit:

```csharp

    /// The column of `report` whose destination card path is `cardPath` (full path, any case, trailing separator), or
    /// -1 when none is. A path that is not a full Windows path (a Mac `/Volumes/…`) is compared as written.
    /// R12 (plan 10): with `serial` and a report that recorded its destinations' serials, the column is the one of that
    /// disk with the same folder under it — whatever letter either has; -1 when none or several are (then every column
    /// must say so, as for a report from the Mac).
    public static int ColumnOf(OffloadReport report, string cardPath, string? serial = null)
    {
        var me = Normal(cardPath);
        if (serial != null && report.Destinations.Any(d => d.VolumeSerial != null))
        {
            var hits = Enumerable.Range(0, report.Destinations.Count).Where(i => report.Destinations[i].VolumeSerial == serial
                && string.Equals(UnderRoot(Normal(report.Destinations[i].CardPath)), UnderRoot(me), StringComparison.OrdinalIgnoreCase)).ToList();
            return hits.Count == 1 ? hits[0] : -1;
        }
        for (int i = 0; i < report.Destinations.Count; i++)
            if (string.Equals(Normal(report.Destinations[i].CardPath), me, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// The path without its drive or share root ("\Zaloha\2026-10-08\A001"); a path that is not a full one as it is.
    static string UnderRoot(string path) =>
        Path.IsPathFullyQualified(path) && Path.GetPathRoot(path) is { Length: > 0 } root ? path[root.TrimEnd('\\', '/').Length..] : path;

    static string Normal(string path)
    {
```

#### `windows/src/Offload.Core/Append/AppendPlanner.cs`

Najít (1/1, řádky 153–159):

```csharp
                    continue;
                }
                goodInRecord++;
                if (!CardRecord.ReadBack(rec, paths[d])) unverifiedFile = true;   // ruling R2: the one definition
                if (rec.File.Size != f.Size || !FileTimes.Close(rec.File.Modified, f.ModifiedUtc, cardLocalTime))
                    conflicts.Add(Conflict(f, Disk(paths[d]), rec.File.Size, rec.File.Modified));
                else if (!exists)
```

Nahradit:

```csharp
                    continue;
                }
                goodInRecord++;
                if (!CardRecord.ReadBack(rec, paths[d], records[d].VolumeSerial)) unverifiedFile = true;   // ruling R2: the one definition (R12: by disk)
                if (rec.File.Size != f.Size || !FileTimes.Close(rec.File.Modified, f.ModifiedUtc, cardLocalTime))
                    conflicts.Add(Conflict(f, Disk(paths[d]), rec.File.Size, rec.File.Modified));
                else if (!exists)
```

#### `windows/src/Offload.Core/Append/RepairPlanner.cs`

Najít (1/2, řádky 78–84):

```csharp
        var todo = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int d = 0; d < paths.Count; d++)
            foreach (var (path, rec) in records[d].Files)
                if (rec.File.SourceError != null || !Good(rec, paths[d], d) || !CardRecord.ReadBack(rec, paths[d])) todo.Add(path);
        // The card is scanned before "Nic k opravě" (batch 10–12 review): a file shot after the backup is in no report,
        // and with nothing to repair it must still keep the card from "kartu lze vysunout".
        var scan = Scanner.Scan(source, ct);
```

Nahradit:

```csharp
        var todo = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int d = 0; d < paths.Count; d++)
            foreach (var (path, rec) in records[d].Files)
                if (rec.File.SourceError != null || !Good(rec, paths[d], d) || !CardRecord.ReadBack(rec, paths[d], records[d].VolumeSerial)) todo.Add(path);
        // The card is scanned before "Nic k opravě" (batch 10–12 review): a file shot after the backup is in no report,
        // and with nothing to repair it must still keep the card from "kartu lze vysunout".
        var scan = Scanner.Scan(source, ct);
```

Najít (2/2, řádky 109–115):

```csharp
                {
                    // A good copy: never written — but it must be there to be read back (as an append requires).
                    if (!exists) throw OffloadError.Preflight(PreflightCode.AppendMissingFile, Msg.AppendMissingFile(AppendPlanner.Disk(paths[d]), path));
                    before[d] = CardRecord.ReadBack(rec, paths[d]);
                    continue;
                }
                mask[d] = true;
```

Nahradit:

```csharp
                {
                    // A good copy: never written — but it must be there to be read back (as an append requires).
                    if (!exists) throw OffloadError.Preflight(PreflightCode.AppendMissingFile, Msg.AppendMissingFile(AppendPlanner.Disk(paths[d]), path));
                    before[d] = CardRecord.ReadBack(rec, paths[d], records[d].VolumeSerial);
                    continue;
                }
                mask[d] = true;
```

#### `windows/src/Offload.Core/Append/BackupState.cs`

Najít (1/1, řádky 15–21):

```csharp
            var record = CardRecord.Load(cardPaths[d]);
            failed.UnionWith(record.FailedFiles);
            foreach (var (path, rec) in record.Files)
                if (!CardRecord.ReadBack(rec, cardPaths[d])) unverified.Add(path);
        }
        unverified.ExceptWith(failed);
        return new BackupState(failed.Count, unverified.Count);
```

Nahradit:

```csharp
            var record = CardRecord.Load(cardPaths[d]);
            failed.UnionWith(record.FailedFiles);
            foreach (var (path, rec) in record.Files)
                if (!CardRecord.ReadBack(rec, cardPaths[d], record.VolumeSerial)) unverified.Add(path);
        }
        unverified.ExceptWith(failed);
        return new BackupState(failed.Count, unverified.Count);
```

#### `windows/src/Offload.Core/Outputs/DayReport.cs`

Najít (1/2, řádky 175–181):

```csharp
            }
            int failed = r.FailedFiles.Count;
            // Read back, but a file failed: not green (fix round 1, item 6).
            var (vClass, vText) = !r.Verified ? ("warn", "NEOVĚŘENO") : failed > 0 ? ("warn", "ověřeno, s chybami") : ("ok", "ověřeno");
            var proxies = c.ProxiesDone + c.ProxiesFailed == 0 ? "—" : $"{c.ProxiesDone} / {c.ProxiesFailed}";
            if (c.ProxiesLighter > 0) proxies += $" · {c.ProxiesLighter} bez proxy";
            var lutNames = c.Luts.Count == 0 ? "—" : string.Join(", ", c.Luts.Keys);
```

Nahradit:

```csharp
            }
            int failed = r.FailedFiles.Count;
            // Read back, but a file failed: not green (fix round 1, item 6).
            // Plan 10: a verification with the proxies that has not ended says so (never green, never "unverified" yet).
            var (vClass, vText) = r.VerifyPending ? ("warn", "OVĚŘUJE SE") : !r.Verified ? ("warn", "NEOVĚŘENO")
                : failed > 0 ? ("warn", "ověřeno, s chybami") : ("ok", "ověřeno");
            var proxies = c.ProxiesDone + c.ProxiesFailed == 0 ? "—" : $"{c.ProxiesDone} / {c.ProxiesFailed}";
            if (c.ProxiesLighter > 0) proxies += $" · {c.ProxiesLighter} bez proxy";
            var lutNames = c.Luts.Count == 0 ? "—" : string.Join(", ", c.Luts.Keys);
```

Najít (2/2, řádky 231–237):

```csharp
            if (failed.Count > 0)
                list.Add((c.Name, $"{Files(failed.Count)} {Texts.Plural(failed.Count, "selhal", "selhaly", "selhalo")}: "
                    + string.Join(", ", failed.Take(5)) + (failed.Count > 5 ? ", …" : ""), true));
            if (!c.Record.Verified && unreadable.Count == 0) list.Add((c.Name, "neověřeno — kopie nebyla přečtena zpět", false));
        }
        return list;
    }
```

Nahradit:

```csharp
            if (failed.Count > 0)
                list.Add((c.Name, $"{Files(failed.Count)} {Texts.Plural(failed.Count, "selhal", "selhaly", "selhalo")}: "
                    + string.Join(", ", failed.Take(5)) + (failed.Count > 5 ? ", …" : ""), true));
            if (c.Record.VerifyPending && unreadable.Count == 0)
                list.Add((c.Name, "ověření s proxy ještě běží, nebo se přerušilo — kartu zatím neformátujte", false));
            else if (!c.Record.Verified && unreadable.Count == 0) list.Add((c.Name, "neověřeno — kopie nebyla přečtena zpět", false));
        }
        return list;
    }
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~ReadBackSerialTests|FullyQualifiedName~CardRecordTests|FullyQualifiedName~DayReportTests|FullyQualifiedName~AppendPlannerTests|FullyQualifiedName~RepairPlannerTests"`

Expected: PASS — `Failed: 0, Passed: 72, Skipped: 0, Total: 72` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1628` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 14: „ověřeno“ podle sériového čísla svazku (R12: disk pod jiným písmenem nikdy nevezme sloupec jiného disku); denní report „OVĚŘUJE SE“ u karty, jejíž ověření s proxy neskončilo.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: read-back column by volume serial, day report while verifying (plan 10, task 14)

CardRecord reads the folder's volume serial and ReadBack matches a report column by serial and
path under the root, so swapped drive letters never borrow another disk's column. The day report
shows a card whose verification has not ended as "OVĚŘUJE SE", never green.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Append\AppendPlanner.cs windows\src\Offload.Core\Append\BackupState.cs windows\src\Offload.Core\Append\RepairPlanner.cs windows\src\Offload.Core\CardRecord.cs windows\src\Offload.Core\Outputs\DayReport.cs windows\tests\Offload.Core.Tests\ReadBackSerialTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 15: Karta úlohy, oznámení a historie: dva stavy „Lze vyjmout“ / „Lze formátovat“ (R5, spec 7, 9)

UI dostane sémantiku R5: zelená karta a „kartu lze formátovat“ jen s `SafeToFormat`; po kopii, než ověření
skončí, „A001 — lze vyjmout, formátovat zatím ne“. Řádek karty má dva štítky, které se nikdy nespojí: „Lze vyjmout“
a „Lze formátovat“ (`JobCard.Chips`; žlutě „Lze formátovat po kontrole vad“, dokud uživatel nepotvrdí vady obsahu).
Kroky běžící karty s ověřením během proxy: Kopírování · Proxy · Ověření · Report. Vady obsahu: seznam klipů s druhem
vady a TC, náhled místa vady (JPEG otevřený mimo UI vlákno přes dočasný soubor) a tlačítko potvrzení — Offload sám
kartu neformátuje, potvrzení jen odemkne zelenou. Oznámení: „kartu lze vyjmout“ na konci kopie, „hotovo — kartu lze
formátovat“ až po ověření. Texty historie a nastavení řeknou „lze formátovat“. CLI vypíše oba stavy a průběh dokončení.

**Files:**
- Create: `windows/src/Offload.App/JobItemViewModel.Verify.cs`
- Modify: `windows/src/Offload.Core/Desktop/JobCard.cs:32-37, 97-119, 214-257`
- Modify: `windows/src/Offload.Core/Desktop/Texts.cs:13-20`
- Modify: `windows/src/Offload.Core/History/HistoryText.cs:44-51`
- Modify: `windows/src/Offload.Core/Desktop/HistoryView.cs:23-30`
- Modify: `windows/src/Offload.Core/Notify/JobOutcome.cs:56-71`
- Modify: `windows/src/Offload.Core/Desktop/DemoScenario.cs:144-150`
- Modify: `windows/src/Offload.App/JobItemViewModel.cs:45-50, 74-80, 206-212`
- Modify: `windows/src/Offload.App/QueuePanel.xaml:26-31, 211-216, 264-269, 369-376, 463-468`
- Modify: `windows/src/Offload.Cli/ProgressDisplay.cs:110-116`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/VerifyVerdictTests.cs` (nový)
- Test: `windows/tests/Offload.Core.Tests/CliNtfyTests.cs:52-58`
- Test: `windows/tests/Offload.Core.Tests/DemoScenarioTests.cs:72-78`
- Test: `windows/tests/Offload.Core.Tests/HistoryTextTests.cs:15-29, 36-46`
- Test: `windows/tests/Offload.Core.Tests/JobCardStateTests.cs:27-32, 40-46, 69-87`
- Test: `windows/tests/Offload.Core.Tests/JobCardTests.cs:66-73, 189-196, 203-214`
- Test: `windows/tests/Offload.Core.Tests/JobProgressTests.cs:167-174`
- Test: `windows/tests/Offload.Core.Tests/NotifyTests.cs:17-32, 70-76`
- Test: `windows/tests/Offload.Core.Tests/SetupTextsTests.cs:89-96, 109-115, 120-128`

**Interfaces:**
- Consumes: `JobStatus.SafeToEject/SafeToFormat/ContentWarnings/VerifiedBytes/VerifyTotal`, `JobPhase.Finishing` (úkol 12), `ClipEntry.Content` (úkol 10).
- Produces:
  - `ChipTone`, `CardChip(Text, Tone)`, `JobCard.Chips(JobStatus, bool contentConfirmed = false)`; `JobCard.Headline(…, bool contentConfirmed = false)`; `JobCard.Steps` pořadí s ověřením během proxy.
  - `Texts.SafeToEject(disks)` (nový text), `Texts.SafeToFormat(disks)`.
  - App: `JobItemViewModel.SafeToFormat`, `ShowChips`, `EjectChip`, `FormatChip`, `NeedsContentConfirm`, `ContentRows`, `ConfirmContentCommand`; `ContentRowViewModel`; šablony `VerdictChips`, `ContentWarnings` v `QueuePanel.xaml`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/VerifyVerdictTests.cs` (nový soubor)

```csharp
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Core.Tests;

/// R5 (plan 10): "Lze vyjmout" and "Lze formátovat" — never merged — on the job card, its chips, its steps and the
/// notifications; spec 7: content warnings wait for a confirmation.
public class VerifyVerdictTests
{
    static readonly string[] Disks = ["RAID_A", "SHUTTLE_B"];
    const string Card = @"D:\Zaloha\2026-10-08\A001";

    static OffloadReport Report(bool verified = true) => Reports.Sample(Card, verified: verified);

    [Fact]
    public void Headline_CopiedNotVerified_EjectYesFormatNo()
    {
        var s = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = false, Warning = Msg.UnverifiedEarlier(2) };
        var h = JobCard.Headline("A001", s, Report(), Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — lze vyjmout, formátovat zatím ne"), (h.Kind, h.Title));
    }

    [Fact]
    public void Headline_VerifiedEverywhere_Green_LzeFormatovat()
    {
        var s = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = true };
        var h = JobCard.Headline("A001", s, Report(), Disks);
        Assert.Equal((JobCardKind.Done, "A001 — kartu lze formátovat"), (h.Kind, h.Title));
    }

    // Spec 7: content warnings keep the card yellow until the user confirms them.
    [Fact]
    public void Headline_ContentWarnings_YellowUntilConfirmed()
    {
        var s = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = true, ContentWarnings = 2 };
        var h = JobCard.Headline("A001", s, Report(), Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — před formátováním zkontrolujte vady obsahu", Msg.ContentWarnings(2)), (h.Kind, h.Title, h.Sub));
        Assert.Equal(JobCardKind.Done, JobCard.Headline("A001", s, Report(), Disks, contentConfirmed: true).Kind);
    }

    [Fact]
    public void Headline_CancelledAfterTheCopy_SaysEjectButNotFormat()
    {
        var s = new JobStatus { Phase = JobPhase.Cancelled, SafeToEject = true };
        Assert.StartsWith("Kopie je hotová, ale neověřená — kartu lze vyjmout", JobCard.Headline("A001", s, Report(false), Disks).Sub);
    }

    [Theory]
    [InlineData(JobPhase.Proxies, true, false, 0, false, "Lze vyjmout", ChipTone.Ok, "Ověřuje se…", ChipTone.Neutral)]
    [InlineData(JobPhase.Copying, false, false, 0, false, "Vyjmout zatím ne", ChipTone.Neutral, "Formátovat zatím ne", ChipTone.Neutral)]
    [InlineData(JobPhase.Done, true, true, 0, false, "Lze vyjmout", ChipTone.Ok, "Lze formátovat", ChipTone.Ok)]
    [InlineData(JobPhase.Done, true, true, 1, false, "Lze vyjmout", ChipTone.Ok, "Lze formátovat po kontrole vad", ChipTone.Warning)]
    [InlineData(JobPhase.Done, true, true, 1, true, "Lze vyjmout", ChipTone.Ok, "Lze formátovat", ChipTone.Ok)]
    [InlineData(JobPhase.Done, true, false, 0, false, "Lze vyjmout", ChipTone.Ok, "Neformátovat", ChipTone.Warning)]
    [InlineData(JobPhase.Done, false, false, 0, false, "Nevyjímat", ChipTone.Error, "Neformátovat", ChipTone.Error)]
    public void Chips_TwoVerdicts_NeverMerged(JobPhase phase, bool eject, bool format, int warnings, bool confirmed,
        string ejectText, ChipTone ejectTone, string formatText, ChipTone formatTone)
    {
        var (e, f) = JobCard.Chips(new JobStatus { Phase = phase, SafeToEject = eject, SafeToFormat = format, ContentWarnings = warnings }, confirmed);
        Assert.Equal((ejectText, ejectTone, formatText, formatTone), (e.Text, e.Tone, f.Text, f.Tone));
    }

    [Fact]
    public void Steps_VerifyInProxy_VerificationAfterTheProxies()
    {
        var config = new JobConfig("E:\\", ["D:\\"]) { Proxy = new ProxySettings { Enabled = true } };
        var steps = JobCard.Steps(new JobStatus { Phase = JobPhase.Finishing }, config, 0.5);
        Assert.Equal(["1 · Kopírování", "2 · Proxy", "3 · Ověření", "4 · Report"], steps.Select(s => s.Text));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Active, StepState.Pending], steps.Select(s => s.State));
        config.VerifyInProxy = false;
        Assert.Equal(["1 · Kopírování", "2 · Ověření", "3 · Proxy", "4 · Report"], JobCard.Steps(new JobStatus { Phase = JobPhase.Proxies }, config, 0).Select(s => s.Text));
        Assert.Equal(("Dokončení ověření", "1,00 GB / 2,00 GB"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Finishing, VerifiedBytes = 1_000_000_000, VerifyTotal = 2_000_000_000 }));
    }

    [Fact]
    public void Notifications_EjectAtCopyEnd_DoneOnlyWhenFormatSafe()
    {
        var copied = new JobStatus { Phase = JobPhase.Proxies, SafeToEject = true };
        var eject = JobOutcomes.CopyDone("A001", copied, proxies: true)!;
        Assert.Equal(("Offload: A001 — kartu lze vyjmout", "Ověřuje se s proxy — formátovat až po dokončení."), (eject.Title, eject.Message));
        Assert.False(eject.ForPhone);

        var done = JobOutcomes.Final("A001", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true }, Report())!;
        Assert.Equal(OutcomeKind.Done, done.Kind);
        Assert.StartsWith("Ověřeno na 1 disku — kartu lze formátovat.", done.Message);
        var notYet = JobOutcomes.Final("A001", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = false }, Report())!;
        Assert.Equal(OutcomeKind.Attention, notYet.Kind);
    }
}
```

#### `windows/tests/Offload.Core.Tests/CliNtfyTests.cs`

Najít (1/1, řádky 52–58):

```csharp
        var body = Body(server);
        Assert.Equal("kanal-1", body.GetProperty("topic").GetString());
        Assert.Equal("Offload: A001 hotovo", body.GetProperty("title").GetString());
        Assert.Equal("Ověřeno na 1 disku.", body.GetProperty("message").GetString());
        Assert.Contains("ntfy: sent", r.Err);
    }

```

Nahradit:

```csharp
        var body = Body(server);
        Assert.Equal("kanal-1", body.GetProperty("topic").GetString());
        Assert.Equal("Offload: A001 hotovo", body.GetProperty("title").GetString());
        Assert.Equal("Ověřeno na 1 disku — kartu lze formátovat.", body.GetProperty("message").GetString());
        Assert.Contains("ntfy: sent", r.Err);
    }

```

#### `windows/tests/Offload.Core.Tests/DemoScenarioTests.cs`

Najít (1/1, řádky 72–78):

```csharp
    {
        var done = DemoScenario.Done(@"C:\demo\A035_ARRI", Roots, _ => [0xFF, 0xD8]);
        var h = JobCard.Headline("A035_ARRI", done.Status(), done.Report, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — kartu lze vysunout", "Všech 24 souborů je na 2 discích a ověřených (xxHash64)."), (h.Kind, h.Title, h.Sub));
        Assert.Equal(["602 MB/s", "799 MB/s", "24 / 24", "6 min 12 s"], JobCard.Summary(done.Report!, null).Select(f => f.Value));
        Assert.Equal(5, JobCard.Thumbs(done.Report!).Thumbs.Count);

```

Nahradit:

```csharp
    {
        var done = DemoScenario.Done(@"C:\demo\A035_ARRI", Roots, _ => [0xFF, 0xD8]);
        var h = JobCard.Headline("A035_ARRI", done.Status(), done.Report, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — kartu lze formátovat", "Všech 24 souborů je na 2 discích a ověřených (xxHash64)."), (h.Kind, h.Title, h.Sub));
        Assert.Equal(["602 MB/s", "799 MB/s", "24 / 24", "6 min 12 s"], JobCard.Summary(done.Report!, null).Select(f => f.Value));
        Assert.Equal(5, JobCard.Thumbs(done.Report!).Thumbs.Count);

```

#### `windows/tests/Offload.Core.Tests/HistoryTextTests.cs`

Najít (1/2, řádky 15–29):

```csharp
    }

    [Fact]
    public void Verified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno", HistoryTone.Ok),
        HistoryText.Line(Match(HistoryMatchKind.Identical)));

    [Fact]
    public void NewFiles() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno · nové soubory: 12 (3,2 GB)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.NewFiles, newFiles: 12, newBytes: 3_200_000_000)));

    [Fact]
    public void NotVerified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — NEOVĚŘENO", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Identical, verified: false)));

    [Fact]
```

Nahradit:

```csharp
    }

    [Fact]
    public void Verified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno, lze formátovat", HistoryTone.Ok),
        HistoryText.Line(Match(HistoryMatchKind.Identical)));

    [Fact]
    public void NewFiles() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno, lze formátovat · nové soubory: 12 (3,2 GB)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.NewFiles, newFiles: 12, newBytes: 3_200_000_000)));

    [Fact]
    public void NotVerified() => Assert.Equal(("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — NEOVĚŘENO (lze vyjmout, neformátovat)", HistoryTone.Warning),
        HistoryText.Line(Match(HistoryMatchKind.Identical, verified: false)));

    [Fact]
```

Najít (2/2, řádky 36–46):

```csharp
        HistoryText.Line(Match(HistoryMatchKind.Changed, changed: 4)));

    [Fact]
    public void NotCompared_IsThePlainLine() => Assert.Equal("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno",
        HistoryText.Line(Match(HistoryMatchKind.NotCompared)).Text);

    [Fact]
    public void Cli_FullPaths() => Assert.Equal(@"Zálohováno 6. 10. 2026 8:15 na D:\Zaloha, F:\Zaloha — ověřeno",
        HistoryText.Line(Match(HistoryMatchKind.Identical), fullPaths: true).Text);

    [Theory]
```

Nahradit:

```csharp
        HistoryText.Line(Match(HistoryMatchKind.Changed, changed: 4)));

    [Fact]
    public void NotCompared_IsThePlainLine() => Assert.Equal("Zálohováno 6. 10. 2026 8:15 na Zaloha (D:), Zaloha (F:) — ověřeno, lze formátovat",
        HistoryText.Line(Match(HistoryMatchKind.NotCompared)).Text);

    [Fact]
    public void Cli_FullPaths() => Assert.Equal(@"Zálohováno 6. 10. 2026 8:15 na D:\Zaloha, F:\Zaloha — ověřeno, lze formátovat",
        HistoryText.Line(Match(HistoryMatchKind.Identical), fullPaths: true).Text);

    [Theory]
```

#### `windows/tests/Offload.Core.Tests/JobCardStateTests.cs`

Najít (1/3, řádky 27–32):

```csharp
        var notSafe = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = false }.WithWarning("2 soubory z dřívější zálohy nebyly ověřeny.");
        var h = JobCard.Headline("A001", notSafe, r, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — kartu zatím nevysunujte", "2 soubory z dřívější zálohy nebyly ověřeny."), (h.Kind, h.Title, h.Sub));
        h = JobCard.Headline("A001", notSafe, r, Disks, new BackupState(0, 2));
        Assert.Equal("2 soubory dřívější zálohy nebylo ověřeno.", h.Sub);
        h = JobCard.Headline("A001", notSafe, r, Disks, new BackupState(1, 0));
```

Nahradit:

```csharp
        var notSafe = new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = false }.WithWarning("2 soubory z dřívější zálohy nebyly ověřeny.");
        var h = JobCard.Headline("A001", notSafe, r, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — kartu zatím nevysunujte", "2 soubory z dřívější zálohy nebyly ověřeny."), (h.Kind, h.Title, h.Sub));
        // R5 (plan 10): everything copied, not everything verified — the card may come out, but not be formatted.
        h = JobCard.Headline("A001", notSafe with { SafeToEject = true }, r, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — lze vyjmout, formátovat zatím ne"), (h.Kind, h.Title));
        h = JobCard.Headline("A001", notSafe, r, Disks, new BackupState(0, 2));
        Assert.Equal("2 soubory dřívější zálohy nebylo ověřeno.", h.Sub);
        h = JobCard.Headline("A001", notSafe, r, Disks, new BackupState(1, 0));
```

Najít (2/3, řádky 40–46):

```csharp
    public void Repair_Green_SaysTheWholeBackup_NotTheRepairedFiles()
    {
        var r = Report(true, OffloadReport.RepairKind, F("verified", "verified"));
        var h = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true }, r, Disks, new BackupState(0, 0));
        Assert.Equal((JobCardKind.Done, "Všechny soubory zálohy jsou teď na všech discích a ověřené."), (h.Kind, h.Sub));
        var still = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, CopySucceeded = false }, Report(true, OffloadReport.RepairKind, F("verified", "failed", "boom")), Disks, new BackupState(1, 0));
        Assert.Equal((JobCardKind.Problem, "V záloze 1 soubor stále nesouhlasí."), (still.Kind, still.Sub));
```

Nahradit:

```csharp
    public void Repair_Green_SaysTheWholeBackup_NotTheRepairedFiles()
    {
        var r = Report(true, OffloadReport.RepairKind, F("verified", "verified"));
        var h = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, CopySucceeded = true, SafeToEject = true, SafeToFormat = true }, r, Disks, new BackupState(0, 0));
        Assert.Equal((JobCardKind.Done, "Všechny soubory zálohy jsou teď na všech discích a ověřené."), (h.Kind, h.Sub));
        var still = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Done, CopySucceeded = false }, Report(true, OffloadReport.RepairKind, F("verified", "failed", "boom")), Disks, new BackupState(1, 0));
        Assert.Equal((JobCardKind.Problem, "V záloze 1 soubor stále nesouhlasí."), (still.Kind, still.Sub));
```

Najít (3/3, řádky 69–87):

```csharp
    {
        var unsafeDone = new JobStatus { Phase = JobPhase.Done, SafeToEject = false, Note = "Nic nového." }.WithWarning("3 soubory dřívější zálohy nebyly ověřeny.");
        var h = JobCard.Headline("A001", unsafeDone, null, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — kartu zatím nevysunujte", "3 soubory dřívější zálohy nebyly ověřeny."), (h.Kind, h.Title, h.Sub));
        Assert.Equal("2 soubory dřívější zálohy nebylo ověřeno.", JobCard.Headline("A001", unsafeDone, null, Disks, new BackupState(0, 2)).Sub);
        // The really complete backup stays green.
        Assert.Equal((JobCardKind.Done, "A001 — záloha je úplná"),
            (JobCard.Headline("A001", unsafeDone with { SafeToEject = true }, null, Disks).Kind, JobCard.Headline("A001", unsafeDone with { SafeToEject = true }, null, Disks).Title));
    }

    [Fact]
    public void Stage2Failed_AfterAGoodCopy_KeepsTheCopySafeWording_AndSaysTheProcessingFailed()
    {
        var r = Report(true, null, F("verified", "verified"));
        var h = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Failed, CopySucceeded = true, SafeToEject = true, Error = "ffmpeg spadl" }, r, Disks);
        Assert.Equal((JobCardKind.Done, "A001 — kartu lze vysunout"), (h.Kind, h.Title));
        Assert.Contains("Zpracování po kopii ale selhalo: ffmpeg spadl", h.Sub);
        Assert.StartsWith("Soubor je na 2 discích", h.Sub);
    }
```

Nahradit:

```csharp
    {
        var unsafeDone = new JobStatus { Phase = JobPhase.Done, SafeToEject = false, Note = "Nic nového." }.WithWarning("3 soubory dřívější zálohy nebyly ověřeny.");
        var h = JobCard.Headline("A001", unsafeDone, null, Disks);
        Assert.Equal((JobCardKind.Problem, "A001 — kartu zatím neformátujte", "3 soubory dřívější zálohy nebyly ověřeny."), (h.Kind, h.Title, h.Sub));
        Assert.Equal("2 soubory dřívější zálohy nebylo ověřeno.", JobCard.Headline("A001", unsafeDone, null, Disks, new BackupState(0, 2)).Sub);
        // The really complete backup stays green.
        var complete = unsafeDone with { SafeToEject = true, SafeToFormat = true };
        Assert.Equal((JobCardKind.Done, "A001 — záloha je úplná"), (JobCard.Headline("A001", complete, null, Disks).Kind, JobCard.Headline("A001", complete, null, Disks).Title));
    }

    [Fact]
    public void Stage2Failed_AfterAGoodCopy_KeepsTheCopySafeWording_AndSaysTheProcessingFailed()
    {
        var r = Report(true, null, F("verified", "verified"));
        var h = JobCard.Headline("A001", new JobStatus { Phase = JobPhase.Failed, CopySucceeded = true, SafeToEject = true, SafeToFormat = true, Error = "ffmpeg spadl" }, r, Disks);
        Assert.Equal((JobCardKind.Done, "A001 — kartu lze formátovat"), (h.Kind, h.Title));
        Assert.Contains("Zpracování po kopii ale selhalo: ffmpeg spadl", h.Sub);
        Assert.StartsWith("Soubor je na 2 discích", h.Sub);
    }
```

#### `windows/tests/Offload.Core.Tests/JobCardTests.cs`

Najít (1/3, řádky 66–73):

```csharp
        Assert.Empty(JobCard.Rows(new JobProgress(T0), Disks));
    }

    static JobConfig Config(bool verify = true, bool analyze = true, bool proxy = true) =>
        new(@"E:\", [@"D:\RAID_A"]) { Verify = verify, AnalyzeClips = analyze, Proxy = new ProxySettings { Enabled = proxy } };

    static string Steps(JobStatus s, JobConfig c, double progress = 0.38) =>
        string.Join(" | ", JobCard.Steps(s, c, progress).Select(x => $"{x.Text}:{x.State}:{x.Fill.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));
```

Nahradit:

```csharp
        Assert.Empty(JobCard.Rows(new JobProgress(T0), Disks));
    }

    /// Plan 10: the classic order (verification right after the copy) unless `inProxy` — the steps of a verify-in-proxy job
    /// are tested in VerifyVerdictTests.
    static JobConfig Config(bool verify = true, bool analyze = true, bool proxy = true, bool inProxy = false) =>
        new(@"E:\", [@"D:\RAID_A"]) { Verify = verify, AnalyzeClips = analyze, Proxy = new ProxySettings { Enabled = proxy }, VerifyInProxy = inProxy };

    static string Steps(JobStatus s, JobConfig c, double progress = 0.38) =>
        string.Join(" | ", JobCard.Steps(s, c, progress).Select(x => $"{x.Text}:{x.State}:{x.Fill.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));
```

Najít (2/3, řádky 189–196):

```csharp
    {
        var ok = Report();
        ok = WithFiles(ok, File("a", "verified", "verified"), File("b", "verified", "verified"));
        var h = JobCard.Headline("A035_ARRI", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, CopySucceeded = true }, ok, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — kartu lze vysunout", "Všechny 2 soubory jsou na 2 discích a ověřené (xxHash64)."), (h.Kind, h.Title, h.Sub));

        var bad = WithFiles(Report(), File("a", "verified", "verified"), File("XDROOT/Clip/B012C015.MXF", "verified", "failed", "HASH MISMATCH: 1 != 2"));
        h = JobCard.Headline("B012_FX6", new JobStatus { Phase = JobPhase.Done, CopySucceeded = false }, bad, Disks);
```

Nahradit:

```csharp
    {
        var ok = Report();
        ok = WithFiles(ok, File("a", "verified", "verified"), File("b", "verified", "verified"));
        var h = JobCard.Headline("A035_ARRI", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true, CopySucceeded = true }, ok, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — kartu lze formátovat", "Všechny 2 soubory jsou na 2 discích a ověřené (xxHash64)."), (h.Kind, h.Title, h.Sub));

        var bad = WithFiles(Report(), File("a", "verified", "verified"), File("XDROOT/Clip/B012C015.MXF", "verified", "failed", "HASH MISMATCH: 1 != 2"));
        h = JobCard.Headline("B012_FX6", new JobStatus { Phase = JobPhase.Done, CopySucceeded = false }, bad, Disks);
```

Najít (3/3, řádky 203–214):

```csharp
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "Cíl nenalezen: D:\\" }, null, Disks) is var f ? (f.Title, f.Sub) : default);
        Assert.Equal(JobCardKind.Failed, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "x" }, null, Disks).Kind);
        Assert.Equal((JobCardKind.Done, "A — záloha je úplná", Msg.NothingNew),
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Done, Note = Msg.NothingNew, SafeToEject = true }, null, Disks) is var n ? (n.Kind, n.Title, n.Sub) : default);
        Assert.Equal(JobCardKind.Cancelled, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Cancelled }, null, Disks).Kind);
        Assert.Equal(JobCardKind.Waiting, JobCard.Headline("A", new JobStatus(), null, Disks).Kind);
        Assert.Equal(JobCardKind.Running, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Proxies }, null, Disks).Kind);
        // Stage 2 failed after a good copy: still safe to eject (the error shows as an orange line).
        Assert.Equal(JobCardKind.Done, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, SafeToEject = true, Error = "x" }, ok, Disks).Kind);
    }

    static OffloadReport WithFiles(OffloadReport r, params ReportFile[] files) => new()
```

Nahradit:

```csharp
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "Cíl nenalezen: D:\\" }, null, Disks) is var f ? (f.Title, f.Sub) : default);
        Assert.Equal(JobCardKind.Failed, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "x" }, null, Disks).Kind);
        Assert.Equal((JobCardKind.Done, "A — záloha je úplná", Msg.NothingNew),
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Done, Note = Msg.NothingNew, SafeToEject = true, SafeToFormat = true }, null, Disks) is var n ? (n.Kind, n.Title, n.Sub) : default);
        Assert.Equal(JobCardKind.Cancelled, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Cancelled }, null, Disks).Kind);
        Assert.Equal(JobCardKind.Waiting, JobCard.Headline("A", new JobStatus(), null, Disks).Kind);
        Assert.Equal(JobCardKind.Running, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Proxies }, null, Disks).Kind);
        // Stage 2 failed after a good copy: still safe to format (the error shows as an orange line).
        Assert.Equal(JobCardKind.Done, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, SafeToEject = true, SafeToFormat = true, Error = "x" }, ok, Disks).Kind);
    }

    static OffloadReport WithFiles(OffloadReport r, params ReportFile[] files) => new()
```

#### `windows/tests/Offload.Core.Tests/JobProgressTests.cs`

Najít (1/1, řádky 167–174):

```csharp
        Assert.Equal("2 cíle", Texts.Destinations(2));
        Assert.Equal("1 soubor · 1.00 KB", Texts.Files(1, 1000));
        Assert.Equal("12 souborů · 2.50 GB", Texts.Files(12, 2_500_000_000));
        Assert.Equal("Všechny soubory jsou na 1 disku a ověřené — kartu lze vysunout.", Texts.SafeToEject(1));
        Assert.Equal("Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.", Texts.SafeToEject(2));
        Assert.Equal("bez varování", Texts.Warnings(0));
        Assert.Equal("2 varování", Texts.Warnings(2));
    }
```

Nahradit:

```csharp
        Assert.Equal("2 cíle", Texts.Destinations(2));
        Assert.Equal("1 soubor · 1.00 KB", Texts.Files(1, 1000));
        Assert.Equal("12 souborů · 2.50 GB", Texts.Files(12, 2_500_000_000));
        Assert.Equal("Všechny soubory jsou na 1 disku — kartu lze vyjmout. Formátovat až po ověření.", Texts.SafeToEject(1));
        Assert.Equal("Všechny soubory jsou na 2 discích a ověřené — kartu lze vyjmout i formátovat.", Texts.SafeToFormat(2));
        Assert.Equal("bez varování", Texts.Warnings(0));
        Assert.Equal("2 varování", Texts.Warnings(2));
    }
```

#### `windows/tests/Offload.Core.Tests/NotifyTests.cs`

Najít (1/2, řádky 17–32):

```csharp

    static JobStatus Status(JobPhase phase, int proxiesDone = 0, int proxiesFailed = 0, string? warning = null, string? error = null, string? note = null,
        bool safe = true) =>
        new() { Phase = phase, ProxiesDone = proxiesDone, ProxiesFailed = proxiesFailed, Warning = warning, Error = error, Note = note, SafeToEject = safe, CopySucceeded = true };

    [Fact]
    public void Done_VerifiedOnDisks_WithProxies()
    {
        var o = JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesDone: 24), Report())!;
        Assert.Equal((OutcomeKind.Done, "Offload: A001 hotovo", "Ověřeno na 1 disku. Proxy: 24 hotovo."), (o.Kind, o.Title, o.Message));
        Assert.Equal(o.Message, o.PhoneMessage);
        Assert.Equal((3, "white_check_mark"), (o.Priority, o.Tags.Single()));
        Assert.Equal("Ověřeno na 1 disku.", JobOutcomes.Final("A001", Status(JobPhase.Done), Report())!.Message);
    }

    [Fact]
```

Nahradit:

```csharp

    static JobStatus Status(JobPhase phase, int proxiesDone = 0, int proxiesFailed = 0, string? warning = null, string? error = null, string? note = null,
        bool safe = true) =>
        new() { Phase = phase, ProxiesDone = proxiesDone, ProxiesFailed = proxiesFailed, Warning = warning, Error = error, Note = note, SafeToEject = safe, SafeToFormat = safe, CopySucceeded = true };

    [Fact]
    public void Done_VerifiedOnDisks_WithProxies()
    {
        var o = JobOutcomes.Final("A001", Status(JobPhase.Done, proxiesDone: 24), Report())!;
        Assert.Equal((OutcomeKind.Done, "Offload: A001 hotovo", "Ověřeno na 1 disku — kartu lze formátovat. Proxy: 24 hotovo."), (o.Kind, o.Title, o.Message));
        Assert.Equal(o.Message, o.PhoneMessage);
        Assert.Equal((3, "white_check_mark"), (o.Priority, o.Tags.Single()));
        Assert.Equal("Ověřeno na 1 disku — kartu lze formátovat.", JobOutcomes.Final("A001", Status(JobPhase.Done), Report())!.Message);
    }

    [Fact]
```

Najít (2/2, řádky 70–76):

```csharp
    public void Ejectable_OnlyWhileStageTwoRuns_WindowsOnly()
    {
        var e = JobOutcomes.CopyDone("A001", Status(JobPhase.Proxies), proxies: true)!;
        Assert.Equal((OutcomeKind.Ejectable, "Offload: A001 — kartu lze vysunout", "Proxy se ještě renderují."), (e.Kind, e.Title, e.Message));
        Assert.False(e.ForPhone);
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Done), proxies: true));
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Analyzing, safe: false), proxies: false));
```

Nahradit:

```csharp
    public void Ejectable_OnlyWhileStageTwoRuns_WindowsOnly()
    {
        var e = JobOutcomes.CopyDone("A001", Status(JobPhase.Proxies), proxies: true)!;
        Assert.Equal((OutcomeKind.Ejectable, "Offload: A001 — kartu lze vyjmout", "Proxy se ještě renderují."), (e.Kind, e.Title, e.Message));
        Assert.False(e.ForPhone);
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Done), proxies: true));
        Assert.Null(JobOutcomes.CopyDone("A001", Status(JobPhase.Analyzing, safe: false), proxies: false));
```

#### `windows/tests/Offload.Core.Tests/SetupTextsTests.cs`

Najít (1/3, řádky 89–96):

```csharp
        var today = HistoryView.Today(entries, now)!;

        Assert.Equal("3 karty · 412 GB · vše ověřeno", today.Summary);
        Assert.Equal([("A034_ARRI", "256 GB", "18:12", "✓ 2 kopie ověřeny"), ("B011_FX6", "118 GB", "16:47", "✓ 2 kopie ověřeny"),
                      ("SND_DAY03", "38,0 GB", "14:05", "✓ 2 kopie ověřeny")],
            today.Rows.Select(r => (r.CardName, r.Size, r.Time, r.Status)));
        Assert.Equal(["Dnes", "Včera"], HistoryView.Days(entries, now).Select(d => d.Title));
        Assert.Null(HistoryView.Today([entries[3]], now));
```

Nahradit:

```csharp
        var today = HistoryView.Today(entries, now)!;

        Assert.Equal("3 karty · 412 GB · vše ověřeno", today.Summary);
        Assert.Equal([("A034_ARRI", "256 GB", "18:12", "✓ 2 kopie ověřeny · lze formátovat"), ("B011_FX6", "118 GB", "16:47", "✓ 2 kopie ověřeny · lze formátovat"),
                      ("SND_DAY03", "38,0 GB", "14:05", "✓ 2 kopie ověřeny · lze formátovat")],
            today.Rows.Select(r => (r.CardName, r.Size, r.Time, r.Status)));
        Assert.Equal(["Dnes", "Včera"], HistoryView.Days(entries, now).Select(d => d.Title));
        Assert.Null(HistoryView.Today([entries[3]], now));
```

Najít (2/3, řádky 109–115):

```csharp
        var days = HistoryView.Days(entries, now);
        Assert.Equal(["Dnes", "4. 10. 2026"], days.Select(d => d.Title));
        Assert.Equal("2 karty · 18,0 GB · 1 karta s chybami · 1 neověřená", days[0].Summary);
        Assert.Equal([("✕ s chybami (3 soubory)", HistoryTone.Error), ("! neověřeno", HistoryTone.Warning)],
            days[0].Rows.Select(r => (r.Status, r.Tone)));
    }
    // The history entry already holds the backup's state as the reports say it (BackupState / CardRecord, ruling R2): the row
```

Nahradit:

```csharp
        var days = HistoryView.Days(entries, now);
        Assert.Equal(["Dnes", "4. 10. 2026"], days.Select(d => d.Title));
        Assert.Equal("2 karty · 18,0 GB · 1 karta s chybami · 1 neověřená", days[0].Summary);
        Assert.Equal([("✕ s chybami (3 soubory)", HistoryTone.Error), ("! neověřeno · neformátovat", HistoryTone.Warning)],
            days[0].Rows.Select(r => (r.Status, r.Tone)));
    }
    // The history entry already holds the backup's state as the reports say it (BackupState / CardRecord, ruling R2): the row
```

Najít (3/3, řádky 120–128):

```csharp
        var e = Entry("R", new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Local), 5_000_000_000);
        e.Generations = [new HistoryGeneration { Seq = 1, Kind = HistoryGeneration.Full, Verified = false, Finished = e.Finished },
                         new HistoryGeneration { Seq = 2, Kind = HistoryGeneration.Repair, Verified = true, Finished = e.Finished }];
        Assert.Equal(("✓ 2 kopie ověřeny", HistoryTone.Ok), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
        e.Verified = false;
        Assert.Equal(("! neověřeno", HistoryTone.Warning), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
        Assert.Equal(("5,00 GB", "R"), (HistoryView.Row(e).Size, HistoryView.Row(e).CardName));
    }
}
```

Nahradit:

```csharp
        var e = Entry("R", new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Local), 5_000_000_000);
        e.Generations = [new HistoryGeneration { Seq = 1, Kind = HistoryGeneration.Full, Verified = false, Finished = e.Finished },
                         new HistoryGeneration { Seq = 2, Kind = HistoryGeneration.Repair, Verified = true, Finished = e.Finished }];
        Assert.Equal(("✓ 2 kopie ověřeny · lze formátovat", HistoryTone.Ok), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
        e.Verified = false;
        Assert.Equal(("! neověřeno · neformátovat", HistoryTone.Warning), (HistoryView.Row(e).Status, HistoryView.Row(e).Tone));
        Assert.Equal(("5,00 GB", "R"), (HistoryView.Row(e).Size, HistoryView.Row(e).CardName));
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifyVerdictTests|FullyQualifiedName~JobCard|FullyQualifiedName~JobOutcomeTests|FullyQualifiedName~CliNtfyTests|FullyQualifiedName~DemoScenarioTests|FullyQualifiedName~HistoryTextTests|FullyQualifiedName~JobProgressTests|FullyQualifiedName~SetupTextsTests|FullyQualifiedName~ThemeTokensTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS0246: The type or namespace name 'ChipTone' could not be found (are you missing a using directive or an assembly reference?)`; `CS0103: The name 'ChipTone' does not exist in the current context`.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.App/JobItemViewModel.Verify.cs` (nový soubor)

```csharp
using System.IO;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.VerifyInProxy;

namespace Offload.App;

/// One content warning of a finished card (spec 7, 9): "A001C003 · 01:00:02:13 · poškozená data" with Náhled.
public sealed class ContentRowViewModel(string clip, string text, byte[]? jpeg, Action<byte[]> open)
{
    public string Clip => clip;
    public string Text => text;
    public bool HasPicture => jpeg != null;
    public RelayCommand PreviewCommand { get; } = new(() => { if (jpeg != null) open(jpeg); }, () => jpeg != null);
}

/// Plan 10 on the job card (R5, spec 7, spec 9): the two verdicts "Lze vyjmout" and "Lze formátovat" side by side, never
/// merged, and the content warnings with their place and a picture — formatting waits for the user's confirmation.
public sealed partial class JobItemViewModel
{
    bool contentConfirmed;
    IReadOnlyList<ContentRowViewModel>? contentRows;

    public RelayCommand ConfirmContentCommand { get; private set; } = null!;

    void InitVerify() => ConfirmContentCommand = new RelayCommand(() =>
    {
        contentConfirmed = true;
        Refresh();
    }, () => NeedsContentConfirm);

    public bool SafeToFormat => progress.Status.SafeToFormat;
    /// The chips show once the job has a plan (not while it waits).
    public bool ShowChips => Phase != JobPhase.Waiting && Job.Plan != null;
    public CardChip EjectChip => JobCard.Chips(progress.Status, contentConfirmed).Eject;
    public CardChip FormatChip => JobCard.Chips(progress.Status, contentConfirmed).Format;
    /// "Rozumím, kartu lze formátovat" is offered while content warnings wait for a look.
    public bool NeedsContentConfirm => IsFinished && progress.Status.ContentWarnings > 0 && !contentConfirmed;

    /// The content warnings of the finished job (read once it has ended; nothing while it runs).
    public IReadOnlyList<ContentRowViewModel> ContentRows
    {
        get
        {
            if (contentRows != null || !IsFinished || Job.Report is not { } r) return contentRows ?? [];
            return contentRows = r.ContentWarnings.Select(c => new ContentRowViewModel(c.Clip.Name,
                $"{c.Content!.Timecode} · {ContentResult.KindText(c.Content.Kind)}", c.Content.Jpeg, OpenDefect)).ToList();
        }
    }

    /// The picture of the defect, written to a temporary file off the UI thread and opened in the system viewer.
    async void OpenDefect(byte[] jpeg)
    {
        try
        {
            var path = await Task.Run(() =>
            {
                var p = Path.Combine(Path.GetTempPath(), $"offload-vada-{Guid.NewGuid():N}.jpg");
                File.WriteAllBytes(p, jpeg);
                return p;
            });
            if (Shell.Open(path) is string error) owner.ShowMessage(error);
        }
        catch (Exception e)
        {
            owner.ShowMessage($"Náhled nejde otevřít: {e.Message}");
        }
    }
}
```

#### `windows/src/Offload.Core/Desktop/JobCard.cs`

Najít (1/3, řádky 32–37):

```csharp

/// What the card header says under the name: the files or clips, the size and the camera.
public sealed record JobCardFacts(int Files, int Clips, long Bytes, string? Camera, bool Append);

/// The texts and numbers of the new job card (UI návrh, nové věci 1–4) — pure functions, the window only binds them.
public static class JobCard
```

Nahradit:

```csharp

/// What the card header says under the name: the files or clips, the size and the camera.
public sealed record JobCardFacts(int Files, int Clips, long Bytes, string? Camera, bool Append);

public enum ChipTone { Neutral, Ok, Warning, Error }

/// One of the card row's two verdicts (R5, spec 9): "Lze vyjmout" or "Lze formátovat" and how it looks.
public sealed record CardChip(string Text, ChipTone Tone);

/// The texts and numbers of the new job card (UI návrh, nové věci 1–4) — pure functions, the window only binds them.
public static class JobCard
```

Najít (2/3, řádky 97–119):

```csharp
    static double Share(long done, long of) => of <= 0 ? 0 : Math.Clamp((double)done / of, 0, 1);

    /// The phase indicator: Kopírování · Ověření · Proxy (or Analýza) · Report — the phases this job does, numbered in a
    /// row; done ones full, the active one as far as it got. Empty for a finished job that did not end Done.
    public static IReadOnlyList<PhaseStep> Steps(JobStatus s, JobConfig config, double phaseProgress)
    {
        var labels = new List<string> { "Kopírování" };
        if (config.Verify) labels.Add("Ověření");
        bool stage2 = config.RepairInto == null && (config.Proxy.Enabled || config.AnalyzeClips);
        if (stage2) labels.Add(config.Proxy.Enabled ? "Proxy" : "Analýza");
        labels.Add("Report");
        if (s.Phase is JobPhase.Failed or JobPhase.Cancelled) return [];
        int active = s.Phase switch
        {
            JobPhase.Waiting => -1,
            JobPhase.Copying or JobPhase.Flushing => 0,
            JobPhase.Verifying => 1,
            // The first report is written at the end of stage 1 (before the clips): the copy's last step, full.
            JobPhase.Reporting when s.CopySucceeded == null => config.Verify ? 1 : 0,
            JobPhase.Analyzing or JobPhase.Proxies => stage2 ? labels.Count - 2 : labels.Count - 1,
            JobPhase.Reporting => labels.Count - 1,
            _ => labels.Count,   // Done
        };
```

Nahradit:

```csharp
    static double Share(long done, long of) => of <= 0 ? 0 : Math.Clamp((double)done / of, 0, 1);

    /// The phase indicator: Kopírování · Ověření · Proxy (or Analýza) · Report — the phases this job does, numbered in a
    /// row; done ones full, the active one as far as it got. Empty for a finished job that did not end Done. Plan 10: with
    /// verify-in-proxy the verification follows the proxies — Kopírování · Proxy · Ověření · Report.
    public static IReadOnlyList<PhaseStep> Steps(JobStatus s, JobConfig config, double phaseProgress)
    {
        bool stage2 = config.RepairInto == null && (config.Proxy.Enabled || config.AnalyzeClips);
        bool inProxy = config.Verify && config.VerifyInProxy && config.Proxy.Enabled && config.RepairInto == null;
        var labels = new List<string> { "Kopírování" };
        if (config.Verify && !inProxy) labels.Add("Ověření");
        if (stage2) labels.Add(config.Proxy.Enabled ? "Proxy" : "Analýza");
        if (inProxy) labels.Add("Ověření");
        labels.Add("Report");
        if (s.Phase is JobPhase.Failed or JobPhase.Cancelled) return [];
        int verify = labels.IndexOf("Ověření");
        int active = s.Phase switch
        {
            JobPhase.Waiting => -1,
            JobPhase.Copying or JobPhase.Flushing => 0,
            JobPhase.Verifying => 1,
            // The first report is written at the end of stage 1 (before the clips): the copy's last step, full.
            JobPhase.Reporting when s.CopySucceeded == null => config.Verify && !inProxy ? 1 : 0,
            JobPhase.Analyzing or JobPhase.Proxies => stage2 ? (inProxy ? verify - 1 : labels.Count - 2) : labels.Count - 1,
            JobPhase.Finishing => verify,
            JobPhase.Reporting => labels.Count - 1,
            _ => labels.Count,   // Done
        };
```

Najít (3/3, řádky 214–257):

```csharp
    public static string? MoreFiles(int total, int shown) =>
        total > shown ? $"… a {Texts.Plural(total - shown, "další", "další", "dalších")} {total - shown} {Texts.Plural(total - shown, "soubor", "soubory", "souborů")} v reportu" : null;

    /// The headline of a finished job (and of a waiting / running one: just its kind). The green "kartu lze vysunout" is
    /// the job's own verdict (`JobStatus.SafeToEject`, shared with the eject notification and history), never recomputed
    /// from this job's report: a repair's report lists only the repaired files. `backup`: the whole backup as
    /// BackupState read it (repair, append) for the words under a card that must not be ejected yet; `blockedByPaused`:
    /// a waiting job stands behind a paused one.
    public static CardHeadline Headline(string card, JobStatus s, OffloadReport? r, IReadOnlyList<string> destNames,
        BackupState? backup = null, bool blockedByPaused = false)
    {
        if (s.Phase == JobPhase.Waiting)
            return new(JobCardKind.Waiting, card, blockedByPaused ? WaitingForPaused : "Čeká ve frontě");
        if (!s.Phase.IsFinished()) return new(JobCardKind.Running, card, "");
        if (s.Phase == JobPhase.Cancelled)
            return new(JobCardKind.Cancelled, $"{card} — zrušeno", s.SafeToEject
                ? "Kopie je hotová a ověřená, zrušilo se jen zpracování po kopii."
                : "Karta není kompletně zálohovaná — zkopírujte ji znovu celou.");
        if (r == null)
        {
            // Nothing new / nothing to repair: green only when the backup is really complete and verified.
            if (s.Phase == JobPhase.Done && !s.SafeToEject)
                return new(JobCardKind.Problem, $"{card} — kartu zatím nevysunujte", NotSafeWhy(s, backup));
            return s.Phase == JobPhase.Done
                ? new(JobCardKind.Done, $"{card} — záloha je úplná", s.Note ?? Msg.NothingNew)
                : new(JobCardKind.Failed, $"{card} — offload selhal", s.Error ?? "Kopie se nepodařila.");
        }
        string notYet = $"{card} — kartu zatím nevysunujte";
        if (!r.Success) return new(JobCardKind.Problem, notYet, backup is { Failed: > 0 } b ? $"V záloze {Files(b.Failed)} stále nesouhlasí." : ProblemText(r, destNames));
        if (!r.Verified) return new(JobCardKind.Problem, $"{card} — zkopírováno bez ověření", SetupTexts.VerifyOffWarning);
        if (!s.SafeToEject)
        {
            // This job's files are good, but the backup as a whole is not (earlier files never read back, files of the
            // card that are in no report, files that still fail): the card stays in.
            return new(JobCardKind.Problem, notYet, NotSafeWhy(s, backup));
        }
        var safe = r.IsRepair ? "Všechny soubory zálohy jsou teď na všech discích a ověřené." : SafeText(r.TotalFiles, r.Destinations.Count, HashKinds.Label(HashKinds.FromNames(r.Algorithms)));
        // Stage 2 (previews, proxies) failed after a good copy: the copy is safe, but say the processing did not finish.
        if (s.Phase == JobPhase.Failed)
            safe = $"{safe} Zpracování po kopii ale selhalo: {s.Error ?? "neznámá chyba"}";
        return new(JobCardKind.Done, $"{card} — kartu lze vysunout", safe);
    }

    static string NotSafeWhy(JobStatus s, BackupState? backup) => backup switch
```

Nahradit:

```csharp
    public static string? MoreFiles(int total, int shown) =>
        total > shown ? $"… a {Texts.Plural(total - shown, "další", "další", "dalších")} {total - shown} {Texts.Plural(total - shown, "soubor", "soubory", "souborů")} v reportu" : null;

    /// The headline of a finished job (and of a waiting / running one: just its kind). The green "kartu lze formátovat"
    /// is the job's own verdict (`JobStatus.SafeToFormat`, R5 — shared with the notification and history), never
    /// recomputed from this job's report: a repair's report lists only the repaired files. "Lze vyjmout" (SafeToEject)
    /// alone is never green. `backup`: the whole backup as BackupState read it (repair, append) for the words under a
    /// card that must not be formatted yet; `blockedByPaused`: a waiting job stands behind a paused one;
    /// `contentConfirmed`: the user confirmed the content warnings (spec 7) — until then the card is not green.
    public static CardHeadline Headline(string card, JobStatus s, OffloadReport? r, IReadOnlyList<string> destNames,
        BackupState? backup = null, bool blockedByPaused = false, bool contentConfirmed = false)
    {
        if (s.Phase == JobPhase.Waiting)
            return new(JobCardKind.Waiting, card, blockedByPaused ? WaitingForPaused : "Čeká ve frontě");
        if (!s.Phase.IsFinished()) return new(JobCardKind.Running, card, "");
        if (s.Phase == JobPhase.Cancelled)
            return new(JobCardKind.Cancelled, $"{card} — zrušeno", s.SafeToFormat
                ? "Kopie je hotová a ověřená, zrušilo se jen zpracování po kopii."
                : s.SafeToEject
                ? "Kopie je hotová, ale neověřená — kartu lze vyjmout, neformátujte ji (Zkopírovat znovu ji ověří)."
                : "Karta není kompletně zálohovaná — zkopírujte ji znovu celou.");
        if (r == null)
        {
            // Nothing new / nothing to repair: green only when the backup is really complete and verified.
            if (s.Phase == JobPhase.Done && !s.SafeToFormat)
                return new(JobCardKind.Problem, $"{card} — kartu zatím neformátujte", NotSafeWhy(s, backup));
            return s.Phase == JobPhase.Done
                ? new(JobCardKind.Done, $"{card} — záloha je úplná", s.Note ?? Msg.NothingNew)
                : new(JobCardKind.Failed, $"{card} — offload selhal", s.Error ?? "Kopie se nepodařila.");
        }
        string notYet = s.SafeToEject ? $"{card} — lze vyjmout, formátovat zatím ne" : $"{card} — kartu zatím nevysunujte";
        if (!r.Success) return new(JobCardKind.Problem, notYet, backup is { Failed: > 0 } b ? $"V záloze {Files(b.Failed)} stále nesouhlasí." : ProblemText(r, destNames));
        if (!r.Verified) return new(JobCardKind.Problem, $"{card} — zkopírováno bez ověření", SetupTexts.VerifyOffWarning);
        if (!s.SafeToFormat)
        {
            // This job's files are good, but the backup as a whole is not (earlier files never read back, files of the
            // card that are in no report, files that still fail): not formatted yet.
            return new(JobCardKind.Problem, notYet, NotSafeWhy(s, backup));
        }
        // Spec 7: the copy is exact, but the decoder found defects — formatting only once the user has looked.
        if (s.ContentWarnings > 0 && !contentConfirmed)
            return new(JobCardKind.Problem, $"{card} — před formátováním zkontrolujte vady obsahu", Msg.ContentWarnings(s.ContentWarnings));
        var safe = r.IsRepair ? "Všechny soubory zálohy jsou teď na všech discích a ověřené." : SafeText(r.TotalFiles, r.Destinations.Count, HashKinds.Label(HashKinds.FromNames(r.Algorithms)));
        // Stage 2 (previews, proxies) failed after a good copy: the copy is safe, but say the processing did not finish.
        if (s.Phase == JobPhase.Failed)
            safe = $"{safe} Zpracování po kopii ale selhalo: {s.Error ?? "neznámá chyba"}";
        return new(JobCardKind.Done, $"{card} — kartu lze formátovat", safe);
    }

    /// R5, spec 9: the card row's two verdicts, never merged — "Lze vyjmout" once everything is copied, "Lze formátovat"
    /// once everything is verified (yellow until content warnings are confirmed). Hidden while the job waits.
    public static (CardChip Eject, CardChip Format) Chips(JobStatus s, bool contentConfirmed = false)
    {
        bool running = s.Phase != JobPhase.Waiting && !s.Phase.IsFinished();
        var eject = s.SafeToEject ? new CardChip("Lze vyjmout", ChipTone.Ok)
            : running ? new CardChip("Vyjmout zatím ne", ChipTone.Neutral) : new CardChip("Nevyjímat", ChipTone.Error);
        var format = s.SafeToFormat
            ? s.ContentWarnings > 0 && !contentConfirmed ? new CardChip("Lze formátovat po kontrole vad", ChipTone.Warning) : new CardChip("Lze formátovat", ChipTone.Ok)
            : running ? new CardChip(s.SafeToEject ? "Ověřuje se…" : "Formátovat zatím ne", ChipTone.Neutral)
            : new CardChip("Neformátovat", s.SafeToEject ? ChipTone.Warning : ChipTone.Error);
        return (eject, format);
    }

    static string NotSafeWhy(JobStatus s, BackupState? backup) => backup switch
```

#### `windows/src/Offload.Core/Desktop/Texts.cs`

Najít (1/1, řádky 13–20):

```csharp

    public static string Clips(int n) => $"{n} {Plural(n, "klip", "klipy", "klipů")}";

    public static string SafeToEject(int disks) =>
        $"Všechny soubory jsou na {disks} {Plural(disks, "disku", "discích", "discích")} a ověřené — kartu lze vysunout.";

    public static string Warnings(int n) => n == 0 ? "bez varování" : $"{n} varování";

```

Nahradit:

```csharp

    public static string Clips(int n) => $"{n} {Plural(n, "klip", "klipy", "klipů")}";

    /// R5 (plan 10) "Lze vyjmout": copied everywhere, the verification may still run.
    public static string SafeToEject(int disks) =>
        $"Všechny soubory jsou na {disks} {Plural(disks, "disku", "discích", "discích")} — kartu lze vyjmout. Formátovat až po ověření.";

    /// R5 (plan 10) "Lze formátovat": verified everywhere.
    public static string SafeToFormat(int disks) =>
        $"Všechny soubory jsou na {disks} {Plural(disks, "disku", "discích", "discích")} a ověřené — kartu lze vyjmout i formátovat.";

    public static string Warnings(int n) => n == 0 ? "bez varování" : $"{n} varování";

```

#### `windows/src/Offload.Core/History/HistoryText.cs`

Najít (1/1, řádky 44–51):

```csharp
        if (m.Kind == HistoryMatchKind.Changed)
            return ($"{head}, ale karta se od té doby změnila ({Files(m.Changed.Count)} jinak) — doplnit nejde, jen celá kopie", HistoryTone.Warning);
        var (state, tone) = !e.Success ? ($" — s chybami ({Files(e.FailedFiles ?? 0)})", HistoryTone.Error)
            : !e.Verified ? (" — NEOVĚŘENO", HistoryTone.Warning)
            : (" — ověřeno", HistoryTone.Ok);
        var text = head + state;
        if (m.Kind == HistoryMatchKind.NewFiles)
        {
```

Nahradit:

```csharp
        if (m.Kind == HistoryMatchKind.Changed)
            return ($"{head}, ale karta se od té doby změnila ({Files(m.Changed.Count)} jinak) — doplnit nejde, jen celá kopie", HistoryTone.Warning);
        var (state, tone) = !e.Success ? ($" — s chybami ({Files(e.FailedFiles ?? 0)})", HistoryTone.Error)
            : !e.Verified ? (" — NEOVĚŘENO (lze vyjmout, neformátovat)", HistoryTone.Warning)
            : (" — ověřeno, lze formátovat", HistoryTone.Ok);
        var text = head + state;
        if (m.Kind == HistoryMatchKind.NewFiles)
        {
```

#### `windows/src/Offload.Core/Desktop/HistoryView.cs`

Najít (1/1, řádky 23–30):

```csharp
        var (status, tone) = e.Unfinished ? ("! nedokončeno", HistoryTone.Warning)
            : !e.Success
            ? ($"✕ s chybami ({e.FailedFiles ?? 0} {Texts.Plural(e.FailedFiles ?? 0, "soubor", "soubory", "souborů")})", HistoryTone.Error)
            : !e.Verified ? ("! neověřeno", HistoryTone.Warning)
            : ($"✓ {copies} {Texts.Plural(copies, "kopie ověřena", "kopie ověřeny", "kopií ověřeno")}", HistoryTone.Ok);
        return new HistoryRow(e.Id, e.CardName, Format.BytesCs3(e.Bytes), e.Finished.ToLocalTime().ToString("H:mm", Cs), status, tone, e.Project);
    }

```

Nahradit:

```csharp
        var (status, tone) = e.Unfinished ? ("! nedokončeno", HistoryTone.Warning)
            : !e.Success
            ? ($"✕ s chybami ({e.FailedFiles ?? 0} {Texts.Plural(e.FailedFiles ?? 0, "soubor", "soubory", "souborů")})", HistoryTone.Error)
            : !e.Verified ? ("! neověřeno · neformátovat", HistoryTone.Warning)
            : ($"✓ {copies} {Texts.Plural(copies, "kopie ověřena", "kopie ověřeny", "kopií ověřeno")} · lze formátovat", HistoryTone.Ok);
        return new HistoryRow(e.Id, e.CardName, Format.BytesCs3(e.Bytes), e.Finished.ToLocalTime().ToString("H:mm", Cs), status, tone, e.Project);
    }

```

#### `windows/src/Offload.Core/Notify/JobOutcome.cs`

Najít (1/1, řádky 56–71):

```csharp
            return Attention(card, "Kopie je ověřená, ale zpracování po kopii selhalo.", "Kopie je ověřená, ale zpracování po kopii selhalo.");
        if (status.Warning != null)
            return Attention(card, $"Kopie je ověřená, ale: {FirstLine(status.Warning)}", "Kopie je ověřená, ale vyžaduje pozornost — podrobnosti v aplikaci.");
        int disks = report.Destinations.Count;
        var done = $"Ověřeno na {disks} {Texts.Plural(disks, "disku", "discích", "discích")}." + (status.ProxiesDone > 0 ? $" Proxy: {status.ProxiesDone} hotovo." : "");
        return new JobOutcome(OutcomeKind.Done, $"Offload: {card} hotovo", done, done);
    }

    /// At the end of stage 1 when the card is safe to eject and stage 2 still runs (Windows only); else null.
    public static JobOutcome? CopyDone(string card, JobStatus status, bool proxies) =>
        status.SafeToEject && !status.Phase.IsFinished()
            ? new JobOutcome(OutcomeKind.Ejectable, $"Offload: {card} — kartu lze vysunout",
                proxies ? "Proxy se ještě renderují." : "Klipy se ještě zpracovávají.", "")
            : null;

    static JobOutcome Attention(string card, string message, string phone) =>
```

Nahradit:

```csharp
            return Attention(card, "Kopie je ověřená, ale zpracování po kopii selhalo.", "Kopie je ověřená, ale zpracování po kopii selhalo.");
        if (status.Warning != null)
            return Attention(card, $"Kopie je ověřená, ale: {FirstLine(status.Warning)}", "Kopie je ověřená, ale vyžaduje pozornost — podrobnosti v aplikaci.");
        // R5: "hotovo" only when every copy is verified (the whole backup for an append or a repair).
        if (!status.SafeToFormat)
            return Attention(card, "Kopie je hotová, ale kartu zatím neformátujte — není celá ověřená.", "Kopie je hotová, ale není celá ověřená.");
        int disks = report.Destinations.Count;
        var done = $"Ověřeno na {disks} {Texts.Plural(disks, "disku", "discích", "discích")} — kartu lze formátovat." + (status.ProxiesDone > 0 ? $" Proxy: {status.ProxiesDone} hotovo." : "");
        return new JobOutcome(OutcomeKind.Done, $"Offload: {card} hotovo", done, done);
    }

    /// At the end of stage 1 when everything is copied (R5 "Lze vyjmout") and stage 2 still runs (Windows only); else
    /// null. With verify-in-proxy (plan 10) the copies are verified only with the proxies: formatting waits.
    public static JobOutcome? CopyDone(string card, JobStatus status, bool proxies) =>
        status.SafeToEject && !status.Phase.IsFinished()
            ? new JobOutcome(OutcomeKind.Ejectable, $"Offload: {card} — kartu lze vyjmout",
                status.SafeToFormat ? (proxies ? "Proxy se ještě renderují." : "Klipy se ještě zpracovávají.")
                    : "Ověřuje se s proxy — formátovat až po dokončení.", "")
            : null;

    static JobOutcome Attention(string card, string message, string phone) =>
```

#### `windows/src/Offload.Core/Desktop/DemoScenario.cs`

Najít (1/1, řádky 144–150):

```csharp
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, Plan(source, "A035_ARRI", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Done, SafeToEject = true, CopySucceeded = true, ProxiesDone = 24,
        }, report);
    }

```

Nahradit:

```csharp
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, Plan(source, "A035_ARRI", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true, CopySucceeded = true, ProxiesDone = 24,
        }, report);
    }

```

#### `windows/src/Offload.App/JobItemViewModel.cs`

Najít (1/3, řádky 45–50):

```csharp
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
        InitCard();
    }

    public OffloadJob Job { get; }
```

Nahradit:

```csharp
        OpenReportCommand = new RelayCommand(OpenReport);
        RevealCommand = new RelayCommand(Reveal);
        InitCard();
        InitVerify();
    }

    public OffloadJob Job { get; }
```

Najít (2/3, řádky 74–80):

```csharp
            ResolveChain(plan);
        }
        // The whole backup's state comes from the job (read on its thread); a waiting card says when a paused job holds it.
        headline = JobCard.Headline(CardName, progress.Status, Job.Report, DestNames, Job.Backup, owner.BlockedByPaused(this));
        RefreshCard();
        Raise(string.Empty);   // every binding of the card may have changed
    }
```

Nahradit:

```csharp
            ResolveChain(plan);
        }
        // The whole backup's state comes from the job (read on its thread); a waiting card says when a paused job holds it.
        headline = JobCard.Headline(CardName, progress.Status, Job.Report, DestNames, Job.Backup, owner.BlockedByPaused(this), contentConfirmed);
        RefreshCard();
        Raise(string.Empty);   // every binding of the card may have changed
    }
```

Najít (3/3, řádky 206–212):

```csharp
    internal void VolumesChanged() => RaiseMany(nameof(ShowEjectButton), nameof(CanRepair));
    /// A repair of this backup was queued or finished: the "Zkopírovat znovu" button may have to go (a finished card is not refreshed).
    internal void RepairChanged() => Raise(nameof(CanRepair));
    public string SafeText => Texts.SafeToEject(Job.Plan?.CardPaths.Count ?? 0);
    // Cancelled (unlike the Mac): no red "N souborů selhalo" and no rate of a copy that was cut short — one orange line.
    bool IsCancelled => Phase == JobPhase.Cancelled;
    public string? ErrorText => progress.Status.Error;
```

Nahradit:

```csharp
    internal void VolumesChanged() => RaiseMany(nameof(ShowEjectButton), nameof(CanRepair));
    /// A repair of this backup was queued or finished: the "Zkopírovat znovu" button may have to go (a finished card is not refreshed).
    internal void RepairChanged() => Raise(nameof(CanRepair));
    /// R5: "lze vyjmout" while the verification still runs, "lze vyjmout i formátovat" once it is done.
    public string SafeText => SafeToFormat ? Texts.SafeToFormat(Job.Plan?.CardPaths.Count ?? 0) : Texts.SafeToEject(Job.Plan?.CardPaths.Count ?? 0);
    // Cancelled (unlike the Mac): no red "N souborů selhalo" and no rate of a copy that was cut short — one orange line.
    bool IsCancelled => Phase == JobPhase.Cancelled;
    public string? ErrorText => progress.Status.Error;
```

#### `windows/src/Offload.App/QueuePanel.xaml`

Najít (1/5, řádky 26–31):

```xml
                </DataTrigger>
            </Style.Triggers>
        </Style>

        <!-- ── Waiting: one compact row ── -->
        <DataTemplate x:Key="WaitingCard">
```

Nahradit:

```xml
                </DataTrigger>
            </Style.Triggers>
        </Style>

        <!-- Plan 10 (R5, spec 9): the two verdicts of the card, never merged — "Lze vyjmout" and "Lze formátovat". -->
        <Style x:Key="VerdictChip" TargetType="Border">
            <Setter Property="CornerRadius" Value="10" />
            <Setter Property="Padding" Value="10,3" />
            <Setter Property="Margin" Value="0,0,8,0" />
            <Setter Property="Background" Value="{DynamicResource Track}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Tone}" Value="Ok">
                    <Setter Property="Background" Value="{DynamicResource SuccessFill}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Tone}" Value="Warning">
                    <Setter Property="Background" Value="{DynamicResource WarnFill}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Tone}" Value="Error">
                    <Setter Property="Background" Value="{DynamicResource Danger}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="VerdictChipText" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Foreground" Value="{DynamicResource Text2}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Tone}" Value="Ok">
                    <Setter Property="Foreground" Value="{DynamicResource OnSuccess}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Tone}" Value="Warning">
                    <Setter Property="Foreground" Value="{DynamicResource OnWarn}" />
                </DataTrigger>
                <DataTrigger Binding="{Binding Tone}" Value="Error">
                    <Setter Property="Foreground" Value="{DynamicResource OnAccent}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <DataTemplate x:Key="VerdictChips">
            <StackPanel Orientation="Horizontal" Margin="0,12,0,0" Visibility="{Binding ShowChips, Converter={StaticResource BoolToVisible}}"
                        AutomationProperties.AutomationId="VerdictChips">
                <Border Style="{StaticResource VerdictChip}" DataContext="{Binding EjectChip}">
                    <TextBlock Text="{Binding Text}" Style="{StaticResource VerdictChipText}" AutomationProperties.AutomationId="EjectChip" />
                </Border>
                <Border Style="{StaticResource VerdictChip}" DataContext="{Binding FormatChip}">
                    <TextBlock Text="{Binding Text}" Style="{StaticResource VerdictChipText}" AutomationProperties.AutomationId="FormatChip" />
                </Border>
            </StackPanel>
        </DataTemplate>
        <!-- Spec 7, 9: a content defect of a clip (the copy matches the card) — its place, a picture, and the confirmation. -->
        <DataTemplate x:Key="ContentWarnings">
            <Border CornerRadius="10" Background="{DynamicResource WarnListBg}" BorderBrush="{DynamicResource WarnListBorder}" BorderThickness="1" Padding="14,10" Margin="0,12,0,0"
                    Visibility="{Binding ContentRows.Count, Converter={StaticResource CountToVisible}}">
                <StackPanel>
                    <TextBlock Text="Vady obsahu (kopie souhlasí s kartou — vada je už na kartě)" FontWeight="SemiBold" Foreground="{DynamicResource WarnText2}" Margin="0,0,0,6" />
                    <ItemsControl ItemsSource="{Binding ContentRows}" AutomationProperties.AutomationId="ContentWarnings">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <DockPanel Margin="0,2">
                                    <Button DockPanel.Dock="Right" Style="{StaticResource SecondaryButton}" Content="Náhled" Command="{Binding PreviewCommand}"
                                            Visibility="{Binding HasPicture, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="DefectPreview" />
                                    <TextBlock VerticalAlignment="Center" TextWrapping="Wrap">
                                        <Run Text="{Binding Clip, Mode=OneWay}" FontWeight="SemiBold" /><Run Text=" · " /><Run Text="{Binding Text, Mode=OneWay}" />
                                    </TextBlock>
                                </DockPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button Style="{StaticResource WarnButton}" Content="Zkontrolováno — kartu lze formátovat" Command="{Binding ConfirmContentCommand}" HorizontalAlignment="Left"
                            Margin="0,10,0,0" Visibility="{Binding NeedsContentConfirm, Converter={StaticResource BoolToVisible}}" AutomationProperties.AutomationId="ConfirmContent" />
                </StackPanel>
            </Border>
        </DataTemplate>

        <!-- ── Waiting: one compact row ── -->
        <DataTemplate x:Key="WaitingCard">
```

Najít (2/5, řádky 211–216):

```xml
                               AutomationProperties.AutomationId="JobWarningText" />
                    <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobProfileNote" />
                    <!-- safe to eject while stage 2 still runs -->
                    <DockPanel Margin="0,12,0,0" Visibility="{Binding ShowSafe, Converter={StaticResource BoolToVisible}}">
                        <Button DockPanel.Dock="Right" Style="{StaticResource SuccessSecondaryButton}" Content="Vysunout kartu" Command="{Binding EjectCommand}"
```

Nahradit:

```xml
                               AutomationProperties.AutomationId="JobWarningText" />
                    <TextBlock Text="{Binding ProfileNote}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding ProfileNote, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobProfileNote" />
                    <ContentControl Content="{Binding}" ContentTemplate="{StaticResource VerdictChips}" Focusable="False" />
                    <!-- safe to eject while stage 2 still runs -->
                    <DockPanel Margin="0,12,0,0" Visibility="{Binding ShowSafe, Converter={StaticResource BoolToVisible}}">
                        <Button DockPanel.Dock="Right" Style="{StaticResource SuccessSecondaryButton}" Content="Vysunout kartu" Command="{Binding EjectCommand}"
```

Najít (3/5, řádky 264–269):

```xml
                            <TextBlock Text="{Binding HeadTitle}" FontSize="20" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" Margin="0,4,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                            <TextBlock Text="Karta vysunuta" Foreground="{DynamicResource SuccessLabel}" Margin="0,4,0,0" Visibility="{Binding Ejected, Converter={StaticResource BoolToVisible}}" />
                        </StackPanel>
                    </DockPanel>
                    <!-- Kopie · Ověření · Proxy · Celkem (nová věc 3) -->
```

Nahradit:

```xml
                            <TextBlock Text="{Binding HeadTitle}" FontSize="20" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource SuccessText2}" TextWrapping="Wrap" Margin="0,4,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                            <TextBlock Text="Karta vysunuta" Foreground="{DynamicResource SuccessLabel}" Margin="0,4,0,0" Visibility="{Binding Ejected, Converter={StaticResource BoolToVisible}}" />
                            <ContentControl Content="{Binding}" ContentTemplate="{StaticResource VerdictChips}" Focusable="False" />
                        </StackPanel>
                    </DockPanel>
                    <!-- Kopie · Ověření · Proxy · Celkem (nová věc 3) -->
```

Najít (4/5, řádky 369–376):

```xml
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource WarnText2}" TextWrapping="Wrap" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                        </StackPanel>
                    </DockPanel>
                    <!-- path · state on each destination ("RAID_A ✓", "SHUTTLE_B ✕ hash") -->
                    <Border CornerRadius="10" Background="{DynamicResource WarnListBg}" BorderBrush="{DynamicResource WarnListBorder}" BorderThickness="1" Padding="14,10" Margin="0,14,0,0"
                            Visibility="{Binding FailedRows.Count, Converter={StaticResource CountToVisible}}">
```

Nahradit:

```xml
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                            <TextBlock Text="{Binding HeadSub}" Foreground="{DynamicResource WarnText2}" TextWrapping="Wrap" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                            <ContentControl Content="{Binding}" ContentTemplate="{StaticResource VerdictChips}" Focusable="False" />
                        </StackPanel>
                    </DockPanel>
                    <ContentControl Content="{Binding}" ContentTemplate="{StaticResource ContentWarnings}" Focusable="False" />
                    <!-- path · state on each destination ("RAID_A ✓", "SHUTTLE_B ✕ hash") -->
                    <Border CornerRadius="10" Background="{DynamicResource WarnListBg}" BorderBrush="{DynamicResource WarnListBorder}" BorderThickness="1" Padding="14,10" Margin="0,14,0,0"
                            Visibility="{Binding FailedRows.Count, Converter={StaticResource CountToVisible}}">
```

Najít (5/5, řádky 463–468):

```xml
                <StackPanel>
                    <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                    <TextBlock Text="{Binding HeadSub}" Style="{StaticResource TextMuted}" FontSize="14" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                    <TextBlock Text="{Binding NoteText}" Style="{StaticResource TextMuted}" TextWrapping="Wrap" Margin="0,8,0,0" Visibility="{Binding NoteText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobNoteText" />
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
```

Nahradit:

```xml
                <StackPanel>
                    <TextBlock Text="{Binding HeadTitle}" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" AutomationProperties.AutomationId="JobHeadTitle" />
                    <TextBlock Text="{Binding HeadSub}" Style="{StaticResource TextMuted}" FontSize="14" Margin="0,3,0,0" AutomationProperties.AutomationId="JobHeadSub" />
                    <ContentControl Content="{Binding}" ContentTemplate="{StaticResource VerdictChips}" Focusable="False" />
                    <TextBlock Text="{Binding NoteText}" Style="{StaticResource TextMuted}" TextWrapping="Wrap" Margin="0,8,0,0" Visibility="{Binding NoteText, Converter={StaticResource NullToCollapsed}}"
                               AutomationProperties.AutomationId="JobNoteText" />
                    <TextBlock Text="{Binding WarningText}" Style="{StaticResource TextWarn}" Margin="0,8,0,0" Visibility="{Binding WarningText, Converter={StaticResource NullToCollapsed}}"
```

#### `windows/src/Offload.Cli/ProgressDisplay.cs`

Najít (1/1, řádky 110–116):

```csharp
            else if (st.Phase is JobPhase.Analyzing or JobPhase.Proxies)
            {
                line = $"{st.Phase.ToString().ToLowerInvariant()} {st.ClipIndex + 1}/{st.ClipCount}  {st.ClipFraction * 100,3:0}%  {st.ClipName}";
                if (st.SafeToEject) line += "  (card safe to eject)";
            }
            else
            {
```

Nahradit:

```csharp
            else if (st.Phase is JobPhase.Analyzing or JobPhase.Proxies)
            {
                line = $"{st.Phase.ToString().ToLowerInvariant()} {st.ClipIndex + 1}/{st.ClipCount}  {st.ClipFraction * 100,3:0}%  {st.ClipName}";
                // R5 (plan 10): "can be removed" once copied; formatting only after the verification.
                if (st.SafeToFormat) line += "  (card safe to format)";
                else if (st.SafeToEject) line += "  (card can be removed — verifying, do not format)";
            }
            else if (st.Phase == JobPhase.Finishing)
            {
                line = $"finishing verify  {Format.Bytes(st.VerifiedBytes)} / {Format.Bytes(st.VerifyTotal)}";
            }
            else
            {
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifyVerdictTests|FullyQualifiedName~JobCard|FullyQualifiedName~JobOutcomeTests|FullyQualifiedName~CliNtfyTests|FullyQualifiedName~DemoScenarioTests|FullyQualifiedName~HistoryTextTests|FullyQualifiedName~JobProgressTests|FullyQualifiedName~SetupTextsTests|FullyQualifiedName~ThemeTokensTests"`

Expected: PASS — `Failed: 0, Passed: 140, Skipped: 0, Total: 140` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1641` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 15: karta úlohy, oznámení, historie a CLI se dvěma stavy (R5): štítky „Lze vyjmout“ / „Lze formátovat“, zelená jen po ověření, vady obsahu se seznamem, náhledem a potvrzením; kroky Kopírování · Proxy · Ověření · Report.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: eject and format verdicts on the job card, notifications and history (plan 10, task 15)

The job card shows two chips, "Lze vyjmout" and "Lze formátovat", never merged; green and
"kartu lze formátovat" only with SafeToFormat, content warnings need a confirmation and offer a
preview of the defect. Notifications, history texts and the CLI follow R5.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\JobItemViewModel.Verify.cs windows\src\Offload.App\JobItemViewModel.cs windows\src\Offload.App\QueuePanel.xaml windows\src\Offload.Cli\ProgressDisplay.cs windows\src\Offload.Core\Desktop\DemoScenario.cs windows\src\Offload.Core\Desktop\HistoryView.cs windows\src\Offload.Core\Desktop\JobCard.cs windows\src\Offload.Core\Desktop\Texts.cs windows\src\Offload.Core\History\HistoryText.cs windows\src\Offload.Core\Notify\JobOutcome.cs windows\tests\Offload.Core.Tests\CliNtfyTests.cs windows\tests\Offload.Core.Tests\DemoScenarioTests.cs windows\tests\Offload.Core.Tests\HistoryTextTests.cs windows\tests\Offload.Core.Tests\JobCardStateTests.cs windows\tests\Offload.Core.Tests\JobCardTests.cs windows\tests\Offload.Core.Tests\JobProgressTests.cs windows\tests\Offload.Core.Tests\NotifyTests.cs windows\tests\Offload.Core.Tests\SetupTextsTests.cs windows\tests\Offload.Core.Tests\VerifyVerdictTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 16: Nastavení „Ověřit primární disk během proxy“ a CLI `--classic-verify`, `--primary` (R11)

Volba v Nastavení → Ověření „Ověřit primární disk během proxy“ (výchozí zapnuto; `verifyInProxy` v
`settings.json`, součást projektu) a „Primární disk“: nejrychlejší při kopii (výchozí) nebo první cíl
(`verifyPrimary`: `fastest` / `first`). CLI: `--classic-verify` vrátí zpětné čtení hned po kopii, `--primary N` zvolí
cíl (číslováno od 1 v pořadí `--dest`; 0 a méně = chyba). Starší `settings.json` bez klíčů = výchozí hodnoty.

**Files:**
- Modify: `windows/src/Offload.Core/Desktop/AppSettings.cs:23-28, 99-105, 189-194`
- Modify: `windows/src/Offload.Core/Desktop/JobConfigs.cs:16-21, 26-31, 71-76`
- Modify: `windows/src/Offload.Core/Desktop/SettingsTexts.cs:8-13`
- Modify: `windows/src/Offload.App/MainViewModel.Setup.cs:442-447`
- Modify: `windows/src/Offload.App/SettingsPage.xaml:118-123`
- Modify: `windows/src/Offload.Cli/StageTwoOptions.cs:25-34, 94-99`
- Modify: `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs:28-29`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/VerifySettingsTests.cs` (nový)

**Interfaces:**
- Consumes: `JobConfig.VerifyInProxy/PrimaryDestination` (úkol 12), `SettingsProfile`, `StageTwoOptions`.
- Produces:
  - `SettingsProfile.VerifyInProxy` (`verifyInProxy`), `SettingsProfile.VerifyPrimary` (`verifyPrimary`), `PrimaryFastest = "fastest"`, `PrimaryFirst = "first"`; `JobConfigs.Primary(p)`.
  - `SettingsTexts` řádky Ověření; `MainViewModel.VerifyInProxy`, `VerifyPrimaryFirst`; CLI `--classic-verify`, `--primary`; `Msg.PrimaryNotPositive(n)`.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/VerifySettingsTests.cs` (nový soubor)

```csharp
using System.Text.Json;
using Offload.Cli;
using Offload.Core.Desktop;

namespace Offload.Core.Tests;

/// R11 (plan 10): "Ověřit primární disk během proxy" and the primary disk in settings.json; CLI --classic-verify and
/// --primary.
public class VerifySettingsTests
{
    [Fact]
    public void Setting_RoundTrip_AndJobConfigs()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{"verifyInProxy":false,"verifyPrimary":"first","projects":[{"name":"X","verifyPrimary":"something-newer"}]}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.VerifyInProxy);
        Assert.Equal("first", s.VerifyPrimary);
        Assert.Equal(0, JobConfigs.Full(s, @"E:\", null, "2026-10-08").PrimaryDestination);
        Assert.False(JobConfigs.Full(s, @"E:\", null, "2026-10-08").VerifyInProxy);
        Assert.Null(JobConfigs.Primary(s.Projects[0]));   // unknown value = the fastest
        Assert.True(new AppSettings().VerifyInProxy);      // R11: on by default
        SettingsStore.Save(s, path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(doc.RootElement.GetProperty("verifyInProxy").GetBoolean());
        Assert.Equal("first", doc.RootElement.GetProperty("verifyPrimary").GetString());
    }

    [Fact]
    public void Cli_ClassicVerifyAndPrimary_OntoTheJob()
    {
        var stage = new StageTwoOptions();
        var cmd = new System.CommandLine.Command("t");
        stage.AddTo(cmd);
        JobConfig Apply(params string[] args)
        {
            var config = new JobConfig(@"E:\", [@"D:\"]);
            stage.Apply(cmd.Parse(args), config, new AppSettings());
            return config;
        }

        Assert.True(Apply().VerifyInProxy);
        Assert.Null(Apply().PrimaryDestination);
        Assert.False(Apply("--classic-verify").VerifyInProxy);
        Assert.Equal(1, Apply("--primary", "2").PrimaryDestination);
        var e = Assert.Throws<ArgumentException>(() => Apply("--primary", "0"));
        Assert.Equal(Msg.PrimaryNotPositive(0), e.Message);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifySettingsTests|FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~JobConfigsTests|FullyQualifiedName~SettingsTextsTests|FullyQualifiedName~CliProjectTests|FullyQualifiedName~CliOutputsTests"`

Expected: FAIL — build selže (rozhoduje kód chyby; v české lokalizaci je text hlášky česky): `CS1061: 'AppSettings' does not contain a definition for 'VerifyInProxy' and no accessible extension method 'VerifyInProxy' accepting a first argument of type 'AppSettings' could be found (are you missing a using directive or an assembly reference?)`; `CS1061: 'AppSettings' does not contain a definition for 'VerifyPrimary' and no accessible extension method 'VerifyPrimary' accepting a first argument of type 'AppSettings' could be found (are you missing a using directive or an assembly reference?)`; `CS0117: 'JobConfigs' does not contain a definition for 'Primary'` a další chyby téhož druhu (všechny jen v testech tohoto úkolu).

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/Desktop/AppSettings.cs`

Najít (1/3, řádky 23–28):

```csharp
{
    [JsonPropertyName("destRoots")] public List<string> DestRoots { get; set; } = [];
    [JsonPropertyName("verify")] public bool Verify { get; set; } = true;
    [JsonPropertyName("analyzeClips")] public bool AnalyzeClips { get; set; } = true;
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
```

Nahradit:

```csharp
{
    [JsonPropertyName("destRoots")] public List<string> DestRoots { get; set; } = [];
    [JsonPropertyName("verify")] public bool Verify { get; set; } = true;
    /// Plan 10 (R11): "Ověřit primární disk během proxy" — on by default; off = the read-back right after the copy.
    [JsonPropertyName("verifyInProxy")] public bool VerifyInProxy { get; set; } = true;
    /// Plan 10 (spec 6): which destination the proxies read and verify — "fastest" (measured during the copy, default) or
    /// "first" (the first in the order of the destinations). A value this version does not know is "fastest".
    [JsonPropertyName("verifyPrimary")] public string VerifyPrimary { get; set; } = PrimaryFastest;
    public const string PrimaryFastest = "fastest";
    public const string PrimaryFirst = "first";
    [JsonPropertyName("analyzeClips")] public bool AnalyzeClips { get; set; } = true;
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
```

Najít (2/3, řádky 99–105):

```csharp
    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The keys of a profile (top level and every project) Load reads into properties.
    static readonly string[] ProfileKeys = ["destRoots", "verify", "analyzeClips", "proxy", "naming", "hash", "outputs", "notify"];
    /// The global top-level keys; every other top-level key goes to AppSettings.Extra.
    static readonly string[] GlobalKeys = ["tools", "portNames", "updates", "projects", "activeProject"];

```

Nahradit:

```csharp
    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The keys of a profile (top level and every project) Load reads into properties.
    static readonly string[] ProfileKeys = ["destRoots", "verify", "verifyInProxy", "verifyPrimary", "analyzeClips", "proxy", "naming", "hash", "outputs", "notify"];
    /// The global top-level keys; every other top-level key goes to AppSettings.Extra.
    static readonly string[] GlobalKeys = ["tools", "portNames", "updates", "projects", "activeProject"];

```

Najít (3/3, řádky 189–194):

```csharp
        Section(obj, p, "destRoots", e => p.DestRoots = e.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList());
        Section(obj, p, "verify", e => p.Verify = e.GetBoolean());
        Section(obj, p, "analyzeClips", e => p.AnalyzeClips = e.GetBoolean());
        Section(obj, p, "hash", e => p.Hash = e.GetString() is { Length: > 0 } h ? h : "xxh64");
        Kept(obj, p, "proxy", (ProxySettings v) => p.Proxy = v, () => new ProxySettings());
```

Nahradit:

```csharp
        Section(obj, p, "destRoots", e => p.DestRoots = e.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList());
        Section(obj, p, "verify", e => p.Verify = e.GetBoolean());
        Section(obj, p, "verifyInProxy", e => p.VerifyInProxy = e.GetBoolean());
        Section(obj, p, "verifyPrimary", e => p.VerifyPrimary = e.GetString() is { Length: > 0 } v ? v : SettingsProfile.PrimaryFastest);
        Section(obj, p, "analyzeClips", e => p.AnalyzeClips = e.GetBoolean());
        Section(obj, p, "hash", e => p.Hash = e.GetString() is { Length: > 0 } h ? h : "xxh64");
        Kept(obj, p, "proxy", (ProxySettings v) => p.Proxy = v, () => new ProxySettings());
```

#### `windows/src/Offload.Core/Desktop/JobConfigs.cs`

Najít (1/3, řádky 16–21):

```csharp
            Date = date,
            Hashes = HashKinds.FromSetting(p.Hash),   // an append (below) keeps the earlier backup's (spec 3.5)
            Verify = p.Verify,
            AnalyzeClips = p.AnalyzeClips,
            Proxy = SettingsStore.Clone(p.Proxy),
            Tools = SettingsStore.Clone(s.Tools),
```

Nahradit:

```csharp
            Date = date,
            Hashes = HashKinds.FromSetting(p.Hash),   // an append (below) keeps the earlier backup's (spec 3.5)
            Verify = p.Verify,
            VerifyInProxy = p.VerifyInProxy,
            PrimaryDestination = Primary(p),
            AnalyzeClips = p.AnalyzeClips,
            Proxy = SettingsStore.Clone(p.Proxy),
            Tools = SettingsStore.Clone(s.Tools),
```

Najít (2/3, řádky 26–31):

```csharp
            Notify = SettingsStore.Clone(p.Notify),
        };
    }

    /// "Zkopírovat znovu" (UI návrh 4) after `finished` ended with failed files: see the overload — with the identity of
    /// the card it copied (ruling I1).
```

Nahradit:

```csharp
            Notify = SettingsStore.Clone(p.Notify),
        };
    }

    /// Plan 10: the profile's primary destination — the first one, or null = the fastest measured during the copy.
    public static int? Primary(SettingsProfile p) => p.VerifyPrimary == SettingsProfile.PrimaryFirst ? 0 : null;

    /// "Zkopírovat znovu" (UI návrh 4) after `finished` ended with failed files: see the overload — with the identity of
    /// the card it copied (ruling I1).
```

Najít (3/3, řádky 71–76):

```csharp
            AllowSameDevice = true,   // the destinations are fixed by the earlier backup (it may have been made with --allow-same-device)
            AppendInto = entry.Destinations.Select(d => d.CardPath).ToList(),
            Verify = p.Verify,
            AnalyzeClips = p.AnalyzeClips,
            Proxy = SettingsStore.Clone(p.Proxy),
            Tools = SettingsStore.Clone(s.Tools),
```

Nahradit:

```csharp
            AllowSameDevice = true,   // the destinations are fixed by the earlier backup (it may have been made with --allow-same-device)
            AppendInto = entry.Destinations.Select(d => d.CardPath).ToList(),
            Verify = p.Verify,
            VerifyInProxy = p.VerifyInProxy,
            PrimaryDestination = Primary(p),
            AnalyzeClips = p.AnalyzeClips,
            Proxy = SettingsStore.Clone(p.Proxy),
            Tools = SettingsStore.Clone(s.Tools),
```

#### `windows/src/Offload.Core/Desktop/SettingsTexts.cs`

Najít (1/1, řádky 8–13):

```csharp
/// The settings page (UI návrh obr. 4), without a window.
public static class SettingsTexts
{
    /// "≈ 75 MB / minutu" under the bitrate (video only; the audio adds little).
    public static string BitrateHint(int mbps) => $"≈ {mbps * 60 / 8} MB / minutu";

```

Nahradit:

```csharp
/// The settings page (UI návrh obr. 4), without a window.
public static class SettingsTexts
{
    /// Plan 10 (R11), Nastavení → Ověření.
    public const string VerifyInProxyLabel = "Ověřit primární disk během proxy";
    public const string VerifyInProxyNote =
        "Primární disk se ověří při čtení pro proxy, ostatní souběžně na pozadí — bez druhého čtení navíc. Kartu lze vyjmout po kopii, formátovat až po ověření všech disků. Bez proxy se ověřuje zpětným čtením.";
    public const string VerifyPrimaryFirstLabel = "Primární je první cíl (jinak nejrychlejší disk změřený při kopii)";

    /// "≈ 75 MB / minutu" under the bitrate (video only; the audio adds little).
    public static string BitrateHint(int mbps) => $"≈ {mbps * 60 / 8} MB / minutu";

```

#### `windows/src/Offload.App/MainViewModel.Setup.cs`

Najít (1/1, řádky 442–447):

```csharp
        set { Settings.Verify = value; SettingsChanged(); Raise(); }
    }

    /// "Kontrolní součet" (spec 3.5): xxHash64 or xxHash64 + MD5 — `hash` in settings.json.
    public IReadOnlyList<HashChoice> HashChoices => HashChoice.All;

```

Nahradit:

```csharp
        set { Settings.Verify = value; SettingsChanged(); Raise(); }
    }

    /// Plan 10 (R11): "Ověřit primární disk během proxy" — `verifyInProxy` in settings.json.
    public bool VerifyInProxy
    {
        get => Settings.VerifyInProxy;
        set { Settings.VerifyInProxy = value; SettingsChanged(); Raise(); }
    }

    /// Plan 10 (spec 6): the first destination is the primary one instead of the fastest — `verifyPrimary`.
    public bool VerifyPrimaryFirst
    {
        get => Settings.VerifyPrimary == SettingsProfile.PrimaryFirst;
        set { Settings.VerifyPrimary = value ? SettingsProfile.PrimaryFirst : SettingsProfile.PrimaryFastest; SettingsChanged(); Raise(); }
    }

    /// "Kontrolní součet" (spec 3.5): xxHash64 or xxHash64 + MD5 — `hash` in settings.json.
    public IReadOnlyList<HashChoice> HashChoices => HashChoice.All;

```

#### `windows/src/Offload.App/SettingsPage.xaml`

Najít (1/1, řádky 118–123):

```xml
                                  MinWidth="200" AutomationProperties.AutomationId="HashChoice" />
                    </Grid>
                    <TextBlock Text="{Binding Md5Note}" Style="{StaticResource TextWarn}" Margin="138,8,0,0" Visibility="{Binding Md5Note, Converter={StaticResource NullToCollapsed}}" />
                    <TextBlock Text="OVĚŘIT ZÁLOHU" Style="{StaticResource StepTitle}" Margin="0,30,0,10" />
                    <TextBlock Text="Zkontroluje dřívější zálohu na cíli proti ASC MHL, bez karty (Ctrl+Shift+O)." Style="{StaticResource TextMuted}" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
```

Nahradit:

```xml
                                  MinWidth="200" AutomationProperties.AutomationId="HashChoice" />
                    </Grid>
                    <TextBlock Text="{Binding Md5Note}" Style="{StaticResource TextWarn}" Margin="138,8,0,0" Visibility="{Binding Md5Note, Converter={StaticResource NullToCollapsed}}" />
                    <!-- Plan 10 (R11): verify the primary disk while the proxies are made; the others in the background. -->
                    <CheckBox Style="{StaticResource Check}" Content="{x:Static core:SettingsTexts.VerifyInProxyLabel}" IsChecked="{Binding VerifyInProxy}" Margin="0,22,0,0"
                              IsEnabled="{Binding Verify}" AutomationProperties.AutomationId="VerifyInProxy" />
                    <TextBlock Text="{x:Static core:SettingsTexts.VerifyInProxyNote}" Style="{StaticResource TextMuted}" TextWrapping="Wrap" Margin="26,4,0,0" />
                    <CheckBox Style="{StaticResource Check}" Content="{x:Static core:SettingsTexts.VerifyPrimaryFirstLabel}" IsChecked="{Binding VerifyPrimaryFirst}" Margin="26,10,0,0"
                              IsEnabled="{Binding VerifyInProxy}" AutomationProperties.AutomationId="VerifyPrimaryFirst" />
                    <TextBlock Text="OVĚŘIT ZÁLOHU" Style="{StaticResource StepTitle}" Margin="0,30,0,10" />
                    <TextBlock Text="Zkontroluje dřívější zálohu na cíli proti ASC MHL, bez karty (Ctrl+Shift+O)." Style="{StaticResource TextMuted}" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
```

#### `windows/src/Offload.Cli/StageTwoOptions.cs`

Najít (1/2, řádky 25–34):

```csharp
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };
    readonly Option<bool> brawGen5 = new("--braw-gen5") { Description = "Decode Blackmagic RAW Film Gen 4 as Gen 5 (the Gen 5 LUT then fits). Not yet verified with a Gen 4 sample." };
    readonly Option<bool> noDayReport = new("--no-day-report") { Description = "Do not make the day report (<date>\\_REPORT\\<date>_day_report.html) again after the copy." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, lutRule, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, proxyAlways, noBurnIn, proxyFolder, noClipList, ale, noDayReport, brawGen5 })
            cmd.Options.Add(o);
    }

```

Nahradit:

```csharp
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };
    readonly Option<bool> brawGen5 = new("--braw-gen5") { Description = "Decode Blackmagic RAW Film Gen 4 as Gen 5 (the Gen 5 LUT then fits). Not yet verified with a Gen 4 sample." };
    readonly Option<bool> noDayReport = new("--no-day-report") { Description = "Do not make the day report (<date>\\_REPORT\\<date>_day_report.html) again after the copy." };
    readonly Option<bool> classicVerify = new("--classic-verify") { Description = "Read every destination back right after the copy (as before 0.6.0) instead of verifying the primary one while the proxies are made." };
    readonly Option<int?> primary = new("--primary") { Description = "Destination (1, 2 …, in the order of --dest) the proxies read and verify; default: the fastest one measured during the copy." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, lutRule, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, proxyAlways, noBurnIn, proxyFolder, noClipList, ale, noDayReport, brawGen5, classicVerify, primary })
            cmd.Options.Add(o);
    }

```

Najít (2/2, řádky 94–99):

```csharp
        if (Given(noDayReport)) outputs.DayReport = !r.GetValue(noDayReport);
        config.Outputs = outputs;
        config.AnalyzeClips = Given(noAnalyze) ? !r.GetValue(noAnalyze) : project!.AnalyzeClips;

        if (r.GetValue(proxyFolder) != null && !p.Enabled)
            Console.Error.WriteLine(Msg.ProxyFolderNoEffect);
```

Nahradit:

```csharp
        if (Given(noDayReport)) outputs.DayReport = !r.GetValue(noDayReport);
        config.Outputs = outputs;
        config.AnalyzeClips = Given(noAnalyze) ? !r.GetValue(noAnalyze) : project!.AnalyzeClips;
        // Plan 10 (R11): verify-in-proxy unless --classic-verify (or the project switched it off); --primary picks the disk.
        config.VerifyInProxy = Given(classicVerify) ? !r.GetValue(classicVerify) : project!.VerifyInProxy;
        if (r.GetValue(primary) is int n)
        {
            if (n < 1) throw new ArgumentException(Msg.PrimaryNotPositive(n));
            config.PrimaryDestination = n - 1;
        }
        else config.PrimaryDestination = project != null ? JobConfigs.Primary(project) : null;

        if (r.GetValue(proxyFolder) != null && !p.Enabled)
            Console.Error.WriteLine(Msg.ProxyFolderNoEffect);
```

#### `windows/src/Offload.Core/VerifyInProxy/Msg.VerifyInProxy.cs`

Najít (1/1, řádky 28–29):

```csharp

    // R6 (resume).
```

Nahradit:

```csharp

    /// CLI --primary 0 or less.
    public static string PrimaryNotPositive(int n) => $"--primary {n}: cíle se číslují od 1 (pořadí --dest).";

    // R6 (resume).
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~VerifySettingsTests|FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~JobConfigsTests|FullyQualifiedName~SettingsTextsTests|FullyQualifiedName~CliProjectTests|FullyQualifiedName~CliOutputsTests"`

Expected: PASS — `Failed: 0, Passed: 82, Skipped: 0, Total: 82` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1643` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 16: Nastavení → Ověření „Ověřit primární disk během proxy“ a „Primární disk“ (nejrychlejší / první), CLI `--classic-verify` a `--primary N` (R11).
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: verify-in-proxy setting and CLI --classic-verify, --primary (plan 10, task 16)

Settings → Ověření gets "Ověřit primární disk během proxy" and the primary disk choice (fastest or
first), stored per project; the CLI gets --classic-verify and --primary N.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.App\MainViewModel.Setup.cs windows\src\Offload.App\SettingsPage.xaml windows\src\Offload.Cli\StageTwoOptions.cs windows\src\Offload.Core\Desktop\AppSettings.cs windows\src\Offload.Core\Desktop\JobConfigs.cs windows\src\Offload.Core\Desktop\SettingsTexts.cs windows\src\Offload.Core\VerifyInProxy\Msg.VerifyInProxy.cs windows\tests\Offload.Core.Tests\VerifySettingsTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 17: HTML a TXT report: metoda po cílech, čas do „lze formátovat“, vady obsahu, poznámky (spec 9)

HTML report (a z něj PDF) ukáže stav „ZKOPÍROVÁNO — OVĚŘUJE SE“ u čekajícího ověření, metodu u každého cíle
(„během proxy“ / „zpětné čtení“ + důvod), sériové číslo svazku, čas do „lze formátovat“, tabulku vad obsahu (klip,
druh, TC, hláška) a poznámky ke spec 10; chybu `HASH MISMATCH: block …` přeloží česky („blok N nesouhlasí“). TXT report
dostane řádky „Safe to format after“, „Content warnings“ a metodu i sériové číslo u cílů. Shrnutí na kartě úlohy
u ověření během proxy napíše „během proxy“ místo rychlosti.

**Files:**
- Modify: `windows/src/Offload.Core/HtmlReport.cs:92-97, 114-119, 157-164, 178-193, 200-205, 216-221, 264-269`
- Modify: `windows/src/Offload.Core/OffloadReport.cs:216-226`
- Modify: `windows/src/Offload.Core/Desktop/JobCard.cs:178-187`
- Modify: `docs/VYVOJ-WINDOWS.md:25-26`
- Test: `windows/tests/Offload.Core.Tests/HtmlReportVerifyTests.cs` (nový)

**Interfaces:**
- Consumes: pole reportu (úkoly 6, 10, 11), `ContentResult.KindText`.
- Produces:
  - `HtmlReport`: řádky metody, tabulka vad obsahu, poznámky, česká hláška blokového nesouladu; `OffloadReport.Summary` nové řádky; `JobCard` souhrn „během proxy“.

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/HtmlReportVerifyTests.cs` (nový soubor)

```csharp
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 9 (plan 10): the report says how each destination was verified, which disk it was (R12), when the card could be
/// formatted, a verification still running, the content warnings with their place and picture, the notes of spec 10.
public class HtmlReportVerifyTests
{
    const string Card = @"D:\Zaloha\2026-10-08\A001";

    [Fact]
    public void Html_PendingContentWarningsNotes_AndBlockMismatchInCzech()
    {
        var r = Reports.Sample(Card, verified: false);
        r.VerifyPending = true;
        r.VerifyNotes = ["Nestabilní čtení: a.mov na RAID_A (blok 3) dal při jednom čtení jiná data — při dalším souhlasil s kartou."];
        r.Clips = [new ClipEntry
        {
            Clip = new Clip { Name = "A001C003", Path = "CLIP/A&B.mov", Segments = ["CLIP/A&B.mov"] },
            Content = new ContentResult { State = ContentResult.Warn, Kind = "decode", Timecode = "01:00:02:13", Message = "corrupt decoded frame", Jpeg = [0xFF, 0xD8] },
        }];

        var html = HtmlReport.Render(r);

        Assert.Contains("ZKOPÍROVÁNO — OVĚŘUJE SE", html);
        Assert.Contains("Vady obsahu (1)", html);
        Assert.Contains("01:00:02:13", html);
        Assert.Contains("poškozená data", html);
        Assert.Contains("data:image/jpeg;base64,/9g=", html);
        Assert.Contains("Ověření — poznámky (1)", html);
        Assert.Equal("nesouhlasí blok 3 (cíl 0000000000000001, karta 0000000000000002)",
            HtmlReport.ErrorText("HASH MISMATCH: block 3 dest 0000000000000001 != source 0000000000000002"));
    }

    // Spec 9: the method and the serial per destination, the time to "Lze formátovat", in the HTML and the text report.
    [Fact]
    public void Classic_Html_MethodSerialAndTimeToFormat()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 10_000, 1);
        var engine = new OffloadEngine(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08"));

        var html = HtmlReport.Render(report);

        Assert.Contains("<th>Svazek</th><th>Ověření</th>", html);
        Assert.Contains($"<td class=\"mono\">{report.Destinations[0].VolumeSerial}</td><td>zpětné čtení</td>", html);
        Assert.Contains("do „lze formátovat“", html);
        Assert.Contains("XXH64 + XXH128", html);
        Assert.Contains("  verify: read-back", report.Summary);
        Assert.Contains("Safe to format after: ", report.Summary);
    }
}
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~HtmlReportVerifyTests|FullyQualifiedName~HtmlReportTests|FullyQualifiedName~ReportTests|FullyQualifiedName~JobCard"`

Expected: build projde, testy FAIL: `HtmlReportVerifyTests.Classic_Html_MethodSerialAndTimeToFormat`, `HtmlReportVerifyTests.Html_PendingContentWarningsNotes_AndBlockMismatchInCzech` — první: „Assert.Contains() Failure: Sub-string not found“.

- [ ] **Step 3: Implementace**

#### `windows/src/Offload.Core/HtmlReport.cs`

Najít (1/7, řádky 92–97):

```csharp
    [GeneratedRegex(@"^(?:HASH|MD5) MISMATCH: dest (?<dest>\S+) != source (?<source>\S+)$")]
    private static partial Regex Mismatch();

    [GeneratedRegex(@" \(after (?<n>\d+) retries\)$")]
    private static partial Regex Retries();

```

Nahradit:

```csharp
    [GeneratedRegex(@"^(?:HASH|MD5) MISMATCH: dest (?<dest>\S+) != source (?<source>\S+)$")]
    private static partial Regex Mismatch();

    /// Plan 10: a block that does not match (BlockReads.Result).
    [GeneratedRegex(@"^HASH MISMATCH: block (?<block>\d+) dest (?<dest>\S+) != source (?<source>\S+)$")]
    private static partial Regex BlockMismatch();

    [GeneratedRegex(@" \(after (?<n>\d+) retries\)$")]
    private static partial Regex Retries();

```

Najít (2/7, řádky 114–119):

```csharp
        if (error.StartsWith("verify: ", StringComparison.Ordinal)) return "chyba při ověření: " + ErrorText(error["verify: ".Length..]);
        if (error.StartsWith("set aside: ", StringComparison.Ordinal))
            return "nesouhlasnou kopii nejde odložit, zůstala na místě: " + error["set aside: ".Length..];
        if (Mismatch().Match(error) is { Success: true } m)
            return $"{(error.StartsWith("MD5", StringComparison.Ordinal) ? "nesouhlasí MD5" : "nesouhlasí kontrolní součet")} (cíl {m.Groups["dest"].Value}, karta {m.Groups["source"].Value})";
        if (HashFailed().Match(error) is { Success: true } h)
```

Nahradit:

```csharp
        if (error.StartsWith("verify: ", StringComparison.Ordinal)) return "chyba při ověření: " + ErrorText(error["verify: ".Length..]);
        if (error.StartsWith("set aside: ", StringComparison.Ordinal))
            return "nesouhlasnou kopii nejde odložit, zůstala na místě: " + error["set aside: ".Length..];
        if (BlockMismatch().Match(error) is { Success: true } bm)
            return $"nesouhlasí blok {bm.Groups["block"].Value} (cíl {bm.Groups["dest"].Value}, karta {bm.Groups["source"].Value})";
        if (Mismatch().Match(error) is { Success: true } m)
            return $"{(error.StartsWith("MD5", StringComparison.Ordinal) ? "nesouhlasí MD5" : "nesouhlasí kontrolní součet")} (cíl {m.Groups["dest"].Value}, karta {m.Groups["source"].Value})";
        if (HashFailed().Match(error) is { Success: true } h)
```

Najít (3/7, řádky 157–164):

```csharp
    public static string Render(OffloadReport r, bool forPrint = false)
    {
        bool ok = r.Success;
        string status = !ok ? "CHYBA" : (r.Verified ? "OVĚŘENO" : "ZKOPÍROVÁNO — NEOVĚŘENO");
        string statusClass = !ok ? "bad" : (r.Verified ? "ok" : "warn");
        var failed = r.FailedFiles;
        var videoClips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        double totalDuration = videoClips.Sum(c => c.Info?.Duration ?? 0);
```

Nahradit:

```csharp
    public static string Render(OffloadReport r, bool forPrint = false)
    {
        bool ok = r.Success;
        string status = !ok ? "CHYBA" : r.VerifyPending == true ? "ZKOPÍROVÁNO — OVĚŘUJE SE" : (r.Verified ? "OVĚŘENO" : "ZKOPÍROVÁNO — NEOVĚŘENO");
        string statusClass = !ok ? "bad" : (r.Verified ? "ok" : "warn");
        bool inProxy = r.Destinations.Any(d => d.VerifyMethod == ReportDestination.InProxy);
        var failed = r.FailedFiles;
        var videoClips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        double totalDuration = videoClips.Sum(c => c.Info?.Duration ?? 0);
```

Najít (4/7, řádky 178–193):

```csharp
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "neprovedeno"));
        if (r.PausedSeconds > 0) Stat(h, Duration(r.PausedSeconds), "pozastaveno · mimo rychlosti");
        Stat(h, Esc(string.Join(" + ", r.Algorithms.Select(a => a.ToUpperInvariant()))), "kontrolní součet");
        h.Append("</div>\n\n");

        h.Append("<h2>Cíle</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Disk</th><th>Složka</th><th>FS</th><th class=\"num\">OK</th><th class=\"num\">Chyby</th></tr>\n");
        foreach (var d in r.Destinations)
        {
            h.Append($"<tr><td>{Esc(DiskName(d.Root))}</td><td class=\"mono wrap\">{Esc(d.CardPath)}</td><td>{Esc(d.Filesystem)}</td>");
            h.Append($"<td class=\"num ok\">{d.FilesOK}</td><td class=\"num {(d.FilesFailed > 0 ? "bad" : "muted")}\">{d.FilesFailed}</td></tr>\n");
        }
        h.Append("</table></div>\n");
```

Nahradit:

```csharp
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (!r.Verified ? (r.VerifyPending == true ? "probíhá" : "neprovedeno")
            : inProxy ? "během proxy" : Format.Rate(r.TotalBytes, r.VerifySeconds)));
        if (r.SafeToFormatSeconds is double toFormat) Stat(h, Duration(toFormat), "do „lze formátovat“");
        if (r.PausedSeconds > 0) Stat(h, Duration(r.PausedSeconds), "pozastaveno · mimo rychlosti");
        var algorithms = r.Algorithms.ToList();
        if (r.Files.Any(f => f.Xxh128 != null) && !algorithms.Contains("xxh128")) algorithms.Add("xxh128");   // plan 10, R2
        Stat(h, Esc(string.Join(" + ", algorithms.Select(a => a.ToUpperInvariant()))), "kontrolní součet");
        h.Append("</div>\n\n");

        h.Append("<h2>Cíle</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Disk</th><th>Složka</th><th>FS</th><th>Svazek</th><th>Ověření</th><th class=\"num\">OK</th><th class=\"num\">Chyby</th></tr>\n");
        foreach (var d in r.Destinations)
        {
            h.Append($"<tr><td>{Esc(DiskName(d.Root))}</td><td class=\"mono wrap\">{Esc(d.CardPath)}</td><td>{Esc(d.Filesystem)}</td>");
            h.Append($"<td class=\"mono\">{Esc(d.VolumeSerial ?? "—")}</td><td>{Esc(MethodText(d.VerifyMethod))}</td>");
            h.Append($"<td class=\"num ok\">{d.FilesOK}</td><td class=\"num {(d.FilesFailed > 0 ? "bad" : "muted")}\">{d.FilesFailed}</td></tr>\n");
        }
        h.Append("</table></div>\n");
```

Najít (5/7, řádky 200–205):

```csharp
        {
            if (r.Destinations[i].Unbuffered == false)
                h.Append($"<p class=\"warn\">Cíl {i + 1}: svazek nepodporuje přímý zápis, použita systémová cache.</p>\n");
        }
        h.Append($"<p class=\"sub\">Zdroj: <span class=\"mono\">{Esc(r.Source)}</span> ({Esc(r.SourceFilesystem)})</p>\n");

```

Nahradit:

```csharp
        {
            if (r.Destinations[i].Unbuffered == false)
                h.Append($"<p class=\"warn\">Cíl {i + 1}: svazek nepodporuje přímý zápis, použita systémová cache.</p>\n");
            if (r.Destinations[i].VerifyNote is string note && r.Destinations[i].VerifyMethod != null)
                h.Append($"<p class=\"sub\">Cíl {i + 1}: {Esc(note)}</p>\n");
        }
        h.Append($"<p class=\"sub\">Zdroj: <span class=\"mono\">{Esc(r.Source)}</span> ({Esc(r.SourceFilesystem)})</p>\n");

```

Najít (6/7, řádky 216–221):

```csharp
                    if (f.Errors[i] is string e) msgs.Add($"cíl {i + 1}: {ErrorText(e)}");
                h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"err\">{Esc(string.Join(" · ", msgs))}</td></tr>\n");
            }
            h.Append("</table></div>\n");
        }

```

Nahradit:

```csharp
                    if (f.Errors[i] is string e) msgs.Add($"cíl {i + 1}: {ErrorText(e)}");
                h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"err\">{Esc(string.Join(" · ", msgs))}</td></tr>\n");
            }
            h.Append("</table></div>\n");
        }

        var warnings = r.ContentWarnings;
        if (warnings.Count > 0)
        {
            // Spec 7, 9: the decoder found a defect — the copy matches the card, the file itself may be damaged.
            h.Append($"<h2 class=\"warn\">Vady obsahu ({warnings.Count})</h2><p class=\"sub\">Kopie souhlasí s kartou bit po bitu — vada je už v souboru z kamery. Před formátováním karty ji zkontrolujte (možná ji zachrání nástroj výrobce přímo z karty).</p>\n");
            h.Append("<div class=\"panel\"><table><tr><th>Klip</th><th>Místo</th><th>Vada</th><th>Dekodér</th><th></th></tr>\n");
            foreach (var c in warnings)
            {
                var img = c.Content!.Jpeg is { } jpeg ? $"<img class=\"thumb\" style=\"max-width:160px\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}\" alt=\"\">" : "";
                h.Append($"<tr><td>{Esc(c.Clip.Name)}</td><td class=\"mono\">{Esc(c.Content.Timecode ?? "")}</td><td class=\"warn\">{Esc(VerifyInProxy.ContentResult.KindText(c.Content.Kind))}</td>");
                h.Append($"<td class=\"mono wrap\">{Esc(c.Content.Message ?? "")}</td><td>{img}</td></tr>\n");
            }
            h.Append("</table></div>\n");
        }
        if (r.VerifyNotes is { Count: > 0 } notes)
        {
            h.Append($"<h2 class=\"warn\">Ověření — poznámky ({notes.Count})</h2><div class=\"panel\"><table>\n");
            foreach (var n in notes) h.Append($"<tr><td class=\"warn wrap\">{Esc(n)}</td></tr>\n");
            h.Append("</table></div>\n");
        }

```

Najít (7/7, řádky 264–269):

```csharp
        h.Append($"<footer>{Esc(r.Tool)} {Esc(r.Version)} · ASC MHL v ascmhl/ · {DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");
```

Nahradit:

```csharp
        h.Append($"<footer>{Esc(r.Tool)} {Esc(r.Version)} · ASC MHL v ascmhl/ · {DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    /// "během proxy" / "zpětné čtení" / "—" (plan 10, spec 9: the method per destination).
    internal static string MethodText(string? method) => method switch
    {
        ReportDestination.InProxy => "během proxy",
        ReportDestination.ReadBack => "zpětné čtení",
        _ => "—",
    };

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");
```

#### `windows/src/Offload.Core/OffloadReport.cs`

Najít (1/1, řádky 216–226):

```csharp
            s.Append($"Finished:    {LocalIso(Finished)}\n");
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : "NOT VERIFIED") + "\n");
            if (PausedSeconds > 0) s.Append($"Paused:      {PausedSeconds.ToString("0.0", inv)} s (not in the speeds)\n");
            s.Append($"Hash:        {string.Join("+", Algorithms)}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}\n");
            if (Skipped.Count > 0) s.Append($"\nSkipped (system files): {string.Join(", ", Skipped)}\n");
            var failed = FailedFiles;
            if (failed.Count > 0)
```

Nahradit:

```csharp
            s.Append($"Finished:    {LocalIso(Finished)}\n");
            s.Append($"Files:       {TotalFiles}  ({Format.Bytes(TotalBytes)})\n");
            s.Append($"Copy:        {CopySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, CopySeconds)}\n");
            s.Append("Verify:      " + (Verified ? $"{VerifySeconds.ToString("0.0", inv)} s  {Format.Rate(TotalBytes, VerifySeconds)}" : VerifyPending == true ? "PENDING (with the proxies)" : "NOT VERIFIED") + "\n");
            if (SafeToFormatSeconds is double toFormat) s.Append($"Safe to format after: {toFormat.ToString("0.0", inv)} s\n");
            if (ContentWarnings.Count > 0) s.Append($"Content warnings: {ContentWarnings.Count} ({string.Join(", ", ContentWarnings.Select(c => $"{c.Clip.Name} {c.Content!.Timecode}"))})\n");
            if (PausedSeconds > 0) s.Append($"Paused:      {PausedSeconds.ToString("0.0", inv)} s (not in the speeds)\n");
            s.Append($"Hash:        {string.Join("+", Algorithms)}\n");
            foreach (var d in Destinations)
                s.Append($"Destination: {d.CardPath} [{d.Filesystem}{(d.VolumeSerial != null ? " " + d.VolumeSerial : "")}]  ok {d.FilesOK}  failed {d.FilesFailed}{(d.Unbuffered == false ? "  (buffered I/O)" : "")}{(d.VerifyMethod != null ? "  verify: " + d.VerifyMethod : "")}\n");
            if (Skipped.Count > 0) s.Append($"\nSkipped (system files): {string.Join(", ", Skipped)}\n");
            var failed = FailedFiles;
            if (failed.Count > 0)
```

#### `windows/src/Offload.Core/Desktop/JobCard.cs`

Najít (1/1, řádky 178–187):

```csharp
        var proxy = r.Proxy is { Enabled: true }
            ? $"{r.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done)} / {r.Clips.Count(c => !(c.ProxyStatus == ProxyStatus.Skipped && (c.ProxyNote == null || c.OriginalLighter)))}"
            : "vypnuto";
        return
        [
            new("Kopie", Format.Rate(r.TotalBytes, r.CopySeconds)),
            new("Ověření", r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "vypnuto"),
            new("Proxy", proxy),
            new("Celkem", Duration(elapsed ?? (r.Finished - r.Started - TimeSpan.FromSeconds(r.PausedSeconds)))),
        ];
```

Nahradit:

```csharp
        var proxy = r.Proxy is { Enabled: true }
            ? $"{r.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done)} / {r.Clips.Count(c => !(c.ProxyStatus == ProxyStatus.Skipped && (c.ProxyNote == null || c.OriginalLighter)))}"
            : "vypnuto";
        // Plan 10: verified with the proxies — its time is the proxies', no rate of its own.
        bool inProxy = r.Destinations.Any(d => d.VerifyMethod == ReportDestination.InProxy);
        return
        [
            new("Kopie", Format.Rate(r.TotalBytes, r.CopySeconds)),
            new("Ověření", !r.Verified ? "vypnuto" : inProxy ? "během proxy" : Format.Rate(r.TotalBytes, r.VerifySeconds)),
            new("Proxy", proxy),
            new("Celkem", Duration(elapsed ?? (r.Finished - r.Started - TimeSpan.FromSeconds(r.PausedSeconds)))),
        ];
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~HtmlReportVerifyTests|FullyQualifiedName~HtmlReportTests|FullyQualifiedName~ReportTests|FullyQualifiedName~JobCard"`

Expected: PASS — `Failed: 0, Passed: 89, Skipped: 0, Total: 89` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1645` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 25–26):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 17: HTML/PDF a TXT report — metoda ověření a sériové číslo u každého cíle, čas do „lze formátovat“, tabulka vad obsahu, poznámky, česky „blok N nesouhlasí“.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows: verification method, time to format and content warnings in the reports (plan 10, task 17)

The HTML/PDF report shows the pending state, the verification method and volume serial per
destination, the time to safe-to-format, the content warnings and the notes, and explains a block
mismatch in Czech; the TXT report and the job card summary follow.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\VYVOJ-WINDOWS.md windows\src\Offload.Core\Desktop\JobCard.cs windows\src\Offload.Core\HtmlReport.cs windows\src\Offload.Core\OffloadReport.cs windows\tests\Offload.Core.Tests\HtmlReportVerifyTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

### Task 18: Dokumentace, návod a verze 0.6.0

Verze **0.6.0** (`0.6.0-win`) v `windows\Directory.Build.props` a v testech s verzí (`FormatTests`,
`ReportTests`, `HtmlReportTests`). Dokumentace: `docs/VYVOJ-WINDOWS.md` (řádek plánu 10 v tabulce, souhrn sekce),
`docs/FORMATY.md` (manifest, nové klíče reportu, `xxh128` a metoda v ASC MHL), `docs/ROZDILY.md` (co Windows umí a Mac
ne), `docs/VYVOJ-MAC.md` („Převzít z Windows 0.6.0“), `windows/README.md` (ruční testy 50–58, stav) a návod
`windows/package/navod/navod.html` (Lze vyjmout × Lze formátovat, nová volba, dokončení přerušené zálohy). PDF návodu
se znovu vykreslí skriptem `render.ps1` (Edge headless — **ne** aplikace Offload). Na konci celá sada testů.

**Files:**
- Modify: `windows/Directory.Build.props:8-9`
- Modify: `docs/VYVOJ-WINDOWS.md:22, 26-27, 26-27`
- Modify: `docs/FORMATY.md:26, 98, 108, 113, 114, 116, 180, 187`
- Modify: `docs/ROZDILY.md:19, 67, 200`
- Modify: `docs/VYVOJ-MAC.md:76`
- Modify: `windows/README.md:23, 399`
- Modify: `windows/package/navod/navod.html:197, 226-228, 235-236, 246-249, 619-621, 631-632, 663-664`
- Test: `windows/tests/Offload.Core.Tests/FormatTests.cs:24`
- Test: `windows/tests/Offload.Core.Tests/ReportTests.cs:28`
- Test: `windows/tests/Offload.Core.Tests/HtmlReportTests.cs:18`

**Interfaces:**
- Consumes: —
- Produces: —

- [ ] **Step 1: Napsat padající testy**

#### `windows/tests/Offload.Core.Tests/FormatTests.cs`

Najít (1/1, řádek 24):

```csharp
Assert.Equal("0.5.2-win", OffloadVersion.String);
```

Nahradit:

```csharp
Assert.Equal("0.6.0-win", OffloadVersion.String);
```

#### `windows/tests/Offload.Core.Tests/ReportTests.cs`

Najít (1/1, řádek 28):

```csharp
Assert.Equal("0.5.2-win", root.GetProperty("version").GetString());
```

Nahradit:

```csharp
Assert.Equal("0.6.0-win", root.GetProperty("version").GetString());
```

#### `windows/tests/Offload.Core.Tests/HtmlReportTests.cs`

Najít (1/1, řádek 18):

```csharp
Assert.Contains("offload 0.5.2-win", html);
```

Nahradit:

```csharp
Assert.Contains("offload 0.6.0-win", html);
```

- [ ] **Step 2: Spustit testy — musí selhat**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~FormatTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HtmlReportTests"`

Expected: build projde, testy FAIL: `FormatTests.Version_IsWindowsBuild`, `ReportTests.Json_NestedShapes`, `HtmlReportTests.Verified_ShowsStatusFilesAndEscapes` — první: „Assert.Equal() Failure: Strings differ“.

- [ ] **Step 3: Implementace**

#### `windows/Directory.Build.props`

Najít (1/1, řádky 8–9):

```xml
    <Version>0.5.2</Version>
    <InformationalVersion>0.5.2-win</InformationalVersion>
```

Nahradit:

```xml
    <Version>0.6.0</Version>
    <InformationalVersion>0.6.0-win</InformationalVersion>
```

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/2, řádek 22):

```markdown
| 9 | nový vzhled podle `docs/ui-navrh/`: tmavé a světlé téma, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, pozastavení, oprava nesouhlasných souborů, dnešní historie, okno Nastavení (0.5.0) | hotovo (čeká ruční test 40–49 s kartou a vizuální kontrolu proti `docs/ui-navrh/screeny`) |
```

Nahradit:

```markdown
| 9 | nový vzhled podle `docs/ui-navrh/`: tmavé a světlé téma, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, pozastavení, oprava nesouhlasných souborů, dnešní historie, okno Nastavení (0.5.0) | hotovo (čeká ruční test 40–49 s kartou a vizuální kontrolu proti `docs/ui-navrh/screeny`) |
| 10 | Verify-in-Proxy: primární disk ověřený čtením pro proxy (HTTP tap pro ffmpeg, čtení přes cache pro RAW SDK), ostatní disky na pozadí, „Lze vyjmout“ × „Lze formátovat“, blokový manifest a navázání po pádu, XXH128 v ASC MHL, obsahová kontrola dekodérem (0.6.0) | hotovo (čeká ruční test 50–58: karta, RAW, přímý zápis na zařízení) |
```

Najít (2/2, řádky 26–27):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

Verze **0.6.0**. Návrh: [`windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md`](../windows/docs/superpowers/specs/2026-10-08-offload-verify-in-proxy-design.md)
(specifikace uživatele + rozhodnutí R1–R12). S proxy a ověřením se primární disk (nejrychlejší při kopii, nebo
zvolený) už nečte zvlášť: ffmpeg čte klip přes HTTP server Offloadu na 127.0.0.1 (`-short_seek_size 65536`, jinak ffmpeg
8.1 četl MOV s `moov` na konci i MXF dvakrát), RAW SDK z cache, kterou Offload naplní jedním čtením; každý přečtený blok
16 MiB se porovná s XXH3-64 z karty, chybějící bloky dočte dorovnání, ostatní disky se čtou souběžně na pozadí.
Naměřeno (ffmpeg 8.1, NVMe, 735 MB ProRes s `moov` na konci): tap přečetl 101,8 % souboru, dekódování přes tap
1,63 s proti 1,55 s z disku. Karta má dva stavy: **Lze vyjmout** (vše zapsané) a **Lze formátovat** (vše ověřené). Zápis
`NO_BUFFERING | WRITE_THROUGH`, `_REPORT/offload-manifest.jsonl`, XXH128 v ASC MHL vedle xxh64. Klasické zpětné čtení
dál: bez proxy, `--classic-verify`, vypnutá volba, síťový cíl, cíl, který neprošel kontrolou čtení mimo cache.

```

#### `docs/FORMATY.md`

Najít (1/8, řádek 26):

```markdown
        offload_report_0002.json      jen po doplnění (generace 2, 3 …): report jen nových souborů
```

Nahradit:

```markdown
        offload-manifest.jsonl        blokový manifest (Windows od 0.6.0) — viz „Blokový manifest“
        offload_report_0002.json      jen po doplnění (generace 2, 3 …): report jen nových souborů
```

Najít (2/8, řádek 98):

```markdown
| `version` | string | `0.1.0` (Mac), `0.5.1-win` (Windows) |
```

Nahradit:

```markdown
| `version` | string | `0.1.0` (Mac), `0.6.0-win` (Windows) |
```

Najít (3/8, řádek 108):

```markdown
| `verified` | bool | proběhlo ověření zpětným čtením |
```

Nahradit:

```markdown
| `verified` | bool | proběhlo ověření (Windows od 0.6.0 i během proxy — metoda v `destinations[].verifyMethod`) |
| `verifyPending` | bool? | jen Windows od 0.6.0: kopie je hotová a ověřuje se s proxy (`verified` je zatím `false`, stavy `written`); report s tímto klíčem ještě **nemá generaci ASC MHL** — zapíše se až po ověření. Po pádu aplikace ho dokončí doplnění (viz „Blokový manifest“). Mac ignoruje |
| `safeToFormatSeconds` | číslo? | jen Windows od 0.6.0: sekundy od startu do chvíle, kdy byl každý soubor ověřený na každém cíli (bez pauz); chybí, když to nenastalo |
| `verifyNotes[]` | string? | jen Windows od 0.6.0: česky — nestabilní čtení (blok se při opakovaném čtení lišil), stejný blok špatně na více cílech a co dává karta při novém čtení |
```

Najít (4/8, řádek 113):

```markdown
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows) |
```

Nahradit:

```markdown
| `destinations[]` | objekt | `root`, `cardPath`, `filesystem`, `filesOK`, `filesFailed`, volitelně `unbuffered` (jen `false`, jen Windows); Windows od 0.6.0 `verifyMethod` (`verify-in-proxy` = čtením pro proxy a dorovnáním, `read-back` = zpětným čtením; chybí bez ověření), `verifyNote` (česky, proč zpětným čtením, i když běželo ověření během proxy) a `volumeSerial` (`1A2B-3C4D`, sériové číslo svazku cíle — podle něj se cíl pozná i pod jiným písmenem, R12) |
```

Najít (5/8, řádek 114):

```markdown
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `md5`? (32 malých hex, jen s MD5, Windows od 0.3.0),
```

Nahradit:

```markdown
| `files[]` | objekt | `path`, `size`, `modified`, `xxh64`?, `xxh128`? (XXH3-128, 32 malých hex, Windows od 0.6.0), `md5`? (32 malých hex, jen s MD5, Windows od 0.3.0),
```

Najít (6/8, řádek 116):

```markdown
`proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje) |
```

Nahradit:

```markdown
`proxyNote`?, `proxySeconds`?, `lut`? {`source`, `id`, `name`, `reason`} (použitý LUT a proč; Windows od plánu 5, Mac zatím ignoruje), `content`? {`state` (`ok` / `warn`), `kind` (`decode`, `timestamps`, `frames`, `end`), `frame`, `timecode`, `message`, `framesDecoded`, `framesExpected`} (Windows od 0.6.0: co dekodér hlásil při tvorbě proxy — `warn` je vada souboru z kamery, kopie přitom souhlasí s kartou; Mac ignoruje) |
```

Najít (7/8, řádek 180):

```markdown
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.
```

Nahradit:

```markdown
- Ověřitelné nástrojem `ascmhl verify <složka karty>`.
- Windows od 0.6.0: každý `<hash>` má i `<xxh128 action=… hashdate=…>` (XXH3-128, R2) — `<xxh64>` zůstává, takže zálohu
  ověří i Mac a starší Offload. Do generace jde jen soubor ověřený na daném cíli (`original`) nebo selhaný (`failed`);
  zapsaný a neověřený jen v generaci bez ověření nebo zrušené před ním. Metoda ověření toho cíle je na konci
  `<creatorinfo><comment>` (`· ověření: verify-in-proxy` / `· ověření: read-back`) — `<process>` zůstává `transfer`,
  schéma ASC MHL v2 jiné hodnoty nepovoluje. S ověřením během proxy se generace zapíše až po ověření (`verifyPending`).
```

Najít (8/8, řádek 187):

```markdown
### Doplnění (Windows od 0.2.0, Mac naplánováno)
```

Nahradit:

```markdown
## Blokový manifest — `_REPORT/offload-manifest.jsonl` (Windows od 0.6.0, Mac ignoruje)

Na každém cíli, **jen připisovaný** (řádek se zapíše přes `WRITE_THROUGH` a flush, nic v souboru se nepřepisuje; řádek
přerušený pádem se při čtení přeskočí a další zápis začne na novém řádku). Jeden JSON objekt na řádek:

| Klíč | Význam |
|---|---|
| `v` | verze formátu, `1` |
| `kind` | `file` = soubor je na svém místě na tomto cíli; `part` = kontrolní bod souboru, který se zapisuje pod `.<jméno>.offload-part` |
| `gen` | generace ASC MHL, kterou kopie psala |
| `path`, `size`, `modified` | cesta na kartě (`/`), velikost a čas souboru na kartě (UTC na sekundy) |
| `xxh64`, `xxh128`, `md5`? | jen `file`: součty karty (`md5` jen s MD5) |
| `blockSize` | velikost bloku (16 MiB = blok kopie) |
| `blocks[]` | XXH3-64 bloků (16 malých hex): u `file` všech, u `part` bloků od `first` |
| `first` | jen `part`: index prvního bloku v `blocks` (záznam s `first` 0 začíná nový pokus) |

Kontrolní bod se zapíše po každých 64 blocích (1 GiB) souboru zapisovaného mimo cache — ty bloky jsou už na disku.
Navázání (R6): doplnění (`offload append`, v GUI „Dokončit přerušenou zálohu“) do složky, jejíž generace N není
v řetězu ASC MHL (nemá report vůbec, nebo jen s `verifyPending`) a manifest ji zná, dokončí **tutéž** generaci N:
soubor na místě se záznamem `file` na všech cílech se znovu nečte, soubor na místě bez záznamu se z karty jen
přepočítá, rozepsaný `.offload-part` pokračuje od společného kontrolního bodu (bloky se z disku přečtou a znovu porovnají),
zbytek se zkopíruje; pak ověření a zápis generace jako u každé kopie. Reporty generace N z přerušeného běhu se přitom
nahradí (ještě nebyly potvrzené). Oprava („Zkopírovat znovu“) takovou zálohu odmítne, dokud ji doplnění nedokončí.

### Doplnění (Windows od 0.2.0, Mac naplánováno)
```

#### `docs/ROZDILY.md`

Najít (1/3, řádek 19):

```markdown
| Verze | 0.1.0 | 0.5.1-win (ve vývoji) |
```

Nahradit:

```markdown
| Verze | 0.1.0 | 0.6.0-win (ve vývoji) |
```

Najít (2/3, řádek 67):

```markdown
| **Bez proxy, když je originál lehčí** (`proxy.skipLighterSources`, `--proxy-always`) | ⏳ naplánováno (renderuje vše) | ✅ CLI + GUI 0.5.1 |
```

Nahradit:

```markdown
| **Bez proxy, když je originál lehčí** (`proxy.skipLighterSources`, `--proxy-always`) | ⏳ naplánováno (renderuje vše) | ✅ CLI + GUI 0.5.1 |
| **Verify-in-Proxy** (primární disk ověřený čtením pro proxy, ostatní na pozadí; `--classic-verify`, `--primary`) | ⏳ naplánováno (zpětné čtení) | ✅ CLI + GUI 0.6.0 |
| **Lze vyjmout × Lze formátovat** (dva stavy karty, oznámení „hotovo“ až po ověření všech disků) | ⏳ naplánováno („kartu lze vysunout“ po ověření) | ✅ GUI + CLI 0.6.0 |
| **XXH128 v ASC MHL** (vedle xxh64) a `files[].xxh128` | ⏳ naplánováno (xxh64) | ✅ 0.6.0 |
| **Blokový manifest a navázání po pádu** (`offload-manifest.jsonl`, „Dokončit přerušenou zálohu“) | ⏳ naplánováno | ✅ 0.6.0 |
| **Obsahová kontrola dekodérem** (`clips[].content`, vady s timecodem a náhledem) | ⏳ naplánováno | ✅ 0.6.0 |
```

Najít (3/3, řádek 200):

```markdown
- **Odkaz na proxy v HTML reportu** (Windows od 0.5.1): stav „hotovo“ odkazuje na soubor proxy (`../_PROXY/…`).
```

Nahradit:

```markdown
- **Odkaz na proxy v HTML reportu** (Windows od 0.5.1): stav „hotovo“ odkazuje na soubor proxy (`../_PROXY/…`).
- **Verify-in-Proxy** (Windows od 0.6.0): s proxy a ověřením se primární disk ověří z čtení pro proxy, ostatní disky na
  pozadí; report ze stage 1 má `verifyPending` a generace ASC MHL se zapíše až po ověření. Mac klíče `verifyPending`,
  `verifyMethod`, `volumeSerial`, `safeToFormatSeconds`, `verifyNotes`, `xxh128`, `content` ignoruje; záloha z Windows se
  na Macu ověří z `<karta>.xxh64` nebo xxh64 v ASC MHL jako dřív. Report s `verifyPending` (aplikace spadla během ověření)
  Mac považuje za neověřený.
- **Lze vyjmout × Lze formátovat** (Windows od 0.6.0): „kartu lze vysunout“ se rozdělilo — vyjmout po zapsání, formátovat
  až po ověření všech disků; Mac zatím hlásí jen „kartu lze vysunout“ po zpětném čtení.
```

#### `docs/VYVOJ-MAC.md`

Najít (1/1, řádek 76):

```markdown
- **Převzít z Windows 0.5.1 — proxy (2026-10-08):**
```

Nahradit:

```markdown
- **Převzít z Windows 0.6.0 — Verify-in-Proxy (2026-10-08, R10: Mac jen záznam):** primární cíl ověřený čtením pro proxy
  (na Macu AVFoundation/`AVAssetReader` s vlastním `AVAssetResourceLoaderDelegate` místo HTTP tapu, nebo jako na Windows
  lokální HTTP server pro ffmpeg), ostatní cíle na pozadí, zápis `F_NOCACHE` a `F_FULLFSYNC`, `offload-manifest.jsonl`,
  dva stavy karty „Lze vyjmout“ / „Lze formátovat“, `xxh128` v ASC MHL vedle `xxh64`, `clips[].content`. Formát ve
  [FORMATY.md](FORMATY.md) („Blokový manifest“, `offload_report.json`), rozdíly v [ROZDILY.md](ROZDILY.md).
- **Převzít z Windows 0.5.1 — proxy (2026-10-08):**
```

#### `windows/README.md`

Najít (1/2, řádek 23):

```markdown
| Nový vzhled (plán 9, 0.5.0, návrh `docs/ui-navrh/`): tmavé i světlé téma podle Windows, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, Pozastavit, Zkopírovat znovu (oprava), dnes a historie karet, stránka Nastavení, štítek kamery a připojení, odhad času | hotovo (čeká ruční test 40–49 s kartou; snímky ukázkového režimu porovnané s `docs/ui-navrh/screeny`) |
```

Nahradit:

```markdown
| Nový vzhled (plán 9, 0.5.0, návrh `docs/ui-navrh/`): tmavé i světlé téma podle Windows, horní lišta, kroky 1–3, karty úloh s průběhem po cílech, Pozastavit, Zkopírovat znovu (oprava), dnes a historie karet, stránka Nastavení, štítek kamery a připojení, odhad času | hotovo (čeká ruční test 40–49 s kartou; snímky ukázkového režimu porovnané s `docs/ui-navrh/screeny`) |
| Verify-in-Proxy (plán 10, 0.6.0): ověření primárního disku čtením pro proxy, ostatní na pozadí, „Lze vyjmout“ × „Lze formátovat“, blokový manifest a navázání po pádu, XXH128, obsahová kontrola | hotovo (čeká ruční test 50–58) |
```

Najít (2/2, řádek 399):

```markdown
      se nic nezměnilo (nastavení, historie ani log), složka dat je `%TEMP%\Offload-demo-…`; bez `--demo` se okno chová jako dřív
```

Nahradit:

```markdown
      se nic nezměnilo (nastavení, historie ani log), složka dat je `%TEMP%\Offload-demo-…`; bez `--demo` se okno chová jako dřív
- [ ] 50. **Verify-in-Proxy na kartě (0.6.0):** karta s MOV/MP4/MXF, proxy zapnuté, 2 disky (NVMe + USB HDD) → po kopii
      čipy „Lze vyjmout“ zeleně a „Ověřuje se…“; Správce úloh: disk primárního cíle (nejrychlejší) se během proxy čte jen
      jednou (Monitor prostředků: čtení ≈ velikost klipů), druhý disk se čte souběžně; po proxy krátce „Dokončení ověření“,
      pak „Lze formátovat“ zeleně a oznámení „… — kartu lze formátovat“; report: Ověření „během proxy“ u primárního cíle,
      „zpětné čtení“ u druhého, sériová čísla svazků, „do „lze formátovat““
- [ ] 51. **Čas proti samotné proxy (spec 11):** stejná karta jednou s `offload copy --proxy --classic-verify` a jednou
      bez → čas do „Lze formátovat“ (`safeToFormatSeconds`) s jedním cílem nejvýš o 5 % delší než render proxy samotný;
      se dvěma cíli proxy nezpomalí o víc než 5 %
- [ ] 52. **RAW (režim B):** karta BRAW (a R3D/ARRIRAW, když je nástroj) → Monitor prostředků: soubor klipu se z disku
      přečte nejvýš 1,05× (Offload ho přečte, SDK ho má z paměti), report „během proxy“; klip větší než polovina volné RAM
      → ověří ho dorovnání zpětným čtením (report pořád správně)
- [ ] 53. **Test cache s přímým zápisem na zařízení (vyžaduje správce, spec 11):** VHDX 4 GB (Správa disků → Vytvořit
      VHD), NTFS, jako druhý cíl; po kopii (čip „Lze vyjmout“) zapsat jeden bajt doprostřed klipu **přímo na zařízení**
      (`\\.\PhysicalDriveN`, HxD → Otevřít disk, bez souborového systému, takže cache Windows o změně neví) → cíl na VHDX
      musí skončit „nesouhlasí blok …“, žádné „Lze formátovat“; totéž pro primární cíl (změna před proxy)
- [ ] 54. **Vada obsahu:** klip z kamery s poškozeným snímkem (nebo uříznutý MP4) → kopie „ověřeno“, žlutý štítek vady
      s timecodem, Náhled otevře obrázek místa; „Lze formátovat po kontrole vad“, po „Zkontrolováno — kartu lze formátovat“ zeleně
- [ ] 55. **Pád během kopie:** velká karta, během kopie velkého klipu ukončit Offload ve Správci úloh → znovu spustit,
      vložit kartu → řádek „Nedokončená záloha … lze ji dokončit“ a zaškrtávátko „Dokončit přerušenou zálohu“; Start →
      dokončené soubory se znovu nečtou (Monitor prostředků: karta se čte jen pro zbytek), rozepsaný klip pokračuje, výsledek
      ověřený, `ascmhl verify` projde
- [ ] 56. **Pád během ověření:** s proxy zavřít Offload ve Správci úloh během „Ověřuje se…“ → report `verifyPending`,
      denní report „OVĚŘUJE SE“; Doplnit/Dokončit → karta se nečte, disky se ověří a generace se zapíše
- [ ] 57. **Odpojený disk během ověření:** během proxy odpojit druhý disk → jeho soubory „✕ čtení“, „Neformátovat“,
      primární cíl ověřený; po připojení Zkopírovat znovu → přečte a ověří, „Lze formátovat“
- [ ] 58. **Nastavení a CLI:** Nastavení → Ověření → vypnout „Ověřit primární disk během proxy“ → ověření zase hned po
      kopii; `offload copy … --primary 2` → report „během proxy“ u cíle 2; síťový cíl (NAS) → „síťový cíl — ověřeno
      zpětným čtením“
```

#### `windows/package/navod/navod.html`

Najít (1/7, řádek 197):

```html
    Verze aplikace 0.5.2-win · Windows 10 a 11 (64bitové) · přenosná verze bez instalace<br>
```

Nahradit:

```html
    Verze aplikace 0.6.0-win · Windows 10 a 11 (64bitové) · přenosná verze bez instalace<br>
```

Najít (2/7, řádky 226–228):

```html
<p>Offload zálohuje kamerové karty. Kartu přečte <b>jen jednou</b> a každý blok dat zapíše zároveň na všechny
zvolené cílové disky. Potom kopie na každém disku přečte zpátky a porovná kontrolní součty. Teprve když všechno
sedí, řekne, že kartu lze vysunout.</p>
```

Nahradit:

```html
<p>Offload zálohuje kamerové karty. Kartu přečte <b>jen jednou</b> a každý blok dat zapíše zároveň na všechny
zvolené cílové disky. Potom kopie na každém disku přečte a porovná kontrolní součty — s proxy (od 0.6.0) primární
disk při tvorbě proxy, bez druhého čtení navíc. Karta má dva stavy: <b>Lze vyjmout</b>, jakmile je všechno zapsané,
a <b>Lze formátovat</b>, až když je všechno ověřené na všech discích.</p>
```

Najít (3/7, řádky 235–236):

```html
  <li><b>Ověření zpětným čtením</b>: každý soubor se z každého cílového disku přečte znovu a jeho kontrolní součet
    xxHash64 se porovná se součtem spočítaným při čtení karty. Data se čtou z disku, ne z paměti počítače.</li>
```

Nahradit:

```html
  <li><b>Ověření</b>: každý soubor se z každého cílového disku přečte a jeho bloky se porovnají se součty spočítanými při
    čtení karty. Data se čtou z disku, ne z paměti počítače (zápis i čtení obcházejí cache Windows). S proxy se primární
    disk ověří během proxy — ffmpeg čte klipy přes Offload a každý přečtený blok se ověří; ostatní disky se čtou souběžně
    na pozadí. Bez proxy, nebo když to cíl neumožní (síťový disk), se každý disk přečte znovu hned po kopii.</li>
```

Najít (4/7, řádky 246–249):

```html
<div class="box tip"><span class="t">Kartu lze vysunout dřív, než doběhnou proxy</span>
Jakmile jsou všechny soubory na všech discích a ověřené, karta úlohy zezelená s textem
<span class="q">„Všechny soubory jsou na 2 discích a ověřené — kartu lze vysunout.“</span> Analýza klipů a proxy už čtou
jen z cílových disků, takže mezitím můžete vložit další kartu.</div>
```

Nahradit:

```html
<div class="box tip"><span class="t">Lze vyjmout × Lze formátovat</span>
Jakmile jsou všechny soubory zapsané na všech discích, karta úlohy ukáže zelený čip <b>Lze vyjmout</b> a text
<span class="q">„Všechny soubory jsou na 2 discích — kartu lze vyjmout. Formátovat až po ověření.“</span> Analýza klipů
a proxy už čtou jen z cílových disků, takže mezitím můžete vložit další kartu. Kartu ale <b>neformátujte</b>, dokud
druhý čip neřekne <b>Lze formátovat</b> — teprve pak je všechno ověřené. Když dekodér při proxy najde vadu v souboru
z kamery (kopie přitom souhlasí s kartou), ukáže žlutý štítek s timecodem a <b class="ui">Náhled</b>; formátovat jde až po
<b class="ui">Zkontrolováno — kartu lze formátovat</b>.</div>
```

Najít (5/7, řádky 619–621):

```html
  <li><b>Počkejte na zelenou kartu</b> <span class="txt-ok">„A001 — kartu lze vysunout“</span> s větou
    <span class="q">„Všech 24 souborů je na 2 discích a ověřených (xxHash64).“</span> Současně zazní zvuk. Teprve teď je karta
    bezpečně zálohovaná.</li>
```

Nahradit:

```html
  <li><b>Počkejte na čip Lze vyjmout</b> — všechno je zapsané, zazní zvuk a kartu můžete vyjmout. Zelená karta
    <span class="txt-ok">„A001 — kartu lze formátovat“</span> s větou <span class="q">„Všech 24 souborů je na 2 discích
    a ověřených (xxHash64).“</span> přijde až po ověření všech disků. Teprve teď je karta bezpečně zálohovaná.</li>
```

Najít (6/7, řádky 631–632):

```html
Dokud karta úlohy není zelená s textem „kartu lze vysunout“, kartu ani cílové disky neodpojujte. Pokud se úloha zruší nebo některý
soubor selže, karta <b>není</b> zálohovaná (kapitola 8).</div>
```

Nahradit:

```html
Dokud karta úlohy neukáže čip „Lze vyjmout“, kartu neodpojujte; cílové disky neodpojujte, dokud neukáže „Lze formátovat“.
Kartu neformátujte před zelenou kartou „kartu lze formátovat“. Pokud se úloha zruší nebo některý soubor selže, karta <b>není</b>
zálohovaná (kapitola 8). Když aplikace spadne nebo se PC vypne uprostřed kopie, po vložení karty nabídne
<b class="ui">Dokončit přerušenou zálohu</b> — co už je na discích, se znovu nekopíruje.</div>
```

Najít (7/7, řádky 663–664):

```html
      report má štítek <b>ZKOPÍROVÁNO — NEOVĚŘENO</b> a karta úlohy je žlutá („zkopírováno bez ověření“), nikdy zelená
      „kartu lze vysunout“.</td></tr>
```

Nahradit:

```html
      report má štítek <b>ZKOPÍROVÁNO — NEOVĚŘENO</b> a karta úlohy je žlutá („zkopírováno bez ověření“), nikdy zelená
      „kartu lze formátovat“.</td></tr>
  <tr><td><b class="ui">Ověřit primární disk během proxy</b></td><td>zapnuto</td>
      <td>S proxy se primární disk ověří čtením pro proxy a ostatní disky souběžně na pozadí — bez dalšího čtení po
      kopii. Primární je nejrychlejší disk změřený při kopii, nebo s volbou <b class="ui">Primární je první cíl</b> první
      cíl. Vypnuto (nebo CLI <code>--classic-verify</code>) = zpětné čtení hned po kopii. Síťový cíl a cíl, který neprojde
      kontrolou čtení mimo cache, se ověří zpětným čtením vždy (důvod je v reportu).</td></tr>
```

- [ ] **Step 4: Spustit testy — projdou, pak build a celá sada**

Run: `dotnet test windows\Offload.sln --filter "FullyQualifiedName~FormatTests|FullyQualifiedName~ReportTests|FullyQualifiedName~HtmlReportTests"`

Expected: PASS — `Failed: 0, Passed: 77, Skipped: 0, Total: 77` (česky `Neúspěšné: 0`). Pak:

```powershell
dotnet build windows\Offload.sln -warnaserror
dotnet test windows\Offload.sln
```

Expected: build `0 Warning(s), 0 Error(s)` (česky `0 upozornění`, `Počet chyb: 0`); celá sada `Failed: 0`, `Total: 1645` (z toho 8 přeskočených). Při jednom selhání časově citlivého testu (seznam v Global Constraints) ho pusťte znovu samostatně a selhání nahlaste.

- [ ] **Step 5: Řádek do `docs/VYVOJ-WINDOWS.md`**

Nový řádek jde nahoru do sekce `## 2026-10-08 — plán 10: Verify-in-Proxy` (nejnovější nahoře, bez SHA).

#### `docs/VYVOJ-WINDOWS.md`

Najít (1/1, řádky 26–27):

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

```

Nahradit:

```markdown
## 2026-10-08 — plán 10: Verify-in-Proxy

- Úkol 18: verze 0.6.0, dokumentace (FORMATY: manifest, klíče reportu, xxh128 a metoda v MHL; ROZDILY; VYVOJ-MAC), README (ruční testy 50–58) a návod.
```

- [ ] **Step 6: Commit**

Zprávu zapište nástrojem Write (UTF-8) do `.git/COMMIT_MSG_PLAN10`:

```text
Windows 0.6.0: Verify-in-Proxy documentation and version (plan 10, task 18)

Version 0.6.0. FORMATY describes the manifest, the new report keys and xxh128/method in the MHL;
ROZDILY, VYVOJ-MAC, the README (manual tests 50-58) and the manual cover verify-in-proxy.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

```powershell
git add docs\FORMATY.md docs\ROZDILY.md docs\VYVOJ-MAC.md docs\VYVOJ-WINDOWS.md windows\Directory.Build.props windows\README.md windows\package\navod\navod.html windows\tests\Offload.Core.Tests\FormatTests.cs windows\tests\Offload.Core.Tests\HtmlReportTests.cs windows\tests\Offload.Core.Tests\ReportTests.cs
git commit -F .git/COMMIT_MSG_PLAN10
git status --short
```

Expected: commit vytvořen; `git status --short` neukazuje nic z tohoto úkolu (nanejvýš `?? windows/docs/superpowers/plans/…` tohoto plánu, pokud ještě není v repu).

---

## Kontrola proti spec (self-review)

**Pokrytí spec → úkol:**

| Spec | Kde |
|---|---|
| 1 Cíle (karta jednou, fan-out, hash za běhu, ověření primárního cíle čtením proxy, ostatní na pozadí, obsah, ASC MHL) | 1, 4, 8, 9, 11, 12, 10, 6 |
| 2 Stavy `copied` / `verified` / `content_ok` / `content_warn` / `failed` / `safe_to_eject` / `safe_to_format` | 6 (report), 10 (`content`), 11 (`WithVerification`), 12 (`JobStatus.SafeToEject` / `SafeToFormat`), 15 (UI) |
| 3 Komponenty: SourceReader, TargetWriter, ProxyQueue, TapReader, BlockVerifier, BackgroundVerifier, StateStore | engine (beze změny), `DestWriter` (4), 2. fáze `OffloadJob` (12), `RangeServer` + `TapFile` (7, 8) / `CacheWarm` (9), `BlockVerifier` (2), `VerifySession` (11), `OffloadReport` + `JobStatus` (6, 12) |
| 4.1–4.3 čtení po 16 MiB, XXH3-64 bloků + XXH128 (+ xxh64), zápis bez cache | 1, 4 |
| 4.4 dočasné jméno, flush, přejmenování | beze změny (`.offload-part`, `FlushFileBuffers`), + `WRITE_THROUGH` (4) |
| 4.5 manifest po každém souboru | 3, 4 |
| 4 pořadí souborů, proxy hned po `copied` | Rozhodnutí: 2. fáze po kopii |
| 4 navázání po přerušení | 13 (R6) |
| 5 režim A (seek, kusy bloků, opakované čtení = chyba I/O), režim B, registr kodeků, rozhraní BlockVerifier | 2, 7, 8, 9 |
| 6 dorovnání, sekundární cíle (vlákno na svazek, nízká priorita I/O), soubory bez proxy, proxy vypnuté → klasicky, nejrychlejší primární + volba | 8 (`BlockReads`), 11, 12, 16 |
| 7 obsah (chyby dekódování, časové značky, počet snímků, předčasný konec, TC, potvrzení, zvuk) | 10, 12, 15 |
| 8 `NO_BUFFERING`, zarovnání, vyhození z cache, `WRITE_THROUGH` ve fázi 1, kontrola za běhu | 4, 5, 8, 9 |
| 9 ASC MHL (`xxh128` + `xxh64`, jen ověřené, metoda), report (čas do `safe_to_format`, metoda po cílech, `content_warn`, `failed`), UI (dva stavy, štítek vady s náhledem) | 1, 6, 15, 17 |
| 10 chybové stavy (nesoulad → znovu bez cache, víc cílů → karta znovu, proxy spadne → dorovnání, zrušení, odpojený cíl, vyjmutá karta, kontrola cache selže, pád aplikace) | 8, 11, 12, 13, 5 |
| 11 testy správnosti (převrácený bit A i B, v nečteném bloku, zkrácený soubor, test cache, náhodné pořadí, vadný soubor z kamery, přerušení) | 8, 9, 12, 2, 10, 13; zápis na zařízení = ruční test 53 |
| 11 výkon (≤ 5 % proti samotné proxy, B ≤ 1,05 × soubor) | naměřeno (Ověřeno před plánem), `CacheWarmTests.Warm_VerifiesInOneRead_AtMostTheFileSize`; na kartě ruční testy 51 a 52 |
| R1 mapování na kód, žádný druhý engine | všechny úkoly (`Offload.Core/VerifyInProxy/`) |
| R2 xxh128 + xxh64, MD5 dál, `.xxh64` zůstává | 1 |
| R3 HTTP tap 127.0.0.1, token, `Range` | 7, 8 |
| R4 `_REPORT/offload-manifest.jsonl`, FORMATY | 3, 18 |
| R5 dva verdikty, zelená jen `SafeToFormat`, oznámení „hotovo“ až po ověření | 12, 14, 15 |
| R6 navázání přes doplnění | 13 |
| R7 nic se nepřepisuje | 11, 12 (nesoulad → `failed`), 4 (manifest jen připisovaný) |
| R8 zápis bez cache, síťové a neprošlé cíle klasicky, oříznutí posledního bloku | 4, 5 |
| R9 obsah ze stderr a počtu snímků, bez `-xerror` | 10 |
| R10 rozsah v1 | 8, 9 (registr), 18 (VYVOJ-MAC) |
| R11 výchozí zapnuto, Nastavení → Ověření, `--classic-verify` | 12, 16 |
| R12 sériové číslo svazku v reportu a v „ověřeno“ | 5, 6, 14 |

**Placeholdery:** v plánu nejsou „TBD“, „TODO“, „doplnit“ ani „podobně jako úkol N“; každý krok s kódem má celý kód (nové soubory celé, změny jako Najít/Nahradit s čísly řádků) a každý krok s příkazem má příkaz a očekávaný výsledek.

**Konzistence typů:** jména a signatury z bloků **Interfaces** jsou ta, která kód používá: `BlockVerifier`/`BlockManifest`/`BlockOutcome` (2) → `TapFile`, `BlockReads`, `CacheWarm`, `VerifySession`; `ManifestRecord`/`PartCheckpoint`/`ManifestFile` (3) → `DestWriter`, `OffloadEngine`, `ResumePlanner`; `OffloadReport.SafeToFormat`/`Commit`/`VerifyPending` (6) → `OffloadJob`, `CardRecord`, `DayReport`; `JobStatus.SafeToEject`/`SafeToFormat`/`ContentWarnings` (12) → `JobCard`, `JobOutcome`, App; `JobConfig.VerifyInProxy`/`PrimaryDestination` (12) → `JobConfigs`, CLI (16). Ověřeno sestavením každého úkolu s `-warnaserror` v kopii repa.

**Review Focus:** body 1–8 mají každý aspoň jeden test v úkolu, který vlastní kód (seznam v prvním souboru).

**Co zbývá ručně (README, ruční testy 50–58):** skutečná karta a disky (50), čas proti samotné proxy (51), RAW s Monitorem prostředků (52), zápis přímo na zařízení (53), vada obsahu z kamery (54), pád během kopie (55) a ověření (56), odpojený disk (57), nastavení a CLI (58).
