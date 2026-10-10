using Offload.Core.History;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Task "Souhrnný report z vybraných záloh" (2026-10-09): one HTML + PDF from backups ticked in the card history — cards
/// of different days and disks, read from the destinations, else from the local copy of the report, else from history
/// alone; never anything written into a card folder or a destination.
public class SummaryReportTests
{
    internal static ClipEntry Clip(string name, double seconds, string? tc = "01:00:00:00", string camera = "Sony FX6", DateTime? recorded = null,
        bool cameraProxy = false, ClipFormat format = ClipFormat.Video) => new()
    {
        Clip = new Clip { Name = name, Path = $"CLIP/{name}.mxf", Segments = [$"CLIP/{name}.mxf"], Bytes = 1000, IsCameraProxy = cameraProxy, Format = format },
        Info = new ClipInfo { Camera = camera, Duration = seconds, FrameRate = "25/1", Timecode = tc, Recorded = recorded },
    };

    internal static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    internal static ReportFile File(int i, long size = 1000, bool verified = true, int day = 6, string? path = null) => new()
    {
        Path = path ?? $"CLIP/f{i}.mxf", Size = size, Modified = Utc(day, 7), Xxh64 = "0123456789abcdef", Destinations = [verified ? "verified" : "written"], Errors = [null],
    };

