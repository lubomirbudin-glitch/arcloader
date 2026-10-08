namespace Offload.Core.Tests;

public class HtmlReportTests
{
    const string Card = @"D:\dst\2026-10-04\A001";

    [Fact]
    public void Verified_ShowsStatusFilesAndEscapes()
    {
        var html = HtmlReport.Render(Reports.Sample(Card));
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<span class=\"badge ok\">OVĚŘENO</span>", html);
        Assert.Contains("CLIP/A&amp;B.mov", html);
        Assert.DoesNotContain("CLIP/A&B.mov", html);
        Assert.Contains("<td class=\"ok\">ověřeno</td>", html);
        Assert.Contains("<details><summary>Všechny soubory (2)</summary>", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("offload 0.5.0-win", html);
    }

    [Fact]
    public void Failed_ListsErrorsPerDestination()
    {
        var html = HtmlReport.Render(Reports.Sample(Card, destError: "boom"));
        Assert.Contains("<span class=\"badge bad\">CHYBA</span>", html);
        Assert.Contains("Chyby (1)", html);
        Assert.Contains("cíl 1: boom", html);
    }

    // Spec 2.6/6.2 task 9: the file errors the JSON keeps in English (shared with the Mac) read in Czech in the HTML.
    [Theory]
    [InlineData("size mismatch: 4000 != 5000", "jiná velikost: 4000 != 5000")]
    [InlineData("HASH MISMATCH: dest 0123456789abcdef != source fedcba9876543210", "nesouhlasí kontrolní součet (cíl 0123456789abcdef, karta fedcba9876543210)")]
    [InlineData("MD5 MISMATCH: dest 900150983cd24fb0d6963f7d28e17f72 != source -", "nesouhlasí MD5 (cíl 900150983cd24fb0d6963f7d28e17f72, karta -)")]
    [InlineData("verify: The device is not ready.", "chyba při ověření: The device is not ready.")]
    [InlineData("write failed", "zápis selhal")]
    [InlineData("source: cancelled", "karta: zrušeno")]
    [InlineData("source: source size changed during copy (10 != 20)", "karta: soubor na kartě se během kopie změnil (10 != 20)")]
    [InlineData("source: read E:\\a.mov: The request could not be performed (after 3 retries)", "karta: read E:\\a.mov: The request could not be performed (po 3 opakováních)")]
    [InlineData("md5 hash failed: out of memory", "výpočet MD5 selhal: out of memory")]
    [InlineData("xxh64 hash failed: boom", "výpočet xxHash64 selhal: boom")]
    [InlineData("source: md5 hash failed: boom", "karta: výpočet MD5 selhal: boom")]
    [InlineData("disk full (Mac)", "disk full (Mac)")]
    public void ErrorText_Czech(string error, string czech) => Assert.Equal(czech, HtmlReport.ErrorText(error));

    [Fact]
    public void Failed_ErrorsInCzech()
    {
        var html = HtmlReport.Render(Reports.Sample(Card, destError: "HASH MISMATCH: dest 0000000000000001 != source fedcba9876543210"));
        Assert.Contains("cíl 1: nesouhlasí kontrolní součet (cíl 0000000000000001, karta fedcba9876543210)", html);
        Assert.DoesNotContain("HASH MISMATCH", html);
    }

    [Fact]
    public void NotVerified_IsWarning() =>
        Assert.Contains("ZKOPÍROVÁNO — NEOVĚŘENO", HtmlReport.Render(Reports.Sample(Card, verified: false)));

    [Fact]
    public void Buffered_ShowsWarning() =>
        Assert.Contains("svazek nepodporuje přímý zápis", HtmlReport.Render(Reports.Sample(Card, unbuffered: false)));

    [Fact]
    public void Print_ExpandsFileListAndUsesLightColours()
    {
        var html = HtmlReport.Render(Reports.Sample(Card), forPrint: true);
        Assert.Contains("<details open>", html);
        Assert.DoesNotContain("prefers-color-scheme:dark", html);
    }

    [Fact]
    public void Print_HasA4LandscapePage() =>
        Assert.Contains("@page{size:A4 landscape;margin:10mm}", HtmlReport.Render(Reports.Sample(Card), forPrint: true));

    [Fact]
    public void Toolbar_HasPrintButtonHintAndPdfLink()
    {
        var html = HtmlReport.Render(Reports.Sample(Card));
        Assert.Contains("<div class=\"toolbar\">", html);
        Assert.Contains("<button type=\"button\" onclick=\"window.print()\">Uložit jako PDF</button>", html);
        Assert.Contains("v tisku zvolte „Uložit jako PDF“", html);
        Assert.Contains("<a href=\"A001_report.pdf\"", html);
        Assert.Contains(">Otevřít PDF report</a>", html);
        Assert.Matches(@"@media print\{.*\.toolbar\{display:none\}", html);
    }

