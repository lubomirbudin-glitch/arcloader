using System.Text;
using Offload.Core.Outputs;
using Offload.Core.Verification;

namespace Offload.Core.Tests;

/// A day folder (spec 3.2 tests): a Windows card with clips and LUTs, a Mac report without the new keys and a failed
/// file, a card with an unverified append, a damaged report, a card folder without one, a hand-copied sound folder.
public static class DayFixtures
{
    static readonly LutUse LogC4 = new("auto", "arri-logc4-awg4", "ARRI LogC4 → Rec.709", "ARRI");

    static ClipEntry Clip(string path, double seconds, ProxyStatus proxy) => new()
    {
        Clip = new Clip { Name = Path.GetFileNameWithoutExtension(path), Path = path, Segments = [path], Bytes = 1000 },
        Info = new ClipInfo { Camera = "ARRI ALEXA 35", Duration = seconds, FrameRate = "25/1", Timecode = "01:00:00:00" },
        Lut = LogC4,
        ProxyStatus = proxy,
    };

    static void MainReport(string cardPath, OffloadReport report) => MainReport(cardPath, report.ToJson());

    static void MainReport(string cardPath, string json)
    {
        Directory.CreateDirectory(Path.Combine(cardPath, "_REPORT"));
        File.WriteAllText(Path.Combine(cardPath, "_REPORT", "offload_report.json"), json);
    }

    /// Ruling M2: what the Mac writes — no "generation", "lut", "proxyEncoder", "md5", no "clips"; the second file failed.
    const string MacReport = """
        {
          "cardName" : "B001", "copyFinished" : "2026-10-06T07:10:00Z", "date" : "2026-10-06",
          "destinations" : [ { "cardPath" : "/Volumes/Zaloha/2026-10-06/B001", "filesFailed" : 1, "filesOK" : 1, "filesystem" : "apfs", "root" : "/Volumes/Zaloha" } ],
          "files" : [ { "destinations" : [ "verified" ], "errors" : [ null ], "modified" : "2026-10-06T07:00:00Z", "path" : "CLIP/A&B.mov", "size" : 1000, "xxh64" : "0123456789abcdef" },
                      { "destinations" : [ "failed" ], "errors" : [ "write failed" ], "modified" : "2026-10-06T07:00:00Z", "path" : "CLIP/c 02.mov", "size" : 2000, "xxh64" : "fedcba9876543210" } ],
          "finished" : "2026-10-06T07:11:00Z", "hashAlgorithm" : "xxh64", "skipped" : [ ], "source" : "/Volumes/B001", "sourceFilesystem" : "exfat",
          "started" : "2026-10-06T07:09:00Z", "tool" : "offload", "totalBytes" : 3000, "totalFiles" : 2, "verified" : true, "version" : "0.1.0"
        }
        """;

    /// `<dir>\D\2026-10-06\` with the cards A001 … E001, a hand-copied `SOUND` folder (no card, ruling M13), a `_REPORT`
    /// holding a verify report of a foreign backup (plan 6), a hidden folder.
    public static string Day(TempDir dir)
    {
        var day = dir.Sub("D", "2026-10-06");
        var a = Path.Combine(day, "A001");
        var ra = Reports.Sample(a);
        ra.Clips = [Clip("CLIP/A&B.mov", 60, ProxyStatus.Done), Clip("CLIP/c 02.mov", 30, ProxyStatus.Failed)];
        MainReport(a, ra);
        MainReport(Path.Combine(day, "B001"), MacReport);
        var c = Path.Combine(day, "C001");
        MainReport(c, Reports.Sample(c));
        var append = Reports.Sample(c, verified: false);
        append.Generation = 2;
        File.WriteAllText(Path.Combine(c, "_REPORT", "offload_report_0002.json"), append.ToJson());
        MainReport(Path.Combine(day, "D001"), "{broken");
        // A card folder whose report is not there (yet): its MHL folder makes it a card.
        Directory.CreateDirectory(Path.Combine(day, "E001", "CLIP"));
        Directory.CreateDirectory(Path.Combine(day, "E001", "ascmhl"));
        TestFs.WriteRandom(Path.Combine(day, "SOUND", "take1.wav"), 100, 1);
        Directory.CreateDirectory(Path.Combine(day, "_REPORT"));
        File.WriteAllText(Path.Combine(day, "_REPORT", "X001_verify_2026-10-06_120000Z.json"), "{}");
        File.WriteAllText(Path.Combine(day, "_REPORT", "X001_verify_2026-10-06_120000Z.html"), "<html>verify</html>");
        Directory.CreateDirectory(Path.Combine(day, ".offload-tmp"));
        return day;
    }

