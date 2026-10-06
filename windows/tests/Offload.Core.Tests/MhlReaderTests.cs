using System.Text;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Hand-written ASC MHL generations — what ascmhl, Silverstack or ShotPut leave, and an append (generation 2).
public static class MhlFixtures
{
    /// One generation file `NNNN_<card>_….mhl` in `<card>/ascmhl`; `files`: (path, size, algorithm → value, action).
    public static string Generation(string cardPath, int seq, IEnumerable<(string Path, long Size, (string Algo, string Value)[] Hashes, string Action)> files,
        string[]? ignore = null)
    {
        var x = new StringBuilder();
        x.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">\n");
        x.Append("  <creatorinfo><creationdate>2026-10-06T10:00:00+02:00</creationdate><hostname>MAC</hostname><tool version=\"1.0\">ascmhl</tool></creatorinfo>\n");
        x.Append("  <processinfo><process>transfer</process><ignore>");
        foreach (var p in ignore ?? [".DS_Store", "ascmhl", "ascmhl/"]) x.Append($"<pattern>{p}</pattern>");
        x.Append("</ignore></processinfo>\n  <hashes>\n");
        foreach (var f in files)
        {
            x.Append($"    <hash><path size=\"{f.Size}\" lastmodificationdate=\"2026-10-06T09:00:00+02:00\">{f.Path}</path>");
            foreach (var (algo, value) in f.Hashes) x.Append($"<{algo} action=\"{f.Action}\" hashdate=\"2026-10-06T10:00:00+02:00\">{value}</{algo}>");
            x.Append("</hash>\n");
        }
        x.Append("    <directoryhash><path>CLIP</path><content><xxh64>0000000000000000</xxh64></content></directoryhash>\n");
        x.Append("  </hashes>\n</hashlist>\n");
        var dir = Path.Combine(cardPath, "ascmhl");
        Directory.CreateDirectory(dir);
        var name = $"{seq:0000}_{Path.GetFileName(cardPath)}_2026-10-06_080000Z.mhl";
        File.WriteAllText(Path.Combine(dir, name), x.ToString(), new UTF8Encoding(false));
        return name;
    }

    /// ascmhl_chain.xml listing `generations` (file names) with their real C4 ids.
    public static void Chain(string cardPath, params string[] generations)
    {
        var dir = Path.Combine(cardPath, "ascmhl");
        var x = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n");
        for (int i = 0; i < generations.Length; i++)
            x.Append($"  <hashlist sequencenr=\"{i + 1}\">\n    <path>{generations[i]}</path>\n    <c4>{Mhl.C4(File.ReadAllBytes(Path.Combine(dir, generations[i])))}</c4>\n  </hashlist>\n");
        x.Append("</ascmhldirectory>\n");
        File.WriteAllText(Path.Combine(dir, "ascmhl_chain.xml"), x.ToString(), new UTF8Encoding(false));
    }
}

