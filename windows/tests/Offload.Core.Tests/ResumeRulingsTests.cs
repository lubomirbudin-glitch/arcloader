using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Verification;
using Offload.Core.VerifyInProxy;
using static Offload.Core.Tests.ResumeTests;

namespace Offload.Core.Tests;

/// R6 with the plan 10 rulings: a resume writes only into Offload's own `.offload-part` files and never over a file at
/// its place (R7); a part is resumed only past the cache (M1) and with the block size its manifest says (M2); I5 — the
/// history entry stays "unfinished" until the verification results are written, the card offers "Dokončit přerušenou
/// zálohu" even with nothing new on it, and a backup whose commit failed in one folder (I1) is finished by it.
public class ResumeRulingsTests
{
    sealed class Watch : IDisposable
    {
        readonly string folder;
        public NativeIO.TestVolume Volume { get; } = new();

        public Watch(string folder)
        {
            this.folder = Path.GetFullPath(folder);
            Assert.True(NativeIO.TestVolumes.TryAdd(this.folder, Volume));
        }

        public void Dispose() => NativeIO.TestVolumes.TryRemove(folder, out _);
    }

    static JobConfig Append(string card, IReadOnlyList<string> into, string? history = null) => new(card, [])
    {
        AppendInto = into, AnalyzeClips = false, RecordHistory = history != null, HistoryFolder = history,
        Outputs = new OutputSettings { DayReport = false },
    };

    static bool Pending(string cardPath) =>
        OffloadReport.FromJson(File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json"))).VerifyPending == true;

    static int[] Chain(string cardPath) => [.. MhlReader.ReadChain(Path.Combine(cardPath, "ascmhl"))!.Select(c => c.Seq)];

    static bool Free(IReadOnlyList<string> paths)
    {
        try
        {
            AppendLock.Acquire(paths).Dispose();
            return true;
        }
        catch (OffloadError)
        {
            return false;
        }
    }

    // R7: the resume opens for writing only its own parts — the resumed one (OpenResume) and the new one — never the
    // complete file at its place (its bytes and its time stay).
    [Fact]
    public void Resume_WritesOnlyIntoOwnParts_CompleteFileUntouched()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", 3 * Block + 10, 1);
        var b = CardFile(card, "CLIP/B.mov", 5 * Block + 20, 2);
        CardFile(card, "CLIP/C.mov", 2 * Block, 3);
        var root = dir.Sub("d1");
        var cardPath = CrashedCopy(dir, root, a, b, partBlocks: 3);
        var aPath = Path.Combine(cardPath, "CLIP", "A.mov");
        using var watch = new Watch(root);

        var job = new OffloadJob(Append(card, [cardPath]), Options());
        job.Run();

        Assert.True(job.Status().SafeToFormat);
        Assert.Equal(2, watch.Volume.Writes.Count);
        Assert.All(watch.Volume.Writes, p => Assert.Matches(@"^\.[BC]\.mov\.offload-part$", Path.GetFileName(p)));
        Assert.Equal(a, File.ReadAllBytes(aPath));
        Assert.Equal(T, File.GetLastWriteTimeUtc(aPath));
    }

