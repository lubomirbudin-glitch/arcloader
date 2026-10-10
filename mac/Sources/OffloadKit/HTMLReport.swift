import Foundation

/// Self-contained HTML report (thumbnails embedded) — can be emailed or opened straight from the disk.
public enum HTMLReport {
    static func esc(_ s: String) -> String {
        var o = ""
        o.reserveCapacity(s.count)
        for c in s {
            switch c {
            case "&": o += "&amp;"
            case "<": o += "&lt;"
            case ">": o += "&gt;"
            case "\"": o += "&quot;"
            default: o.append(c)
            }
        }
        return o
    }

    /// "Oct 10, 2026 14:05:12" — English month, 24-hour time, local time zone.
    static func dateTime(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "MMM d, yyyy HH:mm:ss"
        return f.string(from: d)
    }

    static func timeOnly(_ d: Date) -> String {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f.string(from: d)
    }

    static func duration(_ s: Double) -> String {
        let t = Int(s.rounded())
        return t >= 3600 ? String(format: "%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60) : String(format: "%d:%02d", t / 60, t % 60)
    }

    static let stateLabel: [String: String] = [
        "verified": "verified", "written": "written", "failed": "FAILED", "pending": "pending",
    ]

    static func proxyLabel(_ c: ClipEntry) -> (String, String) {
        switch c.proxyStatus {
        case .done: return ("ok", "done")
        case .failed: return ("bad", "failed")
        case .unsupported: return ("warn", "unsupported")
        case .skipped: return ("muted", "skipped")
        case .none: return ("muted", "—")
        }
    }

    /// `forPrint`: light colours only and the full file list expanded (for the PDF).
    static let darkCSS = "@media (prefers-color-scheme:dark){:root{--bg:#141416;--card:#1e1e21;--fg:#ececee;--muted:#9a9aa0;--line:#2e2e33;--ok:#4cc27c;--okbg:#14301f;--bad:#ff6b6b;--badbg:#3a1717;--warn:#f0b04a;--warnbg:#382a10;--accent:#8aa2ff}}"
    /// Print (PDF) layout: everything must fit the A4 landscape width — cells wrap instead of the panel scrolling,
    /// smaller thumbnails and type. PDFReport additionally zooms the page out if anything is still wider.
    static let printCSS = "body{background:#fff;font-size:10px}main{padding:0 8px 0 2px;max-width:none}"
        + ".panel{overflow:visible}table{width:100%;table-layout:auto}"
        + "th,td{padding:3px 5px;white-space:normal;overflow-wrap:break-word;font-size:9.5px}"
        + "td.clip{min-width:0;max-width:none}td.mono,.mono,code,.path{font-size:8.5px;overflow-wrap:anywhere}"
        + "tr{break-inside:avoid}h2{break-after:avoid}.stat b{font-size:16px}"
        + ".thumb,.nothumb{width:64px;height:36px}.thumbs{gap:2px}.thumbs figure img{width:64px;height:36px}"
        + ".thumbs figcaption{font-size:7.5px}td.num,td.tc{white-space:nowrap;overflow-wrap:normal}"
        + "td.clip div{font-size:8.5px}td.clip .clipname{font-size:10px}"

    public static func render(_ r: OffloadReport, forPrint: Bool = false) -> String {
        let ok = r.success
        let status = !ok ? "FAILED" : (r.verified ? "VERIFIED" : "COPIED — NOT VERIFIED")
        let statusClass = !ok ? "bad" : (r.verified ? "ok" : "warn")
        let failed = r.failedFiles
        let videoClips = r.clips.filter { !$0.clip.isCameraProxy }
        let totalDuration = videoClips.compactMap { $0.info?.duration }.reduce(0, +)

        var h = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Offload \(esc(r.cardName)) – \(esc(r.date))</title>
        <style>
        :root{--bg:#f6f6f4;--card:#fff;--fg:#1d1d1f;--muted:#6e6e73;--line:#e3e3e0;--ok:#1f8a4c;--okbg:#e5f4ea;--bad:#c62828;--badbg:#fdeaea;--warn:#a86400;--warnbg:#fdf2dc;--accent:#3a5bd9}
        \(forPrint ? "" : darkCSS)
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 -apple-system,BlinkMacSystemFont,"Helvetica Neue",sans-serif}
        main{max-width:1600px;margin:0 auto;padding:28px 20px 60px}
        header{display:flex;flex-wrap:wrap;align-items:center;gap:14px;margin-bottom:22px}
        h1{font-size:26px;margin:0;letter-spacing:-.01em}h2{font-size:16px;margin:30px 0 10px}
        .sub{color:var(--muted)}.badge{display:inline-block;padding:4px 12px;border-radius:999px;font-weight:600;font-size:13px}
        .ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}.muted{color:var(--muted)}
        .badge.ok{background:var(--okbg)}.badge.bad{background:var(--badbg)}.badge.warn{background:var(--warnbg)}
        .stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}
        .stat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px}
        .stat b{display:block;font-size:20px;font-variant-numeric:tabular-nums}.stat span{color:var(--muted);font-size:12px}
        .panel{background:var(--card);border:1px solid var(--line);border-radius:10px;overflow-x:auto}
        table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:7px 8px;border-bottom:1px solid var(--line);vertical-align:middle;white-space:nowrap}
        th{font-size:12px;color:var(--muted);font-weight:600}tr:last-child td{border-bottom:0}
        td.num{text-align:right;font-variant-numeric:tabular-nums}code,.mono{font:12px ui-monospace,Menlo,monospace}
        td.clip{white-space:normal;min-width:160px;max-width:280px}.wrap{white-space:normal}td.fmt{max-width:150px}
        td.clip .clipname,td.clip .path,td.clip .note{overflow-wrap:anywhere}
        .thumb{width:104px;height:58px;object-fit:contain;background:#000;border-radius:4px;display:block}
        .nothumb{width:104px;height:58px;border-radius:4px;background:var(--line)}
        .thumbs{display:flex;gap:4px}.thumbs figure{margin:0}.thumbs figcaption{font:10px ui-monospace,Menlo,monospace;color:var(--muted);text-align:center}
        .clipname{font-weight:600}.path,.note{color:var(--muted);font-size:12px}
        details summary{cursor:pointer;padding:10px 0;font-weight:600}
        .err{white-space:normal;color:var(--bad)}
        footer{margin-top:40px;color:var(--muted);font-size:12px}
        @media print{body{background:#fff}.panel,.stat{border-color:#ccc}details{display:block}}
        \(forPrint ? printCSS : "")
        </style></head><body><main>
        <header><h1>\(esc(r.cardName))</h1><span class="badge \(statusClass)">\(status)</span>
        <span class="sub">\(dateTime(r.started)) – \(timeOnly(r.finished))</span></header>
        <div class="stats">
        <div class="stat"><b>\(r.totalFiles)</b><span>\(r.totalFiles == 1 ? "file" : "files")</span></div>
        <div class="stat"><b>\(formatBytes(r.totalBytes))</b><span>data</span></div>
        <div class="stat"><b>\(videoClips.count)</b><span>\(videoClips.count == 1 ? "clip" : "clips")\(totalDuration > 0 ? " · " + duration(totalDuration) : "")</span></div>
        <div class="stat"><b>\(duration(r.copySeconds))</b><span>copy · \(formatRate(r.totalBytes, r.copySeconds))</span></div>
        <div class="stat"><b>\(r.verified ? duration(r.verifySeconds) : "—")</b><span>verification · \(r.verified ? formatRate(r.totalBytes, r.verifySeconds) : "not run")</span></div>
        <div class="stat"><b>\(esc(r.hashAlgorithm.uppercased()))</b><span>checksum</span></div>
        </div>

        <h2>Destinations</h2><div class="panel"><table>
        <tr><th>Drive</th><th>Folder</th><th>FS</th><th class="num">OK</th><th class="num">Errors</th></tr>

        """
        for d in r.destinations {
            h += "<tr><td>\(esc((d.root as NSString).lastPathComponent))</td><td class=\"mono wrap\">\(esc(d.cardPath))</td><td>\(esc(d.filesystem))</td>"
            h += "<td class=\"num ok\">\(d.filesOK)</td><td class=\"num \(d.filesFailed > 0 ? "bad" : "muted")\">\(d.filesFailed)</td></tr>\n"
        }
        h += "</table></div>\n"
        h += "<p class=\"sub\">Source: <span class=\"mono\">\(esc(r.source))</span> (\(esc(r.sourceFilesystem)))</p>\n"

        if !failed.isEmpty {
            h += "<h2 class=\"bad\">Errors (\(failed.count))</h2><div class=\"panel\"><table><tr><th>File</th><th>Problem</th></tr>\n"
            for f in failed {
                var msgs: [String] = []
                if let e = f.sourceError { msgs.append("card: " + e) }
                for (i, e) in f.errors.enumerated() { if let e { msgs.append("destination \(i + 1): " + e) } }
                h += "<tr><td class=\"mono\">\(esc(f.path))</td><td class=\"err\">\(esc(msgs.joined(separator: " · ")))</td></tr>\n"
            }
            h += "</table></div>\n"
        }

        if !r.clips.isEmpty {
            h += "<h2>Clips</h2><div class=\"panel\"><table>\n"
            h += "<tr><th></th><th>Clip</th><th>Format</th><th>Resolution</th><th class=\"num\">FPS</th><th class=\"num\">Duration</th><th>TC</th><th class=\"num\">Audio</th><th class=\"num\">Size</th><th>Proxy</th></tr>\n"
            for c in r.clips {
                let img = c.thumbnails.isEmpty ? "<div class=\"nothumb\"></div>"
                    : "<div class=\"thumbs\">" + c.thumbnails.map { t in
                        "<figure><img class=\"thumb\" src=\"data:image/jpeg;base64,\(t.jpeg.base64EncodedString())\" alt=\"\"><figcaption>\(esc(t.label))</figcaption></figure>"
                    }.joined() + "</div>"
                let i = c.info
                var codecParts: [String] = []
                if c.clip.format != .video { codecParts.append(c.clip.format.label) }
                if let codec = i?.codec, !codecParts.contains(codec) { codecParts.append(codec) }
                let codec = codecParts.joined(separator: " · ")
                let (pc, pl) = proxyLabel(c)
                var notes: [String] = []
                for n in [c.probeError, c.proxyNote].compactMap({ $0 }) where !notes.contains(n) { notes.append(n) }
                let note = notes.joined(separator: " · ")
                var noteHTML = note.isEmpty ? "" : "<div class=\"path err\">\(esc(note))</div>"
                // Which LUT and why — also "no LUT (reason)", unless the user switched LUTs off.
                if let lu = c.lut, lu.source != "none" {
                    noteHTML += "<div class=\"note\">LUT: \(esc(lu.name)) (\(esc(lu.reason)))</div>"
                } else if let ln = c.lut, !LutUse.isOffInSettings(ln.reason) {
                    noteHTML += "<div class=\"note\">LUT: no LUT (\(esc(ln.reason)))</div>"
                }
                h += "<tr><td>\(img)</td><td class=\"clip\"><div class=\"clipname\">\(esc(c.clip.name))\(c.clip.isCameraProxy ? " <span class=\"muted\">(camera proxy)</span>" : "")</div>"
                h += "<div class=\"path\">\(esc(c.clip.path))</div>\(noteHTML)</td>"
                h += "<td class=\"wrap fmt\">\(esc(codec))</td><td>\(esc(i?.resolutionLabel ?? ""))</td><td class=\"num\">\(esc(i?.fpsLabel ?? ""))</td>"
                h += "<td class=\"num\">\(esc(i?.durationLabel ?? ""))</td><td class=\"mono tc\">\(esc(i?.timecode ?? ""))</td>"
                h += "<td class=\"num\">\(i.map { $0.audioChannels > 0 ? "\($0.audioChannels) ch" : "—" } ?? "")</td>"
                h += "<td class=\"num\">\(formatBytes(c.clip.bytes))</td><td class=\"\(pc)\">\(pl)</td></tr>\n"
            }
            h += "</table></div>\n"
            if let p = r.proxy, p.enabled {
                let lut: String
                switch LutResolver.effectiveMode(p) {
                case .auto: lut = "auto by camera"
                case .builtin: lut = LutCatalog.shared.find(p.lutBuiltin)?.name ?? p.lutBuiltin ?? "no LUT"
                case .custom: lut = p.lutPath.flatMap { $0.isEmpty ? nil : ($0 as NSString).lastPathComponent } ?? "no LUT"
                case .off: lut = "no LUT"
                }
                let frame = p.frameLabel.map { ", frame lines \($0)" } ?? ""
                let extra = p.extraFolder.map { " · also in folder <span class=\"mono\">\(esc($0))</span>" } ?? ""
                h += "<p class=\"sub\">Proxy: \(esc(p.codec.label)), \(p.height)p\(frame), LUT: \(esc(lut)) → <span class=\"mono\">\(Layout.proxyDirName)/</span>\(extra)</p>\n"
            }
        }

        h += "<details\(forPrint ? " open" : "")><summary>All files (\(r.files.count))</summary><div class=\"panel\"><table>\n"
        h += "<tr><th>File</th><th class=\"num\">Size</th><th>\(esc(r.hashAlgorithm))</th>"
        for i in r.destinations.indices { h += "<th>Destination \(i + 1)</th>" }
        h += "</tr>\n"
        for f in r.files {
            h += "<tr><td class=\"mono\">\(esc(f.path))</td><td class=\"num\">\(formatBytes(f.size))</td><td class=\"mono\">\(esc(f.xxh64 ?? "—"))</td>"
            for s in f.destinations {
                let cls = s == "verified" ? "ok" : (s == "failed" ? "bad" : "warn")
                h += "<td class=\"\(cls)\">\(stateLabel[s] ?? s)</td>"
            }
            h += "</tr>\n"
        }
        h += "</table></div></details>\n"
        if !r.skipped.isEmpty {
            h += "<p class=\"sub\">Skipped system files: \(esc(r.skipped.joined(separator: ", ")))</p>\n"
        }
        h += "<footer>\(esc(r.tool)) \(esc(r.version)) · ASC MHL in ascmhl/ · \(dateTime(Date()))</footer></main></body></html>\n"
        return h
    }
}
