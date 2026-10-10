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
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static string N(long n) => n.ToString("N0", Inv);
    static string Files(int n) => $"{N(n)} {Texts.Plural(n, "file", "files")}";

    /// "ASC MHL, 2 generations, xxHash64" / "checksum list, xxHash64 + MD5".
    public static string Basis(VerifyResult r)
    {
        var algos = string.Join(" + ", r.HashAlgorithms.Select(HashAlgo.Label));
        if (r.Basis == BackupVerifier.PendingBasis) return "the record of the unfinished generation is not written yet";
        return r.Basis == "ascmhl"
            ? $"ASC MHL, {Texts.N(r.Generations, "generation")}, {algos}"
            : $"checksum list, {algos}";
    }

    /// Plan 10 (M-c): what an unfinished newest generation (VerifyResult.Unfinished) means and what to do.
    public const string UnfinishedText =
        "The backup is not finished: the newest generation was being verified with the proxies and the verification did not end (or its result is not written on all destinations). Do not format the card yet — insert it again and choose \"Finish interrupted backup\".";

    /// The main line, green / red / orange (cancelled, or a generation not finished — M-c: never "all good" then).
    public static (string Text, VerifyTone Tone) Summary(VerifyResult r)
    {
        if (r.Cancelled)
            return ($"Verification cancelled after {N(r.Checked)} / {N(r.TotalFiles)} {(r.TotalFiles == 1 ? "file" : "files")}", VerifyTone.Warning);
        if (r.Success && r.Unfinished)
            return (r.Ok > 0 ? $"{UnfinishedText} The files with a written record match ({N(r.Ok)}, {Basis(r)})." : UnfinishedText, VerifyTone.Warning);
        if (r.Success)
        {
            var all = r.Ok switch
            {
                1 => "the file matches",
                >= 2 and <= 4 => $"all {r.Ok} files match",
                _ => $"all {N(r.Ok)} files match",
            };
            return ($"Verified: {all} ({Basis(r)}).", VerifyTone.Ok);
        }
        var text = r.Changed.Count + r.Missing.Count + r.Unreadable.Count == 0
            ? $"Cannot verify: {Files(r.FailedAtCopy.Count)} already failed during the copy and the record has no valid checksum — details in the verification report."
            : $"Mismatch {Files(r.Changed.Count)}, missing {N(r.Missing.Count)}, unreadable {N(r.Unreadable.Count)} — details in the verification report.";
        if (r.FolderGone) text += " The backup folder disappeared — the disk may have been disconnected.";
        if (r.Unfinished) text += " " + UnfinishedText;
        return (text, VerifyTone.Error);
    }

    /// Orange lines under the summary: files nobody recorded, files that failed already at the copy, chain problems.
    public static IReadOnlyList<string> Warnings(VerifyResult r)
    {
        var lines = new List<string>();
        if (r.Extra.Count > 0)
            lines.Add($"{Files(r.Extra.Count)} extra (not in the {(r.Basis == "ascmhl" ? "MHL" : "checksum list")}).");
        if (r.FailedAtCopy.Count > 0)
            lines.Add($"{Files(r.FailedAtCopy.Count)} already failed during the copy.");
        if (r.FailedSuperseded.Count > 0)
            lines.Add($"{Files(r.FailedSuperseded.Count)} {(r.FailedSuperseded.Count == 1 ? "has" : "have")} the record \"failed during the copy\" in the newest generation — verified against an older valid generation.");
        lines.AddRange(r.ChainProblems);
        return lines;
    }

    /// CLI: "Verified 1,234 files (512 GB) in 22:10 (385 MB/s) — all OK." / "… — PROBLEMS FOUND: …"
    public static string CliSummary(VerifyResult r)
    {
        double secs = (r.Finished - r.Started).TotalSeconds;
        var head = $"Verified {Files(r.Ok + r.Changed.Count)} ({Format.BytesShort(r.BytesRead)}) in {JobProgress.EtaText(secs)} ({Format.Rate(r.BytesRead, secs)})";
        if (r.Cancelled) return Summary(r).Text;
        if (r.Success && r.Unfinished) return $"{head} — BACKUP UNFINISHED: {UnfinishedText}";
        return r.Success
            ? $"{head} — all OK."
            : $"{head} — PROBLEMS FOUND: mismatch {N(r.Changed.Count)}, missing {N(r.Missing.Count)}, unreadable {N(r.Unreadable.Count)}, failed during the copy {N(r.FailedAtCopy.Count)}, extra {N(r.Extra.Count)}.";
    }

    /// Why the report is not in the card folder (an MHL history that does not ignore _REPORT would turn it into an unlisted file).
    public static string ReportOutsideNote(string path) =>
        $"The verification report is saved outside the card folder so it does not disturb its ASC MHL history: {path}";
}

