namespace Offload.Core.VerifyInProxy;

/// Reading blocks of a destination's copy straight from the disk for the verifier: the fill-in after a proxy (spec 6 —
/// only what the tap did not read) and the classic background read (all of it, a fresh verifier). Every read goes through
/// DiskBlocks — a handle that really bypasses the file cache — and only whole blocks reach the verifier.
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
        int size = verifier.Manifest.BlockSize;
        using var buffer = new AlignedBuffer(size);
        using var disk = new DiskBlocks(path);
        verifier.ObserveLength(disk.Length);
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var range in verifier.MissingRanges())
            {
                for (long off = range.Offset; off < range.Offset + range.Length; off += size)
                {
                    ct.ThrowIfCancellationRequested();
                    if (beforeBlock?.Invoke() == false) throw new OperationCanceledException();
                    ct.ThrowIfCancellationRequested();
                    int n = disk.Read(off, buffer, size);
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
