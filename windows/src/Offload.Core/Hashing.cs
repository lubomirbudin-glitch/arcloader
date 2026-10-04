using System.IO.Hashing;

namespace Offload.Core;

public static class Hashing
{
    /// Hashes a whole file straight from the media (no file cache). XXH64 is the hash DIT tools and ASC MHL use.
    public static (ulong Hash, long Size) HashFile(string path, AlignedBuffer buffer, Action<int>? progress = null, CancellationToken ct = default)
    {
        using var reader = new FileReader(path);
        var xx = new XxHash64();
        long offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(buffer.Span, offset);
            if (n == 0) break;
            xx.Append(buffer.Span[..n]);
            offset += n;
            progress?.Invoke(n);
            if (n < buffer.Size) break;
        }
        return (xx.GetCurrentHashAsUInt64(), offset);
    }
}
