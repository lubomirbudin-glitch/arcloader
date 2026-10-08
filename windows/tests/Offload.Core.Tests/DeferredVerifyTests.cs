using System.Text.Json;
using Offload.Core.Append;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Plan 10: a copy whose verification runs with the proxies leaves the read-back out, writes its reports with
/// verifyPending and no ASC MHL generation (only verified files go into the MHL, spec 9); OffloadReport.Commit writes the
/// generation once verified. The classic report gets its method, the destination's serial (R12) and the time to "Lze
/// formátovat" (spec 9).
public class DeferredVerifyTests
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

    static JsonElement Json(string cardPath, string name = "offload_report.json") =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(cardPath, "_REPORT", name))).RootElement.Clone();

    [Fact]
    public void Deferred_ReportsPending_NoMhlNoHashList_NeitherGreenNorRed()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");

        var report = engine.Run(plan);

        Assert.True(report.VerifyPending);
        Assert.False(report.Verified);
        Assert.True(report.Success);   // written counts as good while not verified — never red
        Assert.False(report.SafeToFormat);
        Assert.All(report.Files, f => Assert.Equal(["written"], f.Destinations));
        var p = plan.CardPaths[0];
        Assert.True(Json(p).GetProperty("verifyPending").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(p, "ascmhl")) && Directory.GetFiles(Path.Combine(p, "ascmhl")).Length > 0);
        Assert.False(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
        Assert.All(engine.Snapshot().VerifiedBytes, v => Assert.Equal(0, v));   // nothing read back
        var record = CardRecord.Load(p);
        Assert.False(record.Verified);
        Assert.Empty(record.FailedFiles);
        Assert.Equal(new BackupState(0, 2), BackupState.Of([p]));
    }

    [Fact]
    public void Commit_WritesTheGeneration_ReplacesTheReports()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: true));
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");
        var report = engine.Run(plan);
        foreach (var f in report.Files) f.Destinations = ["verified"];
        report.Verified = true;
        report.VerifyPending = null;

        report.Commit(plan.CardPaths[0]);

        var p = plan.CardPaths[0];
        Assert.True(Json(p).GetProperty("verified").GetBoolean());
        Assert.False(Json(p).TryGetProperty("verifyPending", out _));
        Assert.Single(Directory.GetFiles(Path.Combine(p, "ascmhl"), "0001_*.mhl"));
        Assert.True(File.Exists(Path.Combine(p, "_REPORT", "A001.xxh64")));
        Assert.True(BackupVerifier.Verify(p, chunkSize: 4096).Success);
        Assert.True(CardRecord.Load(p).Verified);
    }

    // An append's reports are created new at the end of its copy (never over an earlier generation's, ruling I1) and
    // replaced by its own commit — the MHL generation file only then.
    [Fact]
    public void Commit_Append_Generation2()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var full = new OffloadEngine(Options(defer: false));
        var paths = full.Run(full.Prepare(card, [dir.Sub("d1")], null, "2026-10-08")).Destinations.Select(d => d.CardPath).ToList();
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), Block, 3);
        var engine = new OffloadEngine(Options(defer: true));
        var plan = engine.PrepareAppend(card, paths);

        var report = engine.Run(plan);

        Assert.Equal(2, plan.Generation);
        Assert.True(Json(paths[0], "offload_report_0002.json").GetProperty("verifyPending").GetBoolean());
        Assert.Empty(Directory.GetFiles(Path.Combine(paths[0], "ascmhl"), "0002_*.mhl"));
        foreach (var f in report.Files) f.Destinations = ["verified"];
        report.Verified = true;
        report.VerifyPending = null;
        report.Commit(paths[0]);
        Assert.Single(Directory.GetFiles(Path.Combine(paths[0], "ascmhl"), "0002_*.mhl"));
        Assert.True(BackupVerifier.Verify(paths[0], chunkSize: 4096).Success);
    }

    // Cancelled before the read-back: nothing to defer — the generation is written now, unverified (as before plan 10).
    [Fact]
    public void Cancelled_NotDeferred_WrittenAsBefore()
    {
        using var dir = new TempDir();
        var options = Options(defer: true);
        var engine = new OffloadEngine(options);
        options.BeforeVerify = engine.Cancel;
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");

        var report = engine.Run(plan);

        Assert.Null(report.VerifyPending);
        Assert.False(report.Verified);
        Assert.Single(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "ascmhl"), "*.mhl"));
    }

    [Fact]
    public void Classic_ReportFields_MethodSerialTimeAndMhlComment()
    {
        using var dir = new TempDir();
        var engine = new OffloadEngine(Options(defer: false));
        var plan = engine.Prepare(Card(dir), [dir.Sub("d1")], null, "2026-10-08");
        var report = engine.Run(plan);

        var dest = Json(plan.CardPaths[0]).GetProperty("destinations")[0];
        Assert.Equal("read-back", dest.GetProperty("verifyMethod").GetString());
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}$", dest.GetProperty("volumeSerial").GetString());
        Assert.True(report.SafeToFormat);
        Assert.True(report.SafeToFormatSeconds >= 0);
        var mhl = File.ReadAllText(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "ascmhl"), "*.mhl").Single());
        Assert.Contains("· ověření: read-back</comment>", mhl);
        Assert.Contains("<process>transfer</process>", mhl);
    }
}
