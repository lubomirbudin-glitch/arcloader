using Offload.Cli;
using Offload.Core.Outputs;

namespace Offload.Core.Tests;

/// Spec 3.2 "Kdy se tvoří": after every job on every destination, on request, one writer at a time per day.
public class DayReportServiceTests
{
    static OffloadJob Job(string card, IReadOnlyList<string> dests, string history, OutputSettings? outputs = null, OffloadOptions? options = null,
        IReadOnlyList<string>? appendInto = null) =>
        new(new JobConfig(card, dests) { Date = "2026-10-07", AnalyzeClips = false, HistoryFolder = history, Outputs = outputs ?? new OutputSettings(), AppendInto = appendInto },
            options ?? AppendFixtures.Options());

    static string Card(TempDir dir, string name, int seed)
    {
        var card = dir.Sub(name);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", $"{name}C001.mov"), 10_000, seed);
        return card;
    }

    const string VerifyName = "X001_verify_2026-10-07_120000Z.json";

    [Fact]
    public void AfterEveryJob_OnEveryDestination_OffWhenSwitchedOff()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var d1 = dir.Sub("d1");
        var d2 = dir.Sub("d2");
        Job(Card(dir, "A001", 1), [d1, d2], history).Run();
        // Ruling M4: plan 6's verify report beside the day report is never deleted nor rewritten.
        var verify = Path.Combine(d1, "2026-10-07", "_REPORT", VerifyName);
        File.WriteAllText(verify, "{\"verify\":1}");
        var second = Job(Card(dir, "B001", 2), [d1, d2], history);

        second.Run();

        Assert.Equal(JobPhase.Done, second.Status().Phase);
        Assert.Equal(new[] { d1, d2 }.Select(d => Path.Combine(d, "2026-10-07", "_REPORT", "2026-10-07_day_report.html")), second.DayReports);
        foreach (var html in second.DayReports)
        {
            var text = File.ReadAllText(html);
            Assert.Contains("<td>A001</td>", text);
            Assert.Contains("<td>B001</td>", text);
        }
        Assert.Equal("{\"verify\":1}", File.ReadAllText(verify));

