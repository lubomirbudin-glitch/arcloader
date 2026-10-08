using Offload.Core.Append;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Plan 10 fix round 1, item 1: OffloadReport.Commit is idempotent and can be retried. A commit that failed after the
/// ASC MHL chain got its generation (the reports could not be replaced) is finished by a retry — the generation is not
/// written again, the chain keeps every generation (also one an append added meanwhile). Generation 1 is never written
/// into a folder that has a chain already.
public class CommitRetryTests
{
    const int Block = 64 * 1024;

    static OffloadOptions Options(bool defer) => new()
    {
        ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, DeferVerify = defer,
    };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 3 * Block + 7, 1);
        TestFs.WriteRandom(Path.Combine(card, "b.wav"), 1000, 2);
        return card;
    }

    static OffloadReport Verified(OffloadReport r)
    {
        foreach (var f in r.Files) f.Destinations = ["verified"];
        r.Verified = true;
        r.VerifyPending = null;
        return r;
    }

    static string Json(string cardPath, int generation) => Path.Combine(cardPath, "_REPORT", OffloadReport.JsonName(generation));

    /// Commit with the report JSON read-only: the MHL generation and the chain are written, the reports are not.
    static void CommitFailingAtTheReports(OffloadReport report, string cardPath, int generation)
    {
        File.SetAttributes(Json(cardPath, generation), FileAttributes.ReadOnly);
        Assert.ThrowsAny<Exception>(() => report.Commit(cardPath));
        File.SetAttributes(Json(cardPath, generation), FileAttributes.Normal);
        Assert.Contains(generation, MhlReader.Read(cardPath)!.Chain.Select(c => c.Seq));
        Assert.True(OffloadReport.FromJson(File.ReadAllText(Json(cardPath, generation))).VerifyPending);
    }

    static void AssertCommitted(string cardPath, int[] chain, int files)
    {
        var mhl = MhlReader.Read(cardPath)!;
        Assert.Equal(chain, mhl.Chain.Select(c => c.Seq));
        Assert.Empty(mhl.ChainProblems);
        foreach (var g in chain) Assert.Single(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), $"{g:0000}_*.mhl"));
        Assert.True(BackupVerifier.Verify(cardPath, chunkSize: 4096).Success);
        Assert.True(CardRecord.Load(cardPath).Verified);
        Assert.Equal(new BackupState(0, 0), BackupState.Of([cardPath]));
        Assert.Equal(files, File.ReadAllLines(Path.Combine(cardPath, "_REPORT", "A001.xxh64")).Length);
    }

    [Fact]
    public void Generation1_FailedAfterTheChain_RetryEndsVerified()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var report = Verified(engine.Run(engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08")));
        var p = report.Destinations[0].CardPath;

        CommitFailingAtTheReports(report, p, 1);
        Assert.Equal(new BackupState(0, 2), BackupState.Of([p]));   // still pending on the disk: never green
        report.Commit(p);
        report.Commit(p);   // and once more: nothing changes

        AssertCommitted(p, [1], 2);
    }

    [Fact]
    public void Generation2_FailedAfterTheChain_RetryEndsVerified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var full = new OffloadEngine(Options(defer: false));
        var p = full.Run(full.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")).Destinations[0].CardPath;
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), Block, 3);
        var engine = new OffloadEngine(Options(defer: true));
        var report = Verified(engine.Run(engine.PrepareAppend(card, [p])));

        CommitFailingAtTheReports(report, p, 2);
        report.Commit(p);

        AssertCommitted(p, [1, 2], 3);
    }

    // An append ran between the failed commit and its retry (generation 2 on top of the committed-but-pending 1): the
    // retry finishes generation 1 and the chain still has both, the hash list every file once.
    [Fact]
    public void AppendBetweenTheFailureAndTheRetry_ChainKeepsEveryGeneration()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var engine = new OffloadEngine(Options(defer: true));
        var report = Verified(engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")));
        var p = report.Destinations[0].CardPath;
        CommitFailingAtTheReports(report, p, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), Block, 3);
        var append = new OffloadEngine(Options(defer: false));
        Assert.True(append.Run(append.PrepareAppend(card, [p])).SafeToFormat);

        report.Commit(p);

        AssertCommitted(p, [1, 2], 3);
    }

    // The chain has generation N, but not this report's (other hashes): the retry refuses, nothing is changed.
    [Fact]
    public void ChainHasAnotherGenerationN_CommitRefused()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var report = Verified(engine.Run(engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08")));
        var p = report.Destinations[0].CardPath;
        CommitFailingAtTheReports(report, p, 1);
        var chain = File.ReadAllBytes(Path.Combine(p, "ascmhl", "ascmhl_chain.xml"));
        var other = OffloadReport.FromJson(report.ToJson().Replace(report.Files[0].Xxh64!, "0000000000000000"));

        Assert.Throws<IOException>(() => other.Commit(p));

        Assert.Equal(chain, File.ReadAllBytes(Path.Combine(p, "ascmhl", "ascmhl_chain.xml")));
        Assert.True(OffloadReport.FromJson(File.ReadAllText(Json(p, 1))).VerifyPending);
    }

    [Fact]
    public void Mhl_Generation1_RefusedWhenAChainExists()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: false));
        var report = engine.Run(engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08"));
        var p = report.Destinations[0].CardPath;
        var before = RepairFixtures.Snapshot(Path.Combine(p, "ascmhl"));

        Assert.Throws<IOException>(() => Mhl.Write(report, p));

        Assert.Empty(RepairFixtures.AssertUntouchedExcept(before, Path.Combine(p, "ascmhl")));
    }
}
