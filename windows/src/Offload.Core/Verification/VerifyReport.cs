using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Offload.Core.Desktop;

namespace Offload.Core.Verification;

public enum VerifyTone { Ok, Warning, Error }

/// The Czech texts of a verification (spec 2.4) — GUI job card, CLI summary, HTML report.
public static class VerifyTexts
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    static string N(long n) => n.ToString("N0", Cs);
    static string Files(int n) => $"{N(n)} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    /// "ASC MHL, 2 generace, xxHash64" / "seznam součtů, xxHash64 + MD5".
    public static string Basis(VerifyResult r)
    {
        var algos = string.Join(" + ", r.HashAlgorithms.Select(HashAlgo.Label));
        return r.Basis == "ascmhl"
            ? $"ASC MHL, {r.Generations} {Texts.Plural(r.Generations, "generace", "generace", "generací")}, {algos}"
            : $"seznam součtů, {algos}";
    }

    /// The main line, green / red / orange (cancelled).
    public static (string Text, VerifyTone Tone) Summary(VerifyResult r)
    {
        if (r.Cancelled)
            return ($"Ověření zrušeno po {N(r.Checked)} / {N(r.TotalFiles)} {(r.TotalFiles == 1 ? "souboru" : "souborech")}", VerifyTone.Warning);
        if (r.Success)
        {
            var all = r.Ok switch
            {
                1 => "soubor sedí",
                >= 2 and <= 4 => $"všechny {r.Ok} soubory sedí",
                _ => $"všech {N(r.Ok)} souborů sedí",
            };
            return ($"Ověřeno: {all} ({Basis(r)}).", VerifyTone.Ok);
        }
        var text = r.Changed.Count + r.Missing.Count + r.Unreadable.Count == 0
            ? $"Nelze ověřit: {Files(r.FailedAtCopy.Count)} {Texts.Plural(r.FailedAtCopy.Count, "selhal", "selhaly", "selhalo")} už při kopii a v záznamu není žádný platný součet — podrobnosti v reportu ověření."
            : $"Nesedí {Files(r.Changed.Count)}, chybí {N(r.Missing.Count)}, nečitelný {N(r.Unreadable.Count)} — podrobnosti v reportu ověření.";
        if (r.FolderGone) text += " Složka zálohy zmizela — disk se možná odpojil.";
        return (text, VerifyTone.Error);
    }

    /// Orange lines under the summary: files nobody recorded, files that failed already at the copy, chain problems.
    public static IReadOnlyList<string> Warnings(VerifyResult r)
    {
        var lines = new List<string>();
        if (r.Extra.Count > 0)
            lines.Add($"{Files(r.Extra.Count)} navíc (nejsou v {(r.Basis == "ascmhl" ? "MHL" : "seznamu součtů")}).");
        if (r.FailedAtCopy.Count > 0)
            lines.Add($"{Files(r.FailedAtCopy.Count)} {Texts.Plural(r.FailedAtCopy.Count, "selhal", "selhaly", "selhalo")} už při kopii.");
        if (r.FailedSuperseded.Count > 0)
            lines.Add($"{Files(r.FailedSuperseded.Count)} {Texts.Plural(r.FailedSuperseded.Count, "má", "mají", "má")} v nejnovější generaci záznam „selhalo při kopii“ — ověřeno proti starší platné generaci.");
        lines.AddRange(r.ChainProblems);
        return lines;
    }

    /// CLI: "Ověřeno 1 234 souborů (512 GB) za 22:10 (385 MB/s) — vše v pořádku." / "… — NALEZENY PROBLÉMY: …"
    public static string CliSummary(VerifyResult r)
    {
        double secs = (r.Finished - r.Started).TotalSeconds;
        var head = $"Ověřeno {Files(r.Ok + r.Changed.Count)} ({Format.BytesCs(r.BytesRead)}) za {JobProgress.EtaText(secs)} ({Format.Rate(r.BytesRead, secs)})";
        if (r.Cancelled) return Summary(r).Text;
        return r.Success
            ? $"{head} — vše v pořádku."
            : $"{head} — NALEZENY PROBLÉMY: nesedí {N(r.Changed.Count)}, chybí {N(r.Missing.Count)}, nečitelné {N(r.Unreadable.Count)}, selhalo při kopii {N(r.FailedAtCopy.Count)}, navíc {N(r.Extra.Count)}.";
    }

    /// Why the report is not in the card folder (an MHL history that does not ignore _REPORT would turn it into an unlisted file).
    public static string ReportOutsideNote(string path) =>
        $"Report ověření je uložený mimo složku karty, aby nenarušil její historii ASC MHL: {path}";
}

