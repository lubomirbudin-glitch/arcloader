using System.Text.Json.Nodes;
using Offload.Core.Append;

namespace Offload.Core.Tests;

public static class RepairFixtures
{
    public const string Bad = "CLIP/A001C002.mov";

    /// Card A001 (two clips) copied onto d1 and d2; on d2 the copy of A001C002.mov is changed before the read-back, so it
    /// fails there with HASH MISMATCH — the case of screen 03 ("1 soubor na disku d2 nesouhlasí s kartou").
    public static (string Card, IReadOnlyList<string> CardPaths, OffloadReport Report) Mismatch(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 60_000, 2);
        var options = AppendFixtures.Options();
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");
        options.BeforeVerify = () => Corrupt(Layout.Join(plan.CardPaths[1], Bad));
        var report = engine.Run(plan);
        return (card, plan.CardPaths, report);
    }

    /// One byte of the file changed — same size, same time (a card that reads differently, a copy that rotted).
    public static void Corrupt(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var time = File.GetLastWriteTimeUtc(path);
        bytes[100] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, time);
    }

    /// Every file under `dir` (relative, "/") with its bytes and time — "never written" is this, unchanged.
    public static Dictionary<string, (byte[] Bytes, DateTime Time)> Snapshot(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(dir, f).Replace('\\', '/'), f => (File.ReadAllBytes(f), File.GetLastWriteTimeUtc(f)), StringComparer.OrdinalIgnoreCase);

    /// Every file of `before` is still there byte for byte with its time, except the paths in `mayChange` (the record
    /// files a generation may rewrite); returns the files that are new.
    public static List<string> AssertUntouchedExcept(Dictionary<string, (byte[] Bytes, DateTime Time)> before, string dir, params string[] mayChange)
    {
        var after = Snapshot(dir);
        foreach (var (path, (bytes, time)) in before)
        {
            if (mayChange.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            Assert.True(after.ContainsKey(path), $"{path} is gone from {dir}");
            Assert.True(bytes.AsSpan().SequenceEqual(after[path].Bytes), $"{path} changed in {dir}");
            Assert.True(time == after[path].Time, $"{path} got a new time in {dir}");
        }
        return after.Keys.Where(k => !before.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
    }

    /// The xxh64 the card gives for `rel` now.
    public static string CardHash(string card, string rel) => Verification.HashAlgo.Of("xxh64", File.ReadAllBytes(Layout.Join(card, rel)));
}

/// "Zkopírovat znovu" planning (UI návrh 4): only the failed files, only where they failed; refuse before writing.
public class RepairPlannerTests
{
    static OffloadError Refused(string card, IReadOnlyList<string> paths) =>
        Assert.Throws<OffloadError>(() => RepairPlanner.Plan(card, paths));

    [Fact]
    public void Mismatch_OnlyTheBadDisk_GoodCopyOnlyReadBack_BadCopySetAside()
    {
        using var dir = new TempDir();
        var (card, paths, report) = RepairFixtures.Mismatch(dir);
        Assert.Equal(["verified", "failed"], report.Files.Single(f => f.Path == RepairFixtures.Bad).Destinations);

        var plan = RepairPlanner.Plan(card, paths);

        Assert.Equal([RepairFixtures.Bad], plan.Files.Select(f => f.RelPath));
        Assert.Equal(["CLIP"], plan.Dirs);
        Assert.Equal([false, true], plan.Write[0]);
        Assert.Equal(new string?[] { null, "_REPORT/neshoda_0002/CLIP/A001C002.mov" }, plan.SetAside[0].ToList());
        Assert.Equal(report.Files.Single(f => f.Path == RepairFixtures.Bad).Xxh64, plan.Expected[0].Xxh64Hex);
        Assert.Equal((2, "2026-10-07", "A001"), (plan.Generation, plan.Date, plan.CardName));
        Assert.Equal([dir.Sub("d1"), dir.Sub("d2")], plan.DestRoots);
    }

    [Fact]
    public void EverythingGood_RepairNothing()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        Assert.Equal(PreflightCode.RepairNothing, Refused(card, paths).Code);
    }

    // A write error leaves no file on that disk: it is simply copied, nothing to set aside.
    [Fact]
    public void WriteFailed_NotOnTheDisk_CopiedWithoutSetAside()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var options = AppendFixtures.Options();
        options.BeforeDestWrite = d => { if (d == 0) throw new IOException("Zařízení není připojeno."); };
        var engine = new OffloadEngine(options);
        var full = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");
        engine.Run(full);

        var plan = RepairPlanner.Plan(card, full.CardPaths);

        Assert.Equal([true, false], plan.Write[0]);
        Assert.All(plan.SetAside[0], Assert.Null);
    }

    // No destination holds a good copy — both get a new one, both bad copies are set aside.
    [Fact]
    public void FailedEverywhere_BothRewritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var paths = CopyWithBothCorrupted(dir, card);

        var plan = RepairPlanner.Plan(card, paths);

        Assert.Equal([true, true], plan.Write[0]);
        Assert.All(plan.SetAside[0], a => Assert.Equal("_REPORT/neshoda_0002/CLIP/A001C001.mov", a));
    }

    static IReadOnlyList<string> CopyWithBothCorrupted(TempDir dir, string card)
    {
        var options = AppendFixtures.Options();
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");
        options.BeforeVerify = () => { foreach (var p in plan.CardPaths) RepairFixtures.Corrupt(Layout.Join(p, "CLIP/A001C001.mov")); };
        engine.Run(plan);
        return plan.CardPaths;
    }

    [Fact]
    public void CardChanged_Refused_NothingWritten()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 61_000, 9);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Contains("CLIP/A001C002.mov: jiná velikost nebo datum", e.Message);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    // Ruling C1(c): same size and time, other bytes — the card is hashed before anything is moved or written; the new hash
    // is only in the (Czech) refusal, never in a record.
    [Fact]
    public void CardReadsDifferently_SameSizeAndTime_Refused_NothingWritten()
    {
        using var dir = new TempDir();
        var (card, paths, report) = RepairFixtures.Mismatch(dir);
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        RepairFixtures.Corrupt(Path.Combine(card, "CLIP", "A001C002.mov"));
        var now = RepairFixtures.CardHash(card, RepairFixtures.Bad);
        var recorded = report.Files.Single(f => f.Path == RepairFixtures.Bad).Xxh64!;

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Contains($"CLIP/A001C002.mov: na kartě je jiný obsah než při záloze (xxh64 {now}, v záloze {recorded})", e.Message);
        Assert.StartsWith("Oprava zastavena — nic se nezkopírovalo.", e.Message);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    [Fact]
    public void FileGoneFromCard_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        File.Delete(Path.Combine(card, "CLIP", "A001C002.mov"));

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.RepairConflict, e.Code);
        Assert.Contains("CLIP/A001C002.mov: na kartě už není", e.Message);
    }

    // A good copy that is gone from its disk: something changed the backup — Ověřit zálohu first, as for an append.
    [Fact]
    public void GoodCopyMissing_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        File.Delete(Layout.Join(paths[0], RepairFixtures.Bad));

        Assert.Equal(PreflightCode.AppendMissingFile, Refused(card, paths).Code);
    }

    // Ruling M7: a place left taken (a repair that stopped after its move, by hand) is never overwritten and never blocks
    // a repair — the next free name is used.
    [Fact]
    public void SetAsidePlaceTaken_NextFreeName()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        TestFs.WriteRandom(Layout.Join(paths[1], "_REPORT/neshoda_0002/CLIP/A001C002.mov"), 10, 1);
        Directory.CreateDirectory(Layout.Join(paths[1], "_REPORT/neshoda_0002_2/CLIP/A001C002.mov"));

        var plan = RepairPlanner.Plan(card, paths);

        Assert.Equal("_REPORT/neshoda_0002_3/CLIP/A001C002.mov", plan.SetAside[0][1]);
    }

    // The checks of an append's earlier backup apply (one chain per folder, same generations) — said for a repair.
    [Fact]
    public void ChainMissing_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        File.Delete(Path.Combine(paths[1], "ascmhl", "ascmhl_chain.xml"));
        foreach (var f in Directory.GetFiles(Path.Combine(paths[1], "ascmhl"))) File.Delete(f);

        var e = Refused(card, paths);

        Assert.Equal(PreflightCode.AppendNoMhl, e.Code);
        Assert.EndsWith("opravit nejde.", e.Message);
    }

    // Ruling C1(b): what the card must give is taken from the copies that read the whole card (full copy, append) — never
    // from a repair's report, which only records.
    [Fact]
    public void Expected_FromTheFullCopy_NotFromARepairReport()
    {
        using var dir = new TempDir();
        var (card, paths, report) = RepairFixtures.Mismatch(dir);
        var cardFile = Path.Combine(card, "CLIP", "A001C002.mov");
        var original = File.ReadAllBytes(cardFile);
        var time = File.GetLastWriteTimeUtc(cardFile);
        // A repair whose card read differently (generation 2, the file stays failed) …
        var engine = new OffloadEngine(AppendFixtures.Options());
        var plan = engine.PrepareRepair(engine.PlanRepair(card, paths));
        RepairFixtures.Corrupt(cardFile);
        engine.Run(plan);
        // … with a report that would claim another hash (as a repair before ruling C1 wrote it).
        foreach (var p in paths)
        {
            var json = Path.Combine(p, "_REPORT", "offload_report_0002.json");
            var node = JsonNode.Parse(File.ReadAllText(json))!;
            node["files"]![0]!["xxh64"] = "0123456789abcdef";
            File.WriteAllText(json, node.ToJsonString());
        }
        File.WriteAllBytes(cardFile, original);
        File.SetLastWriteTimeUtc(cardFile, time);

        var next = RepairPlanner.Plan(card, paths);

        Assert.Equal(report.Files.Single(f => f.Path == RepairFixtures.Bad).Xxh64, next.Expected[0].Xxh64Hex);
        Assert.Equal(3, next.Generation);
    }
}
