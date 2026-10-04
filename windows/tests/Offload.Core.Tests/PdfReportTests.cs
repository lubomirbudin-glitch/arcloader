using System.Text;
using System.Text.RegularExpressions;

namespace Offload.Core.Tests;

public class PdfReportTests
{
    [EdgeFact]
    public void Write_A4LandscapePdf_EvenForLargeReports()
    {
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

    [Fact]
    public void FindEdge_ReturnsExistingFileOrNull()
    {
        var edge = PdfReport.FindEdge();
        Assert.True(edge == null || File.Exists(edge));
    }
}