        var d3 = dir.Sub("d3");
        var off = Job(Card(dir, "C001", 3), [d3], history, new OutputSettings { DayReport = false });
        off.Run();
        Assert.Empty(off.DayReports);
        Assert.False(Directory.Exists(Path.Combine(d3, "2026-10-07", "_REPORT")));
    }

    // Spec 3.2 "i doplnění" (ruling M3): an append makes the day report of its day again.
    [Fact]
    public void Append_Regenerates()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var card = Card(dir, "A001", 1);
        var full = Job(card, [dir.Sub("d1")], history);
        full.Run();
        var html = Assert.Single(full.DayReports);
        Assert.DoesNotContain("<td class=\"num\">2</td></tr>", File.ReadAllText(html));
        AppendFixtures.NewClip(card);

        var append = Job(card, [], history, appendInto: full.Plan!.CardPaths);
        append.Run();

        Assert.Equal(JobPhase.Done, append.Status().Phase);
        Assert.Equal([html], append.DayReports);
        Assert.Contains("<td class=\"num\">2</td></tr>", File.ReadAllText(html));   // two generations
    }

    // Spec 3.2: a cancelled job still makes the day report — its card shows as not complete.
    [Fact]
    public void CancelledJob_StillRegenerates()
    {
        using var dir = new TempDir();
        var card = Card(dir, "A001", 1);
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C002.mov"), 10_000, 2);
        var options = AppendFixtures.Options();
        OffloadJob? job = null;
        options.BeforeFileCopy = i => { if (i == 1) job!.Cancel(); };
        job = Job(card, [dir.Sub("d1")], dir.Sub("appdata"), options: options);

        job.Run();

        Assert.Equal(JobPhase.Cancelled, job.Status().Phase);
        var html = File.ReadAllText(Assert.Single(job.DayReports));
        Assert.Contains("<td>A001</td>", html);
        Assert.Contains("1 soubor selhal: CLIP/A001C002.mov", html);
    }

    // Ruling M9: stage 1 failed (a destination went away while copying) — the day report is still made, best effort.
    [Fact]
    public void FailedStageOne_StillRegenerates()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var d1 = dir.Sub("d1");
        Job(Card(dir, "A001", 1), [d1], history).Run();
        var options = AppendFixtures.Options();
        options.BeforeVerify = () => throw new IOException("disk gone");
        var failed = Job(Card(dir, "B001", 2), [d1], history, options: options);

        failed.Run();

        Assert.Equal(JobPhase.Failed, failed.Status().Phase);
        var html = File.ReadAllText(Assert.Single(failed.DayReports));
        Assert.Contains("<td>A001</td>", html);
        Assert.Contains("<td>B001</td><td colspan=\"11\" class=\"muted\">jiná složka (bez reportu)</td>", html);
    }

    // Ruling M3: a destination that is gone gets no day report, only a log line; the other destination gets its own.
    [Fact]
    public void DestinationGone_Logged_TheOtherOneStillGetsIt()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var d1 = dir.Sub("d1");
        var d2 = dir.Sub("d2");
        var options = AppendFixtures.Options();
        options.BeforeVerify = () =>
        {
            // d2's day is gone, and nothing can make it again (a file of that name is in the way).
            var gone = Path.Combine(d2, "2026-10-07");
            Directory.Delete(gone, recursive: true);
            File.WriteAllText(gone, "");
        };
        var job = Job(Card(dir, "A001", 1), [d1, d2], history, options: options);
        var log = new List<string>();

        job.Run(m => { lock (log) log.Add(m); });

        Assert.Equal([Path.Combine(d1, "2026-10-07", "_REPORT", "2026-10-07_day_report.html")], job.DayReports);
        Assert.Contains(log, m => m.StartsWith($"day report {Path.Combine(d2, "2026-10-07")}: ", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(d2, "_REPORT")));
    }

    // Review focus 3: two regenerations of one day — the second waits for the first; one that waits too long gives up
    // with a Czech message (a job only logs it) and nothing is half-written.
    [Fact]
    public async Task TwoWriters_OneAtATime()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = new Thread(() =>
        {
            DayReportService.WhileHeld = _ => { inside.Set(); release.Wait(); };
            try { DayReportService.RegenerateDay(day, new OutputSettings()); }
            finally { DayReportService.WhileHeld = null; }
        });
        first.Start();
        Assert.True(inside.Wait(TimeSpan.FromSeconds(30)));

        var busy = Assert.Throws<TimeoutException>(() => DayReportService.RegenerateDay(day, new OutputSettings(), wait: TimeSpan.FromMilliseconds(200)));
        Assert.Equal($"Denní report ve složce {day} právě vytváří jiná úloha — zkuste to za chvíli.", busy.Message);

        var waiting = Task.Run(() => DayReportService.RegenerateDay(day, new OutputSettings(), wait: TimeSpan.FromSeconds(30)));
        Thread.Sleep(200);
        Assert.False(waiting.IsCompleted);   // it waits, it does not write beside the first one
        release.Set();
        first.Join();
        Assert.EndsWith("2026-10-06_day_report.html", await waiting);
        Assert.Empty(Directory.GetFiles(Path.Combine(day, "_REPORT"), "*.tmp"));
        Assert.Empty(Directory.GetFiles(Path.Combine(day, "_REPORT"), "*.new"));
    }

    // Review focus 3: another process (the CLI beside the GUI) holds the day's named mutex.
    [Fact]
    public void OtherProcessHoldsTheDay_Busy()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        using var taken = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var other = new Thread(() =>
        {
            using var m = new Mutex(true, DayReportService.MutexName(day.ToLowerInvariant() + "\\"));   // another spelling, the same name
            taken.Set();
            release.Wait();
            m.ReleaseMutex();
        });
        other.Start();
        Assert.True(taken.Wait(TimeSpan.FromSeconds(30)));

        Assert.Throws<TimeoutException>(() => DayReportService.RegenerateDay(day, new OutputSettings(), wait: TimeSpan.FromMilliseconds(200)));

        release.Set();
        other.Join();
        Assert.True(File.Exists(DayReportService.RegenerateDay(day, new OutputSettings())));
    }

    // Ruling M7: the lock is the day's volume and its path on it (like AppendLock), not the spelling — the volume GUID
    // path of the same folder (what another drive letter or a mount point resolves to) gets the same lock.
    [Fact]
    public void VolumeGuidSpelling_SameDayLock()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        var root = NativeIO.VolumeRoot(day)!;
        var byGuid = NativeIO.VolumeId(root)! + Path.GetRelativePath(root, day);
        Assert.StartsWith(@"\\?\Volume{", byGuid);
        Assert.True(Directory.Exists(byGuid));

        Assert.Equal(DayReportService.MutexName(day), DayReportService.MutexName(byGuid));
        Assert.NotEqual(DayReportService.MutexName(day), DayReportService.MutexName(Path.Combine(dir.Sub("D", "2026-10-07"))));
        Assert.Matches(@"^Local\\Offload-day-[0-9a-f]{40}$", DayReportService.MutexName(day));

        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = new Thread(() =>
        {
            DayReportService.WhileHeld = _ => { inside.Set(); release.Wait(); };
            try { DayReportService.RegenerateDay(byGuid, new OutputSettings()); }
            finally { DayReportService.WhileHeld = null; }
        });
        first.Start();
        try
        {
            Assert.True(inside.Wait(TimeSpan.FromSeconds(30)));
            Assert.Throws<TimeoutException>(() => DayReportService.RegenerateDay(day, new OutputSettings(), wait: TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            release.Set();
            first.Join();
        }
    }

    // Review focus 3: the Mac (over SMB) or a browser reading the day report with delete sharing does not stop it; a
    // reader without it (Excel, some viewers) makes it fail — the job is still Done and the log says why.
    [Fact]
    public void ReaderOfTheDayReport_SharingDecides_JobNeverFails()
    {
        using var dir = new TempDir();
        var history = dir.Sub("appdata");
        var d1 = dir.Sub("d1");
        var first = Job(Card(dir, "A001", 1), [d1], history);
        first.Run();
        var html = first.DayReports.Single();

        using (new FileStream(html, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var second = Job(Card(dir, "B001", 2), [d1], history);
            second.Run();
            Assert.Equal([html], second.DayReports);
        }
        Assert.Contains("<td>B001</td>", File.ReadAllText(html));

        var log = new List<string>();
        using (new FileStream(html, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var third = Job(Card(dir, "C001", 3), [d1], history);
            third.Run(m => { lock (log) log.Add(m); });
            Assert.Equal(JobPhase.Done, third.Status().Phase);
            Assert.Empty(third.DayReports);
        }
        Assert.Contains(log, m => m.StartsWith($"day report {Path.Combine(d1, "2026-10-07")}: ", StringComparison.Ordinal));
    }

    [Fact]
    public void DayFolderOf_ACardMeansItsDay_NeverOutsideADay()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);
        Assert.Equal(day, DayReportService.DayFolderOf(Path.Combine(day, "A001")));
        Assert.Equal(day, DayReportService.DayFolderOf(day + "\\"));
        Assert.Equal(Path.Combine(day, "E001"), DayReportService.DayFolderOf(Path.Combine(day, "E001")));   // no report: not a card

        var root = Path.GetDirectoryName(day)!;
        var notADay = Assert.Throws<InvalidOperationException>(() => DayReportService.RegenerateDay(root, new OutputSettings()));
        Assert.Equal($"Ve složce {root} není žádná karta s reportem — vyberte složku dne na cíli (<cíl>\\<datum>).", notADay.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "_REPORT")));
        Assert.Throws<DirectoryNotFoundException>(() => DayReportService.RegenerateDay(Path.Combine(root, "nope"), new OutputSettings()));

        // Ruling M8: a card folder called _REPORT — refused, nothing written into it.
        var card = Path.Combine(day, "_REPORT");
        Reports.Sample(card).Write(card, includeHashRecords: false);
        var before = DayFixtures.CardFiles(card);
        Assert.Throws<InvalidOperationException>(() => DayReportService.RegenerateDay(day, new OutputSettings()));
        DayFixtures.AssertSame(before, DayFixtures.CardFiles(card));
    }
}

