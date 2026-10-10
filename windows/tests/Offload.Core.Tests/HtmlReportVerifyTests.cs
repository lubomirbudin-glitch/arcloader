using Offload.Core.VerifyInProxy;

namespace Offload.Core.Tests;

/// Spec 9 (plan 10): the report says how each destination was verified, which disk it was (R12), when the card could be
/// formatted, a verification still running, the content warnings with their place and picture, the notes of spec 10.
public class HtmlReportVerifyTests
{
    const string Card = @"D:\Zaloha\2026-10-08\A001";

    [Fact]
    public void Html_PendingContentWarningsNotes_AndBlockMismatchInCzech()
    {
        var r = Reports.Sample(Card, verified: false);
        r.VerifyPending = true;
        r.VerifyNotes = ["Unstable read: a.mov on RAID_A (block 3) gave different data in one read — the next read matched the card."];
        r.Clips = [new ClipEntry
        {
            Clip = new Clip { Name = "A001C003", Path = "CLIP/A&B.mov", Segments = ["CLIP/A&B.mov"] },
            Content = new ContentResult { State = ContentResult.Warn, Kind = "decode", Timecode = "01:00:02:13", Message = "corrupt decoded frame", Jpeg = [0xFF, 0xD8] },
        }];

        var html = HtmlReport.Render(r);

        Assert.Contains("COPIED — VERIFYING", html);
        Assert.Contains("Content defects (1)", html);
        Assert.Contains("01:00:02:13", html);
        Assert.Contains("corrupted data", html);
        Assert.Contains("data:image/jpeg;base64,/9g=", html);
        Assert.Contains("Verification — notes (1)", html);
        Assert.Equal("block 3 mismatch (destination 0000000000000001, card 0000000000000002)",
            HtmlReport.ErrorText("HASH MISMATCH: block 3 dest 0000000000000001 != source 0000000000000002"));
    }

    // Final review, item 3: the results were written into this folder but not into every one (verifyIncomplete) — never a
    // green "OVĚŘENO" on its own.
    [Fact]
    public void Html_VerifyIncomplete_NedokoncenoWarn()
    {
        var r = Reports.Sample(Card);
        Assert.Contains("<span class=\"badge ok\">VERIFIED</span>", HtmlReport.Render(r));
        r.VerifyIncomplete = true;

        var html = HtmlReport.Render(r);

        Assert.Contains("<span class=\"badge warn\">UNFINISHED</span>", html);
        Assert.DoesNotContain(">VERIFIED<", html);
    }

    // Spec 9: the method and the serial per destination, the time to "Lze formátovat", in the HTML and the text report.
    [Fact]
    public void Classic_Html_MethodSerialAndTimeToFormat()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.bin"), 10_000, 1);
        var engine = new OffloadEngine(new OffloadOptions { ChunkSize = 64 * 1024, BufferCount = 4, AllowSameDevice = true, AllowSourceVolume = true });
        var report = engine.Run(engine.Prepare(card, [dir.Sub("d1")], null, "2026-10-08"));

        var html = HtmlReport.Render(report);

        Assert.Contains("<th>Volume</th><th>Verification</th>", html);
        Assert.Contains($"<td class=\"mono nw\">{report.Destinations[0].VolumeSerial}</td><td class=\"nw\">read back</td>", html);
        Assert.Contains("until \u201csafe to format\u201d", html);
        Assert.Contains("XXH64 + XXH128", html);
        Assert.Contains("  verify: read-back", report.Summary);
        Assert.Contains("Safe to format after: ", report.Summary);
    }

    // Spec 9: verified with the proxies — the card's summary and the text report say so; content warnings with their TC.
    [Fact]
    public void InProxy_SummaryAndText()
    {
        var r = Reports.Sample(Card);
        r.Destinations[0].VerifyMethod = ReportDestination.InProxy;
        r.Clips = [new ClipEntry
        {
            Clip = new Clip { Name = "A001C003", Path = "CLIP/A&B.mov", Segments = ["CLIP/A&B.mov"] },
            Content = new ContentResult { State = ContentResult.Warn, Kind = "end", Timecode = "01:00:02:13" },
        }];
        Assert.Equal("during proxies", Desktop.JobCard.Summary(r, TimeSpan.FromSeconds(10)).Single(f => f.Label == "Verification").Value);
        Assert.Contains("  verify: verify-in-proxy", r.Summary);
        Assert.Contains("Content warnings: 1 (A001C003 01:00:02:13)", r.Summary);
        Assert.Contains("<td class=\"nw\">during proxies</td>", HtmlReport.Render(r));
        r.Destinations[0].VerifyMethod = ReportDestination.ReadBack;
        Assert.EndsWith("MB/s", Desktop.JobCard.Summary(r, TimeSpan.FromSeconds(10)).Single(f => f.Label == "Verification").Value);
    }
}
