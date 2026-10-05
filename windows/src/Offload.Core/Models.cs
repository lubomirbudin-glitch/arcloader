namespace Offload.Core;

public sealed class OffloadOptions
{
    /// Read/write block size. Large sequential blocks are what cards and disks like best. Multiple of 4096.
    public int ChunkSize { get; set; } = 16 << 20;
    /// Blocks in flight. Lets a slower destination lag behind without stalling the card read.
    public int BufferCount { get; set; } = 16;
    public int ReadRetries { get; set; } = 3;
    public bool Verify { get; set; } = true;
    /// Files at least this large are flushed individually; the rest are flushed once at the end.
    public long FullSyncThreshold { get; set; } = 256L << 20;
    public bool AllowSameDevice { get; set; }
    /// Tests only: source and destinations in temp folders share a volume.
    internal bool AllowSourceVolume { get; set; }
    /// Tests only: behave as on a volume that refuses unbuffered writes.
    internal bool ForceBuffered { get; set; }
    /// Tests only: called on the copy thread just before file i is read (a deterministic point to cancel at).
    internal Action<int>? BeforeFileCopy { get; set; }
    /// Tests only: H.264/HEVC proxies with this encoder (e.g. libx264) instead of the probed one — parallel
    /// NVENC sessions from concurrently running tests can collide.
    internal string? ProxyEncoder { get; set; }
}

public enum FileState { Pending, Written, Verified, Failed }

public static class FileStateExtensions
{
    public static string Wire(this FileState s) => s switch
    {
        FileState.Written => "written",
        FileState.Verified => "verified",
        FileState.Failed => "failed",
        _ => "pending",
    };
}

public readonly record struct DestFileResult(FileState State, string? Error);

public sealed class OffloadPlan
{
    public required string Source { get; init; }
    public required SourceScan Scan { get; init; }
    public required string Date { get; init; }
    public required string CardName { get; init; }
    public required IReadOnlyList<string> DestRoots { get; init; }
    public IReadOnlyList<string> CardPaths => DestRoots.Select(r => Layout.CardPath(r, Date, CardName)).ToList();
}

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors);

public sealed class OffloadError : Exception
{
    public bool IsPreflight { get; }

    OffloadError(string message, bool preflight) : base(message) => IsPreflight = preflight;

    public static OffloadError Preflight(string message) => new(message, true);
    public static OffloadError Failed(string message) => new(message, false);
}
