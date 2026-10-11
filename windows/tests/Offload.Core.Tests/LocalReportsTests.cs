using Offload.Core.Desktop;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Task "Kopie reportu i na počítači" (2026-10-09): every card's `_REPORT` and `ascmhl` are copied to
/// <Dokumenty>\Offload reporty\<datum>\<karta>\ and refreshed whenever the report is rewritten; never media, never the card.
public class LocalReportsTests
{
    static OffloadOptions Options() => AppendFixtures.Options();

    static void Write(string path, string text = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    static string Report(int generation, DateTime finished, string card = "A001", bool pending = false, string started = "2026-10-09T08:00:00Z") =>
        $$"""{"cardName":"{{card}}","date":"2026-10-09","source":"F:\\","sourceFilesystem":"exFAT","started":"{{started}}","copyFinished":"{{started}}","finished":"{{finished:yyyy-MM-ddTHH:mm:ssZ}}","verified":true,"generation":{{generation}},"destinations":[],"files":[],"skipped":[]{{(pending ? ",\"verifyPending\":true" : "")}}}""";

    /// A card folder on a destination as an offload leaves it: reports, MHL, proxies, the clips, a set-aside copy.
    static string CardFolder(string root, string card = "A001", string date = "2026-10-09", int generation = 1, DateTime? finished = null,
        string started = "2026-10-09T08:00:00Z")
    {
        var path = Path.Combine(root, date, card);
        var at = finished ?? new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
        Write(Path.Combine(path, "_REPORT", "offload_report.json"), Report(1, at, card, started: started));
        if (generation > 1) Write(Path.Combine(path, "_REPORT", $"offload_report_{generation:0000}.json"), Report(generation, at, card, started: started));
        Write(Path.Combine(path, "_REPORT", "offload_report.txt"));
        Write(Path.Combine(path, "_REPORT", $"{card}_report.html"), "<html>");
        Write(Path.Combine(path, "_REPORT", $"{card}_report.pdf"));
        Write(Path.Combine(path, "_REPORT", $"{card}.xxh64"));
        Write(Path.Combine(path, "_REPORT", $"{card}.md5"));
        Write(Path.Combine(path, "_REPORT", $"{card}_clips.csv"));
        Write(Path.Combine(path, "_REPORT", $"{card}_clips.ale"));
        Write(Path.Combine(path, "_REPORT", "offload-manifest.jsonl"));
        Write(Path.Combine(path, "_REPORT", "overeni_2026-10-09.html"));
        Write(Path.Combine(path, "_REPORT", "neshoda_0002", "CLIP", "A001C001.mov"), "media set aside");   // a repair's moved copy
        Write(Path.Combine(path, "_REPORT", "stray.mov"), "media");
        Write(Path.Combine(path, "ascmhl", "0001_A001_2026-10-09_080000Z.mhl"));
        Write(Path.Combine(path, "ascmhl", "ascmhl_chain.xml"));
        Write(Path.Combine(path, "_PROXY", "A001C001.mov"), "proxy");
        Write(Path.Combine(path, "CLIP", "A001C001.mov"), "clip");
        return path;
    }

    static List<string> Files(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f)).Order(StringComparer.OrdinalIgnoreCase).ToList();

