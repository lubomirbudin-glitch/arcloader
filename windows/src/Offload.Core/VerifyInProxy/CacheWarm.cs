using System.Runtime.InteropServices;

namespace Offload.Core.VerifyInProxy;

/// How the proxy's reads of a clip verify its copy (R3, R10, spec 5 "registr kodeků").
public enum TapMode
{
    /// Not through the proxy: the fill-in reads it the classic way (other containers, other segments, no proxy).
    None,
    /// Mode A: ffmpeg decodes it itself — it reads through the tap (RangeServer). MOV, MP4, MXF.
    Http,
    /// Mode B: a vendor SDK reads it (BRAW, R3D, ARRIRAW): Offload reads and verifies it first, before the SDK.
    CacheWarm,
}

public static class TapRegistry
{
    static readonly string[] HttpExtensions = [".mov", ".mp4", ".mxf"];

    public static TapMode ModeFor(Clip clip) => clip.Format switch
    {
        ClipFormat.Video when HttpExtensions.Contains(Path.GetExtension(clip.Path), StringComparer.OrdinalIgnoreCase) => TapMode.Http,
        ClipFormat.Braw or ClipFormat.R3d or ClipFormat.Arriraw => TapMode.CacheWarm,
        _ => TapMode.None,
    };
}

/// Mode B (spec 5, spec 8): before the SDK decodes a clip, its files are evicted from the file cache (open and close a
/// NO_BUFFERING handle — measured in plan 10: the next cached read then comes from the disk, NTFS and exFAT) and read
/// once in order, every block into the verifier.
///
/// Batch 7–9 ruling ("mode B never claims verified from a cached read"): the verifying read goes through DiskBlocks — a
/// handle that really bypasses the file cache — not through the cache as the plan had it. The eviction alone is not
/// proof (another process's open handle or mapped view keeps the pages, preflight M4), so a cached read could have
/// verified pages instead of the disk. The cost: the cache is not warmed, the SDK reads the clip from the disk again
/// (≈ 2 × the file in disk reads for mode B instead of the plan's ≤ 1.05 ×; Offload's own read is still exactly 1 ×).
/// The eviction stays, so the SDK decodes what the disk holds now. `Fits` keeps the plan's limit (half the free RAM) on
/// which clips are verified before the SDK; larger ones are verified by the fill-in after their proxy.
public static partial class CacheWarm
{
    /// The free physical memory now (GlobalMemoryStatusEx); 0 when unknown.
    public static long AvailableMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : 0;
    }

    /// Whether `bytes` of a clip can stay in the cache until the SDK reads them: at most half the free RAM (`available`:
    /// tests give it; null = AvailableMemory()).
    public static bool Fits(long bytes, long? available = null) => bytes <= (available ?? AvailableMemory()) / 2;

    /// Evicts `path` from the cache, then reads it once straight from the disk into `verifier`; returns the bytes read.
    /// `beforeBlock` false or `ct` = OperationCanceledException; IOError when the file cannot be read, or its volume
    /// would serve the reads from the cache (nothing is fed then).
    public static long VerifyAndWarm(string path, BlockVerifier verifier, Func<bool>? beforeBlock = null, Action<int>? progress = null,
        CancellationToken ct = default)
    {
        using (NativeIO.OpenRead(path, unbuffered: true)) { }   // closing the last handle drops the cached pages
        using var disk = new DiskBlocks(path);
        verifier.ObserveLength(disk.Length);
        int size = verifier.Manifest.BlockSize;
        using var buffer = new AlignedBuffer(size);
        long total = 0;
        for (long off = 0; off < disk.Length; off += size)
        {
            ct.ThrowIfCancellationRequested();
            if (beforeBlock?.Invoke() == false) throw new OperationCanceledException();
            int n = disk.Read(off, buffer, size);
            if (n <= 0) break;
            verifier.Feed(off, buffer.Span[..n]);
            total += n;
            progress?.Invoke(n);
            if (n < size) break;
        }
        return total;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
