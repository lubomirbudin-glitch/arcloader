using Offload.Core.History;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Append;

/// R6: a generation an earlier run started in these card folders and never committed to the ASC MHL chain — the app ended
/// in the middle of the copy (no report for it yet) or of the verification with the proxies (its reports say
/// verifyPending). `Manifests`: per card folder, what its manifest says (every generation). `Fixed`: the generation is in
/// the ASC MHL chain of some folder already (its commit wrote it there, ruling I1) — its files are those and no others;
/// null = not committed anywhere, the card's other new files may join it.
public sealed class Unfinished(int generation, IReadOnlyList<IReadOnlyList<ManifestRecord>> manifests, IReadOnlySet<string>? fixedFiles = null)
{
    public int Generation => generation;
    public IReadOnlyList<IReadOnlyList<ManifestRecord>> Manifests => manifests;
    public IReadOnlySet<string>? Fixed => fixedFiles;
    readonly IReadOnlyDictionary<string, ManifestRecord>[] files = manifests.Select(m => ManifestFile.Files(m, generation)).ToArray();
    readonly IReadOnlyDictionary<string, PartCheckpoint>[] parts = manifests.Select(m => ManifestFile.Parts(m, generation)).ToArray();

    /// Folder `d`'s `file` record of `f` for this generation, when it is the card's file (size, time).
    public ManifestRecord? FileOf(int d, SourceFile f, bool localTime) =>
        files[d].TryGetValue(f.RelPath, out var r) && r.Size == f.Size && FileTimes.Close(r.Modified, f.ModifiedUtc, localTime) ? r : null;

    /// Folder `d`'s checkpoint of `f` for this generation, when it is the card's file (size, time).
    public PartCheckpoint? PartOf(int d, SourceFile f, bool localTime) =>
        parts[d].TryGetValue(f.RelPath, out var p) && p.Size == f.Size && FileTimes.Close(p.Modified, f.ModifiedUtc, localTime) ? p : null;

    /// The run that never committed wrote `f` there: a file at its place with the card's size whose record (or checkpoint —
    /// it was renamed into place before its record was written) names the card's file.
    public bool Owns(int d, SourceFile f, long sizeOnDisk, bool localTime) =>
        sizeOnDisk == f.Size && (FileOf(d, f, localTime) != null || PartOf(d, f, localTime) != null);

    /// The manifest of some folder mentions `f` (the card is the one that run copied — ruling I3 for a resume).
    public bool Mentions(SourceFile f) => files.Any(x => x.ContainsKey(f.RelPath)) || parts.Any(x => x.ContainsKey(f.RelPath));
}

