namespace Offload.Core.Tests;

public class BenchTests
{
    [Fact]
    public void Write_MeasuresWriteAndReadBack_LeavesNothing()
    {
        using var dir = new TempDir();
        var lines = Bench.Write(dir.Path, (1 << 20) + 5, 64 * 1024);
        Assert.Equal(["write 64 KB", "read  64 KB + xxh64"], lines.Select(l => l.Label));
        Assert.All(lines, l => Assert.Equal((1 << 20) + 5, l.Bytes));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Read_ReadsUpToLimitPerBlockSize()
    {
        using var dir = new TempDir();
        TestFs.WriteRandom(Path.Combine(dir.Path, "a.bin"), 300_000, 1);
        TestFs.WriteRandom(Path.Combine(dir.Path, "b.bin"), 100_000, 2);
        var lines = Bench.Read(dir.Path, 350_000, [64 * 1024, 1 << 20]);
        Assert.Equal(["read  64 KB", "read  1 MB"], lines.Select(l => l.Label));
        Assert.All(lines, l => Assert.InRange(l.Bytes, 350_000, 400_000));
    }

    [Fact]
    public void Hash_ReportsEightPasses() => Assert.Equal(8L * (1 << 20), Bench.Hash(1 << 20).Bytes);
}
