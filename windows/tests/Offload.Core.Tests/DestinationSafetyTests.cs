namespace Offload.Core.Tests;

/// Plan 10 data safety around the write-through unbuffered writes and the manifest: nothing ever writes to the card;
/// unbuffered writes go only into Offload's own `.offload-part`; a file already at its place is never overwritten nor
/// truncated (the trim of the padded last block touches the part only); a volume that refuses FILE_FLAG_NO_BUFFERING is
/// written and read back through the file cache (ruling I3) — never a mass failure.
public class DestinationSafetyTests
{
    const int Block = 64 * 1024;
    static readonly DateTime When = new(2026, 10, 8, 7, 0, 0, DateTimeKind.Utc);

    static OffloadOptions Options() => new() { ChunkSize = Block, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true, CheckpointBlocks = 1 };

    static string Card(TempDir dir)
    {
        var card = dir.Sub("CARD");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A.mov"), 3 * Block + 5, 1);
        TestFs.WriteRandom(Path.Combine(card, "b.wav"), 777, 2);
        return card;
    }

    /// Records (and, with `refuse`, refuses) the unbuffered opens below `folder` for the duration of a test.
    sealed class Volume : IDisposable
    {
        readonly string folder;
        public NativeIO.TestVolume Behaviour { get; }

        public Volume(string folder, bool refuse = false)
        {
            this.folder = Path.GetFullPath(folder);
            Behaviour = new NativeIO.TestVolume { RefuseUnbuffered = refuse };
            Assert.True(NativeIO.TestVolumes.TryAdd(this.folder, Behaviour));
        }

        public void Dispose() => NativeIO.TestVolumes.TryRemove(folder, out _);
    }

    [Fact]
    public void Copy_NeverWritesToTheCard()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var before = RepairFixtures.Snapshot(card);
        var dirs = Directory.GetDirectories(card, "*", SearchOption.AllDirectories).Order().ToList();
        using var watch = new Volume(card);
        var engine = new OffloadEngine(Options());

        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1"), dir.Sub("d2")], null, "2026-10-08"));

        Assert.True(report.Success);
        Assert.Empty(RepairFixtures.AssertUntouchedExcept(before, card));   // nothing changed, nothing new (no _REPORT, no manifest)
        Assert.Equal(dirs, Directory.GetDirectories(card, "*", SearchOption.AllDirectories).Order());
        Assert.Empty(watch.Behaviour.Writes);
    }

    [Fact]
    public void UnbufferedWrites_OnlyIntoOwnPartFiles()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var dest = dir.Sub("d1");
        using var watch = new Volume(dest);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dest], null, "2026-10-08");

        engine.Run(plan);

        Assert.Equal(2, watch.Behaviour.Writes.Count);
        Assert.All(watch.Behaviour.Writes, p => Assert.Matches(@"^\..+\.offload-part$", Path.GetFileName(p)));
        Assert.Empty(Directory.GetFiles(dest, "*.offload-part", SearchOption.AllDirectories));
        Assert.Equal([true], engine.WrittenUnbuffered);
    }

    // The padded last block is trimmed with SetLength on the part's handle: a file already at the final place keeps its
    // bytes and its length, the copy fails there ("rename") and its part is gone.
    [Fact]
    public void FileAlreadyAtItsPlace_NeverOverwrittenNorTruncated()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var existing = TestFs.WriteRandom(Path.Combine(card, "x.mov"), 5 * Block, 9);
        var data = new byte[3 * Block + 123];
        new Random(3).NextBytes(data);
        var options = new OffloadOptions { ChunkSize = Block, BufferCount = 4, CheckpointBlocks = 1 };
        using var pool = new BufferPool(options.BufferCount, options.ChunkSize);
        using (var w = new DestWriter(0, card, 1, options))
        {
            var file = new SourceFile("x.mov", data.Length, When);
            w.Begin(file);
            for (int off = 0; off < data.Length; off += Block)
            {
                int n = Math.Min(Block, data.Length - off);
                var buf = pool.Take();
                data.AsSpan(off, n).CopyTo(buf.Span);
                buf.Span[n..].Clear();
                w.Write(new Chunk(buf, n, off, 1, pool));
            }
            w.Finish(0, file);
            w.Wait();
            Assert.Equal(FileState.Failed, w.Results[0].State);
            Assert.StartsWith("rename", w.Results[0].Error);
            Assert.True(w.Unbuffered);
        }
        Assert.Equal(existing, File.ReadAllBytes(Path.Combine(card, "x.mov")));
        Assert.Empty(Directory.GetFiles(card, "*.offload-part", SearchOption.AllDirectories));
    }

    // Ruling I3: a destination whose volume refuses FILE_FLAG_NO_BUFFERING is written through the file cache and read
    // back through FileReader's fallback, as in 0.5.x — every file verified, nothing failed, no checkpoints (its data
    // may still have been in RAM).
    [Fact]
    public void VolumeRefusingNoBuffering_ClassicCopyVerifiesEverything()
    {
        using var dir = new TempDir();
        var card = Card(dir);
        var dest = dir.Sub("d1");
        using var refusing = new Volume(dest, refuse: true);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dest], null, "2026-10-08");

        var report = engine.Run(plan);

        Assert.True(report.Success);
        Assert.True(report.Verified);
        Assert.All(report.Files, f => Assert.Equal(["verified"], f.Destinations));
        Assert.Equal(false, report.Destinations[0].Unbuffered);
        Assert.Equal([false], engine.WrittenUnbuffered);
        var records = VerifyInProxy.ManifestFile.Read(plan.CardPaths[0]);
        Assert.Empty(VerifyInProxy.ManifestFile.Parts(records, 1));
        Assert.Equal(2, VerifyInProxy.ManifestFile.Files(records, 1).Count);
    }

    [Fact]
    public void FileReader_OnARefusingVolume_FallsBackToBufferedReads()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "f.bin");
        var data = TestFs.WriteRandom(path, 5000, 4);
        using var refusing = new Volume(dir.Path, refuse: true);
        using var buf = new AlignedBuffer(8192);
        using var reader = new FileReader(path);

        Assert.Equal(5000, reader.Read(buf.Span, 0));
        Assert.Equal(data, buf.Span[..5000].ToArray());
        Assert.False(reader.Unbuffered);
    }
}
