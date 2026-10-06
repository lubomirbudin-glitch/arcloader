using System.Text;
using System.Xml.Linq;
using Offload.Core.Append;
using Offload.Core.History;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Spec 2.2: an append writes generation N into the same card folders — new files only, nothing overwritten.
public class AppendEngineTests
{
    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, IReadOnlyList<string>? appendInto = null,
        Action<int>? beforeFile = null, bool verify = true, bool recordHistory = true)
    {
        var options = AppendFixtures.Options();
        options.BeforeFileCopy = beforeFile;
        options.Verify = verify;
        var config = new JobConfig(card, dests)
        {
            Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history, AppendInto = appendInto, RecordHistory = recordHistory,
        };
        return new OffloadJob(config, options);
    }

    static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.GetFiles(dir).ToDictionary(f => Path.GetFileName(f), f => File.ReadAllBytes(f));

    /// The only earlier files an append may rewrite (spec 2.2): the hash list (lines added) and the chain (entry added).
    static readonly string[] Rewritten = [@"_REPORT\A001.xxh64", @"ascmhl\ascmhl_chain.xml"];

    [Fact]
    public void Generation2_OnBothDestinations_EarlierFilesUntouched()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1"), dir.Sub("d2")], history);
        full.Run();
        var paths = full.Plan!.CardPaths;
        var before = paths.Select(p => Snapshot(Path.Combine(p, "_REPORT"))).ToList();
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        AppendFixtures.NewClip(card);

        var append = Job(card, [], history, appendInto: paths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.True(append.Status().SafeToEject);
        Assert.Null(append.Status().Warning);
        Assert.Equal((PlanMode.Append, 2), (append.Plan!.Mode, append.Plan.Generation));
        for (int d = 0; d < paths.Count; d++)
        {
            // Every earlier file byte for byte — the data, the reports, the PDF, generation 0001 — but the two rewritten ones.
            var after = AppendFixtures.Tree(paths[d]);
            foreach (var (name, bytes) in trees[d].Where(kv => !Rewritten.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                Assert.Equal(bytes, after[name]);
            var report = Path.Combine(paths[d], "_REPORT");
            foreach (var name in new[] { "offload_report_0002.json", "offload_report_0002.txt", "A001_report_0002.html" })
                Assert.True(File.Exists(Path.Combine(report, name)), name);
            Assert.Contains("doplnění (generace 2)", File.ReadAllText(Path.Combine(report, "A001_report_0002.html")));
            var json = OffloadReport.FromJson(File.ReadAllText(Path.Combine(report, "offload_report_0002.json")));
            Assert.Equal((2, 1), (json.Generation, json.Files.Count));
            var list = File.ReadAllText(Path.Combine(report, "A001.xxh64"));
            Assert.StartsWith(Encoding.UTF8.GetString(before[d]["A001.xxh64"]), list);
            Assert.Contains("  CLIP/A001C003.mov\n", list);
            Assert.Equal(2, list.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

            var mhl = MhlReader.Read(paths[d])!;
            Assert.Empty(mhl.ChainProblems);   // two chain entries, both C4 ids valid
            Assert.Equal([1, 2], mhl.Generations.Select(g => g.Seq));
            Assert.Equal([1, 2], mhl.Chain.Select(c => c.Seq));
            Assert.Equal(["CLIP/A001C003.mov"], mhl.Generations[1].Files.Select(f => f.Path));
            Assert.Equal("original", Assert.Single(mhl.Generations[1].Files[0].Hashes).Action);
            Assert.Equal(mhl.Generations[0].Ignore, mhl.Generations[1].Ignore);
            var gen2 = XDocument.Load(Directory.GetFiles(Path.Combine(paths[d], "ascmhl"), "0002_A001_*.mhl").Single());
            XNamespace ns = "urn:ASC:MHL:v2.0";
            Assert.Equal("transfer", gen2.Descendants(ns + "process").Single().Value);
            Assert.StartsWith("Offload doplnění: 1 nový soubor z ", gen2.Descendants(ns + "comment").Single().Value);

            var verify = BackupVerifier.Verify(paths[d], chunkSize: 4096);
            Assert.True(verify.Success);
            Assert.Equal(2, verify.Ok);
        }
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.Equal([1, 2], entry.Generations.Select(g => g.Seq));
        Assert.Equal(HistoryGeneration.Append, entry.Generations[1].Kind);
        Assert.Equal(2, entry.Files);
        Assert.Equal(CardFingerprint.Compute(Scanner.Scan(card)).Fingerprint, entry.Fingerprint);
    }

    [Fact]
    public void NothingNew_IsDone_NotAnError()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history);
        full.Run();
        var tree = AppendFixtures.Tree(full.Plan!.CardPaths[0]);

        var append = Job(card, [], history, appendInto: full.Plan.CardPaths);
        append.Run();

        var s = append.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.Equal("Nic nového — záloha je úplná.", s.Note);
        Assert.Null(s.Error);
        AppendFixtures.AssertSameTree(tree, full.Plan.CardPaths[0]);
    }

    // Minors M17: "nothing new" still brings the history entry up to date (no new generation) — otherwise the GUI keeps
    // offering an append that has nothing to copy.
    [Fact]
    public void NothingNew_RefreshesTheHistoryEntry()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history);
        full.Run();
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);
        Job(card, [], history, appendInto: full.Plan!.CardPaths, recordHistory: false).Run();   // CLI --no-history
        var store = new HistoryStore(history);
        var stale = Assert.Single(store.Load());
        Assert.NotEqual(CardFingerprint.Compute(Scanner.Scan(card)).Fingerprint, stale.Fingerprint);

        var append = Job(card, [], history, appendInto: full.Plan.CardPaths);
        append.Run();

        Assert.Equal(Msg.NothingNew, append.Status().Note);
        var entry = Assert.Single(store.Load());
        Assert.Equal(CardFingerprint.Compute(Scanner.Scan(card)).Fingerprint, entry.Fingerprint);
        Assert.Equal([1], entry.Generations.Select(g => g.Seq));
        Assert.Equal(["CLIP/A001C001.mov", "CLIP/A001C002.mov"], store.ReadFileList(entry)!.Select(f => f.RelPath));
        Assert.Equal(2, entry.Files);
    }

    // Review focus 4 + spec 2.2 "zrušení uprostřed": the files done stay and are recorded, history is not touched;
    // the next append copies the rest as generation 3.
    [Fact]
    public void CancelledAppend_RecordsWhatIsDone_NoHistory_NextAppendCopiesTheRest()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1"), dir.Sub("d2")], history);
        full.Run();
        var paths = full.Plan!.CardPaths;
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);
        AppendFixtures.NewClip(card, "A001C003.mov", seed: 3);
        OffloadJob? append = null;
        append = Job(card, [], history, appendInto: paths, beforeFile: i => { if (i == 1) append!.Cancel(); });

        append.Run();

        Assert.Equal(JobPhase.Cancelled, append.Status().Phase);
        foreach (var cardPath in paths)
        {
            var json = OffloadReport.FromJson(File.ReadAllText(Path.Combine(cardPath, "_REPORT", "offload_report_0002.json")));
            Assert.Equal(["CLIP/A001C002.mov", "CLIP/A001C003.mov"], json.Files.Select(f => f.Path));
            Assert.Equal(["written", "written"], json.Files[0].Destinations);
            Assert.Equal("cancelled", json.Files[1].SourceError);
            Assert.False(File.Exists(Layout.Join(cardPath, "CLIP/A001C003.mov")));
            var mhl = MhlReader.Read(cardPath)!;
            Assert.Empty(mhl.ChainProblems);
            Assert.Equal(["CLIP/A001C002.mov"], mhl.Generations[1].Files.Select(f => f.Path));
        }
        Assert.Equal([1], Assert.Single(new HistoryStore(history).Load()).Generations.Select(g => g.Seq));

        var again = Job(card, [], history, appendInto: paths);
        again.Run();

        Assert.Equal(JobPhase.Done, again.Status().Phase);
        Assert.Equal(3, again.Plan!.Generation);
        Assert.Equal(["CLIP/A001C003.mov"], again.Plan.Scan.Files.Select(f => f.RelPath));
        // A001C002 was written but never read back by the cancelled append.
        Assert.Equal(Msg.UnverifiedEarlier(1), again.Status().Warning);
        for (int d = 0; d < paths.Count; d++)
        {
            var after = AppendFixtures.Tree(paths[d]);
            foreach (var (name, bytes) in trees[d].Where(kv => !Rewritten.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                Assert.Equal(bytes, after[name]);
            Assert.True(File.Exists(Path.Combine(paths[d], "_REPORT", "offload_report_0003.json")));
            var verify = BackupVerifier.Verify(paths[d], chunkSize: 4096);
            Assert.True(verify.Success);
            Assert.Equal(3, verify.Ok);
            Assert.Equal([1, 2, 3], MhlReader.Read(paths[d])!.Chain.Select(c => c.Seq));
        }
        Assert.Equal([1, 3], Assert.Single(new HistoryStore(history).Load()).Generations.Select(g => g.Seq));
    }

    [Fact]
    public void Conflict_NothingIsWritten()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        File.AppendAllText(Path.Combine(card, "CLIP", "A001C001.mov"), "x");
        AppendFixtures.NewClip(card);
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        var history = dir.Sub("appdata");

        var append = Job(card, [], history, appendInto: paths);
        append.Run();

        Assert.Equal(JobPhase.Failed, append.Status().Phase);
        Assert.StartsWith("Doplnění zastaveno — nic se nezkopírovalo.", append.Status().Error);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
        Assert.Empty(new HistoryStore(history).Load());
    }

    // Two destinations, one of them got an append the other did not (an append into it alone): refused, nothing written.
    [Fact]
    public void OneDestinationDiverged_Refused_NothingWritten()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var history = dir.Sub("appdata");
        AppendFixtures.NewClip(card);
        var only = Job(card, [], history, appendInto: [paths[1]]);
        only.Run();
        Assert.Equal(JobPhase.Done, only.Status().Phase);
        AppendFixtures.NewClip(card, "A001C004.mov", seed: 4);
        var trees = paths.Select(AppendFixtures.Tree).ToList();

        var append = Job(card, [], history, appendInto: paths);
        append.Run();

        Assert.Equal(JobPhase.Failed, append.Status().Phase);
        var disk = AppendPlanner.Disk(paths[0]);
        Assert.Equal($"Zálohy na cílech se liší ({disk} má 1 generaci MHL, {disk} 2) — zkontrolujte je přes Nástroje → Ověřit zálohu…; doplnit nejde.",
            append.Status().Error);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    // Ruling I1 end to end: an append whose MHL generation never landed leaves report 0002; the next one refuses and
    // leaves that report alone.
    [Fact]
    public void ReportWithoutItsMhlGeneration_Refused_NothingWritten()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var history = dir.Sub("appdata");
        AppendFixtures.NewClip(card);
        Job(card, [], history, appendInto: paths).Run();
        foreach (var p in paths)
        {
            // Generation 0002 lost from the chain and the folder: the reports say 2, the MHL says 1.
            foreach (var f in Directory.GetFiles(Path.Combine(p, "ascmhl"), "0002_*.mhl")) File.Delete(f);
            var gen1 = Path.GetFileName(Directory.GetFiles(Path.Combine(p, "ascmhl"), "0001_*.mhl").Single());
            MhlFixtures.Chain(p, gen1);
        }
        AppendFixtures.NewClip(card, "A001C004.mov", seed: 4);
        var trees = paths.Select(AppendFixtures.Tree).ToList();

        var append = Job(card, [], history, appendInto: paths);
        append.Run();

        Assert.Equal(JobPhase.Failed, append.Status().Phase);
        Assert.StartsWith($"V {paths[0]} nesouhlasí reporty s ASC MHL (poslední report je generace 2, poslední generace MHL 1)", append.Status().Error);
        for (int d = 0; d < paths.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    // Ruling I1: the "_NNNN" reports are created, never replaced — not even by a writer that raced past the planner.
    [Fact]
    public void AppendReport_NeverReplacesAnExistingOne()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Reports.Sample(card).Write(card, includeHashRecords: true);
        var existing = Path.Combine(card, "_REPORT", "offload_report_0002.json");
        File.WriteAllText(existing, "earlier append");
        var tree = AppendFixtures.Tree(card);
        var append = Reports.Sample(card);
        append.Generation = 2;

        Assert.ThrowsAny<Exception>(() => append.Write(card, includeHashRecords: true));

        AppendFixtures.AssertSameTree(tree, card);   // nor the hash list, nor the MHL, and no temporary file left behind
    }

    // Minors M26: unverified files of the earlier backup give the orange warning; the append itself is fine.
    [Fact]
    public void UnverifiedEarlierBackup_Warning()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 10_000, 2);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history, verify: false);
        full.Run();
        AppendFixtures.NewClip(card);

        var append = Job(card, [], history, appendInto: full.Plan!.CardPaths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal("2 soubory z dřívější zálohy nebyly ověřeny — doporučeno Ověřit zálohu.", append.Status().Warning);
    }

    // Fix round 1, item 1: two appends into the same card folders at once (GUI + CLI). The second refuses before it
    // reads or writes anything; the first one's backup is whole.
    [Fact]
    public void TwoAppendsAtOnce_TheSecondRefuses_TheFirstVerifiesClean()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var history = dir.Sub("appdata");
        AppendFixtures.NewClip(card);
        AppendFixtures.NewClip(card, "A001C004.mov", seed: 4);
        OffloadJob? loser = null;
        List<Dictionary<string, byte[]>>? before = null, during = null;
        var winner = Job(card, [], history, appendInto: paths, beforeFile: i =>
        {
            if (i != 0) return;
            // The winner holds the folders and has not written a byte of data yet: run the second append now, on
            // another thread (a mutex is re-entrant on its own thread).
            before = paths.Select(AppendFixtures.Tree).ToList();
            loser = Job(card, [], history, appendInto: [paths[1].ToLowerInvariant() + "\\", paths[0]]);   // other spelling, other order
            var t = new Thread(() => loser.Run());
            t.Start();
            t.Join();
            during = paths.Select(AppendFixtures.Tree).ToList();
        });

        winner.Run();

        Assert.Equal(JobPhase.Failed, loser!.Status().Phase);
        Assert.Contains(loser.Status().Error, loser.Config.AppendInto!.Select(Msg.AppendBusy));
        Assert.Null(loser.Plan);   // refused before planning: nothing read, nothing written
        for (int d = 0; d < paths.Count; d++)
        {
            Assert.Equal(before![d].Keys.Order(), during![d].Keys.Order());
            foreach (var (name, bytes) in before[d]) Assert.Equal(bytes, during[d][name]);
        }
        Assert.Equal(JobPhase.Done, winner.Status().Phase);
        foreach (var p in paths)
        {
            var v = BackupVerifier.Verify(p, chunkSize: 4096);
            Assert.True(v.Success);
            Assert.Equal(4, v.Ok);
        }

        // Released at the end of stage 1: the next append is free to go (and has nothing new).
        var next = Job(card, [], history, appendInto: paths);
        next.Run();
        Assert.Equal(Msg.NothingNew, next.Status().Note);
    }

    [Fact]
    public void AppendLock_Name_SameFolderWhateverTheSpelling()
    {
        using var dir = new TempDir();
        var a = dir.Sub("d1", "2026-10-06", "A001");
        Assert.Equal(AppendLock.Name(a), AppendLock.Name(a.ToUpperInvariant() + "\\"));
        Assert.NotEqual(AppendLock.Name(a), AppendLock.Name(dir.Sub("d1", "2026-10-06", "A002")));
        Assert.StartsWith(@"Local\Offload-append-", AppendLock.Name(a));
        Assert.Equal(@"Local\Offload-append-".Length + 64, AppendLock.Name(a).Length);
    }

    // A full copy goes into a new folder and takes no lock: it runs while an append holds another backup.
    [Fact]
    public void FullCopy_IsNotBlockedByAnAppendLock()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        using var held = AppendLock.Acquire(paths);
        var other = dir.Sub("B001");
        TestFs.WriteRandom(Path.Combine(other, "CLIP", "B001C001.mov"), 10_000, 5);
        var full = Job(other, [dir.Sub("d1")], dir.Sub("appdata"));
        var t = new Thread(() => full.Run());
        t.Start();
        t.Join();
        Assert.Equal(JobPhase.Done, full.Status().Phase);
    }

    // Fix round 1, item 3: "Nic nového" still says that the earlier backup was never read back.
    [Fact]
    public void NothingNew_AfterAnUnverifiedFullCopy_StillWarns()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history, verify: false);   // CLI --no-verify
        full.Run();

        var append = Job(card, [], history, appendInto: full.Plan!.CardPaths);
        append.Run();

        Assert.Equal((JobPhase.Done, Msg.NothingNew), (append.Status().Phase, append.Status().Note));
        Assert.Equal("1 soubor z dřívější zálohy nebyl ověřen — doporučeno Ověřit zálohu.", append.Status().Warning);
    }

    [Fact]
    public void NothingNew_AfterAnAppendCancelledInVerification_StillWarns()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var history = dir.Sub("appdata");
        var full = Job(card, [dir.Sub("d1")], history);
        full.Run();
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);
        var options = AppendFixtures.Options();
        OffloadJob? cancelled = null;
        options.BeforeVerify = () => cancelled!.Cancel();   // every file copied, then cancelled as verification starts
        cancelled = new OffloadJob(new JobConfig(card, []) { AnalyzeClips = false, HistoryFolder = history, AppendInto = full.Plan!.CardPaths }, options);
        cancelled.Run();
        Assert.Equal(JobPhase.Cancelled, cancelled.Status().Phase);

        var append = Job(card, [], history, appendInto: full.Plan.CardPaths);
        append.Run();

        Assert.Equal((JobPhase.Done, Msg.NothingNew), (append.Status().Phase, append.Status().Note));
        Assert.Equal(Msg.UnverifiedEarlier(1), append.Status().Warning);
    }

    // Fix round 1, item 6: a reader holding the hash list or the chain (an indexer, a verify, a backup tool) does not
    // stop the append — both are swapped with ReplaceFile.
    [Fact]
    public void HeldOpenReaders_DoNotBlockTheAppend()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var share = FileShare.ReadWrite | FileShare.Delete;
        using (new FileStream(Path.Combine(paths[0], "_REPORT", "A001.xxh64"), FileMode.Open, FileAccess.Read, share))
        using (new FileStream(Path.Combine(paths[0], "ascmhl", "ascmhl_chain.xml"), FileMode.Open, FileAccess.Read, share))
        {
            var append = Job(card, [], dir.Sub("appdata"), appendInto: paths);
            append.Run();
            Assert.Equal(JobPhase.Done, append.Status().Phase);
        }
        var v = BackupVerifier.Verify(paths[0], chunkSize: 4096);
        Assert.True(v.Success);
        Assert.Equal(3, v.Ok);
        Assert.Equal(3, File.ReadAllText(Path.Combine(paths[0], "_REPORT", "A001.xxh64")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    // Fix round 2, item 6a: Offload's own reader (a verify, a planner) holding the chain open does not stop an append
    // from swapping it — every ascmhl/ read goes through the ReadWrite | Delete sharing of MhlReader. The append runs
    // while MhlReader.Read has the chain open, for the whole append (no retry could outwait it).
    [Fact]
    public void MhlReaderHoldingTheChain_DoesNotBlockTheAppend()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var chain = Path.Combine(paths[0], Mhl.DirName, MhlReader.ChainFileName);
        var gen1 = Path.GetFileName(Directory.GetFiles(Path.Combine(paths[0], Mhl.DirName), "0001_*.mhl").Single());
        var seen = new List<string>();
        OffloadJob? append = null;
        MhlReader.WhileOpen = path =>
        {
            seen.Add(Path.GetFileName(path));
            if (append != null || !string.Equals(path, chain, StringComparison.OrdinalIgnoreCase)) return;
            var watching = MhlReader.WhileOpen;
            MhlReader.WhileOpen = null;   // the append's own reads are not recorded
            append = Job(card, [], dir.Sub("appdata"), appendInto: paths);
            append.Run();
            MhlReader.WhileOpen = watching;
        };
        MhlHistory? read;
        try
        {
            read = MhlReader.Read(paths[0]);
        }
        finally
        {
            MhlReader.WhileOpen = null;
        }

        Assert.Equal((JobPhase.Done, null), (append!.Status().Phase, append.Status().Error));
        // All three reads of ascmhl/ went through the shared open: the chain, the C4 of 0001, its parse.
        Assert.Equal([MhlReader.ChainFileName, gen1, gen1], seen);
        Assert.Equal([1], read!.Chain.Select(c => c.Seq));   // the reader still saw what it opened
        Assert.Equal([1, 2], MhlReader.Read(paths[0])!.Chain.Select(c => c.Seq));
        Assert.True(BackupVerifier.Verify(paths[0], chunkSize: 4096).Success);
    }

    // Fix round 2, item 6a: another program holding the chain without delete sharing for a moment — the swap is retried
    // (Mhl.ChainAttempts × Mhl.ChainPause) instead of failing the append at once.
    [Fact]
    public void ChainHeldBrieflyWithoutDeleteSharing_TheSwapIsRetried()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        AppendFixtures.NewClip(card);
        var ascmhl = Path.Combine(paths[0], Mhl.DirName);
        var reader = new FileStream(Path.Combine(ascmhl, MhlReader.ChainFileName), FileMode.Open, FileAccess.Read, FileShare.Read);
        // Let go a little after the first attempt: generation 0002 is created right before the chain is swapped.
        var releaser = new Thread(() =>
        {
            var until = DateTime.UtcNow.AddSeconds(120);
            while (Directory.GetFiles(ascmhl, "0002_*.mhl").Length == 0 && DateTime.UtcNow < until) Thread.Sleep(2);
            Thread.Sleep(Mhl.ChainPause + TimeSpan.FromMilliseconds(50));
            reader.Dispose();
        });
        releaser.Start();
        var append = Job(card, [], dir.Sub("appdata"), appendInto: paths);

        append.Run();

        releaser.Join();
        Assert.Equal((JobPhase.Done, null), (append.Status().Phase, append.Status().Error));
        Assert.Equal([1, 2], MhlReader.Read(paths[0])!.Chain.Select(c => c.Seq));
    }

    // Fix round 2, item 6b: a hash list whose write failed (step 3 of OffloadReport.Write), then an append with nothing
    // new — that one adds the missed lines, as an append with new files would.
    [Fact]
    public void NothingNew_AfterAHashListFailure_HealsTheList()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var history = dir.Sub("appdata");
        var list = Path.Combine(paths[0], "_REPORT", "A001.xxh64");
        var earlier = File.ReadAllBytes(list);
        AppendFixtures.NewClip(card);
        using (new FileStream(list, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = Job(card, [], history, appendInto: paths);
            failed.Run();
            Assert.Equal(JobPhase.Failed, failed.Status().Phase);
        }
        Assert.DoesNotContain("CLIP/A001C003.mov", File.ReadAllText(list));

        var nothing = Job(card, [], history, appendInto: paths);
        nothing.Run();

        Assert.Equal((JobPhase.Done, Msg.NothingNew, null), (nothing.Status().Phase, nothing.Status().Note, nothing.Status().Warning));
        foreach (var p in paths)
        {
            var bytes = File.ReadAllBytes(Path.Combine(p, "_REPORT", "A001.xxh64"));
            Assert.Equal(earlier, bytes[..earlier.Length]);
            Assert.Equal(1, Encoding.UTF8.GetString(bytes).Split('\n').Count(l => l.EndsWith("  CLIP/A001C003.mov", StringComparison.Ordinal)));
            Assert.Equal(3, Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
        }

        // Whole now: the next "nothing new" leaves it byte for byte.
        var healed = File.ReadAllBytes(list);
        Job(card, [], history, appendInto: paths).Run();
        Assert.Equal(healed, File.ReadAllBytes(list));
    }

    // Fix round 1, item 6 (order): the hash list is written last, after the chain. When it fails, the reports and the
    // chain still agree, so the next append is not refused (ruling I1) — and it adds the lines that were missed.
    [Fact]
    public void HashListFailure_LeavesTheFolderConsistent_NextAppendFillsTheList()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var history = dir.Sub("appdata");
        var list = Path.Combine(paths[0], "_REPORT", "A001.xxh64");
        var earlier = File.ReadAllBytes(list);
        AppendFixtures.NewClip(card);
        using (new FileStream(list, FileMode.Open, FileAccess.Read, FileShare.Read))   // no delete sharing: not replaceable
        {
            var failed = Job(card, [], history, appendInto: paths);
            failed.Run();
            Assert.Equal(JobPhase.Failed, failed.Status().Phase);
            Assert.StartsWith("Report se nepodařilo zapsat: ", failed.Status().Error);
        }
        Assert.Equal(earlier, File.ReadAllBytes(list));
        Assert.Equal([1, 2], MhlReader.Read(paths[0])!.Chain.Select(c => c.Seq));
        AppendFixtures.NewClip(card, "A001C004.mov", seed: 4);

        var next = Job(card, [], history, appendInto: paths);
        next.Run();

        Assert.Equal((JobPhase.Done, 3), (next.Status().Phase, next.Plan!.Generation));
        foreach (var p in paths)
        {
            var bytes = File.ReadAllBytes(Path.Combine(p, "_REPORT", "A001.xxh64"));
            var text = Encoding.UTF8.GetString(bytes);
            Assert.Equal(earlier, bytes[..earlier.Length]);
            Assert.Equal(1, text.Split('\n').Count(l => l.EndsWith("  CLIP/A001C003.mov", StringComparison.Ordinal)));
            Assert.Contains("  CLIP/A001C004.mov\n", text);
            Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
        }
    }

    // Spec 2.2: an existing proxy is never overwritten; a new clip of the same name gets "_2".
    [FfmpegFact]
    public void Proxies_ExistingNeverOverwritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestMedia.MakeClip(Path.Combine(card, "CLIP1", "C0001.MP4"), seconds: 1);
        var history = dir.Sub("appdata");
        var options = new OffloadOptions { ChunkSize = 1 << 20, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ProxyEncoder = "libx264" };
        var proxy = new ProxySettings { Enabled = true, Height = 360 };
        var full = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-06", Proxy = proxy, HistoryFolder = history }, options);
        full.Run();
        var cardPath = full.Plan!.CardPaths[0];
        var first = File.ReadAllBytes(Path.Combine(cardPath, "_PROXY", "C0001.mov"));
        TestMedia.MakeClip(Path.Combine(card, "CLIP2", "C0001.MP4"), seconds: 1, timecode: "02:00:00:00");

        var append = new OffloadJob(new JobConfig(card, []) { Proxy = proxy, HistoryFolder = history, AppendInto = full.Plan.CardPaths }, options);
        var report = append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(cardPath, "_PROXY", "C0001.mov")));
        Assert.Equal("_PROXY/C0001_2.mov", Assert.Single(report!.Clips).ProxyPath);
        Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", "A001_report_0002.html")));
    }
}
