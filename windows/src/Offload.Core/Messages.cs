namespace Offload.Core;

/// Which check before the copy stopped a job. Tests and the GUI tell the cases apart by the code, never by the text.
public enum PreflightCode
{
    Other,
    ChunkSize,
    BufferCount,
    Date,
    SourceNotFound,
    NoDestination,
    DuplicateDestination,
    SourceIsOffload,
    SourceUnreadable,
    DestinationNotFound,
    DestinationNotWritable,
    DestinationUnreadable,
    SameVolumeAsSource,
    SharedVolume,
    EmptySource,
    BadNames,
    Fat32,
    NoSpace,
    NoHashRecord,
    NoFilesToRead,
    AppendUnavailable,
    AppendNoReport,
    AppendNoMhl,
    AppendDiverged,
    AppendConflict,
    AppendMissingFile,
    AppendFailedFile,
    AppendOtherCard,
    AppendMhlDamaged,
    AppendBusy,
    /// Not an error: the backup already has everything (the job ends "Done").
    NothingNew,
    /// Not an error: a repair found every file good on every destination (the job ends "Done").
    RepairNothing,
    /// A repair would not copy what the backup recorded: the card or the card folders changed since — or it may not run
    /// as asked (without verification, another card in the drive).
    RepairConflict,
}

/// Every message a user sees from checks and validation (GUI job card, CLI) — English, one method per message, no resx.
/// Numbers are formatted invariantly (a decimal point), whatever the PC's culture.
public static partial class Msg
{
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    /// "1 file", "3 files".
    static string N(long n, string one, string? other = null) => Desktop.Texts.N(n, one, other);

    public static string ChunkSize(int n) => $"The block size must be a multiple of 4096 B and at most 1 GiB (given {n.ToString(Inv)}).";
    public static string BufferCount(int n) => $"The buffer count must be at least 2 (given {n.ToString(Inv)}).";
    public static string Date(string d) => $"The date must be YYYY-MM-DD (given \"{d}\").";
    public static string SourceNotFound(string path) => $"Source not found: {path}";
    public const string NoDestination = "No destination given.";
    public static string DuplicateDestination(string root) => $"Destination given twice: {root}";
    public const string SourceIsOffload =
        "The source looks like an earlier offload (it has _REPORT, _PROXY or ascmhl) — copy the original card, or remove these folders.";
    public static string SourceUnreadable(string path) => $"Cannot read the source: {path}";
    public static string DestinationNotFound(string root) => $"Destination not found: {root} — is the disk connected?";
    public static string DestinationNotWritable(string root) => $"Cannot write to the destination: {root}";
    public static string DestinationUnreadable(string root) => $"Cannot read the destination: {root}";
    public static string SameVolumeAsSource(string root) => $"Destination {root} is on the same disk as the source.";
    public static string SharedVolume(string root) =>
        $"Two destinations are on the same disk ({root}) — that is not a second copy. Allow it with --allow-same-device.";
    public const string EmptySource = "There are no files on the source.";

    /// The first 20 problems, one per line, then how many more.
    public static string BadNames(IReadOnlyList<string> problems)
    {
        var more = problems.Count > 20 ? $"\n  … and {(problems.Count - 20).ToString(Inv)} more" : "";
        return "Windows cannot store some names:\n  " + string.Join("\n  ", problems.Take(20)) + more;
    }

    public static string SameNameCase(string path, string other) => $"{path}: same name as {other}, differs only in letter case";
    public const string InvalidChar = "contains a character Windows does not allow";
    public const string EndsWithDotOrSpace = "ends with a dot or a space";
    public const string ReservedName = "reserved device name in Windows";
    public static string Fat32(string filesystem, string path, long size) =>
        $"{filesystem} cannot store files of 4 GB or more ({path}, {Format.BytesShort(size)})";
    /// "Not enough space on destination Backup (D:): needs 412 GB (cards 380 GB, proxies ~30 GB, report 2 GB), free 398 GB."
    public static string NoSpace(string where, Space.SpaceNeed need, long free) =>
        $"Not enough space on destination {where}: needs {Format.BytesShort(need.Total)} ({Parts(need, "cards")}), free {Format.BytesShort(free)}.";
    /// CLI --dry-run: "Space: D:\Backup needs 412 GB (card 380 GB, proxies ~30 GB, report 2 GB), free 398 GB"
    public static string SpaceLine(string where, Space.SpaceNeed need, long free) =>
        $"Space: {where} needs {Format.BytesShort(need.Total)} ({Parts(need, "card")}), free {Format.BytesShort(free)}";

