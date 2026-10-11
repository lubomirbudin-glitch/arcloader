import Foundation

/// The texts and the report of a verification (FORMATY „Report ověření“) — the English wording of Windows
/// `VerifyTexts` / `VerifyReport`.
public enum VerifyReport {
    /// "1,234" — the same on every Mac (en_US_POSIX grouping).
    static func n(_ v: Int) -> String {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.numberStyle = .decimal
        f.usesGroupingSeparator = true
        f.groupingSeparator = ","
        f.groupingSize = 3
        return f.string(from: NSNumber(value: v)) ?? "\(v)"
    }

    static func files(_ count: Int) -> String { "\(n(count)) \(Texts.plural(count, "file", "files"))" }

    /// Plan 10 (M-c): what an unfinished newest generation means and what to do.
    public static let unfinishedText =
        "The backup is not finished: the newest generation was being verified with the proxies and the verification did not end (or its result is not written on all destinations). Do not format the card yet — insert it again and choose \"Finish interrupted backup\"."

    /// "ASC MHL, 2 generations, xxHash64 + xxHash128" / "checksum list, xxHash64 + MD5".
    public static func basis(_ r: VerifyResult) -> String {
        let algos = r.hashAlgorithms.map(HashAlgo.label).joined(separator: " + ")
        if r.basis == BackupVerifier.pendingBasis { return "the record of the unfinished generation is not written yet" }
        return r.basis == "ascmhl"
            ? "ASC MHL, \(r.generations) \(Texts.plural(r.generations, "generation", "generations")), \(algos)"
            : "checksum list, \(algos)"
    }

    /// The main line: green / red / orange (cancelled, or a generation not finished — never "all good" then).
    public static func summary(_ r: VerifyResult) -> (text: String, tone: CardTone) {
        if r.cancelled {
            return ("Verification cancelled after \(n(r.checked)) / \(n(r.totalFiles)) \(r.totalFiles == 1 ? "file" : "files")", .warning)
        }
        if r.success && r.unfinished {
            return (r.ok > 0 ? "\(unfinishedText) The files with a written record match (\(n(r.ok)), \(basis(r)))." : unfinishedText, .warning)
        }
        if r.success {
            let all: String
            switch r.ok {
            case 1: all = "the file matches"
            default: all = "all \(n(r.ok)) files match"
            }
            return ("Verified: \(all) (\(basis(r))).", .ok)
        }
        var text = r.changed.count + r.missing.count + r.unreadable.count == 0
            ? "Cannot verify: \(files(r.failedAtCopy.count)) already failed during the copy and the record has no valid checksum — details in the verification report."
            : "Mismatch \(files(r.changed.count)), missing \(n(r.missing.count)), unreadable \(n(r.unreadable.count)) — details in the verification report."
        if r.folderGone { text += " The backup folder disappeared — the disk may have been disconnected." }
        if r.unfinished { text += " " + unfinishedText }
        return (text, .error)
    }

    /// Orange lines under the summary: files nobody recorded, files that failed already at the copy, chain problems.
    public static func warnings(_ r: VerifyResult) -> [String] {
        var lines: [String] = []
        if !r.extra.isEmpty { lines.append("\(files(r.extra.count)) extra (not in the \(r.basis == "ascmhl" ? "MHL" : "checksum list")).") }
        if !r.failedAtCopy.isEmpty { lines.append("\(files(r.failedAtCopy.count)) already failed during the copy.") }
        if !r.failedSuperseded.isEmpty {
            lines.append("\(files(r.failedSuperseded.count)) \(r.failedSuperseded.count == 1 ? "has" : "have") the record \"failed during the copy\" in the newest generation — verified against an older valid generation.")
        }
        return lines + r.chainProblems
    }

