using System.IO.Hashing;
using System.Security.Cryptography;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

public class BackupVerifierTests
{
    static OffloadOptions Options() => new() { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    /// A real offload (engine-written report, .xxh64 and ASC MHL) of three files; returns the card folder on the destination.
    static string Offloaded(TempDir dir)
    {
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 100_000, 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "b.mov"), 50_000, 2);
        TestFs.WriteRandom(Path.Combine(card, "c.xml"), 1_000, 3);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Run(plan);
        return plan.CardPaths[0];
    }

    [Fact]
    public void IntactCopy_AgainstAscMhl()
    {
        using var dir = new TempDir();
        var r = BackupVerifier.Verify(Offloaded(dir), chunkSize: 4096);
        Assert.True(r.Success);
        Assert.Equal("ascmhl", r.Basis);
        Assert.Equal(1, r.Generations);
        Assert.Equal(["xxh64", "xxh128"], r.HashAlgorithms);   // plan 10 (R2): every record has xxh128 too — one read for both
        Assert.Equal((3, 3, 151_000L, 151_000L), (r.Ok, r.TotalFiles, r.TotalBytes, r.BytesRead));
        Assert.Empty(r.Extra);
        Assert.Empty(r.ChainProblems);
    }

    [Fact]
    public void ChangedByte_Missing_Extra()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        var b = Layout.Join(card, "CLIP/b.mov");
        var bytes = File.ReadAllBytes(b);
        bytes[10] ^= 1;
        File.WriteAllBytes(b, bytes);
        File.Delete(Layout.Join(card, "c.xml"));
        File.WriteAllText(Layout.Join(card, "CLIP/new.mov"), "x");
        File.WriteAllText(Layout.Join(card, "CLIP/.DS_Store"), "x");                 // ignored by the MHL
        File.WriteAllText(Path.Combine(card, "_REPORT", "notes.txt"), "x");          // ours

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.False(r.Success);
        Assert.Equal(1, r.Ok);
        Assert.Equal(["CLIP/b.mov"], r.Changed);
        Assert.Equal(["c.xml"], r.Missing);
        Assert.Equal(["CLIP/new.mov"], r.Extra);
    }

    [Fact]
    public void SizeDiffers_IsChanged_EvenWithoutReading()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        File.AppendAllText(Layout.Join(card, "c.xml"), "x");
        Assert.Equal(["c.xml"], BackupVerifier.Verify(card, chunkSize: 4096).Changed);
    }

    [Fact]
    public void FailedAtCopy_IsReportedNotHashed()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/a.mov"), 1000, 1);
        var g1 = MhlFixtures.Generation(card, 1,
        [
            ("CLIP/a.mov", 1000, [("xxh64", Format.Hex64(XxHash64.HashToUInt64(File.ReadAllBytes(Layout.Join(card, "CLIP/a.mov")))))], "original"),
            ("CLIP/b.mov", 2000, [("xxh64", "0000000000000000")], "failed"),
        ]);
        MhlFixtures.Chain(card, g1);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.False(r.Success);   // a file nobody could verify is never "all fine"
        Assert.Equal(["CLIP/b.mov"], r.FailedAtCopy);
        Assert.Equal("1 soubor selhal už při kopii.", Assert.Single(VerifyTexts.Warnings(r)));
    }

    // R9: a Silverstack/ShotPut style MHL with md5 + sha1 + xxh128, two generations.
    [Fact]
    public void ForeignMhl_EveryHashType_TwoGenerations()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var a = TestFs.WriteRandom(Layout.Join(card, "CLIP/a.mov"), 5000, 1);
        var b = TestFs.WriteRandom(Layout.Join(card, "CLIP/b.mov"), 7000, 2);
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 5000, [("md5", Convert.ToHexStringLower(MD5.HashData(a))), ("sha1", Convert.ToHexStringLower(SHA1.HashData(a)))], "original")]);
        var g2 = MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 7000, [("xxh128", HashAlgo.Of("xxh128", b)), ("c4", Mhl.C4(b))], "original")]);
        MhlFixtures.Chain(card, g1, g2);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.True(r.Success);
        Assert.Equal(2, r.Ok);
        Assert.Equal(2, r.Generations);
        Assert.Equal(["md5", "sha1", "xxh128", "c4"], r.HashAlgorithms);
        Assert.Equal("Ověřeno: všechny 2 soubory sedí (ASC MHL, 2 generace, MD5 + SHA-1 + xxHash128 + C4).", VerifyTexts.Summary(r).Text);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void HashListFallback_WithoutAscmhl(string newline)
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var lines = new List<string>();
        foreach (var (rel, seed) in new[] { ("CLIP/a.mov", 1), ("c.xml", 3) })
        {
            var data = TestFs.WriteRandom(Layout.Join(card, rel), 10_000 + seed, seed);
            lines.Add($"{Format.Hex64(XxHash64.HashToUInt64(data))}  {rel}");
        }
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001.xxh64"), string.Join(newline, lines) + newline);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.True(r.Success);
        Assert.Equal(("hashList", 2), (r.Basis, r.Ok));
        Assert.Equal("Ověřeno: všechny 2 soubory sedí (seznam součtů, xxHash64).", VerifyTexts.Summary(r).Text);
    }

    [Fact]
    public void NoRecord_IsPreflightError()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() => BackupVerifier.Verify(dir.Sub("card")));
        Assert.Equal(PreflightCode.NoHashRecord, e.Code);
    }

    [Fact]
    public void Cancelled_StopsAndSaysWhere()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        using var cts = new CancellationTokenSource();
        var r = BackupVerifier.Verify(card, p => { if (p.FileIndex == 1) cts.Cancel(); }, cts.Token, chunkSize: 4096);
        Assert.True(r.Cancelled);
        Assert.False(r.Success);
        Assert.Equal(1, r.Checked);
        Assert.Empty(r.Extra);   // not looked for after a cancel
        Assert.Equal(("Ověření zrušeno po 1 / 3 souborech", VerifyTone.Warning), VerifyTexts.Summary(r));
    }

    [Fact]
    public void ChainProblem_IsAWarning_FilesStillVerified()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        var mhl = Directory.GetFiles(Path.Combine(card, "ascmhl"), "*.mhl").Single();
        File.AppendAllText(mhl, "<!-- x -->");
        var r = BackupVerifier.Verify(card, chunkSize: 4096);
        Assert.True(r.Success);
        Assert.Contains("ASC MHL generace 0001 neodpovídá řetězu (soubor MHL byl změněn).", VerifyTexts.Warnings(r));
    }

    [Theory]
    [InlineData("CLIP/.DS_Store", true)]
    [InlineData("CLIP/x.tmp", true)]
    [InlineData("tmp/x.mov", false)]
    [InlineData("cache/a/b.mov", true)]
    [InlineData("CLIP/a.mov", false)]
    public void IgnorePatterns(string path, bool ignored) =>
        Assert.Equal(ignored, BackupVerifier.Ignored(path, [".DS_Store", "*.tmp", "cache/", "ascmhl/"]));

    [Fact]
    public void Targets_CardItself_OrEveryCardOfADay()
    {
        using var dir = new TempDir();
        var day = dir.Sub("2026-10-06");
        Directory.CreateDirectory(Path.Combine(day, "A001", "ascmhl"));
        Directory.CreateDirectory(Path.Combine(day, "A002", "_REPORT"));
        File.WriteAllText(Path.Combine(day, "A002", "_REPORT", "A002.xxh64"), "");
        Directory.CreateDirectory(Path.Combine(day, "_REPORT"));   // the day report (plan 7) — not a card
        File.WriteAllText(Path.Combine(day, "_REPORT", "2026-10-06_day_report.html"), "");
        Directory.CreateDirectory(Path.Combine(day, "notes"));
        Assert.Equal([Path.Combine(day, "A001"), Path.Combine(day, "A002")], VerifyTargets.Resolve(day));
        Assert.Equal([Path.Combine(day, "A001")], VerifyTargets.Resolve(Path.Combine(day, "A001")));
        Assert.Empty(VerifyTargets.Resolve(Path.Combine(day, "notes")));
    }

    // A fixture shaped like real ascmhl output: three generations, a chain, a file replaced in a later generation,
    // one changed, one missing, one extra, one ignored.
    [Fact]
    public void RealisticHistory_ThreeGenerations_ChangedMissingExtra()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        string X(string rel) => HashAlgo.Of("xxh64", File.ReadAllBytes(Layout.Join(card, rel)));
        TestFs.WriteRandom(Layout.Join(card, "CLIP/a.mov"), 3000, 1);
        TestFs.WriteRandom(Layout.Join(card, "CLIP/b.mov"), 4000, 2);
        TestFs.WriteRandom(Layout.Join(card, "CLIP/c.mov"), 5000, 3);
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 3000, [("xxh64", "0000000000000001")], "original"), ("CLIP/b.mov", 4000, [("xxh64", X("CLIP/b.mov"))], "original")]);
        var g2 = MhlFixtures.Generation(card, 2, [("CLIP/a.mov", 3000, [("xxh64", X("CLIP/a.mov"))], "original"), ("CLIP/c.mov", 5000, [("xxh64", X("CLIP/c.mov"))], "original"),
            ("CLIP/gone.mov", 10, [("xxh64", "0000000000000002")], "original")]);
        var g3 = MhlFixtures.Generation(card, 3, [("CLIP/b.mov", 4000, [("xxh64", "ffffffffffffffff")], "original")]);
        MhlFixtures.Chain(card, g1, g2, g3);
        File.WriteAllText(Layout.Join(card, "CLIP/extra.mov"), "x");

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.Equal(3, r.Generations);
        Assert.Equal(["CLIP/b.mov"], r.Changed);          // newest generation wins
        Assert.Equal(["CLIP/gone.mov"], r.Missing);
        Assert.Equal(["CLIP/extra.mov"], r.Extra);
        Assert.Equal(2, r.Ok);                              // a.mov (gen 2), c.mov
        Assert.Empty(r.ChainProblems);
        Assert.False(r.Success);
    }

    // R8: verifying changes nothing on the card (not ascmhl\, not any file).
    [Fact]
    public void Verify_WritesNothing()
    {
        using var dir = new TempDir();
        var card = Offloaded(dir);
        string Snapshot() => string.Join('|', Directory.GetFiles(card, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => $"{f}:{new FileInfo(f).Length}|{new FileInfo(f).LastWriteTimeUtc.Ticks}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}"));
        var before = Snapshot();
        BackupVerifier.Verify(card, chunkSize: 4096);
        Assert.Equal(before, Snapshot());
    }
}
