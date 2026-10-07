using System.IO.Hashing;

namespace Offload.Core.Tests;

public class DestWriterTests
{
    const int Chunk = 64 * 1024;
    static readonly DateTime When = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    /// Feeds `data` to the writer in blocks the way the engine does.
    static void Feed(DestWriter w, BufferPool pool, byte[] data, int fileIndex, SourceFile file)
    {
        w.Begin(file);
        for (long off = 0; off < data.Length; off += Chunk)
        {
            int n = (int)Math.Min(Chunk, data.Length - off);
            var buf = pool.Take();
            data.AsSpan((int)off, n).CopyTo(buf.Span);
            w.Write(new Chunk(buf, n, off, 1, pool));
        }
        w.Finish(fileIndex, file);
        w.Wait();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(Chunk - 1)]
    [InlineData(Chunk)]
    [InlineData(Chunk + 1)]
    [InlineData(3 * Chunk + 123)]
    public void WritesExactBytesAndModificationTime(int size)
    {
        using var dir = new TempDir();
        var card = dir.Sub("card", "CLIP");
        var data = new byte[size];
        new Random(size).NextBytes(data);
        var file = new SourceFile("CLIP/x.mov", size, When);
        var options = new OffloadOptions { ChunkSize = Chunk, BufferCount = 4 };
        using var pool = new BufferPool(options.BufferCount, options.ChunkSize);
        using (var w = new DestWriter(0, Path.GetDirectoryName(card)!, 1, options))
        {
            Feed(w, pool, data, 0, file);
            Assert.Equal(new DestFileResult(FileState.Written, null), w.Results[0]);
            Assert.Equal(size, w.Written);
            Assert.True(w.Unbuffered);
        }
        var final = Path.Combine(card, "x.mov");
        Assert.Equal(data, File.ReadAllBytes(final));
        Assert.Equal(When, File.GetLastWriteTimeUtc(final));
        Assert.False(File.GetAttributes(final).HasFlag(FileAttributes.Hidden));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.offload-part", SearchOption.AllDirectories));
    }

