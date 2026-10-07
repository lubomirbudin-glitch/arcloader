namespace Offload.Core.Tests;

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (TestMedia.Ffmpeg == null || TestMedia.Ffprobe == null) Skip = "ffmpeg/ffprobe not found";
    }
}

public static class TestMedia
{
    static readonly ExternalTools Tools = new ExternalTools().Resolved();
    public static string? Ffmpeg => Tools.Ffmpeg;
    public static string? Ffprobe => Tools.Ffprobe;

    /// Test pattern with burnt-in TC track and optional sine audio, H.264 in MOV/MP4 (by extension). `reel`: the
    /// camera's reel name on the timecode track (MOV `reel_name`); `audioStreams`: that many mono sine streams;
    /// `timecode` null: no timecode at all.
    public static void MakeClip(string path, double seconds = 2, int fps = 25, string? timecode = "01:00:00:00",
        bool audio = true, int width = 1280, int height = 720, string? reel = null, int audioStreams = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s={width}x{height}:r={fps}:d={d}" };
        int streams = audio ? audioStreams : 0;
        for (int i = 0; i < streams; i++) args.AddRange(["-f", "lavfi", "-i", $"sine=f={440 * (i + 1)}:d={d}"]);
        args.AddRange(["-map", "0:v"]);
        for (int i = 0; i < streams; i++) args.AddRange(["-map", $"{i + 1}:a"]);
        if (timecode != null) args.AddRange(["-timecode", timecode]);
        args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (streams > 0) args.AddRange(["-c:a", "aac", "-shortest"]);
        if (reel != null) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// A Sony-like MXF: MPEG-2 1080p25 with `monoTracks` 24-bit PCM mono streams and timecode `timecode`.
    public static void MakeMxf(string path, int monoTracks, string timecode = "10:00:00:00", double seconds = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s=1920x1080:r=25:d={d}" };
        for (int i = 0; i < monoTracks; i++) args.AddRange(["-f", "lavfi", "-i", $"sine=f={110 * (i + 1)}:d={d}"]);
        args.AddRange(["-map", "0:v"]);
        for (int i = 0; i < monoTracks; i++) args.AddRange(["-map", $"{i + 1}:a"]);
        args.AddRange(["-c:v", "mpeg2video", "-b:v", "30M", "-c:a", "pcm_s24le", "-ar", "48000", "-timecode", timecode, "-f", "mxf", path]);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// ffprobe's streams of a file: (codec_type, codec_name, channels, tags).
    public static List<(string Type, string? Codec, int Channels, Dictionary<string, string> Tags)> Streams(string path)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(ToolProcess.Run(Ffprobe!, ["-v", "error", "-print_format", "json", "-show_streams", path]));
        return doc.RootElement.GetProperty("streams").EnumerateArray().Select(s => (
            s.GetProperty("codec_type").GetString()!,
            s.TryGetProperty("codec_name", out var c) ? c.GetString() : null,
            s.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0,
            s.TryGetProperty("tags", out var t) ? t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "") : new Dictionary<string, string>()))
            .ToList();
    }

    /// 2×2×2 identity 3D LUT.
    public static void IdentityLut(string path) => File.WriteAllText(path,
        "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
}

public sealed class EdgeFactAttribute : FactAttribute
{
    public EdgeFactAttribute()
    {
        if (PdfReport.FindEdge() == null) Skip = "Microsoft Edge not found";
    }
}
