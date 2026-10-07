using System.IO.Hashing;

namespace Offload.Core.Tests;

public class HashingTests
{
    [Fact]
    public void KnownVectors()
    {
        using var dir = new TempDir();
        using var buf = new AlignedBuffer(4096);
        var empty = Path.Combine(dir.Path, "empty");
        var abc = Path.Combine(dir.Path, "abc");
        File.WriteAllBytes(empty, []);
        File.WriteAllText(abc, "abc");
        Assert.Equal((0xef46db3751d8e999UL, 0L), Hashing.HashFile(empty, buf));
        Assert.Equal((0x44bc2cf5ad770999UL, 3L), Hashing.HashFile(abc, buf));
    }

    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(3 * 4096 + 1)]
    public void MatchesInMemoryHash_AcrossBlockBoundaries(int size)
    {
        using var dir = new TempDir();
        using var buf = new AlignedBuffer(4096);
        var path = Path.Combine(dir.Path, "f");
        var data = TestFs.WriteRandom(path, size, size);
        long progress = 0;
        var (hash, read) = Hashing.HashFile(path, buf, n => progress += n);
        Assert.Equal(XxHash64.HashToUInt64(data), hash);
        Assert.Equal(size, read);
        Assert.Equal(size, progress);
    }

    // Spec 3.5: xxh64 and MD5 in one read — RFC 1321 vectors, and across block boundaries.
    [Fact]
    public void HashFile_Both_Md5Vectors()
    {
        using var dir = new TempDir();
        using var buf = new AlignedBuffer(4096);
        var empty = Path.Combine(dir.Path, "empty");
        var abc = Path.Combine(dir.Path, "abc");
        File.WriteAllBytes(empty, []);
        File.WriteAllText(abc, "abc");
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", Hashing.HashFile(empty, buf, HashKind.Both).Hashes.Md5Hex);
        var (h, size) = Hashing.HashFile(abc, buf, HashKind.Both);
        Assert.Equal(("900150983cd24fb0d6963f7d28e17f72", 0x44bc2cf5ad770999UL, 3L), (h.Md5Hex, h.Xxh64!.Value, size));
        Assert.Null(Hashing.HashFile(abc, buf, HashKind.Xxh64).Hashes.Md5);

        var big = Path.Combine(dir.Path, "big");
        var data = TestFs.WriteRandom(big, 3 * 4096 + 1, 7);
        long progress = 0;
        var (hb, read) = Hashing.HashFile(big, buf, HashKind.Md5, n => progress += n);   // R16: MD5 alone still brings xxh64
        Assert.Equal(System.Security.Cryptography.MD5.HashData(data), hb.Md5);
        Assert.Equal(XxHash64.HashToUInt64(data), hb.Xxh64);
        Assert.Equal((data.Length, (long)data.Length), (read, progress));   // both hashes from one read of the file
    }

    [Fact]
    public void HashKinds_Xxh64Always()
    {
        Assert.Equal(HashKind.Both, HashKinds.Normalize(HashKind.Md5));
        Assert.Equal(["xxh64", "md5"], HashKinds.Names(HashKind.Md5));
        Assert.Equal(["xxh64"], HashKinds.Names(HashKind.Xxh64));
        Assert.Equal(HashKind.Both, HashKinds.FromNames(["xxh64", "md5"]));
        Assert.Equal(HashKind.Xxh64, HashKinds.FromNames(null));
        Assert.Equal(HashKind.Xxh64, HashKinds.FromNames(["xxh64"]));
    }
}
