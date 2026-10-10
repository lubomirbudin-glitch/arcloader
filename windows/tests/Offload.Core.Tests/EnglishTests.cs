using System.Globalization;
using System.Text.RegularExpressions;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// 0.9.0 "English only": every report renders in English (lang="en", no Czech), numbers and dates are formatted the same
/// whatever the PC's culture, and the local report copies of older versions (`Documents\Offload reporty`) are still read.
public partial class EnglishTests
{
    /// Czech letters, and Czech words that need no diacritics.
    [GeneratedRegex(@"[ěščřžýáíéůúňťďĚŠČŘŽÝÁÍÉŮÚŇŤĎ„]|\b(karta|karty|soubor|soubory|souborů|chyba|Chyba|hotovo|Hotovo|zrušeno|nenalezen|nejde|vypnuto|klipy|klipů|zálohy|Ověřeno|Denní|Souhrnný|Cíle)\b")]
    private static partial Regex Czech();

    static void AssertEnglish(string html)
    {
        Assert.Contains("<html lang=\"en\">", html);
        var m = Czech().Match(html);
        Assert.False(m.Success, $"Czech text in the report: \"{(m.Success ? html.Substring(Math.Max(0, m.Index - 60), Math.Min(120, html.Length - Math.Max(0, m.Index - 60))) : "")}\"");
    }

    /// A card report with everything that has words in it: errors, a clip with a LUT, a content defect, verification
    /// notes, a lighter original, a repair generation.
    static OffloadReport Busy(string cardPath)
    {
        var r = Reports.Sample(cardPath, destError: "HASH MISMATCH: dest 0000000000000001 != source fedcba9876543210");
        r.Clips.Add(new ClipEntry
        {
            Clip = new Clip { Name = "A001C001", Path = "CLIP/A001C001.mov", Format = ClipFormat.Video, Segments = ["CLIP/A001C001.mov"], Bytes = 1000 },
            Info = new ClipInfo { Codec = "prores", Width = 3840, Height = 2160, FrameRate = "24/1", CaptureFrameRate = "35", Duration = 12.5, AudioChannels = 2 },
            ProxyStatus = ProxyStatus.Done,
            Lut = LutUse.None("camera not recognised"),
            Content = new VerifyInProxy.ContentResult { State = VerifyInProxy.ContentResult.Warn, Kind = "decode", Timecode = "01:00:02:13", Message = "corrupt frame" },
        });
        r.Clips.Add(new ClipEntry
        {
            Clip = new Clip { Name = "A001C002", Path = "CLIP/A001C002.mp4", Format = ClipFormat.Video, Segments = ["CLIP/A001C002.mp4"], Bytes = 500 },
            ProxyStatus = ProxyStatus.Skipped,
            ProxyNote = Msg.ProxyLighter(5.2, 10),
        });
        r.VerifyNotes = [Msg.UnstableRead("CLIP/A&B.mov", "RAID_A", [3])];
        r.Proxy = new ProxySettings { Enabled = true, FrameAspect = 1.85 };
        r.SafeToFormatSeconds = 75.5;
        r.CopyPausedSeconds = 2;
        r.Generation = 2;
        r.Kind = OffloadReport.RepairKind;
        return r;
    }

    [Fact]
    public void CardReport_English_NoCzech()
    {
        var html = HtmlReport.Render(Busy(@"D:\dst\2026-10-04\A001"));
        AssertEnglish(html);
        Assert.Contains("<span class=\"badge bad\">ERROR</span>", html);
        Assert.Contains("· repair (generation 2)", html);
        Assert.Contains("35 fps<div class=\"note\">playback 24</div>", html);
        Assert.Contains("no proxy — the original is lighter (5.2 Mb/s ≤ 10 Mb/s)", html);
        AssertEnglish(HtmlReport.Render(Busy(@"D:\dst\2026-10-04\A001"), forPrint: true));
    }

    [Fact]
    public void DayVerifySummaryReports_English_NoCzech()
    {
        using var dir = new TempDir();
        AssertEnglish(DayReport.Html(DayReport.Load(DayFixtures.Day(dir))));

        var v = new VerifyResult { CardPath = @"D:\B\2026-10-06\A001", Basis = "ascmhl", Generations = 2, HashAlgorithms = ["xxh64"], TotalFiles = 3, Ok = 1, Checked = 3 };
        v.Changed.Add("CLIP/b.mov");
        v.Missing.Add("CLIP/c.mov");
        v.Extra.Add("CLIP/x.mov");
        v.FailedSuperseded.Add("CLIP/d.mov");
        AssertEnglish(VerifyReport.Html(v));

        var root = dir.Sub("d1");
        var d1 = SummaryReportTests.Backup(root, "2026-10-06", "A001", [SummaryReportTests.Clip("A001C001", 60)]);
        var model = SummaryReport.Load([SummaryInput.Of(d1), SummaryInput.Of(SummaryReportTests.Entry("Z009", "2026-10-07", Path.Combine(dir.Path, "gone", "2026-10-07", "Z009")))], []);
        AssertEnglish(SummaryReport.Html(model));
    }