    /// CLI: "Verified 1,234 files (512 GB) in 22:10 (385 MB/s) — all OK." / "… — PROBLEMS FOUND: …"
    public static func cliSummary(_ r: VerifyResult, seconds: Double) -> String {
        if r.cancelled { return summary(r).text }
        let head = "Verified \(files(r.ok + r.changed.count)) (\(formatBytesShort(r.bytesRead))) in \(JobProgress.etaText(seconds)) (\(formatRate(r.bytesRead, seconds)))"
        if r.success && r.unfinished { return "\(head) — BACKUP UNFINISHED: \(unfinishedText)" }
        return r.success
            ? "\(head) — all OK."
            : "\(head) — PROBLEMS FOUND: mismatch \(n(r.changed.count)), missing \(n(r.missing.count)), unreadable \(n(r.unreadable.count)), failed during the copy \(n(r.failedAtCopy.count)), extra \(n(r.extra.count))."
    }

    /// Why the report is not in the card folder (an MHL history that does not ignore _REPORT would turn it into an
    /// unlisted file).
    public static func reportOutsideNote(_ path: String) -> String {
        "The verification report is saved outside the card folder so it does not disturb its ASC MHL history: \(path)"
    }

    /// The machine lines of the CLI (English, for scripts): MISMATCH, MISSING, ERROR, EXTRA, FAILED-AT-COPY …
    public static func machineLines(_ r: VerifyResult) -> [String] {
        r.changed.map { "MISMATCH  \($0)" } + r.missing.map { "MISSING   \($0)" }
            + r.unreadable.map { "ERROR     \($0.path): \($0.error)" } + r.extra.map { "EXTRA     \($0)" }
            + r.failedAtCopy.map { "FAILED-AT-COPY  \($0)" } + r.failedSuperseded.map { "FAILED-SUPERSEDED  \($0)" }
            + r.chainProblems.map { "CHAIN     \($0)" }
    }

    // MARK: - The report

    static func iso(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'"
        return f.string(from: d)
    }

