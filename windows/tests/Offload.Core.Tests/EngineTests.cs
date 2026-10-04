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

        Assert.True(report.SafeToEject);
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

    [Fact]
    public void DefaultBlockSize_FileOneByteOverBlock()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var data = TestFs.WriteRandom(Path.Combine(card, "big.mov"), (16 << 20) + 1, 42);
        var engine = new OffloadEngine(new OffloadOptions { AllowSameDevice = true, AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        Assert.True(engine.Run(plan).SafeToEject);
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
        Assert.True(report.SafeToEject);
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
        Assert.False(report.SafeToEject);
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
        Assert.False(report.SafeToEject);
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
        Assert.False(report.SafeToEject);
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
        Assert.False(report.SafeToEject);
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

        Assert.False(report.SafeToEject);
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
        Assert.True(second.Run(plan2).SafeToEject);
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

        Assert.True(report.SafeToEject);
        var cardPath = plan.CardPaths[0];
        Assert.Equal(a, File.ReadAllBytes(Path.Combine(cardPath, "Záběr 01", "klip č.1.mov")));
        var list = File.ReadAllText(Path.Combine(cardPath, "_REPORT", "Natáčení den 1.xxh64"), Encoding.UTF8);
        Assert.Contains("  Záběr 01/klip č.1.mov\n", list);
        var mhl = File.ReadAllText(Directory.GetFiles(Path.Combine(cardPath, "ascmhl"), "*.mhl").Single(), Encoding.UTF8);
        Assert.Contains(">A&amp;B.txt</path>", mhl);
        Assert.Contains(">Záběr 01/klip č.1.mov</path>", mhl);
    }
}
