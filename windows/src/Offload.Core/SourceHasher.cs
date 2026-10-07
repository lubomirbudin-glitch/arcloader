using Offload.Core.Verification;

namespace Offload.Core;

/// Hashes the source stream on its own thread, one algorithm per hasher (spec 3.5): with MD5 a second hasher runs
/// beside xxh64 on its own thread, so the slower MD5 never holds up xxh64 or the card read — only the buffer pool,
/// when it falls a whole pool behind.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker;
    readonly string algorithm;
    IFileHash? stream;

    public HashKind Kind { get; }
    /// Lower-case hex per file; null when the file was not hashed to the end (it failed).
    public string?[] Values { get; }

    public SourceHasher(HashKind kind, int fileCount)
    {
        Kind = kind;
        algorithm = kind == HashKind.Md5 ? "md5" : "xxh64";
        worker = new SerialWorker("offload.hash." + algorithm);
        Values = new string?[fileCount];
    }

    /// One hasher per kind in `kinds` (xxh64 always first).
    public static List<SourceHasher> For(HashKind kinds, int fileCount)
    {
        var all = new List<SourceHasher> { new(HashKind.Xxh64, fileCount) };
        if (HashKinds.Normalize(kinds).HasFlag(HashKind.Md5)) all.Add(new SourceHasher(HashKind.Md5, fileCount));
        return all;
    }

    /// File `index`'s hashes from every hasher (after Wait).
    public static FileHashes[] Combine(IReadOnlyList<SourceHasher> hashers, int fileCount)
    {
        var xx = hashers.FirstOrDefault(h => h.Kind == HashKind.Xxh64);
        var md5 = hashers.FirstOrDefault(h => h.Kind == HashKind.Md5);
        return Enumerable.Range(0, fileCount).Select(i => new FileHashes(
            xx?.Values[i] is string x ? Convert.ToUInt64(x, 16) : null,
            md5?.Values[i] is string m ? Convert.FromHexString(m) : null)).ToArray();
    }

    public void Begin() => worker.Post(() => stream = HashAlgo.Create(algorithm));

    public void Update(Chunk c) => worker.Post(() =>
    {
        stream?.Append(c.Buffer.Span[..c.Length]);
        c.Release();
    });

    public void Finish(int index) => worker.Post(() =>
    {
        Values[index] = stream?.Finish();
        stream = null;
    });

    public void Abort() => worker.Post(() => stream = null);

    public void Wait() => worker.Wait();

    public void Dispose() => worker.Dispose();
}
