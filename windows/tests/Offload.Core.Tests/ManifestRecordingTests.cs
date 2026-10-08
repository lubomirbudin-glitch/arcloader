using System.IO.Hashing;
using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 4.5, R4, R6: while copying, every destination's `_REPORT\offload-manifest.jsonl` gets a checkpoint of the file
/// being written (every CheckpointBlocks blocks) and the file's record once it is in its place — with the card's hashes
/// and block hashes; a file that failed there gets none. The copy measures each destination's write speed (spec 6).
public class ManifestRecordingTests
{
    const int Block = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, CheckpointBlocks = 1 };

    static ulong[] Blocks(byte[] data) => Enumerable.Range(0, BlockManifest.CountFor(data.Length, Block))
        .Select(b => XxHash3.HashToUInt64(data.AsSpan(b * Block, Math.Min(Block, data.Length - b * Block)))).ToArray();

    [Fact]
    public void Copy_WritesCheckpointsAndRecords()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var big = TestFs.WriteRandom(Path.Combine(card, "CLIP", "A.mov"), 3 * Block + 5, 1);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");

        engine.Run(plan);

        var records = ManifestFile.Read(plan.CardPaths[0]);
        Assert.Equal(Blocks(big), ManifestFile.Parts(records, 1)["CLIP/A.mov"].Blocks);
        var file = ManifestFile.Files(records, 1)["CLIP/A.mov"];
        Assert.Equal(Blocks(big), file.ToManifest()!.Blocks);
        Assert.Equal(Format.Hex64(XxHash64.HashToUInt64(big)), file.Xxh64);
        Assert.Equal(engine.Blocks[0], file.ToManifest()!.Blocks);
    }

    [Fact]
    public void EveryDestination_ItsOwnManifest_WithMd5WhenTheCopyHashesIt()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        var a = TestFs.WriteRandom(Path.Combine(card, "a.mov"), 2 * Block, 1);
        TestFs.WriteRandom(Path.Combine(card, "b.wav"), 100, 2);
        var options = Options();
        options.Hashes = HashKind.Both;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-08");

        engine.Run(plan);

        foreach (var p in plan.CardPaths)
        {
            var files = ManifestFile.Files(ManifestFile.Read(p), 1);
            Assert.Equal(["a.mov", "b.wav"], files.Keys.Order());
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(a)), files["a.mov"].Md5);
            Assert.Equal(32, files["a.mov"].Xxh128!.Length);
        }
        Assert.All(engine.WriteRates, r => Assert.True(r > 0));
        Assert.All(engine.WrittenUnbuffered, Assert.True);
    }

    // A disk that failed while writing a file has no record of it — a resume never trusts that copy.
    [Fact]
    public void FailedOnADestination_NoRecordThere()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 2 * Block, 1);
        var options = Options();
        options.BeforeDestWrite = d => { if (d == 1) throw new IOException("disk gone"); };
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-08");

        engine.Run(plan);

        Assert.Single(ManifestFile.Files(ManifestFile.Read(plan.CardPaths[0]), 1));
        Assert.Empty(ManifestFile.Files(ManifestFile.Read(plan.CardPaths[1]), 1));
    }

    // A buffered destination writes no checkpoints (its data may still be in RAM) — its records are written as usual.
    [Fact]
    public void BufferedDestination_NoCheckpoints_ButRecords()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 3 * Block, 1);
        var options = Options();
        options.ForceBuffered = true;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");

        engine.Run(plan);

        var records = ManifestFile.Read(plan.CardPaths[0]);
        Assert.Empty(ManifestFile.Parts(records, 1));
        Assert.Single(ManifestFile.Files(records, 1));
        Assert.Equal([false], engine.WrittenUnbuffered);
    }

    // Fix round 1, M-a: a file not flushed on its own (smaller than FullSyncThreshold on a written-through destination)
    // may still sit in the drive's cache — its record is written only once FlushDevice emptied it. A file flushed on its
    // own (a big one, or any on a destination written through the file cache) is recorded at once.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallFile_RecordedOnlyAfterTheDeviceFlush(bool buffered)
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        TestFs.WriteRandom(Path.Combine(card, "big.mov"), 3 * Block + 5, 1);
        TestFs.WriteRandom(Path.Combine(card, "small.wav"), 100, 2);
        var options = Options();
        options.FullSyncThreshold = 2 * Block;
        options.ForceBuffered = buffered;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");
        List<string>? beforeFlush = null;
        options.BeforeDeviceFlush = () => beforeFlush = ManifestFile.Files(ManifestFile.Read(plan.CardPaths[0]), 1).Keys.Order().ToList();

        engine.Run(plan);

        Assert.Equal(buffered ? ["big.mov", "small.wav"] : ["big.mov"], beforeFlush);
        Assert.Equal(["big.mov", "small.wav"], ManifestFile.Files(ManifestFile.Read(plan.CardPaths[0]), 1).Keys.Order());
    }

    // The device flush failed: the small file's data may never have reached the disk — it gets no record (a resume reads
    // it from the card again), the big one flushed on its own keeps its record.
    [Fact]
    public void DeviceFlushFailed_SmallFileNotRecorded()
    {
        using var dir = new TempDir();
        var card = dir.Sub("CARD");
        TestFs.WriteRandom(Path.Combine(card, "big.mov"), 3 * Block + 5, 1);
        TestFs.WriteRandom(Path.Combine(card, "small.wav"), 100, 2);
        var options = Options();
        options.FullSyncThreshold = 2 * Block;
        var engine = new OffloadEngine(options);
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");
        Directory.CreateDirectory(Path.Combine(plan.CardPaths[0], ".offload-flush"));   // the flush file cannot be made

        engine.Run(plan);

        Assert.Equal(["big.mov"], ManifestFile.Files(ManifestFile.Read(plan.CardPaths[0]), 1).Keys);
    }
}