    [Fact]
    public void ForceBuffered_StillWritesExactBytes()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var data = new byte[Chunk + 7];
        new Random(5).NextBytes(data);
        var options = new OffloadOptions { ChunkSize = Chunk, BufferCount = 4, ForceBuffered = true };
        using var pool = new BufferPool(4, Chunk);
        using (var w = new DestWriter(0, card, 1, options))
        {
            Feed(w, pool, data, 0, new SourceFile("x.bin", data.Length, When));
            Assert.False(w.Unbuffered);
            Assert.Equal(FileState.Written, w.Results[0].State);
        }
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(card, "x.bin")));
    }

    [Fact]
    public void Abort_RemovesPartAndRecordsReason()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 1, new OffloadOptions { ChunkSize = Chunk });
        var file = new SourceFile("x.bin", 2 * Chunk, When);
        w.Begin(file);
        var buf = pool.Take();
        w.Write(new Chunk(buf, Chunk, 0, 1, pool));
        w.Abort(0, "source: cancelled");
        w.Wait();
        Assert.Equal(new DestFileResult(FileState.Failed, "source: cancelled"), w.Results[0]);
        Assert.Empty(Directory.GetFiles(card));
    }

    [Fact]
    public void FinalPathBlocked_FailsWithRenameErrorAndCleansUp()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        Directory.CreateDirectory(Path.Combine(card, "x.bin"));
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 1, new OffloadOptions { ChunkSize = Chunk });
        Feed(w, pool, [1, 2, 3], 0, new SourceFile("x.bin", 3, When));
        Assert.Equal(FileState.Failed, w.Results[0].State);
        Assert.StartsWith("rename", w.Results[0].Error);
        Assert.Empty(Directory.GetFiles(card));
    }

    [Fact]
    public void Verify_DetectsMatchMismatchAndSize()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var a = new byte[5000]; new Random(1).NextBytes(a);
        var b = new byte[5000]; new Random(2).NextBytes(b);
        var c = new byte[5000]; new Random(3).NextBytes(c);
        var files = new[] { new SourceFile("a", 5000, When), new SourceFile("b", 5000, When), new SourceFile("c", 5000, When) };
        var options = new OffloadOptions { ChunkSize = Chunk };
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 3, options);
        Feed(w, pool, a, 0, files[0]);
        Feed(w, pool, b, 1, files[1]);
        Feed(w, pool, c, 2, files[2]);
        var bytes = File.ReadAllBytes(Path.Combine(card, "b"));
        bytes[100] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(card, "b"), bytes);
        File.WriteAllBytes(Path.Combine(card, "c"), c[..4000]);

        w.Verify(files, [Xx(a), Xx(b), Xx(c)], HashKind.Xxh64, CancellationToken.None);

        Assert.Equal(FileState.Verified, w.Results[0].State);
        Assert.Equal(FileState.Failed, w.Results[1].State);
        Assert.StartsWith("HASH MISMATCH", w.Results[1].Error);
        Assert.Equal("size mismatch: 4000 != 5000", w.Results[2].Error);
        Assert.Equal(14000, w.Verified);
    }

    [Fact]
    public void FlushDevice_LeavesNoFile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        using var w = new DestWriter(0, card, 0, new OffloadOptions());
        w.FlushDevice();
        Assert.Empty(Directory.GetFiles(card));
    }

    [Fact]
    public void PartName_IsHiddenSiblingName()
    {
        Assert.Equal(".x.mov.offload-part", DestWriter.PartName("x.mov"));
        Assert.Equal("A/B/.x.mov.offload-part", DestWriter.PartName("A/B/x.mov"));
    }

    static FileHashes Xx(byte[] data) => new(XxHash64.HashToUInt64(data), null);

    // Spec 3.5: one hasher per algorithm, each on its own thread, every block shared by both (two consumers).
    [Fact]
    public void SourceHashers_Xxh64AndMd5_HashTheChunkStream()
    {
        var data = new byte[3 * Chunk + 5];
        new Random(9).NextBytes(data);
        using var pool = new BufferPool(4, Chunk);
        var hashers = SourceHasher.For(HashKind.Both, 2);
        try
        {
            Assert.Equal([HashKind.Xxh64, HashKind.Md5], hashers.Select(h => h.Kind));
            foreach (var h in hashers) h.Begin();
            for (long off = 0; off < data.Length; off += Chunk)
            {
                int n = (int)Math.Min(Chunk, data.Length - off);
                var buf = pool.Take();
                data.AsSpan((int)off, n).CopyTo(buf.Span);
                var chunk = new Chunk(buf, n, off, hashers.Count, pool);
                foreach (var h in hashers) h.Update(chunk);
            }
            foreach (var h in hashers) h.Finish(1);
            foreach (var h in hashers) h.Wait();
            var hashes = SourceHasher.Combine(hashers, 2);
            Assert.Equal(default, hashes[0]);
            Assert.Equal(XxHash64.HashToUInt64(data), hashes[1].Xxh64);
            Assert.Equal(System.Security.Cryptography.MD5.HashData(data), hashes[1].Md5);
            Assert.Single(SourceHasher.For(HashKind.Xxh64, 1));
        }
        finally
        {
            foreach (var h in hashers) h.Dispose();
        }
    }

    // Spec 3.5: in "both" a copy whose MD5 differs fails even when its xxh64 matches (a wrong source MD5 stands in for it).
    [Fact]
    public void Verify_Both_Md5Mismatch_Fails()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var a = new byte[5000]; new Random(1).NextBytes(a);
        var b = new byte[5000]; new Random(2).NextBytes(b);
        var files = new[] { new SourceFile("a", 5000, When), new SourceFile("b", 5000, When) };
        using var pool = new BufferPool(4, Chunk);
        using var w = new DestWriter(0, card, 2, new OffloadOptions { ChunkSize = Chunk });
        Feed(w, pool, a, 0, files[0]);
        Feed(w, pool, b, 1, files[1]);
        var wrongMd5 = System.Security.Cryptography.MD5.HashData(a);

        w.Verify(files, [new FileHashes(XxHash64.HashToUInt64(a), System.Security.Cryptography.MD5.HashData(a)), new FileHashes(XxHash64.HashToUInt64(b), wrongMd5)],
            HashKind.Both, CancellationToken.None);

        Assert.Equal(FileState.Verified, w.Results[0].State);
        Assert.Equal(FileState.Failed, w.Results[1].State);
        Assert.StartsWith("MD5 MISMATCH: dest ", w.Results[1].Error);
        Assert.Equal(10000, w.Verified);
    }
}