    [Fact]
    public void Toolbar_LinksTheGenerationsOwnPdf()
    {
        var r = Reports.Sample(Card);
        r.Generation = 2;
        Assert.Contains("<a href=\"A001_report_0002.pdf\"", HtmlReport.Render(r));
    }

    [Fact]
    public void Toolbar_IsNotInThePdfItself() =>
        Assert.DoesNotContain("<div class=\"toolbar\">", HtmlReport.Render(Reports.Sample(Card), forPrint: true));

    [Fact]
    public void Css_PrintIsA4LandscapeWhiteAndRowsStayWhole()
    {
        foreach (var forPrint in new[] { false, true })
        {
            var html = HtmlReport.Render(Reports.Sample(Card), forPrint);
            Assert.Contains("@page{size:A4 landscape;margin:10mm}", html);
            Assert.Contains("break-inside:avoid", html);
            Assert.Contains("@media print{:root{--bg:#f6f6f4;--card:#fff;--fg:#1d1d1f", html);   // light colours even when printed from a dark screen
            Assert.Contains("body{background:#fff;color:#000}", html);
        }
    }

    [Fact]
    public void Css_TablesFitTheWidthAndWrap()
    {
        var css = HtmlReport.Styles(false);
        Assert.Contains("overflow-wrap:anywhere", css);
        Assert.Contains("table.clips{table-layout:fixed", css);
        Assert.Contains(".thumbs figure{margin:0;flex:1 1 0;min-width:0;max-width:128px}", css);
        Assert.Contains("max-width:100%", css);
        Assert.DoesNotContain("vertical-align:middle;white-space:nowrap", css);
        Assert.DoesNotContain("src=\"http", css);
    }

    // 0.5.1: the proxy status never breaks inside a word („nepodporo|váno“): normal word breaks in that column, which is
    // a little wider (the thumbnails give it the room — the table still fits 1400 px and A4).
    [Fact]
    public void Css_ProxyStatusBreaksOnlyBetweenWords()
    {
        var css = HtmlReport.Styles(false);
        Assert.Contains("table.clips td.st{overflow-wrap:normal;word-break:normal;hyphens:none}", css);
        Assert.DoesNotContain("td.st{overflow-wrap:anywhere", css);
        Assert.DoesNotContain("table.clips td.clip,table.clips td.st{", css);
        Assert.Contains(".c-thumb{width:20%}", css);
        Assert.Contains(".c-proxy{width:11%}", css);
        Assert.Contains(".c-proxy{width:20%}table.clips td.st{font-size:12px}}", css);   // the narrow window
        Assert.Contains("table.clips td.st a{color:inherit}", css);   // the link to a finished proxy keeps the green
    }

    [Fact]
    public void Clips_TableHasColumnWidthsAndStaysSelfContained()
    {
        var r = Reports.Sample(Card);
        r.Clips.Add(new ClipEntry { Clip = new Clip { Name = "A", Path = "A.mov", Format = ClipFormat.Video, Segments = ["A.mov"], Bytes = 1 }, ProxyStatus = ProxyStatus.Done });
        var html = HtmlReport.Render(r);
        Assert.Contains("<table class=\"clips\"><colgroup>", html);
        Assert.Contains("<col class=\"c-proxy\">", html);
        Assert.DoesNotContain("<script src", html);
        Assert.DoesNotContain("<link ", html);
    }

    [Fact]
    public void Clips_RenderRowWithThumbnailAndProxyStatus()
    {
        var r = Reports.Sample(Card);
        r.Clips.Add(new ClipEntry
        {
            Clip = new Clip { Name = "A001C001", Path = "CLIP/A&B.mov", Format = ClipFormat.Video, Segments = ["CLIP/A&B.mov"], Bytes = 1000 },
            Info = new ClipInfo { Codec = "prores", Width = 3840, Height = 2160, FrameRate = "25/1", Duration = 12, AudioChannels = 2, Timecode = "01:00:00:00" },
            ProxyStatus = ProxyStatus.Done,
            Thumbnails = [new([1, 2, 3], "01:00:00:00"), new([4, 5, 6], "01:00:06:00"), new([7, 8, 9], "01:00:11:24")],
        });
        r.Proxy = new ProxySettings { Enabled = true, FrameAspect = 1.66 };
        var html = HtmlReport.Render(r);
        Assert.Contains("<h2>Klipy</h2>", html);
        Assert.Contains("<div class=\"thumbs\"><figure><img class=\"thumb\" src=\"data:image/jpeg;base64,AQID\" alt=\"\"><figcaption>01:00:00:00</figcaption></figure>", html);
        Assert.Contains("base64,BAUG\" alt=\"\"><figcaption>01:00:06:00</figcaption>", html);
        Assert.Contains("base64,BwgJ\" alt=\"\"><figcaption>01:00:11:24</figcaption></figure></div>", html);
        Assert.Contains("3840×2160", html);
        Assert.Contains("<td class=\"st ok\">hotovo</td>", html);
        Assert.Contains("2 ch", html);
        Assert.Contains("Proxy: H.264, 1080p, výřez 1.66, LUT: automaticky podle kamery", html);
        r.ProxyEncoder = "h264_nvenc";
        Assert.Contains("Proxy: H.264 (h264_nvenc), 1080p, výřez 1.66, LUT: automaticky podle kamery", HtmlReport.Render(r));
        Assert.DoesNotContain("také ve složce", HtmlReport.Render(r));
        r.Proxy.ExtraFolder = @"E:\Proxy & co";
        Assert.Contains("<span class=\"mono\">_PROXY/</span> · také ve složce <span class=\"mono\">E:\\Proxy &amp; co</span></p>", HtmlReport.Render(r));
    }

