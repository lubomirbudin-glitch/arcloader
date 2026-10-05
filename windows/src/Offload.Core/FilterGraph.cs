using System.Globalization;

namespace Offload.Core;

/// ffmpeg filter graphs for thumbnails and proxies. They only ever name files in the renderer's work folder
/// by relative name — a Windows path ("C:\…") inside a filter graph would need escaping that breaks easily.
public static class FilterGraph
{
    public const string LutFile = "look.cube";
    public const string FontFile = "font.ttf";

    static string Num(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// Full-range RGB (BRAW decoder output, video tagged color_range=pc) → the legal-expanded convention the
    /// built-in LUTs expect (code = x·876 + 64). On rgb48le: v16 → ((v16/65535·1023 − 64)/876)·65535, clipped.
    public const string PreMap =
        "lutrgb=r='clip((val*1023/65535-64)/876*65535,0,65535)':g='clip((val*1023/65535-64)/876*65535,0,65535)':b='clip((val*1023/65535-64)/876*65535,0,65535)'";

    /// `file` is a .cube in the work folder; `premap` only for a built-in LUT on a full-range source.
    static string Lut(string file, bool premap) =>
        "format=rgb48le," + (premap ? PreMap + "," : "") + $"lut3d=file={file}:interp=tetrahedral";

    /// Proxies: explicit BT.709 matrix to legal range (swscale would otherwise pick BT.601 after an RGB step, or keep
    /// whatever the source had), then the frames are tagged BT.709 so the encoder and the MOV say so too.
    public const string ToRec709 = "scale=out_color_matrix=bt709:out_range=tv";
    public const string TagRec709 = "setparams=color_primaries=bt709:color_trc=bt709";

    /// Thumbnails: JPEG (JFIF) is BT.601 full range for every browser and PDF viewer, whatever the source was.
    public const string ToJfif = "scale=out_color_matrix=bt601:out_range=pc";

    /// Small JPEG for the report, with the LUT applied so log footage is readable.
    public static string Thumbnail(string? lutFile, bool premap) =>
        string.Join(",", new[] { "scale=-2:180:flags=bicubic", lutFile != null ? Lut(lutFile, premap) : null, ToJfif, "format=yuvj420p" }.Where(x => x != null));

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
        // expansion=none: the name is literal text — a "%" in it would otherwise be read as a drawtext expansion.
        if (name) f.Add($"drawtext={common}:textfile={labelFile}:expansion=none:x={margin}");
        if (tc)
        {
            var start = Timecode.IsValid(timecode) ? timecode! : "00:00:00:00";
            var escaped = start.Replace(":", "\\:").Replace(";", "\\;");
            f.Add($"drawtext={common}:timecode='{escaped}':rate={rate}:x=w-tw-{margin}");
        }
        return f;
    }

    public static string Proxy(ProxySettings s, int outHeight, bool alreadyCropped, string? lutFile, bool premap, bool hasFont,
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
        if (lutFile != null) vf.Add(Lut(lutFile, premap));
        vf.AddRange(BurnIn(s, outHeight, hasFont, labelFile, timecode, rate));
        vf.Add(ToRec709);
        vf.Add($"format={Encoders.PixelFormat(s.Codec)}");
        vf.Add(TagRec709);
        return string.Join(",", vf);
    }
}
