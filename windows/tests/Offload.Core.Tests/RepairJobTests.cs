using Offload.Core.Append;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Notify;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// "Zkopírovat znovu" as a queue job: the repair after a job with a non-matching file, history and outputs consistent.
public class RepairJobTests
{
    /// A full job whose copy of A001C002.mov on d2 is changed before the read-back (screen 03). `identity`: the card
    /// identity the drive reports (null = a folder source, not checked).
    static OffloadJob FailedJob(TempDir dir, string history, bool dayReport = false, string? identity = null)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 60_000, 2);
        var options = AppendFixtures.Options();
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, Project = "Film X",
            Outputs = new OutputSettings { DayReport = dayReport }, Notify = new Notify.NotifySettings { Windows = false },
        }, options) { Identify = _ => identity };
        options.BeforeVerify = () => RepairFixtures.Corrupt(Layout.Join(job.Plan!.CardPaths[1], RepairFixtures.Bad));
        job.Run();
        return job;
    }

    static OffloadJob Repair(OffloadJob failed, string? identity = null, OffloadOptions? options = null) =>
        new(JobConfigs.Repair(failed), options ?? AppendFixtures.Options()) { Identify = _ => identity };

    [Fact]
    public void Repair_FixesTheCard_HistoryEntryGoodAgain_DayReportMade()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = FailedJob(dir, history, dayReport: true);
        Assert.Equal((JobPhase.Done, false), (failed.Status().Phase, failed.Status().CopySucceeded));
        var store = new HistoryStore(history);
        var before = Assert.Single(store.Load());
        Assert.Equal((false, 1), (before.Success, before.FailedFiles!.Value));

        var repair = Repair(failed);
        repair.Run();

        var s = repair.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.Null(s.Warning);
        Assert.Equal((PlanMode.Repair, 2, "Film X"), (repair.Plan!.Mode, repair.Plan.Generation, repair.Plan.Project));
        var entry = Assert.Single(store.Load());
        Assert.True(entry.Success);
        Assert.True(entry.Verified);
        Assert.Null(entry.FailedFiles);
        Assert.Equal([(1, HistoryGeneration.Full), (2, HistoryGeneration.Repair)], entry.Generations.Select(g => (g.Seq, g.Kind)));
        Assert.Equal((before.Files, before.Fingerprint, before.Bytes), (entry.Files, entry.Fingerprint, entry.Bytes));
        Assert.Equal("Film X", entry.Project);
        Assert.All(repair.DayReports, p => Assert.True(File.Exists(p)));
        Assert.Equal(2, repair.DayReports.Count);
        Assert.Equal(OutcomeKind.Done, JobOutcomes.Final("A001", s, repair.Report)!.Kind);
    }

    [Fact]
    public void NothingToRepair_DoneWithNote()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        Repair(failed).Run();

        var again = Repair(failed);
        again.Run();

        var s = again.Status();
        Assert.Equal((JobPhase.Done, Msg.RepairNothing), (s.Phase, s.Note));
        Assert.Null(again.Report);
    }

    // An append or another repair of the same backup holds the folders: the repair is refused at once, nothing written.
    [Fact]
    public void FoldersBusy_Refused()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        var paths = failed.Plan!.CardPaths;
        var trees = paths.Select(AppendFixtures.Tree).ToList();
        var repair = Repair(failed);
        // A mutex is owned per thread: hold it here, run the job on another thread.
        using (AppendLock.Acquire(paths))
        {
            var t = new Thread(() => repair.Run());
            t.Start();
            t.Join();
        }

        Assert.Equal(JobPhase.Failed, repair.Status().Phase);
        Assert.Contains("právě doplňuje jiná úloha", repair.Status().Error);
        for (int d = 0; d < trees.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
    }

    // Ruling I1: the repair job carries the identity of the card that was copied — another card in the drive (same
    // names, sizes and times) is refused before anything is read or written; the copied card goes ahead.
    [Fact]
    public void AnotherCardInTheDrive_Refused_NothingWritten()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = FailedJob(dir, history, identity: "CARD-A");
        Assert.Equal("CARD-A", JobConfigs.Repair(failed).RepairCardIdentity);
        var paths = failed.Plan!.CardPaths;
        var trees = paths.Select(AppendFixtures.Tree).ToList();

        var other = Repair(failed, identity: "CARD-B");
        other.Run();

        Assert.Equal(JobPhase.Failed, other.Status().Phase);
        Assert.Equal(Msg.RepairOtherCard, other.Status().Error);
        Assert.Null(other.Report);
        for (int d = 0; d < trees.Count; d++) AppendFixtures.AssertSameTree(trees[d], paths[d]);
        Assert.Single(Assert.Single(new HistoryStore(history).Load()).Generations);

        var same = Repair(failed, identity: "card-a");
        same.Run();
        Assert.True(same.Status().SafeToEject);
    }

    // Ruling C1 through the job: a card that changed after the backup, "Zkopírovat znovu" pressed three times — each
    // repair is refused before anything is written (the new hash only in the Czech error), the backup and history as they were.
    [Fact]
    public void CardChanged_ThreeRepairs_AllRefused_NothingWritten()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = FailedJob(dir, history);
        var paths = failed.Plan!.CardPaths;
        var before = paths.Select(RepairFixtures.Snapshot).ToList();
        var entryBefore = Assert.Single(new HistoryStore(history).Load());
        RepairFixtures.Corrupt(Path.Combine(failed.Config.Source, "CLIP", "A001C002.mov"));
        var changed = RepairFixtures.CardHash(failed.Config.Source, RepairFixtures.Bad);

        for (int round = 0; round < 3; round++)
        {
            var repair = Repair(failed);
            repair.Run();
            Assert.Equal(JobPhase.Failed, repair.Status().Phase);
            Assert.StartsWith("Oprava zastavena — nic se nezkopírovalo.", repair.Status().Error);
            Assert.Contains($"na kartě je jiný obsah než při záloze (xxh64 {changed}", repair.Status().Error);
        }

        for (int d = 0; d < paths.Count; d++) Assert.Empty(RepairFixtures.AssertUntouchedExcept(before[d], paths[d]));
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.Equal((false, 1, 1), (entry.Success, entry.FailedFiles!.Value, entry.Generations.Count));
        Assert.Equal(entryBefore.Finished, entry.Finished);
        Assert.True(Verification.BackupVerifier.Verify(paths[0], chunkSize: 4096).Success);
    }

    // Ruling I2: "kartu lze vysunout" is judged on the whole backup. A backup copied without verification, one file
    // failed on d1: the repair copies and verifies that file — but the other one was never read back, so the job is
    // not safe to eject, says so, and no "lze vysunout" notification goes out.
    [Fact]
    public void UnverifiedBackup_Repaired_NotSafeToEject_WarningShown()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = UnverifiedJobWithAWriteError(dir, history);
        Assert.False(failed.Report!.Verified);

        var repair = Repair(failed);
        repair.Run();

        var s = repair.Status();
        Assert.Equal((JobPhase.Done, (string?)null), (s.Phase, s.Error));
        Assert.True(repair.Report!.SafeToEject);   // the repaired file alone would say so …
        Assert.False(s.SafeToEject);                // … the whole backup does not
        Assert.Equal(Msg.UnverifiedEarlier(1), s.Warning);
        Assert.Null(JobOutcomes.CopyDone("A001", s with { Phase = JobPhase.Analyzing }, proxies: false));
        Assert.Equal(OutcomeKind.Attention, JobOutcomes.Final("A001", s, repair.Report)!.Kind);
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.True(entry.Success);
        Assert.False(entry.Verified);
    }

    // Minor M12: a cancelled repair keeps its warnings; its card folders (full paths) stay known.
    [Fact]
    public void CancelledRepair_KeepsWarningAndPaths()
    {
        using var dir = new TempDir();
        var failed = UnverifiedJobWithAWriteError(dir, dir.Sub("history"));
        var options = AppendFixtures.Options();
        var repair = Repair(failed, options: options);
        options.BeforeFileCopy = _ => repair.Cancel();

        repair.Run();

        var s = repair.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        Assert.False(s.SafeToEject);
        Assert.Equal(Msg.UnverifiedEarlier(1), s.Warning);
        Assert.Equal(failed.Plan!.CardPaths, repair.Config.RepairInto);
        Assert.Equal(failed.Plan.CardPaths, repair.Plan!.CardPaths);
    }

    /// A full job without verification whose first file's first block fails to write on d1.
    static OffloadJob UnverifiedJobWithAWriteError(TempDir dir, string history)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 60_000, 2);
        var options = AppendFixtures.Options();
        options.Verify = false;
        int thrown = 0;
        options.BeforeDestWrite = d => { if (d == 0 && Interlocked.Exchange(ref thrown, 1) == 0) throw new IOException("Zařízení není připojeno."); };
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-07", Verify = false, AnalyzeClips = false, HistoryFolder = history,
            Outputs = new OutputSettings { DayReport = false }, Notify = new Notify.NotifySettings { Windows = false },
        }, options) { Identify = _ => null };
        job.Run();
        Assert.Equal(["CLIP/A001C001.mov"], job.Report!.FailedFiles.Select(f => f.Path));
        return job;
    }

    [Fact]
    public void Config_TheJobsOwnCopy_AlwaysVerified_NoStage2()
    {
        var original = new JobConfig(@"E:\", [@"D:\A", @"F:\B"])
        {
            Verify = false, AnalyzeClips = true, Proxy = new ProxySettings { Enabled = true }, Project = "Film X",
            Outputs = new OutputSettings { ClipListAle = true }, Notify = new Notify.NotifySettings { Windows = false },
            Tools = new ExternalTools { Ffmpeg = @"C:\ff\ffmpeg.exe" }, ChunkMB = 32, Buffers = 8,
        };
        var plan = new OffloadPlan
        {
            Source = @"E:\", Date = "2026-10-07", CardName = "A001", DestRoots = [@"D:\A", @"F:\B"], Project = "Film X",
            Scan = new SourceScan { Root = @"E:\", Files = [], Dirs = [], Skipped = [] },
        };

        var c = JobConfigs.Repair(original, plan, cardIdentity: "VOL-1234");

        Assert.Equal(@"E:\", c.Source);
        Assert.Equal([@"D:\A\2026-10-07\A001", @"F:\B\2026-10-07\A001"], c.RepairInto);
        Assert.Equal("VOL-1234", c.RepairCardIdentity);
        Assert.True(c.Verify);
        Assert.False(c.AnalyzeClips);
        Assert.False(c.Proxy.Enabled);
        Assert.True(c.AllowSameDevice);
        Assert.Equal((32, 8), (c.ChunkMB, c.Buffers));
        Assert.Equal("Film X", c.Project);
        Assert.True(c.Outputs.ClipListAle);
        Assert.NotSame(original.Outputs, c.Outputs);
        Assert.False(c.Notify.Windows);
        Assert.NotSame(original.Notify, c.Notify);
        Assert.Equal(@"C:\ff\ffmpeg.exe", c.Tools.Ffmpeg);
    }

    [Theory]
    [InlineData(JobPhase.Done, true, true, false, true)]
    [InlineData(JobPhase.Failed, true, true, false, true)]       // stage 2 failed after a copy with a failed file
    [InlineData(JobPhase.Cancelled, true, true, false, false)]   // a cancelled copy: a new full copy, not a repair
    [InlineData(JobPhase.Copying, true, true, false, false)]
    [InlineData(JobPhase.Done, false, true, false, false)]       // nothing failed
    [InlineData(JobPhase.Done, true, false, false, false)]       // the card is not in its drive
    [InlineData(JobPhase.Done, true, true, true, false)]         // a repair is already queued or running
    public void Offer_Table(JobPhase phase, bool withFailure, bool cardPresent, bool pending, bool offered)
    {
        var report = Reports.Sample(@"D:\dst\2026-10-07\A001", destError: withFailure ? "HASH MISMATCH: dest 1 != source 2" : null);
        Assert.Equal(offered, RepairRules.Offer(phase, report, cardPresent, pending));
        Assert.False(RepairRules.Offer(JobPhase.Done, null, true, false));
    }

    [Fact]
    public void Title_AndSameBackup()
    {
        Assert.Equal("A001 · oprava", RepairRules.Title("A001"));
        Assert.True(RepairRules.SameBackup([@"D:\A\2026-10-07\A001\"], [@"F:\B\2026-10-07\A001", @"d:\a\2026-10-07\a001"]));
        Assert.False(RepairRules.SameBackup([@"D:\A\2026-10-07\A001"], [@"D:\A\2026-10-07\A001_2"]));
    }
}
