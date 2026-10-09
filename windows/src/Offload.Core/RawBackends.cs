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
    /// `singleFrameAt` (seconds) asks for just one frame — used for report thumbnails. `isCancelled`: a decoder that writes an
    /// intermediate first (art-cmd, REDline) is killed when it turns true.
    FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt,
        Func<bool>? isCancelled = null);
}

/// What a RAW decoder should hand ffmpeg: camera log (a LUT follows) or its own Rec.709 look.
/// `ArriColorSpace` is the ARRI target space matching the LUT (null = from the clip's metadata).
/// BRAW and Canon ignore it (BRAW always decodes in the clip's gamma).
internal sealed record RawLook(bool Log, string? ArriColorSpace);

/// Vendor decoders for formats ffmpeg cannot read. When a LUT is applied they hand ffmpeg the camera's log
/// (RawLook.Log; BRAW always decodes in the clip's gamma), so the same LUT / scale / burn-in chain fits every
/// camera; without a LUT, RED and ARRI deliver their own Rec.709 look.
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

    public static void DeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// A RAW tool's log lines that explain a failure, on one line: its [error]/[critical] lines (else the last three),
    /// without the timestamp and logger prefix ("[2026-10-05 10:00:00.000] [art] [error] …" → "[error] …").
    public static string LogTail(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.TrimEntries)
            .Select(s => System.Text.RegularExpressions.Regex.Replace(s, @"^\[[^\]]*\]\s*\[[^\]]*\]\s*(?=\[)", ""))
            .Select(s => s.Length > 300 ? s[..300] : s)
            .Where(s => s.Length > 0).ToList();
        var errors = lines.Where(s => s.StartsWith("[error]", StringComparison.Ordinal) || s.StartsWith("[critical]", StringComparison.Ordinal)).ToList();
        return string.Join(" | ", errors.Count > 0 ? errors : lines.TakeLast(3));
    }

    /// A RAW tool that ended with exit code 0 but wrote no output (art-cmd does that when it refuses an argument): never a
    /// silent "no proxy" — the clip's note says so in Czech, the tool's own words (`log`, LogTail) go with it and to the log.
    public static RawDecoderException NoOutput(string format, string tool, string log) =>
        new($"{format}: {tool} skončil bez chyby, ale nic nezapsal — proxy nevznikla" + (log.Length > 0 ? $" ({log})" : ""),
            new ToolError(tool, 0, "no output file" + (log.Length > 0 ? ": " + log : "")));
}

/// A RAW decoder's error turned into a note for the clip. The decoder's own error (with its stderr) is the inner
/// exception; ProxyRenderer writes it to the job's log.
internal sealed class RawDecoderException(string note, ToolError decoderError) : Exception(note, decoderError)
{
    public ToolError DecoderError => decoderError;
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
    [property: JsonPropertyName("camera")] string Camera,
    [property: JsonPropertyName("gamma")] string? Gamma = null,
    [property: JsonPropertyName("gamut")] string? Gamut = null,
    [property: JsonPropertyName("colorScienceGen")] int? ColorScienceGen = null,
    [property: JsonPropertyName("colorScienceGenOriginal")] int? ColorScienceGenOriginal = null);

internal sealed class BrawBackend : IRawBackend
{
    public ClipFormat Format => ClipFormat.Braw;
    public string MissingHint => "Blackmagic RAW: chybí braw-decode nebo knihovna Blackmagic RAW (SDK, Player nebo DaVinci Resolve)";

    /// How long `braw-decode --info` may take: opening a clip is quick; a hung SDK must never hang the job (the clip
    /// then gets a note instead).
    public static readonly TimeSpan DefaultInfoTimeout = TimeSpan.FromSeconds(60);

    readonly Func<string, string?> _findLibrary;
    readonly TimeSpan _infoTimeout;
    readonly bool _gen5;
    /// M11 (b): the decoder answered --gen5 with a usage error (an older braw-decode the user pinned) — from then on it
    /// runs without it, so BRAW clips still get proxies (Film Gen 4 then without the Gen 5 LUT).
    volatile bool _gen5Rejected;

