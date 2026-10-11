using System.Text;
using Offload.Core.Append;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// A repair run by the engine: generation 2 on every destination, the bad copy replaced (the old one set aside), the good
/// one never written but read back, the records consistent with plan 6 (reports, ASC MHL, hash list).
public class RepairEngineTests
{
    static (OffloadEngine Engine, OffloadPlan Plan) Prepare(string card, IReadOnlyList<string> paths, OffloadOptions? options = null)
    {
        var engine = new OffloadEngine(options ?? AppendFixtures.Options());
        return (engine, engine.PrepareRepair(engine.PlanRepair(card, paths)));
    }

    /// The files of generation `g` a card folder may gain: its reports and its ASC MHL file.
    static void AssertGenerationFiles(IEnumerable<string> added, int g, params string[] more)
    {
        var expected = new[] { $"_REPORT/offload_report_{g:0000}.json", $"_REPORT/offload_report_{g:0000}.txt", $"_REPORT/A001_report_{g:0000}.html" }.Concat(more);
        var list = added.ToList();
        Assert.Single(list, a => a.StartsWith($"ascmhl/{g:0000}_A001_", StringComparison.Ordinal) && a.EndsWith(".mhl", StringComparison.Ordinal));
        Assert.Equal(expected.Order(StringComparer.Ordinal), list.Where(a => !a.StartsWith("ascmhl/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Repair_RewritesOnlyTheBadCopy_Generation2Everywhere_RecordsConsistent()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var good = Layout.Join(paths[0], RepairFixtures.Bad);
        var goodBytes = File.ReadAllBytes(good);
        var goodTime = File.GetLastWriteTimeUtc(good);
        var badBytes = File.ReadAllBytes(Layout.Join(paths[1], RepairFixtures.Bad));
        var lists = paths.Select(p => File.ReadAllText(Path.Combine(p, "_REPORT", "A001.xxh64"))).ToList();
        var before = paths.Select(RepairFixtures.Snapshot).ToList();
        var (engine, plan) = Prepare(card, paths);

        var report = engine.Run(plan);

        Assert.True(report.SafeToFormat);
        Assert.Equal((PlanMode.Repair, 2, "repair"), (plan.Mode, report.Generation!.Value, report.Kind));
        var f = Assert.Single(report.Files);
        Assert.Equal(RepairFixtures.Bad, f.Path);
        Assert.Equal(["verified", "verified"], f.Destinations);
        Assert.Equal([false, true], f.Rewritten);
        Assert.Equal([new SetAsideCopy(1, RepairFixtures.Bad, "_REPORT/neshoda_0002/CLIP/A001C002.mov")], report.SetAside);
        // The good copy: never written (same bytes, same time); the bad one: set aside byte for byte, replaced by the card's.
        Assert.Equal(goodBytes, File.ReadAllBytes(good));
        Assert.Equal(goodTime, File.GetLastWriteTimeUtc(good));
        Assert.Equal(badBytes, File.ReadAllBytes(Layout.Join(paths[1], "_REPORT/neshoda_0002/CLIP/A001C002.mov")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(card, "CLIP", "A001C002.mov")), File.ReadAllBytes(Layout.Join(paths[1], RepairFixtures.Bad)));
        // Every other file of both card folders byte for byte as it was; only the chain is rewritten (plan 6), and only
        // generation 2's own files are new (plus the set-aside copy on d2).
        AssertGenerationFiles(RepairFixtures.AssertUntouchedExcept(before[0], paths[0], "ascmhl/ascmhl_chain.xml"), 2);
        AssertGenerationFiles(RepairFixtures.AssertUntouchedExcept(before[1], paths[1], "ascmhl/ascmhl_chain.xml", RepairFixtures.Bad), 2,
            "_REPORT/neshoda_0002/CLIP/A001C002.mov");
        for (int d = 0; d < paths.Count; d++)
        {
            Assert.True(File.Exists(Path.Combine(paths[d], "_REPORT", "offload_report_0002.json")));
            Assert.Contains("repair (generation 2)", File.ReadAllText(Path.Combine(paths[d], "_REPORT", "A001_report_0002.html")));
            Assert.Equal(lists[d], File.ReadAllText(Path.Combine(paths[d], "_REPORT", "A001.xxh64")));   // the line is there once
            var mhl = MhlReader.Read(paths[d])!;
            Assert.Empty(mhl.ChainProblems);
            Assert.Equal([1, 2], mhl.Chain.Select(c => c.Seq));
            var gen2 = Assert.Single(mhl.Generations[1].Files);
            // Plan 10 (R2): xxh64 and xxh128, both with the same action.
            Assert.Equal(["xxh64", "xxh128"], gen2.Hashes.Select(h => h.Algorithm));
            Assert.All(gen2.Hashes, h => Assert.Equal(d == 0 ? "verified" : "original", h.Action));
            Assert.Equal(f.Xxh64, gen2.Hashes[0].Value);
            Assert.StartsWith("ArcLoader repair: 1 file copied again from ", File.ReadAllText(Directory.GetFiles(Path.Combine(paths[d], "ascmhl"), "0002_*.mhl").Single()).Split("<comment>")[1]);
            Assert.Empty(CardRecord.Load(paths[d]).FailedFiles);
            var verify = BackupVerifier.Verify(paths[d], chunkSize: 4096);
            Assert.True(verify.Success);
            Assert.Empty(verify.Extra);   // the set-aside copy lives in _REPORT
        }
    }

    // The card reads differently now (same size and time, other bytes) after the plan was made: the copy's own hash shows
    // it before anything is moved — the new copy is thrown away (never renamed into place), the bad copy stays where it is,
    // the records keep the hash the backup recorded (ruling C1), the good copy elsewhere is read back and stays verified.
    [Fact]
    public void CardDiffersWhenRead_NothingMovedNothingWritten_GoodCopyStillVerified()
    {
        using var dir = new TempDir();
        var (card, paths, first) = RepairFixtures.Mismatch(dir);
        var recorded = first.Files.Single(f => f.Path == RepairFixtures.Bad).Xxh64!;
        var lists = paths.Select(p => File.ReadAllText(Path.Combine(p, "_REPORT", "A001.xxh64"))).ToList();
        var before = paths.Select(RepairFixtures.Snapshot).ToList();
        var (engine, plan) = Prepare(card, paths);
        RepairFixtures.Corrupt(Path.Combine(card, "CLIP", "A001C002.mov"));
        var now = RepairFixtures.CardHash(card, RepairFixtures.Bad);

        var report = engine.Run(plan);

        var f = Assert.Single(report.Files);
        Assert.Equal($"card differs from the backup: xxh64 {now} != recorded {recorded}", f.SourceError);
        Assert.Equal(["verified", "failed"], f.Destinations);
        Assert.Equal(recorded, f.Xxh64);   // the record, never the card's new hash
        Assert.False(report.Success);
        Assert.Null(report.SetAside);
        Assert.Contains("the card differs from the backup", File.ReadAllText(Path.Combine(paths[0], "_REPORT", "A001_report_0002.html")));
        // Nothing moved, nothing renamed into place, no temporary copy left: both card folders as they were but for
        // generation 2's own records.
        for (int d = 0; d < paths.Count; d++)
        {
            AssertGenerationFiles(RepairFixtures.AssertUntouchedExcept(before[d], paths[d], "ascmhl/ascmhl_chain.xml"), 2);
            Assert.Equal(lists[d], File.ReadAllText(Path.Combine(paths[d], "_REPORT", "A001.xxh64")));
            var gen2 = Assert.Single(MhlReader.Read(paths[d])!.Generations[1].Files);
            Assert.Equal((d == 0 ? "verified" : "failed", recorded), (gen2.Hashes[0].Action, gen2.Hashes[0].Value));
            Assert.Equal([RepairFixtures.Bad], CardRecord.Load(paths[d]).FailedFiles);
        }
        Assert.True(BackupVerifier.Verify(paths[0], chunkSize: 4096).Success);
    }

    // Ruling C1(d): a card that changed after the backup, "Zkopírovat znovu" pressed three times (each time the card read
    // as recorded when planned and differently when copied — the worst case), then once more with the card changed when
    // planned. The good copy and every record of it stay as they were, Ověřit zálohu still passes there, the changed
    // card's hash is in no record, nothing is set aside; with the original card back the repair finishes.
    [Fact]
    public void ThreeRounds_CardChanged_GoodCopyRecordsIntact()
    {
        using var dir = new TempDir();
        var (card, paths, first) = RepairFixtures.Mismatch(dir);
        var cardFile = Path.Combine(card, "CLIP", "A001C002.mov");
        var original = File.ReadAllBytes(cardFile);
        var time = File.GetLastWriteTimeUtc(cardFile);
        var recorded = first.Files.Single(f => f.Path == RepairFixtures.Bad).Xxh64!;
        var before = paths.Select(RepairFixtures.Snapshot).ToList();
        var lists = paths.Select(p => File.ReadAllText(Path.Combine(p, "_REPORT", "A001.xxh64"))).ToList();
        string changed = "";
        for (int round = 2; round <= 4; round++)
        {
            File.WriteAllBytes(cardFile, original);
            File.SetLastWriteTimeUtc(cardFile, time);
            var (engine, plan) = Prepare(card, paths);
            Assert.Equal(round, plan.Generation);
            RepairFixtures.Corrupt(cardFile);
            changed = RepairFixtures.CardHash(card, RepairFixtures.Bad);
            var report = engine.Run(plan);
            Assert.Equal(["verified", "failed"], Assert.Single(report.Files).Destinations);
        }
        var refused = Assert.Throws<OffloadError>(() => Prepare(card, paths));
        Assert.Equal(PreflightCode.RepairConflict, refused.Code);
        Assert.Contains(changed, refused.Message);   // the new hash: only in the refusal

        for (int d = 0; d < paths.Count; d++)
        {
            var added = RepairFixtures.AssertUntouchedExcept(before[d], paths[d], "ascmhl/ascmhl_chain.xml");
            Assert.All(added, a => Assert.Matches(@"^(_REPORT/(offload_report_000[234]\.(json|txt)|A001_report_000[234]\.html)|ascmhl/000[234]_A001_.*\.mhl)$", a));
            Assert.Equal(lists[d], File.ReadAllText(Path.Combine(paths[d], "_REPORT", "A001.xxh64")));
            // The records (ASC MHL, the hash list) never carry it; the reports only in the file's error text.
            foreach (var file in Directory.GetFiles(Path.Combine(paths[d], "ascmhl")).Append(Path.Combine(paths[d], "_REPORT", "A001.xxh64")))
                Assert.DoesNotContain(changed, File.ReadAllText(file));
            var mhl = MhlReader.Read(paths[d])!;
            Assert.Empty(mhl.ChainProblems);
            Assert.Equal([1, 2, 3, 4], mhl.Chain.Select(c => c.Seq));
            Assert.All(mhl.Generations.Skip(1), g => Assert.Equal(recorded, Assert.Single(g.Files).Hashes[0].Value));
            Assert.All(new[] { 2, 3, 4 }, g => Assert.Equal(recorded,
                OffloadReport.FromJson(File.ReadAllText(Path.Combine(paths[d], "_REPORT", OffloadReport.JsonName(g)))).Files[0].Xxh64));
        }
        Assert.True(BackupVerifier.Verify(paths[0], chunkSize: 4096).Success);
        Assert.Equal([RepairFixtures.Bad], CardRecord.Load(paths[1]).FailedFiles);

        File.WriteAllBytes(cardFile, original);
        File.SetLastWriteTimeUtc(cardFile, time);
        var (last, lastPlan) = Prepare(card, paths);
        Assert.True(last.Run(lastPlan).SafeToFormat);
        Assert.Equal(before[0][RepairFixtures.Bad].Bytes, File.ReadAllBytes(Layout.Join(paths[0], RepairFixtures.Bad)));
        Assert.Equal(before[1][RepairFixtures.Bad].Bytes, File.ReadAllBytes(Layout.Join(paths[1], "_REPORT/neshoda_0005/CLIP/A001C002.mov")));
        foreach (var p in paths) Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
    }

    [Fact]
    public void Progress_PlannedBytesPerDestination()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var (engine, plan) = Prepare(card, paths);

        engine.Run(plan);

        var s = engine.Snapshot();
        Assert.Equal([0L, 60_000L], s.PlannedBytes!);
        Assert.Equal(0, s.Planned(0));
        Assert.Equal(60_000, s.Planned(1));
        Assert.Equal([0L, 60_000L], s.WrittenBytes);
    }

    // A repair cancelled before it copied anything: the good copy keeps a good record ("written" in a report that was not
    // read back) — it is never rewritten or set aside later; the next repair (generation 3) finishes the job.
    [Fact]
    public void CancelledRepair_GoodCopyStaysGood_NextRepairFinishes()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var good = Layout.Join(paths[0], RepairFixtures.Bad);
        var goodTime = File.GetLastWriteTimeUtc(good);
        var options = AppendFixtures.Options();
        var (engine, plan) = Prepare(card, paths, options);
        options.BeforeFileCopy = _ => engine.Cancel();

        var cancelled = engine.Run(plan);

        var f = Assert.Single(cancelled.Files);
        // Fix round 1, item 2: the kept copy was read back before and is untouched — it stays "verified"; not read back
        // in this generation, it is not in its ASC MHL generation.
        Assert.Equal(["verified", "pending"], f.Destinations);
        Assert.Equal("cancelled", f.SourceError);
        Assert.Empty(MhlReader.Read(paths[0])!.Generations[1].Files);
        var next = RepairPlanner.Plan(card, paths);
        Assert.Equal(3, next.Generation);
        Assert.Equal([false, true], next.Write[0]);
        Assert.Equal("_REPORT/neshoda_0003/CLIP/A001C002.mov", next.SetAside[0][1]);
        var (engine2, plan2) = Prepare(card, paths);
        Assert.True(engine2.Run(plan2).SafeToFormat);
        Assert.Equal(goodTime, File.GetLastWriteTimeUtc(good));
        foreach (var p in paths) Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
    }

    // A file a write error left off a disk: copied there; a card that reads differently then leaves nothing in its place.
    [Fact]
    public void WriteFailed_CardDiffersWhenRead_NothingInItsPlace()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var options = AppendFixtures.Options();
        options.BeforeDestWrite = d => { if (d == 0) throw new IOException("The device is not connected."); };
        var full = new OffloadEngine(options);
        var fullPlan = full.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-07");
        full.Run(fullPlan);
        var paths = fullPlan.CardPaths;
        var (engine, plan) = Prepare(card, paths);
        RepairFixtures.Corrupt(Path.Combine(card, "CLIP", "A001C001.mov"));

        var report = engine.Run(plan);

        Assert.Equal(["failed", "verified"], Assert.Single(report.Files).Destinations);
        Assert.False(File.Exists(Layout.Join(paths[0], "CLIP/A001C001.mov")));
        Assert.Empty(Directory.GetFiles(paths[0], "*.offload-part", SearchOption.AllDirectories));
    }

