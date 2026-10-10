using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

/// The new job card (UI návrh, nové věci 1–4): speed table, phase indicator, summary fields, failed files, headlines.
public class JobCardTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    static readonly string[] Disks = ["RAID_A", "SHUTTLE_B"];

    static ProgressSnapshot Snap(CopyPhase phase, long read, long[] written, long[] verified, long[]? planned = null, int file = 9, bool paused = false) =>
        new(phase, 1_000_000_000, 24, file, "XDROOT/Clip/A035C010_261007_R2KQ.mxf", read, written, verified, 0, paused, paused, planned);

    [Fact]
    public void Sizes_ThreeSignificantDigits()
    {
        Assert.Equal("1.70 GB", Format.Bytes3(1_700_000_000));
        Assert.Equal("14.2 GB", Format.Bytes3(14_200_000_000));
        Assert.Equal("412 GB", Format.Bytes3(412_000_000_000));
        Assert.Equal("7.20 TB", Format.Bytes3(7_200_000_000_000));
        Assert.Equal("1.00 GB", Format.Bytes3(999_600_000));
        Assert.Equal("850 B", Format.Bytes3(850));
    }

    [Fact]
    public void Sub_ClipsSizeCamera_AppendSameDestinations()
    {
        Assert.Equal("24 clips · 1.70 GB · ALEXA 35", JobCard.Sub(new JobCardFacts(30, 24, 1_700_000_000, "ALEXA 35", false)));
        Assert.Equal("2 new clips · 14.2 GB · same destinations", JobCard.Sub(new JobCardFacts(4, 2, 14_200_000_000, "FX6", true)));
        Assert.Equal("3 files · 850 MB", JobCard.Sub(new JobCardFacts(3, 0, 850_000_000, null, false)));
    }

    [Fact]
    public void Rows_Copying_CardThenEveryDestination()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 0, [0, 0], [0, 0]) }, T0.AddSeconds(0.5));
        p.Update(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 380_000_000, [380_000_000, 370_000_000], [0, 0]) }, T0.AddSeconds(1.5));

        var rows = JobCard.Rows(p, Disks);

        Assert.Equal(["Card read", "RAID_A", "SHUTTLE_B"], rows.Select(r => r.Name));
        Assert.Equal([null, 1, 2], rows.Select(r => r.Number));
        Assert.Equal([0.38, 0.38, 0.37], rows.Select(r => Math.Round(r.Share, 2)));
        Assert.Equal(["380 MB/s", "380 MB/s", "370 MB/s"], rows.Select(r => r.RateText));
    }

    [Fact]
    public void Rows_Verifying_NoCardRow_Paused_Dash_RepairReadBackOnly()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus { Phase = JobPhase.Verifying, Copy = Snap(CopyPhase.Verify, 1_000_000_000, [1_000_000_000, 1_000_000_000], [500_000_000, 250_000_000]) }, T0);
        Assert.Equal(["RAID_A", "SHUTTLE_B"], JobCard.Rows(p, Disks).Select(r => r.Name));
        Assert.Equal([0.5, 0.25], JobCard.Rows(p, Disks).Select(r => r.Share));

        p.Update(new JobStatus { Phase = JobPhase.Copying, Paused = true, PauseHeld = true, Copy = Snap(CopyPhase.Copy, 10, [10, 10], [0, 0], paused: true) }, T0.AddSeconds(2));
        Assert.All(JobCard.Rows(p, Disks), r => Assert.Equal("—", r.RateText));

        // A repair: RAID_A only reads its good copy back — nothing to write there.
        var repair = new JobProgress(T0);
        repair.Update(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 30_000, [0, 30_000], [0, 0], planned: [0, 60_000]) }, T0);
        var rows = JobCard.Rows(repair, Disks);
        Assert.Equal((1.0, "verify only"), (rows[1].Share, rows[1].RateText));
        Assert.Equal(0.5, rows[2].Share);
        Assert.Empty(JobCard.Rows(new JobProgress(T0), Disks));
    }

    /// Plan 10: the classic order (verification right after the copy) unless `inProxy` — the steps of a verify-in-proxy job
    /// are tested in VerifyVerdictTests.
    static JobConfig Config(bool verify = true, bool analyze = true, bool proxy = true, bool inProxy = false) =>
        new(@"E:\", [@"D:\RAID_A"]) { Verify = verify, AnalyzeClips = analyze, Proxy = new ProxySettings { Enabled = proxy }, VerifyInProxy = inProxy };

    static string Steps(JobStatus s, JobConfig c, double progress = 0.38) =>
        string.Join(" | ", JobCard.Steps(s, c, progress).Select(x => $"{x.Text}:{x.State}:{x.Fill.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));

    [Fact]
    public void Steps_FromThePhase_OnlyThePhasesThisJobDoes()
    {
        var copying = new JobStatus { Phase = JobPhase.Copying };
        Assert.Equal("1 · Copying:Active:0.38 | 2 · Verification:Pending:0 | 3 · Proxies:Pending:0 | 4 · Report:Pending:0", Steps(copying, Config()));
        Assert.Equal("1 · Copying:Active:0.38 | 2 · Verification:Pending:0 | 3 · Analysis:Pending:0 | 4 · Report:Pending:0", Steps(copying, Config(proxy: false)));
        Assert.Equal("1 · Copying:Active:0.38 | 2 · Report:Pending:0", Steps(copying, Config(verify: false, analyze: false, proxy: false)));
        Assert.Equal("1 · Copying:Active:0.38 | 2 · Verification:Pending:0 | 3 · Proxies:Pending:0 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Flushing }, Config()));
        Assert.Equal("1 · Copying:Done:1 | 2 · Verification:Active:0.38 | 3 · Proxies:Pending:0 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Verifying }, Config()));
        // The first report at the end of stage 1 belongs to the copy, not to step 4.
        Assert.Equal("1 · Copying:Done:1 | 2 · Verification:Active:1 | 3 · Proxies:Pending:0 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Reporting }, Config()));
        Assert.Equal("1 · Copying:Done:1 | 2 · Verification:Done:1 | 3 · Proxies:Active:0.38 | 4 · Report:Pending:0",
            Steps(new JobStatus { Phase = JobPhase.Analyzing, CopySucceeded = true }, Config()));
        Assert.Equal("1 · Copying:Done:1 | 2 · Verification:Done:1 | 3 · Proxies:Done:1 | 4 · Report:Active:0.38",
            Steps(new JobStatus { Phase = JobPhase.Reporting, CopySucceeded = true }, Config()));
        Assert.Equal("1 · Copying:Done:1 | 2 · Verification:Done:1 | 3 · Proxies:Done:1 | 4 · Report:Done:1", Steps(new JobStatus { Phase = JobPhase.Done }, Config()));
        Assert.Equal("1 · Copying:Pending:0 | 2 · Verification:Pending:0 | 3 · Proxies:Pending:0 | 4 · Report:Pending:0", Steps(new JobStatus(), Config()));
        Assert.Empty(JobCard.Steps(new JobStatus { Phase = JobPhase.Failed }, Config(), 0));
        // A repair: no stage 2.
        var repair = Config();
        repair.RepairInto = [@"D:\RAID_A\2026-10-07\A001"];
        Assert.Equal("1 · Copying:Active:0.38 | 2 · Verification:Pending:0 | 3 · Report:Pending:0", Steps(copying, repair));
    }

    [Fact]
    public void FileLine_And_Remaining()
    {
        Assert.Equal(("File 10 / 24", "A035C010_261007_R2KQ.mxf"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Copying, Copy = Snap(CopyPhase.Copy, 0, [0], [0]) }));
        Assert.Equal(("Verifying", "500 MB / 2.00 GB"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Verifying, Copy = Snap(CopyPhase.Verify, 0, [0, 0], [250_000_000, 250_000_000]) }));
        Assert.Equal(("Proxy 3 / 24", "A035C003 · 43 %"),
            JobCard.FileLine(new JobStatus { Phase = JobPhase.Proxies, ClipIndex = 2, ClipCount = 24, ClipName = "A035C003", ClipFraction = 0.426 }));

        Assert.Equal("~0:02 left for the copy · ~6:00 in total", JobCard.Remaining(JobPhase.Copying, 2, 360));
        Assert.Equal("~0:09 left for verification", JobCard.Remaining(JobPhase.Verifying, 9, null));
        Assert.Equal("~1:00 left in total", JobCard.Remaining(JobPhase.Proxies, null, 60));
        Assert.Null(JobCard.Remaining(JobPhase.Analyzing, null, null));
        Assert.Equal(["45 s", "6 min 12 s", "1 h 05 min"],
            new[] { 45, 372, 3900 }.Select(s => JobCard.Duration(TimeSpan.FromSeconds(s))));
    }

    static OffloadReport Report(bool verified = true) => new()
    {
        CardName = "A035_ARRI", Date = "2026-10-07", Source = @"E:\", SourceFilesystem = "exFAT",
        Started = T0, CopyFinished = T0.AddSeconds(10), Finished = T0.AddSeconds(15), Verified = verified,
        TotalFiles = 2, TotalBytes = 6_020_000_000,
        Destinations =
        [
            new ReportDestination { Root = @"D:\RAID_A", CardPath = @"D:\RAID_A\2026-10-07\A035_ARRI", Filesystem = "NTFS" },
            new ReportDestination { Root = @"F:\SHUTTLE_B", CardPath = @"F:\SHUTTLE_B\2026-10-07\A035_ARRI", Filesystem = "exFAT" },
        ],
        Files = [], Skipped = [],
    };

    static ReportFile File(string path, string d1, string d2, string? e2 = null, string? source = null) =>
        new() { Path = path, Size = 1, Destinations = [d1, d2], Errors = [null, e2], SourceError = source };

    [Fact]
    public void Summary_FourFields()
    {
        var r = Report();
        r.Proxy = new ProxySettings { Enabled = true };
        r.Clips =
        [
            new ClipEntry { Clip = new Clip { Name = "C001", Path = "C001.mxf", Segments = ["C001.mxf"] }, ProxyStatus = ProxyStatus.Done },
            new ClipEntry { Clip = new Clip { Name = "C002", Path = "C002.mxf", Segments = ["C002.mxf"] }, ProxyStatus = ProxyStatus.Failed },
            new ClipEntry { Clip = new Clip { Name = "S001", Path = "SUB/S001.mp4", Segments = ["SUB/S001.mp4"] }, ProxyStatus = ProxyStatus.Skipped },
        ];
        Assert.Equal([("Copy", "602 MB/s"), ("Verification", "1204 MB/s"), ("Proxies", "1 / 2"), ("Total", "6 min 12 s")],
            JobCard.Summary(r, TimeSpan.FromSeconds(372)).Select(f => (f.Label, f.Value)));
        var plain = Report(verified: false);
        Assert.Equal([("Copy", "602 MB/s"), ("Verification", "off"), ("Proxies", "off"), ("Total", "15 s")],
            JobCard.Summary(plain, null).Select(f => (f.Label, f.Value)));
    }

    [Fact]
    public void FailedRows_PerDestination_WhyInTwoWords()
    {
        var r = Report();
        var report = new OffloadReport
        {
            CardName = r.CardName, Date = r.Date, Source = r.Source, SourceFilesystem = r.SourceFilesystem, Verified = true,
            Destinations = r.Destinations, Skipped = [],
            Files =
            [
                File("XDROOT/Clip/B012C001.MXF", "verified", "verified"),
                File("XDROOT/Clip/B012C015_261007_9QW1.MXF", "verified", "failed", "HASH MISMATCH: dest 1 != source 2"),
                File("XDROOT/Clip/B012C016.MXF", "verified", "failed", "Zařízení není připojeno."),
                File("XDROOT/Clip/B012C017.MXF", "failed", "failed", "source: read failed", source: "read failed"),
            ],
        };

        var rows = JobCard.FailedRows(report, Disks);

        Assert.Equal(3, rows.Count);
        Assert.Equal(@"XDROOT\Clip\B012C015_261007_9QW1.MXF", rows[0].Path);
        Assert.Equal(["RAID_A ✓", "SHUTTLE_B ✕ hash"], rows[0].Disks.Select(d => d.Text));
        Assert.Equal(["RAID_A ✓", "SHUTTLE_B ✕ write"], rows[1].Disks.Select(d => d.Text));
        Assert.Equal(["RAID_A ✕ card", "SHUTTLE_B ✕ card"], rows[2].Disks.Select(d => d.Text));
        Assert.Equal(["hash", "size", "read", "cancelled", "card", "write", "not copied"],
            new[] { ("failed", "MD5 MISMATCH: x"), ("failed", "size mismatch: 1 != 2"), ("failed", "verify: gone"), ("failed", "source: cancelled"),
                    ("failed", "source: read failed"), ("failed", "boom"), ("pending", (string?)null) }.Select(x => JobCard.Problem(x.Item1, x.Item2)));
        Assert.Equal("… and 3 more files in the report", JobCard.MoreFiles(8, 5));
        Assert.Null(JobCard.MoreFiles(5, 5));
    }

    [Fact]
    public void Headlines_EveryState()
    {
        var ok = Report();
        ok = WithFiles(ok, File("a", "verified", "verified"), File("b", "verified", "verified"));
        var h = JobCard.Headline("A035_ARRI", new JobStatus { Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true, CopySucceeded = true }, ok, Disks);
        Assert.Equal((JobCardKind.Done, "A035_ARRI — safe to format", "All 2 files are on 2 disks and verified (xxHash64)."), (h.Kind, h.Title, h.Sub));

        var bad = WithFiles(Report(), File("a", "verified", "verified"), File("XDROOT/Clip/B012C015.MXF", "verified", "failed", "HASH MISMATCH: 1 != 2"));
        h = JobCard.Headline("B012_FX6", new JobStatus { Phase = JobPhase.Done, CopySucceeded = false }, bad, Disks);
        Assert.Equal((JobCardKind.Problem, "B012_FX6 — do not eject the card yet", "1 file on disk SHUTTLE_B does not match the card. Everything is fine on RAID_A."),
            (h.Kind, h.Title, h.Sub));

        var unverified = WithFiles(Report(verified: false), File("a", "written", "written"));
        Assert.Equal(JobCardKind.Problem, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Done, CopySucceeded = true }, unverified, Disks).Kind);
        Assert.Equal(("A — offload failed", "Destination not found: D:\\"),
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "Destination not found: D:\\" }, null, Disks) is var f ? (f.Title, f.Sub) : default);
        Assert.Equal(JobCardKind.Failed, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, Error = "x" }, null, Disks).Kind);
        Assert.Equal((JobCardKind.Done, "A — the backup is complete", Msg.NothingNew),
            JobCard.Headline("A", new JobStatus { Phase = JobPhase.Done, Note = Msg.NothingNew, SafeToEject = true, SafeToFormat = true }, null, Disks) is var n ? (n.Kind, n.Title, n.Sub) : default);
        Assert.Equal(JobCardKind.Cancelled, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Cancelled }, null, Disks).Kind);
        Assert.Equal(JobCardKind.Waiting, JobCard.Headline("A", new JobStatus(), null, Disks).Kind);
        Assert.Equal(JobCardKind.Running, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Proxies }, null, Disks).Kind);
        // Stage 2 failed after a good copy: still safe to format (the error shows as an orange line).
        Assert.Equal(JobCardKind.Done, JobCard.Headline("A", new JobStatus { Phase = JobPhase.Failed, SafeToEject = true, SafeToFormat = true, Error = "x" }, ok, Disks).Kind);
    }

    static OffloadReport WithFiles(OffloadReport r, params ReportFile[] files) => new()
    {
        CardName = r.CardName, Date = r.Date, Source = r.Source, SourceFilesystem = r.SourceFilesystem, Verified = r.Verified,
        Destinations = r.Destinations, Skipped = [], Files = files, TotalFiles = files.Length,
    };

    [Fact]
    public void Texts_Plurals()
    {
        Assert.Equal("The file is on 1 disk and verified (xxHash64).", JobCard.SafeText(1, 1, "xxHash64"));
        Assert.Equal("All 24 files are on 2 disks and verified (xxHash64 + MD5).", JobCard.SafeText(24, 2, "xxHash64 + MD5"));
        var two = WithFiles(Report(), File("a", "failed", "verified", "x"), File("b", "verified", "failed", "y"), File("c", "verified", "failed", "z"));
        Assert.Equal("Not matching the card: RAID_A 1 file, SHUTTLE_B 2 files.", JobCard.ProblemText(two, Disks));
        var card = WithFiles(Report(), File("a", "failed", "failed", "source: x", source: "x"));
        Assert.Equal("Could not read 1 file from the card.", JobCard.ProblemText(card, Disks));
    }

    [Fact]
    public void Thumbs_FirstFive_MiddleFrame_AndTheRest()
    {
        var r = Report();
        r.Clips = Enumerable.Range(1, 24).Select(i => new ClipEntry
        {
            Clip = new Clip { Name = $"C{i:000}", Path = $"C{i:000}.mxf", Segments = [$"C{i:000}.mxf"] },
            Thumbnails = [new Thumbnail([1], "a"), new Thumbnail([2], "b"), new Thumbnail([3], "c")],
        }).ToList();

        var (thumbs, more) = JobCard.Thumbs(r);

        Assert.Equal(["C001", "C002", "C003", "C004", "C005"], thumbs.Select(t => t.Clip));
        Assert.All(thumbs, t => Assert.Equal([2], t.Jpeg));
        Assert.Equal(19, more);
        Assert.Equal(0, JobCard.Thumbs(Report()).More);
    }

    [Fact]
    public void BrakeSentence_CardOrDisk()
    {
        var chain = ChainView.Build(
            [new RoleLink("Card", new DriveLink("E:\\", StorageBus.Usb, "reader", new UsbLink("PCI\\HC", "x", "R#5", 5, 5, UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, false))),
             new RoleLink("Destination 1", new DriveLink("D:\\", StorageBus.Nvme, "SSD", null)),
             new RoleLink("Destination 2", new DriveLink("F:\\", StorageBus.Sata, "HDD", null))], [400, 400, 400]);
        Assert.Equal("Bottleneck: the card reader (USB 3 (5 Gb/s)). The disks could do more.", JobCard.BrakeSentence(new Brake(0, 400, 0.5), chain, Disks));
        Assert.Equal("Bottleneck: the card reader (USB 3 (5 Gb/s) — the link is saturated). The disks could do more.", JobCard.BrakeSentence(new Brake(0, 440, 0.97), chain, Disks));
        Assert.Equal("Bottleneck: disk SHUTTLE_B. The card could be read faster.", JobCard.BrakeSentence(new Brake(2, 150, null), chain, Disks));
        Assert.Null(JobCard.BrakeSentence(null, chain, Disks));
    }

    [Fact]
    public void QueuePill_StartLabel()
    {
        var counts = QueueSummary.Count([(JobPhase.Copying, false), (JobPhase.Waiting, false), (JobPhase.Done, false), (JobPhase.Proxies, true)]);
        Assert.Equal((1, 1, 1), counts);
        // Pozastavit pressed but not yet holding: still running
        Assert.Equal((2, 0, 0), QueueSummary.Count([(JobPhase.Copying, false), (JobPhase.Copying, false)]));
        Assert.Equal((0, 0, 0), QueueSummary.Count([(JobPhase.Done, true)]));
        Assert.Equal("Running 1 offload · 1 in queue", QueueSummary.Pill(1, 0, 1));
        Assert.Equal("Running 2 offloads · 1 paused · 3 in queue", QueueSummary.Pill(2, 1, 3));
        Assert.Equal("Paused: 1 offload", QueueSummary.Pill(0, 1, 0));
        Assert.Null(QueueSummary.Pill(0, 0, 0));
        Assert.Equal("Add to queue", QueueSummary.StartLabel([JobPhase.Verifying, JobPhase.Done]));
        Assert.Equal("Start offload", QueueSummary.StartLabel([JobPhase.Proxies, JobPhase.Done]));
    }

    [Fact]
    public void Elapsed_WithoutThePauses()
    {
        var p = new JobProgress(T0);
        p.Update(new JobStatus(), T0);
        Assert.Null(p.Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Copying }, T0.AddSeconds(10));
        p.Update(new JobStatus { Phase = JobPhase.Proxies, PausedTotal = TimeSpan.FromSeconds(30) }, T0.AddSeconds(300));
        Assert.Null(p.Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Done, PausedTotal = TimeSpan.FromSeconds(30) }, T0.AddSeconds(412));
        Assert.Equal(TimeSpan.FromSeconds(372), p.Elapsed);
        p.Update(new JobStatus { Phase = JobPhase.Done, PausedTotal = TimeSpan.FromSeconds(30) }, T0.AddSeconds(999));
        Assert.Equal(TimeSpan.FromSeconds(372), p.Elapsed);   // the first finished tick counts
        var seenFinished = new JobProgress(T0);
        seenFinished.Update(new JobStatus { Phase = JobPhase.Done }, T0.AddSeconds(5));
        Assert.Null(seenFinished.Elapsed);                     // never seen running: the report's times count
    }

    // Minor M12: two destinations with the same folder name are told apart by their drive; a drive root stays as it is.
    [Fact]
    public void DiskNames_DriveOnlyWhenNamesCollide()
    {
        Assert.Equal(["RAID_A", "SHUTTLE_B"], JobCard.DiskNames([@"D:\RAID_A", @"E:\SHUTTLE_B\"]));
        Assert.Equal(["Backup (D:)", "backup (e:)"], JobCard.DiskNames([@"D:\Backup", @"e:\backup\"]));
        Assert.Equal(["E:\\", "F:\\"], JobCard.DiskNames([@"E:\", @"F:\"]));
    }

    // Tasks 1–3 review, minor 3: the waiting card behind a paused job says so as a sentence of its own.
    [Fact]
    public void WaitingHint_OnlyBehindAPausedJob()
    {
        var waiting = new JobStatus { Phase = JobPhase.Waiting };
        Assert.Equal("Waiting for a paused job.", JobCard.WaitingHint(JobCard.Headline("B012", waiting, null, Disks, blockedByPaused: true)));
        Assert.Null(JobCard.WaitingHint(JobCard.Headline("B012", waiting, null, Disks)));
        Assert.Null(JobCard.WaitingHint(JobCard.Headline("B012", new JobStatus { Phase = JobPhase.Copying }, null, Disks, blockedByPaused: true)));
    }
}
