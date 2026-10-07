using Offload.Core.Verification;

namespace Offload.Core;

public static class Hashing
{
    /// Hashes a whole file straight from the media (no file cache). XXH64 is the hash DIT tools and ASC MHL use.
    public static (ulong Hash, long Size) HashFile(string path, AlignedBuffer buffer, Action<int>? progress = null, CancellationToken ct = default)
    {
        var xx = HashAlgo.Create("xxh64");
        long size = HashFile(path, buffer, [xx], progress, ct);
        return (Convert.ToUInt64(xx.Finish(), 16), size);
    }

    /// The one read loop: the file is read once, straight from the media, and every hash in `hashers` is fed each block.
    /// Returns the size read.
    public static long HashFile(string path, AlignedBuffer buffer, IReadOnlyList<IFileHash> hashers, Action<int>? progress = null, CancellationToken ct = default)
    {
        using var reader = new FileReader(path);
        long offset = 0;
        while (true)
        {
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