    static string Parts(Space.SpaceNeed need, string cards)
    {
        var parts = new List<string>();
        if (need.Card > 0) parts.Add($"{cards} {Format.BytesShort(need.Card)}");
        if (need.Proxy > 0) parts.Add($"proxies ~{Format.BytesShort(need.Proxy)}");
        if (need.Report > 0) parts.Add($"report {Format.BytesShort(need.Report)}");
        return string.Join(", ", parts);
    }
    public static string LowSpace(string where, long left) =>
        $"Only {Format.BytesShort(left)} will be left on destination {where} after the offload — less than the 5 % reserve, and the proxy estimate is approximate.";
    public static string ProxiesSkippedForSpace(string where, long need, long free) =>
        $"Proxies were not rendered: not enough space on {where} (needs ~{Format.BytesShort(need)}, free {Format.BytesShort(free)}).";
    public static string ReportNotWritten(string details) => $"Could not write the report: {details}";
    public static string NoHashRecord(string dir) => $"Folder {dir} has no ASC MHL and no checksum list — nothing to compare with.";
    public static string NoFilesToRead(string path) => $"No files to read in {path}.";
    public const string CardVolumeUnreadable = "Cannot read the card.";
    public const string CardInspectFailed = "Could not check the card.";
    public const string CardSwapped = "The card was swapped while it was being read.";
    public const string CardNameAutoNone = "No name suggestion found; the volume label is used.";
    public static string CardNameAuto(string name) => $"Card name: {name} (from the clips)";
    public static string CliProject(string name) => $"Project: {name} (defaults from the app settings; command-line options take precedence)";
    public static string CliAppendNotify(string name) => $"Notifications from the project of the earlier backup: {name}";
    public static string CliAppendProject(string name) => $"Settings from project: {name} (the backup's project stays as in the earlier report)";
    public static string CardUnreadable(string detail) => $"Cannot read the card: {detail}";

    /// Where a backup is checked: the top bar of the window (since the new look, UI návrh) or the CLI.
    public const string VerifyHint = "Verify backup (top bar of the window; on the command line offload verify)";

    /// The end of every refused append; a refused repair says CannotRepair instead (ForRepair, minor M5).
    const string CannotAppend = "cannot append";
    const string CannotRepair = "cannot repair";
    const string OnlyAppend = "you can only append to";
    const string OnlyRepair = "you can only repair";