    // 0.5.1: a finished proxy links to its file — relative to _REPORT, in the card's folders (_PROXY mirrors them).
    [Fact]
    public void Clips_DoneProxyLinksToItsFile()
    {
        var r = Reports.Sample(Card);
        r.Clips.Add(new ClipEntry
        {
            Clip = new Clip { Name = "A&B", Path = "idle cam/A&B.mov", Format = ClipFormat.Video, Segments = ["idle cam/A&B.mov"], Bytes = 1 },
            ProxyStatus = ProxyStatus.Done,
            ProxyPath = "_PROXY/idle cam/A&B.mov",
        });
        var html = HtmlReport.Render(r);
        Assert.Contains("<td class=\"st ok\"><a href=\"../_PROXY/idle%20cam/A%26B.mov\" title=\"_PROXY/idle cam/A&amp;B.mov\">hotovo</a></td>", html);
    }

    [Fact]
    public void Write_CreatesAllReportFiles()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        var report = Reports.Sample(card);

        report.Write(card, includeHashRecords: true);

        var rep = Path.Combine(card, "_REPORT");
        Assert.True(File.Exists(Path.Combine(rep, "offload_report.json")));
        Assert.True(File.Exists(Path.Combine(rep, "offload_report.txt")));
        Assert.True(File.Exists(Path.Combine(rep, "A001_report.html")));
        Assert.Equal(report.HashList, File.ReadAllText(Path.Combine(rep, "A001.xxh64")));
        Assert.Single(Directory.GetFiles(Path.Combine(card, "ascmhl"), "*.mhl"));
        Assert.Empty(Directory.GetFiles(rep, "*.tmp"));
        var json = File.ReadAllBytes(Path.Combine(rep, "offload_report.json"));
        Assert.NotEqual(0xEF, json[0]);   // no UTF-8 BOM
        foreach (var name in new[] { "offload_report.json", "offload_report.txt", "A001_report.html", "A001.xxh64" })
            Assert.DoesNotContain((byte)13, File.ReadAllBytes(Path.Combine(rep, name)));
        foreach (var name in Directory.GetFiles(Path.Combine(card, "ascmhl"), "*.mhl"))
            Assert.DoesNotContain((byte)13, File.ReadAllBytes(name));
    }

    [Fact]
    public void Write_WithoutHashRecords_SkipsListAndMhl()
    {
        using var dir = new TempDir();
        var card = dir.Sub("2026-10-04", "A001");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        Assert.False(File.Exists(Path.Combine(card, "_REPORT", "A001.xxh64")));
        Assert.False(Directory.Exists(Path.Combine(card, "ascmhl")));
    }

    [Fact]
    public void Clip_ShowsLut_AndJsonKey()
    {
        var report = Reports.Sample(Card);
        report.Clips.Add(new ClipEntry
        {
            Clip = new Clip { Name = "C0001", Path = "C0001.MP4", Format = ClipFormat.Video, Segments = ["C0001.MP4"], Bytes = 1 },
            Lut = new LutUse("auto", "sony-slog3-sgamut3cine", "Sony S-Log3 / S-Gamut3.Cine → Rec.709", "Sony XML: s-log3-cine / S-Gamut3Cine"),
        });
        var html = HtmlReport.Render(report);
        Assert.Contains("LUT: Sony S-Log3 / S-Gamut3.Cine → Rec.709 (Sony XML: s-log3-cine / S-Gamut3Cine)", html);
        var json = report.ToJson();
        Assert.Contains("\"lut\"", json);
        Assert.Contains("\"source\": \"auto\"", json);
    }
}
