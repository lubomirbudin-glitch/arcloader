using System.Globalization;
using System.Text.Json.Serialization;

namespace Offload.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ClipFormat>))]
public enum ClipFormat
{
    [JsonStringEnumMemberName("video")] Video,      // anything ffmpeg decodes: ProRes, H.264/H.265, XAVC, DNx …
    [JsonStringEnumMemberName("braw")] Braw,
    [JsonStringEnumMemberName("r3d")] R3d,
    [JsonStringEnumMemberName("arriraw")] Arriraw,  // .ari sequence or .mxf
    [JsonStringEnumMemberName("canonRaw")] CanonRaw,
    [JsonStringEnumMemberName("unknown")] Unknown,
}

public static class ClipFormatExtensions
{
    public static string Label(this ClipFormat f) => f switch
    {
        ClipFormat.Video => "Video",
        ClipFormat.Braw => "Blackmagic RAW",
        ClipFormat.R3d => "RED R3D",
        ClipFormat.Arriraw => "ARRIRAW",
        ClipFormat.CanonRaw => "Canon RAW",
        _ => "?",
    };
}

public sealed class Clip
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    /// Primary file, relative to the card folder (for .ari sequences the folder).
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("format")] public ClipFormat Format { get; set; }
    [JsonPropertyName("segments")] public required IReadOnlyList<string> Segments { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("isCameraProxy")] public bool IsCameraProxy { get; init; }
}

public sealed class ClipInfo
{
    [JsonPropertyName("codec")] public string? Codec { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    /// Rational as ffmpeg likes it ("24000/1001").
    [JsonPropertyName("frameRate")] public string? FrameRate { get; set; }
    [JsonPropertyName("duration")] public double? Duration { get; set; }
    [JsonPropertyName("frames")] public int? Frames { get; set; }
    [JsonPropertyName("timecode")] public string? Timecode { get; set; }
    [JsonPropertyName("audioChannels")] public int AudioChannels { get; set; }
    [JsonPropertyName("camera")] public string? Camera { get; set; }
    /// Log curve the camera recorded in (e.g. "LogC4"), when the format reports it.
    [JsonPropertyName("logEncoding")] public string? LogEncoding { get; set; }
    /// Anamorphic squeeze of the lens (2.0 for 2x), when the format reports it.
    [JsonPropertyName("squeeze")] public double? Squeeze { get; set; }

    [JsonIgnore]
    public string FpsLabel
    {
        get
        {
            if (FrameRate == null) return "";
            var parts = FrameRate.Split('/')
                .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
                .Where(v => !double.IsNaN(v)).ToArray();
            double fps = parts.Length == 2 && parts[1] != 0 ? parts[0] / parts[1] : parts.FirstOrDefault();
            return fps == Math.Round(fps)
                ? ((long)fps).ToString(CultureInfo.InvariantCulture)
                : fps.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }

    [JsonIgnore]
    public string ResolutionLabel => Width is int w && Height is int h ? $"{w}×{h}" : "";

    [JsonIgnore]
    public string DurationLabel
    {
        get
        {
            if (Duration is not double d) return "";
            int s = (int)Math.Round(d, MidpointRounding.AwayFromZero);
            return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ProxyStatus>))]
public enum ProxyStatus
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("skipped")] Skipped,
    [JsonStringEnumMemberName("unsupported")] Unsupported,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("failed")] Failed,
}

public sealed class ClipEntry
{
    [JsonPropertyName("clip")] public required Clip Clip { get; set; }
    [JsonPropertyName("info")] public ClipInfo? Info { get; set; }
    [JsonPropertyName("probeError")] public string? ProbeError { get; set; }
    [JsonPropertyName("proxyStatus")] public ProxyStatus ProxyStatus { get; set; } = ProxyStatus.None;
    /// Relative to the card folder.
    [JsonPropertyName("proxyPath")] public string? ProxyPath { get; set; }
    [JsonPropertyName("proxyNote")] public string? ProxyNote { get; set; }
    [JsonPropertyName("proxySeconds")] public double? ProxySeconds { get; set; }
    /// First, middle and last frame (JPEG + caption); embedded in the HTML/PDF report only.
    [JsonIgnore] public List<Thumbnail> Thumbnails { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<ProxyCodec>))]
