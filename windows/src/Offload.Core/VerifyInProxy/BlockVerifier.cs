using System.IO.Hashing;

namespace Offload.Core.VerifyInProxy;

/// Spec 2, 4.5: what the card gave for one file — its size and the XXH3-64 of every block (`BlockSize` bytes, the last one
/// shorter; none for an empty file). The block manifest's record and what a BlockVerifier checks a destination against.
public sealed record BlockManifest
{
    public long Size { get; }
    public int BlockSize { get; }
    public IReadOnlyList<ulong> Blocks { get; }

    public BlockManifest(long size, int blockSize, IReadOnlyList<ulong> blocks)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
        if (blocks.Count != CountFor(size, blockSize))
            throw new ArgumentException($"{blocks.Count} block hashes for {size} bytes in blocks of {blockSize}", nameof(blocks));
        Size = size;
        BlockSize = blockSize;
        Blocks = blocks;
    }

    /// How many blocks `size` bytes make (0 for an empty file).
    public static int CountFor(long size, int blockSize) => (int)((size + blockSize - 1) / blockSize);

    public int Count => Blocks.Count;
    public long Offset(int block) => (long)block * BlockSize;
    public int Length(int block) => (int)Math.Min(BlockSize, Size - Offset(block));
}

public enum BlockOutcomeKind { Verified, Mismatch, Incomplete }

/// The verdict on one file × destination. `Block`: the first block that did not match (Mismatch), or -1 when the file on
/// the disk has another size (`SizeOnDisk`).
public readonly record struct BlockOutcome(BlockOutcomeKind Kind, int Block = -1, long? SizeOnDisk = null);

/// A byte range of the file, `Length` bytes from `Offset`.
public readonly record struct ByteRange(long Offset, long Length);

/// Spec 5 (BlockVerifier): every byte read from a destination — by the proxy tap in any order and with seeks, by the
/// fill-in, by a background read — goes in through Feed; a block is checked as soon as all its bytes are in. Whole-block
/// feeds are hashed straight away; pieces of a block are put together by their offset (at most MaxPartials blocks at a
/// time — the oldest is dropped and simply stays missing). A verified block read again: a whole read is hashed again and
/// a different answer makes the block a mismatch (spec 5: "nesoulad při opakování = chyba I/O"); a piece is ignored. A
/// mismatching block fed whole again and matching now is verified, but counted as an unstable read (spec 10). Bytes past
/// the manifest's size are ignored here — ObserveLength tells the verifier how long the file on the disk is.
/// Thread-safe: the tap feeds from several connections at once.
public sealed class BlockVerifier
{
    public const int MaxPartials = 8;

    enum State : byte { Missing, Partial, Verified, Mismatch }

    sealed class Partial(int length)
    {
        public readonly byte[] Data = new byte[length];
        public readonly List<(long Start, long End)> Covered = [];
        public long LastUse;
    }

    readonly Lock gate = new();
    readonly State[] state;
    readonly ulong?[] diskHash;
    readonly Dictionary<int, Partial> partials = [];
    readonly HashSet<int> unstable = [];
    long tick, verifiedBytes, fedBytes;
    long? sizeOnDisk;
    bool lengthObserved;

    public BlockManifest Manifest { get; }

    public BlockVerifier(BlockManifest manifest)
    {
        Manifest = manifest;
        state = new State[manifest.Count];
        diskHash = new ulong?[manifest.Count];
    }

    /// Bytes of verified blocks so far.
    public long VerifiedBytes
    {
        get { lock (gate) return verifiedBytes; }
    }

    /// Every byte handed in so far, read again or not (spec 11: mode B reads at most 1.05 × the file).
    public long FedBytes
    {
        get { lock (gate) return fedBytes; }
    }

    /// Blocks that gave two different answers on two reads (spec 10: "varování o nestabilním čtení"), in order.
    public IReadOnlyList<int> UnstableBlocks
    {
        get { lock (gate) return unstable.Order().ToList(); }
    }

    /// The XXH3-64 the disk gave for a mismatching block (spec 10: the same block wrong on several destinations), else null.
    public ulong? DiskHash(int block)
    {
        lock (gate) return state[block] == State.Mismatch ? diskHash[block] : null;
    }

    /// The length of the file on the disk, as a reader found it: another length than the card's is a mismatch.
    public void ObserveLength(long length)
    {
        lock (gate)
        {
            lengthObserved = true;
            if (length != Manifest.Size) sizeOnDisk = length;
        }
    }

