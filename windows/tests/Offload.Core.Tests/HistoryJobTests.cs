using Offload.Core.History;

namespace Offload.Core.Tests;

/// Spec 2.1: a finished stage 1 is recorded in history.json (also with failed files), a cancelled one never.
public class HistoryJobTests
{
    static OffloadOptions Options(Action<int>? beforeFile = null) => new()
    {
        ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, BeforeFileCopy = beforeFile,
    };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 50_000, 2);
        return card;
    }

    [Fact]
    public void Success_IsRecorded_WithFingerprintAndDestinations()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var history = dir.Sub("appdata");
        var config = new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")]) { Date = "2026-10-06", AnalyzeClips = false, HistoryFolder = history };
        var job = new OffloadJob(config, Options());

        job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        var store = new HistoryStore(history);
        var e = Assert.Single(store.Load());
        Assert.Null(e.Card);   // a folder, not a drive root
        Assert.Equal("A001", e.CardName);
        Assert.Equal("2026-10-06", e.Date);
        Assert.True(e.Success);
        Assert.True(e.Verified);
        Assert.Equal(2, e.Files);
        Assert.Equal(150_000, e.Bytes);
        Assert.Equal(CardFingerprint.Compute(Scanner.Scan(card)), (e.Fingerprint, e.Shape));
        Assert.Equal(job.Plan!.CardPaths, e.Destinations.Select(d => d.CardPath));
        Assert.Equal(1, Assert.Single(e.Generations).Seq);
        Assert.Equal(2, store.ReadFileList(e)!.Count);
    }

    [Fact]
    public void CardVolume_IsStored()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = history };
        var job = new OffloadJob(config, Options())
        {
            Identify = _ => @"\?\Volume{x}\|1A2B3C4D",
            VolumeOf = _ => new CardVolume(@"\?\Volume{x}\|1A2B3C4D", "1A2B3C4D", 256_060_514_304, "A001", "exFAT"),
        };

        job.Run();

        Assert.Equal(@"\?\Volume{x}\|1A2B3C4D", job.SourceIdentity);
        var card = Assert.Single(new HistoryStore(history).Load()).Card!;
        Assert.Equal(("1A2B3C4D", 256_060_514_304L, "exFAT"), (card.FsSerial, card.Capacity, card.Filesystem));
    }

    // Review focus 4: a cancelled job leaves no history entry.
    [Fact]
    public void Cancelled_IsNotRecorded()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        OffloadJob? job = null;
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = history };
        job = new OffloadJob(config, Options(i => { if (i == 1) job!.Cancel(); }));

        job.Run();

        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        Assert.Empty(new HistoryStore(history).Load());
    }

    [Fact]
    public void RecordHistoryOff_WritesNothing()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = history, RecordHistory = false };
        new OffloadJob(config, Options()).Run();
        Assert.False(File.Exists(Path.Combine(history, "history.json")));
    }

    [Fact]
    public void BrokenHistoryFolder_NeverFailsTheJob()
    {
        using var dir = new TempDir();
        var blocker = Path.Combine(dir.Path, "file-not-folder");
        File.WriteAllText(blocker, "x");
        var log = new List<string>();
        var config = new JobConfig(Card(dir), [dir.Sub("d1")]) { AnalyzeClips = false, HistoryFolder = blocker };
        var job = new OffloadJob(config, Options());
        job.Run(log.Add);
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Contains(log, l => l.StartsWith("history", StringComparison.Ordinal));
    }
}
