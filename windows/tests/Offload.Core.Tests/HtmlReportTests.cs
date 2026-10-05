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
        Assert.Contains("offload 0.1.0-win", html);
    }

    [Fact]
    public void Failed_ListsErrorsPerDestination()
    {
        var html = HtmlReport.Render(Reports.Sample(Card, destError: "boom"));
        Assert.Contains("<span class=\"badge bad\">CHYBA</span>", html);
        Assert.Contains("Chyby (1)", html);
        Assert.Contains("cíl 1: boom", html);
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
        Assert.Contains("@page{size:A4 landscape;margin:28pt}", HtmlReport.Render(Reports.Sample(Card), forPrint: true));

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
        Assert.Contains("<td class=\"ok\">hotovo</td>", html);
        Assert.Contains("2 ch", html);
        Assert.Contains("Proxy: H.264, 1080p, výřez 1.66, LUT: bez LUT", html);
        r.ProxyEncoder = "h264_nvenc";
        Assert.Contains("Proxy: H.264 (h264_nvenc), 1080p, výřez 1.66, LUT: bez LUT", HtmlReport.Render(r));
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
}
