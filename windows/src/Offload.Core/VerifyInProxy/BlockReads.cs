namespace Offload.Core.VerifyInProxy;

/// Reading blocks of a destination's copy straight from the disk for the verifier: the fill-in after a proxy (spec 6 —
/// only what the tap did not read) and the classic background read (all of it, a fresh verifier). Complete reads only
/// through DiskBlocks — a handle that really bypasses the file cache; CompleteClassic (VerifySession, ruling I3) through
/// FileReader, the same unless unbuffered reads are impossible there. Only whole blocks reach the verifier.
public static class BlockReads
{
    /// Reads every block the verifier still lacks, then once more every block that did not match (spec 10: "blok znovu
    /// bez cache" — matching now is an unstable read, not a failure, and the verifier records it); returns the verdict.
    /// `beforeBlock`: false stops (a pause that ended in a cancel) — OperationCanceledException, as `ct`. `progress`:
    /// bytes read. IOError when the file cannot be opened or read (a disk unplugged) or its volume would serve the reads
    /// from the cache.
    public static BlockOutcome Complete(string path, BlockVerifier verifier, Func<bool>? beforeBlock = null, Action<int>? progress = null,
        CancellationToken ct = default)
    {
        // Verified already by this session's reads (the tap, the cache read): nothing to open again.
        if (verifier.LengthObserved && verifier.Finish().Kind == BlockOutcomeKind.Verified) return verifier.Finish();
        using var disk = new DiskBlocks(path);
        verifier.ObserveLength(disk.Length);
        return Fill(verifier, (off, buffer, size) => disk.Read(off, buffer, size), beforeBlock, progress, ct);
    }

    /// Ruling I3, the classic read-back of stage 2 (a destination read in the background, the fill-in): like Complete, but
    /// through FileReader — unbuffered with a handle that really bypasses the cache, and only where that is impossible
    /// (the volume refuses or silently drops FILE_FLAG_NO_BUFFERING, an NTFS-compressed or EFS file) through the cache,
    /// as 0.5.x read back. `FromCache` true = some block was read that way (the report's notes say so). Any other error
    /// (the file gone, locked, the disk unplugged) is an IOError, never a cached read.
    public static (BlockOutcome Outcome, bool FromCache) CompleteClassic(string path, BlockVerifier verifier, Func<bool>? beforeBlock = null,
        Action<int>? progress = null, CancellationToken ct = default)
    {
        if (verifier.LengthObserved && verifier.Finish().Kind == BlockOutcomeKind.Verified) return (verifier.Finish(), false);
        using var reader = new FileReader(path);
        verifier.ObserveLength(reader.Length);
        var outcome = Fill(verifier, (off, buffer, size) => reader.Read(buffer.Span[..size], off), beforeBlock, progress, ct);
        return (outcome, !reader.FromDisk);
    }

    /// Reads the verifier's missing blocks with `read` (offset, buffer, block size → bytes), then once more the blocks
    /// that did not match; only whole blocks reach the verifier.
    static BlockOutcome Fill(BlockVerifier verifier, Func<long, AlignedBuffer, int, int> read, Func<bool>? beforeBlock, Action<int>? progress,
        CancellationToken ct)
    {
        int size = verifier.Manifest.BlockSize;
        using var buffer = new AlignedBuffer(size);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var range in verifier.MissingRanges())
            {
                for (long off = range.Offset; off < range.Offset + range.Length; off += size)
                {
                    ct.ThrowIfCancellationRequested();
                    if (beforeBlock?.Invoke() == false) throw new OperationCanceledException();
                    ct.ThrowIfCancellationRequested();
                    int n = read(off, buffer, size);
                    if (n <= 0) break;   // shorter on the disk: ObserveLength already made it a mismatch
                    verifier.Feed(off, buffer.Span[..n]);
                    progress?.Invoke(n);
                }
            }
            var now = verifier.Finish();
            if (now.Kind != BlockOutcomeKind.Mismatch || now.Block < 0) return now;
        }
        return verifier.Finish();
    }

    /// The destination's result from a verdict (English texts, as every error in the report JSON — spec 2.6): a mismatch
    /// names the block and both hashes, a size difference both sizes; Incomplete leaves the copy "written".
    public static DestFileResult Result(BlockOutcome outcome, BlockVerifier verifier) => outcome.Kind switch
    {
        BlockOutcomeKind.Verified => new DestFileResult(FileState.Verified, null),
        BlockOutcomeKind.Mismatch when outcome.Block < 0 => new DestFileResult(FileState.Failed, $"size mismatch: {outcome.SizeOnDisk} != {verifier.Manifest.Size}"),
        BlockOutcomeKind.Mismatch => new DestFileResult(FileState.Failed,
            $"HASH MISMATCH: block {outcome.Block} dest {Format.Hex64(verifier.DiskHash(outcome.Block) ?? 0)} != source {Format.Hex64(verifier.Manifest.Blocks[outcome.Block])}"),
        _ => new DestFileResult(FileState.Written, null),
    };
}
