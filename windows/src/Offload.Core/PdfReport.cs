using System.Text;

namespace Offload.Core;

/// Prints the HTML report into an A4 landscape PDF with Microsoft Edge in headless mode — the same engine
/// as WebView2, present on every Windows 10/11, no window and no extra package. Page size and margins come
/// from the report's @page rule.
public static class PdfReport
{
    /// How long Edge may take to print one report before the PDF is given up (the HTML report is already written).
    public const int DefaultTimeoutSeconds = 60;

    /// Tests only (fix round 2, item 9): set for the whole test assembly, every PDF is skipped (a ToolError, which
    /// every caller logs and goes on) except inside a PrintHere() scope — a headless Edge per job was slow and flaked
    /// under load. The scope is an AsyncLocal, so it reaches the threads and tasks a test starts.
    internal static bool SkipUnlessAsked;
    static readonly AsyncLocal<bool> asked = new();

    internal static bool AskedHere => asked.Value;

    internal static IDisposable PrintHere()
    {
        asked.Value = true;
        return new Asked();
    }

    sealed class Asked : IDisposable
    {
        public void Dispose() => asked.Value = false;
    }

    /// Edge lays the page out (and FitScript measures it) at the width of A4 landscape inside the report's 10 mm margins:
    /// 277 mm = 1047 CSS px.
    internal const string WindowSizeArg = "--window-size=1047,1400";

    /// A process started with __COMPAT_LAYER in its environment (Windows sets e.g. "DetectorsAppHealth" for some parents)
    /// makes Edge relaunch itself without it: the process we wait for exits at once with code 0, the PDF appears later
    /// (or never, once its work folder is gone) and printing "did not produce a file". Edge skips that relaunch with this.
    internal const string NoRelaunchArg = "--edge-skip-compat-layer-relaunch";

    /// Safety net, as on the Mac (138cdba): anything still wider than the page (an unbreakable name, many columns) zooms the
    /// whole page out before Edge prints it, so nothing is cut off at the right edge. Runs while the page loads, before printing.
    internal const string FitScript =
        "<script>(function(){var d=document.documentElement,w=d.clientWidth,s=Math.max(d.scrollWidth,document.body.scrollWidth);" +
        "if(s>w+1)document.body.style.zoom=String(0.98*w/s);})()</script>";

    /// The page Edge prints: `html` with FitScript at the end of its body.
    internal static string ForPrinting(string html)
    {
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html + FitScript : html.Insert(end, FitScript);
    }

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
    public static void Write(OffloadReport report, string path, int timeoutSeconds = DefaultTimeoutSeconds, bool replace = true) =>
        WriteHtml(HtmlReport.Render(report, forPrint: true), path, timeoutSeconds, replace);

    /// How long a PDF may still appear after Edge has exited (WaitForLatePdf).
    public static readonly TimeSpan DefaultLateWait = TimeSpan.FromSeconds(60);

    /// Any self-contained HTML page (the offload report, the verification report, the day report) printed to `path`.
    /// `browser` (tests): another executable called like Edge; `lateWait`: see WaitForLatePdf.
    public static void WriteHtml(string html, string path, int timeoutSeconds = DefaultTimeoutSeconds, bool replace = true,
        CancellationToken cancel = default, string? browser = null, TimeSpan? lateWait = null)
    {
        if (SkipUnlessAsked && !AskedHere) throw new ToolError("Edge", -1, "PDF skipped (tests)");
        var edge = browser ?? FindEdge() ?? throw new ToolError("Edge", -1, "Microsoft Edge not found — PDF skipped");
        var work = Path.Combine(Path.GetTempPath(), "offload-pdf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".tmp");
        try
        {
            // A file, not a data URL: the report with thumbnails is easily several MB.
            var page = Path.Combine(work, "report.html");
            File.WriteAllText(page, ForPrinting(html), new UTF8Encoding(false));
            NativeIO.Delete(tmp);
            using var p = new ToolProcess(edge,
            [
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                $"--user-data-dir={Path.Combine(work, "profile")}",  // never touches the user's own Edge profile
                NoRelaunchArg, WindowSizeArg, "--no-pdf-header-footer", $"--print-to-pdf={tmp}", new Uri(page).AbsoluteUri,
            ], work, pipeStdin: false);
            p.Start(_ => { });
            if (!p.WaitForExit(timeoutSeconds * 1000))
            {
                p.Kill();
                throw new ToolError("Edge", -1, $"PDF: report did not print in {timeoutSeconds} s");
            }
            if (!WaitForLatePdf(tmp, lateWait ?? DefaultLateWait, cancel))
                throw new ToolError("Edge", p.ExitCode, "PDF: printing did not produce a file\n" + p.StderrTail);
            NativeIO.Move(tmp, path, replace);
        }
        finally
        {
            NativeIO.Delete(tmp);
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// Edge 154 (seen 2026-10-09) can exit with code 0 many seconds before a lingering Edge process writes the PDF. A
    /// complete PDF that is there when Edge exits is taken at once; otherwise the file is awaited for up to `max`: complete
    /// (starts with %PDF, ends with %%EOF) and its size unchanged for a second. Cancellation is honoured while waiting.
    internal static bool WaitForLatePdf(string file, TimeSpan max, CancellationToken cancel)
    {
        if (IsCompletePdf(file)) return true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long lastSize = -1;
        var stableSince = TimeSpan.Zero;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            long size;
            try { size = File.Exists(file) ? new FileInfo(file).Length : -1; }
            catch (IOException) { size = -1; }
            if (size != lastSize) { lastSize = size; stableSince = clock.Elapsed; }
            else if (size > 4 && clock.Elapsed - stableSince >= TimeSpan.FromSeconds(1) && IsCompletePdf(file)) return true;
            if (clock.Elapsed >= max) return false;
            if (cancel.WaitHandle.WaitOne(200)) cancel.ThrowIfCancellationRequested();
        }
    }

    static bool IsCompletePdf(string file)
    {
        try
        {
            using var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (f.Length < 9) return false;
            var head = new byte[4];
            f.ReadExactly(head);
            if (head is not [0x25, 0x50, 0x44, 0x46]) return false;
            var tailLength = (int)Math.Min(1024, f.Length);
            f.Seek(-tailLength, SeekOrigin.End);
            var tail = new byte[tailLength];
            f.ReadExactly(tail);
            return Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