    /// A report of `card` on `root` (`<root>\<date>\<card>`) listing `files` and `clips`.
    internal static OffloadReport Report(string root, string date, string card, IReadOnlyList<ClipEntry> clips, IReadOnlyList<ReportFile> files,
        bool verified = true, string source = @"E:\", int day = 6) => new()
    {
        CardName = card, Date = date, Source = source, SourceFilesystem = "exFAT",
        Started = Utc(day, 8), CopyFinished = Utc(day, 8, 10), Finished = Utc(day, 8, 15), Verified = verified,
        TotalFiles = files.Count, TotalBytes = files.Sum(f => f.Size),
        Destinations = [new ReportDestination { Root = root, CardPath = Path.Combine(root, date, card), Filesystem = "NTFS", FilesOK = files.Count }],
        Files = files, Skipped = [], Clips = clips.ToList(),
    };

    /// A full copy of `card` on `root` (`<root>\<date>\<card>`) with `files` files of 1000 bytes and `clips`.
    internal static string Backup(string root, string date, string card, IReadOnlyList<ClipEntry> clips, int files = 2, bool verified = true,
        string source = @"E:\", int day = 6)
    {
        var r = Report(root, date, card, clips, Enumerable.Range(1, files).Select(i => File(i, verified: verified, day: day)).ToList(), verified, source, day);
        var path = Path.Combine(root, date, card);
        Directory.CreateDirectory(Path.Combine(path, "_REPORT"));
        System.IO.File.WriteAllText(Path.Combine(path, "_REPORT", "offload_report.json"), r.ToJson());
        return path;
    }

    /// The history entry of the backups at `cardPaths` (one card, several destinations).
    internal static HistoryEntry Entry(string card, string date, params string[] cardPaths) => new()
    {
        Id = HistoryStore.NewId(Utc(6, 9)), CardName = card, Date = date, Source = @"E:\", Finished = Utc(6, 8, 15), Success = true, Verified = true,
        Files = 7, Bytes = 7_000_000,
        Destinations = cardPaths.Select(p => new HistoryDestination { Root = Path.GetDirectoryName(Path.GetDirectoryName(p))!, CardPath = p }).ToList(),
        Generations = [new HistoryGeneration { Seq = 1, Kind = HistoryGeneration.Full, Finished = Utc(6, 8, 15), Files = 7, Bytes = 7_000_000, Verified = true }],
    };

    internal static Dictionary<string, byte[]> Tree(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(dir, f), System.IO.File.ReadAllBytes);

    [Fact]
    public void FromDestination_TheCardsNumbers_AndWhereItIs()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60), Clip("A001C002", 30)]);
        var model = SummaryReport.Load([SummaryInput.Of(Entry("A001", "2026-10-06", d1))], [dir.Sub("local")]);

        var card = Assert.Single(model.Cards);
        Assert.Equal(SummarySource.Destination, card.Source);
        Assert.Equal(d1, card.ReadFrom);
        Assert.Equal(2, card.ClipCount);
        Assert.Equal(2, card.Files);
        Assert.Equal(2000, card.Bytes);
        Assert.Equal(90, card.Seconds);
        Assert.Equal(["Sony FX6"], card.Cameras);
        Assert.True(card.Verified);
        var copy = Assert.Single(card.Copies);
        Assert.True(copy.Connected);
        Assert.True(copy.Verified);
        Assert.Null(card.LocalCopy);
    }

    [Fact]
    public void DiskGone_ReadsTheLocalCopy()
    {
        using var dir = new TempDir();
        var root = dir.Sub("d1");
        var d1 = Backup(root, "2026-10-06", "A001", [Clip("A001C001", 60), Clip("A001C002", 30)]);
        var local = dir.Sub("local");
        var mirrored = LocalReports.MirrorCard([d1], local);
        Directory.Delete(root, recursive: true);   // the disk was handed on

        var model = SummaryReport.Load([SummaryInput.Of(Entry("A001", "2026-10-06", d1))], [local]);

        var card = Assert.Single(model.Cards);
        Assert.Equal(SummarySource.LocalCopy, card.Source);
        Assert.Equal(mirrored, card.LocalCopy);
        Assert.Equal(2, card.ClipCount);
        Assert.Equal(90, card.Seconds);
        var copy = Assert.Single(card.Copies);
        Assert.False(copy.Connected);
        Assert.True(copy.Verified);   // what the report says about that disk
        var html = SummaryReport.Html(model);
        Assert.Contains("report copy on this computer", html);
        Assert.Contains("not connected", html);
    }

    [Fact]
    public void LocalCopy_OfAnotherBackupWithTheSameName_IsNotTaken()
    {
        using var dir = new TempDir();
        var root = dir.Sub("d1");
        var d1 = Backup(root, "2026-10-06", "A001", [Clip("A001C001", 60)], source: @"F:\");   // another card, another reader
        var local = dir.Sub("local");
        LocalReports.MirrorCard([d1], local);
        Directory.Delete(root, recursive: true);

        var card = Assert.Single(SummaryReport.Load([SummaryInput.Of(Entry("A001", "2026-10-06", d1))], [local]).Cards);

        Assert.Equal(SummarySource.None, card.Source);
        Assert.Null(card.LocalCopy);
    }

    [Fact]
    public void NeitherAvailable_SaysSo_TheReportIsStillMade()
    {
        using var dir = new TempDir();
        var gone = Path.Combine(dir.Path, "nothing", "2026-10-06", "A001");
        var model = SummaryReport.Load([SummaryInput.Of(Entry("A001", "2026-10-06", gone))], [dir.Sub("local")]);

        var card = Assert.Single(model.Cards);
        Assert.Equal(SummarySource.None, card.Source);
        Assert.Equal(7, card.Files);              // history knows the files and the size
        Assert.Equal(7_000_000, card.Bytes);
        Assert.Equal(0, card.ClipCount);
        Assert.Null(card.First);
        Assert.False(Assert.Single(card.Copies).Connected);
        var html = SummaryReport.Html(model);
        Assert.Contains("report not available", html);
        Assert.Contains("A001", html);
    }

    [Fact]
    public void Claps_ByRecordTime_WhenEveryClipHasIt()
    {
        using var dir = new TempDir();
        // C003 was recorded first (the camera's clip counter was reset): the record time decides, not the number.
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001",
        [
            Clip("A001C001", 10, tc: "10:00:00:00", recorded: Utc(6, 9)),
            Clip("A001C002", 10, tc: "11:00:00:00", recorded: Utc(6, 10)),
            Clip("A001C003", 10, tc: "08:00:00:00", recorded: Utc(6, 7)),
        ]);
        var card = Assert.Single(SummaryReport.Load([SummaryInput.Of(d1)], [dir.Sub("local")]).Cards);

        Assert.Equal("A001C003", card.First!.Clip);
        Assert.Equal("08:00:00:00", card.First.Timecode);
        Assert.Equal("A001C002", card.Last!.Clip);
        Assert.Equal("11:00:00:00", card.Last.Timecode);
    }

    [Fact]
    public void Claps_ByClipNumber_WhenARecordTimeIsMissing()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001",
        [
            Clip("A001C010", 10, tc: "12:00:00:00", recorded: Utc(6, 7)),
            Clip("A001C002", 10, tc: "09:00:00:00"),                       // no record time: by the number
            Clip("A001C001", 10, tc: "08:30:00:00", recorded: Utc(6, 12)),
            Clip("A001C001_proxy", 10, tc: "00:00:00:00", cameraProxy: true),  // a camera proxy is no clip
        ]);
        var card = Assert.Single(SummaryReport.Load([SummaryInput.Of(d1)], [dir.Sub("local")]).Cards);

        Assert.Equal(3, card.ClipCount);
        Assert.Equal("A001C001", card.First!.Clip);
        Assert.Equal("08:30:00:00", card.First.Timecode);
        Assert.Equal("A001C010", card.Last!.Clip);   // C010 after C002: the number, not the text
    }

    [Fact]
    public void Alexa_TimeOfDayTimecode()
    {
        using var dir = new TempDir();
        var a = Backup(dir.Sub("d1"), "2026-10-06", "A001",
        [
            Clip("A001C001_261006_R1AB", 10, tc: "14:02:11:05", camera: "ARRI ALEXA 35", format: ClipFormat.Arriraw),
            Clip("A001C002_261006_R1AB", 10, tc: "14:30:00:00", camera: "ARRI ALEXA 35", format: ClipFormat.Arriraw),
        ]);
        var b = Backup(dir.Sub("d2"), "2026-10-06", "B001", [Clip("B001C001", 10, tc: "01:00:00:00")]);
        var model = SummaryReport.Load([SummaryInput.Of(a), SummaryInput.Of(b)], [dir.Sub("local")]);

        var alexa = model.Cards.Single(c => c.Name == "A001");
        Assert.Equal("14:02:11:05", alexa.First!.Timecode);
        Assert.True(alexa.First.TimeOfDay);
        Assert.False(model.Cards.Single(c => c.Name == "B001").First!.TimeOfDay);
        var html = SummaryReport.Html(model);
        Assert.Contains("14:02:11:05", html);
        Assert.Contains("time-of-day TC", html);
    }

    [Fact]
    public void Totals_OverDaysAndDisks_EveryGeneration()
    {
        using var dir = new TempDir();
        var a = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60), Clip("A001C002", 30)], files: 2);
        // An append (generation 2) adds a clip and a file; a repair (generation 3) rewrites that file, no new clip.
        var append = Report(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C003", 15)], [File(3, size: 5000, path: "CLIP/A001C003.mxf")]);
        append.Generation = 2;
        System.IO.File.WriteAllText(Path.Combine(a, "_REPORT", "offload_report_0002.json"), append.ToJson());
        var repair = Report(dir.Sub("d1"), "2026-10-06", "A001", [], [File(3, size: 5000, path: "CLIP/A001C003.mxf")]);
        repair.Generation = 3;
        repair.Kind = OffloadReport.RepairKind;
        System.IO.File.WriteAllText(Path.Combine(a, "_REPORT", "offload_report_0003.json"), repair.ToJson());
        var b = Backup(dir.Sub("d2"), "2026-10-07", "B001", [Clip("B001C001", 120, camera: "ARRI ALEXA Mini LF")], files: 4, day: 7);

        var model = SummaryReport.Load([SummaryInput.Of(b), SummaryInput.Of(a)], [dir.Sub("local")]);

        Assert.Equal(["A001", "B001"], model.Cards.Select(c => c.Name));   // by day
        var cardA = model.Cards[0];
        Assert.Equal(3, cardA.ClipCount);
        Assert.Equal(3, cardA.Files);
        Assert.Equal(7000, cardA.Bytes);
        Assert.Equal(105, cardA.Seconds);
        Assert.Equal(["2026-10-06", "2026-10-07"], model.Days.Select(d => d.Date));
        Assert.Equal(4, model.Total.ClipCount);
        Assert.Equal(7, model.Total.Files);
        Assert.Equal(11000, model.Total.Bytes);
        Assert.Equal(225, model.Total.Seconds);
        Assert.Equal(["ARRI ALEXA Mini LF", "Sony FX6"], model.Total.Cameras);
        Assert.Equal("A001C001", model.Total.First!.Clip);
        Assert.Equal("B001C001", model.Total.Last!.Clip);
        Assert.Single(model.Days[1].Cards);
        Assert.Equal(2, model.Total.VerifiedCards);
        var html = SummaryReport.Html(model);
        Assert.Contains("3:45", html);   // 225 s
        Assert.Contains("Total", html);
    }

    [Fact]
    public void OneBackupTwoDisks_OneGone_ReadFromTheOther_BothListed()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        var gone = Path.Combine(dir.Path, "d2", "2026-10-06", "A001");
        var card = Assert.Single(SummaryReport.Load([SummaryInput.Of(Entry("A001", "2026-10-06", gone, d1))], [dir.Sub("local")]).Cards);

        Assert.Equal(SummarySource.Destination, card.Source);
        Assert.Equal(d1, card.ReadFrom);
        Assert.Equal([false, true], card.Copies.Select(c => c.Connected));
    }

    [Fact]
    public void FolderInput_ADayFolder_EveryCardInIt_AndAnUnknownFolderIsAnError()
    {
        using var dir = new TempDir();
        var root = dir.Sub("d1");
        Backup(root, "2026-10-06", "A001", [Clip("A001C001", 60)]);
        Backup(root, "2026-10-06", "B001", [Clip("B001C001", 60)]);

        var inputs = SummaryReport.Inputs([Path.Combine(root, "2026-10-06")], []);
        Assert.Equal(2, inputs.Count);
        var e = Assert.Throws<ArgumentException>(() => SummaryReport.Inputs([dir.Sub("empty")], []));
        Assert.Contains("is not a card folder", e.Message);
        Assert.Throws<ArgumentException>(() => SummaryReport.Inputs(["20990101T000000Z-deadbeef"], []));
    }

    [Fact]
    public void Inputs_AHistoryId()
    {
        var entry = Entry("A001", "2026-10-06", @"X:\nowhere\2026-10-06\A001");
        var input = Assert.Single(SummaryReport.Inputs([entry.Id], [entry]));
        Assert.Same(entry, input.Entry);
    }

    [Fact]
    public void Save_LocallyIntoSouhrn_AndOntoAPickedDisk_NeverOverwriting_NothingElseTouched()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        var before = Tree(dir.Sub("d1"));
        var model = SummaryReport.Load([SummaryInput.Of(d1)], [dir.Sub("local")]);
        var picked = dir.Sub("picked");
        var now = new DateTime(2026, 10, 10, 14, 32, 5);
        System.IO.File.WriteAllText(Path.Combine(picked, "2026-10-10_143205_summary.html"), "the user's own");

        var saved = SummaryReport.Save(model, dir.Sub("local"), picked, null, now);

        Assert.Equal(Path.Combine(dir.Path, "local", "summary", "2026-10-10_143205_summary.html"), saved.Html);
        Assert.Contains("Summary report", System.IO.File.ReadAllText(saved.Html));
        Assert.Equal(Path.Combine(picked, "2026-10-10_143205_summary_2.html"), saved.ExtraHtml);
        Assert.Null(saved.ExtraError);
        Assert.Equal("the user's own", System.IO.File.ReadAllText(Path.Combine(picked, "2026-10-10_143205_summary.html")));
        // Review S-10: no PDF was printed (tests skip Edge) — the copy on the disk has no link to a file that is not there.
        Assert.Null(saved.ExtraPdf);
        Assert.DoesNotContain("Open PDF report", System.IO.File.ReadAllText(saved.ExtraHtml!));

        var again = SummaryReport.Save(model, dir.Sub("local"), null, null, now);   // the same second: a suffix, never over
        Assert.Equal(Path.Combine(dir.Path, "local", "summary", "2026-10-10_143205_summary_2.html"), again.Html);
        DayFixtures.AssertSame(before, Tree(dir.Sub("d1")));
    }

    [Fact]
    public void Save_IntoACardFolder_IsRefused_TheLocalOneIsMade()
    {
        using var dir = new TempDir();
        var d1 = Backup(dir.Sub("d1"), "2026-10-06", "A001", [Clip("A001C001", 60)]);
        Directory.CreateDirectory(Path.Combine(d1, "CLIP"));
        var model = SummaryReport.Load([SummaryInput.Of(d1)], [dir.Sub("local")]);
        var before = Tree(d1);

        var saved = SummaryReport.Save(model, dir.Sub("local"), Path.Combine(d1, "CLIP"), null, new DateTime(2026, 10, 10, 9, 0, 0));

        Assert.True(System.IO.File.Exists(saved.Html));
        Assert.Null(saved.ExtraHtml);
        Assert.Contains("inside the backup folder of a card", saved.ExtraError);
        DayFixtures.AssertSame(before, Tree(d1));
    }

    [Fact]
    public void LocalRoot_TheLocalReportsFolder_EvenWhenTheCopyIsSwitchedOff()
    {
        Assert.Equal(LocalReports.DefaultRoot, SummaryReport.LocalRoot(new OutputSettings { LocalReport = false }));
        Assert.Equal(@"D:\Reporty", SummaryReport.LocalRoot(new OutputSettings { LocalReportFolder = @" D:\Reporty " }));
    }

    [Fact]
    public void Recorded_FromFfprobeCreationTime_RoundTripsInTheReport_OldReportsWithout()
    {
        var info = FFProbe.Parse("""
            {"streams":[{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"avg_frame_rate":"25/1","tags":{"timecode":"10:00:00:00"}}],
             "format":{"duration":"10.0","tags":{"creation_time":"2026-10-06T08:15:30.000000Z"}}}
            """);
        Assert.Equal(new DateTime(2026, 10, 6, 8, 15, 30, DateTimeKind.Utc), info.Recorded);
        Assert.Equal(DateTimeKind.Utc, info.Recorded!.Value.Kind);

        var r = Reports.Sample(@"D:\Zaloha\2026-10-04\A001");
        r.Clips = [new ClipEntry { Clip = new Clip { Name = "A", Path = "A.mov", Segments = ["A.mov"] }, Info = info }];
        var json = r.ToJson();
        Assert.Contains("\"recorded\": \"2026-10-06T08:15:30Z\"", json);
        Assert.Equal(info.Recorded, OffloadReport.FromJson(json).Clips[0].Info!.Recorded);

        Assert.Null(FFProbe.Parse("""{"streams":[],"format":{"tags":{"creation_time":"nonsense"}}}""").Recorded);
        Assert.DoesNotContain("recorded", Reports.Sample(@"D:\Zaloha\2026-10-04\A001").ToJson());
    }
}

