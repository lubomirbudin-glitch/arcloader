using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Offload.Core;

/// ASC MHL v2 (urn:ASC:MHL:v2.0) — the industry-standard hash record, written as the first generation
/// into `<card>/ascmhl/` so `ascmhl verify` and other DIT tools can check the copy later.
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

        var stamp = IsoDate(now);
        var folder = Path.GetFileName(cardPath.TrimEnd('\\'));
        var fileName = $"0001_{folder}_{now.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}Z.mhl";

        var x = new StringBuilder();
        void Line(string s) => x.Append(s).Append('\n');
        Line("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        Line("<hashlist version=\"2.0\" xmlns=\"urn:ASC:MHL:v2.0\">");
        Line("  <creatorinfo>");
        Line($"    <creationdate>{stamp}</creationdate>");
        Line($"    <hostname>{Esc(host)}</hostname>");
        Line($"    <tool version=\"{Esc(report.Version)}\">{Esc(report.Tool)}</tool>");
        Line($"    <comment>Offload of {Esc(report.CardName)} from {Esc(report.Source)}</comment>");
        Line("  </creatorinfo>");
        Line("  <processinfo>");
        Line("    <process>transfer</process>");
        Line("    <ignore>");
        foreach (var p in IgnorePatterns.Concat(report.Skipped.Select(s => s.Split('/')[^1])))
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
        NativeIO.WriteFileDurably(Path.Combine(dir, fileName), data);

        var chain = new StringBuilder();
        chain.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        chain.Append("<ascmhldirectory xmlns=\"urn:ASC:MHL:DIRECTORY:v2.0\">\n");
        chain.Append("  <hashlist sequencenr=\"1\">\n");
        chain.Append($"    <path>{Esc(fileName)}</path>\n");
        chain.Append($"    <c4>{C4(data)}</c4>\n");
        chain.Append("  </hashlist>\n");
        chain.Append("</ascmhldirectory>\n");
        NativeIO.WriteFileDurably(Path.Combine(dir, ChainName), Encoding.UTF8.GetBytes(chain.ToString()));
    }

    static string IsoDate(DateTime d) => d.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    internal static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// C4 ID: "c4" + base58(SHA-512), left-padded with "1" to 88 characters.
    internal static string C4(ReadOnlySpan<byte> data)
    {
        var n = new BigInteger(SHA512.HashData(data), isUnsigned: true, isBigEndian: true);
        var digits = new StringBuilder();
        while (n > 0)
        {
            n = BigInteger.DivRem(n, 58, out var rem);
            digits.Insert(0, Base58[(int)rem]);
        }
        return "c4" + new string('1', Math.Max(0, 88 - digits.Length)) + digits;
    }
}
