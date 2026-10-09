using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core.Tests;

[Collection("Edge")]
public class PdfReportTests
{
    [EdgeFact]
    public void Write_A4LandscapePdf_EvenForLargeReports()
    {
        using var pdfs = PdfReport.PrintHere();
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-05", "A001");
        var report = Reports.Sample(card);
        var rnd = new Random(1);
        for (int i = 0; i < 40; i++)
        {
            var jpeg = new byte[20_000];
            rnd.NextBytes(jpeg);
            report.Clips.Add(new ClipEntry
            {
                Clip = new Clip { Name = $"C{i:000}", Path = $"CLIP/C{i:000}.MP4", Format = ClipFormat.Video, Segments = [$"CLIP/C{i:000}.MP4"], Bytes = 1 },
                Thumbnails = [new(jpeg, "01:00:00:00"), new(jpeg, "01:00:01:00"), new(jpeg, "01:00:02:00")],
            });
        }
        Assert.True(HtmlReport.Render(report, forPrint: true).Length > 2_000_000);
        var pdf = Path.Combine(card, "_REPORT", "A001_report.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);

        PdfReport.Write(report, pdf);

        var bytes = File.ReadAllBytes(pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        var box = Regex.Match(Encoding.Latin1.GetString(bytes), @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)");
        Assert.True(box.Success);
        Assert.InRange(double.Parse(box.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 840, 844);
        Assert.InRange(double.Parse(box.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 593, 597);
        Assert.Equal(["A001_report.pdf"], Directory.GetFiles(Path.GetDirectoryName(pdf)!).Select(Path.GetFileName));
    }

    // Fix round 2, item 9: outside PrintHere a test prints nothing (Edge is never started) — also on the threads it starts.
    [Fact]
    public void InTests_NoPdfUnlessAsked()
    {
        using var dir = new TempDir();
        var pdf = Path.Combine(dir.Path, "x.pdf");
        Assert.True(PdfReport.SkipUnlessAsked);
        Assert.Equal("PDF skipped (tests)", Assert.Throws<ToolError>(() => PdfReport.WriteHtml("<p>x</p>", pdf)).Detail);
        bool askedOnAThread = false;
        using (PdfReport.PrintHere())
        {
            // The scope reaches a thread the test starts (only the flag is read; Edge is not started).
            var t = new Thread(() => askedOnAThread = PdfReport.AskedHere);
            t.Start();
            t.Join();
        }
        Assert.True(askedOnAThread);
        Assert.False(PdfReport.AskedHere);
        Assert.False(File.Exists(pdf));
    }

    // Edge 154 on the developer's PC: --print-to-pdf exits 0 after ~4 s, a lingering Edge process writes the PDF 12–20 s
    // later. The fake browser does the same: it exits at once and a detached child writes the file a few seconds later.
    static string FakeBrowser(TempDir dir, string tmpTarget, int delaySeconds, bool write = true)
    {
        var pdf = Path.Combine(dir.Path, "late.pdf");
        File.WriteAllLines(pdf, ["%PDF-1.4", "1 0 obj<<>>endobj", "trailer<<>>", "%%EOF"]);
        var cmd = Path.Combine(dir.Path, "fake-edge.cmd");
        var later = write ? $"copy /y \"{pdf}\" \"{tmpTarget}\"" : "rem";
        File.WriteAllLines(cmd,
        [
            "@echo off",
            $"start \"\" /b cmd /c \"ping -n {delaySeconds + 1} 127.0.0.1 >nul & {later} >nul\" >nul 2>nul <nul",
            "exit /b 0",
        ]);
        return cmd;
    }

    static string TmpFor(string path) => Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".tmp");

    [Fact]
    public void LatePdf_FromALingeringBrowserProcess_IsWaitedFor()
    {
        using var pdfs = PdfReport.PrintHere();
        using var dir = new TempDir();
        var path = Path.Combine(dir.Sub("out"), "A001_report.pdf");
        var fake = FakeBrowser(dir, TmpFor(path), delaySeconds: 3);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        PdfReport.WriteHtml("<p>x</p>", path, browser: fake);

        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(2), sw.Elapsed.ToString());
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        Assert.False(File.Exists(TmpFor(path)));
    }

    [Fact]
    public void LatePdf_NeverComes_FailsAfterTheWait()
    {
        using var pdfs = PdfReport.PrintHere();
        using var dir = new TempDir();
        var path = Path.Combine(dir.Sub("out"), "A001_report.pdf");
        var fake = FakeBrowser(dir, TmpFor(path), delaySeconds: 1, write: false);
        var e = Assert.Throws<ToolError>(() => PdfReport.WriteHtml("<p>x</p>", path, browser: fake, lateWait: TimeSpan.FromSeconds(2)));
        Assert.Contains("did not produce a file", e.Detail);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LatePdf_Wait_HonoursCancellation()
    {
        using var pdfs = PdfReport.PrintHere();
        using var dir = new TempDir();
        var path = Path.Combine(dir.Sub("out"), "A001_report.pdf");
        var fake = FakeBrowser(dir, TmpFor(path), delaySeconds: 1, write: false);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => PdfReport.WriteHtml("<p>x</p>", path, browser: fake, cancel: cancel.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), sw.Elapsed.ToString());
    }

    // Mac 138cdba: whatever is still wider than A4 landscape is zoomed out before printing, never cut off at the right edge.
    [Fact]
    public void ForPrinting_AddsTheFitScriptBeforeBodyEnd()
    {
        var html = PdfReport.ForPrinting("<!doctype html><html><body><main>x</main></body></html>");
        Assert.EndsWith(PdfReport.FitScript + "</body></html>", html);
        Assert.Equal("<p>x</p>" + PdfReport.FitScript, PdfReport.ForPrinting("<p>x</p>"));
    }

    [EdgeFact]
    public void FitScript_ZoomsOutOnlyWhatIsWiderThanThePage()
    {
        Assert.Matches(@"zoom:\s*0\.[34]\d*", BodyStyleAfterScript("<div style=\"width:3000px;height:10px\">wide</div>"));
        Assert.DoesNotContain("zoom", BodyStyleAfterScript("<p>narrow</p>"));
    }

    /// The page as Edge leaves it after the script, laid out at the window width PdfReport prints with.
    static string BodyStyleAfterScript(string content)
    {
        using var dir = new TempDir();
        var page = Path.Combine(dir.Path, "p.html");
        File.WriteAllText(page, PdfReport.ForPrinting($"<!doctype html><html><head><style>body{{margin:0}}</style></head><body>{content}</body></html>"));
        var psi = new System.Diagnostics.ProcessStartInfo(PdfReport.FindEdge()!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                     $"--user-data-dir={Path.Combine(dir.Path, "profile")}", PdfReport.NoRelaunchArg, PdfReport.WindowSizeArg, "--dump-dom", new Uri(page).AbsoluteUri })
            psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        var dom = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(60_000));
        var body = Regex.Match(dom, "<body([^>]*)>");
        Assert.True(body.Success, dom);
        return body.Groups[1].Value;
    }

    [Fact]
    public void FindEdge_ReturnsExistingFileOrNull()
    {
        var edge = PdfReport.FindEdge();
        Assert.True(edge == null || File.Exists(edge));
    }
}
