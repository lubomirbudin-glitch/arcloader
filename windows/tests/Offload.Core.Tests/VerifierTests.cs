using System.IO.Hashing;

namespace Offload.Core.Tests;

public class VerifierTests
{
    /// Card folder with three files and a hash list, as an offload leaves it.
    static string MakeOffload(TempDir dir, string newline = "\n")
    {
        var card = dir.Sub("2026-10-04", "A001");
        var lines = new List<string>();
        foreach (var (rel, seed) in new[] { ("CLIP/a.mov", 1), ("CLIP/b.mov", 2), ("c.xml", 3) })
        {
            var data = TestFs.WriteRandom(Layout.Join(card, rel), 10_000 + seed, seed);
            lines.Add($"{Format.Hex64(XxHash64.HashToUInt64(data))}  {rel}");
        }
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001.xxh64"), string.Join(newline, lines) + newline);
        File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.txt"), "x");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        File.WriteAllText(Path.Combine(card, "ascmhl", "x.mhl"), "x");
        return card;
    }

    [Fact]
    public void IntactCopy_IsOk()
    {
        using var dir = new TempDir();
        var r = Verifier.Verify(MakeOffload(dir), chunkSize: 4096);
        Assert.True(r.Success);
        Assert.Equal(3, r.Ok.Count);
        Assert.Empty(r.Extra);
        Assert.Equal(30_006, r.Bytes);
    }

    [Fact]
    public void WindowsLineEndings_AreAccepted()
    {
        using var dir = new TempDir();
        Assert.True(Verifier.Verify(MakeOffload(dir, "\r\n"), chunkSize: 4096).Success);
    }

    [Fact]
    public void FindsMismatchMissingAndExtra()
    {
        using var dir = new TempDir();
        var card = MakeOffload(dir);
        var b = Layout.Join(card, "CLIP/b.mov");
        var bytes = File.ReadAllBytes(b);
        bytes[0] ^= 1;
        File.WriteAllBytes(b, bytes);
        File.Delete(Layout.Join(card, "c.xml"));
        File.WriteAllText(Layout.Join(card, "CLIP/new.mov"), "x");

        var r = Verifier.Verify(card, chunkSize: 4096);

        Assert.False(r.Success);
        Assert.Equal(["CLIP/a.mov"], r.Ok);
        Assert.Equal(["CLIP/b.mov"], r.Mismatched);
        Assert.Equal(["c.xml"], r.Missing);
        Assert.Equal(["CLIP/new.mov"], r.Extra);
    }

    [Fact]
    public void NoHashList_IsPreflightError()
    {
        using var dir = new TempDir();
        var e = Assert.Throws<OffloadError>(() => Verifier.Verify(dir.Sub("card")));
        Assert.Equal(PreflightCode.NoHashRecord, e.Code);
    }

    [Fact]
    public void ReverifiesWhatTheEngineWrote()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "x.mov"), 100_000, 7);
        var engine = new OffloadEngine(new OffloadOptions { AllowSourceVolume = true });
        var plan = engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-04");
        engine.Run(plan);
        var r = Verifier.Verify(plan.CardPaths[0]);
        Assert.True(r.Success);
        Assert.Equal(["x.mov"], r.Ok);
        Assert.Empty(r.Extra);
    }
}
