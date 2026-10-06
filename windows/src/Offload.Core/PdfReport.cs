using System.Text;

namespace Offload.Core;

/// Prints the HTML report into an A4 landscape PDF with Microsoft Edge in headless mode — the same engine
/// as WebView2, present on every Windows 10/11, no window and no extra package. Page size and margins come
/// from the report's @page rule.
public static class PdfReport
{
    public static string? FindEdge()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// `replace`: false for an append's `_NNNN` PDF — it never replaces an existing file (ruling I1).
    public static void Write(OffloadReport report, string path, int timeoutSeconds = 60, bool replace = true) =>
        WriteHtml(HtmlReport.Render(report, forPrint: true), path, timeoutSeconds, replace);

    /// Any self-contained HTML page (the offload report, the verification report) printed to `path`.
    public static void WriteHtml(string html, string path, int timeoutSeconds = 60, bool replace = true)
    {
        var edge = FindEdge() ?? throw new ToolError("Edge", -1, "Microsoft Edge not found — PDF skipped");
        var work = Path.Combine(Path.GetTempPath(), "offload-pdf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".tmp");
        try
        {
            // A file, not a data URL: the report with thumbnails is easily several MB.
            var page = Path.Combine(work, "report.html");
            File.WriteAllText(page, html, new UTF8Encoding(false));
            NativeIO.Delete(tmp);
            using var p = new ToolProcess(edge,
            [
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                $"--user-data-dir={Path.Combine(work, "profile")}",  // never touches the user's own Edge profile
                "--no-pdf-header-footer", $"--print-to-pdf={tmp}", new Uri(page).AbsoluteUri,
            ], work, pipeStdin: false);
            p.Start(_ => { });
            if (!p.WaitForExit(timeoutSeconds * 1000))
            {
                p.Kill();
                throw new ToolError("Edge", -1, $"PDF: report did not print in {timeoutSeconds} s");
            }
            var ok = File.Exists(tmp) && new FileInfo(tmp).Length > 4 && File.ReadAllBytes(tmp)[..4] is [0x25, 0x50, 0x44, 0x46];
            if (!ok) throw new ToolError("Edge", p.ExitCode, "PDF: printing did not produce a file\n" + p.StderrTail);
            NativeIO.Move(tmp, path, replace);
        }
        finally
        {
            NativeIO.Delete(tmp);
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
