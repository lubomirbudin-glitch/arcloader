using Offload.Core.History;

namespace Offload.Core.Append;

/// What a repair copies ("Zkopírovat znovu", UI návrh 4): the files whose newest record failed, each only onto the
/// destinations where it failed, as ASC MHL generation `Generation` in the same card folders. A destination that holds
/// a good copy of such a file gets nothing written — its copy is only read back in the same generation, so every card
/// folder keeps the same generations (R7, plan 6 ruling I1).
public sealed class RepairPlan
{
    public required string Source { get; init; }
    public required IReadOnlyList<string> CardPaths { get; init; }
    public required IReadOnlyList<string> DestRoots { get; init; }
    public required string Date { get; init; }
    public required string CardName { get; init; }
    public required int Generation { get; init; }
    /// The files to repair, as the card has them now, in path order.
    public required IReadOnlyList<SourceFile> Files { get; init; }
    public required IReadOnlyList<string> Dirs { get; init; }
    /// Per file, per destination: true = a new copy is written there.
    public required IReadOnlyList<bool[]> Write { get; init; }
    /// Per file: the hashes the backup recorded from the card (ruling C1 — from a full copy or an append, never from a
    /// repair's report unless no other generation hashed the file); none when the card could never be read.
    public required IReadOnlyList<FileHashes> Expected { get; init; }
    /// Per file, per destination: where the copy that did not match goes first (relative to the card folder), or null.
    public required IReadOnlyList<string?[]> SetAside { get; init; }
    /// Per file, per destination: a kept copy (not written) whose newest record says it was read back
    /// (CardRecord.ReadBack) — a repair that ends before reading it again records it "verified" still.
    public required IReadOnlyList<bool[]> ReadBackBefore { get; init; }
    /// The hash types of the earlier backup — a repair uses them, like an append (spec 3.5).
    public IReadOnlyList<string> HashAlgorithms { get; init; } = ["xxh64"];
    /// Fix round 1, item 1: files on the card that no report of the backup records (shot after it) — not repaired, but
    /// the card must not be ejected as backed up while they are there.
    public int NotInBackup { get; init; }
}

/// Pure decisions over the card and the card folders, like AppendPlanner (R4: the truth is what the destinations hold).
/// Reads only — the card is never written, nor any destination. Refuses (OffloadError.Preflight) before anything is
/// written when the repair would not reproduce what the backup recorded. The rules, against plan 6:
/// - the earlier backup must pass AppendPlanner's steps 0 and 1 (folders, reports, chains, generations);
/// - only a file whose newest record failed is touched, and only on a destination where it failed; a good copy is
///   never written (it is read back and compared with the recorded hash);
/// - the card file must still be what the backup recorded: size and time, and (ruling C1) its hash — the card is read
///   here, before anything is moved or written; a different hash refuses the repair, the new hash only in the message;
/// - a failed copy that is on the disk is moved to `_REPORT\neshoda_NNNN\<path>` before the new one takes its place —
///   never deleted, never overwritten: a taken place gets the next free name (minor M7);
/// - a good copy that is missing from its disk stops the repair (Ověřit zálohu first), as it stops an append.
public static class RepairPlanner
{
    /// `_REPORT/neshoda_0003/<path>`: where generation 3 moves the copies that did not match; `attempt` 2, 3 … give
    /// `neshoda_0003_2`, `neshoda_0003_3` … when that place is taken.
    public static string SetAsidePath(int generation, string relPath, int attempt = 1) =>
        $"{Layout.ReportDirName}/neshoda_{generation:0000}{(attempt > 1 ? $"_{attempt}" : "")}/{relPath}";

