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
