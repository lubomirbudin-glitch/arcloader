using System.Text;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// Generations shaped like the output of the real `ascmhl` tool 1.0.1 (backslash paths, roothash, directoryhash,
/// previousPath, no `_REPORT` in the ignore patterns) — copied from a real run, with the hash values recomputed.
public class RealAscmhlTests
{
    static string Bs(string s) => s.Replace('/', '\\');

    /// One generation file in the shape ascmhl writes (paths with backslashes).
    internal static string Gen(string card, int seq, params (string Path, string Xml)[] files)
    {
        var x = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">\n");
        x.Append("  <creatorinfo>\n    <creationdate>2026-10-06T21:52:47+02:00</creationdate>\n    <hostname>PC</hostname>\n    <tool version=\"1.0.1\">ascmhl</tool>\n  </creatorinfo>\n");
        x.Append("  <processinfo>\n    <process>in-place</process>\n    <roothash>\n      <content>\n        <xxh64 hashdate=\"2026-10-06T21:52:47.03+02:00\">0000000000000000</xxh64>\n      </content>\n    </roothash>\n");
        x.Append("    <ignore>\n      <pattern>.DS_Store</pattern>\n      <pattern>ascmhl</pattern>\n      <pattern>ascmhl/</pattern>\n    </ignore>\n  </processinfo>\n  <hashes>\n");
        foreach (var (path, xml) in files)
            x.Append($"    <hash>\n      <path size=\"{SizeOf(card, path)}\" lastmodificationdate=\"2026-10-06T21:52:03+02:00\">{Bs(path)}</path>\n{xml}    </hash>\n");
        x.Append("    <directoryhash>\n      <path lastmodificationdate=\"2026-10-06T21:52:03+02:00\">CLIP</path>\n      <content>\n        <xxh64 hashdate=\"2026-10-06T21:52:47.03+02:00\">0000000000000000</xxh64>\n      </content>\n    </directoryhash>\n  </hashes>\n</hashlist>\n");
        var dir = Path.Combine(card, "ascmhl");
        Directory.CreateDirectory(dir);
        var name = $"{seq:0000}_{Path.GetFileName(card)}_2026-10-06_19524{seq}Z.mhl";
        File.WriteAllText(Path.Combine(dir, name), x.ToString(), new UTF8Encoding(false));
        return name;
    }

    internal static string H(string algo, string action, string value, string extra = "") =>
        $"      <{algo} action=\"{action}\" hashdate=\"2026-10-06T21:52:47.026650+02:00\">{value}</{algo}>\n{extra}";

    internal static string X(string card, string rel) => HashAlgo.Of("xxh64", File.ReadAllBytes(Layout.Join(card, rel)));

    static long SizeOf(string card, string rel) => File.Exists(Layout.Join(card, rel)) ? new FileInfo(Layout.Join(card, rel)).Length : 0;

    [Fact]
    public void FailedNewest_OlderGoodRecord_FlippedByte_IsChanged()
    {
        using var dir = new TempDir();
        var card = dir.Sub("D001");
        var data = TestFs.WriteRandom(Layout.Join(card, "CLIP/x.mov"), 2000, 1);
        var good = X(card, "CLIP/x.mov");
        var g1 = Gen(card, 1, ("CLIP/x.mov", H("xxh64", "original", good)));
        var g2 = Gen(card, 2, ("CLIP/x.mov", H("xxh64", "failed", "df88698dec264e9a")));
        MhlFixtures.Chain(card, g1, g2);
        data[5] ^= 1;
        File.WriteAllBytes(Layout.Join(card, "CLIP/x.mov"), data);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.Equal(["CLIP/x.mov"], r.Changed);
        Assert.Empty(r.FailedAtCopy);
        Assert.False(r.Success);
        Assert.StartsWith("Nesedí", VerifyTexts.Summary(r).Text);
        Assert.EndsWith("NALEZENY PROBLÉMY: nesedí 1, chybí 0, nečitelné 0, selhalo při kopii 0, navíc 0.", VerifyTexts.CliSummary(r));
    }

    [Fact]
    public void FailedNewest_OlderGoodRecord_IntactFile_IsOk_WithANote()
    {
        using var dir = new TempDir();
        var card = dir.Sub("D001");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/x.mov"), 2000, 1);
        var g1 = Gen(card, 1, ("CLIP/x.mov", H("xxh64", "original", X(card, "CLIP/x.mov"))));
        var g2 = Gen(card, 2, ("CLIP/x.mov", H("xxh64", "failed", "df88698dec264e9a")));
        MhlFixtures.Chain(card, g1, g2);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.True(r.Success);
        Assert.Equal(1, r.Ok);
        Assert.Equal(["CLIP/x.mov"], r.FailedSuperseded);
        Assert.Single(VerifyTexts.Warnings(r), w => w.Contains("selhalo při kopii"));
    }

    [Fact]
    public void FailedOnly_IsNeverAllGood_AndSaysSo()
    {
        using var dir = new TempDir();
        var card = dir.Sub("D001");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/x.mov"), 2000, 1);
        var g1 = Gen(card, 1, ("CLIP/x.mov", H("xxh64", "failed", "df88698dec264e9a")));
        MhlFixtures.Chain(card, g1);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);

        Assert.False(r.Success);
        Assert.Equal(["CLIP/x.mov"], r.FailedAtCopy);
        Assert.DoesNotContain("v pořádku", VerifyTexts.CliSummary(r));
        Assert.Equal(VerifyTone.Error, VerifyTexts.Summary(r).Tone);
    }

    [Fact]
    public void Rename_PreviousPath_IsNotMissing()
    {
        using var dir = new TempDir();
        var card = dir.Sub("R001");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/y.mov"), 2000, 1);
        var g1 = Gen(card, 1, ("CLIP/x.mov", H("sha1", "original", "058ab945f7d95e192675c99c31d2de6cbf413c89")));
        var sha = HashAlgo.Of("sha1", File.ReadAllBytes(Layout.Join(card, "CLIP/y.mov")));
        var g2 = Gen(card, 2, ("CLIP/y.mov", H("sha1", "original", sha, "      <previousPath>CLIP\\x.mov</previousPath>\n")));
        MhlFixtures.Chain(card, g1, g2);

        var h = MhlReader.Read(card)!;
        Assert.Equal(["sha1"], h.Files["CLIP/y.mov"].Hashes.Select(x => x.Algorithm));
        Assert.Equal("CLIP/x.mov", h.Files["CLIP/y.mov"].PreviousPath);
        Assert.DoesNotContain("CLIP/x.mov", h.Files.Keys);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);
        Assert.True(r.Success);
        Assert.Empty(r.Missing);
        Assert.Equal(["sha1"], r.HashAlgorithms);
    }

    [Fact]
    public void CaseVariantPaths_AreOneRecord()
    {
        using var dir = new TempDir();
        var card = dir.Sub("B001");
        var g1 = Gen(card, 1, ("CLIP/Mixed Case.MOV", H("xxh64", "original", "0000000000000001")));
        var g2 = Gen(card, 2, ("clip/mixed case.mov", H("xxh64", "verified", "0000000000000002")));
        MhlFixtures.Chain(card, g1, g2);
        var h = MhlReader.Read(card)!;
        var rec = Assert.Single(h.Files.Values);
        Assert.Equal(2, rec.Generation);
    }

    [Theory]
    [InlineData("a/b/c.mov", "a/*/c.mov", true)]
    [InlineData("a/b/d/c.mov", "a/*/c.mov", false)]
    [InlineData("a/b/d/c.mov", "a/**/c.mov", true)]
    [InlineData("a/c.mov", "a/**/c.mov", true)]
    [InlineData("a/b/x.mov", "a/**", true)]
    [InlineData("CLIP/a.mov", "/a.mov", false)]
    [InlineData("a.mov", "/a.mov", true)]
    [InlineData("CLIP/a.mov", "a.mov", true)]
    [InlineData("CLIP/sub/a.mov", "CLIP/*.mov", false)]
    [InlineData("CLIP/a.mov", "CLIP/*.mov", true)]
    public void Ignored_GitWildmatch(string path, string pattern, bool ignored) =>
        Assert.Equal(ignored, BackupVerifier.Ignored(path, [pattern]));

    [Fact]
    public void FolderGone_RemainingFilesAreUnreadable_NotMissing()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/a.mov"), 20000, 1);
        TestFs.WriteRandom(Layout.Join(card, "CLIP/b.mov"), 2000, 2);
        TestFs.WriteRandom(Layout.Join(card, "CLIP/c.mov"), 2000, 3);
        var g1 = Gen(card, 1, ("CLIP/a.mov", H("xxh64", "original", X(card, "CLIP/a.mov"))),
            ("CLIP/b.mov", H("xxh64", "original", X(card, "CLIP/b.mov"))), ("CLIP/c.mov", H("xxh64", "original", X(card, "CLIP/c.mov"))));
        MhlFixtures.Chain(card, g1);
        bool gone = false;

        var r = BackupVerifier.Verify(card, p =>
        {
            if (p.FileIndex == 1 && !gone)   // the disk is pulled while the second file is about to be read
            {
                gone = true;
                Directory.Delete(card, true);
            }
        }, chunkSize: 4096);

        Assert.Empty(r.Missing);
        Assert.True(r.FolderGone);
        Assert.Contains(r.Unreadable, u => u.Error == "složka zálohy zmizela");
        Assert.False(r.Success);
    }

    [Fact]
    public void CliSummary_CountsOnlyFilesActuallyHashed()
    {
        var r = new VerifyResult { CardPath = "x", Basis = "ascmhl", Started = DateTime.UtcNow.AddSeconds(-1), Finished = DateTime.UtcNow, Checked = 10, Ok = 3 };
        r.Changed.Add("a");
        r.Missing.AddRange(["b", "c", "d", "e", "f", "g"]);
        Assert.StartsWith("Ověřeno 4 soubory (", VerifyTexts.CliSummary(r));
    }

    [Fact]
    public void NoHashRecord_KeepsTheChainProblems()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        File.WriteAllText(Path.Combine(card, "ascmhl", "0001_A001_x.mhl"), "<hashlist");
        var e = Assert.Throws<OffloadError>(() => BackupVerifier.Verify(card));
        Assert.Contains("ASC MHL generaci 0001 nejde přečíst", e.Message);
    }

    [Fact]
    public void BrokenMhl_WithHashList_FallsBack_AndKeepsTheProblem()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        var data = TestFs.WriteRandom(Layout.Join(card, "c.xml"), 1000, 3);
        Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001.xxh64"), $"{HashAlgo.Of("xxh64", data)}  c.xml\n");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        File.WriteAllText(Path.Combine(card, "ascmhl", "0001_A001_x.mhl"), "<hashlist");
        var r = BackupVerifier.Verify(card, chunkSize: 4096);
        Assert.Equal("hashList", r.Basis);
        Assert.Contains(r.ChainProblems, p => p.Contains("nejde přečíst"));
    }

    [Fact]
    public void ReportOutsideCard_WhenMhlDoesNotIgnoreReport()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        TestFs.WriteRandom(Layout.Join(card, "c.xml"), 1000, 3);
        var g1 = Gen(card, 1, ("c.xml", H("xxh64", "original", X(card, "c.xml"))));
        MhlFixtures.Chain(card, g1);

        var r = BackupVerifier.Verify(card, chunkSize: 4096);
        Assert.False(r.ReportInsideCard);
        var before = Directory.GetFiles(card, "*", SearchOption.AllDirectories).Order().ToList();
        var html = VerifyReport.Write(r);

        var day = Path.GetDirectoryName(card)!;
        Assert.StartsWith(Path.Combine(day, "_REPORT") + Path.DirectorySeparatorChar + "A001_verify_", html);
        Assert.True(File.Exists(Path.Combine(day, "_REPORT", $"A001_verify_{VerifyReport.Stamp(r)}Z.json")));
        Assert.Equal(before, Directory.GetFiles(card, "*", SearchOption.AllDirectories).Order().ToList());
        Assert.Contains("mimo složku karty", VerifyTexts.ReportOutsideNote(html));
        Assert.Contains(html, VerifyTexts.ReportOutsideNote(html));
        Assert.Empty(BackupVerifier.Verify(card, chunkSize: 4096).Extra);
    }

    [Fact]
    public void ReportStaysInCard_WhenMhlIgnoresReport_OurOwn()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Mhl.Write(Reports.Sample(card), card, new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Local), "pc");
        TestFs.WriteRandom(Layout.Join(card, "CLIP/A&B.mov"), 1000, 1);
        var r = BackupVerifier.Verify(card, chunkSize: 4096);
        Assert.True(r.ReportInsideCard);
        Assert.Equal(Path.Combine(card, "_REPORT"), VerifyReport.ReportDir(r));
    }
}
