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

        w.Verify(files, [XxHash64.HashToUInt64(a), XxHash64.HashToUInt64(b), XxHash64.HashToUInt64(c)], CancellationToken.None);

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

    [Fact]
    public void SourceHasher_HashesChunkStream()
    {
        var data = new byte[3 * Chunk + 5];
        new Random(9).NextBytes(data);
        using var pool = new BufferPool(4, Chunk);
        using var hasher = new SourceHasher(2);
        hasher.Begin();
        for (long off = 0; off < data.Length; off += Chunk)
        {
            int n = (int)Math.Min(Chunk, data.Length - off);
            var buf = pool.Take();
            data.AsSpan((int)off, n).CopyTo(buf.Span);
            hasher.Update(new Chunk(buf, n, off, 1, pool));
        }
        hasher.Finish(1);
        hasher.Wait();
        Assert.Null(hasher.Hashes[0]);
        Assert.Equal(XxHash64.HashToUInt64(data), hasher.Hashes[1]);
    }
}
