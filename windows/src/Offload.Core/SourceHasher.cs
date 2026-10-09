using System.IO.Hashing;
using Offload.Core.Verification;

namespace Offload.Core;

/// Hashes the source stream on its own thread, one algorithm per hasher (spec 3.5): with MD5 a second hasher runs
/// beside xxh64 on its own thread, so the slower MD5 never holds up xxh64 or the card read — only the buffer pool,
/// when it falls a whole pool behind. Plan 10 (R2, spec 4.2): the xxh64 hasher also keeps XXH3-128 of the whole file
/// and XXH3-64 of every block (`blockSize` bytes, the last one shorter) — the block manifest and the MHL's xxh128.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker;
    readonly string algorithm;
    readonly Func<string, IFileHash> create;
    readonly int blockSize;
    IFileHash? stream;
    XxHash128? whole;
    List<ulong>? blocks;
    string? error;

    public HashKind Kind { get; }
    /// Lower-case hex per file; null when the file was not hashed to the end (it failed).
    public string?[] Values { get; }
    /// Per file: why this hash could not be computed (the hash itself threw), else null.
    public string?[] Errors { get; }
    /// Per file, with a block size: XXH3-128 of the whole file (lower-case hex), null when it was not hashed to the end.
    public string?[] Xxh128 { get; }
    /// Per file, with a block size: XXH3-64 of every block in order, null when it was not hashed to the end.
    public ulong[]?[] Blocks { get; }

    /// `blockSize` > 0: also the block hashes and XXH3-128 (the engine gives its chunk size: one block per chunk).
    public SourceHasher(HashKind kind, int fileCount, Func<string, IFileHash>? create = null, int blockSize = 0)
    {
        Kind = kind;
        this.create = create ?? HashAlgo.Create;
        this.blockSize = blockSize;
        algorithm = kind == HashKind.Md5 ? "md5" : "xxh64";
        worker = new SerialWorker("offload.hash." + algorithm);
        Values = new string?[fileCount];
        Errors = new string?[fileCount];
        Xxh128 = new string?[fileCount];
        Blocks = new ulong[]?[fileCount];
    }

    /// "md5 hash failed: …" when a hasher could not hash file `index` (English: it goes into the report JSON).
    public static string? ErrorOf(IReadOnlyList<SourceHasher> hashers, int index) =>
        hashers.FirstOrDefault(h => h.Errors[index] != null) is { } h ? $"{h.algorithm} hash failed: {h.Errors[index]}" : null;

    /// One hasher per kind in `kinds` (xxh64 always first; it also keeps the blocks and XXH3-128 when `blockSize` > 0).
    public static List<SourceHasher> For(HashKind kinds, int fileCount, Func<string, IFileHash>? create = null, int blockSize = 0)
    {
        var all = new List<SourceHasher> { new(HashKind.Xxh64, fileCount, create, blockSize) };
        if (HashKinds.Normalize(kinds).HasFlag(HashKind.Md5)) all.Add(new SourceHasher(HashKind.Md5, fileCount, create));
        return all;
    }

    /// File `index`'s hashes from every hasher (after Wait).
    public static FileHashes[] Combine(IReadOnlyList<SourceHasher> hashers, int fileCount)
    {
        var xx = hashers.FirstOrDefault(h => h.Kind == HashKind.Xxh64);
        var md5 = hashers.FirstOrDefault(h => h.Kind == HashKind.Md5);
        return Enumerable.Range(0, fileCount).Select(i => new FileHashes(
            xx?.Values[i] is string x ? Convert.ToUInt64(x, 16) : null,
            md5?.Values[i] is string m ? Convert.FromHexString(m) : null,
            xx?.Xxh128[i])).ToArray();
    }

    public void Begin() => worker.Post(() =>
    {
        error = null;
        stream = null;
        whole = blockSize > 0 ? new XxHash128() : null;
        blocks = blockSize > 0 ? [] : null;
        try { stream = create(algorithm); }
        catch (Exception e) { error = e.Message; }
    });

    /// The block always goes back to the pool, even when the hash throws — otherwise the pool runs dry and the copy
    /// hangs in BufferPool.Take. A hash that threw stops hashing this file; Finish records the error instead of a value.
    public void Update(Chunk c) => worker.Post(() =>
    {
        try
        {
            var data = c.Buffer.Span[..c.Length];
            stream?.Append(data);
            if (blocks != null && error == null)
            {
                // One chunk is one block: a chunk that is not the next block would make every later block hash wrong.
                if (c.Offset != (long)blocks.Count * blockSize) throw new InvalidOperationException($"block at {c.Offset} out of order");
                blocks.Add(XxHash3.HashToUInt64(data));
                whole!.Append(data);
            }
        }
        catch (Exception e)
        {
            error ??= e.Message;
            stream = null;
        }
        finally { c.Release(); }
    });

    public void Finish(int index) => worker.Post(() =>
    {
        try
        {
            if (error == null)
            {
                Values[index] = stream?.Finish();
                if (blocks != null)
                {
                    Xxh128[index] = Convert.ToHexStringLower(whole!.GetCurrentHash());
                    Blocks[index] = [.. blocks];
                }
            }
        }
        catch (Exception e) { error = e.Message; }
        Errors[index] = error;
        stream = null;
        whole = null;
        blocks = null;
        error = null;
    });

    public void Abort() => worker.Post(() =>
    {
        stream = null;
        whole = null;
        blocks = null;
        error = null;
    });

    public void Wait() => worker.Wait();

    public void Dispose() => worker.Dispose();
}
