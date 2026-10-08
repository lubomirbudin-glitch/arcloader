using System.Globalization;
using Offload.Core.History;
using Offload.Core.Verification;

namespace Offload.Core.Append;

/// What an append copies (spec 2.2): only the card's new files, into the same card folders, as ASC MHL generation `Generation`.
public sealed class AppendPlan
{
    public required string Source { get; init; }
    /// `<root>\<date>\<card>` on every destination of the earlier backup, in its order.
    public required IReadOnlyList<string> CardPaths { get; init; }
    public required IReadOnlyList<string> DestRoots { get; init; }
    public required string Date { get; init; }
    public required string CardName { get; init; }
    /// The new ASC MHL generation: the generations already there + 1 (the same on every destination, R7; ruling I1).
    public required int Generation { get; init; }
    /// The whole card now (history fingerprint and file list).
    public required SourceScan FullScan { get; init; }
    /// The files the backup does not have yet, and the folders they are in.
    public required IReadOnlyList<SourceFile> NewFiles { get; init; }
    public required IReadOnlyList<string> NewDirs { get; init; }
    /// Files of the earlier backup that were written but never read back (counted once, whatever the destinations).
    public int UnverifiedEarlier { get; init; }
    /// The hash types of the earlier backup — an append uses them, not the current setting (spec 3.5).
    public IReadOnlyList<string> HashAlgorithms { get; init; } = ["xxh64"];
    /// R6 (plan 10): this append completes generation `Generation`, which an earlier run never committed — NewFiles are
    /// then that generation's files with what ResumePlanner.Decide found for each (aligned with NewFiles).
    public bool Resuming { get; init; }
    public IReadOnlyList<bool[]>? Write { get; init; }
    public IReadOnlyList<VerifyInProxy.ManifestRecord?>? Known { get; init; }
    public IReadOnlyList<bool>? HashOnly { get; init; }
    public IReadOnlyList<VerifyInProxy.PartCheckpoint?>? Resume { get; init; }
    /// A resume of a generation already in an ASC MHL chain: card files it cannot take (shot since) — the next append
    /// copies them; until then the card is not safe (OffloadPlan.NotInBackup).
    public int Later { get; init; }
}

