using System.Globalization;
using Offload.Core.Append;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// How a job card looks (UI návrh, obr. 2 a 3): running, waiting, done (green), with failed files (yellow), failed before
/// or without a copy (red), cancelled (grey).
public enum JobCardKind { Waiting, Running, Done, Problem, Failed, Cancelled }

/// One row of the speed table: "Card read" or a destination (with its number), the share done and the speed.
public sealed record TransferRow(string Name, int? Number, double Share, string RateText);

public enum StepState { Done, Active, Pending }

/// One segment of the phase indicator: "1 · Copying".
public sealed record PhaseStep(int Number, string Label, StepState State, double Fill)
{
    public string Text => $"{Number.ToString(CultureInfo.InvariantCulture)} · {Label}";
}

/// One number of a finished card: "Copy" / "602 MB/s".
public sealed record SummaryField(string Label, string Value);

/// One destination of a file that did not make it: "RAID_A ✓", "SHUTTLE_B ✕ hash".
public sealed record FileOnDisk(string Name, bool Ok, string Text);

public sealed record FailedFileRow(string Path, IReadOnlyList<FileOnDisk> Disks);

/// The big line of a finished card and the sentence under it.
public sealed record CardHeadline(JobCardKind Kind, string Title, string Sub);

/// What the card header says under the name: the files or clips, the size and the camera.
public sealed record JobCardFacts(int Files, int Clips, long Bytes, string? Camera, bool Append);

public enum ChipTone { Neutral, Ok, Warning, Error }

/// One of the card row's two verdicts (R5, spec 9): "Safe to eject" or "Safe to format" and how it looks.
public sealed record CardChip(string Text, ChipTone Tone);

/// One content warning of a finished card (spec 7, 9): the clip and the label with the place and the kind of the defect.
public sealed record ContentLine(string Clip, string Label, bool HasPicture);