/// R6, tied into the append (plan 6): finds an unfinished generation and decides, per card file, what a resume does —
/// complete copies are never copied again, a part on the disk goes on from its checkpoint. Reads only.
public static class ResumePlanner
{
    /// The unfinished generation of the card folders, or null when every one's newest generation is committed (then the
    /// append plans as before). Refuses (AppendDiverged) folders that disagree.
    /// A folder is open at generation g when g's report says verifyPending — g after its committed generation (the run
    /// ended before its commit), or g its committed one (the commit wrote the ASC MHL generation but not the reports, plan
    /// 10 fix round 1), or g its committed one whose report says verifyIncomplete (the commit failed in another folder,
    /// final review item 2) — or when there is no report of g after its committed one and its manifest has records of g (the
    /// run ended during the copy). Every other folder must stand right before g with no report of it, or (ruling I1: the
    /// results were written into some folders only) have g committed by the same run.
    public static Unfinished? Find(IReadOnlyList<string> cardPaths)
    {
        var states = cardPaths.Select(p => (Path: p, Committed: Committed(p), Records: ManifestFile.Read(p))).ToList();
        static int? OpenAt((string Path, int Committed, IReadOnlyList<ManifestRecord> Records) s)
        {
            if (s.Committed > 0 && Unsettled(s.Path, s.Committed)) return s.Committed;
            int next = s.Committed + 1;
            return Pending(s.Path, next) || (!ReportExists(s.Path, next) && s.Records.Any(r => r.Generation == next)) ? next : null;
        }
        var open = states.Select(OpenAt).ToList();
        if (open.FirstOrDefault(o => o != null) is not int g) return null;
        var pendingReport = states.Where((s, d) => open[d] == g && Unsettled(s.Path, g)).Select(s => Read(s.Path, g)).FirstOrDefault(r => r != null);
        // Some chain has g already: the generation's files are fixed (its MHL lists them), as its reports say.
        IReadOnlySet<string>? fixedFiles = states.Any(s => s.Committed == g) && pendingReport != null
            ? pendingReport.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal) : null;
        for (int d = 0; d < states.Count; d++)
        {
            var s = states[d];
            if (open[d] == g) continue;
            bool before = open[d] == null && s.Committed == g - 1 && !ReportExists(s.Path, g);
            bool doneBySameRun = open[d] == null && s.Committed == g && pendingReport != null && Read(s.Path, g) is { } done && SameRun(done, pendingReport);
            if (!before && !doneBySameRun)
                throw OffloadError.Preflight(PreflightCode.AppendDiverged, Msg.ResumeDiverged(s.Path, g));
        }
        return new Unfinished(g, states.Select(s => s.Records).ToList(), fixedFiles);
    }

    /// Two reports of one generation written by the same run: the same start and the same card folders.
    static bool SameRun(OffloadReport a, OffloadReport b) =>
        a.Started == b.Started && a.Destinations.Count == b.Destinations.Count
        && a.Destinations.Zip(b.Destinations).All(x => string.Equals(x.First.CardPath.TrimEnd('\\'), x.Second.CardPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    static OffloadReport? Read(string cardPath, int generation)
    {
        try
        {
            return ReportExists(cardPath, generation) ? OffloadReport.FromJson(File.ReadAllText(ReportPath(cardPath, generation))) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// The newest committed generation: the ASC MHL chain's (0 without one).
    static int Committed(string cardPath) =>
        MhlReader.ReadChain(Path.Combine(cardPath, Mhl.DirName)) is { Count: > 0 } chain ? chain.Max(c => c.Seq) : 0;

    static string ReportPath(string cardPath, int generation) => Path.Combine(cardPath, Layout.ReportDirName, OffloadReport.JsonName(generation));

    static bool ReportExists(string cardPath, int generation) => File.Exists(ReportPath(cardPath, generation));

    /// The report of `generation` says its verification never ended (plan 10: verifyPending).
    static bool Pending(string cardPath, int generation) => Read(cardPath, generation)?.VerifyPending == true;

    /// Pending, or its results were written here but not into every folder (verifyIncomplete, fix round 1 M-a): the
    /// generation is still open at this folder's committed one — "Finish" finishes it (final review, item 2).
    static bool Unsettled(string cardPath, int generation) =>
        Read(cardPath, generation) is { } r && (r.VerifyPending == true || r.VerifyIncomplete == true);

    /// What a resume does with each card file of the unfinished generation (`files`, in copy order; `present[i][d]`: on
    /// the disk at its place): written where it is missing; not read at all when every folder has it with a record
    /// (Known); read only for its hash when every folder has it without one (HashOnly); resumed from the checkpoint every
    /// folder it is written to has — the shortest one, and only as far as all of them agree.
    public static (IReadOnlyList<bool[]> Write, IReadOnlyList<ManifestRecord?> Known, IReadOnlyList<bool> HashOnly, IReadOnlyList<PartCheckpoint?> Resume)
        Decide(Unfinished u, IReadOnlyList<string> cardPaths, IReadOnlyList<SourceFile> files, IReadOnlyList<bool[]> present, IReadOnlyList<bool> localTime)
    {
        var write = new List<bool[]>();
        var known = new List<ManifestRecord?>();
        var hashOnly = new List<bool>();
        var resume = new List<PartCheckpoint?>();
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var mask = present[i].Select(p => !p).ToArray();
            write.Add(mask);
            var records = Enumerable.Range(0, cardPaths.Count).Select(d => u.FileOf(d, f, localTime[d])).ToList();
            bool everywhere = mask.All(m => !m);
            bool sameRecord = records.All(r => r != null) && records.Select(r => r!.Xxh64).Distinct().Count() == 1;
            known.Add(everywhere && sameRecord ? records[0] : null);
            hashOnly.Add(everywhere && !sameRecord);
            resume.Add(mask.Any(m => m) ? CommonCheckpoint(u, cardPaths, f, mask, localTime) : null);
        }
        return (write, known, hashOnly, resume);
    }

    static PartCheckpoint? CommonCheckpoint(Unfinished u, IReadOnlyList<string> cardPaths, SourceFile f, bool[] mask, IReadOnlyList<bool> localTime)
    {
        PartCheckpoint? common = null;
        for (int d = 0; d < cardPaths.Count; d++)
        {
            if (!mask[d]) continue;
            var part = new FileInfo(Layout.Join(cardPaths[d], DestWriter.PartName(f.RelPath)));
            if (u.PartOf(d, f, localTime[d]) is not { Blocks.Length: > 0 } p || !part.Exists || part.Length < (long)p.Blocks.Length * p.BlockSize) return null;
            if (common == null) { common = p; continue; }
            if (common.BlockSize != p.BlockSize) return null;
            int same = 0;
            while (same < Math.Min(common.Blocks.Length, p.Blocks.Length) && common.Blocks[same] == p.Blocks[same]) same++;
            if (same == 0) return null;
            common = common with { Blocks = common.Blocks[..same] };
        }
        return common;
    }
}
