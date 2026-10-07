using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// Self-contained HTML report (thumbnails embedded) — can be emailed or opened straight from the disk.
public static partial class HtmlReport
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly Dictionary<string, string> StateLabel = new()
    {
        ["verified"] = "ověřeno", ["written"] = "zapsáno", ["failed"] = "CHYBA", ["pending"] = "nezpracováno",
    };

    const string RootCss = ":root{--bg:#f6f6f4;--card:#fff;--fg:#1d1d1f;--muted:#6e6e73;--line:#e3e3e0;--ok:#1f8a4c;--okbg:#e5f4ea;--bad:#c62828;--badbg:#fdeaea;--warn:#a86400;--warnbg:#fdf2dc;--accent:#3a5bd9}";
    const string DarkCss = "@media (prefers-color-scheme:dark){:root{--bg:#141416;--card:#1e1e21;--fg:#ececee;--muted:#9a9aa0;--line:#2e2e33;--ok:#4cc27c;--okbg:#14301f;--bad:#ff6b6b;--badbg:#3a1717;--warn:#f0b04a;--warnbg:#382a10;--accent:#8aa2ff}}";
    /// For the PDF: light colours only and the full file list expanded.
    const string PrintCss = "@page{size:A4 landscape;margin:28pt}body{background:#fff;font-size:11px}main{padding:0;max-width:none}th,td{padding:5px 7px}tr{break-inside:avoid}h2{break-after:avoid}.stat b{font-size:17px}.thumb,.nothumb{width:112px;height:63px}.thumbs figure img{width:96px;height:54px}";
    const string BaseCss =
        "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 \"Segoe UI\",-apple-system,BlinkMacSystemFont,\"Helvetica Neue\",sans-serif}\n" +
        "main{max-width:1200px;margin:0 auto;padding:28px 20px 60px}\n" +
        "header{display:flex;flex-wrap:wrap;align-items:center;gap:14px;margin-bottom:22px}\n" +
        "h1{font-size:26px;margin:0;letter-spacing:-.01em}h2{font-size:16px;margin:30px 0 10px}\n" +
        ".sub{color:var(--muted)}.badge{display:inline-block;padding:4px 12px;border-radius:999px;font-weight:600;font-size:13px}\n" +
        ".ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}.muted{color:var(--muted)}\n" +
        ".badge.ok{background:var(--okbg)}.badge.bad{background:var(--badbg)}.badge.warn{background:var(--warnbg)}\n" +
        ".stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}\n" +
        ".stat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px}\n" +
        ".stat b{display:block;font-size:20px;font-variant-numeric:tabular-nums}.stat span{color:var(--muted);font-size:12px}\n" +
        ".panel{background:var(--card);border:1px solid var(--line);border-radius:10px;overflow-x:auto}\n" +
        "table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:middle;white-space:nowrap}\n" +
        "th{font-size:12px;color:var(--muted);font-weight:600}tr:last-child td{border-bottom:0}\n" +
        "td.num{text-align:right;font-variant-numeric:tabular-nums}code,.mono{font:12px ui-monospace,Consolas,Menlo,monospace}\n" +
        "td.clip{white-space:normal;min-width:180px;max-width:300px}.wrap{white-space:normal}\n" +
        ".thumb{width:128px;height:72px;object-fit:contain;background:#000;border-radius:4px;display:block}\n" +
        ".nothumb{width:128px;height:72px;border-radius:4px;background:var(--line)}\n" +
        ".thumbs{display:flex;gap:4px}.thumbs figure{margin:0}.thumbs figcaption{font:10px ui-monospace,Consolas,monospace;color:var(--muted);text-align:center}\n" +
        ".clipname{font-weight:600}.path,.note{color:var(--muted);font-size:12px}\n" +
        "details summary{cursor:pointer;padding:10px 0;font-weight:600}\n" +
        ".err{white-space:normal;color:var(--bad)}\n" +
        "footer{margin-top:40px;color:var(--muted);font-size:12px}\n" +
        "@media print{body{background:#fff}.panel,.stat{border-color:#ccc}details{display:block}}\n";

    internal static string Esc(string s)
    {
        var o = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': o.Append("&amp;"); break;
                case '<': o.Append("&lt;"); break;
                case '>': o.Append("&gt;"); break;
                case '"': o.Append("&quot;"); break;
                default: o.Append(c); break;
            }
        }
        return o.ToString();
    }

    /// The report's CSS (screen with dark mode, or print) — shared with the verification report.
    internal static string Styles(bool forPrint) => RootCss + "\n" + (forPrint ? PrintCss : DarkCss) + "\n" + BaseCss;

    internal static string DateTimeText(DateTime d) => d.ToLocalTime().ToString("d. M. yyyy HH:mm:ss", Cs);
    static string TimeText(DateTime d) => d.ToLocalTime().ToString("HH:mm:ss", Inv);

    internal static string Duration(double seconds)
    {
        int t = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    }

    [GeneratedRegex(@"^(?:HASH|MD5) MISMATCH: dest (?<dest>\S+) != source (?<source>\S+)$")]
    private static partial Regex Mismatch();

    [GeneratedRegex(@" \(after (?<n>\d+) retries\)$")]
    private static partial Regex Retries();

    [GeneratedRegex(@"^(?<algo>md5|xxh64) hash failed: ")]
    private static partial Regex HashFailed();

    /// A file error as the report JSON keeps it (English, shared with the Mac — spec 2.6) in Czech for the HTML/PDF.
    /// Texts it does not know (the Mac's, the system's) stay as they are.
    public static string ErrorText(string error)
    {
        if (error.StartsWith("source: ", StringComparison.Ordinal)) return "karta: " + ErrorText(error["source: ".Length..]);
        if (error.StartsWith("verify: ", StringComparison.Ordinal)) return "chyba při ověření: " + ErrorText(error["verify: ".Length..]);
        if (Mismatch().Match(error) is { Success: true } m)
            return $"{(error.StartsWith("MD5", StringComparison.Ordinal) ? "nesouhlasí MD5" : "nesouhlasí kontrolní součet")} (cíl {m.Groups["dest"].Value}, karta {m.Groups["source"].Value})";
        if (HashFailed().Match(error) is { Success: true } h)
            return $"výpočet {(h.Groups["algo"].Value == "md5" ? "MD5" : "xxHash64")} selhal: " + error[h.Length..];
        if (error.StartsWith("size mismatch: ", StringComparison.Ordinal)) return "jiná velikost: " + error["size mismatch: ".Length..];
        if (error.StartsWith("source size changed during copy ", StringComparison.Ordinal))
            return "soubor na kartě se během kopie změnil " + error["source size changed during copy ".Length..];
        if (error == "write failed") return "zápis selhal";
        if (error == "cancelled") return "zrušeno";
        if (error == OffloadEngine.CardChangedWhilePaused) return "karta se během pozastavení vyměnila nebo vytáhla";
        return Retries().Replace(error, " (po ${n} opakováních)");
    }

    static (string Class, string Label) ProxyLabel(ClipEntry c) => c.ProxyStatus switch
    {
        ProxyStatus.Done => ("ok", "hotovo"),
        ProxyStatus.Failed => ("bad", "chyba"),
        ProxyStatus.Unsupported => ("warn", "nepodporováno"),
        ProxyStatus.Skipped => ("muted", "přeskočeno"),
        _ => ("muted", "—"),
    };

    /// Disk column: last folder of the root, or the drive itself ("E:\").
    static string DiskName(string root)
    {
        var name = Path.GetFileName(root.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : root;
    }

    public static string Render(OffloadReport r, bool forPrint = false)
    {
        bool ok = r.Success;
        string status = !ok ? "CHYBA" : (r.Verified ? "OVĚŘENO" : "ZKOPÍROVÁNO — NEOVĚŘENO");
        string statusClass = !ok ? "bad" : (r.Verified ? "ok" : "warn");
        var failed = r.FailedFiles;
        var videoClips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        double totalDuration = videoClips.Sum(c => c.Info?.Duration ?? 0);

        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Offload {Esc(r.CardName)} – {Esc(r.Date)}</title>\n<style>\n");
        h.Append(Styles(forPrint));
        h.Append("</style></head><body><main>\n");
        var append = r.Generation > 1 ? $" <span class=\"sub\">· doplnění (generace {r.Generation})</span>" : "";
        h.Append($"<header><h1>{Esc(r.CardName)}{append}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
        h.Append($"<span class=\"sub\">{DateTimeText(r.Started)} – {TimeText(r.Finished)}</span></header>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, r.TotalFiles.ToString(Inv), "souborů");
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, videoClips.Count.ToString(Inv), "klipů" + (totalDuration > 0 ? " · " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "kopírování · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "ověření · " + (r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "neprovedeno"));
        if (r.PausedSeconds > 0) Stat(h, Duration(r.PausedSeconds), "pozastaveno · mimo rychlosti");
        Stat(h, Esc(string.Join(" + ", r.Algorithms.Select(a => a.ToUpperInvariant()))), "kontrolní součet");
        h.Append("</div>\n\n");

        h.Append("<h2>Cíle</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Disk</th><th>Složka</th><th>FS</th><th class=\"num\">OK</th><th class=\"num\">Chyby</th></tr>\n");
        foreach (var d in r.Destinations)
        {
            h.Append($"<tr><td>{Esc(DiskName(d.Root))}</td><td class=\"mono wrap\">{Esc(d.CardPath)}</td><td>{Esc(d.Filesystem)}</td>");
            h.Append($"<td class=\"num ok\">{d.FilesOK}</td><td class=\"num {(d.FilesFailed > 0 ? "bad" : "muted")}\">{d.FilesFailed}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        for (int i = 0; i < r.Destinations.Count; i++)
        {
            if (r.Destinations[i].Unbuffered == false)
                h.Append($"<p class=\"warn\">Cíl {i + 1}: svazek nepodporuje přímý zápis, použita systémová cache.</p>\n");
        }
        h.Append($"<p class=\"sub\">Zdroj: <span class=\"mono\">{Esc(r.Source)}</span> ({Esc(r.SourceFilesystem)})</p>\n");

        if (failed.Count > 0)
        {
            h.Append($"<h2 class=\"bad\">Chyby ({failed.Count})</h2><div class=\"panel\"><table><tr><th>Soubor</th><th>Problém</th></tr>\n");
            foreach (var f in failed)
            {
                var msgs = new List<string>();
                if (f.SourceError != null) msgs.Add("karta: " + ErrorText(f.SourceError));
                for (int i = 0; i < f.Errors.Count; i++)
                    if (f.Errors[i] is string e) msgs.Add($"cíl {i + 1}: {ErrorText(e)}");
                h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"err\">{Esc(string.Join(" · ", msgs))}</td></tr>\n");
            }
            h.Append("</table></div>\n");
        }

        if (r.Clips.Count > 0) AppendClips(h, r);

        h.Append($"<details{(forPrint ? " open" : "")}><summary>Všechny soubory ({r.Files.Count})</summary><div class=\"panel\"><table>\n");
        bool md5 = r.Algorithms.Contains("md5");
        h.Append($"<tr><th>Soubor</th><th class=\"num\">Velikost</th><th>xxh64</th>{(md5 ? "<th>md5</th>" : "")}");
        for (int i = 0; i < r.Destinations.Count; i++) h.Append($"<th>Cíl {i + 1}</th>");
        h.Append("</tr>\n");
        foreach (var f in r.Files)
        {
            h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"num\">{Format.Bytes(f.Size)}</td><td class=\"mono\">{Esc(f.Xxh64 ?? "—")}</td>");
            if (md5) h.Append($"<td class=\"mono\">{Esc(f.Md5 ?? "—")}</td>");
            foreach (var s in f.Destinations)
            {
                var cls = s == "verified" ? "ok" : (s == "failed" ? "bad" : "warn");
                h.Append($"<td class=\"{cls}\">{StateLabel.GetValueOrDefault(s, s)}</td>");
            }
            h.Append("</tr>\n");
        }
        h.Append("</table></div></details>\n");
        if (r.Skipped.Count > 0)
            h.Append($"<p class=\"sub\">Vynechané systémové soubory: {Esc(string.Join(", ", r.Skipped))}</p>\n");
        h.Append($"<footer>{Esc(r.Tool)} {Esc(r.Version)} · ASC MHL v ascmhl/ · {DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");

    static void AppendClips(StringBuilder h, OffloadReport r)
    {
        h.Append("<h2>Klipy</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th></th><th>Klip</th><th>Formát</th><th>Rozlišení</th><th class=\"num\">FPS</th><th class=\"num\">Délka</th><th>TC</th><th class=\"num\">Audio</th><th class=\"num\">Velikost</th><th>Proxy</th></tr>\n");
        foreach (var c in r.Clips)
        {
            var img = c.Thumbnails.Count > 0
                ? "<div class=\"thumbs\">" + string.Concat(c.Thumbnails.Select(t =>
                    $"<figure><img class=\"thumb\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(t.Jpeg)}\" alt=\"\"><figcaption>{Esc(t.Label)}</figcaption></figure>")) + "</div>"
                : "<div class=\"nothumb\"></div>";
            var i = c.Info;
            var codecParts = new List<string>();
            if (c.Clip.Format != ClipFormat.Video) codecParts.Add(c.Clip.Format.Label());
            if (i?.Codec is string codec && !codecParts.Contains(codec)) codecParts.Add(codec);
            var (pc, pl) = ProxyLabel(c);
            var notes = new List<string>();
            foreach (var n in new[] { c.ProbeError, c.ProxyNote })
                if (n != null && !notes.Contains(n)) notes.Add(n);
            var note = string.Join(" · ", notes);
            var cameraProxy = c.Clip.IsCameraProxy ? " <span class=\"muted\">(proxy kamery)</span>" : "";
            var noteHtml = note.Length == 0 ? "" : $"<div class=\"path err\">{Esc(note)}</div>";
            if (c.Lut is { Source: not "none" } lu) noteHtml += $"<div class=\"note\">LUT: {Esc(lu.Name)} ({Esc(lu.Reason)})</div>";
            else if (c.Lut is { } ln && ln.Reason != "vypnuto v nastavení") noteHtml += $"<div class=\"note\">LUT: bez LUT ({Esc(ln.Reason)})</div>";
            var audio = i == null ? "" : (i.AudioChannels > 0 ? $"{i.AudioChannels} ch" : "—");
            h.Append($"<tr><td>{img}</td><td class=\"clip\"><div class=\"clipname\">{Esc(c.Clip.Name)}{cameraProxy}</div>");
            h.Append($"<div class=\"path\">{Esc(c.Clip.Path)}</div>{noteHtml}</td>");
            h.Append($"<td class=\"wrap\">{Esc(string.Join(" · ", codecParts))}</td><td>{Esc(i?.ResolutionLabel ?? "")}</td><td class=\"num\">{Esc(i?.FpsLabel ?? "")}</td>");
            h.Append($"<td class=\"num\">{Esc(i?.DurationLabel ?? "")}</td><td class=\"mono\">{Esc(i?.Timecode ?? "")}</td>");
            h.Append($"<td class=\"num\">{audio}</td>");
            h.Append($"<td class=\"num\">{Format.Bytes(c.Clip.Bytes)}</td><td class=\"{pc}\">{pl}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        if (r.Proxy is { Enabled: true } p)
        {
            var lut = LutResolver.EffectiveMode(p) switch
            {
                LutMode.Auto => "automaticky podle kamery",
                LutMode.Builtin => LutCatalog.Default.Find(p.LutBuiltin)?.Name ?? p.LutBuiltin ?? "bez LUT",
                LutMode.Custom when !string.IsNullOrEmpty(p.LutPath) => Path.GetFileName(p.LutPath),
                _ => "bez LUT",
            };
            var frame = p.FrameLabel is string fl ? $", výřez {fl}" : "";
            var encoder = r.ProxyEncoder is string enc ? $" ({enc})" : "";
            var extra = p.ExtraFolder is string x ? $" · také ve složce <span class=\"mono\">{Esc(x)}</span>" : "";
            h.Append($"<p class=\"sub\">Proxy: {Esc(p.Codec.Label() + encoder)}, {p.Height}p{frame}, LUT: {Esc(lut)} → <span class=\"mono\">{Layout.ProxyDirName}/</span>{extra}</p>\n");
        }
    }
}