/// The texts and numbers of the new job card (UI návrh, nové věci 1–4) — pure functions, the window only binds them.
public static class JobCard
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// The name a destination is shown with: its folder's name ("RAID_A"), a drive root as it is ("E:\").
    public static string DiskName(string root) => Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;

    /// The names of a job's destinations (minor M12): DiskName, with the drive when two would read the same
    /// ("Backup (D:)", "Backup (E:)") — a "✕ hash" row must say which disk it is.
    public static IReadOnlyList<string> DiskNames(IReadOnlyList<string> roots)
    {
        var names = roots.Select(DiskName).ToList();
        return names.Select((n, i) => names.Count(m => string.Equals(m, n, StringComparison.OrdinalIgnoreCase)) > 1
            && Path.GetPathRoot(roots[i])?.TrimEnd('\\') is { Length: > 0 } drive && !string.Equals(drive, n.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                ? $"{n} ({drive})" : n).ToList();
    }

    /// "661 MB/s"; a job standing paused shows a dash (while it still finishes its file it shows its speed).
    public static string Rate(double bytesPerSecond, bool paused = false) =>
        paused ? "—" : (bytesPerSecond / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s";

    /// "24 clips · 1.70 GB · ALEXA 35", an append "2 new clips · 14.2 GB · same destinations".
    public static string Sub(JobCardFacts f)
    {
        var what = f.Clips > 0
            ? f.Append ? Texts.N(f.Clips, "new clip") : Texts.Clips(f.Clips)
            : f.Append ? Texts.N(f.Files, "new file") : Texts.N(f.Files, "file");
        var parts = new List<string> { what, Format.Bytes3(f.Bytes) };
        if (f.Append) parts.Add("same destinations");
        else if (f.Camera is { Length: > 0 } camera) parts.Add(camera);
        return string.Join(" · ", parts);
    }

    /// The speed table while the card is read and written or read back: "Card read" (copy only), then every destination.
    public static IReadOnlyList<TransferRow> Rows(JobProgress p, IReadOnlyList<string> destNames)
    {
        var s = p.Status;
        if (s.Copy is not { } c || s.Phase is not (JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying)) return [];
        bool verifying = s.Phase == JobPhase.Verifying;
        var rows = new List<TransferRow>();
        if (!verifying)
            rows.Add(new TransferRow("Card read", null, Share(c.ReadBytes, c.TotalBytes), Rate(p.ReadRate, s.PauseHeld)));
        for (int d = 0; d < destNames.Count; d++)
        {
            long done = verifying ? At(c.VerifiedBytes, d) : At(c.WrittenBytes, d);
            long of = verifying ? c.ToVerify(d) : c.Planned(d);
            double rate = d < p.DestRates.Length ? p.DestRates[d] : 0;
            // A destination a repair only reads back has nothing to write: full, and no speed while the others copy.
            rows.Add(new TransferRow(destNames[d], d + 1, of == 0 ? 1 : Share(done, of), of == 0 && !verifying ? "verify only" : Rate(rate, s.PauseHeld)));
        }
        return rows;
    }

    static long At(long[] values, int i) => i < values.Length ? values[i] : 0;

    static double Share(long done, long of) => of <= 0 ? 0 : Math.Clamp((double)done / of, 0, 1);

    const string CopyStep = "Copying";
    const string VerifyStep = "Verification";

    /// The phase indicator: Copying · Verification · Proxies (or Analysis) · Report — the phases this job does, numbered
    /// in a row; done ones full, the active one as far as it got. Empty for a finished job that did not end Done. Plan 10:
    /// with verify-in-proxy the verification follows the proxies — Copying · Proxies · Verification · Report.
    public static IReadOnlyList<PhaseStep> Steps(JobStatus s, JobConfig config, double phaseProgress)
    {
        bool stage2 = config.RepairInto == null && (config.Proxy.Enabled || config.AnalyzeClips);
        bool inProxy = config.Verify && config.VerifyInProxy && config.Proxy.Enabled && config.RepairInto == null;
        var labels = new List<string> { CopyStep };
        if (config.Verify && !inProxy) labels.Add(VerifyStep);
        if (stage2) labels.Add(config.Proxy.Enabled ? "Proxies" : "Analysis");
        if (inProxy) labels.Add(VerifyStep);
        labels.Add("Report");
        if (s.Phase is JobPhase.Failed or JobPhase.Cancelled) return [];
        int verify = labels.IndexOf(VerifyStep);
        int active = s.Phase switch
        {
            JobPhase.Waiting => -1,
            JobPhase.Copying or JobPhase.Flushing => 0,
            JobPhase.Verifying => 1,
            // The first report is written at the end of stage 1 (before the clips): the copy's last step, full.
            JobPhase.Reporting when s.CopySucceeded == null => config.Verify && !inProxy ? 1 : 0,
            JobPhase.Analyzing or JobPhase.Proxies => stage2 ? (inProxy ? verify - 1 : labels.Count - 2) : labels.Count - 1,
            JobPhase.Finishing => verify,
            JobPhase.Reporting => labels.Count - 1,
            _ => labels.Count,   // Done
        };
        bool stage1Report = s.Phase == JobPhase.Reporting && s.CopySucceeded == null;
        return labels.Select((label, i) =>
        {
            var state = i < active ? StepState.Done : i == active ? StepState.Active : StepState.Pending;
            double fill = state switch
            {
                StepState.Done => 1,
                StepState.Active => stage1Report ? 1 : Math.Clamp(phaseProgress, 0, 1),
                _ => 0,
            };
            return new PhaseStep(i + 1, label, state, fill);
        }).ToList();
    }

    /// "File 10 / 24" + the file's name, "Verifying" + how much, "Proxy 3 / 24" + the clip — under the speed table.
    public static (string Caption, string Value) FileLine(JobStatus s)
    {
        var c = s.Copy;
        return s.Phase switch
        {
            JobPhase.Copying when c != null => ($"File {Math.Min(c.FileIndex + 1, c.TotalFiles).ToString(Inv)} / {c.TotalFiles.ToString(Inv)}", Path.GetFileName(c.CurrentFile)),
            JobPhase.Flushing => ("Flushing disk caches", ""),
            JobPhase.Verifying when c != null => ("Verifying", $"{Format.Bytes3(c.VerifiedBytes.Sum())} / {Format.Bytes3(c.ToVerifyTotal)}"),
            JobPhase.Finishing => ("Finishing verification", $"{Format.Bytes3(s.VerifiedBytes)} / {Format.Bytes3(s.VerifyTotal)}"),
            JobPhase.Analyzing => ($"Clip {(s.ClipIndex + 1).ToString(Inv)} / {s.ClipCount.ToString(Inv)}", s.ClipName),
            JobPhase.Proxies => ($"Proxy {(s.ClipIndex + 1).ToString(Inv)} / {s.ClipCount.ToString(Inv)}", $"{s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %"),
            JobPhase.Reporting => ("Report", ""),
            _ => (s.Phase.Label(), ""),
        };
    }

    /// "~0:02 left for the copy · ~0:06 in total" — the phase's ETA and, when known, the whole job's; null without an ETA.
    public static string? Remaining(JobPhase phase, double? phaseSeconds, double? totalSeconds)
    {
        if (phaseSeconds is not double t) return totalSeconds is double only ? $"~{JobProgress.EtaText(only)} left in total" : null;
        var what = phase switch { JobPhase.Copying => "the copy", JobPhase.Verifying or JobPhase.Finishing => "verification", _ => phase.Label().ToLowerInvariant() };
        var text = $"~{JobProgress.EtaText(t)} left for {what}";
        return totalSeconds is double total && total > t ? $"{text} · ~{JobProgress.EtaText(total)} in total" : text;
    }

    /// "6 min 12 s", "45 s", "1 h 05 min".
    public static string Duration(TimeSpan t)
    {
        long s = (long)Math.Round(Math.Max(0, t.TotalSeconds));
        if (s < 60) return $"{s.ToString(Inv)} s";
        if (s < 3600) return $"{(s / 60).ToString(Inv)} min {(s % 60).ToString(Inv)} s";
        return $"{(s / 3600).ToString(Inv)} h {(s / 60 % 60).ToString("00", Inv)} min";
    }

    /// Copy · Verification · Proxies · Total of a finished job. `elapsed`: the job's own time without pauses (JobProgress);
    /// null = from the report.
    public static IReadOnlyList<SummaryField> Summary(OffloadReport r, TimeSpan? elapsed)
    {
        var proxy = r.Proxy is { Enabled: true }
            ? $"{r.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done).ToString(Inv)} / {r.Clips.Count(c => !(c.ProxyStatus == ProxyStatus.Skipped && (c.ProxyNote == null || c.OriginalLighter))).ToString(Inv)}"
            : "off";
        // Plan 10: verified with the proxies — its time is the proxies', no rate of its own.
        bool inProxy = r.Destinations.Any(d => d.VerifyMethod == ReportDestination.InProxy);
        return
        [
            new("Copy", Format.Rate(r.TotalBytes, r.CopySeconds)),
            new("Verification", !r.Verified ? "off" : inProxy ? "during proxies" : Format.Rate(r.TotalBytes, r.VerifySeconds)),
            new("Proxies", proxy),
            new("Total", Duration(elapsed ?? (r.Finished - r.Started - TimeSpan.FromSeconds(r.PausedSeconds)))),
        ];
    }

    /// Why a copy failed, in a word or two: hash, size, read, card, cancelled, write.
    public static string Problem(string state, string? error) => error switch
    {
        _ when state == "pending" => "not copied",
        { } e when e.StartsWith("HASH MISMATCH", StringComparison.Ordinal) || e.StartsWith("MD5 MISMATCH", StringComparison.Ordinal) => "hash",
        { } e when e.StartsWith("size mismatch", StringComparison.Ordinal) => "size",
        { } e when e.StartsWith("verify:", StringComparison.Ordinal) => "read",
        { } e when e.Contains("cancelled", StringComparison.Ordinal) => "cancelled",
        { } e when e.StartsWith("source:", StringComparison.Ordinal) => "card",
        _ => "write",
    };

    /// The files that are not on every destination, each with its state per destination (newest report of the job).
    public static IReadOnlyList<FailedFileRow> FailedRows(OffloadReport r, IReadOnlyList<string> destNames) =>
        // The path with Windows separators, like the path preview ("CLIP\A001C007.mxf"); the report keeps "/".
        r.FailedFiles.Select(f => new FailedFileRow(f.Path.Replace('/', '\\'), destNames.Select((name, d) =>
        {
            var state = d < f.Destinations.Count ? f.Destinations[d] : "pending";
            bool ok = f.SourceError == null && r.IsGood(state);
            var why = f.SourceError != null ? "card" : Problem(state, d < f.Errors.Count ? f.Errors[d] : null);
            return new FileOnDisk(name, ok, ok ? $"{name} ✓" : $"{name} ✕ {why}");
        }).ToList())).ToList();

    /// "… and 3 more files in the report" under the first `shown` rows; null when all are shown.
    public static string? MoreFiles(int total, int shown) =>
        total > shown ? $"… and {Texts.N(total - shown, "more file")} in the report" : null;

    /// The headline of a finished job (and of a waiting / running one: just its kind). The green "safe to format" is the
    /// job's own verdict (`JobStatus.SafeToFormat`, R5 — shared with the notification and history), never recomputed
    /// from this job's report: a repair's report lists only the repaired files. "Safe to eject" (SafeToEject) alone is
    /// never green (ruling I2). `backup`: the whole backup as BackupState read it (repair, append) for the words under a
    /// card that must not be formatted yet; `blockedByPaused`: a waiting job stands behind a paused one;
    /// `contentConfirmed`: the user confirmed the content warnings (spec 7) — until then the card is not green.
    public static CardHeadline Headline(string card, JobStatus s, OffloadReport? r, IReadOnlyList<string> destNames,
        BackupState? backup = null, bool blockedByPaused = false, bool contentConfirmed = false)
    {
        if (s.Phase == JobPhase.Waiting)
            return new(JobCardKind.Waiting, card, blockedByPaused ? WaitingForPaused : "Waiting in the queue");
        if (!s.Phase.IsFinished()) return new(JobCardKind.Running, card, "");
        if (s.Phase == JobPhase.Cancelled)
            return new(JobCardKind.Cancelled, $"{card} — cancelled", s.SafeToFormat
                ? "The copy is done and verified; only the processing after the copy was cancelled."
                : s.SafeToEject
                ? "The copy is done but not verified — the card is safe to eject; do not format it (Copy again verifies it)."
                : "The card is not completely backed up — copy it again in full.");
        if (r == null)
        {
            // Nothing new / nothing to repair: green only when the backup is really complete and verified.
            if (s.Phase == JobPhase.Done && !s.SafeToFormat)
                return new(JobCardKind.Problem, $"{card} — do not format the card yet", NotSafeWhy(s, backup));
            return s.Phase == JobPhase.Done
                ? new(JobCardKind.Done, $"{card} — the backup is complete", s.Note ?? Msg.NothingNew)
                : new(JobCardKind.Failed, $"{card} — offload failed", s.Error ?? "The copy failed.");
        }
        string notYet = s.SafeToEject ? $"{card} — safe to eject, do not format yet" : $"{card} — do not eject the card yet";
        if (!r.Success) return new(JobCardKind.Problem, notYet, backup is { Failed: > 0 } b ? StillFailed(b.Failed) : ProblemText(r, destNames));
        if (!r.Verified) return new(JobCardKind.Problem, $"{card} — copied without verification", SetupTexts.VerifyOffWarning);
        if (!s.SafeToFormat)
        {
            // This job's files are good, but the backup as a whole is not (earlier files never read back, files of the
            // card that are in no report, files that still fail): not formatted yet.
            return new(JobCardKind.Problem, notYet, NotSafeWhy(s, backup));
        }
        // Spec 7: the copy is exact, but the decoder found defects — formatting only once the user has looked.
        if (s.ContentWarnings > 0 && !contentConfirmed)
            return new(JobCardKind.Problem, $"{card} — check the content defects before formatting", Msg.ContentWarnings(s.ContentWarnings));
        var safe = r.IsRepair ? "All files of the backup are now on all disks and verified." : SafeText(r.TotalFiles, r.Destinations.Count, HashKinds.Label(HashKinds.FromNames(r.Algorithms)));
        // Stage 2 (previews, proxies) failed after a good copy: the copy is safe, but say the processing did not finish.
        if (s.Phase == JobPhase.Failed)
            safe = $"{safe} But the processing after the copy failed: {s.Error ?? "unknown error"}";
        return new(JobCardKind.Done, $"{card} — safe to format", safe);
    }

    /// R5, spec 9: the card row's two verdicts, never merged — "Safe to eject" once everything is copied, "Safe to format"
    /// once everything is verified (yellow until content warnings are confirmed). Ruling I2: green (Ok) only where the
    /// card may be formatted — "Safe to eject" alone is neutral. The window hides them while the job waits.
    public static (CardChip Eject, CardChip Format) Chips(JobStatus s, bool contentConfirmed = false)
    {
        bool running = s.Phase != JobPhase.Waiting && !s.Phase.IsFinished();
        var format = s.SafeToFormat
            ? s.ContentWarnings > 0 && !contentConfirmed ? new CardChip("Safe to format after checking defects", ChipTone.Warning) : new CardChip("Safe to format", ChipTone.Ok)
            : running ? new CardChip(s.SafeToEject ? "Verifying…" : "Do not format yet", ChipTone.Neutral)
            : new CardChip("Do not format", s.SafeToEject ? ChipTone.Warning : ChipTone.Error);
        var eject = s.SafeToEject ? new CardChip("Safe to eject", format.Tone == ChipTone.Ok ? ChipTone.Ok : ChipTone.Neutral)
            : running ? new CardChip("Do not eject yet", ChipTone.Neutral) : new CardChip("Do not eject", ChipTone.Error);
        return (eject, format);
    }

    /// Spec 7: the button that unlocks the green verdict once the user has looked at the content warnings (Offload never
    /// formats a card itself).
    public const string ContentConfirmText = "Checked — the card is safe to format";

    /// The confirmation is offered on a finished card that may be formatted but for its content warnings, until given.
    public static bool NeedsContentConfirm(JobStatus s, bool confirmed) =>
        s.Phase.IsFinished() && s.SafeToFormat && s.ContentWarnings > 0 && !confirmed;

    /// Spec 7, 9: every clip with a content warning — its name and the yellow label "01:00:02:13 · corrupted data" (the
    /// kind alone without a timecode), and whether the report has a picture of the place.
    public static IReadOnlyList<ContentLine> ContentLines(OffloadReport r) =>
        r.ContentWarnings.Select(c => new ContentLine(c.Clip.Name,
            c.Content!.Timecode is { Length: > 0 } tc ? $"{tc} · {VerifyInProxy.ContentResult.KindText(c.Content.Kind)}" : VerifyInProxy.ContentResult.KindText(c.Content.Kind),
            c.Content.Jpeg is { Length: > 0 })).ToList();

    static string NotSafeWhy(JobStatus s, BackupState? backup) => backup switch
    {
        { Failed: > 0 } b => StillFailed(b.Failed),
        { Unverified: > 0 } b => $"{Files(b.Unverified)} of the earlier backup {(b.Unverified == 1 ? "was" : "were")} not verified.",
        _ => s.Warning?.Split('\n')[0] ?? "The backup is not complete yet.",
    };

    static string StillFailed(int n) => $"{Files(n)} in the backup still {(n == 1 ? "does" : "do")} not match.";

    /// The sub of a waiting card whose queue stands behind a paused job (a paused job keeps its place).
    public const string WaitingForPaused = "waiting for a paused job";

    /// The line under a waiting card that stands behind a paused job, capitalised as a sentence of its own; else null.
    public static string? WaitingHint(CardHeadline h) =>
        h is { Kind: JobCardKind.Waiting, Sub: WaitingForPaused } ? "Waiting for a paused job." : null;

    /// "All 24 files are on 2 disks and verified (xxHash64)."
    public static string SafeText(int files, int disks, string hash)
    {
        var on = $"on {Texts.N(disks, "disk")}";
        return files == 1
            ? $"The file is {on} and verified ({hash})."
            : $"All {files.ToString(Inv)} files are {on} and verified ({hash}).";
    }

    /// "1 file on disk SHUTTLE_B does not match the card. Everything is fine on RAID_A."
    public static string ProblemText(OffloadReport r, IReadOnlyList<string> destNames)
    {
        var failed = r.FailedFiles;
        int fromCard = failed.Count(f => f.SourceError != null);
        var perDisk = destNames.Select((name, d) => (Name: name, Count: failed.Count(f => f.SourceError == null && !Good(r, f, d)))).ToList();
        var bad = perDisk.Where(p => p.Count > 0).ToList();
        var good = perDisk.Where(p => p.Count == 0).Select(p => p.Name).ToList();
        var parts = new List<string>();
        if (fromCard > 0) parts.Add($"Could not read {Files(fromCard)} from the card.");
        if (bad.Count == 1) parts.Add($"{Files(bad[0].Count)} on disk {bad[0].Name} {(bad[0].Count == 1 ? "does" : "do")} not match the card.");
        else if (bad.Count > 1) parts.Add($"Not matching the card: {string.Join(", ", bad.Select(b => $"{b.Name} {Files(b.Count)}"))}.");
        if (bad.Count > 0 && good.Count > 0 && fromCard == 0) parts.Add($"Everything is fine on {Join(good)}.");
        return string.Join(" ", parts);
    }

    static bool Good(OffloadReport r, ReportFile f, int d) => d < f.Destinations.Count && r.IsGood(f.Destinations[d]);

    static string Files(int n) => Texts.N(n, "file");

    static string Join(IReadOnlyList<string> names) => names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    /// The first `max` clips with a thumbnail (the middle frame when there are three) and how many clips are left.
    public static (IReadOnlyList<(string Clip, byte[] Jpeg)> Thumbs, int More) Thumbs(OffloadReport r, int max = 5)
    {
        var clips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        var thumbs = clips.Where(c => c.Thumbnails.Count > 0).Take(max)
            .Select(c => (c.Clip.Name, c.Thumbnails[c.Thumbnails.Count == 3 ? 1 : 0].Jpeg)).ToList();
        return (thumbs, thumbs.Count == 0 ? 0 : clips.Count - thumbs.Count);
    }

    /// "Bottleneck: the card reader (USB 3 (5 Gb/s)). The disks could do more." — from the brake while copying.
    public static string? BrakeSentence(Brake? brake, IReadOnlyList<ChainElement> chain, IReadOnlyList<string> destNames)
    {
        if (brake is not { } b || b.Element < 0 || b.Element >= chain.Count) return null;
        var e = chain[b.Element];
        var link = e.Link != null ? $" ({e.Link}{(b.LinkFull ? " — the link is saturated" : "")})" : "";
        if (b.Element == 0)
            return $"Bottleneck: the card reader{link}. {(destNames.Count == 1 ? "The disk could do more." : "The disks could do more.")}";
        var name = b.Element - 1 < destNames.Count ? destNames[b.Element - 1] : e.Role;
        return $"Bottleneck: disk {name}{link}. The card could be read faster.";
    }
}

/// The pill in the top bar and the queue tab's number (UI návrh: „● Running 1 offload · 1 in queue“).
public static class QueueSummary
{
    /// A job counts as paused only while it really waits (`PauseHeld`); between Pause and the stop it still runs.
    public static (int Running, int Paused, int Waiting) Count(IEnumerable<(JobPhase Phase, bool PauseHeld)> jobs)
    {
        int running = 0, paused = 0, waiting = 0;
        foreach (var (phase, held) in jobs)
        {
            if (phase == JobPhase.Waiting) waiting++;
            else if (!phase.IsFinished()) { if (held) paused++; else running++; }
        }
        return (running, paused, waiting);
    }

    /// Null when nothing is running, paused or waiting.
    public static string? Pill(int running, int paused, int waiting)
    {
        var parts = new List<string>();
        if (running > 0) parts.Add($"Running {Texts.N(running, "offload")}");
        if (paused > 0) parts.Add(running > 0 ? $"{paused.ToString(CultureInfo.InvariantCulture)} paused" : $"Paused: {Texts.N(paused, "offload")}");
        if (waiting > 0) parts.Add($"{waiting.ToString(CultureInfo.InvariantCulture)} in queue");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// The start button says "Add to queue" while a card waits for or is in its copy — a new one then waits.
    public static string StartLabel(IEnumerable<JobPhase> phases) =>
        phases.Any(p => p is JobPhase.Waiting or JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying) ? "Add to queue" : "Start offload";
}
