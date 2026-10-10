using System.Text.Json;
using Offload.Core.Append;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Plan 10 data safety: no reader of plans 6–9 (CardRecord, BackupState, AppendPlanner, RepairPlanner, the day report)
/// ever takes a report that says verifyPending for verified; a commit that fails leaves the folder unverified on the disk
/// (ruling I1: the job recomputes "Lze formátovat" from BackupState); the classic copy is unchanged.
public class PendingReportReadersTests
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

    static string Day(string cardPath) => Path.GetDirectoryName(cardPath)!;

    static PreflightCode? Refusal(Action plan)
    {
        var e = Assert.Throws<OffloadError>(plan);
        return e.Code;
    }

    [Fact]
    public void PendingFullCopy_NoReaderSeesItVerified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var engine = new OffloadEngine(Options(defer: true));
        var p = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")).Destinations[0].CardPath;

        Assert.False(CardRecord.Load(p).Verified);
        Assert.Equal(new BackupState(0, 2), BackupState.Of([p]));
        var day = DayReport.Load(Day(p));
        Assert.Equal(0, day.VerifiedCards);
        Assert.Contains("VERIFYING", DayReport.Html(day));   // task 14: a verification with the proxies not ended, never green
        // No ASC MHL generation yet: an append never builds on it as verified — since task 13 (R6) it completes the
        // generation (every file verified again before the commit); a repair refuses it.
        Assert.Equal(PreflightCode.AppendNoMhl, Refusal(() => AppendPlanner.CheckBackup([p])));
        var resume = AppendPlanner.Plan(card, [p], CancellationToken.None, sourceFilesystem: "NTFS");
        Assert.Equal((true, 1), (resume.Resuming, resume.Generation));
        Assert.Equal(PreflightCode.RepairConflict, Refusal(() => RepairPlanner.Plan(card, [p])));
    }

    [Fact]
    public void PendingAppend_NoReaderSeesItVerified()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var full = new OffloadEngine(Options(defer: false));
        var p = full.Run(full.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")).Destinations[0].CardPath;
        Assert.True(CardRecord.Load(p).Verified);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), Block, 3);
        var engine = new OffloadEngine(Options(defer: true));

        var report = engine.Run(engine.PrepareAppend(card, [p]));

        Assert.True(report.VerifyPending);
        Assert.False(CardRecord.Load(p).Verified);
        Assert.Equal(new BackupState(0, 1), BackupState.Of([p]));   // the new file only; the earlier two stay verified
        Assert.Equal(0, DayReport.Load(Day(p)).VerifiedCards);
        // Report 0002 without its MHL generation: never planned on as verified — refused as before by the checks of an
        // ordinary append (plan 6 ruling I1); since task 13 (R6) an append completes generation 2, a repair refuses.
        Assert.Equal(PreflightCode.AppendDiverged, Refusal(() => AppendPlanner.CheckBackup([p])));
        var resume = AppendPlanner.Plan(card, [p], CancellationToken.None, sourceFilesystem: "NTFS");
        Assert.Equal((true, 2), (resume.Resuming, resume.Generation));
        Assert.Equal(PreflightCode.RepairConflict, Refusal(() => RepairPlanner.Plan(card, [p])));
    }

    // Belt: even a pending report whose states say "verified" (no stage writes that, but a reader must not depend on it)
    // is not verified — CardRecord.ReadBack, the one definition, checks verifyPending first.
    [Fact]
    public void PendingReport_WithVerifiedStates_StillNotVerified()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var p = engine.Run(engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08")).Destinations[0].CardPath;
        var json = Path.Combine(p, "_REPORT", "offload_report.json");
        var r = OffloadReport.FromJson(File.ReadAllText(json));
        foreach (var f in r.Files) f.Destinations = ["verified"];
        r.Verified = true;
        Assert.True(r.VerifyPending);
        File.WriteAllText(json, r.ToJson());

        Assert.False(CardRecord.Load(p).Verified);
        Assert.Equal(new BackupState(0, 2), BackupState.Of([p]));
        Assert.Equal(0, DayReport.Load(Day(p)).VerifiedCards);
    }

    // Ruling I1: a commit that cannot write the ASC MHL generation (the commit point) throws and leaves the folder's
    // reports saying verifyPending — BackupState, history's source, never turns green from it.
    [Fact]
    public void Commit_MhlNotWritable_ReportsStayPending()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var report = engine.Run(engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08"));
        var p = report.Destinations[0].CardPath;
        File.WriteAllText(Path.Combine(p, "ascmhl"), "not a folder");
        foreach (var f in report.Files) f.Destinations = ["verified"];
        report.Verified = true;
        report.VerifyPending = null;

        Assert.ThrowsAny<Exception>(() => report.Commit(p));

        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(p, "_REPORT", "offload_report.json"))).RootElement;
        Assert.True(json.GetProperty("verifyPending").GetBoolean());
        Assert.False(json.GetProperty("verified").GetBoolean());
        Assert.False(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
        Assert.Equal(new BackupState(0, 2), BackupState.Of([p]));
        Assert.False(CardRecord.Load(p).Verified);
    }

    // A report still saying verifyPending is never committed: no MHL generation for files nobody verified.
    [Fact]
    public void Commit_OfAPendingReport_Refused_NothingWritten()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var report = engine.Run(engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08"));
        var p = report.Destinations[0].CardPath;

        Assert.Throws<InvalidOperationException>(() => report.Commit(p));

        Assert.False(Directory.Exists(Path.Combine(p, "ascmhl")) && Directory.EnumerateFileSystemEntries(Path.Combine(p, "ascmhl")).Any());
        Assert.False(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
    }

    // Verify-in-proxy off (classic): no verifyPending key, the read-back done in the copy, the generation written at once.
    [Fact]
    public void Classic_NoPendingKey_GenerationAtOnce()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: false));
        var report = engine.Run(engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08"));
        var p = report.Destinations[0].CardPath;

        Assert.Null(report.VerifyPending);
        Assert.All(report.Files, f => Assert.Equal(["verified"], f.Destinations));
        Assert.False(JsonDocument.Parse(File.ReadAllText(Path.Combine(p, "_REPORT", "offload_report.json"))).RootElement.TryGetProperty("verifyPending", out _));
        Assert.Single(Directory.GetFiles(Path.Combine(p, "ascmhl"), "0001_*.mhl"));
        Assert.True(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
        Assert.True(CardRecord.Load(p).Verified);
        Assert.Equal(new BackupState(0, 0), BackupState.Of([p]));
        Assert.All(engine.Snapshot().VerifiedBytes, v => Assert.True(v > 0));
    }
}
