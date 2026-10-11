namespace Offload.Core;

/// Plan 10 (verify-in-proxy): the texts of the report, the job card and the log.
public static partial class Msg
{
    // Why a destination is verified the classic way (report, per destination).
    public const string ClassicBufferedWrites = "the volume did not accept writes past the cache — verified by reading back";
    public const string ClassicNetwork = "network destination — verified by reading back";
    public const string ClassicCacheCheck = "the check of reads past the cache did not pass — verified by reading back";
    public static string ClassicCacheCheckFailed(string detail) => $"cannot check reads past the cache ({detail}) — verified by reading back";
    public const string ClassicCompressedOrEncrypted = "the destination folder is compressed or encrypted (NTFS) — verified by reading back";
    public const string ClassicSetting = "verification during proxies is off — verified by reading back";
    public const string ClassicSecondary = "another destination — verified by reading in the background";

    /// A destination's disk stopped answering while it was verified (the files are failed, the job finishes).
    public const string VerifyDiskHung = "the disk stopped responding during verification";

    // Spec 10 notes (report "verifyNotes", the job's warning).
    public static string UnstableRead(string path, string disk, IReadOnlyList<int> blocks) =>
        $"Unstable read: {path} on {disk} (block {string.Join(", ", blocks.Select(b => b.ToString(Inv)))}) gave different data in one read — the next read matched the card.";
    public static string SameBlockWrong(string path, int block, string disks, string card) =>
        $"{path}: block {block.ToString(Inv)} mismatches the same way on {disks} — {card}";
    public static string SameBlockWrongMore(string path, int blocks) =>
        $"{path}: {N(blocks, "more block mismatches", "more blocks mismatch")} the same way on several destinations (the card was not read again for {(blocks == 1 ? "it" : "them")}).";
    public const string CardNotReadAgain = "could not read the card again (no longer in the reader?) — insert it and start Copy again.";
    public const string CardGivesRecorded = "the card now gives the recorded data: the defect arose while writing to the disks.";
    public const string CardGivesWhatDisksHave = "the card now gives the same data as the disks: the card was probably misread during the copy.";
    public const string CardGivesOther = "the card now gives data different from the copy and from the disks: the card or the reader is probably faulty.";

    /// The job's warning when the verified generation could not be written into a card folder (it stays uncommitted:
    /// "Finish interrupted backup", an append, finishes it — R6, ruling I5; a repair refuses an unfinished backup).
    public static string VerifyNotCommitted(string detail) =>
        $"Could not write the verification result ({detail}) — do not format the card yet; \"Finish interrupted backup\" (Append, mmmover append) finishes the backup.";

    // Fix round 1, item 2b: a record of the job that the history lock kept out.
    public const string HistoryDelayed = "The card history is busy right now — the record of this backup will be written as soon as possible (do not close the app yet).";
    public const string HistoryNotWritten = "Could not write the record of this backup to the card history (the history was busy for too long) — the backup on the disks is fine, but the card will not be recognised the next time it is inserted.";

    /// CLI --primary 0 or less.
    public static string PrimaryNotPositive(int n) => $"--primary {n.ToString(Inv)}: destinations are numbered from 1 (the order of --dest).";

    /// CLI --primary above the number of destinations.
    public static string PrimaryTooHigh(int n, int count) =>
        $"--primary {n.ToString(Inv)}: there {(count == 1 ? "is" : "are")} only {N(count, "destination")} (numbered from 1 in the order of --dest).";

    // R6 (resume).
    /// A resume keeps the proxy the interrupted run finished (carried from tasks 12–14).
    public const string ProxyKeptOnResume = "proxy from the interrupted run — not rendered again (the decoder did not check the clip's content this time)";
    public static string ResumeDiverged(string cardPath, int generation) =>
        $"{cardPath}: the unfinished backup (generation {generation.ToString(Inv)}) does not match the other destinations — check it by hand.";
    /// Ruling I5: the repair points to the tick the card gets on insert ("Finish interrupted backup", even with no new
    /// file) and to its CLI form.
    public const string RepairUnfinished = "The backup was not finished (the app ended during the copy or the verification) — finish it first with \"Finish interrupted backup\" on the card (mmmover append).";

    /// A card folder gone before the verification results were written into it (an unplugged disk) — never made anew.
    public const string CardFolderGone = "the card folder on the destination disappeared (disk disconnected?)";

    /// Spec 7: "2 clips have a content defect — check them before formatting (the copy matches the card)."
    public static string ContentWarnings(int n) =>
        $"{N(n, "clip has", "clips have")} a content defect — check {(n == 1 ? "it" : "them")} before formatting (the copy matches the card).";
    /// Ruling I3: a destination where reads past the file cache are impossible was read back through it (as 0.5.x did).
    public static string VerifiedThroughCache(string disk, int files) =>
        $"Verified by reading through the Windows cache: {disk}, {N(files, "file")} — the volume or the file does not allow reads past the cache, so the data may have come from memory instead of the disk.";
}