/// The verification report (spec 2.4, R8): `_REPORT\<card>_verify_<stamp>Z.html/.pdf` and `offload_verify_<stamp>Z.json`.
/// Nothing goes into ascmhl\ — _REPORT is ignored by every MHL, so a later `ascmhl verify` still passes.
public static class VerifyReport
{
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// "2026-10-06_081500" — the start of the run in UTC.
    public static string PdfName(VerifyResult r) => $"{CardName(r)}_verify_{Stamp(r)}Z.pdf";

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
            ["unfinished"] = r.Unfinished,
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
            VerifyTone.Ok => ("ok", "VERIFIED"),
            VerifyTone.Error => ("bad", "PROBLEMS FOUND"),
            _ when r.Cancelled => ("warn", "CANCELLED"),
            _ => ("warn", "UNFINISHED"),
        };
        var card = HtmlReport.Esc(CardName(r));
        var h = new StringBuilder();
        h.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        h.Append($"<title>Verification {card}</title>\n<style>\n{HtmlReport.Styles(forPrint)}</style></head><body><main>\n");
        h.Append(HtmlReport.Toolbar(PdfName(r), forPrint));
        h.Append($"<header><h1>Verification {card}</h1><span class=\"badge {badgeClass}\">{badge}</span>\n");
        h.Append($"<span class=\"sub\">{HtmlReport.DateTimeText(r.Started)} – {HtmlReport.DateTimeText(r.Finished)}</span></header>\n");
        h.Append($"<p class=\"{badgeClass}\">{HtmlReport.Esc(summary)}</p>\n");
        foreach (var w in VerifyTexts.Warnings(r)) h.Append($"<p class=\"warn\">{HtmlReport.Esc(w)}</p>\n");
        h.Append("<div class=\"stats\">\n");
        Stat(h, r.TotalFiles.ToString(CultureInfo.InvariantCulture), "files in the record");
        Stat(h, Format.Bytes(r.TotalBytes), "data");
        Stat(h, r.Ok.ToString(CultureInfo.InvariantCulture), "match");
        Stat(h, HtmlReport.Esc(VerifyTexts.Basis(r)), "compared with");
        Stat(h, HtmlReport.Duration((r.Finished - r.Started).TotalSeconds), "verification · " + Format.Rate(r.BytesRead, (r.Finished - r.Started).TotalSeconds));
        h.Append("</div>\n");
        h.Append($"<p class=\"sub\">Folder: <span class=\"mono\">{HtmlReport.Esc(r.CardPath)}</span></p>\n");
        Section(h, "Mismatch (the content differs from the record)", "bad", r.Changed);
        Section(h, "Missing", "bad", r.Missing);
        Section(h, "Unreadable", "bad", r.Unreadable.Select(u => $"{u.Path} — {u.Error}").ToList());
        Section(h, "Failed already during the copy (no valid record)", "bad", r.FailedAtCopy);
        Section(h, "The newest record failed during the copy (verified against an older generation)", "warn", r.FailedSuperseded);
        Section(h, r.Basis == "ascmhl" ? "Extra (not in the MHL)" : "Extra (not in the checksum list)", "warn", r.Extra);
        Section(h, "ASC MHL chain", "warn", r.ChainProblems);
        h.Append($"<footer>offload {HtmlReport.Esc(OffloadVersion.String)} · verification report · {HtmlReport.DateTimeText(DateTime.Now)}</footer></main></body></html>\n");
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
            PdfReport.WriteHtml(Html(r, forPrint: true), Path.Combine(dir, PdfName(r)));
        }
        catch (Exception e)
        {
            log?.Invoke($"verify PDF {card}: {e.Message}");
        }
        return html;
    }
}
