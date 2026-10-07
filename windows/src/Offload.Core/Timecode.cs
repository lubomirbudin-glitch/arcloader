using System.Globalization;
using System.Text.RegularExpressions;

namespace Offload.Core;

public static partial class Timecode
{
    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}[:;.]\d{2}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? tc) => tc != null && Pattern().IsMatch(tc);

    /// "24000/1001" → 23.976; "25" → 25; null for anything unusable.
    public static double? Fps(string? rational)
    {
        if (string.IsNullOrEmpty(rational)) return null;
        var parts = rational.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return null;
        double fps = num;
        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) || den == 0) return null;
            fps = num / den;
        }
        return fps > 0 ? fps : null;
    }

    /// The nominal (rounded) rate a timecode counts at: 23.976 → 24, 29.97 → 30, 59.94 → 60.
    public static int Nominal(double fps) => Math.Max(1, (int)Math.Round(fps));

    /// Drop-frame counting: a ";" timecode at 29.97 or 59.94 (spec 3.3, review focus 1).
    public static bool IsDropFrame(string tc, double fps) =>
        IsValid(tc) && tc[8] == ';' && Nominal(fps) is 30 or 60 && Math.Abs(fps - Nominal(fps)) > 0.001;

    /// Start timecode plus a number of frames, wrapping at 24 h, with the separator of `tc`. A drop-frame timecode counts
    /// as the camera does: frame numbers 00 and 01 (00–03 at 59.94) are left out at every minute but every tenth.
    public static string Add(string tc, long frames, double fps)
    {
        bool df = IsDropFrame(tc, fps);
        long perDay = df ? 144L * FramesPerTenMinutes(fps) : 24L * 3600 * Nominal(fps);
        long total = ((ToFrames(tc, fps) + frames) % perDay + perDay) % perDay;
        return FromFrames(total, fps, df, tc[8]);
    }

    /// A length in frames as "HH:MM:SS:FF" at the nominal rate (the Duration of a clip list).
    public static string Length(long frames, double fps) => FromFrames(Math.Max(0, frames), fps, dropFrame: false, ':');

    static long FramesPerTenMinutes(double fps) => Nominal(fps) * 600L - Nominal(fps) / 15 * 9;

    /// The frame number of `tc` counted from 00:00:00:00.
    static long ToFrames(string tc, double fps)
    {
        int n = Nominal(fps);
        long h = long.Parse(tc[..2], CultureInfo.InvariantCulture), m = long.Parse(tc[3..5], CultureInfo.InvariantCulture);
        long s = long.Parse(tc[6..8], CultureInfo.InvariantCulture), f = long.Parse(tc[9..11], CultureInfo.InvariantCulture);
        long frames = ((h * 60 + m) * 60 + s) * n + f;
        if (IsDropFrame(tc, fps))
        {
            long minutes = h * 60 + m;
            frames -= n / 15 * (minutes - minutes / 10);
        }
        return frames;
    }

    static string FromFrames(long frames, double fps, bool dropFrame, char separator)
    {
        int n = Nominal(fps);
        if (dropFrame)
        {
            int drop = n / 15;
            long perTen = FramesPerTenMinutes(fps), perMinute = n * 60L - drop;
            long tens = frames / perTen, rest = frames % perTen;
            frames += drop * 9L * tens + (rest > drop ? drop * ((rest - drop) / perMinute) : 0);
        }
        long ff = frames % n, secs = frames / n;
        return string.Create(CultureInfo.InvariantCulture, $"{secs / 3600 % 24:00}:{secs / 60 % 60:00}:{secs % 60:00}{separator}{ff:00}");
    }
}