    [Fact]
    public void MirrorCard_ReportAndMhl_NeverMedia_DestinationUnchanged()
    {
        using var dir = new TempDir();
        var card = CardFolder(dir.Sub("d1"));
        var before = AppendFixtures.Tree(card);
        var local = dir.Sub("local");

        var copied = LocalReports.MirrorCard([card], local);

        Assert.Equal(Path.Combine(local, "2026-10-09", "A001"), copied);
        var files = Files(copied!);
        Assert.Contains(@"_REPORT\offload_report.json", files);
        Assert.Contains(@"_REPORT\A001_report.html", files);
        Assert.Contains(@"_REPORT\A001_report.pdf", files);
        Assert.Contains(@"_REPORT\A001.xxh64", files);
        Assert.Contains(@"_REPORT\A001.md5", files);
        Assert.Contains(@"_REPORT\A001_clips.csv", files);
        Assert.Contains(@"_REPORT\A001_clips.ale", files);
        Assert.Contains(@"_REPORT\offload-manifest.jsonl", files);
        Assert.Contains(@"_REPORT\overeni_2026-10-09.html", files);
        Assert.Contains(@"ascmhl\ascmhl_chain.xml", files);
        Assert.Contains(@"ascmhl\0001_A001_2026-10-09_080000Z.mhl", files);
        Assert.DoesNotContain(files, f => f.Contains("neshoda", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.StartsWith("_PROXY", StringComparison.OrdinalIgnoreCase) || f.StartsWith("CLIP", StringComparison.OrdinalIgnoreCase));
        AppendFixtures.AssertSameTree(before, card);   // read only
    }

    [Fact]
    public void MirrorCard_Refresh_ReplacesChangedFiles()
    {
        using var dir = new TempDir();
        var card = CardFolder(dir.Sub("d1"));
        var local = dir.Sub("local");
        var copied = LocalReports.MirrorCard([card], local)!;
        File.WriteAllText(Path.Combine(card, "_REPORT", "A001_report.html"), "<html>with clips");
        File.SetLastWriteTimeUtc(Path.Combine(card, "_REPORT", "A001_report.html"), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(copied, LocalReports.MirrorCard([card], local));   // the same card: the same folder
        Assert.Equal("<html>with clips", File.ReadAllText(Path.Combine(copied, "_REPORT", "A001_report.html")));
        Assert.False(Directory.Exists(Path.Combine(local, "2026-10-09", "A001_2")));
    }

    [Fact]
    public void MirrorCard_SeveralDestinations_FirstWithTheNewestReport()
    {
        using var dir = new TempDir();
        var t = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
        var d1 = CardFolder(dir.Sub("d1"), generation: 1, finished: t);
        var d2 = CardFolder(dir.Sub("d2"), generation: 2, finished: t);   // an append reached only d2
        var d3 = CardFolder(dir.Sub("d3"), generation: 2, finished: t);
        File.WriteAllText(Path.Combine(d2, "_REPORT", "A001_report.html"), "from d2");
        File.WriteAllText(Path.Combine(d3, "_REPORT", "A001_report.html"), "from d3");
        var local = dir.Sub("local");

        var copied = LocalReports.MirrorCard([d1, d2, d3], local)!;

        Assert.Equal("from d2", File.ReadAllText(Path.Combine(copied, "_REPORT", "A001_report.html")));
        Assert.True(File.Exists(Path.Combine(copied, "_REPORT", "offload_report_0002.json")));
    }

    [Fact]
    public void MirrorCard_VerifiedBeatsPending_LaterFinishBeatsEarlier()
    {
        using var dir = new TempDir();
        var t = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
        var d1 = CardFolder(dir.Sub("d1"), finished: t);
        File.WriteAllText(Path.Combine(d1, "_REPORT", "offload_report.json"), Report(1, t.AddMinutes(5), pending: true));
        var d2 = CardFolder(dir.Sub("d2"), finished: t);
        File.WriteAllText(Path.Combine(d2, "_REPORT", "A001_report.html"), "verified");
        Assert.Equal("verified", File.ReadAllText(Path.Combine(LocalReports.MirrorCard([d1, d2], dir.Sub("l1"))!, "_REPORT", "A001_report.html")));

        var d3 = CardFolder(dir.Sub("d3"), finished: t);
        var d4 = CardFolder(dir.Sub("d4"), finished: t.AddMinutes(1));
        File.WriteAllText(Path.Combine(d4, "_REPORT", "A001_report.html"), "later");
        Assert.Equal("later", File.ReadAllText(Path.Combine(LocalReports.MirrorCard([d3, d4], dir.Sub("l2"))!, "_REPORT", "A001_report.html")));
    }

    [Fact]
    public void MirrorCard_NameCollision_SameUnderscoreRuleAsTheDestination()
    {
        // Two different cards called A001 on the same day (two destination sets, or two projects): A001 and A001_2 — the
        // first copy is never overwritten by the other card; each card keeps its own folder on every refresh.
        using var dir = new TempDir();
        var first = CardFolder(dir.Sub("x"), started: "2026-10-09T08:00:00Z");
        var other = CardFolder(dir.Sub("y"), started: "2026-10-09T13:00:00Z");
        File.WriteAllText(Path.Combine(first, "_REPORT", "A001_report.html"), "first");
        File.WriteAllText(Path.Combine(other, "_REPORT", "A001_report.html"), "other");
        var local = dir.Sub("local");

        var a = LocalReports.MirrorCard([first], local);
        var b = LocalReports.MirrorCard([other], local);
        var again = LocalReports.MirrorCard([first], local);

        Assert.Equal(Path.Combine(local, "2026-10-09", "A001"), a);
        Assert.Equal(Path.Combine(local, "2026-10-09", "A001_2"), b);
        Assert.Equal(a, again);
        Assert.Equal("first", File.ReadAllText(Path.Combine(a!, "_REPORT", "A001_report.html")));
        Assert.Equal("other", File.ReadAllText(Path.Combine(b!, "_REPORT", "A001_report.html")));
    }

    [Fact]
    public void MirrorCard_OtherDay_OwnDateFolder()
    {
        using var dir = new TempDir();
        var d = CardFolder(dir.Sub("d1"), date: "2026-10-08");
        var local = dir.Sub("local");
        Assert.Equal(Path.Combine(local, "2026-10-08", "A001"), LocalReports.MirrorCard([d], local));
    }

    [Fact]
    public void MirrorCard_FolderAlreadyThereWithoutMarker_NotTaken()
    {
        using var dir = new TempDir();
        var d = CardFolder(dir.Sub("d1"));
        var local = dir.Sub("local");
        Write(Path.Combine(local, "2026-10-09", "A001", "moje.txt"), "the user's own");
        Assert.Equal(Path.Combine(local, "2026-10-09", "A001_2"), LocalReports.MirrorCard([d], local));
        Assert.Equal("the user's own", File.ReadAllText(Path.Combine(local, "2026-10-09", "A001", "moje.txt")));
    }

    [Fact]
    public void MirrorCard_NoReportAnywhere_Nothing()
    {
        using var dir = new TempDir();
        var local = dir.Sub("local");
        Assert.Null(LocalReports.MirrorCard([dir.Sub("d1", "2026-10-09", "A001"), Path.Combine(dir.Path, "gone", "2026-10-09", "A001")], local));
        Assert.Empty(Directory.GetFileSystemEntries(local));
    }

    [Fact]
    public void MirrorDay_DayReportFolder()
    {
        using var dir = new TempDir();
        var day1 = dir.Sub("d1", "2026-10-09");
        var day2 = dir.Sub("d2", "2026-10-09");
        Write(Path.Combine(day1, "_REPORT", "2026-10-09_day_report.html"), "day d1");
        Write(Path.Combine(day1, "_REPORT", "2026-10-09_day_report.pdf"));
        Write(Path.Combine(day1, "_REPORT", "2026-10-09_clips.csv"));
        Write(Path.Combine(day2, "_REPORT", "2026-10-09_day_report.html"), "day d2");
        var local = dir.Sub("local");

        var copied = LocalReports.MirrorDay([day1, day2], local);

        Assert.Equal(Path.Combine(local, "2026-10-09", "_REPORT"), copied);
        Assert.Equal("day d1", File.ReadAllText(Path.Combine(copied!, "2026-10-09_day_report.html")));   // both new: the first
        Assert.True(File.Exists(Path.Combine(copied!, "2026-10-09_clips.csv")));
    }

    // ── where (the Documents seam) ──────────────────────────────────────

    [Fact]
    public void Root_DefaultInDocuments_CustomFolder_Off()
    {
        Assert.Equal(Path.Combine(LocalReports.Documents, "mmMover reports"), LocalReports.RootFor(new OutputSettings()));
        Assert.Equal(@"X:\Reporty", LocalReports.RootFor(new OutputSettings { LocalReportFolder = @"X:\Reporty" }));
        Assert.Null(LocalReports.RootFor(new OutputSettings { LocalReport = false }));
        // The test assembly never writes into the real Documents (TestEnvironment).
        var real = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.False(LocalReports.Documents.StartsWith(real, StringComparison.OrdinalIgnoreCase) && real.Length > 0);
    }

    [Fact]
    public void Settings_RoundTrip_EmptyFolderIsDefault()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, """{"outputs":{"localReport":false,"localReportFolder":"  "}}""");
        var s = SettingsStore.Load(path);
        Assert.False(s.Outputs.LocalReport);
        Assert.Null(s.Outputs.LocalReportFolder);
        s.Outputs.LocalReportFolder = @"D:\Reporty";
        SettingsStore.Save(s, path);
        Assert.Equal(@"D:\Reporty", SettingsStore.Load(path).Outputs.LocalReportFolder);
        Assert.True(SettingsStore.Load(Path.Combine(dir.Path, "none.json")).Outputs.LocalReport);   // on by default
    }