/// `offload day-report`, `copy --no-day-report` and the "Denní report: …" line.
[Collection("Console")]
public class CliDayReportTests
{
    static (int Exit, string Out, string Err) Run(System.CommandLine.Command command, params string[] args)
    {
        var o = Console.Out;
        var e = Console.Error;
        var so = new StringWriter();
        var se = new StringWriter();
        var old = CopyCommand.JobFactory;
        Console.SetOut(so);
        Console.SetError(se);
        CopyCommand.JobFactory = c => new OffloadJob(c, AppendFixtures.Options());
        try { return (command.Parse(args).Invoke(), so.ToString(), se.ToString()); }
        finally
        {
            Console.SetOut(o);
            Console.SetError(e);
            CopyCommand.JobFactory = old;
        }
    }

    [Fact]
    public void DayReport_PrintsThePath_Exit0_MissingFolderExit1()
    {
        using var dir = new TempDir();
        var day = DayFixtures.Day(dir);

        var ok = Run(DayReportCommand.Create(), Path.Combine(day, "A001"), "--ale");

        Assert.Equal(0, ok.Exit);
        Assert.Equal($"Denní report: {Path.Combine(day, "_REPORT", "2026-10-06_day_report.html")}", ok.Out.Trim());
        Assert.True(File.Exists(Path.Combine(day, "_REPORT", "2026-10-06_clips.ale")));

        var missing = Path.Combine(dir.Path, "nope");
        var bad = Run(DayReportCommand.Create(), missing);
        Assert.Equal(1, bad.Exit);
        Assert.Contains($"Chyba: Složka dne nenalezena: {missing}", bad.Err);
    }

    [Fact]
    public void Copy_PrintsTheDayReport_NoDayReportLeavesItOut()
    {
        using var dir = new TempDir();
        var card = dir.Sub("A001");
        TestFs.WriteRandom(Path.Combine(card, "CLIP", "A001C001.mov"), 10_000, 1);
        var d1 = dir.Sub("d1");
        var d2 = dir.Sub("d2");

        var on = Run(CopyCommand.Create(), card, "-d", d1, "--date", "2026-10-07", "--no-history", "--no-analyze");
        var off = Run(CopyCommand.Create(), card, "-d", d2, "--date", "2026-10-07", "--no-history", "--no-analyze", "--no-day-report");

        Assert.Equal((0, 0), (on.Exit, off.Exit));
        var reportLine = on.Out.IndexOf("Report: ", StringComparison.Ordinal);
        var dayLine = on.Out.IndexOf($"Denní report: {Path.Combine(d1, "2026-10-07", "_REPORT", "2026-10-07_day_report.html")}", StringComparison.Ordinal);
        Assert.True(reportLine >= 0 && dayLine > reportLine, on.Out);   // ruling I2: the CLI output order is kept
        Assert.DoesNotContain("Denní report", off.Out);
        Assert.False(Directory.Exists(Path.Combine(d2, "2026-10-07", "_REPORT")));
    }
}
