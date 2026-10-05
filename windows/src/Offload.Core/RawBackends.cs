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
    FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt);
}

/// What a RAW decoder should hand ffmpeg: camera log (a LUT follows) or its own Rec.709 look.
/// `ArriColorSpace` is the ARRI target space matching the LUT (null = from the clip's metadata).
/// BRAW and Canon ignore it (BRAW always decodes in the clip's gamma).
internal sealed record RawLook(bool Log, string? ArriColorSpace);

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

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt)
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
            FullRange = true,
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

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt)
    {
        // Thumbnails come from the finished proxy instead (one REDline run per clip is enough).
        if (singleFrameAt != null) throw new ToolError("REDline", -1, "thumbnail from proxy");
        var dir = RawBackends.FreshDir(workDir, "r3d");
        ToolProcess.Run(tools.Redline!, Args(path, dir, info, settings, look));
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
            args.AddRange(["--desqueeze", "--output-height", s.Height.ToString(CultureInfo.InvariantCulture)]);
        }
        if (look.Log) args.AddRange(["--target-colorspace", look.ArriColorSpace ?? ColorSpace(null, c)]);
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

    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt)
    {
        if (singleFrameAt != null) throw new ToolError("art-cmd", -1, "thumbnail from proxy");
        var dir = RawBackends.FreshDir(workDir, "ari");
        var output = Path.Combine(dir, "intermediate.mxf");
        var parameters = Path.Combine(dir, "parameters.json");
        var (args, json, cropped) = Args(path, output, info, settings, look, parameters);
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
    public FrameSource Source(string path, ClipInfo info, ProxySettings settings, RawLook look, ExternalTools tools, string workDir, double? singleFrameAt) =>
        throw new ToolError("Canon RAW", -1, MissingHint);
}