/// `offload summary-report` (task 2026-10-09).
[Collection("Console")]
public class CliSummaryReportTests
{
    static (int Exit, string Out, string Err) Run(string settingsDir, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var oldPath = Offload.Cli.CliProject.SettingsPath;
        Console.SetOut(so);
        Console.SetError(se);
        Offload.Cli.CliProject.SettingsPath = () => Path.Combine(settingsDir, "settings.json");
        try { return (Offload.Cli.SummaryReportCommand.Create().Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            Offload.Cli.CliProject.SettingsPath = oldPath;
        }
    }

    [Fact]
    public void SummaryReport_LocalAndIntoTheFolder_Exit0_UnknownExit1()
    {
        using var dir = new TempDir();
        var a = SummaryReportTests.Backup(dir.Sub("d1"), "2026-10-06", "A001", [SummaryReportTests.Clip("A001C001", 60)]);
        var b = SummaryReportTests.Backup(dir.Sub("d2"), "2026-10-07", "B001", [SummaryReportTests.Clip("B001C001", 60)], day: 7);
        var picked = dir.Sub("picked");

        var ok = Run(dir.Sub("settings"), a, Path.GetDirectoryName(b)!, "-o", picked);

        Assert.True(ok.Exit == 0, ok.Out + ok.Err);
        var lines = ok.Out.Trim().Split('\n').Select(l => l.Trim()).ToList();
        var html = lines[0]["Summary report: ".Length..];
        Assert.StartsWith(Path.Combine(LocalReports.DefaultRoot, "summary") + Path.DirectorySeparatorChar, html);
        Assert.EndsWith("_summary.html", html);
        Assert.Contains("B001", System.IO.File.ReadAllText(html));
        Assert.Single(Directory.GetFiles(picked, "*_summary.html"));
        Assert.Contains(lines, l => l.StartsWith("Summary report also on the selected disk: " + picked, StringComparison.Ordinal));

        var bad = Run(dir.Sub("settings"), Path.Combine(dir.Path, "nope"));
        Assert.Equal(1, bad.Exit);
        Assert.Contains("Error: \"" + Path.Combine(dir.Path, "nope") + "\" is neither a card folder nor a backup id", bad.Err);
    }
}