    // Append (spec 2.2)
    public static string AppendUnavailable(string cardPath) =>
        $"The earlier backup is not available: {cardPath} — connect the disk, or make a full copy.";
    public static string AppendNoReport(string cardPath) =>
        $"The report of the earlier backup is missing in {cardPath} (_REPORT\\offload_report.json) — {CannotAppend}.";
    public static string AppendBadReport(string cardPath, string detail) =>
        $"Cannot read the report of the earlier backup in {cardPath} ({detail}) — {CannotAppend}.";
    public static string AppendNoMhl(string cardPath) =>
        $"ASC MHL is missing in {cardPath} (ascmhl\\ascmhl_chain.xml) — {CannotAppend}.";
    public static string AppendDiverged(string details) =>
        $"The backups on the destinations differ ({details}) — check them with {VerifyHint}; {CannotAppend}.";
    /// Ruling I1: the numbered reports and the ASC MHL chain of one card folder disagree (an append stopped half way).
    public static string AppendReportsMismatch(string cardPath, int report, int mhl) =>
        $"The reports in {cardPath} do not match the ASC MHL (the last report is generation {report.ToString(Inv)}, the last MHL generation {mhl.ToString(Inv)}) — an earlier append did not finish. "
        + $"Check the backup with {VerifyHint} and fix the folder by hand; {CannotAppend}.";
    public static string AppendNotACardFolder(string path) =>
        $"{path} is not a card folder of a backup (<destination>\\<date>\\<card>) — {CannotAppend}.";
    public static string AppendDifferentFolders(IEnumerable<string> cardPaths) =>
        $"The folders of the earlier backup have different names ({string.Join(", ", cardPaths)}) — {OnlyAppend} folders with the same date and card name.";
    public static string AppendConflict(IReadOnlyList<string> lines, string fullCopyFolder) =>
        $"Append stopped — nothing was copied. {N(lines.Count, "file does", "files do")} not match the backup:\n  "
        + string.Join("\n  ", lines.Take(10)) + (lines.Count > 10 ? "\n  …" : "")
        + $"\nMake a full copy (it creates {fullCopyFolder}).";
    public static string AppendMissingFile(string disk, string path) =>
        $"A file of the earlier backup is missing on {disk}: {path} — check the backup with {VerifyHint}";
    public static string AppendFailedFile(string path, string disk) =>
        $"{path} was not copied correctly to {disk} in the earlier backup — {CannotAppend}; make a full copy.";
    public const string AppendOtherCard = "No file of the earlier backup is on the card — is it a different card? Make a full copy.";
    public static string AppendMhlDamaged(string cardPath) =>
        $"The ASC MHL history in {cardPath} is damaged — check it with {VerifyHint}; {CannotAppend}.";
    /// One line of the AppendConflict list: a file on a destination that no report records (a crashed append leaves one).
    public static string AppendUnknownFile(string disk, string path) =>
        $"{disk} has a file {path} that no report knows (an unfinished append?)";
    /// One line of the AppendConflict list: the temporary copy name of a new file is taken.
    public static string AppendPartNameTaken(string path, string part) =>
        $"{path} (the temporary copy name {part} is another file of the backup or the card)";
    public static string HashListNotHealed(string cardPath, string error) =>
        $"Cannot add the missing lines to the checksum list (.xxh64) in {cardPath}: {error}";
    /// Ruling I4 (plan 10): also while a job still verifies this backup with its proxies (it holds the folders until the
    /// verification results are written).
    public static string AppendBusy(string cardPath) =>
        $"Another job is working with this backup right now — an append, a repair or a verification during proxies ({cardPath}) — wait until it finishes.";
    public const string NothingNew = "Nothing new — the backup is complete.";