/// The verification report (spec 2.4, R8): `_REPORT\<card>_verify_<stamp>Z.html/.pdf` and `offload_verify_<stamp>Z.json`.
/// Nothing goes into ascmhl\ — _REPORT is ignored by every MHL, so a later `ascmhl verify` still passes.
public static class VerifyReport
{
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// "2026-10-06_081500" — the start of the run in UTC.
    public static string Stamp(VerifyResult r) => r.Started.ToUniversalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);

    static string CardName(VerifyResult r) => Path.GetFileName(r.CardPath.TrimEnd('\\', '/'));

    /// UTF-8 without BOM, sorted keys, ISO 8601 UTC dates — like offload_report.json.
    public static string Json(VerifyResult r)
    {
        static JsonArray Strings(IEnumerable<string> items) => new(items.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        var o = new JsonObject
        {
            ["basis"] = r.Basis,
            ["cardPath"] = r.CardPath,
            ["chainProblems"] = Strings(r.ChainProblems),
            ["changed"] = Strings(r.Changed),
            ["extra"] = Strings(r.Extra),
            ["failedAtCopy"] = Strings(r.FailedAtCopy),
            ["failedSuperseded"] = Strings(r.FailedSuperseded),
            ["finished"] = Iso(r.Finished),
            ["generations"] = r.Generations,
            ["hashAlgorithms"] = Strings(r.HashAlgorithms),
            ["missing"] = Strings(r.Missing),
            ["ok"] = r.Ok,
            ["started"] = Iso(r.Started),
            ["success"] = r.Success,
            ["tool"] = "offload",
            ["totalBytes"] = r.TotalBytes,
            ["totalFiles"] = r.TotalFiles,
            ["unreadable"] = new JsonArray(r.Unreadable.Select(u => (JsonNode?)new JsonObject { ["error"] = u.Error, ["path"] = u.Path }).ToArray()),
            ["version"] = OffloadVersion.String,
        };
        return o.ToJsonString(WriteOptions);
    }

    public static string Html(VerifyResult r, bool forPrint = false)
    {
        var (summary, tone) = VerifyTexts.Summary(r);
        var (badgeClass, badge) = tone switch
        {
            VerifyTone.Ok => ("ok", "OVĚŘENO"),
            VerifyTone.Error => ("bad", "NALEZENY PROBLÉMY"),
            _ => ("warn", "ZRUŠENO"),
        };
        var card = HtmlReport.Esc(CardName(r));
        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"cs\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Ověření {card}</title>\n<style>\n{HtmlReport.Styles(forPrint)}</style></head><body><main>\n");
        h.Append($"<header><h1>Ověření {card}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">{HtmlReport.DateTimeText(r.Started)} – {HtmlReport.DateTimeText(r.Finished)}</span></header>\n");
        h.Append($"<p class=\"{badgeClass}\">{HtmlReport.Esc(summary)}</p>\n");
        foreach (var w in VerifyTexts.Warnings(r)) h.Append($"<p class=\"warn\">{HtmlReport.Esc(w)}</p>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, r.TotalFiles.ToString(CultureInfo.InvariantCulture), "souborů v záznamu");
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, r.Ok.ToString(CultureInfo.InvariantCulture), "sedí");
        Stat(h, HtmlReport.Esc(VerifyTexts.Basis(r)), "porovnáno s");
        Stat(h, HtmlReport.Duration((r.Finished - r.Started).TotalSeconds), "ověření · " + Format.Rate(r.BytesRead, (r.Finished - r.Started).TotalSeconds));
        h.Append("</div>\n");
        h.Append($"<p class=\"sub\">Složka: <span class=\"mono\">{HtmlReport.Esc(r.CardPath)}</span></p>\n");
        Section(h, "Nesedí (obsah se liší od záznamu)", "bad", r.Changed);
        Section(h, "Chybí", "bad", r.Missing);
        Section(h, "Nečitelné", "bad", r.Unreadable.Select(u => $"{u.Path} — {u.Error}").ToList());
        Section(h, "Selhaly už při kopii (bez platného záznamu)", "bad", r.FailedAtCopy);
        Section(h, "Nejnovější záznam selhal při kopii (ověřeno proti starší generaci)", "warn", r.FailedSuperseded);
        Section(h, r.Basis == "ascmhl" ? "Navíc (nejsou v MHL)" : "Navíc (nejsou v seznamu součtů)", "warn", r.Extra);
        Section(h, "Řetěz ASC MHL", "warn", r.ChainProblems);
        h.Append($"<footer>offload {HtmlReport.Esc(OffloadVersion.String)} · report ověření · {HtmlReport.DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
        return h.ToString();
    }

    static void Stat(StringBuilder h, string value, string label) =>
        h.Append($"<div class=\"stat\"><b>{value}</b><span>{label}</span></div>\n");

    static void Section(StringBuilder h, string title, string cls, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        h.Append($"<h2 class=\"{cls}\">{HtmlReport.Esc(title)} ({lines.Count})</h2><div class=\"panel\"><table>\n");
        foreach (var l in lines) h.Append($"<tr><td class=\"mono wrap\">{HtmlReport.Esc(l)}</td></tr>\n");
        h.Append("</table></div>\n");
    }

    /// `<card>\_REPORT`, or — when the card's MHL history does not ignore _REPORT — `<parent>\_REPORT` (never into the card).
    public static string ReportDir(VerifyResult r)
    {
        var card = r.CardPath.TrimEnd('\\', '/');
        var parent = r.ReportInsideCard ? null : Path.GetDirectoryName(card);
        return Path.Combine(parent ?? card, Layout.ReportDirName);
    }

    /// Writes JSON, HTML and (best effort, logged) PDF into `<card>\_REPORT\`. Returns the HTML path. Not for a cancelled run.
    public static string Write(VerifyResult r, Action<string>? log = null)
    {
        var dir = ReportDir(r);
        NativeIO.Mkdirs(dir);
        var stamp = Stamp(r);
        var card = CardName(r);
        // Beside the card folder the day-level _REPORT holds several cards' reports, so the JSON carries the card name too.
        var jsonName = r.ReportInsideCard ? $"offload_verify_{stamp}Z.json" : $"{card}_verify_{stamp}Z.json";
        NativeIO.WriteFileDurably(Path.Combine(dir, jsonName), new UTF8Encoding(false).GetBytes(Json(r)));
        var html = Path.Combine(dir, $"{card}_verify_{stamp}Z.html");
        NativeIO.WriteFileDurably(html, new UTF8Encoding(false).GetBytes(Html(r)));
        try
        {
            PdfReport.WriteHtml(Html(r, forPrint: true), Path.Combine(dir, $"{card}_verify_{stamp}Z.pdf"));
        }
        catch (Exception e)
        {
            log?.Invoke($"verify PDF {card}: {e.Message}");
        }
        return html;
    }
}