/// Pure decisions over the card and the content of the card folders (R4): the truth is what is on the destinations —
/// every report generation and the files themselves — not history. Reads only (never the card, never a destination is
/// written); throws OffloadError.Preflight before anything is written when an append is not safe.
public static class AppendPlanner
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// "D:" for "D:\Zaloha\2026-10-06\A001", the share for a UNC path.
    public static string Disk(string cardPath) => (Path.GetPathRoot(cardPath) ?? cardPath).TrimEnd('\\');

    static string When(DateTime utc) => utc.ToLocalTime().ToString("d. M. H:mm", Cs);

    public static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default) =>
        Plan(source, cardPaths, ct, sourceFilesystem: null);

    /// The earlier backup as steps 0 and 1 found it (also the start of a repair, RepairPlanner): the card folders in
    /// their order, their roots, date and name, every folder's reports, and the generation the next copy writes.
    internal sealed record Backup(List<string> Paths, List<string> Roots, string Date, string CardName, List<CardRecord> Records, int NextGeneration);

    /// Steps 0 and 1: one backup `<root>\<date>\<card>` with the same date and name everywhere, every report readable,
    /// every ASC MHL chain intact and of the same length, and the numbered reports in agreement with it. Reads only.
    /// `unfinished` (R6): the generation a resume completes — only the generations before it are checked (generation 1:
    /// nothing is committed yet, the folders need no report).
    internal static Backup CheckBackup(IReadOnlyList<string> cardPaths, Unfinished? unfinished = null)
    {
        if (cardPaths.Count == 0) throw OffloadError.Preflight(PreflightCode.NoDestination, Msg.NoDestination);
        var paths = cardPaths.Select(p => Path.GetFullPath(p).TrimEnd('\\')).ToList();

        // 0. One backup: `<root>\<date>\<card>` with the same date and name everywhere — the copy goes exactly there.
        var (roots, date, cardName) = Split(paths);
        if (unfinished is { Generation: 1 })
        {
            foreach (var p in paths)
                if (!Directory.Exists(p)) throw OffloadError.Preflight(PreflightCode.AppendUnavailable, Msg.AppendUnavailable(p));
            return new Backup(paths, roots, date, cardName, paths.Select(p => new CardRecord { CardPath = p }).ToList(), 1);
        }

        // 1. Every earlier backup is there, with its report and its ASC MHL chain (R7).
        var records = new List<CardRecord>();
        var generations = new List<int>();
        foreach (var cardPath in paths)
        {
            if (!Directory.Exists(cardPath)) throw OffloadError.Preflight(PreflightCode.AppendUnavailable, Msg.AppendUnavailable(cardPath));
            var record = CardRecord.Load(cardPath, unfinished?.Generation - 1);
            if (record.ReportMissing) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendNoReport(cardPath));
            if (record.Error != null) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendBadReport(cardPath, record.Error));
            // An append report nobody can read: the files it recorded would look like someone else's.
            if (record.Problems.Count > 0) throw OffloadError.Preflight(PreflightCode.AppendNoReport, Msg.AppendBadReport(cardPath, record.Problems[0]));
            if (MhlReader.Read(cardPath) is not { Chain.Count: > 0 } mhl)
                throw OffloadError.Preflight(PreflightCode.AppendNoMhl, Msg.AppendNoMhl(cardPath));
            // A changed or missing generation, or one outside the chain (fix round 1, item 5): the history the next
            // generation would extend is not what was written — have it checked first.
            if (mhl.ChainProblems.Count > 0) throw OffloadError.Preflight(PreflightCode.AppendMhlDamaged, Msg.AppendMhlDamaged(cardPath));
            records.Add(record);
            // R6 + ruling I1: the generation a resume completes may be committed in some folders already — the backup
            // before it is what is checked here.
            generations.Add(Math.Min(mhl.Chain.Max(c => c.Seq), (unfinished?.Generation ?? int.MaxValue) - 1));
        }
        if (generations.Distinct().Count() > 1)
        {
            var details = string.Join(", ", paths.Select((p, i) =>
                i == 0 ? $"{Disk(p)} má {generations[i]} {Generace(generations[i])} MHL" : $"{Disk(p)} {generations[i]}"));
            throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.AppendDiverged(details));
        }
        // Ruling I1: the numbered reports and the chain must agree, or the next number could hit an earlier append's
        // reports (an append whose MHL never landed) — such a folder needs a look by hand first.
        for (int d = 0; d < paths.Count; d++)
            if (records[d].HighestReportNumber != generations[d])
                throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.AppendReportsMismatch(paths[d], records[d].HighestReportNumber, generations[d]));
        // Ruling I1: max of both (equal here, the check above refused otherwise).
        return new Backup(paths, roots, date, cardName, records, Math.Max(generations[0], records[0].HighestReportNumber) + 1);
    }

    /// `sourceFilesystem`: tests pretend a FAT32 card; null = the card's own. `destFilesystem`: the same for a card
    /// folder on a destination (ruling I5); null = NativeIO.FilesystemType.
    internal static AppendPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem,
        Func<string, string>? destFilesystem = null)
    {
        // R6 (plan 10): a generation an earlier run never committed is completed by this append, not numbered after it.
        var unfinished = ResumePlanner.Find(cardPaths);
        var (paths, roots, date, cardName, records, generation) = CheckBackup(cardPaths, unfinished);

        // 2. The card, compared with every backup: a known file must be the same on the card, in the report and on disk.
        var scan = Scanner.Scan(source, ct);
        bool cardLocalTime = FileTimes.StoresLocalTime(sourceFilesystem ?? NativeIO.FilesystemType(source));
        // Ruling I5: a FAT destination shifts its own file times after a change of time, whatever the card is.
        var diskLocalTime = paths.Select(p => cardLocalTime || FileTimes.StoresLocalTime(destFilesystem?.Invoke(p) ?? NativeIO.FilesystemType(p))).ToList();
        var newFiles = new List<SourceFile>();
        var presence = new List<bool[]>();
        var conflicts = new List<string>();
        int unverified = 0, known = 0, later = 0;
        foreach (var f in scan.Files)
        {
            ct.ThrowIfCancellationRequested();
            int present = 0, goodInRecord = 0;
            bool unverifiedFile = false;
            var here = new bool[paths.Count];
            for (int d = 0; d < paths.Count; d++)
            {
                var onDisk = new FileInfo(Layout.Join(paths[d], f.RelPath));
                bool exists = onDisk.Exists;
                if (exists) present++;
                here[d] = exists;
                if (!records[d].Files.TryGetValue(f.RelPath, out var rec))
                {
                    // Not in any report but on the disk: someone else's file — never overwritten, never trusted. Unless the
                    // unfinished run wrote it (R6): its manifest names the card's file there.
                    if (exists && unfinished?.Owns(d, f, onDisk.Length, diskLocalTime[d]) != true)
                        conflicts.Add(Msg.AppendUnknownFile(Disk(paths[d]), f.RelPath));
                    continue;
                }
                int column = CardRecord.DestinationIndex(rec.Report, paths[d], d);
                var state = column < rec.File.Destinations.Count ? rec.File.Destinations[column] : "pending";
                bool good = state is "verified" or "written";
                if (!good)
                {
                    // A failed file that is not on the disk (a cancelled append) is simply copied now; one that is
                    // there may be anything — never trusted, never overwritten.
                    if (exists) throw OffloadError.Preflight(PreflightCode.AppendFailedFile, Msg.AppendFailedFile(f.RelPath, Disk(paths[d])));
                    continue;
                }
                goodInRecord++;
                if (!CardRecord.ReadBack(rec, paths[d], records[d].VolumeSerial)) unverifiedFile = true;   // ruling R2: the one definition (R12: by disk)
                if (rec.File.Size != f.Size || !FileTimes.Close(rec.File.Modified, f.ModifiedUtc, cardLocalTime))
                    conflicts.Add(Conflict(f, Disk(paths[d]), rec.File.Size, rec.File.Modified));
                else if (!exists)
                    throw OffloadError.Preflight(PreflightCode.AppendMissingFile, Msg.AppendMissingFile(Disk(paths[d]), f.RelPath));
                else if (onDisk.Length != f.Size || !FileTimes.Close(onDisk.LastWriteTimeUtc, f.ModifiedUtc, diskLocalTime[d]))
                    conflicts.Add(Conflict(f, Disk(paths[d]), onDisk.Length, onDisk.LastWriteTimeUtc));
            }
            if (unverifiedFile) unverified++;
            if (goodInRecord > 0 || unfinished?.Mentions(f) == true) known++;
            // A generation already in an ASC MHL chain (ruling I1) takes no file it does not list: one shot since waits for
            // the next append — the card is not safe meanwhile (NotInBackup).
            if (goodInRecord == 0 && present == 0 && unfinished?.Fixed is { } only && !only.Contains(f.RelPath))
            {
                later++;
                continue;
            }
            // R6: a file of the unfinished generation on some disks (its copies there are the run's own — else a conflict
            // above) is part of this generation too.
            if (goodInRecord == 0 && (present == 0 || unfinished != null))
            {
                newFiles.Add(f);
                presence.Add(here);
            }
            else if (goodInRecord > 0 && goodInRecord < paths.Count && present < paths.Count)
                throw OffloadError.Preflight(PreflightCode.AppendDiverged,
                    Msg.AppendDiverged($"{f.RelPath} je jen na {string.Join(", ", paths.Where(p => File.Exists(Layout.Join(p, f.RelPath))).Select(Disk))}"));
        }
        // Ruling I3 (fix round 1, item 2): not one file of the card is in the backup — another card, never mixed into it.
        if (scan.Files.Count > 0 && known == 0) throw OffloadError.Preflight(PreflightCode.AppendOtherCard, Msg.AppendOtherCard);
        // Fix round 1, item 4: a new file is copied via ".<name>.offload-part" next to it, created over whatever has
        // that name — never over a file the backup recorded, nor over another file of the card.
        var taken = scan.Files.Select(f => f.RelPath).Concat(records.SelectMany(r => r.Files.Keys)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in newFiles)
            if (DestWriter.PartName(f.RelPath) is var part && taken.Contains(part))
                conflicts.Add(Msg.AppendPartNameTaken(f.RelPath, part));
        if (conflicts.Count > 0)
            throw OffloadError.Preflight(PreflightCode.AppendConflict, Msg.AppendConflict(conflicts.Distinct().ToList(), cardName + "_2"));

        var newDirs = newFiles
            .SelectMany(f => Ancestors(f.RelPath))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var first = records[0].Generations.FirstOrDefault()?.Report;
        var resumed = unfinished == null ? default
            : ResumePlanner.Decide(unfinished, paths, newFiles, presence, diskLocalTime);
        // Generation 1 resumed: no report yet — the hashes the run used, as its manifest says (MD5 when it has it).
        var algorithms = first?.Algorithms ?? (unfinished!.Manifests.Any(m => m.Any(r => r.Md5 != null)) ? ["xxh64", "md5"] : ["xxh64"]);
        return new AppendPlan
        {
            Source = Path.GetFullPath(source),
            CardPaths = paths,
            DestRoots = roots,
            Date = date,
            CardName = cardName,
            Generation = generation,
            FullScan = scan,
            NewFiles = newFiles,
            NewDirs = newDirs,
            UnverifiedEarlier = unverified,
            HashAlgorithms = algorithms,
            Resuming = unfinished != null,
            Later = later,
            Write = unfinished == null ? null : resumed.Write,
            Known = unfinished == null ? null : resumed.Known,
            HashOnly = unfinished == null ? null : resumed.HashOnly,
            Resume = unfinished == null ? null : resumed.Resume,
        };
    }

    /// The roots, date and card name of `<root>\<date>\<card>` folders — the same date and name on every destination,
    /// so OffloadPlan.CardPaths gives back exactly these folders.
    static (List<string> Roots, string Date, string CardName) Split(List<string> paths)
    {
        var roots = new List<string>();
        string? date = null, name = null;
        foreach (var p in paths)
        {
            var parent = Path.GetDirectoryName(p);
            var root = parent == null ? null : Path.GetDirectoryName(parent);
            if (parent == null || root == null) throw OffloadError.Preflight(PreflightCode.AppendUnavailable, Msg.AppendNotACardFolder(p));
            var thisDate = Path.GetFileName(parent);
            var thisName = Path.GetFileName(p);
            date ??= thisDate;
            name ??= thisName;
            bool same = string.Equals(thisDate, date, StringComparison.OrdinalIgnoreCase) && string.Equals(thisName, name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Layout.CardPath(root, date, name).TrimEnd('\\'), p, StringComparison.OrdinalIgnoreCase);
            if (!same) throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.AppendDifferentFolders(paths));
            roots.Add(root);
        }
        return (roots, date!, name!);
    }

    static string Generace(int n) => Desktop.Texts.Plural(n, "generaci", "generace", "generací");

    static IEnumerable<string> Ancestors(string rel)
    {
        for (int i = rel.IndexOf('/'); i >= 0; i = rel.IndexOf('/', i + 1)) yield return rel[..i];
    }

    /// "CLIP/A001C003.mov (karta 1,2 GB · 6. 10. 9:14, D: 1,1 GB · 6. 10. 9:10)"
    internal static string Conflict(SourceFile card, string disk, long size, DateTime modifiedUtc)
    {
        // A difference smaller than the rounding ("100 KB" on both sides) shows the exact bytes instead.
        var (a, b) = Format.BytesCs(card.Size) == Format.BytesCs(size) && card.Size != size ? ($"{card.Size} B", $"{size} B") : (Format.BytesCs(card.Size), Format.BytesCs(size));
        return $"{card.RelPath}: jiná velikost nebo datum (karta {a} · {When(card.ModifiedUtc)}, {disk} {b} · {When(modifiedUtc)})";
    }
}