    // Repair — "Copy again" (UI návrh, nová věc 4)
    public const string RepairNothing = "Nothing to repair — all files are fine on all destinations.";
    public static string RepairConflict(IReadOnlyList<string> lines) =>
        $"Repair stopped — nothing was copied. {N(lines.Count, "file")} cannot be repaired:\n  "
        + string.Join("\n  ", lines.Take(10)) + (lines.Count > 10 ? "\n  …" : "")
        + "\nThe card changed since the backup or a file is missing in the backup folder — check it with Verify backup, or make a full copy.";
    /// One line of RepairConflict: the file is no longer on the card.
    public static string RepairNotOnCard(string path) => $"{path}: no longer on the card";
    /// One line of RepairConflict (ruling C1): same size and time, but the card gives another hash than the backup
    /// recorded — the new hash is only ever here, never in a record.
    public static string RepairCardDiffers(string path, string algorithm, string now, string recorded) =>
        $"{path}: the card has different content than at the backup ({algorithm} {now}, in the backup {recorded})";
    /// One line of RepairConflict: the card file could not be read to compare it.
    public static string RepairCardUnreadable(string path, string detail) => $"{path}: cannot read it on the card ({detail})";
    /// Ruling M16: a repair always reads back — a kept copy is recorded "verified" only after it was.
    public const string RepairNeedsVerify = "A repair is always verified by reading back — it cannot run without verification.";
    /// The comment of a repair's ASC MHL generation: the files copied again, and those only read back (fix round 2, B).
    public static string MhlRepairComment(int copied, int readBack, string source) =>
        $"Offload repair: {N(copied, "file")} copied again from {source}"
        + (readBack > 0 ? $"; {N(readBack, "file")} only read back" : "");
    /// Ruling I1: the card in the drive is not the one the repaired backup was copied from.
    public const string RepairOtherCard =
        "The card the backup was made from is not in the reader (another card, or none) — nothing was copied. Insert the original card and start Copy again.";
    /// Fix round 1 (plan 9, tasks 4–5), item 1: card files that no report of the backup records (shot after it) — a
    /// repair does not copy them, so the card must not be ejected as backed up.
    public static string CardFilesNotInBackup(int n) =>
        $"The card has {N(n, "file")} that the backup does not have — do not eject the card; append to the backup.";
    /// Batch 10–12 review: "Nothing to repair", but the backup's reports (read again after the planner) record failed
    /// files — another job wrote them meanwhile.
    public static string BackupStillFailed(int n) =>
        $"{N(n, "file")} in the backup still {(n == 1 ? "does" : "do")} not match — do not eject the card; start Copy again.";
    /// Batch 10–12 review: the backup's reports could not be read after "Nothing to repair" — nothing was checked.
    public static string BackupStateUnreadable(string detail) =>
        $"Cannot read the state of the backup ({detail}) — nothing was checked; do not eject the card yet.";
    /// Minor M5: the checks of the earlier backup are an append's; refusing a repair they say "cannot repair".
    public static string ForRepair(string appendRefusal) =>
        appendRefusal.Replace(CannotAppend, CannotRepair).Replace(OnlyAppend, OnlyRepair);
    /// Ruling I1 (plan 9): the card in the drive after Resume is not the one being copied.
    public const string CardChangedWhilePaused =
        "The card in the reader was swapped or removed while paused — the remaining files were not copied. Insert the original card and append to the backup.";
    public static string UnverifiedEarlier(int n) =>
        $"{N(n, "file")} of the earlier backup {(n == 1 ? "was" : "were")} not verified — Verify backup is recommended.";
    public const string CardNotInHistory = "The card is not in the history — give the backup folders with --into.";

    // CLI
    public const string CliError = "Error: ";
    public static string UnknownProxyCodec(string? x) =>
        $"Unknown proxy codec \"{x}\" — use h264, hevc, proresProxy, proresLT, prores422 or prores422HQ.";
    public static string LutNotFound(string path) => $"LUT not found: {path}";
    public static string RuleLutNotFound(int rule, string name) => $"LUT of rule {rule.ToString(Inv)} not found: {name}";
    public const string LutRuleNeedsAuto = "--lut-rule works only with --lut auto";
    public static string BadNtfyUrl(string url) =>
        $"Cannot use the ntfy address \"{url}\" — give a server and a topic, e.g. https://ntfy.sh/my-secret-channel (topic: only letters without accents, digits, - and _).";
    public static string BadLutRule(string spec) =>
        $"Cannot read the LUT rule \"{spec}\" — use <match>=<value>:<lut>, e.g. camera=FX6:sony-slog3-sgamut3cine (match: camera, serial, reel, folder; lut: a built-in one, none or a path to a .cube).";
    /// A proxy rendered without its custom LUT (the file was gone when stage 2 began).
    public static string ProxyWithoutLut(string file) => $"LUT not found: {file} — proxy without a LUT";
    public static string UnknownHash(string? x) => $"Unknown checksum \"{x}\" — use xxh64 or both (xxh64 + MD5).";

    // Day report (spec 3.2)
    public static string DayNotFound(string path) => $"Day folder not found: {path}";
    public static string NotADayFolder(string path) => $"Folder {path} has no card with a report — select a day folder on a destination (<destination>\\<date>).";
    public static string DayReportFolderIsCard(string path) =>
        $"Day folder {path} has a card named \"{Layout.ReportDirName}\" — the day report would be written into it, so it is not created.";
    public static string DayIsVolumeRoot(string path) =>
        $"{path} is the root of a disk, not a day folder — select a day folder on a destination (<destination>\\<date>) or a card in it.";
    public static string DayReportBusy(string path) => $"Another job is creating the day report in {path} right now — try again in a moment.";
    public static string DayReportFailed(string detail) => $"Could not create the day report: {detail}";
    public static string DayReportWritten(string path) => $"Day report: {path}";
    public static string ProxyFolderNotFound(string path) => $"Proxy folder not found: {path}";
    public const string ProxyFolderNoEffect = "Note: --proxy-folder does nothing without --proxy.";
    public const string Cancelling = "Cancelling — finishing the current block; partly copied files will be deleted…";
    public static string NotPositiveNumber(string? s) => $"\"{s}\" is not a positive number.";
    public static string UnknownFallbackLut(string x, string ids) => $"Unknown fallback LUT \"{x}\". Built-in: {ids}";
    public static string UnknownLut(string x, string ids) => $"Unknown LUT \"{x}\". Use auto, none, a path to a .cube or one of: {ids}";
    public const string NoBuiltinLuts = "Built-in LUTs not found (the luts folder is missing).";