    /// Every file under `day` but its `_REPORT` (the card folders): relative path → bytes.
    public static Dictionary<string, byte[]> CardFiles(string day) =>
        Directory.GetFiles(day, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(day, f).StartsWith("_REPORT" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetRelativePath(day, f), File.ReadAllBytes);

    public static void AssertSame(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, bytes) in before) Assert.True(bytes.AsSpan().SequenceEqual(after[path]), path);
    }
}

public class DayReportTests
{
    [Fact]
    public void Load_EveryCardFolder_ButTheDaysReportFolder()
    {
        using var dir = new TempDir();
        var day = DayReport.Load(DayFixtures.Day(dir));

        Assert.Equal(["A001", "B001", "C001", "D001", "E001", "SOUND"], day.Folders.Select(c => c.Name));
        Assert.Equal(["A001", "B001", "C001", "D001", "E001"], day.Cards.Select(c => c.Name));
        Assert.False(day.Folders[5].IsCard);   // ruling M13: no _REPORT, no ascmhl
        Assert.Equal("2026-10-06", day.Date);
        Assert.Equal(Path.Combine(dir.Path, "D"), day.Root);
        Assert.Equal(1, day.VerifiedCards);   // A001; B001 has a failed file, C001 an unverified append
        var a = day.Cards[0];
        Assert.Equal((2, 90.0, 1, 1), (a.ClipCount, a.Seconds, a.ProxiesDone, a.ProxiesFailed));
        Assert.Equal(["ARRI ALEXA 35"], a.Cameras);
        Assert.Equal(2, a.Luts["ARRI LogC4 → Rec.709"]);
        Assert.Equal(2, day.Cards[1].ClipCount);   // the Mac report has no clips: found in its files
        Assert.Equal(["CLIP/c 02.mov"], day.Cards[1].Record.FailedFiles);
        Assert.True(day.Cards[4].NoReport);
        Assert.NotNull(day.Cards[3].Unreadable);
    }

    [Fact]
    public void Html_TilesTableProblemsLuts()
    {
        using var dir = new TempDir();
        var html = DayReport.Html(DayReport.Load(DayFixtures.Day(dir)));

        Assert.Contains("<h1>Denní report — 6. 10. 2026</h1>", html);
        Assert.Contains("VYŽADUJE POZORNOST", html);
        Assert.Contains("<b>5</b><span>karet</span>", html);
        Assert.Contains("<b>6</b><span>souborů</span>", html);
        Assert.Contains($"<b>{Format.Bytes(9000)}</b><span>data</span>", html);
        Assert.Contains("<b>6</b><span>klipů</span>", html);
        Assert.Contains("<b>1:30</b><span>celková délka</span>", html);
        Assert.Contains("<b>1 / 5</b><span>ověřené karty</span>", html);
        Assert.Contains("<td>E001</td><td colspan=\"11\" class=\"warn\">bez reportu (probíhá, nebo byl přerušen)</td>", html);
        Assert.Contains("<td>D001</td><td colspan=\"11\" class=\"warn\">report nejde přečíst</td>", html);
        Assert.Contains("<td>SOUND</td><td colspan=\"11\" class=\"muted\">jiná složka (bez reportu)</td>", html);
        Assert.Contains("<td>1 / 1</td><td class=\"wrap\">ARRI LogC4 → Rec.709</td><td class=\"num\">—</td>", html);   // A001
        Assert.Contains("<td class=\"warn\">NEOVĚŘENO</td>", html);   // C001
        Assert.Contains("<td class=\"num\">2</td></tr>", html);      // C001: two generations
        Assert.Contains("1 soubor selhal: CLIP/c 02.mov", html);    // B001
        Assert.Contains("neověřeno — kopie nebyla přečtena zpět", html);
        Assert.Contains("<tr><td>ARRI LogC4 → Rec.709</td><td class=\"num\">2</td></tr>", html);
        Assert.DoesNotContain("<tr><td>SOUND</td><td class=", html);   // ruling M13: not a problem
        Assert.Contains("<footer>offload ", html);
        Assert.Contains("@page{size:A4 landscape", DayReport.Html(DayReport.Load(Path.Combine(dir.Path, "D", "2026-10-06")), forPrint: true));
    }