    // The place to set the bad copy aside was taken after planning: the move never replaces — that disk fails the file,
    // its bad copy and the file in the place stay as they are, no temporary copy is left.
    [Fact]
    public void SetAsidePlaceTakenAfterPlanning_NeverOverwritten()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var (engine, plan) = Prepare(card, paths);
        var badBytes = File.ReadAllBytes(Layout.Join(paths[1], RepairFixtures.Bad));
        var taken = TestFs.WriteRandom(Layout.Join(paths[1], "_REPORT/neshoda_0002/CLIP/A001C002.mov"), 10, 7);

        var report = engine.Run(plan);

        Assert.Equal(["verified", "failed"], Assert.Single(report.Files).Destinations);
        Assert.Equal(taken, File.ReadAllBytes(Layout.Join(paths[1], "_REPORT/neshoda_0002/CLIP/A001C002.mov")));
        Assert.Equal(badBytes, File.ReadAllBytes(Layout.Join(paths[1], RepairFixtures.Bad)));
        Assert.Empty(Directory.GetFiles(paths[1], "*.offload-part", SearchOption.AllDirectories));
        Assert.Null(report.SetAside);
        Assert.StartsWith("set aside: ", Assert.Single(report.Files).Errors[1]);
        Assert.Contains("cannot set the mismatching copy aside, it stayed in place", File.ReadAllText(Path.Combine(paths[1], "_REPORT", "A001_report_0002.html")));
    }

    // Ruling M16: a repair is always read back — a kept copy is recorded "verified" only after it was.
    [Fact]
    public void RepairWithoutVerification_Refused()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        var options = AppendFixtures.Options();
        options.Verify = false;
        var engine = new OffloadEngine(options);
        var plan = engine.PlanRepair(card, paths);

        Assert.Throws<OffloadError>(() => engine.PrepareRepair(plan));
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    // After a repair the card folder takes an append like any other: generation 3 is the next one.
    [Fact]
    public void AfterRepair_AppendWritesGeneration3()
    {
        using var dir = new TempDir();
        var (card, paths, _) = RepairFixtures.Mismatch(dir);
        var (engine, plan) = Prepare(card, paths);
        engine.Run(plan);
        AppendFixtures.NewClip(card);

        var append = AppendPlanner.Plan(card, paths);

        Assert.Equal(3, append.Generation);
        Assert.Equal(["CLIP/A001C003.mov"], append.NewFiles.Select(n => n.RelPath));
    }

    // A generation without clip analysis (the repair) never replaces the clip the full copy analysed.
    [Fact]
    public void CardRecord_RepairDoesNotEraseTheAnalysedClip()
    {
        using var dir = new TempDir();
        var path = dir.Sub("d1", "2026-10-07", "A001");
        var full = Reports.Sample(path);
        full.Clips = [new ClipEntry { Clip = new Clip { Name = "c 02", Path = "CLIP/c 02.mov", Segments = ["CLIP/c 02.mov"] }, Info = new ClipInfo { Camera = "FX6" } }];
        full.Write(path, includeHashRecords: true);
        var repair = Reports.Sample(path);
        repair.Generation = 2;
        repair.Kind = OffloadReport.RepairKind;
        File.WriteAllText(Path.Combine(path, "_REPORT", "offload_report_0002.json"), repair.ToJson(), Encoding.UTF8);

        var record = CardRecord.Load(path);

        var clip = Assert.Single(record.Clips, c => c.Entry.Clip.Path == "CLIP/c 02.mov");
        Assert.Equal("FX6", clip.Entry.Info!.Camera);
        Assert.Equal(1, clip.Generation);
    }
}
