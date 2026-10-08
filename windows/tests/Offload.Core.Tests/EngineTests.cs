using System.Text;

namespace Offload.Core.Tests;

public class EngineTests
{
    const int Chunk = 64 * 1024;

    static OffloadOptions Options(bool forceBuffered = false) => new()
    {
        ChunkSize = Chunk, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, ForceBuffered = forceBuffered,
    };

    static readonly int[] Sizes = [0, 1, 4095, 4096, 4097, Chunk - 1, Chunk, Chunk + 1, 3 * Chunk + 123];

    /// Card with files of every boundary size; returns relative path → content.
    static Dictionary<string, byte[]> MakeCard(string card)
    {
        var files = new Dictionary<string, byte[]>();
        for (int i = 0; i < Sizes.Length; i++)
        {
            var rel = $"CLIP/F{i:00}.mov";
            files[rel] = TestFs.WriteRandom(Layout.Join(card, rel), Sizes[i], i);
            File.SetLastWriteTimeUtc(Layout.Join(card, rel), new DateTime(2024, 1, 1, 0, 0, i, DateTimeKind.Utc));
        }
        Directory.CreateDirectory(Path.Combine(card, "EMPTY"));
        return files;
    }

    static void AssertNoPartFiles(string root) =>
        Assert.Empty(Directory.GetFiles(root, "*.offload-part", SearchOption.AllDirectories));

    [Fact]
    public void CopiesEveryFileToTwoDestinations_Verified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var files = MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");

        var report = engine.Run(plan);

        Assert.True(report.SafeToFormat);
        Assert.Equal(Sizes.Length, report.TotalFiles);
        foreach (var cardPath in plan.CardPaths)
        {
            foreach (var (rel, data) in files)
            {
                var path = Layout.Join(cardPath, rel);
                Assert.Equal(data, File.ReadAllBytes(path));
                Assert.Equal(File.GetLastWriteTimeUtc(Layout.Join(card, rel)), File.GetLastWriteTimeUtc(path));
            }
            Assert.True(Directory.Exists(Path.Combine(cardPath, "EMPTY")));
            foreach (var name in new[] { "offload_report.json", "offload_report.txt", "A001_report.html", "A001.xxh64" })
                Assert.True(File.Exists(Path.Combine(cardPath, "_REPORT", name)), name);
            Assert.Single(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl"));
            AssertNoPartFiles(cardPath);
        }
        var snap = engine.Snapshot();
        Assert.Equal(CopyPhase.Done, snap.Phase);
        Assert.Equal(report.TotalBytes, snap.ReadBytes);
        Assert.All(snap.VerifiedBytes, v => Assert.Equal(report.TotalBytes, v));
    }

    // Spec 3.5 / review focus 4: with MD5 a second hasher shares every block — a pool of just two blocks still flows
    // (a wrong consumer count would hang or hand a block back early), and every file verifies through both hashes.
    [Fact]
    public async Task Both_TwoBlockPool_EveryFileVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var files = MakeCard(card);
        var options = Options();
        options.BufferCount = 2;
        options.Hashes = HashKind.Both;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        Assert.Equal(HashKind.Both, plan.Hashes);

