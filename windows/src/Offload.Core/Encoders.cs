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
        return Cache.GetOrAdd((ffmpeg, codec), key => Pick(key.Item1, key.Item2, key.Item2 == ProxyCodec.H264 ? H264 : Hevc));
    }

    /// The first candidate that works (no cache); the last one when none does, so ffmpeg reports the real error.
    internal static string Pick(string ffmpeg, ProxyCodec codec, IReadOnlyList<string> candidates)
    {
        if (codec is not (ProxyCodec.H264 or ProxyCodec.Hevc)) return "prores_ks";
        return candidates.FirstOrDefault(e => Works(ffmpeg, e)) ?? candidates[^1];
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

    /// Every proxy is BT.709 legal range (FilterGraph.ToRec709 / TagRec709); the encoder and container say so as well.
    public static readonly string[] Rec709Args = ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"];

    public static string[] AudioArgs(ProxyCodec codec) =>
        codec is ProxyCodec.H264 or ProxyCodec.Hevc ? ["-c:a", "aac", "-b:a", "192k"] : ["-c:a", "pcm_s16le"];

    public static string PixelFormat(ProxyCodec codec) =>
        codec is ProxyCodec.H264 or ProxyCodec.Hevc ? "yuv420p" : "yuv422p10le";
}
