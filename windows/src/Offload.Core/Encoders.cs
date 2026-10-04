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