        // A block that never comes back to the pool hangs the copy: TimeoutException instead of a hung test run.
        var report = await Task.Run(() => engine.Run(plan)).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.True(report.SafeToFormat);
        foreach (var cardPath in plan.CardPaths)
            foreach (var (rel, data) in files)
                Assert.Equal(data, File.ReadAllBytes(Layout.Join(cardPath, rel)));
        Assert.All(report.Files, f => Assert.Equal(f.Xxh64, Format.Hex64(System.IO.Hashing.XxHash64.HashToUInt64(files[f.Path]))));
        // One read pass for both hashes: the card is read once, every destination is read back once (not per algorithm).
        var snap = engine.Snapshot();
        Assert.Equal(report.TotalBytes, snap.ReadBytes);
        Assert.All(snap.VerifiedBytes, v => Assert.Equal(report.TotalBytes, v));
    }

    sealed class ThrowingHash : Verification.IFileHash
    {
        public void Append(ReadOnlySpan<byte> data) => throw new IOException("md5 broke");
        public string Finish() => throw new InvalidOperationException("never finished");
    }

    // Fix round 1: a source hash that throws still hands every block back to the pool, so a two-block pool does not
    // hang the copy; the file fails (its MD5 is missing, never a hash of part of it) — with verification off as well.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThrowingSourceHash_FailsTheFile_DoesNotHang(bool verify)
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 5 * Chunk + 7, 1);
        var b = TestFs.WriteRandom(Path.Combine(card, "CLIP", "b.mov"), 5 * Chunk + 3, 2);
        var options = Options();
        options.BufferCount = 2;
        options.Hashes = HashKind.Both;
        options.Verify = verify;
        int made = 0;   // the first file's MD5 throws, the second one's is real
        options.HashFactory = a => a == "md5" && Interlocked.Increment(ref made) == 1 ? new ThrowingHash() : Verification.HashAlgo.Create(a);
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");

        var report = await Task.Run(() => engine.Run(plan)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(report.Success);
        var fa = report.Files.Single(f => f.Path == "CLIP/a.mov");
        var fb = report.Files.Single(f => f.Path == "CLIP/b.mov");
        Assert.Equal("md5 hash failed: md5 broke", fa.SourceError);
        Assert.Null(fa.Md5);
        Assert.NotNull(fa.Xxh64);
        Assert.Equal(["CLIP/a.mov"], report.FailedFiles.Select(f => f.Path));
        Assert.Null(fb.SourceError);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(b)), fb.Md5);
        Assert.Equal(verify ? ["verified", "verified"] : ["written", "written"], fb.Destinations);
        if (verify) Assert.All(fa.Errors, e => Assert.StartsWith("MD5 MISMATCH: dest ", e));
        Assert.DoesNotContain("CLIP/a.mov", File.ReadAllText(Path.Combine(plan.CardPaths[0], "_REPORT", "A001.md5")));
        Assert.Equal(1, engine.Snapshot().Errors);
    }

    // Spec 3.5: a byte changed on a destination after the copy fails verification in both modes. xxh64 is compared first,
    // so both rows fail with HASH MISMATCH; the MD5 path is covered by DestWriterTests.Verify_Both_Md5Mismatch_Fails.
    [Theory]
    [InlineData(HashKind.Xxh64, "HASH MISMATCH")]
    [InlineData(HashKind.Both, "HASH MISMATCH")]
    public void TamperedDestination_FailsInBothModes(HashKind kinds, string error)
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 3 * Chunk + 7, 1);
        var options = Options();
        options.Hashes = kinds;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        options.BeforeVerify = () =>
        {
            var copy = Layout.Join(plan.CardPaths[1], "CLIP/a.mov");
            var bytes = File.ReadAllBytes(copy);
            bytes[Chunk + 3] ^= 0x01;
            File.WriteAllBytes(copy, bytes);
        };

        var report = engine.Run(plan);

        var f = Assert.Single(report.Files);
        Assert.Equal(["verified", "failed"], f.Destinations);
        Assert.StartsWith(error, f.Errors[1]);
        Assert.False(report.Success);
    }

    // Spec 3.5: xxh64 + MD5 — "md5" per file, `<card>.md5` (md5sum format), MHL <md5> beside <xxh64>, TXT and HTML say
    // so, and the verification of the backup reads both; xxh64 alone leaves no trace of MD5.
    [Fact]
    public void Both_ReportHashListMhlAndBackupVerification()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var files = MakeCard(card);
        var options = Options();
        options.Hashes = HashKind.Both;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");

        var report = engine.Run(plan);

        Assert.Equal(["xxh64", "md5"], report.HashAlgorithms);
        foreach (var f in report.Files)
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(files[f.Path])), f.Md5);
        var rep = Path.Combine(plan.CardPaths[0], "_REPORT");
        var md5List = File.ReadAllText(Path.Combine(rep, "A001.md5"));
        Assert.Equal(report.HashListOf("md5"), md5List);
        Assert.Contains($"{report.Files[0].Md5}  {report.Files[0].Path}\n", md5List);
        Assert.DoesNotContain("\r", md5List);
        Assert.NotEqual(0xEF, File.ReadAllBytes(Path.Combine(rep, "A001.md5"))[0]);   // no BOM
        Assert.Contains("Hash:        xxh64+md5", File.ReadAllText(Path.Combine(rep, "offload_report.txt")));
        var html = File.ReadAllText(Path.Combine(rep, "A001_report.html"));
        Assert.Contains("XXH64 + MD5", html);
        Assert.Contains("<th>xxh64</th><th>md5</th>", html);   // a column per algorithm (preflight M3)
        Assert.All(report.Files, f => Assert.Contains($"<td class=\"mono\">{f.Md5}</td>", html));
        var mhl = Verification.MhlReader.Read(plan.CardPaths[0])!;
        Assert.All(mhl.Files.Values, r => Assert.Equal(["xxh64", "xxh128", "md5"], r.Hashes.Select(h => h.Algorithm)));
        // <md5> carries the same action and hashdate as <xxh64> (spec 3.5, preflight M3).
        System.Xml.Linq.XNamespace ns = "urn:ASC:MHL:v2.0";
        var gen = System.Xml.Linq.XDocument.Load(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "ascmhl"), "0001_*.mhl").Single());
        var hashEls = gen.Descendants(ns + "hash").ToList();
        Assert.Equal(report.Files.Count, hashEls.Count);
        foreach (var hashEl in hashEls)
        {
            var xx = hashEl.Element(ns + "xxh64")!;
            var md5 = hashEl.Element(ns + "md5")!;
            Assert.Equal(((string?)xx.Attribute("action"), (string?)xx.Attribute("hashdate")), ((string?)md5.Attribute("action"), (string?)md5.Attribute("hashdate")));
            Assert.Equal("original", (string?)md5.Attribute("action"));
            Assert.Equal(32, md5.Value.Length);
        }
        var verify = Verification.BackupVerifier.Verify(plan.CardPaths[0], chunkSize: Chunk);
        Assert.True(verify.Success);
        Assert.Equal(["xxh64", "md5", "xxh128"], verify.HashAlgorithms);

        // xxh64 alone (the default): no md5 key, no .md5 list, no <md5> in the MHL.
        var plain = new OffloadEngine(Options());
        var plainPlan = plain.Prepare(card, [dir.Sub("d2")], null, "2026-10-04");
        var plainReport = plain.Run(plainPlan);
        Assert.Null(plainReport.HashAlgorithms);
        Assert.All(plainReport.Files, f => Assert.Null(f.Md5));
        Assert.False(File.Exists(Path.Combine(plainPlan.CardPaths[0], "_REPORT", "A001.md5")));
        Assert.DoesNotContain("<th>md5</th>", File.ReadAllText(Path.Combine(plainPlan.CardPaths[0], "_REPORT", "A001_report.html")));
        Assert.All(Verification.MhlReader.Read(plainPlan.CardPaths[0])!.Files.Values, r => Assert.Equal(["xxh64", "xxh128"], r.Hashes.Select(h => h.Algorithm)));
        Assert.DoesNotContain("md5", plainReport.ToJson());
    }

    [Fact]
    public void DefaultBlockSize_FileOneByteOverBlock()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var data = TestFs.WriteRandom(Path.Combine(card, "big.mov"), (16 << 20) + 1, 42);
        var engine = new OffloadEngine(new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        Assert.True(engine.Run(plan).SafeToFormat);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(plan.CardPaths[0], "big.mov")));
    }

    [Fact]
    public void ForceBuffered_IsVerifiedAndReported()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options(forceBuffered: true));
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.SafeToFormat);
        Assert.False(report.Destinations[0].Unbuffered);
        Assert.Contains("(buffered I/O)", report.Summary);
    }

    [Fact]
    public void NoVerify_ReportsWrittenNotSafeToEject()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var options = Options();
        options.Verify = false;
        var engine = new OffloadEngine(options);
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04"));
        Assert.True(report.Success);
        Assert.False(report.SafeToFormat);
        Assert.All(report.Files, f => Assert.Equal(["written"], f.Destinations));
    }

    [Fact]
    public void CancelledBeforeRun_CopiesNothingLeavesNoParts()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Cancel();
        var report = engine.Run(plan);
        Assert.False(report.SafeToFormat);
        Assert.All(report.Files, f => Assert.Equal("cancelled", f.SourceError));
        Assert.Empty(Directory.GetFiles(Path.Combine(plan.CardPaths[0], "CLIP")));
    }

    [Fact]
    public async Task CancelledDuringCopy_LeavesNoPartsAndIsNotSafe()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        for (int i = 0; i < 40; i++) TestFs.WriteRandom(Path.Combine(card, $"F{i:00}.bin"), 1 << 20, i);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        var run = Task.Run(() => engine.Run(plan));
        while (engine.Snapshot().ReadBytes == 0 && !run.IsCompleted) await Task.Delay(1);
        engine.Cancel();
        var report = await run;
        Assert.False(report.SafeToFormat);
        Assert.False(report.Verified);
        AssertNoPartFiles(dir.Path);
    }

    // Review focus 1
    [Fact]
    public void SourceShrinksAfterPrepare_FileFailsOthersVerified()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        File.WriteAllBytes(Path.Combine(card, "CLIP", "F08.mov"), new byte[10]);

        var report = engine.Run(plan);

        var bad = report.Files.Single(f => f.Path == "CLIP/F08.mov");
        Assert.StartsWith("source size changed during copy", bad.SourceError);
        Assert.Equal(["failed"], bad.Destinations);
        Assert.All(report.Files.Where(f => f.Path != "CLIP/F08.mov"), f => Assert.Equal(["verified"], f.Destinations));
        Assert.False(report.SafeToFormat);
        Assert.False(File.Exists(Path.Combine(plan.CardPaths[0], "CLIP", "F08.mov")));
        AssertNoPartFiles(dir.Path);
    }

    // Review focus 2
    [Fact]
    public void OneDestinationBlocked_OtherDestinationComplete()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-04");
        Directory.CreateDirectory(Layout.Join(plan.CardPaths[1], "CLIP/F03.mov"));

        var report = engine.Run(plan);

        Assert.False(report.SafeToFormat);
        Assert.Equal(0, report.Destinations[0].FilesFailed);
        Assert.Equal(1, report.Destinations[1].FilesFailed);
        var f = report.Files.Single(x => x.Path == "CLIP/F03.mov");
        Assert.Equal(["verified", "failed"], f.Destinations);
        Assert.StartsWith("rename", f.Errors[1]);
        Assert.Contains("dest 2: rename", report.Summary);
    }

    // Review focus 3
    [Fact]
    public void SameCardTwice_SecondGetsSuffixFirstUntouched()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MakeCard(card);
        var d1 = dir.Sub("d1");
        var first = new OffloadEngine(Options());
        var plan1 = first.Prepare(card, [d1], null, "2026-10-04");
        first.Run(plan1);
        var firstJson = File.ReadAllText(Path.Combine(plan1.CardPaths[0], "_REPORT", "offload_report.json"));

        var second = new OffloadEngine(Options());
        var plan2 = second.Prepare(card, [d1], null, "2026-10-04");
        Assert.Equal("A001_2", plan2.CardName);
        Assert.True(second.Run(plan2).SafeToFormat);
        Assert.Equal(firstJson, File.ReadAllText(Path.Combine(plan1.CardPaths[0], "_REPORT", "offload_report.json")));
    }

    // Review focus 4
    [Fact]
    public void CzechNamesAndAmpersand_RoundTrip()
    {
        using var dir = new TempDir();
        var card = dir.Sub("Natáčení den 1");
        var a = TestFs.WriteRandom(Path.Combine(card, "Záběr 01", "klip č.1.mov"), 5000, 1);
        TestFs.WriteRandom(Path.Combine(card, "A&B.txt"), 10, 2);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        Assert.Equal("Natáčení den 1", plan.CardName);

        var report = engine.Run(plan);

        Assert.True(report.SafeToFormat);
        var cardPath = plan.CardPaths[0];
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(cardPath, "Záběr 01", "klip č.1.mov")));
        var list = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "Natáčení den 1.xxh64"), Encoding.UTF8);
        Assert.Contains("  Záběr 01/klip č.1.mov\n", list);
        var mhl = File.ReadAllText(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl").Single(), Encoding.UTF8);
        Assert.Contains(">A&amp;B.txt</path>", mhl);
        Assert.Contains(">Záběr 01/klip č.1.mov</path>", mhl);
    }
}
