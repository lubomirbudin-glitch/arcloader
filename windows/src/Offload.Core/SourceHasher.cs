using System.IO.Hashing;

namespace Offload.Core;

/// Hashes the source stream on its own thread, so hashing never blocks the card read.
internal sealed class SourceHasher : IDisposable
{
    readonly SerialWorker worker = new("offload.hash");
    XxHash64? stream;

    public ulong?[] Hashes { get; }

    public SourceHasher(int fileCount) => Hashes = new ulong?[fileCount];

    public void Begin() => worker.Post(() => stream = new XxHash64());

    public void Update(Chunk c) => worker.Post(() =>
    {
        stream?.Append(c.Buffer.Span[..c.Length]);
        c.Release();
    });

    public void Finish(int index) => worker.Post(() =>
    {
        Hashes[index] = stream?.GetCurrentHashAsUInt64();
        stream = null;
    });

    public void Abort() => worker.Post(() => stream = null);

    public void Wait() => worker.Wait();

    public void Dispose() => worker.Dispose();
}
