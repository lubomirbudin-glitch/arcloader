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
        r.VerifyNotes = ["Nestabilní čtení: a.mov na RAID_A (blok 3) dal při jednom čtení jiná data — při dalším souhlasil s kartou."];
        r.Clips = [new ClipEntry
        {
            Clip = new Clip { Name = "A001C003", Path = "CLIP/A&B.mov", Segments = ["CLIP/A&B.mov"] },
            Content = new ContentResult { State = ContentResult.Warn, Kind = "decode", Timecode = "01:00:02:13", Message = "corrupt decoded frame", Jpeg = [0xFF, 0xD8] },
        }];

        var html = HtmlReport.Render(r);

        Assert.Contains("ZKOPÍROVÁNO — OVĚŘUJE SE", html);
        Assert.Contains("Vady obsahu (1)", html);
        Assert.Contains("01:00:02:13", html);
        Assert.Contains("poškozená data", html);
        Assert.Contains("data:image/jpeg;base64,/9g=", html);
        Assert.Contains("Ověření — poznámky (1)", html);
        Assert.Equal("nesouhlasí blok 3 (cíl 0000000000000001, karta 0000000000000002)",
            HtmlReport.ErrorText("HASH MISMATCH: block 3 dest 0000000000000001 != source 0000000000000002"));
    }

    // Final review, item 3: the results were written into this folder but not into every one (verifyIncomplete) — never a
    // green "OVĚŘENO" on its own.
    [Fact]
    public void Html_VerifyIncomplete_NedokoncenoWarn()
    {
        var r = Reports.Sample(Card);
        Assert.Contains("<span class=\"badge ok\">OVĚŘENO</span>", HtmlReport.Render(r));
        r.VerifyIncomplete = true;

        var html = HtmlReport.Render(r);

        Assert.Contains("<span class=\"badge warn\">NEDOKONČENO</span>", html);
        Assert.DoesNotContain(">OVĚŘENO<", html);
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

        Assert.Contains("<th>Svazek</th><th>Ověření</th>", html);
        Assert.Contains($"<td class=\"mono\">{report.Destinations[0].VolumeSerial}</td><td>zpětné čtení</td>", html);
        Assert.Contains("do „lze formátovat“", html);
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
        Assert.Equal("během proxy", Desktop.JobCard.Summary(r, TimeSpan.FromSeconds(10)).Single(f => f.Label == "Ověření").Value);
        Assert.Contains("  verify: verify-in-proxy", r.Summary);
        Assert.Contains("Content warnings: 1 (A001C003 01:00:02:13)", r.Summary);
        Assert.Contains("<td>během proxy</td>", HtmlReport.Render(r));
        r.Destinations[0].VerifyMethod = ReportDestination.ReadBack;
        Assert.EndsWith("MB/s", Desktop.JobCard.Summary(r, TimeSpan.FromSeconds(10)).Single(f => f.Label == "Ověření").Value);
    }
}
