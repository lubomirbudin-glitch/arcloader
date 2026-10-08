using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 5 and 11: the block verifier — any order, pieces, seeks, repeats; a flipped byte, a short or long file and a
/// missing block are never "verified".
public class BlockVerifierTests
{
    const int Block = 4096;

    static (byte[] Data, BlockManifest Manifest) File(long size, int seed = 1)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        var blocks = Enumerable.Range(0, BlockManifest.CountFor(size, Block))
            .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, (int)Math.Min(Block, size - (long)b * Block)))).ToList();
        return (data, new BlockManifest(size, Block, blocks));
    }

    static void FeedBlock(BlockVerifier v, byte[] data, int b) =>
        v.Feed((long)b * Block, data.AsSpan(b * Block, (int)Math.Min(Block, data.Length - (long)b * Block)));

    [Fact]
    public void Sequential_WholeBlocks_Verified()
    {
        var (data, m) = File(3 * Block + 100);
        var v = new BlockVerifier(m);
        for (int b = 0; b < m.Count; b++) FeedBlock(v, data, b);

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Empty(v.MissingRanges());
        Assert.Equal(data.Length, v.VerifiedBytes);
    }

    // Spec 11: a random order of reads gives the same result as a sequential one.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomOrder_Pieces_SameAsSequential(int seed)
    {
        var (data, m) = File(5 * Block + 1234, seed);
        var v = new BlockVerifier(m);
        var rnd = new Random(seed);
        // Pieces of random length at random offsets, overlapping, in random order, until every byte was given.
        var cuts = new List<(int Off, int Len)>();
        for (int off = 0; off < data.Length;)
        {
            int len = Math.Min(rnd.Next(1, Block + 700), data.Length - off);
            cuts.Add((off, len));
            off += len;
        }
        foreach (var (off, len) in cuts.OrderBy(_ => rnd.Next())) v.Feed(off, data.AsSpan(off, len));

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }

    [Fact]
    public void FlippedByte_IsAMismatch_AtItsBlock_AndStaysMissing()
    {
        var (data, m) = File(4 * Block);
        var bad = (byte[])data.Clone();
        bad[2 * Block + 17] ^= 0x01;
        var v = new BlockVerifier(m);
        for (int b = 0; b < m.Count; b++) FeedBlock(v, bad, b);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 2), v.Finish());
        Assert.Equal([new ByteRange(2 * Block, Block)], v.MissingRanges());
        Assert.Equal(XxHash3.HashToUInt64(bad.AsSpan(2 * Block, Block)), v.DiskHash(2));
        Assert.Null(v.DiskHash(1));
    }

    // Spec 6: what the tap never read (the header, the index at the end) is what the fill-in reads — nothing else.
    [Fact]
    public void Unread_HeaderAndTail_AreTheMissingRanges()
    {
        var (data, m) = File(6 * Block + 10);
        var v = new BlockVerifier(m);
        for (int b = 1; b <= 4; b++) FeedBlock(v, data, b);

        Assert.Equal(BlockOutcomeKind.Incomplete, v.Finish().Kind);
        Assert.Equal([new ByteRange(0, Block), new ByteRange(5 * Block, Block + 10)], v.MissingRanges());
    }

    [Fact]
    public void ShortLastBlock_FedInPieces_IsVerified()
    {
        var (data, m) = File(Block + 10);
        var v = new BlockVerifier(m);
        FeedBlock(v, data, 0);
        v.Feed(Block + 5, data.AsSpan(Block + 5, 5));
        Assert.Equal(BlockOutcomeKind.Incomplete, v.Finish().Kind);
        v.Feed(Block, data.AsSpan(Block, 5));

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }

    [Fact]
    public void EmptyFile_NothingToRead_Verified()
    {
        var v = new BlockVerifier(new BlockManifest(0, Block, []));
        v.ObserveLength(0);
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Empty(v.MissingRanges());
    }

    // Spec 11: a truncated file is found — also when every block the reader got matched.
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void OtherLengthOnDisk_IsAMismatch_EvenWithMatchingBlocks(int delta)
    {
        var (data, m) = File(2 * Block);
        var v = new BlockVerifier(m);
        FeedBlock(v, data, 0);
        FeedBlock(v, data, 1);
        v.ObserveLength(2 * Block + delta);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, -1, 2 * Block + delta), v.Finish());
    }

    [Fact]
    public void BytesPastTheCardsSize_AreIgnored()
    {
        var (data, m) = File(Block + 1);
        var v = new BlockVerifier(m);
        var longer = data.Concat(new byte[100]).ToArray();
        v.Feed(0, longer);

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Equal(longer.Length, v.FedBytes);
    }

    // Spec 5: reading a verified block again changes nothing — unless a whole read gives another answer (an I/O error).
    [Fact]
    public void RepeatedRead_Same_Ignored_Different_MismatchAndUnstable()
    {
        var (data, m) = File(2 * Block);
        var v = new BlockVerifier(m);
        FeedBlock(v, data, 0);
        FeedBlock(v, data, 1);
        FeedBlock(v, data, 0);
        v.Feed(10, new byte[20]);   // a piece of a verified block: ignored, whatever it holds
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Empty(v.UnstableBlocks);

        var other = (byte[])data.Clone();
        other[5] ^= 0xFF;
        FeedBlock(v, other, 0);

        Assert.Equal(new BlockOutcome(BlockOutcomeKind.Mismatch, 0), v.Finish());
        Assert.Equal([0], v.UnstableBlocks);
        Assert.Equal(Block, v.VerifiedBytes);
    }

    // Spec 10: a mismatching block read again without the cache and matching now is verified, with a warning.
    [Fact]
    public void Mismatch_ReadAgainAndMatching_VerifiedButUnstable()
    {
        var (data, m) = File(Block);
        var bad = (byte[])data.Clone();
        bad[0] ^= 1;
        var v = new BlockVerifier(m);
        v.Feed(0, bad);
        Assert.Equal(BlockOutcomeKind.Mismatch, v.Finish().Kind);

        v.Feed(0, data);

        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
        Assert.Equal([0], v.UnstableBlocks);
    }

    [Fact]
    public void TooManyBlocksInPieces_TheOldestIsDropped_AndStaysMissing()
    {
        var (data, m) = File((BlockVerifier.MaxPartials + 1) * Block);
        var v = new BlockVerifier(m);
        for (int b = 0; b <= BlockVerifier.MaxPartials; b++) v.Feed((long)b * Block, data.AsSpan(b * Block, 10));
        // Block 0 was the oldest piece: its first bytes are gone, the rest alone never completes it.
        for (int b = 1; b <= BlockVerifier.MaxPartials; b++) v.Feed((long)b * Block + 10, data.AsSpan(b * Block + 10, Block - 10));
        v.Feed(10, data.AsSpan(10, Block - 10));

        Assert.Equal(BlockOutcomeKind.Incomplete, v.Finish().Kind);
        Assert.Equal([new ByteRange(0, Block)], v.MissingRanges());
    }

    [Fact]
    public void Manifest_WrongBlockCount_Refused()
    {
        Assert.Throws<ArgumentException>(() => new BlockManifest(Block + 1, Block, [1UL]));
        Assert.Throws<ArgumentException>(() => new BlockManifest(0, Block, [1UL]));
    }

    [Fact]
    public void ConcurrentFeeds_FromSeveralConnections_Verified()
    {
        var (data, m) = File(64 * Block + 3);
        var v = new BlockVerifier(m);
        Parallel.For(0, m.Count, b => FeedBlock(v, data, b));
        Assert.Equal(BlockOutcomeKind.Verified, v.Finish().Kind);
    }
}
