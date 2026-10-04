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
}
