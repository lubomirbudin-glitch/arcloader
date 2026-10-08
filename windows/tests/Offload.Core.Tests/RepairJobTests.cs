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
        Assert.Equal(new BackupState(0, 0), repair.Backup);   // what the card's words are read from (no disk in the UI)
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
        Assert.Contains("právě pracuje jiná úloha", repair.Status().Error);
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

    // Ruling I2 with fix round 2, item B: "kartu lze vysunout" is judged on the whole backup. A backup copied without
    // verification, one file failed on d1: the repair copies that file there and reads back every copy that was never
    // read back (no card read for those) — only then is the card safe to eject, and history and the day report agree.
    [Fact]
    public void UnverifiedBackup_Repaired_EveryCopyReadBack_SafeToEject()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = UnverifiedJobWithAWriteError(dir, history);
        Assert.False(failed.Report!.Verified);

        var repair = Repair(failed);
        repair.Run();

        var s = repair.Status();
        Assert.Equal((JobPhase.Done, (string?)null, (string?)null), (s.Phase, s.Error, s.Warning));
        Assert.True(s.SafeToEject);
        var files = repair.Report!.Files.ToDictionary(f => f.Path);
        Assert.Equal([true, false], files["CLIP/A001C001.mov"].Rewritten);
        Assert.Equal([false, false], files["CLIP/A001C002.mov"].Rewritten);   // only read back
        Assert.All(files.Values, f => Assert.Equal(["verified", "verified"], f.Destinations));
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.True(entry.Success && entry.Verified);
        Assert.All(failed.Plan!.CardPaths, p => Assert.True(CardRecord.Load(p).Verified));
    }

    // Ruling I2 on its own: a repair of a backup whose copy the repair could not read back (its read-back cancelled)
    // is not safe to eject; the warning says why.
    [Fact]
    public void RepairCancelledDuringVerify_EjectYes_FormatNo_Warning()
    {
        using var dir = new TempDir();
        var failed = UnverifiedJobWithAWriteError(dir, dir.Sub("history"));
        var options = AppendFixtures.Options();
        var repair = Repair(failed, options: options);
        options.BeforeVerify = () => repair.Cancel();

        repair.Run();

        var s = repair.Status();
        Assert.Equal(JobPhase.Cancelled, s.Phase);
        // R5 (plan 10): the repaired copies are written — the card may be taken out — but not read back: never "Lze
        // formátovat".
        Assert.True(s.SafeToEject);
        Assert.False(s.SafeToFormat);
        Assert.Equal(Msg.UnverifiedEarlier(2), s.Warning);
    }

    // Fix round 1, item 1: a clip shot onto the card after the backup is in no report — the repair fixes what failed,
    // but the card is not safe to eject (formatting it would lose the clip): Czech warning, no "lze vysunout".
    [Fact]
    public void NewClipOnTheCard_RepairNotSafeToEject()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        AppendFixtures.NewClip(failed.Config.Source);

        var repair = Repair(failed);
        repair.Run();

        var s = repair.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(repair.Report!.SafeToFormat);
        Assert.False(s.SafeToEject);
        Assert.Equal("Na kartě je 1 soubor, který záloha nemá — kartu nevysouvejte, zálohu doplňte.", s.Warning);
        Assert.Equal("Na kartě jsou 3 soubory, které záloha nemá — kartu nevysouvejte, zálohu doplňte.", Msg.CardFilesNotInBackup(3));
        Assert.Equal("Na kartě je 5 souborů, které záloha nemá — kartu nevysouvejte, zálohu doplňte.", Msg.CardFilesNotInBackup(5));
        Assert.Equal(OutcomeKind.Attention, JobOutcomes.Final("A001", s, repair.Report)!.Kind);
    }

    // Fix round 1, item 2: cancel a repair, then repair again — the day report says "ověřeno" and history agrees
    // (CardRecord.Verified and BackupState share one definition: every file's newest record read back).
    [Fact]
    public void CancelThenRepair_DayReportVerified_HistoryAgrees()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = FailedJob(dir, history, dayReport: true);
        var options = AppendFixtures.Options();
        var cancelled = Repair(failed, options: options);
        options.BeforeFileCopy = _ => cancelled.Cancel();
        cancelled.Run();
        Assert.Equal(JobPhase.Cancelled, cancelled.Status().Phase);
        Assert.Equal(["verified", "pending"], Assert.Single(cancelled.Report!.Files).Destinations);   // the kept copy stays verified

        var repair = Repair(failed);
        repair.Run();

        Assert.True(repair.Status().SafeToEject);
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.True(entry.Success && entry.Verified);
        foreach (var root in failed.Plan!.DestRoots)
        {
            var day = DayReport.Load(Path.Combine(root, "2026-10-07"));
            Assert.True(Assert.Single(day.Cards).Record.Verified);
            Assert.Equal(1, day.VerifiedCards);
        }
        Assert.All(repair.DayReports, p =>
        {
            var html = File.ReadAllText(p);
            Assert.Contains("<td class=\"ok\">ověřeno</td>", html);
            Assert.DoesNotContain("NEOVĚŘENO", html);
        });
    }

    // Fix round 1, item 3 / round 2, item B: "Nic k opravě" — history learns the backup from its reports (here a repair
    // that ran without history fixed it); nothing is unverified then, so no warning (the planner reads back every copy
    // that was not, so "Nic k opravě" never hides one).
    [Fact]
    public void NothingToRepair_HistoryLearnsTheBackup()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = UnverifiedJobWithAWriteError(dir, history);
        var config = JobConfigs.Repair(failed);
        config.RecordHistory = false;
        new OffloadJob(config, AppendFixtures.Options()) { Identify = _ => null }.Run();
        var stale = Assert.Single(new HistoryStore(history).Load());
        Assert.False(stale.Success || stale.Verified);

        var again = Repair(failed);
        again.Run();

        var s = again.Status();
        Assert.Equal((JobPhase.Done, Msg.RepairNothing, (string?)null), (s.Phase, s.Note, s.Warning));
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.Equal((true, true, (int?)null), (entry.Success, entry.Verified, entry.FailedFiles));
    }

    // Batch 10–12 review: "Nic k opravě" on a card with a file shot after the backup — no report has it, so the card
    // is not "kartu lze vysunout": the warning says so (the planner counts it before refusing).
    [Fact]
    public void NothingToRepair_CardHasAFileNotInTheBackup_NotSafe()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        Repair(failed).Run();
        TestFs.WriteRandom(Path.Combine(failed.Config.Source, "CLIP", "A001C003.mov"), 10_000, 3);

        var again = Repair(failed);
        again.Run();

        var s = again.Status();
        Assert.Equal((JobPhase.Done, Msg.RepairNothing), (s.Phase, s.Note));
        Assert.Equal(Msg.CardFilesNotInBackup(1), s.Warning);
        Assert.False(s.SafeToEject);
        Assert.Equal(1, Assert.Throws<OffloadError>(() => RepairPlanner.Plan(failed.Config.Source, failed.Plan!.CardPaths)).NotInBackup);
    }

    // Batch 10–12 review: "Nic k opravě" but the backup's reports cannot be read afterwards — nothing was checked, so a
    // warning and never safe.
    [Fact]
    public void NothingToRepair_BackupStateUnreadable_NotSafe()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        Repair(failed).Run();

        var again = new OffloadJob(JobConfigs.Repair(failed), AppendFixtures.Options())
        {
            Identify = _ => null, ReadBackupState = _ => throw new IOException("Zařízení není připojeno."),
        };
        again.Run();

        var s = again.Status();
        Assert.Equal((JobPhase.Done, Msg.RepairNothing), (s.Phase, s.Note));
        Assert.Equal(Msg.BackupStateUnreadable("Zařízení není připojeno."), s.Warning);
        Assert.False(s.SafeToEject);
    }

    // Batch 10–12 review: the reports read after the planner record a failed file (another job meanwhile) — warned.
    [Fact]
    public void NothingToRepair_BackupStateHasFailedFiles_NotSafe()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        Repair(failed).Run();

        var again = new OffloadJob(JobConfigs.Repair(failed), AppendFixtures.Options())
        {
            Identify = _ => null, ReadBackupState = _ => new BackupState(2, 0),
        };
        again.Run();

        var s = again.Status();
        Assert.Equal(Msg.BackupStillFailed(2), s.Warning);
        Assert.False(s.SafeToEject);
        Assert.Equal(new BackupState(2, 0), again.Backup);
    }

    // Fix round 2, item B: a repair cancelled during its read-back leaves its new copy "written" in a report that was not
    // read back; the next repair reads it back (nothing copied, nothing moved) — never "Nic k opravě" for ever — and
    // the card, history and the day report all say verified.
    [Fact]
    public void RepairCancelledDuringVerify_NextRepairReadsBack_EverythingAgrees()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var failed = FailedJob(dir, history, dayReport: true);
        var options = AppendFixtures.Options();
        var cancelled = Repair(failed, options: options);
        options.BeforeVerify = () => cancelled.Cancel();
        cancelled.Run();
        Assert.Equal(JobPhase.Cancelled, cancelled.Status().Phase);
        Assert.Equal(["verified", "written"], Assert.Single(cancelled.Report!.Files).Destinations);
        var paths = failed.Plan!.CardPaths;
        var before = paths.Select(RepairFixtures.Snapshot).ToList();

        var repair = Repair(failed);
        repair.Run();

        Assert.True(repair.Status().SafeToEject);
        var f = Assert.Single(repair.Report!.Files);
        Assert.Equal(RepairFixtures.Bad, f.Path);
        Assert.Equal([false, false], f.Rewritten);   // nothing copied: only read back
        Assert.Equal(["verified", "verified"], f.Destinations);
        Assert.Null(repair.Report.SetAside);
        for (int d = 0; d < paths.Count; d++)
        {
            // The chain and the clip list (derived from the reports, plan 7) are rewritten; the copies only read.
            var added = RepairFixtures.AssertUntouchedExcept(before[d], paths[d], "ascmhl/ascmhl_chain.xml", "_REPORT/A001_clips.csv");
            Assert.All(added, a => Assert.Matches(@"^(_REPORT/.*_0003\.(json|txt|html|pdf)|ascmhl/0003_A001_.*\.mhl)$", a));
            Assert.True(CardRecord.Load(paths[d]).Verified);
        }
        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.True(entry.Success && entry.Verified);
        Assert.All(repair.DayReports, p => Assert.Contains("<td class=\"ok\">ověřeno</td>", File.ReadAllText(p)));
    }

    // Fix round 2, item C (from plan 6): an append cancelled during its read-back is not in history, but its copies are
    // on the disks unverified — after a successful append, history and the day report both say NEOVĚŘENO, until a
    // repair reads those copies back; then both say verified.
    [Fact]
    public void AppendCancelledDuringVerify_HistoryAndDayReportAgree()
    {
        using var dir = new TempDir();
        var history = dir.Sub("history");
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var full = Job(card, [dir.Sub("d1"), dir.Sub("d2")], history);
        full.Run();
        var paths = full.Plan!.CardPaths;
        AppendFixtures.NewClip(card, "A001C002.mov", seed: 2);
        var options = AppendFixtures.Options();
        var cancelled = Job(card, [], history, paths, options);
        options.BeforeVerify = () => cancelled.Cancel();
        cancelled.Run();
        Assert.Equal(JobPhase.Cancelled, cancelled.Status().Phase);
        AppendFixtures.NewClip(card, "A001C003.mov", seed: 3);

        var append = Job(card, [], history, paths);
        append.Run();

        Assert.True(append.Report!.SafeToFormat);
        Assert.True(append.Status().SafeToEject);     // R5: every file is on the disks
        Assert.False(append.Status().SafeToFormat);   // but the cancelled append's file was never read back
        Assert.False(Assert.Single(new HistoryStore(history).Load()).Verified);
        Assert.All(paths, p => Assert.False(CardRecord.Load(p).Verified));
        Assert.All(append.DayReports, p => Assert.Contains("NEOVĚŘENO", File.ReadAllText(p)));

        var repair = new OffloadJob(JobConfigs.Repair(append), AppendFixtures.Options()) { Identify = _ => null };
        repair.Run();

        Assert.True(repair.Status().SafeToEject);
        Assert.Equal(["CLIP/A001C002.mov"], repair.Report!.Files.Select(f => f.Path));
        Assert.True(Assert.Single(new HistoryStore(history).Load()).Verified);
        Assert.All(paths, p => Assert.True(CardRecord.Load(p).Verified));
        Assert.All(repair.DayReports, p => Assert.DoesNotContain("NEOVĚŘENO", File.ReadAllText(p)));
    }

    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, IReadOnlyList<string>? appendInto = null,
        OffloadOptions? options = null) =>
        new(new JobConfig(card, dests)
        {
            Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, AppendInto = appendInto,
            Outputs = new OutputSettings { DayReport = true }, Notify = new Notify.NotifySettings { Windows = false },
        }, options ?? AppendFixtures.Options()) { Identify = _ => null };

    // Fix round 1, item 4: Ověřit zálohu does not take AppendLock — it runs while a repair holds the card folders, and
    // only reads (so at worst it sees a copy that is being set aside as missing for a moment).
    [Fact]
    public void VerifyBackup_RunsWhileTheFoldersAreHeld_ReadsOnly()
    {
        using var dir = new TempDir();
        var failed = FailedJob(dir, dir.Sub("history"));
        var paths = failed.Plan!.CardPaths;
        var before = paths.Select(RepairFixtures.Snapshot).ToList();

        using (AppendLock.Acquire(paths))
        {
            Assert.True(Verification.BackupVerifier.Verify(paths[0], chunkSize: 4096).Success);
            Assert.False(Verification.BackupVerifier.Verify(paths[1], chunkSize: 4096).Success);
        }

        for (int d = 0; d < paths.Count; d++) Assert.Empty(RepairFixtures.AssertUntouchedExcept(before[d], paths[d]));
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
    public void Offer_HiddenOnceALaterRepairSucceeded()
    {
        var report = Reports.Sample(@"D:\dst\2026-10-07\A001", destError: "HASH MISMATCH: dest 1 != source 2");
        var ok = Reports.Sample(@"D:\dst\2026-10-07\A001");
        string[] cards = [@"D:\dst\2026-10-07\A001"];
        Assert.False(RepairRules.Offer(JobPhase.Done, report, true, false, repairedLater: true));
        Assert.True(RepairRules.RepairedLater(cards, [(cards, JobPhase.Done, ok)]));
        Assert.True(RepairRules.RepairedLater(cards, [(new[] { @"d:\DST\2026-10-07\a001\" }, JobPhase.Done, ok)]));
        Assert.False(RepairRules.RepairedLater(cards, [(cards, JobPhase.Done, report)]));          // that repair failed too
        Assert.False(RepairRules.RepairedLater(cards, [(cards, JobPhase.Copying, null)]));          // still running
        Assert.False(RepairRules.RepairedLater(cards, [(null, JobPhase.Done, ok)]));                // not a repair
        Assert.False(RepairRules.RepairedLater(cards, [(new[] { @"D:\dst\2026-10-07\B002" }, JobPhase.Done, ok)]));   // another backup
    }

    [Fact]
    public void Title_AndSameBackup()
    {
        Assert.Equal("A001 · oprava", RepairRules.Title("A001"));
        Assert.True(RepairRules.SameBackup([@"D:\A\2026-10-07\A001\"], [@"F:\B\2026-10-07\A001", @"d:\a\2026-10-07\a001"]));
        Assert.False(RepairRules.SameBackup([@"D:\A\2026-10-07\A001"], [@"D:\A\2026-10-07\A001_2"]));
    }
}
