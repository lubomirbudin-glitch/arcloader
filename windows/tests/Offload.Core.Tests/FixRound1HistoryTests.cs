using Offload.Core.Append;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Plan 10 tasks 12–14, fix round 1: a job's history record is never lost to a busy history lock (one lock per history
/// folder, a long wait, then a background retry with a warning on the job card); a stale "nedokončeno" heals; a job
/// that failed before it wrote anything leaves no "nedokončeno" entry; a folder that alone has the verification results
/// is never "ověřeno" in its day report.
public class FixRound1HistoryTests
{
    static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    static JobConfig Config(string card, IReadOnlyList<string> dests, string history) => new(card, dests)
    {
        Date = "2026-10-08", AnalyzeClips = false, HistoryFolder = history, Outputs = new OutputSettings { DayReport = false },
    };

    /// Holds the history lock of `folder` on a thread of its own until disposed.
    sealed class HeldHistory : IDisposable
    {
        readonly ManualResetEventSlim release = new();
        readonly Thread holder;

        public HeldHistory(string folder)
        {
            using var held = new ManualResetEventSlim();
            holder = new Thread(() =>
            {
                using var m = new Mutex(false, HistoryStore.LockName(folder));
                m.WaitOne();
                held.Set();
                release.Wait();
                m.ReleaseMutex();
            });
            holder.Start();
            Assert.True(held.Wait(TimeSpan.FromSeconds(30)));
        }

        public void Dispose()
        {
            release.Set();
            holder.Join();
            release.Dispose();
        }
    }

    static IReadOnlyList<HistoryEntry> Peek(string folder) => new HistoryStore(folder) { LockTimeout = TimeSpan.FromMilliseconds(50) }.Load();

    // 2a: one lock per history folder — any spelling of the same folder shares it, another folder never waits for it.
    [Fact]
    public void HistoryLock_PerFolder()
    {
        using var dir = new TempDir();
        var a = dir.Sub("a");
        var b = dir.Sub("b");
        Assert.Equal(HistoryStore.LockName(a), HistoryStore.LockName(a.ToUpperInvariant() + "\\"));
        Assert.NotEqual(HistoryStore.LockName(a), HistoryStore.LockName(b));
        using (new HeldHistory(a))
        {
            Assert.True(new HistoryStore(b) { LockTimeout = TimeSpan.FromMilliseconds(200) }.Record(HistoryFixtures.Entry(HistoryFixtures.T0), null));
            Assert.Equal(HistoryWrite.Busy, new HistoryStore(a.ToUpperInvariant() + "\\") { LockTimeout = TimeSpan.FromMilliseconds(200) }
                .Write("x-1", _ => (HistoryFixtures.Entry(HistoryFixtures.T0), null)));
        }
    }

