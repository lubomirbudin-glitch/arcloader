using System.IO.Hashing;
using System.Xml.Linq;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Plan 10, R2: every copy hashes XXH3-128 next to xxh64 — into the report JSON and the ASC MHL — and keeps the XXH3-64
/// of every block for the block manifest; xxh64 and its hash list stay for older Offload and the Mac.
public class Xxh128Tests
{
    const int Chunk = 64 * 1024;

    static OffloadOptions Options() => new() { ChunkSize = Chunk, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true };

    static string Hex128(byte[] data)
    {
        var h = new XxHash128();
        h.Append(data);
        return Convert.ToHexStringLower(h.GetCurrentHash());
    }

    [Fact]
    public void Copy_RecordsXxh128_InReportAndMhl_NextToXxh64()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var a = TestFs.WriteRandom(Path.Combine(card, "CLIP", "a.mov"), 3 * Chunk + 5, 1);
        var b = TestFs.WriteRandom(Path.Combine(card, "b.wav"), 0, 2);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");

        var report = engine.Run(plan);

        Assert.True(report.Success && report.Verified);
        var fa = report.Files.Single(f => f.Path == "CLIP/a.mov");
        var fb = report.Files.Single(f => f.Path == "b.wav");
        Assert.Equal(Hex128(a), fa.Xxh128);
        Assert.Equal(Hex128(b), fb.Xxh128);
        Assert.Equal(Format.Hex64(XxHash64.HashToUInt64(a)), fa.Xxh64);
        Assert.Contains("\"xxh128\"", File.ReadAllText(Path.Combine(plan.CardPaths[0], "_REPORT", "offload_report.json")));

        var mhl = MhlReader.Read(plan.CardPaths[0])!;
        var rec = mhl.Files["CLIP/a.mov"];
        Assert.Equal(["xxh64", "xxh128"], rec.Hashes.Select(h => h.Algorithm));
        Assert.Equal(Hex128(a), rec.Hashes.Single(h => h.Algorithm == "xxh128").Value);
        Assert.All(rec.Hashes, h => Assert.Equal("original", h.Action));
        // The hash list stays xxh64 only (older Offload, the Mac).
        Assert.DoesNotContain(Hex128(a), File.ReadAllText(Path.Combine(plan.CardPaths[0], "_REPORT", "A001.xxh64")));
        // "Ověřit zálohu" checks both hashes of the record.
        var verify = BackupVerifier.Verify(plan.CardPaths[0], chunkSize: 4096);
        Assert.True(verify.Success);
        Assert.Equal(["xxh64", "xxh128"], verify.HashAlgorithms);
    }

    [Fact]
    public void Mhl_Xxh128Element_IsInTheAscNamespace_AfterXxh64()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 1000, 3);
        var engine = new OffloadEngine(Options());
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08");
        engine.Run(plan);

        var file = Directory.GetFiles(Path.Combine(plan.CardPaths[0], "ascmhl"), "0001_*.mhl").Single();
        XNamespace ns = "urn:ASC:MHL:v2.0";
        var hash = XDocument.Load(file).Descendants(ns + "hash").Single();
        Assert.Equal(["path", "xxh64", "xxh128"], hash.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void SourceHasher_KeepsOneXxh3PerBlock_AndTheWholeFileXxh128()
    {
        var data = new byte[3 * Chunk + 7];
        new Random(4).NextBytes(data);
        using var pool = new BufferPool(4, Chunk);
        using var h = new SourceHasher(HashKind.Xxh64, 1, blockSize: Chunk);
        h.Begin();
        for (int off = 0; off < data.Length; off += Chunk)
        {
            int n = Math.Min(Chunk, data.Length - off);
            var buf = pool.Take();
            data.AsSpan(off, n).CopyTo(buf.Span);
            h.Update(new Chunk(buf, n, off, 1, pool));
        }
        h.Finish(0);
        h.Wait();

        var want = Enumerable.Range(0, 4).Select(i => XxHash3.HashToUInt64(data.AsSpan(i * Chunk, Math.Min(Chunk, data.Length - i * Chunk)))).ToArray();
        Assert.Equal(want, h.Blocks[0]);
        Assert.Equal(Hex128(data), h.Xxh128[0]);
        Assert.Equal(Format.Hex64(XxHash64.HashToUInt64(data)), h.Values[0]);
    }

    [Fact]
    public void SourceHasher_ABlockOutOfOrder_FailsTheFile_NeverAWrongList()
    {
        using var pool = new BufferPool(2, Chunk);
        using var h = new SourceHasher(HashKind.Xxh64, 1, blockSize: Chunk);
        h.Begin();
        var buf = pool.Take();
        h.Update(new Chunk(buf, Chunk, Chunk, 1, pool));   // block 1 before block 0
        h.Finish(0);
        h.Wait();

        Assert.Null(h.Blocks[0]);
        Assert.Null(h.Xxh128[0]);
        Assert.Null(h.Values[0]);
        Assert.Contains("out of order", h.Errors[0]);
    }
}
