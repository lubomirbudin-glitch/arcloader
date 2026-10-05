using System.CommandLine.Parsing;
using System.Globalization;

namespace Offload.Cli;

/// Culture-independent number parsing for options (accepts "1.85" and "1,85" regardless of Windows locale).
static class CliParse
{
    /// Positive, finite number or null.
    public static double? Double(string? text)
    {
        if (text != null && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && double.IsFinite(v) && v > 0) return v;
        return null;
    }

    public static double? Parse(ArgumentResult a)
    {
        var s = a.Tokens.Count > 0 ? a.Tokens[0].Value : null;
        var v = Double(s);
        if (v == null) a.AddError($"Cannot parse '{s}' as a positive number.");
        return v;
    }
}