    // 2b: the history is busy longer than the job waits — the job ends, its card says so (Czech), and the records are
    // written in the background, in order, once the lock is free: never lost; the warning goes.
    [Fact]
    public void HistoryBusy_RecordWrittenLater_NotLost()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 100_000, 1);
        var history = dir.Sub("h");
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], history), Options())
        {
            HistoryLockWait = TimeSpan.FromMilliseconds(300), HistoryRetryDelay = TimeSpan.FromMilliseconds(200), HistoryRetries = 500,
        };
        using (new HeldHistory(history))
        {
            job.Run();
            Assert.Equal(JobPhase.Done, job.Status().Phase);
            Assert.Contains(Msg.HistoryDelayed, job.Status().Warning);
            Assert.False(job.HistorySettled);
            Assert.Empty(Peek(history));
        }
        Assert.True(SpinWait.SpinUntil(() => job.HistorySettled, TimeSpan.FromSeconds(60)));

        var entry = Assert.Single(new HistoryStore(history).Load());
        Assert.False(entry.Unfinished);   // the start record, then the copy's — in order
        Assert.True(entry.Success && entry.Verified);
        Assert.Null(job.Status().Warning);
        Assert.True(job.Status().SafeToFormat);
    }

    // 2b: busy for good — logged, and the card says the record is not in history (never silently dropped).
    [Fact]
    public void HistoryBusyForGood_SaysSo()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 10_000, 1);
        var history = dir.Sub("h");
        var log = new List<string>();
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], history), Options())
        {
            HistoryLockWait = TimeSpan.FromMilliseconds(100), HistoryRetryDelay = TimeSpan.FromMilliseconds(50), HistoryRetries = 2,
        };
        using (new HeldHistory(history))
        {
            job.Run(l => { lock (log) log.Add(l); });
            Assert.True(SpinWait.SpinUntil(() => job.HistorySettled, TimeSpan.FromSeconds(60)));
        }
        Assert.Contains(Msg.HistoryNotWritten, job.Status().Warning);
        Assert.DoesNotContain(Msg.HistoryDelayed, job.Status().Warning);
        lock (log) Assert.Contains(log, l => l.Contains("not written"));
    }

    // 2c: a stale "nedokončeno" (the backup was finished without history learning it) heals the next time history learns
    // the backup — here "Nic nového"; a backup with a pending folder is never "settled".
    [Fact]
    public void StaleUnfinished_Heals()
    {
        using var dir = new TempDir();
        var (card, paths) = AppendFixtures.CopiedCard(dir);
        var history = dir.Sub("h");
        var store = new HistoryStore(history);
        Assert.True(store.Record(new HistoryEntry
        {
            Id = "20261006T080000Z-0000000a", CardName = "A001", Date = "2026-10-06", Unfinished = true, Finished = HistoryFixtures.T0,
            Destinations = [.. paths.Select(p => new HistoryDestination { Root = Path.GetDirectoryName(Path.GetDirectoryName(p))!, CardPath = p })],
        }, null));
        Assert.True(OffloadJob.Settled(paths));
        var config = new JobConfig(card, []) { AppendInto = paths, AnalyzeClips = false, HistoryFolder = history, Outputs = new OutputSettings { DayReport = false } };

        new OffloadJob(config, AppendFixtures.Options()).Run();

        var entry = Assert.Single(store.Load());
        Assert.False(entry.Unfinished);
        Assert.True(entry.Verified);   // from BackupState, the shared definition

        var pendingCard = dir.Sub("B002");
        TestFs.WriteRandom(Path.Combine(pendingCard, "x.bin"), 1000, 4);
        var options = Options();
        options.DeferVerify = true;
        var engine = new OffloadEngine(options);
        var pending = engine.Run(engine.Prepare(pendingCard, [dir.Sub("d3")], null, "2026-10-08")).Destinations[0].CardPath;
        Assert.False(OffloadJob.Settled([pending]));
    }

    // M-e: a stage-1 failure before the copy wrote anything leaves no "nedokončeno" entry; one after it recorded a file
    // keeps it (that copy can be resumed).
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void FailureInStageOne_StartEntryOnlyWhenResumable(int failAt, bool kept)
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "b.bin"), 100_000, 2);
        var history = dir.Sub("h");
        var options = Options();
        options.FullSyncThreshold = 1;   // every file flushed on its own: its record is written at once
        options.BeforeFileCopy = i => { if (i == failAt) throw new IOException("card gone"); };
        var job = new OffloadJob(Config(card, [dir.Sub("d1")], history), options);

        job.Run();

        Assert.Equal(JobPhase.Failed, job.Status().Phase);
        var entries = new HistoryStore(history).Load();
        Assert.Equal(kept, entries.Count == 1);
        if (kept) Assert.True(entries[0].Unfinished);
    }

    // M-e: a verify-in-proxy copy whose new card folders are busy fails before it writes: no entry.
    [FfmpegFact]
    public void FullCopyRefusedForABusyFolder_NoEntry()
    {
        using var dir = new TempDir();
        var card = VerifyInProxyRulingsTests.Card(dir);
        var root = dir.Sub("d1");
        var config = VerifyInProxyRulingsTests.Config(card, [root], dir);
        var cardPath = Layout.CardPath(root, "2026-10-08", "A001");
        var job = new OffloadJob(config, VerifyInProxyRulingsTests.Options());
        using (AppendLock.Acquire([cardPath]))
            job.Run();

        Assert.Equal(JobPhase.Failed, job.Status().Phase);
        Assert.Equal(Msg.AppendBusy(cardPath), job.Status().Error);
        Assert.Empty(new HistoryStore(Path.Combine(dir.Path, "history")).Load());
    }

    // M-a: the app ended between the commits (the other destination's report still pending): this folder's day report is
    // not "ověřeno"; a destination that is simply not connected changes nothing.
    [Fact]
    public void OtherDestinationPending_DayReportNotGreen_UnpluggedIsNot()
    {
        using var dir = new TempDir();
        var (_, paths) = AppendFixtures.CopiedCard(dir);
        var (p1, p2) = (paths[0], paths[1]);
        Assert.True(CardRecord.Load(p1).Verified);
        var json = Path.Combine(p2, "_REPORT", "offload_report.json");
        var r = OffloadReport.FromJson(File.ReadAllText(json));
        r.VerifyPending = true;
        File.WriteAllText(json, r.ToJson());

        var record = CardRecord.Load(p1);
        Assert.True(record.Incomplete);
        Assert.False(record.Verified);
        var day = DayReport.Load(Path.GetDirectoryName(p1)!);
        Assert.Equal(0, day.VerifiedCards);
        Assert.Contains("NEDOKONČENO", DayReport.Html(day));
        Assert.DoesNotContain("VŠE OVĚŘENO", DayReport.Html(day));

        Directory.Move(Path.GetDirectoryName(Path.GetDirectoryName(p2))!, Path.GetDirectoryName(Path.GetDirectoryName(p2))! + "-unplugged");
        Assert.False(CardRecord.Load(p1).Incomplete);
        Assert.True(CardRecord.Load(p1).Verified);
    }

    // M-a: a commit that failed on the second destination: the first folder's report says verifyIncomplete, its day report
    // is "NEDOKONČENO", never "VŠE OVĚŘENO"; the resume clears it.
    [FfmpegFact]
    public void CommitFailedOnTheSecond_FirstDayReportNotGreen_ResumeClears()
    {
        using var dir = new TempDir();
        var card = VerifyInProxyRulingsTests.Card(dir);
        var d1 = dir.Sub("d1");
        var d2 = dir.Sub("d2");
        var config = VerifyInProxyRulingsTests.Config(card, [d1, d2], dir);
        config.Outputs = new OutputSettings { DayReport = true, ClipListCsv = false };
        var job = new OffloadJob(config, VerifyInProxyRulingsTests.Options())
        {
            BeforeCommit = path => { if (path.StartsWith(d2, StringComparison.OrdinalIgnoreCase)) throw new IOException("disk full"); },
        };
        job.Run();
        var p1 = job.Plan!.CardPaths[0];
        Assert.True(OffloadReport.FromJson(File.ReadAllText(Path.Combine(p1, "_REPORT", "offload_report.json"))).VerifyIncomplete);
        var day = DayReport.Load(Path.Combine(d1, "2026-10-08"));
        Assert.Equal(0, day.VerifiedCards);
        Assert.Contains("NEDOKONČENO", DayReport.Html(day));
        Assert.DoesNotContain("VŠE OVĚŘENO", DayReport.Html(day));
        Assert.Contains(DayReport.Problems(day), x => x.Text.StartsWith("výsledek ověření není zapsaný na všech cílech", StringComparison.Ordinal));

        var resume = new OffloadJob(new JobConfig(card, []) { AppendInto = job.Plan.CardPaths, AnalyzeClips = false, RecordHistory = false,
            Outputs = new OutputSettings { DayReport = false } }, ResumeTests.Options());
        resume.Run();

        Assert.True(resume.Status().SafeToFormat);
        Assert.Null(OffloadReport.FromJson(File.ReadAllText(Path.Combine(p1, "_REPORT", "offload_report.json"))).VerifyIncomplete);
        Assert.Equal(1, DayReport.Load(Path.Combine(d1, "2026-10-08")).VerifiedCards);
    }
}