public enum ProxyCodec
{
    [JsonStringEnumMemberName("h264")] H264,
    [JsonStringEnumMemberName("hevc")] Hevc,
    [JsonStringEnumMemberName("proresProxy")] ProresProxy,
    [JsonStringEnumMemberName("proresLT")] ProresLT,
    [JsonStringEnumMemberName("prores422")] Prores422,
    [JsonStringEnumMemberName("prores422HQ")] Prores422HQ,
}

public static class ProxyCodecExtensions
{
    public static string Label(this ProxyCodec c) => c switch
    {
        ProxyCodec.H264 => "H.264",
        ProxyCodec.Hevc => "H.265 / HEVC",
        ProxyCodec.ProresProxy => "ProRes 422 Proxy",
        ProxyCodec.ProresLT => "ProRes 422 LT",
        ProxyCodec.Prores422 => "ProRes 422",
        _ => "ProRes 422 HQ",
    };

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
}

public sealed class ProxySettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; } = 1080;
    [JsonPropertyName("codec")] public ProxyCodec Codec { get; set; } = ProxyCodec.H264;
    [JsonPropertyName("bitrateMbps")] public int BitrateMbps { get; set; } = 10;
    /// .cube 3D LUT applied after downscaling (cheap at proxy resolution).
    [JsonPropertyName("lutPath")] public string? LutPath { get; set; }
    [JsonPropertyName("burnFilename")] public bool BurnFilename { get; set; } = true;
    [JsonPropertyName("burnTimecode")] public bool BurnTimecode { get; set; } = true;
    [JsonPropertyName("audio")] public bool Audio { get; set; } = true;
    /// Skip the low-res files cameras record themselves (Sony SUB, PROXY folders).
    [JsonPropertyName("skipCameraProxies")] public bool SkipCameraProxies { get; set; } = true;
    /// Proxies are rendered once, onto the first destination, then copied to the others.
    [JsonPropertyName("copyToAllDestinations")] public bool CopyToAllDestinations { get; set; } = true;
    /// Crop to the frame lines: aspect ratio of the frame (e.g. 1.66); null = full image.
    [JsonPropertyName("frameAspect")] public double? FrameAspect { get; set; }
    /// How much of the image the frame covers (0.9 = 90 %, i.e. 10 % overscan around it).
    [JsonPropertyName("frameScale")] public double FrameScale { get; set; } = 1.0;

    /// Centered crop rectangle (x, y, w, h) inside a W×H image, even-sized, or null when no crop is set.
    public (int X, int Y, int W, int H)? FrameCrop(int width, int height)
    {
        if (FrameAspect is not double aspect || aspect <= 0 || width <= 0 || height <= 0) return null;
        double scale = Math.Min(Math.Max(FrameScale, 0.1), 1.0);
        double w = width * scale;
        double h = w / aspect;
        if (h > height * scale)
        {
            h = height * scale;
            w = h * aspect;
        }
        int cw = (int)w & ~1, ch = (int)h & ~1;
        return (((width - cw) / 2) & ~1, ((height - ch) / 2) & ~1, cw, ch);
    }

    [JsonIgnore]
    public string? FrameLabel => FrameAspect is double a
        ? a.ToString("0.00", CultureInfo.InvariantCulture)
          + (FrameScale < 1 ? " · " + (FrameScale * 100).ToString("0", CultureInfo.InvariantCulture) + " %" : "")
        : null;
}

/// One report thumbnail: a small JPEG and its caption (timecode, or frame number when the clip has no TC).
public sealed record Thumbnail(byte[] Jpeg, string Label);