    // Task 1–2 review: an unreadable report is shown in a Czech sentence, its English reason only in parentheses.
    [Fact]
    public void Problems_UnreadableReports_InACzechSentence()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        File.WriteAllText(Path.Combine(day, "A001", "_REPORT", "offload_report_0002.json"), "{nope");

        var problems = DayReport.Problems(DayReport.Load(day));

        var a = Assert.Single(problems, p => p.Card == "A001");
        Assert.StartsWith("report doplnění nejde přečíst (offload_report_0002.json: ", a.Text);
        Assert.EndsWith(")", a.Text);
        Assert.True(a.Bad);
        var d = Assert.Single(problems, p => p.Card == "D001");
        Assert.StartsWith("report nejde přečíst (", d.Text);
        Assert.DoesNotContain(problems, p => p.Card == "SOUND");
        Assert.Contains(problems, p => p.Card == "E001" && p.Text == "bez reportu (probíhá, nebo byl přerušen)");
    }

    [Fact]
    public void Html_EscapesCardNames()
    {
        using var dir = new TempDir();
        var day = dir.Sub("2026-10-06");
        Directory.CreateDirectory(Path.Combine(day, "F&G", "_REPORT"));
        Assert.Contains("<td>F&amp;G</td>", DayReport.Html(DayReport.Load(day)));
        Assert.Contains("<b>0 / 1</b>", DayReport.Html(DayReport.Load(day)));
    }

    // Spec 3.2: HTML and the day's clip lists in <day>\_REPORT; the PDF is a convenience (skipped in tests, logged); the
    // day folder is still not a card for "Ověřit zálohu" (its _REPORT has no report or hash list). Ruling M4: plan 6's
    // verify reports in the same folder survive, also when ALE is switched on and off (the stale-ALE delete runs); the
    // card folders are never touched.
    [Fact]
    public void Write_HtmlAndDayClipLists_DayIsNoCard()
    {
        using var dir = new TempDir();
        var dayPath = DayFixtures.Day(dir);
        var rep = Path.Combine(dayPath, "_REPORT");
        var verifyJson = File.ReadAllBytes(Path.Combine(rep, "X001_verify_2026-10-06_120000Z.json"));
        var verifyHtml = File.ReadAllBytes(Path.Combine(rep, "X001_verify_2026-10-06_120000Z.html"));
        var cards = DayFixtures.CardFiles(dayPath);
        var log = new List<string>();

        var html = DayReport.Write(dayPath, new OutputSettings { ClipListCsv = true, ClipListAle = true }, log.Add);

        Assert.Equal(Path.Combine(rep, "2026-10-06_day_report.html"), html);
        Assert.StartsWith("<!doctype html>", File.ReadAllText(html, Encoding.UTF8));
        var csv = File.ReadAllLines(Path.Combine(rep, "2026-10-06_clips.csv"));
        Assert.Equal(1 + 6, csv.Length);
        Assert.Equal(["A001", "A001", "B001", "B001", "C001", "C001"], csv.Skip(1).Select(l => l.Split(',')[3]));
        Assert.True(File.Exists(Path.Combine(rep, "2026-10-06_clips.ale")));
        Assert.Contains(log, m => m.StartsWith("day report PDF ", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(rep, "*.tmp"));
        Assert.Empty(Directory.GetFiles(rep, "*.new"));
        // The day's cards (E001 by its ascmhl), never the day itself nor its _REPORT.
        Assert.Equal(["A001", "B001", "C001", "D001", "E001"], VerifyTargets.Resolve(dayPath).Select(Path.GetFileName));

        DayReport.Write(dayPath, new OutputSettings { ClipListCsv = true, ClipListAle = false });
        DayReport.Write(dayPath, new OutputSettings { ClipListCsv = true, ClipListAle = true });

        Assert.Equal(verifyJson, File.ReadAllBytes(Path.Combine(rep, "X001_verify_2026-10-06_120000Z.json")));
        Assert.Equal(verifyHtml, File.ReadAllBytes(Path.Combine(rep, "X001_verify_2026-10-06_120000Z.html")));
        Assert.Equal(
            ["2026-10-06_clips.ale", "2026-10-06_clips.csv", "2026-10-06_day_report.html", "X001_verify_2026-10-06_120000Z.html", "X001_verify_2026-10-06_120000Z.json"],
            Directory.GetFiles(rep).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        DayFixtures.AssertSame(cards, DayFixtures.CardFiles(dayPath));
    }

    // Data safety: a folder without a card that has its report (a destination root, a card folder of its own) never
    // gets a `_REPORT`; a card folder named `_REPORT` (ruling M8) is refused — the day files would land in that card.
    [Fact]
    public void Write_NotADay_RefusedAndNothingCreated()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        var root = Path.GetDirectoryName(day)!;

        var notADay = Assert.Throws<InvalidOperationException>(() => DayReport.Write(root, new OutputSettings()));
        Assert.Equal($"Ve složce {root} není žádná karta s reportem — vyberte složku dne na cíli (<cíl>\\<datum>).", notADay.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "_REPORT")));
        Assert.Throws<InvalidOperationException>(() => DayReport.Write(Path.Combine(day, "E001"), new OutputSettings()));
        Assert.False(Directory.Exists(Path.Combine(day, "E001", "_REPORT")));

        var other = dir.Sub("X", "2026-10-07");
        var a = Path.Combine(other, "A001");
        Reports.Sample(a).Write(a, includeHashRecords: false);   // a real card: the folder is a day
        var card = Path.Combine(other, "_REPORT");
        Reports.Sample(card).Write(card, includeHashRecords: false);   // a card folder that is called _REPORT
        Directory.CreateDirectory(Path.Combine(card, "ascmhl"));
        var before = DayFixtures.CardFiles(card);
        var onCard = Assert.Throws<InvalidOperationException>(() => DayReport.Write(other, new OutputSettings()));
        Assert.Equal($"Ve složce dne {other} je karta „_REPORT“ — denní report by se zapsal do ní, proto se nevytvoří.", onCard.Message);
        DayFixtures.AssertSame(before, DayFixtures.CardFiles(card));
        Assert.False(File.Exists(Path.Combine(card, "2026-10-07_day_report.html")));
    }
}