    /// The PC's culture never reaches a number or a date the user reads: a Czech Windows formats like any other.
    [Fact]
    public void InvariantFormatting_UnderCzechCulture()
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("cs-CZ");
            Assert.Equal("1,5", 1.5.ToString(CultureInfo.CurrentCulture));   // the culture really is Czech here

            Assert.Equal("3.2 GB", Format.BytesShort(3_200_000_000));
            Assert.Equal("1.70 GB", Format.Bytes3(1_700_000_000));
            Assert.Equal("2.50 GB", Format.Bytes(2_500_000_000));
            Assert.Equal("no proxy — the original is lighter (5.2 Mb/s ≤ 10 Mb/s)", Msg.ProxyLighter(5.2, 10));
            Assert.Equal("24 clips · 1.70 GB · ALEXA 35", JobCard.Sub(new JobCardFacts(30, 24, 1_700_000_000, "ALEXA 35", false)));
            Assert.Equal("Verifying 1,200 / 12,345 files · 3.2 GB / 32 GB · 380 MB/s",
                VerifyJobView.Detail(new VerifyProgress(1199, 12_345, 3_200_000_000, 32_000_000_000, "a"), 380_000_000));
            Assert.Equal("1:32", JobProgress.EtaText(92));
            Assert.Equal("1:02:03", HtmlReport.Duration(3723));
            Assert.Equal("1:02:03", new ClipInfo { Duration = 3723 }.DurationLabel);
            Assert.Equal("23.976", new ClipInfo { FrameRate = "24000/1001" }.FpsLabel);

            var local = new DateTime(2026, 10, 6, 8, 5, 9, DateTimeKind.Local);
            Assert.Equal("2026-10-06 08:05:09", HtmlReport.DateTimeText(local));
            Assert.Equal("2026-10-06 08:05", HistoryText.When(local.ToUniversalTime()));
            var day = HistoryView.Days([new HistoryEntry { Id = "x", CardName = "A001", Finished = local.ToUniversalTime(), Success = true, Verified = true, Destinations = [] }],
                new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Local));
            Assert.Equal("2026-10-06", Assert.Single(day).Title);
            Assert.Equal("08:05", Assert.Single(day[0].Rows).Time);

            var html = HtmlReport.Render(Busy(@"D:\dst\2026-10-04\A001"));
            Assert.Contains("<b>3.00 KB</b>", html);
            Assert.Contains("1:16", html);   // until "safe to format", 75.5 s
            Assert.DoesNotContain("3,00 KB", html);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    /// The local report copies of versions before 0.9.0 are in `Documents\Offload reporty`: the summary report still finds
    /// them there (its fallback when the disk is gone), while new copies go to `Documents\Offload reports`.
    [Fact]
    public void OldLocalFolder_StillRead_NewCopiesGoToTheNewOne()
    {
        using var dir = new TempDir();
        var card = "Q" + Random.Shared.Next(100, 999);
        var root = dir.Sub("d1");
        var d1 = SummaryReportTests.Backup(root, "2026-09-30", card, [SummaryReportTests.Clip(card + "C001", 60)]);
        var mirrored = LocalReports.MirrorCard([d1], LocalReports.OldDefaultRoot);   // as an older version copied it
        try
        {
            Assert.NotNull(mirrored);
            Assert.StartsWith(Path.Combine(LocalReports.Documents, "Offload reporty") + Path.DirectorySeparatorChar, mirrored);
            Assert.True(File.Exists(Path.Combine(mirrored!, ".offload-origin")));
            Directory.Delete(root, recursive: true);   // the disk was handed on

            var roots = SummaryReport.LocalRoots(new AppSettings());
            Assert.Contains(LocalReports.OldDefaultRoot, roots);
            Assert.Equal(LocalReports.DefaultRoot, roots[0]);
            Assert.Equal(Path.Combine(LocalReports.Documents, "Offload reports"), LocalReports.DefaultRoot);
            Assert.Equal(LocalReports.DefaultRoot, LocalReports.RootFor(new OutputSettings()));

            var found = Assert.Single(SummaryReport.Load([SummaryInput.Of(SummaryReportTests.Entry(card, "2026-09-30", d1))], roots).Cards);
            Assert.Equal(SummarySource.LocalCopy, found.Source);
            Assert.Equal(mirrored, found.LocalCopy);
            Assert.Equal(1, found.ClipCount);
        }
        finally
        {
            try { Directory.Delete(Path.Combine(LocalReports.OldDefaultRoot, "2026-09-30", card), recursive: true); }
            catch (IOException) { }
        }
    }

    /// The summary report is saved as `<local root>\summary\<date>_<time>_summary.html`.
    [Fact]
    public void Summary_SavedUnderSummaryFolder()
    {
        using var dir = new TempDir();
        var d1 = SummaryReportTests.Backup(dir.Sub("d1"), "2026-10-06", "A001", [SummaryReportTests.Clip("A001C001", 60)]);
        var saved = SummaryReport.Save(SummaryReport.Load([SummaryInput.Of(d1)], []), dir.Sub("local"), null, null, new DateTime(2026, 10, 10, 14, 32, 5));
        Assert.Equal(Path.Combine(dir.Path, "local", "summary", "2026-10-10_143205_summary.html"), saved.Html);
        AssertEnglish(File.ReadAllText(saved.Html));
    }
}