    /// `findLibrary` (decoder folder → library path) is injectable so tests do not depend on what is installed on the PC;
    /// `infoTimeout` so tests need not wait a minute. `gen5` (proxy.brawGen4ToGen5, spec 4.2): braw-decode gets --gen5.
    public BrawBackend(Func<string, string?>? findLibrary = null, TimeSpan? infoTimeout = null, bool gen5 = false)
    {
        _findLibrary = findLibrary ?? BrawLibrary.Find;
        _infoTimeout = infoTimeout ?? DefaultInfoTimeout;
        _gen5 = gen5;
    }

    /// Where the warning about a decoder without --gen5 goes (the job's log).
    internal Action<string>? Log { get; init; }

    /// --gen5 first, as the decoder's usage line lists it.
    List<string> Args(params string[] rest) => _gen5 && !_gen5Rejected ? ["--gen5", .. rest] : [.. rest];

    /// braw-decode alone is not enough: without BlackmagicRawAPI.dll it can only fail.
    public bool IsAvailable(ExternalTools tools) =>
        tools.BrawDecoder is string exe && _findLibrary(Path.GetDirectoryName(exe) ?? "") != null;

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
        Gamma = string.IsNullOrEmpty(i.Gamma) ? null : i.Gamma,
        Gamut = string.IsNullOrEmpty(i.Gamut) ? null : i.Gamut,
        ColorScienceGen = i.ColorScienceGen,
        ColorScienceGenOriginal = i.ColorScienceGenOriginal,
    };

    /// The frame braw-decode starts at for a thumbnail `seconds` into the clip. Rounded: `seconds` comes from the
    /// exact rational rate (24000/1001), braw-decode reports the SDK's float (23.976) — truncating lands a frame early.
    public static int StartFrame(double seconds, double fps, int frames)
    {
        double rate = fps > 0 ? fps : 25;
        return Math.Min(Math.Max(0, frames - 1), Math.Max(0, (int)Math.Round(seconds * rate, MidpointRounding.AwayFromZero)));
    }

    /// Smallest decode scale that still covers the target height — decoding less is much faster.
    public static string Scale(int sourceHeight, int target)
    {
        foreach (var (name, div) in new[] { ("eighth", 8), ("quarter", 4), ("half", 2) })
            if (sourceHeight / div >= target) return name;
        return "full";
    }

    BrawInfo Info(string path, string scale, ExternalTools tools)
    {
        string output;
        try
        {
            var args = Args("--scale", scale, path);
            try
            {
                output = ToolProcess.Run(tools.BrawDecoder!, ["--info", .. args], timeout: _infoTimeout);
            }
            catch (ToolError e) when (e.Status == 2 && args[0] == "--gen5")
            {
                // M11 (b): an older braw-decode does not know --gen5 — once, a warning, then on without it.
                if (!_gen5Rejected)
                {
                    _gen5Rejected = true;
                    Log?.Invoke("Blackmagic RAW: braw-decode nezná --gen5 (starší verze) — Film Gen 4 se dekóduje bez převodu na Gen 5");
                }
                output = ToolProcess.Run(tools.BrawDecoder!, ["--info", .. Args("--scale", scale, path)], timeout: _infoTimeout);
            }
        }
        catch (ToolError e)
        {
            throw Explain(e, tools);
        }
        return ParseInfo(output);
    }

    /// braw-decode's exit codes 3 (library unusable), 4 (clip cannot be opened) and 5 (a frame failed) as a note
    /// for the clip; the original error, with braw-decode's stderr, stays inside it for the log. Anything else
    /// (a timeout, a usage error) is returned as it is.
    internal Exception Explain(ToolError e, ExternalTools tools)
    {
        var dll = (tools.BrawDecoder is string exe ? _findLibrary(Path.GetDirectoryName(exe) ?? "") : null) ?? BrawLibrary.DllName;
        var note = e.Status switch
        {
            3 => $"Blackmagic RAW: knihovnu {dll} nelze použít — nainstalujte aktuální Blackmagic RAW Player",
            4 => $"Blackmagic RAW: klip nelze otevřít — je poškozený, nebo je knihovna {dll} starší než kamera (nainstalujte aktuální Blackmagic RAW Player)",
            5 => $"Blackmagic RAW: snímek klipu se nepodařilo dekódovat ({LastLine(e.Detail)})",
            _ => null,
        };
        return note == null ? e : new RawDecoderException(note, e);
    }

    /// braw-decode's last stderr line without its "braw-decode: " prefix.
    static string LastLine(string stderr)
    {
        var line = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        return line.StartsWith("braw-decode: ", StringComparison.Ordinal) ? line["braw-decode: ".Length..] : line;
    }

    public ClipInfo Probe(string path, ExternalTools tools, string workDir) => ToClipInfo(Info(path, "eighth", tools));

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt,
        Func<bool>? isCancelled = null)
    {
        int target = singleFrameAt == null ? settings.Height : 180;
        var scale = Scale(info.Height ?? 2160, target);
        var dims = Info(path, scale, tools);
        var args = Args("--scale", scale);
        if (singleFrameAt is double t)
        {
            int start = StartFrame(t, dims.Fps, dims.Frames);
            args.AddRange(["--start", start.ToString(CultureInfo.InvariantCulture), "--count", "1"]);
        }
        args.Add(path);
        return new FrameSource
        {
            InputArgs = ["-f", "rawvideo", "-pix_fmt", "rgb48le", "-s", $"{dims.Width}x{dims.Height}", "-r", info.FrameRate ?? "25", "-i", "-"],
            FeederExe = tools.BrawDecoder,
            FeederArgs = args,
            AudioFromInput = false,
            FullRange = true,
            ExplainFeederError = e => Explain(e, tools),
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

    public static List<string> Args(string input, string outDir, ClipInfo info, ProxySettings s, RawLook look)
    {
        int h = info.Height ?? 4320;
        var res = h / 4 >= s.Height ? "4" : (h / 2 >= s.Height ? "3" : "1");
        var args = new List<string> { "--i", input, "--outDir", outDir, "--o", "intermediate", "--format", "201", "--PRcodec", "2", "--res", res, "--useMeta" };
        // The LUT expects log: IPP2, REDWideGamutRGB / Log3G10.
        if (look.Log) args.AddRange(["--colorSciVersion", "3", "--colorSpace", "25", "--gammaCurve", "34"]);
        return args;
    }

    public ClipInfo Probe(string path, ExternalTools tools, string workDir) =>
        ParseMeta(ToolProcess.Run(tools.Redline!, ["--i", path, "--printMeta", "1"]));

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt,
        Func<bool>? isCancelled = null)
    {
        // Thumbnails come from the finished proxy instead (one REDline run per clip is enough).
        if (singleFrameAt != null) throw new ToolError("REDline", -1, "thumbnail from proxy");
        var dir = RawBackends.FreshDir(workDir, "r3d");
        try
        {
            var (exit, log) = ToolProcess.RunCombined(tools.Redline!, Args(path, dir, info, settings, look), isCancelled: isCancelled);
            if (exit != 0) throw new ToolError("REDline", exit, RawBackends.LogTail(log));
            var mov = RawBackends.NewestFile(dir, "mov") ?? throw RawBackends.NoOutput("RED R3D", "REDline", RawBackends.LogTail(log));
            return new FrameSource { InputArgs = ["-i", mov], Cleanup = [dir] };
        }
        catch
        {
            RawBackends.DeleteDir(dir);
            throw;
        }
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
        return IsAlexa35(info.Camera) ? "AWG4/D65/LogC4" : "AWG3/D65/LogC3";
    }

    /// "ALEXA 35" / "ALEXA35" as a whole word, not "ALEXA Mini LF 35mm" or a serial number.
    public static bool IsAlexa35(string? camera) =>
        camera != null && System.Text.RegularExpressions.Regex.IsMatch(camera, @"\bALEXA\s*35\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static (List<string> Args, string? ParametersJson, bool Cropped) Args(string input, string output, ClipInfo c, ProxySettings s, RawLook look, string parametersFile)
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
            // --desqueeze only for anamorphic lenses: it halves art-cmd's speed (4.6K Open Gate 25.7 → 11.6 fps on the
            // Mac) and changes nothing for spherical ones. ParseMetadata sets Squeeze only above 1.01.
            if (sq > 1.01) args.Add("--desqueeze");
            args.AddRange(["--output-height", s.Height.ToString(CultureInfo.InvariantCulture)]);
        }
        if (look.Log) args.AddRange(["--target-colorspace", look.ArriColorSpace ?? ColorSpace(null, c)]);
        // No LUT: the camera's embedded look (ARRI's display transform to Rec.709; plain LogC when the clip has none).
        // Never with --target-colorspace: art-cmd refuses that pair for looks without DRT LUTs ("target-colorspace argument
        // is only valid for embedded looks with DRT LUTs") — and exits 0 without writing anything (Mac 3b43352).
        else args.Add("--embedded-look");
        return (args, json, json != null);
    }

    /// Where the note about a full metadata export goes (the job's log).
    internal Action<string>? Log { get; init; }

    /// How long the one-frame metadata export may take: it reads a frame, not the clip; a hung tool must never hang the job.
    internal TimeSpan ExportTimeout { get; init; } = TimeSpan.FromSeconds(120);

    public ClipInfo Probe(string path, ExternalTools tools, string workDir) => Probe(path, tools, workDir, container: null);

    /// The clip's metadata from `art-cmd export` of its first frame only (`--duration 1`): without it art-cmd writes the
    /// metadata of every frame, i.e. reads the whole clip — minutes per 100 GB clip (Mac, card A_0009: analysis over 15
    /// min). An art-cmd that refuses the option gets the full export, as before. The frame count does not depend on what
    /// a one-frame export reports as `clipDuration`: an .ari sequence counts its frames (`segments`, the scanner's), an
    /// MXF takes ffprobe's (`container`). When neither is known and the export said ≤ 1, the length stays unknown — a
    /// one-frame export's 1 may be just that frame, and a wrong length (1 frame, 0.04 s) is worse than none.
    public ClipInfo Probe(string path, ExternalTools tools, string workDir, ClipInfo? container, int? segments = null)
    {
        var dir = RawBackends.FreshDir(workDir, "art-meta");
        try
        {
            var json = Path.Combine(dir, "meta.json");
            List<string> args = ["export", "--input", path, "--output", json, "--skip-audio", "--skip-look"];
            string text;
            bool oneFrame = true;
            // art-cmd writes art.log into its working directory — never let that be the card or a destination. It logs
            // (its errors too) to stdout. A time limit: it reads one frame, and a hung tool must never hang the job.
            var (exit, output) = ToolProcess.RunCombined(tools.ArtCmd!, [.. args, "--duration", "1"], dir, ExportTimeout);
            if (exit == 0 && File.Exists(json)) text = File.ReadAllText(json);
            else if (exit == 0 || RefusesDuration(output))
            {
                // An art-cmd without --duration for export: the whole clip's metadata, slow but known to work. Any other
                // failure (the file is no ARRIRAW, or damaged) would only fail again after reading the whole clip.
                Log?.Invoke($"ARRIRAW {Path.GetFileName(path)}: art-cmd export --duration 1: "
                    + (exit == 0 ? "no metadata written" : RawBackends.LogTail(output)) + " — exporting the metadata of every frame");
                NativeIO.Delete(json);
                (exit, output) = ToolProcess.RunCombined(tools.ArtCmd!, args, dir);
                if (exit != 0) throw new ToolError("art-cmd", exit, RawBackends.LogTail(output));
                if (!File.Exists(json)) throw new ToolError("art-cmd", 0, "no metadata written: " + RawBackends.LogTail(output));
                text = File.ReadAllText(json);
                oneFrame = false;
            }
            else throw new ToolError("art-cmd", exit, RawBackends.LogTail(output));
            var c = ParseMetadata(text);
            if (c.Frames is not > 1)
            {
                if (CountFrames(path, container, c.FrameRate, segments) is int frames)
                {
                    c.Frames = frames;
                    c.Duration = Timecode.Fps(c.FrameRate) is double fps ? frames / fps : null;
                }
                else if (oneFrame)
                {
                    Log?.Invoke($"ARRIRAW {Path.GetFileName(path)}: length unknown — art-cmd export --duration 1 says clipDuration "
                        + $"{c.Frames?.ToString(CultureInfo.InvariantCulture) ?? "(none)"} and ffprobe gave no length");
                    c.Frames = null;
                    c.Duration = null;
                }
            }
            return c;
        }
        finally
        {
            RawBackends.DeleteDir(dir);
        }
    }

    /// art-cmd's words for an option it does not know name it: its error lines (or last lines) mention "duration".
    internal static bool RefusesDuration(string output) =>
        RawBackends.LogTail(output).Contains("duration", StringComparison.OrdinalIgnoreCase);

    /// The clip's frame count without art-cmd: a sequence's frames (its path is the folder) — the scanner's `segments`,
    /// else its .ari files without a Mac's "._" copies — else ffprobe's frames of the MXF, else its duration × the ARRI
    /// frame rate; null when none is known.
    internal static int? CountFrames(string path, ClipInfo? container, string? frameRate, int? segments = null)
    {
        if (Directory.Exists(path))
        {
            int n = segments ?? Directory.EnumerateFiles(path, "*.ari").Count(f => !Path.GetFileName(f).StartsWith("._", StringComparison.Ordinal));
            return n > 0 ? n : null;
        }
        if (container?.Frames is int f && f > 0) return f;
        if (container?.Duration is double d && d > 0 && Timecode.Fps(frameRate ?? container.FrameRate) is double fps)
            return (int)Math.Round(d * fps, MidpointRounding.AwayFromZero);
        return null;
    }

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt,
        Func<bool>? isCancelled = null)
    {
        if (singleFrameAt != null) throw new ToolError("art-cmd", -1, "thumbnail from proxy");
        var dir = RawBackends.FreshDir(workDir, "ari");
        var output = Path.Combine(dir, "intermediate.mxf");
        var parameters = Path.Combine(dir, "parameters.json");
        try
        {
            var (args, json, cropped) = Args(path, output, info, settings, look, parameters);
            if (json != null) File.WriteAllText(parameters, json);
            // art-cmd logs (its errors too) to stdout and may exit 0 without the output file: both cases quote its own lines.
            var (exit, log) = ToolProcess.RunCombined(tools.ArtCmd!, args, dir, isCancelled: isCancelled);
            if (exit != 0) throw new ToolError("art-cmd", exit, RawBackends.LogTail(log));
            if (!File.Exists(output)) throw RawBackends.NoOutput("ARRIRAW", "art-cmd", RawBackends.LogTail(log));
            return new FrameSource { InputArgs = ["-i", output], AlreadyCropped = cropped, Cleanup = [dir] };
        }
        catch
        {
            RawBackends.DeleteDir(dir);
            throw;
        }
    }
}

// MARK: Canon Cinema RAW Light — no command-line decoder exists

internal sealed class CanonRawBackend : IRawBackend
{
    public ClipFormat Format => ClipFormat.CanonRaw;
    public string MissingHint => "Canon Cinema RAW Light: pro Windows neexistuje dekodér pro příkazovou řádku — zkopírováno a ověřeno, proxy přeskočeno";
    public bool IsAvailable(ExternalTools tools) => false;
    public ClipInfo Probe(string path, ExternalTools tools, string workDir) => throw new ToolError("Canon RAW", -1, MissingHint);
    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt,
        Func<bool>? isCancelled = null) =>
        throw new ToolError("Canon RAW", -1, MissingHint);
}
