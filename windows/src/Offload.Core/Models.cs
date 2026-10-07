namespace Offload.Core;

public sealed class OffloadOptions
{
    /// Read/write block size. Large sequential blocks are what cards and disks like best. Multiple of 4096.
    public int ChunkSize { get; set; } = 16 << 20;
    /// Blocks in flight. Lets a slower destination lag behind without stalling the card read.
    public int BufferCount { get; set; } = 16;
    public int ReadRetries { get; set; } = 3;
    public bool Verify { get; set; } = true;
    /// The checksums of a full copy (spec 3.5): xxh64, or xxh64 + MD5 (R16 — never MD5 alone). An append uses the
    /// earlier backup's instead (OffloadPlan.Hashes).
    public HashKind Hashes { get; set; } = HashKind.Xxh64;
    /// Files at least this large are flushed individually; the rest are flushed once at the end.
    public long FullSyncThreshold { get; set; } = 256L << 20;
    public bool AllowSameDevice { get; set; }
    /// Tests only: source and destinations in temp folders share a volume.
    internal bool AllowSourceVolume { get; set; }
    /// Tests only: behave as on a volume that refuses unbuffered writes.
    internal bool ForceBuffered { get; set; }
    /// Tests only: called on the copy thread just before file i is read (a deterministic point to cancel at).
    internal Action<int>? BeforeFileCopy { get; set; }
    /// Tests only: called on the copy thread after every file is copied and flushed, just before verification.
    internal Action? BeforeVerify { get; set; }
    /// Tests only: the free bytes of the volume of a path, instead of NativeIO.AvailableBytes.
    internal Func<string, long?>? FreeSpace { get; set; }
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

public enum PlanMode { Full, Append }

public sealed class OffloadPlan
{
    public required string Source { get; init; }
    /// What is copied: the whole card, or for an append only the new files and their folders.
    public required SourceScan Scan { get; init; }
    public required string Date { get; init; }
    public required string CardName { get; init; }
    public required IReadOnlyList<string> DestRoots { get; init; }
    public IReadOnlyList<string> CardPaths => DestRoots.Select(r => Layout.CardPath(r, Date, CardName)).ToList();
    public PlanMode Mode { get; init; } = PlanMode.Full;
    /// The ASC MHL generation this copy writes: 1 for a full copy, N for an append (spec 2.2).
    public int Generation { get; init; } = 1;
    /// Append: the whole card as it is now (history); null for a full copy (then Scan is the whole card).
    public SourceScan? FullScan { get; init; }
    /// Append: files of the earlier backup that were never read back.
    public int UnverifiedEarlier { get; init; }
    /// What this copy hashes and verifies with (xxh64 always): OffloadOptions.Hashes for a full copy, the earlier
    /// backup's for an append (spec 3.5).
    public HashKind Hashes { get; init; } = HashKind.Xxh64;
    /// Spec 2.5: what each destination volume needs and has free (the CLI's --dry-run lists it).
    public IReadOnlyList<Space.SpaceLine> Space { get; init; } = [];
    /// "Na cíli … zbude po offloadu jen … — rezerva pod 5 %…" — never blocks (R11).
    public IReadOnlyList<string> SpaceWarnings { get; init; } = [];
}

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors);

public sealed class OffloadError : Exception
{
    public bool IsPreflight { get; }
    /// Which check failed (preflight errors only); tests and the GUI tell the cases apart by it, not by the text.
    public PreflightCode? Code { get; }

    OffloadError(string message, bool preflight, PreflightCode? code) : base(message)
    {
        IsPreflight = preflight;
        Code = code;
    }

    public static OffloadError Preflight(PreflightCode code, string message) => new(message, true, code);
    public static OffloadError Failed(string message) => new(message, false, null);
}