    [Fact]
    public void JobConfigs_TakeTheProfilesFolder()
    {
        var s = new AppSettings();
        Assert.Equal(LocalReports.RootFor(s.Outputs), JobConfigs.Full(s, @"F:\", null, "2026-10-09").LocalReports);
        s.Outputs.LocalReport = false;
        Assert.Null(JobConfigs.Full(s, @"F:\", null, "2026-10-09").LocalReports);
    }

    // ── the job ─────────────────────────────────────────────────────────

    [Fact]
    public void Job_CopiesTheReport_AndTheDayReport_RefreshedAfterStage2()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 100_000, 1);
        var local = dir.Sub("local");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1"), dir.Sub("d2")])
        {
            Date = "2026-10-09",
            RecordHistory = false,
            AnalyzeClips = true,   // stage 2 rewrites the report with the clips (no ffprobe needed: found in the files)
            Tools = new ExternalTools { Ffmpeg = Path.Combine(dir.Path, "none.exe"), Ffprobe = Path.Combine(dir.Path, "none.exe") },
            LocalReports = local,
        }, Options());

        job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.Null(s.LocalReportNote);
        var copied = Path.Combine(local, "2026-10-09", "A001");
        Assert.Equal(copied, job.LocalReport);
        var dest = Path.Combine(dir.Path, "d1", "2026-10-09", "A001");
        Assert.Equal(File.ReadAllText(Path.Combine(dest, "_REPORT", "offload_report.json")), File.ReadAllText(Path.Combine(copied, "_REPORT", "offload_report.json")));
        Assert.Contains("A001C001", File.ReadAllText(Path.Combine(copied, "_REPORT", "offload_report.json")));   // the clips of stage 2
        Assert.True(Directory.GetFiles(Path.Combine(copied, "ascmhl")).Length > 0);
        Assert.True(File.Exists(Path.Combine(local, "2026-10-09", "_REPORT", "2026-10-09_day_report.html")));
        Assert.DoesNotContain(Files(local), f => f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Job_LocalCopyFails_JobStillDone_NoteOnly()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 10_000, 1);
        var blocked = Path.Combine(dir.Path, "blocked");
        File.WriteAllText(blocked, "a file where the folder should be");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-09", RecordHistory = false, AnalyzeClips = false, LocalReports = blocked }, Options());

        job.Run();

        var s = job.Status();
        Assert.Equal(JobPhase.Done, s.Phase);
        Assert.True(s.SafeToEject);
        Assert.True(s.SafeToFormat);
        Assert.Null(s.Warning);
        Assert.StartsWith("Could not copy the report to this computer", s.LocalReportNote);
        Assert.Null(job.LocalReport);
    }

    [Fact]
    public void Job_Off_NothingLocal()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 10_000, 1);
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-09", RecordHistory = false, AnalyzeClips = false }, Options());
        job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.Null(job.LocalReport);
    }

    [Fact]
    public void Job_LocalFolderOnTheCard_Refused()
    {
        // Nothing is ever written to the card: a local folder on the card's volume is refused (a note), not used.
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "a.mov"), 10_000, 1);
        var onCard = Path.Combine(card, "reporty");
        var job = new OffloadJob(new JobConfig(card, [dir.Sub("d1")]) { Date = "2026-10-09", RecordHistory = false, AnalyzeClips = false, LocalReports = onCard }, Options());
        job.Run();
        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.False(Directory.Exists(onCard));
        Assert.NotNull(job.Status().LocalReportNote);
    }

    [Fact]
    public void Append_RefreshesTheLocalCopy()
    {
        using var dir = new TempDir();
        var (card, cardPaths) = AppendFixtures.CopiedCard(dir);
        var local = dir.Sub("local");
        AppendFixtures.NewClip(card);
        var job = new OffloadJob(new JobConfig(card, [])
        {
            AppendInto = cardPaths, RecordHistory = false, AnalyzeClips = false, LocalReports = local, AllowSameDevice = true,
        }, Options());

        job.Run();

        Assert.Equal(JobPhase.Done, job.Status().Phase);
        Assert.True(File.Exists(Path.Combine(local, "2026-10-06", "A001", "_REPORT", "offload_report_0002.json")));
    }
}