    // Clip notes (report, job card)
    public const string NoVerifiedCopy = "no verified copy";
    public static string ProxyRenamed(string name) => $"Proxy renamed to {name} (name clash) — relink it by hand.";

    /// LighterSource (0.5.1): the note of a clip without a proxy because its original is lighter — neutral, not a failure.
    public const string ProxyLighterPrefix = "no proxy — the original is lighter";

    /// The same note as Offload wrote it before 0.9.0 (in Czech): old reports still say it and must still count.
    public const string ProxyLighterPrefixOld = "bez proxy — origin\u00e1l je leh\u010d\u00ed";

    public static string ProxyLighter(double sourceMbps, double targetMbps) =>
        $"{ProxyLighterPrefix} ({Mbps(sourceMbps)} Mb/s ≤ {Mbps(targetMbps)} Mb/s)";

    static string Mbps(double v) => v.ToString("0.#", Inv);
    public const string FfmpegNotFound = "ffmpeg not found";
    public const string FfprobeNotFound = "ffprobe not found";
    public const string UnknownFormat = "unknown format";
    public const string NoDecoderShort = "no decoder";
    public static string NoDecoder(string format) => $"{format}: no decoder";

    // Disks loaded automatically (2026-10-09)
    public static string AutoDiskFull(string name) => $"Disk {name} is full — not added as a destination.";
    public static string AutoDiskReadOnly(string name) => $"Disk {name} is read-only — not added as a destination.";

    // A copy of the report on this computer too (2026-10-09)
    public static string LocalReportFailed(string detail) => $"Could not copy the report to this computer: {detail} — the report is only on the destination disks.";
    public const string LocalReportOnCard = "the folder for the report copy is on the card — Offload never writes to a card";
    public static string LocalReportWritten(string path) => $"Report copy: {path}";

    // Summary report of selected backups (2026-10-09)
    public static string SummaryNotACard(string path) =>
        $"{path} is not a card folder with a backup ({Layout.ReportDirName}\\{CardRecord.MainReport} is missing) nor a day folder with cards.";
    public static string SummaryUnknown(string arg) => $"\"{arg}\" is neither a card folder nor a backup id in the history.";
    public const string SummaryNothing = "Select at least one backup.";
    public static string SummaryTargetMissing(string path) => $"Folder for the summary report not found: {path}";
    public static string SummaryTargetIsCard(string path) =>
        $"{path} is inside the backup folder of a card (even an unfinished or interrupted one) — the summary report is never written into a backup; select another folder.";
    public static string SummaryTargetCameraCard(string path) => $"{path} looks like a camera card — Offload never writes to a card.";
    public static string SummaryTargetRemovable(string path) =>
        $"{path} is on removable media (a card in a reader, a flash drive) — the summary report is not saved there; select a disk.";
    public static string SummaryTargetReadOnly(string path) => $"{path} is on a read-only disk.";
    public static string SummaryTargetIsSource(string path) => $"{path} is on the card — Offload never writes to a card.";
    public static string SummaryWritten(string path) => $"Summary report: {path}";
    public static string SummaryExtraWritten(string path) => $"Summary report also on the selected disk: {path}";
    public static string SummaryExtraFailed(string detail) => $"The summary report was not written to the selected disk: {detail} — it is only on this computer.";
    public static string SummaryFailed(string detail) => $"Could not create the summary report: {detail}";
}
