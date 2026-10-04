namespace Offload.Core;

/// Fixed set of aligned buffers; Take blocks when all are in flight (backpressure).
internal sealed class BufferPool : IDisposable
{
    readonly AlignedBuffer[] all;
    readonly Stack<AlignedBuffer> free;
    readonly SemaphoreSlim available;
    readonly Lock gate = new();

    public int Size { get; }

    public BufferPool(int count, int size)
    {
        Size = size;
        all = Enumerable.Range(0, count).Select(_ => new AlignedBuffer(size)).ToArray();
        free = new Stack<AlignedBuffer>(all);
        available = new SemaphoreSlim(count, count);
    }

    public AlignedBuffer Take()
    {
        available.Wait();
        lock (gate) return free.Pop();
    }

    public void Give(AlignedBuffer buffer)
    {
        lock (gate) free.Push(buffer);
        available.Release();
    }

    public void Dispose()
    {
        foreach (var b in all) b.Dispose();
        available.Dispose();
    }
}

/// One block read from the card, shared by the hasher and every writer; back to the pool when all are done.
internal sealed class Chunk(AlignedBuffer buffer, int length, long offset, int consumers, BufferPool pool)
{
    int pending = consumers;

    public AlignedBuffer Buffer => buffer;
    public int Length => length;
    public long Offset => offset;

    public void Release()
    {
        if (Interlocked.Decrement(ref pending) == 0) pool.Give(buffer);
    }
}
