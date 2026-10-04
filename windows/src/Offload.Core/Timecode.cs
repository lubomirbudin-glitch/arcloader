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

    /// Start timecode plus a number of frames, counted at the nominal (rounded) rate and wrapping at 24 h.
    /// Drop-frame timecode is labelled approximately — good enough for a thumbnail caption.
    public static string Add(string tc, long frames, double fps)
    {
        int nominal = Math.Max(1, (int)Math.Round(fps));
        var sep = tc[8];
        long h = long.Parse(tc[..2], CultureInfo.InvariantCulture), m = long.Parse(tc[3..5], CultureInfo.InvariantCulture);
        long s = long.Parse(tc[6..8], CultureInfo.InvariantCulture), f = long.Parse(tc[9..11], CultureInfo.InvariantCulture);
        long perDay = 24L * 3600 * nominal;
        long total = (((h * 3600 + m * 60 + s) * nominal + f + frames) % perDay + perDay) % perDay;
        long ff = total % nominal, secs = total / nominal;
        return string.Create(CultureInfo.InvariantCulture, $"{secs / 3600:00}:{secs / 60 % 60:00}:{secs % 60:00}{sep}{ff:00}");
    }
}
