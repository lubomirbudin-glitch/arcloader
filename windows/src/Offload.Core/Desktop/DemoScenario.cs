using Offload.Core.History;

namespace Offload.Core.Desktop;

/// `mmMover.exe --demo <stav>` (off by default): the window filled with the states of docs/ui-navrh/screeny, for the
/// visual check against the design — nothing is scanned, inspected, copied or written. pripraveno = obr. 1, probiha =
/// obr. 2, hotovo = obr. 3, nastaveni = obr. 4; overuje (plan 10, R5) = both verdicts: a card copied and verifying with
/// its proxies ("Safe to eject"), one with a content defect and the green one ("Safe to format").
public enum DemoState { Ready, Running, Finished, Settings, Verifying }

/// A card of the demo: where it "is" (a folder path that need not exist), its look and how it is connected.
public sealed record DemoCard(string Path, string Label, CardInsight Insight, string Connection, string Format, long Total, long Free);

/// The data of the demo states, built from the texts of the mockups (docs/ui-navrh/mockup/*.html). Pure: the window
/// puts it in place of its scans (MainViewModel.Demo.cs).
public static class DemoScenario
{
    public static DemoState? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ready" or "pripraveno" => DemoState.Ready,
        "running" or "probiha" => DemoState.Running,
        "done" or "hotovo" => DemoState.Finished,
        "settings" or "nastaveni" => DemoState.Settings,
        "verifying" or "overuje" => DemoState.Verifying,
        _ => null,
    };

    static readonly DateTime Shot = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    static List<Clip> Clips(string prefix, int count, string folder = "") => Enumerable.Range(1, count).Select(i =>
    {
        var name = $"{prefix}C{i:000}_261007_R2KQ";
        var path = $"{folder}{name}.mxf";
        return new Clip { Name = name, Path = path, Format = ClipFormat.Video, Segments = [path], Bytes = 70_000_000 };
    }).ToList();

    /// The card that is ready to go (obr. 1): A035_ARRI, 24 clips, 1,70 GB, ALEXA 35 · LogC4.
    public static DemoCard Ready(string folder) => new(System.IO.Path.Combine(folder, "A035_ARRI"), "A035_ARRI",
        new CardInsight
        {
            Source = System.IO.Path.Combine(folder, "A035_ARRI"), TotalBytes = 1_700_000_000, FileCount = 24, Filesystem = "exFAT",
            Clips = Clips("A035", 24), Camera = new CameraLabel("ALEXA 35", "LogC4"),
        }, "reader USB 3 · 5 Gb/s", "exFAT", 128_000_000_000, 126_300_000_000);

    /// The card backed up yesterday with two new clips (obr. 1): "Backed up on 2026-10-06 21:40 · 2 new clips" + Append.
    public static DemoCard BackedUp(string folder, IReadOnlyList<string> destRoots)
    {
        var entry = new HistoryEntry
        {
            Id = "20261006T194000Z-b012b012", CardName = "B012_FX6", Date = "2026-10-06", Finished = new DateTime(2026, 10, 6, 19, 40, 0, DateTimeKind.Utc),
            Success = true, Verified = true, Files = 22, Bytes = 118_000_000_000, Project = null,
            Destinations = destRoots.Select(r => new HistoryDestination { Root = r, CardPath = System.IO.Path.Combine(r, "2026-10-06", "B012_FX6") }).ToList(),
        };
        var fresh = new List<SourceFile>
        {
            new("XDROOT/Clip/B012C023_261007_9QW1.MXF", 7_100_000_000, Shot),
            new("XDROOT/Clip/B012C024_261007_9QW1.MXF", 7_100_000_000, Shot),
        };
        var source = System.IO.Path.Combine(folder, "B012_FX6");
        return new DemoCard(source, "B012_FX6", new CardInsight
        {
            Source = source, TotalBytes = 132_200_000_000, FileCount = 24, Filesystem = "exFAT", Clips = Clips("B012", 24, "XDROOT/Clip/"),
            Volume = new CardVolume(null, "1A2B3C4D", 256_000_000_000, "B012_FX6", "exFAT"), AppendReachable = true,
            History = new HistoryMatch(entry, HistoryMatchKind.NewFiles, fresh, 14_200_000_000, [], 0),
            Camera = new CameraLabel("ILME-FX6V", "S-Log3"),
        }, "reader USB 3 · 5 Gb/s", "exFAT", 256_000_000_000, 123_800_000_000);
    }

    /// DNES (obr. 1): three cards of today, each on two disks and verified.
    public static IReadOnlyList<HistoryEntry> Today(IReadOnlyList<string> destRoots, DateTime todayLocal) =>
    [
        Entry("A034_ARRI", 256_000_000_000, todayLocal.Date.AddHours(18).AddMinutes(12), destRoots),
        Entry("B011_FX6", 118_000_000_000, todayLocal.Date.AddHours(16).AddMinutes(47), destRoots),
        Entry("SND_DAY03", 38_000_000_000, todayLocal.Date.AddHours(14).AddMinutes(5), destRoots),
    ];

    static HistoryEntry Entry(string card, long bytes, DateTime finishedLocal, IReadOnlyList<string> roots) => new()
    {
        Id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{finishedLocal.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}-{card.Length:x8}"), CardName = card, Bytes = bytes, Files = 20, Success = true, Verified = true,
        Finished = finishedLocal.ToUniversalTime(), Date = finishedLocal.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Destinations = roots.Select(r => new HistoryDestination { Root = r, CardPath = System.IO.Path.Combine(r, card) }).ToList(),
    };

    static OffloadPlan Plan(string source, string card, IReadOnlyList<string> roots, IReadOnlyList<SourceFile> files, PlanMode mode = PlanMode.Full) => new()
    {
        Source = source, CardName = card, Date = "2026-10-07", DestRoots = roots, Mode = mode,
        Scan = new SourceScan { Root = source, Files = files, Dirs = [], Skipped = [] },
    };

    static List<SourceFile> Files(IReadOnlyList<Clip> clips, long each) => clips.Select(c => new SourceFile(c.Path, each, Shot)).ToList();

    /// Obr. 2: A035_ARRI copying at ~661 MB/s from 38 % (the card is 170 GB here, so the picture holds for minutes), the
    /// second disk a little behind. `elapsed` = seconds since the window opened (the status moves like a real copy).
    public static OffloadJob Running(string source, IReadOnlyList<string> roots, Func<double> elapsed)
    {
        var clips = Clips("A035", 24);
        var files = Files(clips, 170_000_000_000 / 24);
        var plan = Plan(source, "A035_ARRI", roots, files);
        long total = files.Sum(f => f.Size);
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, plan, () =>
        {
            double t = elapsed();
            long read = Math.Min(total, (long)(total * 0.38 + 661e6 * t));
            long behind = Math.Min(read, (long)(6e6 * t) + 16_000_000);
            int file = (int)Math.Min(23, read / (total / 24));
            return new JobStatus
            {
                Phase = JobPhase.Copying,
                Copy = new ProgressSnapshot(CopyPhase.Copy, total, 24, Math.Max(file, 9), clips[Math.Max(file, 9)].Path, read, [read, read - behind], [0, 0], 0),
            };
        });
    }

    /// Obr. 2: the append of B012_FX6 waiting behind it.
    public static (OffloadJob Job, JobCardFacts Facts) WaitingAppend(string source, IReadOnlyList<string> roots) =>
        (OffloadJob.ForPreview(new JobConfig(source, roots) { AppendInto = roots.Select(r => System.IO.Path.Combine(r, "2026-10-06", "B012_FX6")).ToList() },
            null, () => new JobStatus()), new JobCardFacts(2, 2, 14_200_000_000, null, Append: true));

    static OffloadReport Report(string source, string card, IReadOnlyList<string> roots, IReadOnlyList<ReportFile> files, long bytes, double copyMBps, double verifyMBps) => new()
    {
        CardName = card, Date = "2026-10-07", Source = source, SourceFilesystem = "exFAT", Verified = true,
        Started = Shot, CopyFinished = Shot.AddSeconds(bytes / (copyMBps * 1e6)),
        Finished = Shot.AddSeconds(bytes / (copyMBps * 1e6) + bytes / (verifyMBps * 1e6)), TotalFiles = files.Count, TotalBytes = bytes,
        Destinations = roots.Select(r => new ReportDestination { Root = r, CardPath = System.IO.Path.Combine(r, "2026-10-07", card), Filesystem = "NTFS" }).ToList(),
        Files = files, Skipped = [],
    };

    /// Obr. 3, green: A035_ARRI done — 602 MB/s, 799 MB/s, proxy 24 / 24, 6 min 12 s (the card is 128 GB here: copy and
    /// read-back at those speeds take 6 min 12 s). `thumbnail`: a JPEG for the first five clips (the window makes grey
    /// ones), null = none.
    public static OffloadJob Done(string source, IReadOnlyList<string> roots, Func<int, byte[]>? thumbnail)
    {
        var clips = Clips("A035", 24);
        var files = Files(clips, 127_700_000_000 / 24);
        var report = Report(source, "A035_ARRI", roots,
            files.Select(f => new ReportFile { Path = f.RelPath, Size = f.Size, Xxh64 = "0123456789abcdef", Destinations = ["verified", "verified"], Errors = [null, null] }).ToList(),
            files.Sum(f => f.Size), 602, 799);
        report.Proxy = new ProxySettings { Enabled = true };
        report.Clips = clips.Select((c, i) => new ClipEntry
        {
            Clip = new Clip { Name = $"C{i + 1:000}", Path = c.Path, Segments = c.Segments, Format = ClipFormat.Video },
            ProxyStatus = ProxyStatus.Done,
            Thumbnails = thumbnail != null && i < 5 ? [new Thumbnail(thumbnail(i), "a"), new Thumbnail(thumbnail(i), "b"), new Thumbnail(thumbnail(i), "c")] : [],
        }).ToList();
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, Plan(source, "A035_ARRI", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true, CopySucceeded = true, ProxiesDone = 24,
        }, report);
    }

    /// Plan 10 (R5): A036_ARRI copied to both disks, its proxies render and verify the primary — "Safe to eject", formatting
    /// waits (clip 10 of 24 at 40 %).
    public static OffloadJob Verifying(string source, IReadOnlyList<string> roots)
    {
        var clips = Clips("A036", 24);
        var files = Files(clips, 96_000_000_000 / 24);
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, Plan(source, "A036_ARRI", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Proxies, CopySucceeded = true, SafeToEject = true, ClipCount = 24, ClipIndex = 9, ClipName = clips[9].Name, ClipFraction = 0.4,
            ProxiesDone = 9,
        });
    }

    /// Plan 10 (spec 7): A037_ARRI verified everywhere, but the decoder found damaged data in one clip — yellow until the
    /// user has looked ("Safe to format after checking defects", the label with its timecode, Náhled, the confirmation).
    public static OffloadJob ContentDefect(string source, IReadOnlyList<string> roots, Func<int, byte[]>? picture)
    {
        var clips = Clips("A037", 12);
        var files = Files(clips, 48_000_000_000 / 12);
        var report = Report(source, "A037_ARRI", roots,
            files.Select(f => new ReportFile { Path = f.RelPath, Size = f.Size, Xxh64 = "0123456789abcdef", Destinations = ["verified", "verified"], Errors = [null, null] }).ToList(),
            files.Sum(f => f.Size), 610, 790);
        report.Proxy = new ProxySettings { Enabled = true };
        report.Clips = clips.Select((c, i) => new ClipEntry
        {
            Clip = new Clip { Name = $"A037C{i + 1:000}", Path = c.Path, Segments = c.Segments, Format = ClipFormat.Video },
            ProxyStatus = ProxyStatus.Done,
            Content = i == 6
                ? new VerifyInProxy.ContentResult { State = VerifyInProxy.ContentResult.Warn, Kind = "decode", Timecode = "01:00:12:05", Message = "corrupt decoded frame", Jpeg = picture?.Invoke(i) }
                : new VerifyInProxy.ContentResult(),
        }).ToList();
        var config = new JobConfig(source, roots) { Proxy = new ProxySettings { Enabled = true } };
        return OffloadJob.ForPreview(config, Plan(source, "A037_ARRI", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Done, SafeToEject = true, SafeToFormat = true, CopySucceeded = true, ProxiesDone = 12, ContentWarnings = 1,
            Warning = Msg.ContentWarnings(1),
        }, report);
    }

    /// Obr. 3, yellow: B012_FX6 with one file that does not match on the second disk.
    public static OffloadJob Mismatch(string source, IReadOnlyList<string> roots)
    {
        var clips = Clips("B012", 24, "XDROOT/Clip/");
        var files = Files(clips, 118_000_000_000 / 24);
        const string bad = "XDROOT/Clip/B012C015_261007_9QW1.MXF";
        var report = Report(source, "B012_FX6", roots,
            files.Select(f => new ReportFile { Path = f.RelPath, Size = f.Size, Xxh64 = "0123456789abcdef", Destinations = ["verified", "verified"], Errors = [null, null] })
                .Append(new ReportFile
                {
                    Path = bad, Size = 4_900_000_000, Xxh64 = "fedcba9876543210", Destinations = ["verified", "failed"],
                    Errors = [null, "HASH MISMATCH: dest 1111111111111111 != source fedcba9876543210"],
                }).ToList(),
            files.Sum(f => f.Size), 600, 800);
        return OffloadJob.ForPreview(new JobConfig(source, roots), Plan(source, "B012_FX6", roots, files), () => new JobStatus
        {
            Phase = JobPhase.Done, CopySucceeded = false,
        }, report);
    }
}
