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
    /// Tests only: called on destination `index`'s thread before every block it writes; an exception it throws is a write
    /// error there (a disk pulled out).
    internal Action<int>? BeforeDestWrite { get; set; }
    /// Tests only: creates the source hash of an algorithm ("xxh64", "md5") instead of HashAlgo.Create.
    internal Func<string, Verification.IFileHash>? HashFactory { get; set; }
    /// Tests only: the free bytes of the volume of a path, instead of NativeIO.AvailableBytes.
    internal Func<string, long?>? FreeSpace { get; set; }
    /// Tests only: H.264/HEVC proxies with this encoder (e.g. libx264) instead of the probed one — parallel
    /// NVENC sessions from concurrently running tests can collide.
    internal string? ProxyEncoder { get; set; }
    /// Tests only: the proxy stall watchdog's limit instead of ProxyRenderer.DefaultStallTimeout.
    internal TimeSpan? ProxyStallTimeout { get; set; }
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

public enum PlanMode { Full, Append, Repair }

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
    /// The project the job runs in (spec 4.1), null = "Bez projektu" — set by OffloadJob, written into the report.
    public string? Project { get; set; }
    /// Repair: per file, the destinations that get a new copy — the others hold a good copy, which is only read back.
    /// Null = every destination (a full copy, an append).
    public IReadOnlyList<bool[]>? Write { get; init; }
    /// Repair: per file the hashes the earlier backup recorded from the card (ruling C1: what the report records, what a
    /// kept copy is compared with, and what the card read now must give — a new copy is never put in place otherwise).
    public IReadOnlyList<FileHashes>? Expected { get; init; }
    /// Repair: per file and destination, where its copy that did not match goes before the new one takes its place
    /// (relative to the card folder), else null.
    public IReadOnlyList<string?[]>? SetAside { get; init; }
    /// Repair: per file and destination, a kept copy whose newest record says it was read back — still "verified" when
    /// the repair ends (cancelled) before reading it again (fix round 1, item 2).
    public IReadOnlyList<bool[]>? ReadBackBefore { get; init; }
    /// Repair: card files that no report of the backup records (fix round 1, item 1) — the card is not safe to eject.
    public int NotInBackup { get; init; }

    /// File `file` gets written to destination `dest`.
    public bool Writes(int file, int dest) => Write?[file][dest] ?? true;
}

public enum CopyPhase { Copy, Flush, Verify, Report, Done }

/// `Paused`: Pozastavit was pressed (PauseGate); `PauseHeld`: the copy or the read-back really waits now.
/// `PlannedBytes`: what each destination gets written (null = TotalBytes each).
public sealed record ProgressSnapshot(
    CopyPhase Phase, long TotalBytes, int TotalFiles, int FileIndex, string CurrentFile,
    long ReadBytes, long[] WrittenBytes, long[] VerifiedBytes, int Errors,
    bool Paused = false, bool PauseHeld = false, long[]? PlannedBytes = null, long[]? VerifyPlannedBytes = null)
{
    /// What destination `d` really reads back: the files written in this run (a repair trusts the others). Known once the
    /// read-back starts; before that, what the destination gets written.
    public long ToVerify(int d) => VerifyPlannedBytes is { } p && d < p.Length && p[d] >= 0 ? p[d] : Planned(d);

    /// The bytes all destinations together read back - the denominator of the read-back progress.
    public long ToVerifyTotal => Enumerable.Range(0, Math.Max(1, VerifiedBytes.Length)).Sum(ToVerify);

    /// What destination `d` gets written: TotalBytes, less for a destination a repair only reads back.
    public long Planned(int d) => PlannedBytes is { } p && d < p.Length ? p[d] : TotalBytes;
}

public sealed class OffloadError : Exception
{
    public bool IsPreflight { get; }
    /// Which check failed (preflight errors only); tests and the GUI tell the cases apart by it, not by the text.
    public PreflightCode? Code { get; }
    /// `RepairNothing` only: files on the card that no report of the backup records (shot after it) — nothing to
    /// repair, but the card must not be ejected (batch 10–12 review: counted before the refusal, never skipped).
    public int NotInBackup { get; private init; }

    OffloadError(string message, bool preflight, PreflightCode? code) : base(message)
    {
        IsPreflight = preflight;
        Code = code;
    }

    public static OffloadError Preflight(PreflightCode code, string message) => new(message, true, code);
    public static OffloadError Failed(string message) => new(message, false, null);
    public static OffloadError RepairNothing(int notInBackup) =>
        new(Msg.RepairNothing, true, PreflightCode.RepairNothing) { NotInBackup = notInBackup };
}
