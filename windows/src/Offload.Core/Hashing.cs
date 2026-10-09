using Offload.Core.Verification;

namespace Offload.Core;

/// The checksums a copy computes (spec 3.5, R16): xxh64 always, MD5 only in addition — never MD5 alone, so every
/// backup stays verifiable by the Mac and by older `offload verify`.
[Flags]
public enum HashKind
{
    Xxh64 = 1,
    Md5 = 2,
    Both = Xxh64 | Md5,
}

public static class HashKinds
{
    /// R16: xxh64 is always in.
    public static HashKind Normalize(HashKind kinds) => kinds | HashKind.Xxh64;

    /// The ASC MHL / report names, in that order: ["xxh64"] or ["xxh64", "md5"].
    public static IReadOnlyList<string> Names(HashKind kinds) =>
        Normalize(kinds).HasFlag(HashKind.Md5) ? ["xxh64", "md5"] : ["xxh64"];

    /// From a report's hash names ("md5" anywhere → both); anything else is xxh64.
    public static HashKind FromNames(IEnumerable<string>? names) =>
        names != null && names.Contains("md5", StringComparer.OrdinalIgnoreCase) ? HashKind.Both : HashKind.Xxh64;

    /// settings.json `hash` and CLI --hash: "xxh64" | "both".
    public static string Wire(HashKind kinds) => Normalize(kinds).HasFlag(HashKind.Md5) ? "both" : "xxh64";

    public static bool TryParseWire(string? s, out HashKind kinds)
    {
        kinds = s switch { "xxh64" => HashKind.Xxh64, "both" => HashKind.Both, _ => default };
        return kinds != default;
    }

    /// The setting as the copy uses it: anything this version does not know (a newer value) is xxh64.
    public static HashKind FromSetting(string? s) => TryParseWire(s, out var k) ? k : HashKind.Xxh64;

    /// For people: "xxHash64", "xxHash64 + MD5".
    public static string Label(HashKind kinds) => Normalize(kinds).HasFlag(HashKind.Md5) ? "xxHash64 + MD5" : "xxHash64";
}

/// The checksums of one file; null = not computed (the file failed, or MD5 was not asked for). `Xxh128` (plan 10, R2):
/// XXH3-128 as ASC MHL writes it (32 lower-case hex digits) — every copy computes it next to xxh64; null in older reports.
public readonly record struct FileHashes(ulong? Xxh64, byte[]? Md5, string? Xxh128 = null)
{
    public string? Xxh64Hex => Xxh64 is ulong x ? Format.Hex64(x) : null;
    public string? Md5Hex => Md5 is { } m ? Convert.ToHexStringLower(m) : null;
}

public static class Hashing
{
    /// Hashes a whole file straight from the media (no file cache). XXH64 is the hash DIT tools and ASC MHL use.
    public static (ulong Hash, long Size) HashFile(string path, AlignedBuffer buffer, Action<int>? progress = null, CancellationToken ct = default)
    {
        var xx = HashAlgo.Create("xxh64");
        long size = HashFile(path, buffer, [xx], progress, ct);
        return (Convert.ToUInt64(xx.Finish(), 16), size);
    }

    /// Every hash in `kinds` (xxh64 always) in one read of the file — the read-back verification of a copy.
    /// `beforeBlock`: called before every block is read (a pause waits there).
    public static (FileHashes Hashes, long Size) HashFile(string path, AlignedBuffer buffer, HashKind kinds, Action<int>? progress = null, CancellationToken ct = default,
        Action? beforeBlock = null)
    {
        var xx = HashAlgo.Create("xxh64");
        var md5 = HashKinds.Normalize(kinds).HasFlag(HashKind.Md5) ? HashAlgo.Create("md5") : null;
        long size = HashFile(path, buffer, md5 == null ? [xx] : [xx, md5], progress, ct, beforeBlock);
        return (new FileHashes(Convert.ToUInt64(xx.Finish(), 16), md5 == null ? null : Convert.FromHexString(md5.Finish())), size);
    }

    /// The one read loop: the file is read once, straight from the media, and every hash in `hashers` is fed each block.
    /// Returns the size read.
    public static long HashFile(string path, AlignedBuffer buffer, IReadOnlyList<IFileHash> hashers, Action<int>? progress = null, CancellationToken ct = default,
        Action? beforeBlock = null)
    {
        using var reader = new FileReader(path);
        long offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            beforeBlock?.Invoke();
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(buffer.Span, offset);
            if (n == 0) break;
            foreach (var h in hashers) h.Append(buffer.Span[..n]);
            offset += n;
            progress?.Invoke(n);
            if (n < buffer.Size) break;
        }
        return offset;
    }
}
