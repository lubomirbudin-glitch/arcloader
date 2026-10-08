using System.IO.Hashing;
using System.Security.Cryptography;

namespace Offload.Core.Verification;

/// One running hash of a file, fed block by block.
public interface IFileHash
{
    void Append(ReadOnlySpan<byte> data);
    /// The value as ASC MHL writes it.
    string Finish();
}

/// The ASC MHL hash types the .NET class library has (R9): xxh64, md5, sha1, xxh3, xxh128, c4. Values are lower-case hex
/// of the canonical (big-endian) digest, as ascmhl writes them; c4 is "c4" + base58(SHA-512) like Mhl.C4.
public static class HashAlgo
{
    public static readonly IReadOnlyList<string> Names = ["xxh64", "md5", "sha1", "xxh3", "xxh128", "c4"];

    public static bool IsKnown(string name) => Names.Contains(name);

    public static IFileHash Create(string name) => name switch
    {
        "xxh64" => new NonCrypto(new XxHash64()),
        "xxh3" => new NonCrypto(new XxHash3()),
        "xxh128" => new NonCrypto(new XxHash128()),
        "md5" => new Crypto(IncrementalHash.CreateHash(HashAlgorithmName.MD5), c4: false),
        "sha1" => new Crypto(IncrementalHash.CreateHash(HashAlgorithmName.SHA1), c4: false),
        "c4" => new Crypto(IncrementalHash.CreateHash(HashAlgorithmName.SHA512), c4: true),
        _ => throw new ArgumentException($"unknown hash type {name}", nameof(name)),
    };

    /// The whole of `data` at once (tests, small files).
    public static string Of(string name, ReadOnlySpan<byte> data)
    {
        var h = Create(name);
        h.Append(data);
        return h.Finish();
    }

    /// Hex compares in any case; a c4 id is base58, where case matters.
    public static bool Same(string name, string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), name == "c4" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// For people: "xxHash64", "MD5", "SHA-1", "xxHash3", "xxHash128", "C4".
    public static string Label(string name) => name switch
    {
        "xxh64" => "xxHash64",
        "md5" => "MD5",
        "sha1" => "SHA-1",
        "xxh3" => "xxHash3",
        "xxh128" => "xxHash128",
        "c4" => "C4",
        _ => name,
    };

    sealed class NonCrypto(NonCryptographicHashAlgorithm hash) : IFileHash
    {
        public void Append(ReadOnlySpan<byte> data) => hash.Append(data);
        public string Finish() => Convert.ToHexStringLower(hash.GetCurrentHash());
    }

    sealed class Crypto(IncrementalHash hash, bool c4) : IFileHash
    {
        public void Append(ReadOnlySpan<byte> data) => hash.AppendData(data);

        public string Finish()
        {
            var digest = hash.GetHashAndReset();
            hash.Dispose();
            return c4 ? Mhl.C4FromSha512(digest) : Convert.ToHexStringLower(digest);
        }
    }
}