    public static RepairPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct = default) =>
        Plan(source, cardPaths, ct, sourceFilesystem: null);

    internal static RepairPlan Plan(string source, IReadOnlyList<string> cardPaths, CancellationToken ct, string? sourceFilesystem)
    {
        AppendPlanner.Backup backup;
        try
        {
            backup = AppendPlanner.CheckBackup(cardPaths);
        }
        catch (OffloadError e) when (e.Code is PreflightCode code)
        {
            throw OffloadError.Preflight(code, Msg.ForRepair(e.Message));   // minor M5: said for a repair
        }
        var (paths, roots, date, cardName, records, generation) = backup;

        // What failed, from the reports on the destinations (never from memory): the newest record of each path.
        var failed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int d = 0; d < paths.Count; d++)
            foreach (var (path, rec) in records[d].Files)
                if (rec.File.SourceError != null || !Good(rec, paths[d], d)) failed.Add(path);
        if (failed.Count == 0) throw OffloadError.Preflight(PreflightCode.RepairNothing, Msg.RepairNothing);

        var scan = Scanner.Scan(source, ct);
        var onCard = scan.Files.ToDictionary(f => f.RelPath, StringComparer.OrdinalIgnoreCase);
        bool cardLocalTime = FileTimes.StoresLocalTime(sourceFilesystem ?? NativeIO.FilesystemType(source));
        var taken = scan.Files.Select(f => f.RelPath).Concat(records.SelectMany(r => r.Files.Keys)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<string>();
        var files = new List<SourceFile>();
        var write = new List<bool[]>();
        var expected = new List<FileHashes>();
        var setAside = new List<string?[]>();
        var readBack = new List<bool[]>();
        foreach (var path in failed)
        {
            ct.ThrowIfCancellationRequested();
            var recorded = records.Select(r => r.Files.GetValueOrDefault(path)).First(r => r != null)!;
            if (!onCard.TryGetValue(path, out var card))
            {
                conflicts.Add(Msg.RepairNotOnCard(path));
                continue;
            }
            if (recorded.File.Size != card.Size || !FileTimes.Close(recorded.File.Modified, card.ModifiedUtc, cardLocalTime))
            {
                conflicts.Add(AppendPlanner.Conflict(card, "záloha", recorded.File.Size, recorded.File.Modified));
                continue;
            }
            if (taken.Contains(DestWriter.PartName(path))) conflicts.Add(Msg.AppendPartNameTaken(path, DestWriter.PartName(path)));
            var mask = new bool[paths.Count];
            var aside = new string?[paths.Count];
            var before = new bool[paths.Count];
            for (int d = 0; d < paths.Count; d++)
            {
                bool exists = File.Exists(Layout.Join(paths[d], path));
                var rec = records[d].Files.GetValueOrDefault(path);
                if (rec != null && Good(rec, paths[d], d))
                {
                    // A good copy: never written — but it must be there to be read back (as an append requires).
                    if (!exists) throw OffloadError.Preflight(PreflightCode.AppendMissingFile, Msg.AppendMissingFile(AppendPlanner.Disk(paths[d]), path));
                    before[d] = CardRecord.ReadBack(rec, paths[d], d);
                    continue;
                }
                mask[d] = true;
                if (exists) aside[d] = FreeSetAside(paths[d], generation, path);
            }
            files.Add(card);
            write.Add(mask);
            setAside.Add(aside);
            readBack.Add(before);
            expected.Add(Expected(records, path));
        }
        // Ruling C1(c): the card read now must give what the backup recorded — checked before anything is written (one
        // read of the few files to repair); the hash it gives instead goes only into the refusal.
        if (conflicts.Count == 0) CheckCardHashes(source, files, expected, conflicts, ct);
        if (conflicts.Count > 0) throw OffloadError.Preflight(PreflightCode.RepairConflict, Msg.RepairConflict(conflicts));

        var dirs = files.SelectMany(f => Ancestors(f.RelPath)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new RepairPlan
        {
            Source = Path.GetFullPath(source), CardPaths = paths, DestRoots = roots, Date = date, CardName = cardName, Generation = generation,
            Files = files, Dirs = dirs, Write = write, Expected = expected, SetAside = setAside, ReadBackBefore = readBack,
            HashAlgorithms = records[0].Generations[0].Report.Algorithms,
            // Fix round 1, item 1: a file shot after the backup is in no report — not repaired, but counted.
            NotInBackup = scan.Files.Count(f => !records.Any(r => r.Files.ContainsKey(f.RelPath))),
        };
    }

    static void CheckCardHashes(string source, List<SourceFile> files, List<FileHashes> expected, List<string> conflicts, CancellationToken ct)
    {
        using var buffer = new AlignedBuffer(1 << 20);
        for (int i = 0; i < files.Count; i++)
        {
            if (expected[i].Xxh64 == null) continue;   // never recorded: nothing to compare with (the copy records it)
            FileHashes now;
            try
            {
                now = Hashing.HashFile(Layout.Join(source, files[i].RelPath), buffer, expected[i].Md5 != null ? HashKind.Xxh64 | HashKind.Md5 : HashKind.Xxh64,
                    ct: ct).Hashes;
            }
            catch (Exception e) when (e is IOError or IOException or UnauthorizedAccessException)
            {
                conflicts.Add(Msg.RepairCardUnreadable(files[i].RelPath, e.Message));
                continue;
            }
            if (now.Xxh64 != expected[i].Xxh64)
                conflicts.Add(Msg.RepairCardDiffers(files[i].RelPath, "xxh64", now.Xxh64Hex!, expected[i].Xxh64Hex!));
            else if (expected[i].Md5 != null && !now.Md5!.AsSpan().SequenceEqual(expected[i].Md5))
                conflicts.Add(Msg.RepairCardDiffers(files[i].RelPath, "md5", now.Md5Hex!, expected[i].Md5Hex!));
        }
    }

    /// Ruling C1(b): what the card must give — the newest hash of `path` that a full copy or an append read from the card;
    /// a repair's report only when no such generation has one (the card could not be read then, a repair copied it first).
    static FileHashes Expected(IReadOnlyList<CardRecord> records, string path)
    {
        ReportFile? Newest(bool repairs) =>
            records.SelectMany(r => r.Generations).Where(g => g.Report.IsRepair == repairs).OrderByDescending(g => g.Generation)
                .SelectMany(g => g.Report.Files.Where(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase) && f.Xxh64 != null))
                .FirstOrDefault();
        var f = Newest(repairs: false) ?? Newest(repairs: true);
        return f == null ? new FileHashes(null, null) : new FileHashes(Convert.ToUInt64(f.Xxh64, 16), f.Md5 is string m ? Convert.FromHexString(m) : null);
    }

    /// The newest record of a file holds a good copy on the destination of `cardPath` (its column in that report). A
    /// copy made while the card could not be read as recorded is never trusted — except a repair's kept copy, which
    /// carries on the state of a copy the repair did not touch.
    static bool Good(RecordedFile rec, string cardPath, int position)
    {
        int column = CardRecord.DestinationIndex(rec.Report, cardPath, position);
        var state = CardRecord.StateOf(rec, cardPath, position);
        bool kept = rec.File.Rewritten is { } rewritten && column < rewritten.Count && !rewritten[column];
        if (rec.File.SourceError != null && !kept) return false;
        return state is "verified" || (state == "written" && !rec.Report.Verified);
    }

    /// Minor M7: the first `neshoda_NNNN[_n]` place in the card folder where nothing is — a place left taken by a repair
    /// that stopped (or by hand) is never overwritten and never blocks the next one.
    static string FreeSetAside(string cardPath, int generation, string relPath)
    {
        var report = Path.Combine(cardPath, Layout.ReportDirName);
        for (int attempt = 1; ; attempt++)
        {
            var rel = SetAsidePath(generation, relPath, attempt);
            var full = Layout.Join(cardPath, rel);
            bool free = !File.Exists(full) && !Directory.Exists(full);
            for (var dir = Path.GetDirectoryName(full); free && dir != null && dir.Length > report.Length; dir = Path.GetDirectoryName(dir))
                if (File.Exists(dir)) free = false;
            if (free) return rel;
        }
    }

    static IEnumerable<string> Ancestors(string rel)
    {
        for (int i = rel.IndexOf('/'); i >= 0; i = rel.IndexOf('/', i + 1)) yield return rel[..i];
    }
}