    /// "2026-10-06_081500" — the start of the run in UTC.
    public static func stamp(_ r: VerifyResult) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd_HHmmss"
        return f.string(from: r.started)
    }

    static func cardName(_ r: VerifyResult) -> String { (r.cardPath.trimmingTrailingSlash() as NSString).lastPathComponent }

    /// UTF-8 without BOM, sorted keys, ISO 8601 UTC dates — like offload_report.json.
    public static func jsonData(_ r: VerifyResult) throws -> Data {
        let o: [String: Any] = [
            "basis": r.basis,
            "cardPath": r.cardPath,
            "chainProblems": r.chainProblems,
            "changed": r.changed,
            "extra": r.extra,
            "failedAtCopy": r.failedAtCopy,
            "failedSuperseded": r.failedSuperseded,
            "finished": iso(r.finished),
            "generations": r.generations,
            "hashAlgorithms": r.hashAlgorithms,
            "missing": r.missing,
            "ok": r.ok,
            "started": iso(r.started),
            "success": r.success,
            "tool": "offload",
            "totalBytes": r.totalBytes,
            "totalFiles": r.totalFiles,
            "unfinished": r.unfinished,
            "unreadable": r.unreadable.map { ["error": $0.error, "path": $0.path] },
            "version": OffloadVersion.string,
        ]
        return try JSONSerialization.data(withJSONObject: o, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
    }

    public static func html(_ r: VerifyResult, forPrint: Bool = false) -> String {
        let esc = HTMLReport.esc
        let (text, tone) = summary(r)
        let (badgeClass, badge): (String, String) = tone == .ok ? ("ok", "VERIFIED") : tone == .error ? ("bad", "PROBLEMS FOUND")
            : r.cancelled ? ("warn", "CANCELLED") : ("warn", "UNFINISHED")
        let card = esc(cardName(r))
        let seconds = r.finished.timeIntervalSince(r.started)
        var h = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Verification \(card)</title>
        <style>
        \(HTMLReport.styles(forPrint: forPrint))
        </style></head><body><main>
        <header><h1>Verification \(card)</h1><span class="badge \(badgeClass)">\(badge)</span>
        <span class="sub">\(HTMLReport.dateTime(r.started)) – \(HTMLReport.dateTime(r.finished))</span></header>
        <p class="\(badgeClass)">\(esc(text))</p>

        """
        for w in warnings(r) { h += "<p class=\"warn\">\(esc(w))</p>\n" }
        func stat(_ value: String, _ label: String) { h += "<div class=\"stat\"><b>\(value)</b><span>\(label)</span></div>\n" }
        h += "<div class=\"stats\">\n"
        stat(String(r.totalFiles), "files in the record")
        stat(formatBytes(r.totalBytes), "data")
        stat(String(r.ok), "match")
        stat(esc(basis(r)), "compared with")
        stat(HTMLReport.duration(seconds), "verification · " + formatRate(r.bytesRead, seconds))
        h += "</div>\n"
        h += "<p class=\"sub\">Folder: <span class=\"mono\">\(esc(r.cardPath))</span></p>\n"
        func section(_ title: String, _ cls: String, _ lines: [String]) {
            guard !lines.isEmpty else { return }
            h += "<h2 class=\"\(cls)\">\(esc(title)) (\(lines.count))</h2><div class=\"panel\"><table>\n"
            for l in lines { h += "<tr><td class=\"mono wrap\">\(esc(l))</td></tr>\n" }
            h += "</table></div>\n"
        }
        section("Mismatch (the content differs from the record)", "bad", r.changed)
        section("Missing", "bad", r.missing)
        section("Unreadable", "bad", r.unreadable.map { "\($0.path) — \($0.error)" })
        section("Failed already during the copy (no valid record)", "bad", r.failedAtCopy)
        section("The newest record failed during the copy (verified against an older generation)", "warn", r.failedSuperseded)
        section(r.basis == "ascmhl" ? "Extra (not in the MHL)" : "Extra (not in the checksum list)", "warn", r.extra)
        section("ASC MHL chain", "warn", r.chainProblems)
        h += "<footer>offload \(esc(OffloadVersion.string)) · verification report · \(HTMLReport.dateTime(Date()))</footer></main></body></html>\n"
        return h
    }

    /// `<card>/_REPORT`, or — when the card's MHL history does not ignore _REPORT — `<date folder>/_REPORT` (never into
    /// the card then).
    public static func reportDir(_ r: VerifyResult) -> String {
        let card = r.cardPath.trimmingTrailingSlash()
        let base = r.reportInsideCard ? card : (card as NSString).deletingLastPathComponent
        return base + "/" + Layout.reportDirName
    }

    /// Writes JSON, HTML (new files only — nothing is replaced) and, best effort, the PDF. Not for a cancelled run.
    /// Returns the HTML and JSON paths and whether they are inside the card folder.
    @discardableResult
    public static func write(_ r: VerifyResult, pdf: Bool = true, log: ((String) -> Void)? = nil) throws -> (html: String, json: String, inside: Bool) {
        if r.cancelled { throw OffloadError.failed("A cancelled verification writes no report.") }
        let dir = reportDir(r)
        try IO.mkdirs(dir)
        let s = stamp(r), card = cardName(r)
        // Beside the card folder the date folder's _REPORT holds several cards' reports: the JSON carries the card name.
        let json = dir + "/" + (r.reportInsideCard ? "offload_verify_\(s)Z.json" : "\(card)_verify_\(s)Z.json")
        let html = dir + "/" + "\(card)_verify_\(s)Z.html"
        try IO.writeFileNew(json, jsonData(r))
        try IO.writeFileNew(html, Data(VerifyReport.html(r).utf8))
        if pdf {
            let pdfPath = dir + "/" + "\(card)_verify_\(s)Z.pdf"
            if !FileManager.default.fileExists(atPath: pdfPath) {
                do { try PDFReport.writeHTML(VerifyReport.html(r, forPrint: true), to: pdfPath) } catch { log?("verify PDF \(card): \(error)") }
            }
        }
        return (html, json, r.reportInsideCard)
    }
}