    /// A reader has told the file's length on the disk (an empty file is verified only by that).
    public bool LengthObserved
    {
        get { lock (gate) return lengthObserved; }
    }

    public void Feed(long offset, ReadOnlySpan<byte> data)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (gate)
        {
            fedBytes += data.Length;
            long end = Math.Min(offset + data.Length, Manifest.Size);
            for (long at = offset; at < end;)
            {
                int b = (int)(at / Manifest.BlockSize);
                long blockStart = Manifest.Offset(b), blockEnd = blockStart + Manifest.Length(b);
                long upTo = Math.Min(end, blockEnd);
                var piece = data.Slice((int)(at - offset), (int)(upTo - at));
                if (at == blockStart && upTo == blockEnd) Whole(b, piece);
                else Piece(b, at - blockStart, piece);
                at = upTo;
            }
        }
    }

    void Whole(int b, ReadOnlySpan<byte> data)
    {
        partials.Remove(b);
        ulong got = XxHash3.HashToUInt64(data);
        bool same = got == Manifest.Blocks[b];
        switch (state[b])
        {
            case State.Verified when !same:
                // Verified before, another answer now: the disk (or the way to it) does not read the same twice.
                unstable.Add(b);
                Set(b, State.Mismatch, got);
                break;
            case State.Verified:
                break;
            case State.Mismatch when same:
                unstable.Add(b);
                Set(b, State.Verified, null);
                break;
            default:
                Set(b, same ? State.Verified : State.Mismatch, same ? null : got);
                break;
        }
    }

    void Piece(int b, long at, ReadOnlySpan<byte> data)
    {
        if (state[b] is State.Verified or State.Mismatch) return;   // a whole read decides those
        if (!partials.TryGetValue(b, out var p))
        {
            if (partials.Count >= MaxPartials)
            {
                var oldest = partials.MinBy(kv => kv.Value.LastUse).Key;
                partials.Remove(oldest);
                state[oldest] = State.Missing;
            }
            p = partials[b] = new Partial(Manifest.Length(b));
            state[b] = State.Partial;
        }
        p.LastUse = ++tick;
        data.CopyTo(p.Data.AsSpan((int)at));
        Add(p.Covered, at, at + data.Length);
        if (p.Covered is [(0, var e)] && e == p.Data.Length) Whole(b, p.Data);
    }

    static void Add(List<(long Start, long End)> covered, long start, long end)
    {
        covered.Add((start, end));
        covered.Sort();
        var merged = new List<(long Start, long End)>();
        foreach (var (s, e) in covered)
        {
            if (merged.Count > 0 && s <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, e));
            else merged.Add((s, e));
        }
        covered.Clear();
        covered.AddRange(merged);
    }

    void Set(int b, State s, ulong? hash)
    {
        if (state[b] == State.Verified) verifiedBytes -= Manifest.Length(b);
        state[b] = s;
        diskHash[b] = hash;
        if (s == State.Verified) verifiedBytes += Manifest.Length(b);
    }

    /// The blocks not verified yet (missing, put together only in part, or mismatching), adjacent ones joined — what the
    /// fill-in reads (spec 6: "nic se nečte celé znovu").
    public IReadOnlyList<ByteRange> MissingRanges()
    {
        lock (gate)
        {
            var ranges = new List<ByteRange>();
            for (int b = 0; b < state.Length; b++)
            {
                if (state[b] == State.Verified) continue;
                long off = Manifest.Offset(b);
                if (ranges.Count > 0 && ranges[^1].Offset + ranges[^1].Length == off)
                    ranges[^1] = ranges[^1] with { Length = ranges[^1].Length + Manifest.Length(b) };
                else ranges.Add(new ByteRange(off, Manifest.Length(b)));
            }
            return ranges;
        }
    }

    /// Verified: every block matched (and the file has the card's size, when a reader told). Mismatch: the size, or the
    /// first block that does not match. Incomplete: blocks are still missing.
    public BlockOutcome Finish()
    {
        lock (gate)
        {
            if (sizeOnDisk is long other) return new BlockOutcome(BlockOutcomeKind.Mismatch, -1, other);
            for (int b = 0; b < state.Length; b++)
                if (state[b] == State.Mismatch) return new BlockOutcome(BlockOutcomeKind.Mismatch, b);
            return state.All(s => s == State.Verified) ? new BlockOutcome(BlockOutcomeKind.Verified) : new BlockOutcome(BlockOutcomeKind.Incomplete);
        }
    }
}
