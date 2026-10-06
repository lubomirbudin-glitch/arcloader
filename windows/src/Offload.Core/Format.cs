using System.Globalization;

namespace Offload.Core;

public static class OffloadVersion
{
    public const string String = "0.1.0-win";
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
        return i == 0 ? $"{b} B" : v.ToString("0.00", CultureInfo.InvariantCulture) + " " + Units[i];
    }

    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// A size inside a Czech sentence: "3,2 GB", "2 GB", "412 GB", "850 MB", "12 B" — decimal units like Bytes, at most
    /// one decimal place below 10, none above.
    public static string BytesCs(long b)
    {
        double v = b;
        int i = 0;
        while (v >= 1000 && i < Units.Length - 1) { v /= 1000; i++; }
        if (i == 0) return $"{b} B";
        return v.ToString(v < 10 ? "0.#" : "0", Cs) + " " + Units[i];
    }

    public static string Rate(long bytes, double seconds) =>
        seconds > 0 ? (bytes / seconds / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s" : "-";
}
