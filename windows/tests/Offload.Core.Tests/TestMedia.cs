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

    /// Test pattern with burnt-in TC track and optional sine audio, H.264 in MOV/MP4 (by extension).
    public static void MakeClip(string path, double seconds = 2, int fps = 25, string timecode = "01:00:00:00",
        bool audio = true, int width = 1280, int height = 720)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var d = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s={width}x{height}:r={fps}:d={d}" };
        if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=d={d}"]);
        args.AddRange(["-timecode", timecode, "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "aac", "-shortest"]);
        args.Add(path);
        ToolProcess.Run(Ffmpeg!, args);
    }

    /// 2×2×2 identity 3D LUT.
    public static void IdentityLut(string path) => File.WriteAllText(path,
        "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
}
