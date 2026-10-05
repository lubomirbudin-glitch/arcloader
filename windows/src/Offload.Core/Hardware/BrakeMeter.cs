namespace Offload.Core.Hardware;

/// The element holding the copy back. Element 0 = the card (source read), i = destination i.
/// MBps: its pace over the window; Utilization: of its USB link (null without one).
public readonly record struct Brake(int Element, double MBps, double? Utilization)
{
    public bool LinkFull => Utilization > 0.85;
}

/// Decides which element brakes the copy, from the backlog rather than from rates.
///
/// The engine runs in lockstep: one bounded buffer pool (BufferCount × ChunkSize), a chunk returns to it only
/// when every writer and the hasher are done with it. So the card and all disks move at the same average pace and
/// short-window rates only show chunk quantisation. What differs is the backlog: lag_i = ReadBytes − WrittenBytes[i].
/// The slow writer keeps the pool full (its lag is near the pool size), a fast one stays within a chunk or two.
/// → The writer with the largest lag brakes, if that lag (averaged over the window) is at least ~2 chunks
///   (half the pool when the pool is smaller) or grew by that much within the window (the pool is still filling
///   at the start); otherwise every disk keeps up and the card brakes.
public sealed class BrakeMeter
{
    readonly double threshold;
    readonly double windowSeconds;
    readonly IReadOnlyList<double?> linkMBps;
    readonly Queue<(double Time, long Read, long[] Written)> samples = new();

    /// linkMBps: ceiling per element (card first, then destinations), null = no USB link; optional.
    public BrakeMeter(long chunkBytes, int bufferCount, IReadOnlyList<double?>? linkMBps = null, double windowSeconds = 4)
    {
        long pool = chunkBytes * Math.Max(bufferCount, 1);
        threshold = Math.Min(2.0 * chunkBytes, pool / 2.0);
        this.windowSeconds = windowSeconds;
        this.linkMBps = linkMBps ?? [];
    }

    /// Feed one progress snapshot (seconds from any fixed start). Returns the brake once the window holds
    /// enough history (half the window), else null.
    public Brake? Add(double seconds, long readBytes, IReadOnlyList<long> writtenBytes)
    {
        if (samples.Count > 0 && (samples.Peek().Written.Length != writtenBytes.Count || seconds < samples.Last().Time))
            samples.Clear();
        samples.Enqueue((seconds, readBytes, writtenBytes.ToArray()));
        while (samples.Count > 2 && seconds - samples.Peek().Time > windowSeconds) samples.Dequeue();

        var first = samples.Peek();
        double span = seconds - first.Time;
        if (samples.Count < 2 || span <= 0 || span < windowSeconds / 2) return null;

        // A writer brakes when its backlog is large, or still growing (the pool is filling up: the card is faster).
        // Of those, the largest backlog; equal backlogs → the first. No such writer → the card.
        int brake = 0;
        double worst = double.NegativeInfinity;
        var last = samples.Last();
        for (int i = 0; i < writtenBytes.Count; i++)
        {
            double lag = samples.Average(s => (double)(s.Read - s.Written[i]));
            double growth = (last.Read - last.Written[i]) - (first.Read - first.Written[i]);
            if ((lag >= threshold || growth >= threshold) && lag > worst) { worst = lag; brake = i + 1; }
        }

        long moved = brake == 0 ? readBytes - first.Read : writtenBytes[brake - 1] - first.Written[brake - 1];
        double mbps = Math.Max(0, moved) / span / 1e6;
        double? util = brake < linkMBps.Count && linkMBps[brake] is double l && l > 0 ? mbps / l : null;
        return new Brake(brake, mbps, util);
    }
}
