using System.Globalization;
using System.Text;

namespace Offload.BrawDecode;

/// What --info reports. Width/Height are the processed size at the requested scale (decoded from frame 0 —
/// the SDK's rounding is the truth, not width/scale), Source* the sensor size.
/// `ColorScienceGenOriginal`: with --gen5, the generation the clip was recorded in (ColorScienceGen is then the
/// effective one). `SensorRate`: the rate frame 0 was captured at (IBlackmagicRawFrame::GetSensorRate), null when the
/// SDK does not say (0).
internal sealed record ClipFacts(
    uint Width, uint Height, uint SourceWidth, uint SourceHeight, float Fps, ulong Frames,
    string Timecode, string Camera, string? Gamma, string? Gamut, int? ColorScienceGen, int? ColorScienceGenOriginal = null,
    float? SensorRate = null);

internal static class InfoJson
{
    /// One line, byte-compatible with the Mac braw-decode for the shared keys ("fps" with 6 decimals);
    /// gamma / gamut / colorScienceGen follow when the SDK reports them (Windows only so far), then "sensorRate" (the capture
    /// rate, 6 decimals like "fps"; task 2026-10-10) when known.
    public static string Format(ClipFacts f)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"{{\"width\":{f.Width},\"height\":{f.Height},\"sourceWidth\":{f.SourceWidth},\"sourceHeight\":{f.SourceHeight},");
        sb.Append(inv, $"\"fps\":{((double)f.Fps).ToString("F6", inv)},\"frames\":{f.Frames},");
        sb.Append(inv, $"\"timecode\":\"{Escape(f.Timecode)}\",\"camera\":\"{Escape(f.Camera)}\"");
        if (f.Gamma != null) sb.Append(inv, $",\"gamma\":\"{Escape(f.Gamma)}\"");
        if (f.Gamut != null) sb.Append(inv, $",\"gamut\":\"{Escape(f.Gamut)}\"");
        if (f.ColorScienceGen is int gen) sb.Append(inv, $",\"colorScienceGen\":{gen}");
        if (f.ColorScienceGenOriginal is int original) sb.Append(inv, $",\"colorScienceGenOriginal\":{original}");
        if (f.SensorRate is float sensor && sensor > 0) sb.Append(inv, $",\"sensorRate\":{((double)sensor).ToString("F6", inv)}");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// Like the Mac: quote and backslash escaped, control characters dropped, everything else as is (UTF-8).
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\');
            if (c >= 0x20) sb.Append(c);
        }
        return sb.ToString();
    }
}
