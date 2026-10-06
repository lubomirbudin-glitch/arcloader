using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core.History;

/// What a card looked like (spec 2.1, R2): two SHA-256 hashes (lower-case hex) over its file list sorted ordinally —
/// `Fingerprint` over "relpath\tsize\tmtimeTicksUtc\n" lines (the card is exactly as it was), `Shape` over
/// "relpath\tsize\n" (the same files whatever their times — how a manually added folder is found again).
public static class CardFingerprint
{
    public static (string Fingerprint, string Shape) Compute(IEnumerable<SourceFile> files)
    {
        using var full = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var shape = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var f in files.OrderBy(f => f.RelPath, StringComparer.Ordinal))
        {
            var size = f.Size.ToString(CultureInfo.InvariantCulture);
            shape.AppendData(Encoding.UTF8.GetBytes($"{f.RelPath}\t{size}\n"));
            full.AppendData(Encoding.UTF8.GetBytes($"{f.RelPath}\t{size}\t{f.ModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture)}\n"));
        }
        return (Convert.ToHexStringLower(full.GetHashAndReset()), Convert.ToHexStringLower(shape.GetHashAndReset()));
    }

    public static (string Fingerprint, string Shape) Compute(SourceScan scan) => Compute(scan.Files);
}

/// File times compared over a card's life (R3). FAT and FAT32 store local time: after a daylight-saving change Windows
/// reports every file shifted by whole hours, so there two times are the same when they differ by whole hours — at
/// most 14 — with equal minutes, seconds and fractions. exFAT and NTFS store UTC: there only an exact match counts.
public static class FileTimes
{
    public static bool StoresLocalTime(string? filesystem) =>
        filesystem != null && filesystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase);

    public static bool Same(DateTime a, DateTime b, bool localTimeFilesystem)
    {
        if (a.Ticks == b.Ticks) return true;
        if (!localTimeFilesystem) return false;
        long diff = Math.Abs(a.Ticks - b.Ticks);
        return diff % TimeSpan.TicksPerHour == 0 && diff <= 14 * TimeSpan.TicksPerHour;
    }

    /// Times as a report keeps them (whole seconds) or a FAT disk (2-second steps): the same within 2 s — on a
    /// local-time file system also after a shift by whole hours (at most 14).
    public static bool Close(DateTime a, DateTime b, bool localTimeFilesystem)
    {
        const long slack = 2 * TimeSpan.TicksPerSecond;
        long diff = Math.Abs(a.Ticks - b.Ticks);
        if (diff <= slack) return true;
        if (!localTimeFilesystem || diff > 14 * TimeSpan.TicksPerHour + slack) return false;
        long off = diff % TimeSpan.TicksPerHour;
        return off <= slack || TimeSpan.TicksPerHour - off <= slack;
    }
}