/// Ruling M1: a wall-clock budget, measured with nothing else running.
[CollectionDefinition("Timing", DisableParallelization = true)]
public class TimingCollection;

[Collection("Timing")]
public class DayReportTimingTests
{
    // Spec 5.5: a day of 20 cards — reading the reports and the HTML within a second.
    [Fact]
    public void TwentyCards_HtmlUnderOneSecond()
    {
        using var dir = new TempDir();
        var day = dir.Sub("2026-10-06");
        for (int c = 1; c <= 20; c++)
        {
            var card = Path.Combine(day, $"A{c:000}");
            var r = Reports.Sample(card);
            r.Clips = Enumerable.Range(1, 100).Select(i => new ClipEntry
            {
                Clip = new Clip { Name = $"A{c:000}C{i:000}", Path = $"CLIP/A{c:000}C{i:000}.mov", Segments = [$"CLIP/A{c:000}C{i:000}.mov"], Bytes = 1 },
                Info = new ClipInfo { Camera = "ARRI ALEXA 35", Duration = 30, FrameRate = "25/1" },
            }).ToList();
            Directory.CreateDirectory(Path.Combine(card, "_REPORT"));
            File.WriteAllText(Path.Combine(card, "_REPORT", "offload_report.json"), r.ToJson());
        }
        DayReport.Html(DayReport.Load(day));   // warm-up (JIT)

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var html = DayReport.Html(DayReport.Load(day));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"{clock.ElapsedMilliseconds} ms");
        Assert.Contains("<b>2000</b><span>klipů</span>", html);
    }
}

/// Launches Edge: in the Edge collection.
[Collection("Edge")]
public class DayReportPdfTests
{
    [EdgeFact]
    public void Write_PrintsThePdf()
    {
        using var pdfs = PdfReport.PrintHere();
        using var dir = new TempDir();
        var dayPath = DayFixtures.Day(dir);

        DayReport.Write(dayPath, new OutputSettings());

        var rep = Path.Combine(dayPath, "_REPORT");
        var pdf = File.ReadAllBytes(Path.Combine(rep, "2026-10-06_day_report.pdf"));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Empty(Directory.GetFiles(rep, ".*"));   // no temp file of the print left behind

        // A reader with delete sharing (a browser, the Mac over SMB) does not stop the next print: ReplaceFile.
        using (new FileStream(Path.Combine(rep, "2026-10-06_day_report.pdf"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var log = new List<string>();
            DayReport.Write(dayPath, new OutputSettings(), log.Add);
            Assert.DoesNotContain(log, m => m.StartsWith("day report PDF ", StringComparison.Ordinal));
        }
    }
}