public class MhlReaderTests
{
    [Fact]
    public void OurOwnGeneration()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Mhl.Write(Reports.Sample(card, destError: "boom"), card, new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Local), "pc");

        var h = MhlReader.Read(card)!;

        Assert.Empty(h.ChainProblems);
        var g = Assert.Single(h.Generations);
        Assert.Equal(1, g.Seq);
        Assert.Contains("_REPORT", h.Ignore);
        Assert.Contains(".DS_Store", h.Ignore);
        var a = h.Files["CLIP/A&B.mov"];
        Assert.Equal(1000, a.Size);
        Assert.Equal(new MhlHash("xxh64", "0123456789abcdef", "original"), Assert.Single(a.Hashes));
        Assert.False(a.Failed);
        Assert.True(h.Files["CLIP/c 02.mov"].Failed);
        Assert.Single(h.Chain);
    }

    [Fact]
    public void TwoGenerations_NewestWins_EveryHashType()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "AAAAAAAAAAAAAAAA")], "original")]);
        var g2 = MhlFixtures.Generation(card, 2,
        [
            ("CLIP/a.mov", 11, [("xxh64", "bbbbbbbbbbbbbbbb")], "original"),
            ("CLIP/b.mov", 20, [("md5", "900150983cd24fb0d6963f7d28e17f72"), ("sha1", "a9993e364706816aba3e25717850c26c9cd0d89d"),
                                ("xxh128", "06b05ab6733a618578af5f94892f3950"), ("xxh3", "78af5f94892f3950")], "original"),
        ]);
        MhlFixtures.Chain(card, g1, g2);

        var h = MhlReader.Read(card)!;

        Assert.Empty(h.ChainProblems);
        Assert.Equal([1, 2], h.Generations.Select(g => g.Seq));
        Assert.Equal((11L, 2), (h.Files["CLIP/a.mov"].Size!.Value, h.Files["CLIP/a.mov"].Generation));
        Assert.Equal(["md5", "sha1", "xxh128", "xxh3"], h.Files["CLIP/b.mov"].Hashes.Select(x => x.Algorithm));
        Assert.Equal(new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), h.Files["CLIP/b.mov"].Modified);
        Assert.Equal([1, 2], h.Chain.Select(c => c.Seq));
    }

    [Fact]
    public void ChangedGeneration_IsAChainProblem()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")]);
        var g2 = MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "original")]);
        MhlFixtures.Chain(card, g1, g2);
        File.AppendAllText(Path.Combine(card, "ascmhl", g2), "<!-- edited -->");

        var h = MhlReader.Read(card)!;

        Assert.Equal(["ASC MHL generace 0002 neodpovídá řetězu (soubor MHL byl změněn)."], h.ChainProblems);
        Assert.Equal(2, h.Generations.Count);   // still read: the files are verified anyway
    }

    [Fact]
    public void MissingChain_ReadsByNumber_WithAProblem()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "original")]);
        MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")]);

        var h = MhlReader.Read(card)!;

        Assert.Equal(["Chybí ascmhl_chain.xml — generace se čtou podle čísla v názvu souboru."], h.ChainProblems);
        Assert.Equal([1, 2], h.Generations.Select(g => g.Seq));
        Assert.Empty(h.Chain);
    }

    [Fact]
    public void GenerationMissing_AndUnlistedFile()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var g1 = MhlFixtures.Generation(card, 1, [("CLIP/a.mov", 10, [("xxh64", "aaaaaaaaaaaaaaaa")], "original")]);
        var g2 = MhlFixtures.Generation(card, 2, [("CLIP/b.mov", 10, [("xxh64", "bbbbbbbbbbbbbbbb")], "original")]);
        MhlFixtures.Chain(card, g1, g2);
        File.Delete(Path.Combine(card, "ascmhl", g2));
        MhlFixtures.Generation(card, 3, [("CLIP/c.mov", 10, [("xxh64", "cccccccccccccccc")], "original")]);

        var problems = MhlReader.Read(card)!.ChainProblems;

        Assert.Contains($"V řetězu ASC MHL je generace 0002, ale její soubor chybí: {g2}", problems);
        Assert.Contains(problems, p => p.StartsWith("Soubor ASC MHL není v řetězu: 0003_", StringComparison.Ordinal));
    }

    [Fact]
    public void BrokenGeneration_IsAProblem_NotACrash()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        File.WriteAllText(Path.Combine(card, "ascmhl", "0001_A001_x.mhl"), "<hashlist");
        var h = MhlReader.Read(card)!;
        Assert.Empty(h.Generations);
        Assert.Contains(h.ChainProblems, p => p.StartsWith("ASC MHL generaci 0001 nejde přečíst", StringComparison.Ordinal));
    }

    [Fact]
    public void NoAscmhl_IsNull()
    {
        using var dir = new TempDir();
        Assert.Null(MhlReader.Read(dir.Sub("A001")));
        Directory.CreateDirectory(Path.Combine(dir.Path, "A001", "ascmhl"));
        Assert.Null(MhlReader.Read(Path.Combine(dir.Path, "A001")));
    }
}

public class HashAlgoTests
{
    [Theory]
    [InlineData("xxh64", "", "ef46db3751d8e999")]
    [InlineData("xxh64", "abc", "44bc2cf5ad770999")]
    [InlineData("xxh3", "", "2d06800538d394c2")]
    [InlineData("xxh3", "abc", "78af5f94892f3950")]
    [InlineData("xxh128", "", "99aa06d3014798d86001c324468d497f")]
    [InlineData("xxh128", "abc", "06b05ab6733a618578af5f94892f3950")]
    [InlineData("md5", "", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("md5", "abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("sha1", "abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("c4", "", "c459dsjfscH38cYeXXYogktxf4Cd9ibshE3BHUo6a58hBXmRQdZrAkZzsWcbWtDg5oQstpDuni4Hirj75GEmTc1sFT")]
    [InlineData("c4", "abc", "c45S4rnaTNWonxss1u8LzsaJdEph1AJhWUF4sh2waXKMsutyfAxg4ybUeuXVWS9HdNcEypmeXn8FZGonD4w1rj9DZp")]
    public void Vectors(string algo, string input, string expected) => Assert.Equal(expected, HashAlgo.Of(algo, Encoding.ASCII.GetBytes(input)));

    [Fact]
    public void BlockByBlock_EqualsAtOnce()
    {
        var data = new byte[100_000];
        new Random(5).NextBytes(data);
        foreach (var algo in HashAlgo.Names)
        {
            var h = HashAlgo.Create(algo);
            for (int i = 0; i < data.Length; i += 4096) h.Append(data.AsSpan(i, Math.Min(4096, data.Length - i)));
            Assert.Equal(HashAlgo.Of(algo, data), h.Finish());
        }
    }

    [Fact]
    public void Same_HexAnyCase_C4Exact()
    {
        Assert.True(HashAlgo.Same("xxh64", "44BC2CF5AD770999", "44bc2cf5ad770999"));
        Assert.False(HashAlgo.Same("c4", "c45S4", "C45s4"));
        Assert.Equal("xxHash64", HashAlgo.Label("xxh64"));
        Assert.Equal("MD5", HashAlgo.Label("md5"));
    }
}
