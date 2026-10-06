using System.Diagnostics;
using System.IO.Hashing;

namespace Offload.Core;

/// Measures what the hardware can really do, so the engine settings rest on numbers, not guesses.
public static class Bench
{
    public sealed record Line(string Label, long Bytes, double Seconds)
    {
        public string Rate => Format.Rate(Bytes, Seconds);
    }

    /// Sequential read of existing files (largest first) at several block sizes, bypassing the file cache.
    /// ct: checked before every block — a cancelled read throws OperationCanceledException.
    public static IReadOnlyList<Line> Read(string path, long limit, IReadOnlyList<int> blockSizes, CancellationToken ct = default)
    {
        List<string> targets;
        if (Directory.Exists(path))
            targets = Scanner.Scan(path).Files.OrderByDescending(f => f.Size).Select(f => Layout.Join(path, f.RelPath)).ToList();
        else if (File.Exists(path))
            targets = [path];
        else
            throw new IOError("open", path, 2);
        if (targets.Count == 0) throw OffloadError.Preflight(PreflightCode.NoFilesToRead, Msg.NoFilesToRead(path));

        var lines = new List<Line>();
        foreach (var bs in blockSizes)
        {
            using var buf = new AlignedBuffer(bs);
            long total = 0;
            var sw = Stopwatch.StartNew();
            foreach (var file in targets)
            {
                using var reader = new FileReader(file);
                long off = 0;
                while (total < limit)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = reader.Read(buf.Span, off);
                    if (n == 0) break;
                    off += n;
                    total += n;
                    if (n < bs) break;
                }
                if (total >= limit) break;
            }
            lines.Add(new Line($"read  {BlockLabel(bs)}", total, sw.Elapsed.TotalSeconds));
        }
        return lines;
    }

    /// Writes a temporary file (unbuffered, flushed to the media), reads it back, deletes it.
    /// ct: checked before every block of the write and the read-back — a cancelled test throws
    /// OperationCanceledException at once and the temporary file is still deleted.
    public static IReadOnlyList<Line> Write(string dir, long size, int blockSize, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(dir, $".offload-bench-{Environment.ProcessId}");
        using var buf = new AlignedBuffer(blockSize);
        Random.Shared.NextBytes(buf.Span);
        try
        {
            var sw = Stopwatch.StartNew();
            using (var h = NativeIO.OpenWrite(path, unbuffered: true, size))
            {
                for (long off = 0; off < size; off += blockSize)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(blockSize, size - off);
                    int padded = (n + AlignedBuffer.Alignment - 1) / AlignedBuffer.Alignment * AlignedBuffer.Alignment;
                    NativeIO.Write(h, buf.Span[..padded], off, path);
                }
                NativeIO.SetLength(h, size, path);
                NativeIO.Flush(h, path);
            }
            var write = new Line($"write {BlockLabel(blockSize)}", size, sw.Elapsed.TotalSeconds);
            sw.Restart();
            var (_, read) = Hashing.HashFile(path, buf, ct: ct);
            return [write, new Line($"read  {BlockLabel(blockSize)} + xxh64", read, sw.Elapsed.TotalSeconds)];
        }
        finally
        {
            NativeIO.Delete(path);
        }
    }

    /// Raw hashing throughput from memory — must be far above any disk, otherwise it is the bottleneck.
    public static Line Hash(int size)
    {
        using var buf = new AlignedBuffer(size);
        Random.Shared.NextBytes(buf.Span);
        var sw = Stopwatch.StartNew();
        var xx = new XxHash64();
        for (int i = 0; i < 8; i++) xx.Append(buf.Span);
        xx.GetCurrentHashAsUInt64();
        return new Line("xxh64 in RAM", (long)size * 8, sw.Elapsed.TotalSeconds);
    }

    internal static string BlockLabel(int bs) => bs >= 1 << 20 ? $"{bs >> 20} MB" : $"{bs >> 10} KB";
}
