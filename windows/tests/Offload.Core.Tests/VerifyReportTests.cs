using System.Text.Json;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

public class VerifyReportTests
{
    static VerifyResult Sample(string cardPath)
    {
        var r = new VerifyResult
        {
            CardPath = cardPath,
            Started = new DateTime(2026, 10, 6, 8, 15, 0, DateTimeKind.Utc),
            Basis = "ascmhl",
            Generations = 2,
            HashAlgorithms = ["xxh64"],
            TotalFiles = 5,
            TotalBytes = 5000,
            Ok = 2,
            Checked = 5,
            Finished = new DateTime(2026, 10, 6, 8, 16, 0, DateTimeKind.Utc),
        };
        r.Changed.Add("CLIP/b.mov");
        r.Missing.Add("CLIP/c.mov");
        r.Unreadable.Add(new UnreadableFile("CLIP/d.mov", "read failed"));
        r.Extra.Add("CLIP/new.mov");
        r.ChainProblems.Add("ASC MHL generation 0002 does not match the chain (the MHL file was changed).");
        return r;
    }

    [Fact]
    public void Json_ShapeOfSpec24()
    {
        var json = VerifyReport.Json(Sample(@"D:\Zaloha\2026-10-06\A001"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var keys = root.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.Equal(["basis", "cardPath", "chainProblems", "changed", "extra", "failedAtCopy", "failedSuperseded", "finished", "generations", "hashAlgorithms",
            "missing", "ok", "started", "success", "tool", "totalBytes", "totalFiles", "unfinished", "unreadable", "version"], keys);
        Assert.Equal("2026-10-06T08:15:00Z", root.GetProperty("started").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("CLIP/d.mov", root.GetProperty("unreadable")[0].GetProperty("path").GetString());
        Assert.Equal("read failed", root.GetProperty("unreadable")[0].GetProperty("error").GetString());
        Assert.Equal(OffloadVersion.String, root.GetProperty("version").GetString());
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void Html_HasToolbarWithItsOwnPdf()
    {
        var r = Sample(@"D:\Zaloha6-10-06\A001");
        var html = VerifyReport.Html(r);
        Assert.Contains("onclick=\"window.print()\">Save as PDF</button>", html);
        Assert.Contains($"<a href=\"A001_verify_{VerifyReport.Stamp(r)}Z.pdf\"", html);
        Assert.DoesNotContain("class=\"toolbar\"", VerifyReport.Html(r, forPrint: true));
    }

    [Fact]
    public void Html_CzechTexts()
    {
        var html = VerifyReport.Html(Sample(@"D:\Zaloha\2026-10-06\A001"));
        Assert.Contains("<h1>Verification A001</h1>", html);
        Assert.Contains("PROBLEMS FOUND", html);
        Assert.Contains("Mismatch 1 file, missing 1, unreadable 1 — details in the verification report.", html);
        Assert.Contains("1 file extra (not in the MHL).", html);
        Assert.Contains("Missing (1)", html);
        Assert.Contains("CLIP/d.mov — read failed", html);
    }

    [Fact]
    public void CliSummary_OkAndProblems()
    {
        var ok = new VerifyResult { CardPath = "x", Basis = "ascmhl", Started = DateTime.UtcNow.AddSeconds(-10), Finished = DateTime.UtcNow, Checked = 12, Ok = 12, BytesRead = 512_000_000_000 };
        Assert.StartsWith("Verified 12 files (512 GB) in 0:10 (", VerifyTexts.CliSummary(ok));
        Assert.EndsWith(" — all OK.", VerifyTexts.CliSummary(ok));
        Assert.EndsWith(" — PROBLEMS FOUND: mismatch 1, missing 1, unreadable 1, failed during the copy 0, extra 1.", VerifyTexts.CliSummary(Sample("x")));
    }

    // R8: the report goes into _REPORT, never into ascmhl\.
    [Fact]
    public void Write_IntoReportFolderOnly()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-06", "A001");
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        var html = VerifyReport.Write(Sample(card));
        Assert.Equal(Path.Combine(card, "_REPORT", "A001_verify_2026-10-06_081500Z.html"), html);
        Assert.True(File.Exists(Path.Combine(card, "_REPORT", "offload_verify_2026-10-06_081500Z.json")));
        Assert.Empty(Directory.GetFiles(Path.Combine(card, "ascmhl")));
    }

    /// Launches Edge: in the Edge collection (fix round 2, item 9), the rest of the class stays parallel.
    [Collection("Edge")]
    public class Pdf
    {
        [EdgeFact]
        public void Write_Pdf()
        {
            using var pdfs = PdfReport.PrintHere();
            using var dir = new TempDir();
            var card = dir.Sub("2026-10-06", "A001");
            VerifyReport.Write(Sample(card));
            var pdf = Path.Combine(card, "_REPORT", "A001_verify_2026-10-06_081500Z.pdf");
            Assert.True(File.Exists(pdf));
            Assert.Equal("%PDF"u8.ToArray(), File.ReadAllBytes(pdf)[..4]);
        }
    }
}
