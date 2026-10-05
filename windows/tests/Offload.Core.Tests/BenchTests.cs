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
    public void Write_AlreadyCancelled_ThrowsAndLeavesNothing()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Bench.Write(dir.Path, 64 << 20, 1 << 20, cts.Token));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    // Cancelled while the temporary file is being written: stops inside the file, still deletes it.
    [Fact]
    public void Write_CancelledWhileWriting_ThrowsAndDeletesTempFile()
    {
        using var dir = new TempDir();
        using var cts = CancelWhenBenchFileAppears(dir.Path);
        Assert.ThrowsAny<OperationCanceledException>(() => Bench.Write(dir.Path, 512L << 20, 1 << 20, cts.Token));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Read_Cancelled_Throws()
    {
        using var dir = new TempDir();
        TestFs.WriteRandom(Path.Combine(dir.Path, "a.bin"), 300_000, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Bench.Read(dir.Path, 350_000, [64 * 1024], cts.Token));
    }

    /// Cancels as soon as a `.offload-bench-*` file exists in `dir` (i.e. in the middle of a write test).
    internal static CancellationTokenSource CancelWhenBenchFileAppears(string dir)
    {
        var cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!cts.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                if (Directory.GetFiles(dir, ".offload-bench-*").Length > 0)
                {
                    cts.Cancel();
                    return;
                }
                await Task.Delay(1);
            }
        });
        return cts;
    }

    [Fact]
    public void Hash_ReportsEightPasses() => Assert.Equal(8L * (1 << 20), Bench.Hash(1 << 20).Bytes);
}
