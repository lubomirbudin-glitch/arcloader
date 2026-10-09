using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 5 mode B, spec 8, spec 11: the RAW clip's file is verified by one read through the cache (≤ 1.05 × its size),
/// a flipped byte is found; the codec registry decides mode A or B.
public class CacheWarmTests
{
    const int Block = 64 * 1024;

    static (byte[] Data, BlockManifest Manifest) WriteUnbuffered(string path, int size)
    {
        var data = new byte[size];
        new Random(size).NextBytes(data);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // As the copy writes it (R8): past the file cache, the last block padded and trimmed.
        using (var h = NativeIO.OpenWrite(path, unbuffered: true, size))
        using (var buf = new AlignedBuffer(Block))
        {
            for (int off = 0; off < size; off += Block)
            {
                int n = Math.Min(Block, size - off);
                buf.Span.Clear();
                data.AsSpan(off, n).CopyTo(buf.Span);
                NativeIO.Write(h, buf.Span[..((n + 4095) / 4096 * 4096)], off, path);
            }
            NativeIO.SetLength(h, size, path);
        }
        var blocks = Enumerable.Range(0, BlockManifest.CountFor(size, Block))
            .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, size - b * Block)))).ToList();
        return (data, new BlockManifest(size, Block, blocks));
    }

    [Fact]
    public void Warm_VerifiesInOneRead_AtMostTheFileSize()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001_0001.braw");
        var (data, m) = WriteUnbuffered(path, 5 * Block + 333);
        var v = new BlockVerifier(m);

        long read = CacheWarm.VerifyAndWarm(path, v);

        Assert.Equal(data.Length, read);
        Assert.Equal(data.Length, v.FedBytes);   // spec 11: mode B reads the file once (≤ 1.05 ×)
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }

    [Fact]
    public void Warm_FlippedByteOnTheDisk_Mismatch()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001_0001.braw");
        var (_, m) = WriteUnbuffered(path, 3 * Block);
        // Read through the cache first: the flip below must still be seen (it goes past the cache, NTFS keeps it coherent).
        _ = File.ReadAllBytes(path);
        DiskFlip.Flip(path, Block + 5);
        var v = new BlockVerifier(m);

        CacheWarm.VerifyAndWarm(path, v);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 1), v.Finish());
    }

    [Fact]
    public void Warm_TruncatedFile_SizeMismatch()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.R3D");
        var (_, m) = WriteUnbuffered(path, 2 * Block);
        using (var f = new FileStream(path, FileMode.Open)) f.SetLength(Block);
        var v = new BlockVerifier(m);

        CacheWarm.VerifyAndWarm(path, v);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, -1, Block), v.Finish());
    }

    [Fact]
    public void Warm_CancelledBeforeABlock_Throws_NothingVerifiedAfter()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.braw");
        var (_, m) = WriteUnbuffered(path, 4 * Block);
        var v = new BlockVerifier(m);
        int blocks = 0;

        Assert.Throws<OperationCanceledException>(() => CacheWarm.VerifyAndWarm(path, v, () => ++blocks <= 2));
        Assert.Equal(2 * Block, v.VerifiedBytes);
    }

    [Fact]
    public void Fits_HalfTheFreeMemory()
    {
        Assert.True(CacheWarm.Fits(4L << 30, available: 8L << 30));
        Assert.False(CacheWarm.Fits((4L << 30) + 1, available: 8L << 30));
        Assert.True(CacheWarm.AvailableMemory() > 0);
    }

    [Theory]
    [InlineData(ClipFormat.Video, "CLIP/A001C001.mov", TapMode.Http)]
    [InlineData(ClipFormat.Video, "M4ROOT/CLIP/C0001.MP4", TapMode.Http)]
    [InlineData(ClipFormat.Video, "XDROOT/Clip/C0001.MXF", TapMode.Http)]
    [InlineData(ClipFormat.Video, "PRIVATE/AVCHD/00000.MTS", TapMode.None)]
    [InlineData(ClipFormat.Braw, "A001_0001.braw", TapMode.CacheWarm)]
    [InlineData(ClipFormat.R3d, "A001.RDC/A001_001.R3D", TapMode.CacheWarm)]
    [InlineData(ClipFormat.Arriraw, "A001C001.mxf", TapMode.CacheWarm)]
    [InlineData(ClipFormat.CanonRaw, "A001.CRM", TapMode.None)]
    public void Registry_ModeA_ForFfmpegContainers_ModeB_ForSdks(ClipFormat format, string path, TapMode want) =>
        Assert.Equal(want, TapRegistry.ModeFor(new Clip { Name = "x", Path = path, Format = format, Segments = [path] }));

    // ---- Batch 7–9 (beyond the brief): mode B never claims "verified" from a read the file cache could serve ----

    // A volume that silently drops NO_BUFFERING (its reads could come from the cache): refused, nothing verified.
    [Fact]
    public void Warm_HandleThatKeepsTheCache_Refused_NothingVerified()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001_0001.braw");
        var (_, m) = WriteUnbuffered(path, 3 * Block);
        var folder = Path.GetFullPath(dir.Path);
        NativeIO.TestVolumes[folder] = new NativeIO.TestVolume { IgnoreUnbuffered = true };
        try
        {
            var v = new BlockVerifier(m);
            Assert.Throws<IOError>(() => CacheWarm.VerifyAndWarm(path, v));
            Assert.Equal(0, v.FedBytes);
            Assert.NotEqual(BlockOutcomeKind.Verified, v.Finish().Kind);
        }
        finally
        {
            NativeIO.TestVolumes.TryRemove(folder, out _);
        }
    }

    // The file's pages kept in the cache by another open handle (an NLE, the indexer — the eviction cannot drop them):
    // the verifying read is not stopped by it and a byte changed on the disk is still found. (NTFS keeps the cached copy
    // coherent with the flip, so this does not tell a cached read from a disk read — the handle guard above does.)
    [Fact]
    public void Warm_PagesHeldInTheCacheByAnotherHandle_StillReadFromTheDisk()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001_0001.braw");
        var (data, m) = WriteUnbuffered(path, 4 * Block);
        using var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.None);
        Assert.Equal(data, ReadAll(other));   // its pages are in the cache now and stay while it is open
        DiskFlip.Flip(path, 3 * Block + 1);
        var v = new BlockVerifier(m);

        CacheWarm.VerifyAndWarm(path, v);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 3), v.Finish());
    }

    static byte[] ReadAll(Stream s)
    {
        using var all = new MemoryStream();
        s.CopyTo(all);
        return all.ToArray();
    }

    // NTFS-compressed files may be served through the cache even for non-cached reads (preflight M3): refused.
    [Fact]
    public void Warm_CompressedFile_Refused()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001_0001.braw");
        var (_, m) = WriteUnbuffered(path, 3 * Block);
        try { ToolProcess.Run(Path.Combine(Environment.SystemDirectory, "compact.exe"), ["/c", "/q", path]); }
        catch (ToolError) { return; }   // not NTFS here: nothing to test
        if (!File.GetAttributes(path).HasFlag(FileAttributes.Compressed)) return;
        var v = new BlockVerifier(m);

        Assert.Throws<IOError>(() => CacheWarm.VerifyAndWarm(path, v));
        Assert.Equal(0, v.FedBytes);
    }

    // The partial last block, and a block size of 16 MiB as the copy uses: one read, every block verified.
    [Fact]
    public void Warm_PartialLastBlock_Verified_ProgressAddsUp()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001.RDC", "A001_001.R3D");
        var (data, m) = WriteUnbuffered(path, 7 * Block + 4097);
        var v = new BlockVerifier(m);
        long progress = 0;

        CacheWarm.VerifyAndWarm(path, v, progress: n => progress += n);

        Assert.Equal(data.Length, progress);
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Equal(data.Length, v.VerifiedBytes);
    }
}