    // A complete copy at its place whose record never landed (only its part's checkpoint): never written again, only its
    // hash is read from the card and the copy is verified — a bad one fails, and stays as it was.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileAtItsPlaceWithoutRecord_HashOnly_NeverOverwritten(bool damaged)
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 3 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 1);
        var part = Path.Combine(cardPath, "CLIP", ".B.mov.offload-part");
        var final = Path.Combine(cardPath, "CLIP", "B.mov");
        File.Delete(part);
        var onDisk = (byte[])b.Clone();
        if (damaged) onDisk[Block + 5] ^= 1;
        File.WriteAllBytes(final, onDisk);
        File.SetLastWriteTimeUtc(final, T);
        var read = new List<string>();
        var options = Options();
        var job = new OffloadJob(Append(card, [cardPath]), options);
        options.BeforeFileCopy = i => read.Add(job.Plan!.Scan.Files[i].RelPath);

        job.Run();

        Assert.Equal(["CLIP/B.mov"], read);   // only for its hash
        Assert.Equal(onDisk, File.ReadAllBytes(final));
        Assert.Equal(T, File.GetLastWriteTimeUtc(final));
        Assert.Equal(!damaged, job.Status().SafeToFormat);
        Assert.Equal(damaged ? "failed" : "verified", job.Report!.Files.Single(f => f.Path == "CLIP/B.mov").Destinations[0]);
    }

    // M1: a writer that writes through the cache never resumes a part — the file fails at once ("resume: …"), the part
    // is neither truncated nor written.
    [Fact]
    public void Resume_OntoABufferedWriter_FailsAtOnce_PartUntouched()
    {
        using var dir = new TempDir();
        var dest = dir.Sub("d1");
        var part = Path.Combine(dest, ".B.mov.offload-part");
        var bytes = TestFs.WriteRandom(part, 2 * Block, 5);
        var options = new OffloadOptions { ChunkSize = Block, BufferCount = 4, ForceBuffered = true };
        var file = new SourceFile("B.mov", 4 * Block, T);
        using (var w = new DestWriter(0, dest, 1, options))
        {
            w.Begin(file, new PartCheckpoint("B.mov", file.Size, T, Block, [1, 2]));
            w.Finish(0, file);
            w.Wait();
            Assert.Equal(FileState.Failed, w.Results[0].State);
            Assert.StartsWith("resume: ", w.Results[0].Error);
        }
        Assert.Equal(bytes, File.ReadAllBytes(part));
        Assert.False(File.Exists(Path.Combine(dest, "B.mov")));
    }

    // A destination written through the cache: the engine never resumes there — the file is copied from its start (its own
    // part made anew), and the copy is exact.
    [Fact]
    public void BufferedDestination_CopiesFromTheStart_Exact()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", Block, 1);
        var b = CardFile(card, "CLIP/B.mov", 4 * Block, 2);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 2);
        var options = Options();
        options.ForceBuffered = true;
        var job = new OffloadJob(Append(card, [cardPath]), options);

        job.Run();

        Assert.True(job.Status().SafeToFormat);
        Assert.Equal(4L * Block, job.Status().Copy!.ReadBytes);   // B whole from the card, A not at all
        Assert.Equal(b, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "B.mov")));
    }

    // M2: the block size is the manifest's, not the setting's (ChunkMB changed since the crash): A not read again, B
    // resumed after its checkpoint, the verification uses the recorded blocks.
    [Fact]
    public void BlockSizeFromTheManifest_NotTheSetting()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = CardFile(card, "CLIP/A.mov", 3 * Block + 10, 1);
        var b = CardFile(card, "CLIP/B.mov", 5 * Block + 20, 2);
        CardFile(card, "CLIP/C.mov", 2 * Block, 3);
        var cardPath = CrashedCopy(dir, dir.Sub("d1"), a, b, partBlocks: 3);
        var options = Options();
        options.ChunkSize = 2 * Block;   // the setting now
        var read = new List<string>();
        var job = new OffloadJob(Append(card, [cardPath]), options);
        options.BeforeFileCopy = i => read.Add(job.Plan!.Scan.Files[i].RelPath);

        job.Run();

        Assert.Equal(Block, job.Engine.Options.ChunkSize);
        Assert.Equal(["CLIP/B.mov", "CLIP/C.mov"], read);
        Assert.True(job.Status().SafeToFormat);
        Assert.Equal(b, File.ReadAllBytes(Path.Combine(cardPath, "CLIP", "B.mov")));
        Assert.True(BackupVerifier.Verify(cardPath, chunkSize: 4096).Success);
    }

    // I5: "Dnes" and the history line of an unfinished entry — never green, never "s chybami".
    [Fact]
    public void UnfinishedEntry_Today_NeitherGreenNorFailed()
    {
        var e = new HistoryEntry { Id = "x", CardName = "A001", Unfinished = true, Finished = T, Destinations = [new() { Root = @"D:\Zaloha", CardPath = @"D:\Zaloha\2026-10-08\A001" }] };
        var summary = HistoryView.Summary([e]);
        Assert.Contains("1 nedokončená", summary);
        Assert.DoesNotContain("vše ověřeno", summary);
        Assert.DoesNotContain("s chybami", summary);
        Assert.Equal(HistoryTone.Warning, HistoryView.Row(e).Tone);
        Assert.Contains("Dokončit přerušenou zálohu", Msg.RepairUnfinished);
    }

    static readonly CardVolume Vol = new("card1", "1A2B3C4D", 256_060_514_304, "A001", "exFAT");

    /// What the Karty list learns about `card` (fix round 1, item 1: through CardInspector, as the GUI does).
    static CardInsight Inspect(string card, HistoryStore store) =>
        CardInspector.Inspect(card, store.Load(), store, CancellationToken.None, _ => Vol, _ => null, _ => null);

    // I5 + fix round 1, item 1: an unfinished backup with nothing new on the card is reachable and offered — through the
    // inspector the Karty list uses; a finished one with nothing new is not (as before).
    [Fact]
    public void Unfinished_NothingNew_StillOffered()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        CardFile(card, "CLIP/A.mov", Block, 1);
        var cardPath = dir.Sub("d1", "2026-10-08", "A001");
        var store = new HistoryStore(dir.Sub("h"));
        var scan = Scanner.Scan(card);
        var (fingerprint, shape) = CardFingerprint.Compute(scan);
        var e = new HistoryEntry
        {
            Id = "20261008T090000Z-00000001", CardName = "A001", Unfinished = true, Finished = T, Card = Vol.ToHistoryCard(), Fingerprint = fingerprint,
            Shape = shape, Destinations = [new() { Root = dir.Sub("d1"), CardPath = cardPath }],
        };
        Assert.True(store.Record(e, scan.Files));

        var identical = Inspect(card, store);
        Assert.Equal(HistoryMatchKind.Identical, identical.History!.Kind);
        Assert.True(identical.AppendReachable);
        Assert.True(AppendRules.Offer(identical));
        Assert.Equal("Dokončit přerušenou zálohu (dokončí ověření)", AppendRules.CheckboxText(identical.History!));
        Assert.False(BackedUpPrompt.ShouldAsk(identical));
        // A finished backup with nothing new: not reachable, no tick, as before.
        e.Unfinished = false;
        Assert.True(store.Record(e, scan.Files));
        var finished = Inspect(card, store);
        Assert.False(finished.AppendReachable);
        Assert.False(AppendRules.Offer(finished));
    }

    // I5 end to end: the app dies during stage 2 (verify-in-proxy) — history says "nedokončeno", the card re-inserted
    // (nothing new) offers "Dokončit přerušenou zálohu", a repair refuses and points to it; the resume verifies and
    // commits the generation: green, the entry finished.
    [FfmpegFact]
    public void CrashDuringStageTwo_DokoncitOffered_ResumeGoesGreen()
    {
        using var dir = new TempDir();
        var card = VerifyInProxyRulingsTests.Card(dir);
        var job = new OffloadJob(VerifyInProxyRulingsTests.Config(card, [dir.Sub("d1"), dir.Sub("d2")], dir), VerifyInProxyRulingsTests.Options())
        {
            VolumeOf = _ => Vol,   // the card's volume, as history knows it again
        };
        Assert.True(job.RunCopy());
        var store = new HistoryStore(Path.Combine(dir.Path, "history"));
        var entry = Assert.Single(store.Load());
        Assert.True(entry.Unfinished);
        Assert.Equal(("! nedokončeno", HistoryTone.Warning), (HistoryView.Row(entry).Status, HistoryView.Row(entry).Tone));
        job.Abandon();   // the app ended here: no stage 2, no commit
        var paths = job.Plan!.CardPaths;
        Assert.All(paths, p => Assert.True(Pending(p)));

        var insight = Inspect(card, store);   // the card inserted again, as the Karty list reads it
        var match = insight.History!;
        Assert.Equal(HistoryMatchKind.Identical, match.Kind);
        Assert.True(insight.AppendReachable);
        Assert.True(AppendRules.Offer(insight));
        Assert.StartsWith("Dokončit přerušenou zálohu", AppendRules.CheckboxText(match));
        Assert.False(BackedUpPrompt.ShouldAsk(insight));
        var refused = Assert.Throws<OffloadError>(() => RepairPlanner.Plan(card, paths));
        Assert.Contains("Dokončit přerušenou zálohu", refused.Message);

        var config = VerifyInProxyRulingsTests.Config(card, [], dir);
        config.AppendInto = [.. entry.Destinations.Select(d => d.CardPath)];
        var resume = new OffloadJob(config, VerifyInProxyRulingsTests.Options());
        resume.Run();

        Assert.True(resume.Plan!.Resuming);
        Assert.Equal(1, resume.Plan.Generation);
        Assert.True(resume.Status().SafeToFormat);
        var done = Assert.Single(store.Load());
        Assert.False(done.Unfinished);
        Assert.True(done.Verified);
        Assert.Equal(HistoryGeneration.Full, Assert.Single(done.Generations).Kind);
        Assert.False(AppendRules.Offer(Inspect(card, store)));   // finished: nothing more offered
        Assert.All(paths, p =>
        {
            Assert.False(Pending(p));
            Assert.Equal([1], Chain(p));
            Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
        });
        Assert.True(Free(paths));
    }

    // I1 + I5: the results could not be written into the second folder (the first has its generation): the entry stays
    // "nedokončeno"; "Dokončit přerušenou zálohu" finishes it — one generation 1 in each chain, green.
    [FfmpegFact]
    public void CommitFailedInOneFolder_FinishedByTheResume()
    {
        using var dir = new TempDir();
        var card = VerifyInProxyRulingsTests.Card(dir);
        var d2 = dir.Sub("d2");
        var job = new OffloadJob(VerifyInProxyRulingsTests.Config(card, [dir.Sub("d1"), d2], dir), VerifyInProxyRulingsTests.Options())
        {
            BeforeCommit = path => { if (path.StartsWith(d2, StringComparison.OrdinalIgnoreCase)) throw new IOException("disk full"); },
        };
        job.Run();
        Assert.False(job.Status().SafeToFormat);
        var history = Path.Combine(dir.Path, "history");
        Assert.True(Assert.Single(new HistoryStore(history).Load()).Unfinished);
        var paths = job.Plan!.CardPaths;
        Assert.Equal([1], Chain(paths[0]));
        Assert.True(Pending(paths[1]));
        Assert.NotNull(ResumePlanner.Find(paths));

        var read = new List<int>();
        var options = Options();
        options.BeforeFileCopy = read.Add;
        var resume = new OffloadJob(Append(card, paths, history), options);
        resume.Run();

        Assert.Empty(read);   // nothing read from the card: every copy is on the disks with its record
        Assert.True(resume.Plan!.Resuming);
        Assert.True(resume.Status().SafeToFormat);
        Assert.All(paths, p =>
        {
            Assert.False(Pending(p));
            Assert.Equal([1], Chain(p));
            Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
            Assert.True(CardRecord.Load(p).Verified);
        });
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.False(entry.Unfinished);
        Assert.True(entry.Verified);
    }

    // A folder holding a complete backup of its own (another run) never counts as this run's: diverged, as before.
    [Fact]
    public void Find_CompleteBackupOfAnotherRun_StillDiverged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        CardFile(card, "CLIP/A.mov", Block, 1);
        CardFile(card, "CLIP/B.mov", 2 * Block, 2);
        var options = Options();
        options.DeferVerify = true;
        var engine = new OffloadEngine(options);
        var pending = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")).Destinations[0].CardPath;
        var other = new OffloadEngine(Options());
        var whole = other.Run(other.Prepare(card, [dir.Sub("d2")], null, "2026-10-08")).Destinations[0].CardPath;

        var e = Assert.Throws<OffloadError>(() => ResumePlanner.Find([pending, whole]));
        Assert.Equal(PreflightCode.AppendDiverged, e.Code);
        Assert.NotNull(ResumePlanner.Find([pending]));
    }
}
