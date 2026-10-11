using System.Text;
using System.Xml.Linq;

namespace Offload.Core.Tests;

public class MhlTests
{
    static readonly XNamespace Ns = "urn:ASC:MHL:v2.0";

    [Fact]
    public void C4_MatchesReferenceIds()
    {
        Assert.Equal("c459dsjfscH38cYeXXYogktxf4Cd9ibshE3BHUo6a58hBXmRQdZrAkZzsWcbWtDg5oQstpDuni4Hirj75GEmTc1sFT", Mhl.C4([]));
        Assert.Equal("c45S4rnaTNWonxss1u8LzsaJdEph1AJhWUF4sh2waXKMsutyfAxg4ybUeuXVWS9HdNcEypmeXn8FZGonD4w1rj9DZp", Mhl.C4("abc"u8));
    }

    [Fact]
    public void Esc_EscapesXmlSpecials() => Assert.Equal("a&amp;b&lt;c&gt;&quot;", Mhl.Esc("a&b<c>\""));

    [Fact]
    public void Write_CreatesHashListAndChain()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var report = Reports.Sample(card);
        var now = new DateTime(2026, 10, 4, 10, 30, 0, DateTimeKind.Local);

        Mhl.Write(report, card, now, "PC&1");

        var mhlDir = Path.Combine(card, "ascmhl");
        var mhlFile = Assert.Single(Directory.GetFiles(mhlDir, "*.mhl"));
        Assert.Matches(@"\\0001_A001_\d{4}-\d\d-\d\d_\d{6}Z\.mhl$", mhlFile);
        var text = File.ReadAllText(mhlFile, Encoding.UTF8);
        Assert.Contains("<hostname>PC&amp;1</hostname>", text);
        Assert.Contains(">CLIP/A&amp;B.mov</path>", text);
        Assert.DoesNotContain("\r", text);

        var doc = XDocument.Parse(text);
        var hashes = doc.Descendants(Ns + "hash").ToList();
        Assert.Equal(2, hashes.Count);
        Assert.All(hashes, h => Assert.Equal("original", h.Element(Ns + "xxh64")!.Attribute("action")!.Value));
        Assert.Contains(doc.Descendants(Ns + "pattern"), p => p.Value == "_REPORT");
        // The renames (0.10.0, 0.11.0): the creatorinfo names mmMover (free text); the report's own `tool` stays "offload".
        var tool = doc.Descendants(Ns + "tool").Single();
        Assert.Equal(("mmMover", report.Version), (tool.Value, tool.Attribute("version")!.Value));
        Assert.Equal("offload", report.Tool);
        Assert.StartsWith("mmMover offload of A001 from ", doc.Descendants(Ns + "comment").Single().Value);

        var chain = XDocument.Load(Path.Combine(mhlDir, "ascmhl_chain.xml"));
        XNamespace dirNs = "urn:ASC:MHL:DIRECTORY:v2.0";
        Assert.Equal(Path.GetFileName(mhlFile), chain.Descendants(dirNs + "path").Single().Value);
        Assert.Equal(Mhl.C4(File.ReadAllBytes(mhlFile)), chain.Descendants(dirNs + "c4").Single().Value);
    }

    [Fact]
    public void Write_MarksFailedFiles()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Mhl.Write(Reports.Sample(card, destError: "boom"), card, DateTime.Now, "pc");
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(card, "ascmhl"), "*.mhl").Single());
        Assert.Contains("action=\"failed\"", text);
    }
}
