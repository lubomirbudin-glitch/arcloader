using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Batch 7–9 data safety (beyond the brief): a block counts as verified only from bytes read off the destination through
/// a handle that really bypasses the file cache, and only when every byte of it was hashed; seeks and re-reads never
/// hide a mismatch; the partial last block; the tap's activity for the stall watchdog.
public class TapFileTests
{
    const int Block = 64 * 1024;

    static (byte[] Data, BlockManifest Manifest) Write(string path, int size)
    {
        var data = new byte[size];
        new Random(size).NextBytes(data);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return (data, ManifestOf(data));
    }

    static BlockManifest ManifestOf(byte[] data) => new(data.Length, Block,
        Enumerable.Range(0, BlockManifest.CountFor(data.Length, Block))
            .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, data.Length - b * Block)))).ToList());

    static byte[] ReadVia(ITapSource tap, long offset, int length)
    {
        var buffer = new byte[length];
        int done = 0;
        while (done < length)
        {
            int n = tap.Read(offset + done, buffer.AsSpan(done, Math.Min(256 << 10, length - done)));
            if (n <= 0) break;
            done += n;
        }
        return buffer[..done];
    }

    [Fact]
    public void RandomSeeksAndReReads_ExactBytes_PartialLastBlockVerified()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "A001C001.mov");
        var (data, m) = Write(path, 10 * Block + 333);
        var v = new BlockVerifier(m);
        using var tap = new TapFile(path, v);
        var random = new Random(7);

        for (int i = 0; i < 200; i++)
        {
            long at = random.NextInt64(data.Length);
            int length = (int)Math.Min(random.Next(1, 3 * Block), data.Length - at);
            Assert.Equal(data.AsSpan((int)at, length).ToArray(), ReadVia(tap, at, length));
        }
        Assert.Equal(data[(10 * Block)..], ReadVia(tap, 10 * Block, 333));
        Assert.Equal(0, tap.Read(data.Length, new byte[10]));

        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(path, v).Kind);
        Assert.Empty(v.UnstableBlocks);
    }

    // A piece of a block asked for: the whole block is still read from the disk and hashed — never a part of it.
    [Fact]
    public void OneByteAsked_WholeBlockReadAndHashed_OthersUntouched()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (data, m) = Write(path, 3 * Block);
        var v = new BlockVerifier(m);
        using var tap = new TapFile(path, v);

        Assert.Equal(data[(Block + 5)..(Block + 6)], ReadVia(tap, Block + 5, 1));

        Assert.Equal(Block, tap.ReadFromDisk);
        Assert.Equal(Block, v.VerifiedBytes);
        Assert.Equal([new ByteRange(0, Block), new ByteRange(2 * Block, Block)], v.MissingRanges());
    }

    // A wrong byte stays a mismatch however often ffmpeg seeks back to it — from the cache or the disk again.
    [Theory]
    [InlineData(2 * Block + 100)]
    [InlineData(10 * Block + 332)]   // the last byte of the partial last block
    public void FlippedOnTheDisk_SeeksAndReReads_NeverHideIt(long flipAt)
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (data, m) = Write(path, 10 * Block + 333);
        DiskFlip.Flip(path, flipAt);
        var v = new BlockVerifier(m);
        using var tap = new TapFile(path, v);
        var random = new Random(11);
        int bad = (int)(flipAt / Block);

        for (int i = 0; i < 300; i++)
        {
            long at = random.Next(0, data.Length);
            ReadVia(tap, at, (int)Math.Min(random.Next(1, Block), data.Length - at));
            ReadVia(tap, (long)bad * Block, 10);   // and back to the bad block, cached or evicted by now
        }

        Assert.True(tap.ReadFromDisk > 2L * data.Length, "the bad block was read from the disk again and again");
        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, bad), v.Finish());
        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, bad), BlockReads.Complete(path, v));
        Assert.Equal(FileState.Failed, BlockReads.Result(v.Finish(), v).State);
        Assert.StartsWith($"HASH MISMATCH: block {bad} ", BlockReads.Result(v.Finish(), v).Error);
        Assert.Empty(v.UnstableBlocks);
    }

    // Spec 10: a block that read wrong once and right later is an unstable read — recorded, never silently forgotten.
    [Fact]
    public void WrongThenRight_RecordedAsUnstable()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (_, m) = Write(path, 8 * Block);
        var v = new BlockVerifier(m);
        DiskFlip.Flip(path, 2 * Block + 9);
        using (var first = new TapFile(path, v)) ReadVia(first, 2 * Block, 1);
        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 2), v.Finish());
        DiskFlip.Flip(path, 2 * Block + 9);   // back as it was

        using (var again = new TapFile(path, v)) ReadVia(again, 2 * Block, 1);

        Assert.Equal([2], v.UnstableBlocks);
    }

    // The file on the disk longer or shorter than the card's: never verified, whatever is read.
    [Theory]
    [InlineData(+1)]
    [InlineData(-1)]
    [InlineData(-Block)]
    public void OtherLengthOnTheDisk_SizeMismatch(int delta)
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (data, _) = Write(path, 4 * Block);
        var card = new byte[data.Length - delta];
        data.AsSpan(0, Math.Min(data.Length, card.Length)).CopyTo(card);
        var v = new BlockVerifier(ManifestOf(card));
        using (var tap = new TapFile(path, v)) ReadVia(tap, 0, data.Length);

        var outcome = BlockReads.Complete(path, v);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, -1, data.Length), outcome);
        Assert.Equal($"size mismatch: {data.Length} != {card.Length}", BlockReads.Result(outcome, v).Error);
    }

    // An empty copy is verified only once a reader opened it and found it empty.
    [Fact]
    public void EmptyFile_VerifiedOnlyAfterOpening()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "empty.wav");
        File.WriteAllBytes(path, []);
        var v = new BlockVerifier(new BlockManifest(0, Block, []));
        Assert.False(v.LengthObserved);

        Assert.Equal(BlockOutcomeKind.Verified, BlockReads.Complete(path, v).Kind);
        Assert.True(v.LengthObserved);
        Assert.Throws<IOError>(() => BlockReads.Complete(Path.Combine(dir.Path, "missing.wav"), new BlockVerifier(new BlockManifest(0, Block, []))));
    }

    // A volume that silently ignores NO_BUFFERING (the handle reads through the cache): never used to verify.
    [Fact]
    public void HandleWithoutNoBuffering_Refused_NothingVerified()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (_, m) = Write(path, 3 * Block);
        NativeIO.TestVolumes[Path.GetFullPath(dir.Path)] = new NativeIO.TestVolume { IgnoreUnbuffered = true };
        try
        {
            var v = new BlockVerifier(m);
            Assert.Throws<IOError>(() => new TapFile(path, v));
            Assert.Throws<IOError>(() => BlockReads.Complete(path, v));
            Assert.Equal(0, v.VerifiedBytes);
            Assert.NotEqual(BlockOutcomeKind.Verified, v.Finish().Kind);
        }
        finally
        {
            NativeIO.TestVolumes.TryRemove(Path.GetFullPath(dir.Path), out _);
        }
    }

    // NTFS-compressed (or EFS) streams may be served through the cache even without buffering asked for (preflight M3).
    [Fact]
    public void CompressedFile_Refused()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (_, m) = Write(path, 3 * Block);
        try { ToolProcess.Run(Path.Combine(Environment.SystemDirectory, "compact.exe"), ["/c", "/q", path]); }
        catch (ToolError) { return; }   // not NTFS here: nothing to test
        if (!File.GetAttributes(path).HasFlag(FileAttributes.Compressed)) return;
        var v = new BlockVerifier(m);

        Assert.Throws<IOError>(() => new TapFile(path, v));
        Assert.Throws<IOError>(() => BlockReads.Complete(path, v));
        Assert.Equal(0, v.VerifiedBytes);
    }

    // The tap after its lease ended: no disk read any more.
    [Fact]
    public void Disposed_ReadsNothing()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (_, m) = Write(path, 3 * Block);
        var v = new BlockVerifier(m);
        var tap = new TapFile(path, v);
        ReadVia(tap, 0, 10);
        tap.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tap.Read(Block, new byte[10]));
        Assert.Equal(Block, tap.ReadFromDisk);
    }

    // Final review, item 4: the lease ends (the proxy's watchdog killed ffmpeg) while the tap's read hangs on the disk —
    // Dispose returns at once, never waiting for that read; the read ends refused once the disk answers.
    [Fact]
    public async Task Dispose_WhileAReadHangsOnTheDisk_ReturnsAtOnce_TheReadEndsRefused()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (_, m) = Write(path, 3 * Block);
        var tap = new TapFile(path, new BlockVerifier(m));
        ReadVia(tap, 0, 10);   // block 0 in the cache
        using var inRead = new ManualResetEventSlim();
        using var disk = new ManualResetEventSlim();
        Assert.True(DiskBlocks.ReadHooks.TryAdd(Path.GetFullPath(path), () =>
        {
            inRead.Set();
            disk.Wait(TimeSpan.FromSeconds(30));
        }));
        try
        {
            var reading = Task.Run(() => tap.Read(Block, new byte[10]));
            Assert.True(inRead.Wait(TimeSpan.FromSeconds(10)));

            var disposing = Task.Run(tap.Dispose);
            Assert.Same(disposing, await Task.WhenAny(disposing, Task.Delay(TimeSpan.FromSeconds(5))));   // never waits for the read
            Assert.False(reading.IsCompleted);

            disk.Set();
            var e = await Assert.ThrowsAnyAsync<Exception>(() => reading);
            Assert.True(e is ObjectDisposedException or IOError, e.ToString());
            Assert.Throws<ObjectDisposedException>(() => tap.Read(0, new byte[10]));
        }
        finally
        {
            DiskBlocks.ReadHooks.TryRemove(Path.GetFullPath(path), out _);
            disk.Set();
        }
    }

    // For the stall watchdog: a block is read in pieces and each piece is activity.
    [Fact]
    public void BlockReadInPieces_EachPieceReported()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        int size = 4 * DiskBlocks.PieceBytes + 4096;
        var data = new byte[size];
        new Random(3).NextBytes(data);
        File.WriteAllBytes(path, data);
        int block = 8 * DiskBlocks.PieceBytes;
        using var disk = new DiskBlocks(path);
        using var buffer = new AlignedBuffer(block);
        var pieces = new List<int>();

        int n = disk.Read(0, buffer, block, pieces.Add);

        Assert.Equal(size, n);
        Assert.Equal([.. Enumerable.Repeat(DiskBlocks.PieceBytes, 4), 4096], pieces);
        Assert.Equal(data, buffer.Span[..n].ToArray());
    }

    [Fact]
    public void TapActivity_CountsDiskBytes()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.mov");
        var (_, m) = Write(path, 3 * Block);
        using var tap = new TapFile(path, new BlockVerifier(m));
        Assert.Equal(0, ((ITapSource)tap).Activity);
        ReadVia(tap, 0, 1);
        Assert.Equal(Block, ((ITapSource)tap).Activity);
    }

    // The renderer finds the tap's activity by the URL it reads: the bytes served plus the source's own disk activity.
    [Fact]
    public async Task ActivityOf_TheUrl_RisesWithBytesServed_GoneAfterUnregisterAndDispose()
    {
        var data = new byte[100_000];
        var server = new RangeServer();
        var url = server.Register(new Fixed(data), "a.mov");
        var activity = RangeServer.ActivityOf(url);
        Assert.NotNull(activity);
        Assert.Equal(0, activity());

        using (var http = new HttpClient()) await http.GetByteArrayAsync(url);

        Assert.Equal(data.Length, activity());
        var token = new Uri(url).AbsolutePath.Split('/')[1];
        Assert.Null(RangeServer.ActivityOf($"http://127.0.0.1:{server.Port}/{token}/0000000000000000/a.mov"));
        Assert.Null(RangeServer.ActivityOf($"http://127.0.0.1:{server.Port}/{new string(token.Reverse().ToArray())}/{new Uri(url).AbsolutePath.Split('/')[2]}/a.mov"));
        Assert.Null(RangeServer.ActivityOf("http://127.0.0.1:1/x/y/z.mov"));
        Assert.Null(RangeServer.ActivityOf("not a url"));
        server.Unregister(url);
        Assert.Null(RangeServer.ActivityOf(url));
        var again = server.Register(new Fixed(data), "a.mov");
        server.Dispose();
        Assert.Null(RangeServer.ActivityOf(again));
    }

    sealed class Fixed(byte[] data) : ITapSource
    {
        public long Length => data.Length;

        public int Read(long offset, Span<byte> buffer)
        {
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
    }
}
