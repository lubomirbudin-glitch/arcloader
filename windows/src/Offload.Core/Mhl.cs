using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Offload.Core.Desktop;
using Offload.Core.Verification;

namespace Offload.Core;

/// ASC MHL v2 (urn:ASC:MHL:v2.0) — the industry-standard hash record in `<card>/ascmhl/`, so `ascmhl verify` and other
/// DIT tools can check the copy later. A full copy writes generation 0001; an append (spec 2.2, R5) writes generation N
/// with only its new files and adds it to the chain — the earlier generation files are never touched.
/// Optional directory/root hashes are omitted; per-file xxh64 hashes are what verification relies on.
public static class Mhl
{
    public const string DirName = "ascmhl";
    const string ChainName = "ascmhl_chain.xml";
    const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// Our own folders inside the card folder are not camera data.
    static readonly string[] IgnorePatterns = [".DS_Store", "ascmhl", "ascmhl/", Layout.ReportDirName, Layout.ProxyDirName];

    public static void Write(OffloadReport report, string cardPath) => Write(report, cardPath, DateTime.Now, Environment.MachineName);

    internal static void Write(OffloadReport report, string cardPath, DateTime now, string host)
    {
        int d = report.Destinations.Select(x => x.CardPath).ToList().IndexOf(cardPath);
        if (d < 0) return;
        var dir = Path.Combine(cardPath, DirName);
        NativeIO.Mkdirs(dir);

        int generation = report.Generation ?? 1;
        // An append keeps the chain and the ignore list of the earlier generations (spec 2.2) — and goes right after them.
        var earlier = generation > 1 ? MhlReader.Read(cardPath) : null;
        if (generation > 1)
        {
            int last = earlier is { Chain.Count: > 0 } ? earlier.Chain.Max(c => c.Seq) : 0;
            if (last != generation - 1)
                throw new IOException($"ASC MHL v {cardPath} se během doplnění změnil (poslední generace {last}, doplnění píše {generation}) — generace se nezapsala.");
        }
        var stamp = IsoDate(now);
        var folder = Path.GetFileName(cardPath.TrimEnd('\\'));
        var fileName = $"{generation:0000}_{folder}_{now.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}Z.mhl";
        int listed = report.Files.Count(f => f.Xxh64 != null);
        var comment = generation > 1
            ? $"Offload doplnění: {listed} {Texts.Plural(listed, "nový soubor", "nové soubory", "nových souborů")} z {report.Source}"
            : $"Offload of {report.CardName} from {report.Source}";
        IEnumerable<string> ignore = earlier?.Generations.FirstOrDefault() is { } first
            ? first.Ignore
            : IgnorePatterns.Concat(report.Skipped.Select(s => s.Split('/')[^1]));

        var x = new StringBuilder();
        void Line(string s) => x.Append(s).Append('\n');
        Line("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        Line("<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">");
        Line("  <creatorinfo>");
        Line($"    <creationdate>{stamp}</creationdate>");
        Line($"    <hostname>{Esc(host)}</hostname>");
        Line($"    <tool version=\"{Esc(report.Version)}\">{Esc(report.Tool)}</tool>");
        Line($"    <comment>{Esc(comment)}</comment>");
        Line("  </creatorinfo>");
        Line("  <processinfo>");
        Line("    <process>transfer</process>");
        Line("    <ignore>");
        foreach (var p in ignore)
            Line($"      <pattern>{Esc(p)}</pattern>");
        Line("    </ignore>");
        Line("  </processinfo>");
        Line("  <hashes>");
        foreach (var f in report.Files)
        {
            if (f.Xxh64 == null) continue;
            var state = f.Destinations[d];
            var action = state is "verified" or "written" ? "original" : "failed";
            Line("    <hash>");
            Line($"      <path size=\"{f.Size}\" lastmodificationdate=\"{IsoDate(f.Modified)}\">{Esc(f.Path)}</path>");
            Line($"      <xxh64 action=\"{action}\" hashdate=\"{stamp}\">{f.Xxh64}</xxh64>");
            Line("    </hash>");
        }
        Line("  </hashes>");
        Line("</hashlist>");

        var data = Encoding.UTF8.GetBytes(x.ToString());
        // A new generation file never replaces one (ruling I1); generation 0001 goes into a fresh card folder.
        if (generation > 1) NativeIO.WriteFileNew(Path.Combine(dir, fileName), data);
        else NativeIO.WriteFileDurably(Path.Combine(dir, fileName), data);

        // The chain: every earlier generation exactly as it was read, then this one — replaced in one rename.
        var entries = (earlier?.Chain ?? []).Append(new MhlChainEntry(generation, fileName, C4(data)));
        var chain = new StringBuilder();
        chain.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        chain.Append("<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n");
        foreach (var e in entries)
        {
            chain.Append($"  <hashlist sequencenr=\"{e.Seq.ToString(CultureInfo.InvariantCulture)}\">\n");
            chain.Append($"    <path>{Esc(e.Path)}</path>\n");
            chain.Append($"    <c4>{Esc(e.C4)}</c4>\n");
            chain.Append("  </hashlist>\n");
        }
        chain.Append("</ascmhldirectory>\n");
        NativeIO.WriteFileDurably(Path.Combine(dir, ChainName), Encoding.UTF8.GetBytes(chain.ToString()));
    }

    static string IsoDate(DateTime d) => d.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    internal static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// C4 ID: "c4" + base58(SHA-512), left-padded with "1" to 88 characters.
    internal static string C4(ReadOnlySpan<byte> data) => C4FromSha512(SHA512.HashData(data));

    /// The C4 ID of data whose SHA-512 is `digest` (a file hashed block by block).
    internal static string C4FromSha512(ReadOnlySpan<byte> digest)
    {
        var n = new BigInteger(digest, isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (n > 0)
        {
            n = BigInteger.DivRem(n, 58, out var rem);
            digits.Insert(0, Base58[(int)rem]);
        }
        return "c4" + new string('1', Math.Max(0, 88 - digits.Length)) + digits;
    }
}
