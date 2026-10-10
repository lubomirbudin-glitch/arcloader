using System.Globalization;
using System.Reflection;

namespace Offload.Core;

public static class OffloadVersion
{
    /// "x.y.z-win": InformationalVersion of Directory.Build.props (R22 - the one place a version is set), read from this
    /// assembly. The report's `version`, the HTML footers and the update check use it.
    public static readonly string String =
        typeof(OffloadVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-win";
}

public static class Format
{
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Hex64(ulong v) => v.ToString("x16", CultureInfo.InvariantCulture);

    /// Decimal units (1 KB = 1000 B), like the Mac version, cameras and disk vendors.
    public static string Bytes(long b)
    {
        double v = b;
        int i = 0;
        while (v >= 1000 && i < Units.Length - 1) { v /= 1000; i++; }
        return i == 0 ? $"{b.ToString(CultureInfo.InvariantCulture)} B" : v.ToString("0.00", CultureInfo.InvariantCulture) + " " + Units[i];
    }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// A size inside a sentence: "3.2 GB", "2 GB", "412 GB", "850 MB", "12 B" — decimal units like Bytes, at most one
    /// decimal place below 10, none above. Invariant (a decimal point), whatever the PC's culture.
    public static string BytesShort(long b)
    {
        double v = b;
        int i = 0;
        while (v >= 1000 && i < Units.Length - 1) { v /= 1000; i++; }
        if (i == 0) return $"{b.ToString(Inv)} B";
        return v.ToString(v < 10 ? "0.#" : "0", Inv) + " " + Units[i];
    }

    /// Three significant digits, as the new window shows sizes (UI návrh): "1.70 GB", "14.2 GB", "412 GB", "850 MB".
    public static string Bytes3(long b)
    {
        double v = b;
        int i = 0;
        while (v >= 1000 && i < Units.Length - 1) { v /= 1000; i++; }
        if (i == 0) return $"{b.ToString(Inv)} B";
        if (Math.Round(v, 2) < 10) return Math.Round(v, 2).ToString("0.00", Inv) + " " + Units[i];
        if (Math.Round(v, 1) < 100) return Math.Round(v, 1).ToString("0.0", Inv) + " " + Units[i];
        if (Math.Round(v) < 1000 || i == Units.Length - 1) return Math.Round(v).ToString("0", Inv) + " " + Units[i];
        return (v / 1000).ToString("0.00", Inv) + " " + Units[i + 1];   // 999.6 MB → 1.00 GB
    }

    /// The old names (the app was Czech until 0.9.0); same output as BytesShort / Bytes3.
    public static string BytesCs(long b) => BytesShort(b);
    public static string BytesCs3(long b) => Bytes3(b);

    public static string Rate(long bytes, double seconds) =>
        seconds > 0 ? (bytes / seconds / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "-";
}
