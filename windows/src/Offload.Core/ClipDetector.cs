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
            info.ColorRange = Str(v, "color_range");
            info.Duration = Dbl(Str(v, "duration"));
        }
        info.AudioStreams = streams.Where(x => Str(x, "codec_type") == "audio").Select(x => Int(x, "channels") ?? 0).ToList();
        info.AudioChannels = info.AudioStreams.Sum();
        if (Dbl(Str(format, "duration")) is double fd) info.Duration = fd;
        var ftags = Tags(format);
        info.Timecode ??= Str(ftags, "timecode") ?? streams.Select(x => Str(Tags(x), "timecode")).FirstOrDefault(t => t != null);
        info.Camera = Camera(Str(ftags, "com.apple.quicktime.make"), Str(ftags, "com.apple.quicktime.model"))
            ?? Str(ftags, "product_name") ?? Str(ftags, "company_name");
        info.Reel = new[] { format }.Concat(streams).Select(x => Reel(Tags(x))).FirstOrDefault(r => r != null);
        return info;
    }

    /// Tags that name the reel: MOV keeps it on the timecode track (tmcd "reel_name"), MXF on every stream.
    static readonly string[] ReelTags = ["reel_name", "tape_name", "reel"];

    static string? Reel(JsonElement tags)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in ReelTags)
            foreach (var p in tags.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String
                    && p.Value.GetString()!.Trim() is { Length: > 0 } reel)
                    return reel;
        return null;
    }

    /// Make and model together ("ARRI" + "ALEXA 35" → "ARRI ALEXA 35"), unless the model already starts with the make.
    static string? Camera(string? make, string? model)
    {
        make = string.IsNullOrWhiteSpace(make) ? null : make.Trim();
        model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (make == null || model == null) return make ?? model;
        return model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : make + " " + model;
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
