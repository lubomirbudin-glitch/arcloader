using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// Self-contained HTML report (thumbnails embedded) — can be emailed or opened straight from the disk.
public static partial class HtmlReport
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly Dictionary<string, string> StateLabel = new()
    {
        ["verified"] = "verified", ["written"] = "written", ["failed"] = "ERROR", ["pending"] = "not processed",
    };

    const string RootCss = ":root{--bg:#f6f6f4;--card:#fff;--fg:#1d1d1f;--muted:#6e6e73;--line:#e3e3e0;--ok:#1f8a4c;--okbg:#e5f4ea;--bad:#c62828;--badbg:#fdeaea;--warn:#a86400;--warnbg:#fdf2dc;--accent:#3a5bd9}";
    const string DarkCss = "@media (prefers-color-scheme:dark){:root{--bg:#141416;--card:#1e1e21;--fg:#ececee;--muted:#9a9aa0;--line:#2e2e33;--ok:#4cc27c;--okbg:#14301f;--bad:#ff6b6b;--badbg:#3a1717;--warn:#f0b04a;--warnbg:#382a10;--accent:#8aa2ff}}";
    /// For the PDF: the full file list expanded and a denser type size (the page rule is in BaseCss). Comes after BaseCss,
    /// so it wins; panels never scroll, so PdfReport.FitScript sees anything wider than the page and zooms it out.
    const string PrintCss = "body{font-size:11px}main{padding:0;max-width:none}th,td{padding:5px 7px}h2{break-after:avoid}.stat b{font-size:17px}.panel{overflow:visible}code,.mono{font-size:10px}";
    const string BaseCss =
        "@page{size:A4 landscape;margin:10mm}\n" +
        "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 \"Segoe UI\",-apple-system,BlinkMacSystemFont,\"Helvetica Neue\",sans-serif;overflow-wrap:anywhere}\n" +
        "main{max-width:1200px;margin:0 auto;padding:28px 20px 60px}\n" +
        ".toolbar{display:flex;flex-wrap:wrap;align-items:center;gap:8px 14px;margin-bottom:18px}\n" +
        ".toolbar button{font:inherit;font-weight:600;padding:7px 16px;border-radius:8px;border:1px solid var(--accent);background:var(--accent);color:#fff;cursor:pointer}\n" +
        ".toolbar button:hover{filter:brightness(1.1)}.toolbar a{color:var(--accent);font-weight:600}.toolbar .hint{color:var(--muted);font-size:12px}\n" +
        "header{display:flex;flex-wrap:wrap;align-items:center;gap:14px;margin-bottom:22px}\n" +
        "h1{font-size:26px;margin:0;letter-spacing:-.01em}h2{font-size:16px;margin:30px 0 10px}\n" +
        ".sub{color:var(--muted)}.badge{display:inline-block;padding:4px 12px;border-radius:999px;font-weight:600;font-size:13px}\n" +
        ".ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}.muted{color:var(--muted)}\n" +
        ".badge.ok{background:var(--okbg)}.badge.bad{background:var(--badbg)}.badge.warn{background:var(--warnbg)}\n" +
        ".stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}\n" +
        ".stat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px}\n" +
        ".stat b{display:block;font-size:20px;font-variant-numeric:tabular-nums}.stat span{color:var(--muted);font-size:12px}\n" +
        ".panel{background:var(--card);border:1px solid var(--line);border-radius:10px;overflow-x:auto}\n" +
        "table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:middle;overflow-wrap:anywhere}\n" +
        "table.clips{table-layout:fixed}table.clips td,table.clips th{padding:8px 6px;overflow-wrap:normal}table.clips td.clip{overflow-wrap:anywhere}table.clips td.st{overflow-wrap:normal;word-break:normal;hyphens:none}table.clips td.st a{color:inherit}table.clips td.num,table.clips th.num,table.clips td.nw{white-space:nowrap}table.clips td.fps{white-space:normal}\n" +
        "td.nw{white-space:nowrap}table:not(.clips) th{white-space:nowrap}\n" +
        ".c-thumb{width:20%}.c-clip{width:17%}.c-codec{width:8%}.c-res{width:9%}.c-fps{width:7%}.c-dur{width:6%}.c-tc{width:9%}.c-audio{width:5%}.c-size{width:8%}.c-proxy{width:11%}\n" +
        "th{font-size:12px;color:var(--muted);font-weight:600}tr:last-child td{border-bottom:0}tr{break-inside:avoid}\n" +
        "td.num{text-align:right;font-variant-numeric:tabular-nums}code,.mono{font:12px ui-monospace,Consolas,Menlo,monospace}\n" +
        "td.clip{min-width:0}.wrap{white-space:normal}\n" +
        ".thumb{display:block;width:100%;max-width:100%;height:auto;aspect-ratio:16/9;object-fit:contain;background:#000;border-radius:4px}\n" +
        ".nothumb{width:100%;max-width:128px;aspect-ratio:16/9;border-radius:4px;background:var(--line)}\n" +
        ".thumbs{display:flex;flex-wrap:wrap;gap:4px}.thumbs figure{margin:0;flex:1 1 0;min-width:0;max-width:128px;container-type:inline-size}.thumbs figcaption{font:10px ui-monospace,Consolas,monospace;font-size:min(10px,14cqi);color:var(--muted);text-align:center;white-space:nowrap}\n" +
        ".clipname{font-weight:600}.path,.note{color:var(--muted);font-size:12px}\n" +
        "details summary{cursor:pointer;padding:10px 0;font-weight:600}\n" +
        ".err{color:var(--bad)}\n" +
        "footer{margin-top:40px;color:var(--muted);font-size:12px}\n" +
        "@media (max-width:760px){main{padding:16px 10px 40px}.hide-sm{display:none}.thumbs figcaption{display:none}.c-thumb{width:22%}.c-clip{width:26%}.c-fps{width:8%}.c-dur{width:12%}.c-size{width:12%}.c-proxy{width:20%}table.clips td.st{font-size:12px}}\n" +
        // Printing from any browser, even from a dark screen: white page, dark text.
        "@media print{" + RootCss + "body{background:#fff;color:#000}.toolbar{display:none}.panel{overflow:visible;border-color:#ccc}.stat{border-color:#ccc}details{display:block}th.hide-sm,td.hide-sm{display:table-cell}col.hide-sm{display:table-column}.thumbs figcaption{display:block}}\n";

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
    internal static string Styles(bool forPrint) => RootCss + "\n" + (forPrint ? "" : DarkCss + "\n") + BaseCss + (forPrint ? PrintCss + "\n" : "");

    /// Screen-only bar at the top of every HTML report: print to PDF from the browser, or open the PDF the app wrote next to the
    /// HTML. The PDF is made after the HTML (and skipped when Edge is missing); there is no option to switch it off, so the link
    /// is always there and the file may be missing without Edge. Not in the PDF itself.
    internal static string Toolbar(string pdfFileName, bool forPrint) => forPrint ? "" :
        "<div class=\"toolbar\"><button type=\"button\" onclick=\"window.print()\">Save as PDF</button>" +
        "<span class=\"hint\">in the print dialog choose \u201cSave as PDF\u201d</span>" +
        $"<a href=\"{Esc(Uri.EscapeDataString(pdfFileName))}\" title=\"The PDF is made at the end with Microsoft Edge; without it the file may not exist\">Open PDF report</a></div>\n";

    internal static string DateTimeText(DateTime d) => d.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Inv);
    static string TimeText(DateTime d) => d.ToLocalTime().ToString("HH:mm:ss", Inv);

    internal static string Duration(double seconds)
    {
        int t = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        return t >= 3600
            ? string.Create(Inv, $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}")
            : string.Create(Inv, $"{t / 60}:{t % 60:00}");
    }

    [GeneratedRegex(@"^(?:HASH|MD5) MISMATCH: dest (?<dest>\S+) != source (?<source>\S+)$")]
    private static partial Regex Mismatch();

    /// Plan 10: a block that does not match (BlockReads.Result).
    [GeneratedRegex(@"^HASH MISMATCH: block (?<block>\d+) dest (?<dest>\S+) != source (?<source>\S+)$")]
    private static partial Regex BlockMismatch();

    [GeneratedRegex(@" \(after (?<n>\d+) retries\)$")]
    private static partial Regex Retries();

    [GeneratedRegex(@"^(?<algo>md5|xxh64) hash failed: ")]
    private static partial Regex HashFailed();

    /// OffloadEngine.CardDiffers: a repair's card read differently from what the backup recorded.
    [GeneratedRegex(@"^card differs from the backup: (?<algo>xxh64|md5) (?<now>\S+) != recorded (?<recorded>\S+)$")]
    private static partial Regex CardDiffers();

    /// A file error as the report JSON keeps it (terse, shared with the Mac — spec 2.6) in plain words for the HTML/PDF.
    /// Texts it does not know (the Mac's, the system's) stay as they are.
    public static string ErrorText(string error)
    {
        if (error.StartsWith("source: ", StringComparison.Ordinal)) return "card: " + ErrorText(error["source: ".Length..]);
        if (error.StartsWith("verify: ", StringComparison.Ordinal)) return "verification error: " + ErrorText(error["verify: ".Length..]);
        if (error.StartsWith("set aside: ", StringComparison.Ordinal))
            return "cannot set the mismatching copy aside, it stayed in place: " + error["set aside: ".Length..];
        if (BlockMismatch().Match(error) is { Success: true } bm)
            return $"block {bm.Groups["block"].Value} mismatch (destination {bm.Groups["dest"].Value}, card {bm.Groups["source"].Value})";
        if (Mismatch().Match(error) is { Success: true } m)
            return $"{(error.StartsWith("MD5", StringComparison.Ordinal) ? "MD5 mismatch" : "checksum mismatch")} (destination {m.Groups["dest"].Value}, card {m.Groups["source"].Value})";
        if (HashFailed().Match(error) is { Success: true } h)
            return $"{(h.Groups["algo"].Value == "md5" ? "MD5" : "xxHash64")} calculation failed: " + error[h.Length..];
        if (error.StartsWith("size mismatch: ", StringComparison.Ordinal)) return "size mismatch: " + error["size mismatch: ".Length..];
        if (error.StartsWith("source size changed during copy ", StringComparison.Ordinal))
            return "the file on the card changed during the copy " + error["source size changed during copy ".Length..];
        if (error == "write failed") return "write failed";
        if (error == "cancelled") return "cancelled";
        if (error == OffloadEngine.CardChangedWhilePaused) return "the card was swapped or removed while paused";
        if (CardDiffers().Match(error) is { Success: true } c)
            return $"the card differs from the backup ({c.Groups["algo"].Value} {c.Groups["now"].Value}, in the backup {c.Groups["recorded"].Value}) — nothing was moved or written";
        return Retries().Replace(error, " (after ${n} retries)");
    }

    static (string Class, string Label) ProxyLabel(ClipEntry c) => c.ProxyStatus switch
    {
        ProxyStatus.Done => ("ok", "done"),
        ProxyStatus.Failed => ("bad", "error"),
        ProxyStatus.Unsupported => ("warn", "unsupported"),
        ProxyStatus.Skipped when c.OriginalLighter => ("muted", "no proxy"),
        ProxyStatus.Skipped => ("muted", "skipped"),
        _ => ("muted", "—"),
    };

    /// The proxy status; a finished proxy links to its file (the report is in `_REPORT`, the proxy in `_PROXY` beside it).
    static string ProxyCell(ClipEntry c, string label) =>
        c.ProxyStatus == ProxyStatus.Done && c.ProxyPath is string rel
            ? $"<a href=\"../{Esc(string.Join("/", rel.Split('/').Select(Uri.EscapeDataString)))}\" title=\"{Esc(rel)}\">{label}</a>"
            : label;

    /// Disk column: last folder of the root, or the drive itself ("E:\").
    static string DiskName(string root)
    {
        var name = Path.GetFileName(root.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : root;
    }

    public static string Render(OffloadReport r, bool forPrint = false)
    {
        bool ok = r.Success;
        // Final review, item 3: verifyIncomplete — the results are not in every folder yet, never a green "VERIFIED".
        bool incomplete = r.VerifyIncomplete == true;
        string status = !ok ? "ERROR" : r.VerifyPending == true ? "COPIED — VERIFYING" : incomplete ? "UNFINISHED" : (r.Verified ? "VERIFIED" : "COPIED — NOT VERIFIED");
        string statusClass = !ok ? "bad" : (r.Verified && !incomplete ? "ok" : "warn");
        bool inProxy = r.Destinations.Any(d => d.VerifyMethod == ReportDestination.InProxy);
        var failed = r.FailedFiles;
        var videoClips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        double totalDuration = videoClips.Sum(c => c.Info?.Duration ?? 0);

        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Offload {Esc(r.CardName)} – {Esc(r.Date)}</title>\n<style>\n");
        h.Append(Styles(forPrint));
        h.Append("</style></head><body><main>\n");
        h.Append(Toolbar(OffloadReport.PdfName(r.CardName, r.Generation ?? 1), forPrint));
        var append = r.Generation > 1 ? $" <span class=\"sub\">· {(r.IsRepair ? "repair" : "append")} (generation {r.Generation})</span>" : "";
        h.Append($"<header><h1>{Esc(r.CardName)}{append}</h1><span class=\"badge {statusClass}\">{status}</span>\n");
        h.Append($"<span class=\"sub\">{DateTimeText(r.Started)} – {TimeText(r.Finished)}</span></header>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, r.TotalFiles.ToString(Inv), "files");
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, videoClips.Count.ToString(Inv), "clips" + (totalDuration > 0 ? " · duration " + Duration(totalDuration) : ""));
        Stat(h, Duration(r.CopySeconds), "copy · " + Format.Rate(r.TotalBytes, r.CopySeconds));
        Stat(h, r.Verified ? Duration(r.VerifySeconds) : "—", "verification · " + (!r.Verified ? (r.VerifyPending == true ? "running" : "not done")
            : inProxy ? "during proxies" : Format.Rate(r.TotalBytes, r.VerifySeconds)));
        if (r.SafeToFormatSeconds is double toFormat) Stat(h, Duration(toFormat), "until \u201csafe to format\u201d");
        if (r.PausedSeconds > 0) Stat(h, Duration(r.PausedSeconds), "paused · not in the speeds");
        var algorithms = r.Algorithms.ToList();
        if (r.Files.Any(f => f.Xxh128 != null) && !algorithms.Contains("xxh128")) algorithms.Add("xxh128");   // plan 10, R2
        Stat(h, Esc(string.Join(" + ", algorithms.Select(a => a.ToUpperInvariant()))), "checksum");
        h.Append("</div>\n\n");

        h.Append("<h2>Destinations</h2><div class=\"panel\"><table>\n");
        h.Append("<tr><th>Disk</th><th>Folder</th><th>FS</th><th>Volume</th><th>Verification</th><th class=\"num\">OK</th><th class=\"num\">Errors</th></tr>\n");
        foreach (var d in r.Destinations)
        {
            h.Append($"<tr><td class=\"nw\">{Esc(DiskName(d.Root))}</td><td class=\"mono wrap\">{Esc(d.CardPath)}</td><td class=\"nw\">{Esc(d.Filesystem)}</td>");
            h.Append($"<td class=\"mono nw\">{Esc(d.VolumeSerial ?? "—")}</td><td class=\"nw\">{Esc(MethodText(d.VerifyMethod))}</td>");
            h.Append($"<td class=\"num ok\">{d.FilesOK}</td><td class=\"num {(d.FilesFailed > 0 ? "bad" : "muted")}\">{d.FilesFailed}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        for (int i = 0; i < r.Destinations.Count; i++)
        {
            if (r.Destinations[i].Unbuffered == false)
                h.Append($"<p class=\"warn\">Destination {i + 1}: the volume does not support direct writes; the system cache was used.</p>\n");
            if (r.Destinations[i].VerifyNote is string note && r.Destinations[i].VerifyMethod != null)
                h.Append($"<p class=\"sub\">Destination {i + 1}: {Esc(note)}</p>\n");
        }
        h.Append($"<p class=\"sub\">Source: <span class=\"mono\">{Esc(r.Source)}</span> ({Esc(r.SourceFilesystem)})</p>\n");

        if (failed.Count > 0)
        {
            h.Append($"<h2 class=\"bad\">Errors ({failed.Count})</h2><div class=\"panel\"><table><tr><th>File</th><th>Problem</th></tr>\n");
            foreach (var f in failed)
            {
                var msgs = new List<string>();
                if (f.SourceError != null) msgs.Add("card: " + ErrorText(f.SourceError));
                for (int i = 0; i < f.Errors.Count; i++)
                    if (f.Errors[i] is string e) msgs.Add($"destination {i + 1}: {ErrorText(e)}");
                h.Append($"<tr><td class=\"mono\">{Esc(f.Path)}</td><td class=\"err\">{Esc(string.Join(" · ", msgs))}</td></tr>\n");
            }
            h.Append("</table></div>\n");
        }

        var warnings = r.ContentWarnings;
        if (warnings.Count > 0)
        {
            // Spec 7, 9: the decoder found a defect — the copy matches the card, the file itself may be damaged.
            h.Append($"<h2 class=\"warn\">Content defects ({warnings.Count})</h2><p class=\"sub\">The copy matches the card bit for bit — the defect is already in the camera file. Check it before formatting the card (the maker's tool may still rescue it straight from the card).</p>\n");
            h.Append("<div class=\"panel\"><table><tr><th>Clip</th><th>Where</th><th>Defect</th><th>Decoder</th><th></th></tr>\n");
            foreach (var c in warnings)
            {
                var img = c.Content!.Jpeg is { } jpeg ? $"<img class=\"thumb\" style=\"max-width:160px\" src=\"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}\" alt=\"\">" : "";
                h.Append($"<tr><td>{Esc(c.Clip.Name)}</td><td class=\"mono\">{Esc(c.Content.Timecode ?? "")}</td><td class=\"warn\">{Esc(VerifyInProxy.ContentResult.KindText(c.Content.Kind))}</td>");
                h.Append($"<td class=\"mono wrap\">{Esc(c.Content.Message ?? "")}</td><td>{img}</td></tr>\n");
            }
            h.Append("</table></div>\n");
        }
        if (r.VerifyNotes is { Count: > 0 } notes)
        {
            h.Append($"<h2 class=\"warn\">Verification — notes ({notes.Count})</h2><div class=\"panel\"><table>\n");
            foreach (var n in notes) h.Append($"<tr><td class=\"warn wrap\">{Esc(n)}</td></tr>\n");
            h.Append("</table></div>\n");
        }

        if (r.Clips.Count > 0) AppendClips(h, r);

        h.Append($"<details{(forPrint ? " open" : "")}><summary>All files ({r.Files.Count})</summary><div class=\"panel\"><table>\n");
        bool md5 = r.Algorithms.Contains("md5");
        h.Append($"<tr><th>File</th><th class=\"num\">Size</th><th>xxh64</th>{(md5 ? "<th>md5</th>" : "")}");
        for (int i = 0; i < r.Destinations.Count; i++) h.Append($"<th>Destination {i + 1}</th>");
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
            h.Append($"<p class=\"sub\">Skipped system files: {Esc(string.Join(", ", r.Skipped))}</p>\n");
        h.Append($"<footer>{Esc(r.Tool)} {Esc(r.Version)} · ASC MHL in ascmhl/ · {DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    /// "during proxies" / "read back" / "—" (plan 10, spec 9: the method per destination).
    internal static string MethodText(string? method) => method switch
    {
        ReportDestination.InProxy => "during proxies",
        ReportDestination.ReadBack => "read back",
        _ => "—",
    };

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");

    /// The FPS cell (task 2026-10-10, review F-1): a rate that differs is two lines in the narrow column — "35 fps", then
    /// "playback 24" small and muted; otherwise the one rate as before.
    internal static string FpsCell(ClipInfo? i) =>
        i is { CaptureDiffers: true }
            ? $"{Esc(ClipInfo.Label(i.CaptureFrameRate))} fps<div class=\"note\">playback {Esc(i.FpsLabel)}</div>"
            : Esc(i?.FpsSummary ?? "");

    static void AppendClips(StringBuilder h, OffloadReport r)
    {
        h.Append("<h2>Clips</h2><div class=\"panel\"><table class=\"clips\"><colgroup>");
        h.Append("<col class=\"c-thumb\"><col class=\"c-clip\"><col class=\"c-codec hide-sm\"><col class=\"c-res hide-sm\">");
        h.Append("<col class=\"c-fps\"><col class=\"c-dur\"><col class=\"c-tc hide-sm\"><col class=\"c-audio hide-sm\">");
        h.Append("<col class=\"c-size\"><col class=\"c-proxy\"></colgroup>\n");
        h.Append("<tr><th></th><th>Clip</th><th class=\"hide-sm\">Format</th><th class=\"hide-sm\">Resolution</th><th class=\"num\">FPS</th><th class=\"num\">Duration</th><th class=\"hide-sm\">TC</th><th class=\"num hide-sm\">Audio</th><th class=\"num\">Size</th><th>Proxy</th></tr>\n");
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
            // The lighter original's reason is neutral (grey), not a problem (red).
            foreach (var n in new[] { c.ProbeError, c.OriginalLighter ? null : c.ProxyNote })
                if (n != null && !notes.Contains(n)) notes.Add(n);
            var note = string.Join(" · ", notes);
            var cameraProxy = c.Clip.IsCameraProxy ? " <span class=\"muted\">(camera proxy)</span>" : "";
            var noteHtml = note.Length == 0 ? "" : $"<div class=\"path err\">{Esc(note)}</div>";
            if (c.OriginalLighter) noteHtml += $"<div class=\"note\">{Esc(c.ProxyNote!)}</div>";
            if (c.Lut is { Source: not "none" } lu) noteHtml += $"<div class=\"note\">LUT: {Esc(lu.Name)} ({Esc(lu.Reason)})</div>";
            else if (c.Lut is { SwitchedOff: false } ln) noteHtml += $"<div class=\"note\">LUT: {LutUse.NoLutName} ({Esc(ln.Reason)})</div>";
            var audio = i == null ? "" : (i.AudioChannels > 0 ? $"{i.AudioChannels} ch" : "—");
            h.Append($"<tr><td>{img}</td><td class=\"clip\"><div class=\"clipname\">{Esc(c.Clip.Name)}{cameraProxy}</div>");
            h.Append($"<div class=\"path\">{Esc(c.Clip.Path)}</div>{noteHtml}</td>");
            h.Append($"<td class=\"wrap hide-sm\">{Esc(string.Join(" · ", codecParts))}</td><td class=\"nw hide-sm\">{Esc(i?.ResolutionLabel ?? "")}</td><td class=\"num fps\">{FpsCell(i)}</td>");
            h.Append($"<td class=\"num\">{Esc(i?.DurationLabel ?? "")}</td><td class=\"mono hide-sm\">{Esc(i?.Timecode ?? "")}</td>");
            h.Append($"<td class=\"num hide-sm\">{audio}</td>");
            h.Append($"<td class=\"num\">{Format.Bytes(c.Clip.Bytes)}</td><td class=\"st {pc}\">{ProxyCell(c, pl)}</td></tr>\n");
        }
        h.Append("</table></div>\n");
        // Task 2026-10-10: a clip shot at another rate than it plays at — the length is the editing one.
        if (r.Clips.Any(c => c.Info?.CaptureDiffers == true))
            h.Append("<p class=\"sub\">FPS: the capture rate from the metadata of the first frame, followed by the file's playback rate when it differs. Duration = frame count ÷ playback fps (the length in the edit).</p>\n");
        if (r.Proxy is { Enabled: true } p)
        {
            var lut = LutResolver.EffectiveMode(p) switch
            {
                LutMode.Auto => "automatic by camera",
                LutMode.Builtin => LutCatalog.Default.Find(p.LutBuiltin)?.Name ?? p.LutBuiltin ?? LutUse.NoLutName,
                LutMode.Custom when !string.IsNullOrEmpty(p.LutPath) => Path.GetFileName(p.LutPath),
                _ => LutUse.NoLutName,
            };
            var frame = p.FrameLabel is string fl ? $", crop {fl}" : "";
            var encoder = r.ProxyEncoder is string enc ? $" ({enc})" : "";
            var extra = p.ExtraFolder is string x ? $" · also in the folder <span class=\"mono\">{Esc(x)}</span>" : "";
            h.Append($"<p class=\"sub\">Proxies: {Esc(p.Codec.Label() + encoder)}, {p.Height}p{frame}, LUT: {Esc(lut)} → <span class=\"mono\">{Layout.ProxyDirName}/</span>{extra}</p>\n");
        }
    }
}
