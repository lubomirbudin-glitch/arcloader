using System.Globalization;
using Offload.Core.Append;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// How a job card looks (UI návrh, obr. 2 a 3): running, waiting, done (green), with failed files (yellow), failed before
/// or without a copy (red), cancelled (grey).
public enum JobCardKind { Waiting, Running, Done, Problem, Failed, Cancelled }

/// One row of the speed table: "Čtení karty" or a destination (with its number), the share done and the speed.
public sealed record TransferRow(string Name, int? Number, double Share, string RateText);

public enum StepState { Done, Active, Pending }

/// One segment of the phase indicator: "1 · Kopírování".
public sealed record PhaseStep(int Number, string Label, StepState State, double Fill)
{
    public string Text => $"{Number} · {Label}";
}

/// One number of a finished card: "Kopie" / "602 MB/s".
public sealed record SummaryField(string Label, string Value);

/// One destination of a file that did not make it: "RAID_A ✓", "SHUTTLE_B ✕ hash".
public sealed record FileOnDisk(string Name, bool Ok, string Text);

public sealed record FailedFileRow(string Path, IReadOnlyList<FileOnDisk> Disks);

/// The big line of a finished card and the sentence under it.
public sealed record CardHeadline(JobCardKind Kind, string Title, string Sub);

/// What the card header says under the name: the files or clips, the size and the camera.
public sealed record JobCardFacts(int Files, int Clips, long Bytes, string? Camera, bool Append);

/// The texts and numbers of the new job card (UI návrh, nové věci 1–4) — pure functions, the window only binds them.
public static class JobCard
{
    static readonly CultureInfo Cs = CultureInfo.GetCultureInfo("cs-CZ");

    /// The name a destination is shown with: its folder's name ("RAID_A"), a drive root as it is ("E:\").
    public static string DiskName(string root) => Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;

    /// "661 MB/s"; a paused job shows a dash.
    public static string Rate(double bytesPerSecond, bool paused = false) =>
        paused ? "—" : (bytesPerSecond / 1_000_000).ToString("0", CultureInfo.InvariantCulture) + " MB/s";

    /// "24 klipů · 1,70 GB · ALEXA 35", an append "2 nové klipy · 14,2 GB · stejné cíle".
    public static string Sub(JobCardFacts f)
    {
        var what = f.Clips > 0
            ? f.Append ? $"{f.Clips} {Texts.Plural(f.Clips, "nový klip", "nové klipy", "nových klipů")}" : Texts.Clips(f.Clips)
            : f.Append ? $"{f.Files} {Texts.Plural(f.Files, "nový soubor", "nové soubory", "nových souborů")}" : $"{f.Files} {Texts.Plural(f.Files, "soubor", "soubory", "souborů")}";
        var parts = new List<string> { what, Format.BytesCs3(f.Bytes) };
        if (f.Append) parts.Add("stejné cíle");
        else if (f.Camera is { Length: > 0 } camera) parts.Add(camera);
        return string.Join(" · ", parts);
    }

    /// The speed table while the card is read and written or read back: "Čtení karty" (copy only), then every destination.
    public static IReadOnlyList<TransferRow> Rows(JobProgress p, IReadOnlyList<string> destNames)
    {
        var s = p.Status;
        if (s.Copy is not { } c || s.Phase is not (JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying)) return [];
        bool verifying = s.Phase == JobPhase.Verifying;
        var rows = new List<TransferRow>();
        if (!verifying)
            rows.Add(new TransferRow("Čtení karty", null, Share(c.ReadBytes, c.TotalBytes), Rate(p.ReadRate, s.Paused)));
        for (int d = 0; d < destNames.Count; d++)
        {
            long done = verifying ? At(c.VerifiedBytes, d) : At(c.WrittenBytes, d);
            long of = verifying ? c.TotalBytes : c.Planned(d);
            double rate = d < p.DestRates.Length ? p.DestRates[d] : 0;
            // A destination a repair only reads back has nothing to write: full, and no speed while the others copy.
            rows.Add(new TransferRow(destNames[d], d + 1, of == 0 ? 1 : Share(done, of), of == 0 && !verifying ? "jen ověření" : Rate(rate, s.Paused)));
        }
        return rows;
    }

    static long At(long[] values, int i) => i < values.Length ? values[i] : 0;

    static double Share(long done, long of) => of <= 0 ? 0 : Math.Clamp((double)done / of, 0, 1);

    /// The phase indicator: Kopírování · Ověření · Proxy (or Analýza) · Report — the phases this job does, numbered in a
    /// row; done ones full, the active one as far as it got. Empty for a finished job that did not end Done.
    public static IReadOnlyList<PhaseStep> Steps(JobStatus s, JobConfig config, double phaseProgress)
    {
        var labels = new List<string> { "Kopírování" };
        if (config.Verify) labels.Add("Ověření");
        bool stage2 = config.RepairInto == null && (config.Proxy.Enabled || config.AnalyzeClips);
        if (stage2) labels.Add(config.Proxy.Enabled ? "Proxy" : "Analýza");
        labels.Add("Report");
        if (s.Phase is JobPhase.Failed or JobPhase.Cancelled) return [];
        int active = s.Phase switch
        {
            JobPhase.Waiting => -1,
            JobPhase.Copying or JobPhase.Flushing => 0,
            JobPhase.Verifying => 1,
            // The first report is written at the end of stage 1 (before the clips): the copy's last step, full.
            JobPhase.Reporting when s.CopySucceeded == null => config.Verify ? 1 : 0,
            JobPhase.Analyzing or JobPhase.Proxies => stage2 ? labels.Count - 2 : labels.Count - 1,
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

    /// "Soubor 10 / 24" + the file's name, "Ověřování" + how much, "Proxy 3 / 24" + the clip — under the speed table.
    public static (string Caption, string Value) FileLine(JobStatus s)
    {
        var c = s.Copy;
        return s.Phase switch
        {
            JobPhase.Copying when c != null => ($"Soubor {Math.Min(c.FileIndex + 1, c.TotalFiles)} / {c.TotalFiles}", Path.GetFileName(c.CurrentFile)),
            JobPhase.Flushing => ("Zápis cache disků", ""),
            JobPhase.Verifying when c != null => ("Ověřování", $"{Format.BytesCs3(c.VerifiedBytes.Sum())} / {Format.BytesCs3(c.TotalBytes * Math.Max(1, c.VerifiedBytes.Length))}"),
            JobPhase.Analyzing => ($"Klip {s.ClipIndex + 1} / {s.ClipCount}", s.ClipName),
            JobPhase.Proxies => ($"Proxy {s.ClipIndex + 1} / {s.ClipCount}", $"{s.ClipName} · {(s.ClipFraction * 100).ToString("0", CultureInfo.InvariantCulture)} %"),
            JobPhase.Reporting => ("Report", ""),
            _ => (s.Phase.Label(), ""),
        };
    }

    /// "zbývá ~0:02 kopie · ~0:06 celkem" — the phase's ETA and, when known, the whole job's; null without an ETA.
    public static string? Remaining(JobPhase phase, double? phaseSeconds, double? totalSeconds)
    {
        if (phaseSeconds is not double t) return totalSeconds is double only ? $"zbývá ~{JobProgress.EtaText(only)} celkem" : null;
        var what = phase switch { JobPhase.Copying => "kopie", JobPhase.Verifying => "ověření", _ => phase.Label().ToLower(Cs) };
        var text = $"zbývá ~{JobProgress.EtaText(t)} {what}";
        return totalSeconds is double total && total > t ? $"{text} · ~{JobProgress.EtaText(total)} celkem" : text;
    }

    /// "6 min 12 s", "45 s", "1 h 05 min".
    public static string Duration(TimeSpan t)
    {
        long s = (long)Math.Round(Math.Max(0, t.TotalSeconds));
        if (s < 60) return $"{s} s";
        if (s < 3600) return $"{s / 60} min {s % 60} s";
        return $"{s / 3600} h {s / 60 % 60:00} min";
    }

    /// Kopie · Ověření · Proxy · Celkem of a finished job. `elapsed`: the job's own time without pauses (JobProgress);
    /// null = from the report.
    public static IReadOnlyList<SummaryField> Summary(OffloadReport r, TimeSpan? elapsed)
    {
        var proxy = r.Proxy is { Enabled: true }
            ? $"{r.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done)} / {r.Clips.Count(c => !(c.ProxyStatus == ProxyStatus.Skipped && c.ProxyNote == null))}"
            : "vypnuto";
        return
        [
            new("Kopie", Format.Rate(r.TotalBytes, r.CopySeconds)),
            new("Ověření", r.Verified ? Format.Rate(r.TotalBytes, r.VerifySeconds) : "vypnuto"),
            new("Proxy", proxy),
            new("Celkem", Duration(elapsed ?? (r.Finished - r.Started - TimeSpan.FromSeconds(r.PausedSeconds)))),
        ];
    }

    /// Why a copy failed, in two words: hash, velikost, čtení, karta, zrušeno, zápis.
    public static string Problem(string state, string? error) => error switch
    {
        _ when state == "pending" => "nezkopírováno",
        { } e when e.StartsWith("HASH MISMATCH", StringComparison.Ordinal) || e.StartsWith("MD5 MISMATCH", StringComparison.Ordinal) => "hash",
        { } e when e.StartsWith("size mismatch", StringComparison.Ordinal) => "velikost",
        { } e when e.StartsWith("verify:", StringComparison.Ordinal) => "čtení",
        { } e when e.Contains("cancelled", StringComparison.Ordinal) => "zrušeno",
        { } e when e.StartsWith("source:", StringComparison.Ordinal) => "karta",
        _ => "zápis",
    };

    /// The files that are not on every destination, each with its state per destination (newest report of the job).
    public static IReadOnlyList<FailedFileRow> FailedRows(OffloadReport r, IReadOnlyList<string> destNames) =>
        // The path with Windows separators, like the path preview ("CLIP\A001C007.mxf"); the report keeps "/".
        r.FailedFiles.Select(f => new FailedFileRow(f.Path.Replace('/', '\\'), destNames.Select((name, d) =>
        {
            var state = d < f.Destinations.Count ? f.Destinations[d] : "pending";
            bool ok = f.SourceError == null && r.IsGood(state);
            var why = f.SourceError != null ? "karta" : Problem(state, d < f.Errors.Count ? f.Errors[d] : null);
            return new FileOnDisk(name, ok, ok ? $"{name} ✓" : $"{name} ✕ {why}");
        }).ToList())).ToList();

    /// "… a další 3 soubory" under the first `shown` rows; null when all are shown.
    public static string? MoreFiles(int total, int shown) =>
        total > shown ? $"… a {Texts.Plural(total - shown, "další", "další", "dalších")} {total - shown} {Texts.Plural(total - shown, "soubor", "soubory", "souborů")} v reportu" : null;

    /// The headline of a finished job (and of a waiting / running one: just its kind). The green "kartu lze vysunout" is
    /// the job's own verdict (`JobStatus.SafeToEject`, shared with the eject notification and history), never recomputed
    /// from this job's report: a repair's report lists only the repaired files. `backup`: the whole backup as
    /// BackupState read it (repair, append) for the words under a card that must not be ejected yet; `blockedByPaused`:
    /// a waiting job stands behind a paused one.
    public static CardHeadline Headline(string card, JobStatus s, OffloadReport? r, IReadOnlyList<string> destNames,
        BackupState? backup = null, bool blockedByPaused = false)
    {
        if (s.Phase == JobPhase.Waiting)
            return new(JobCardKind.Waiting, card, blockedByPaused ? WaitingForPaused : "Čeká ve frontě");
        if (!s.Phase.IsFinished()) return new(JobCardKind.Running, card, "");
        if (s.Phase == JobPhase.Cancelled)
            return new(JobCardKind.Cancelled, $"{card} — zrušeno", s.SafeToEject
                ? "Kopie je hotová a ověřená, zrušilo se jen zpracování po kopii."
                : "Karta není kompletně zálohovaná — zkopírujte ji znovu celou.");
        if (r == null)
            return s.Phase == JobPhase.Done
                ? new(JobCardKind.Done, $"{card} — záloha je úplná", s.Note ?? Msg.NothingNew)
                : new(JobCardKind.Failed, $"{card} — offload selhal", s.Error ?? "Kopie se nepodařila.");
        string notYet = $"{card} — kartu zatím nevysunujte";
        if (!r.Success) return new(JobCardKind.Problem, notYet, backup is { Failed: > 0 } b ? $"V záloze {Files(b.Failed)} stále nesouhlasí." : ProblemText(r, destNames));
        if (!r.Verified) return new(JobCardKind.Problem, $"{card} — zkopírováno bez ověření", SetupTexts.VerifyOffWarning);
        if (!s.SafeToEject)
        {
            // This job's files are good, but the backup as a whole is not (earlier files never read back, files of the
            // card that are in no report, files that still fail): the card stays in.
            var why = backup switch
            {
                { Failed: > 0 } b => $"V záloze {Files(b.Failed)} stále nesouhlasí.",
                { Unverified: > 0 } b => $"{Files(b.Unverified)} dřívější zálohy nebylo ověřeno.",
                _ => s.Warning?.Split('\n')[0] ?? "Záloha ještě není kompletní.",
            };
            return new(JobCardKind.Problem, notYet, why);
        }
        return new(JobCardKind.Done, $"{card} — kartu lze vysunout",
            r.IsRepair ? "Všechny soubory zálohy jsou teď na všech discích a ověřené." : SafeText(r.TotalFiles, r.Destinations.Count, HashKinds.Label(HashKinds.FromNames(r.Algorithms))));
    }

    /// The sub of a waiting card whose queue stands behind a paused job (a paused job keeps its place).
    public const string WaitingForPaused = "čeká na pozastavenou úlohu";

    /// "Všech 24 souborů je na 2 discích a ověřených (xxHash64)."
    public static string SafeText(int files, int disks, string hash)
    {
        var on = $"na {disks} {Texts.Plural(disks, "disku", "discích", "discích")}";
        return files switch
        {
            1 => $"Soubor je {on} a je ověřený ({hash}).",
            >= 2 and <= 4 => $"Všechny {files} soubory jsou {on} a ověřené ({hash}).",
            _ => $"Všech {files} souborů je {on} a ověřených ({hash}).",
        };
    }

    /// "1 soubor na disku SHUTTLE_B nesouhlasí s kartou. Na RAID_A je vše v pořádku."
    public static string ProblemText(OffloadReport r, IReadOnlyList<string> destNames)
    {
        var failed = r.FailedFiles;
        int fromCard = failed.Count(f => f.SourceError != null);
        var perDisk = destNames.Select((name, d) => (Name: name, Count: failed.Count(f => f.SourceError == null && !Good(r, f, d)))).ToList();
        var bad = perDisk.Where(p => p.Count > 0).ToList();
        var good = perDisk.Where(p => p.Count == 0).Select(p => p.Name).ToList();
        var parts = new List<string>();
        if (fromCard > 0) parts.Add($"{Files(fromCard)} se nepodařilo přečíst z karty.");
        if (bad.Count == 1) parts.Add($"{Files(bad[0].Count)} na disku {bad[0].Name} nesouhlasí s kartou.");
        else if (bad.Count > 1) parts.Add($"S kartou nesouhlasí: {string.Join(", ", bad.Select(b => $"{b.Name} {Files(b.Count)}"))}.");
        if (bad.Count > 0 && good.Count > 0 && fromCard == 0) parts.Add($"Na {Join(good)} je vše v pořádku.");
        return string.Join(" ", parts);
    }

    static bool Good(OffloadReport r, ReportFile f, int d) => d < f.Destinations.Count && r.IsGood(f.Destinations[d]);

    static string Files(int n) => $"{n} {Texts.Plural(n, "soubor", "soubory", "souborů")}";

    static string Join(IReadOnlyList<string> names) => names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} a {names[^1]}";

    /// The first `max` clips with a thumbnail (the middle frame when there are three) and how many clips are left.
    public static (IReadOnlyList<(string Clip, byte[] Jpeg)> Thumbs, int More) Thumbs(OffloadReport r, int max = 5)
    {
        var clips = r.Clips.Where(c => !c.Clip.IsCameraProxy).ToList();
        var thumbs = clips.Where(c => c.Thumbnails.Count > 0).Take(max)
            .Select(c => (c.Clip.Name, c.Thumbnails[c.Thumbnails.Count == 3 ? 1 : 0].Jpeg)).ToList();
        return (thumbs, thumbs.Count == 0 ? 0 : clips.Count - thumbs.Count);
    }

    /// "Nejpomalejší článek: čtečka karty (USB 3 (5 Gb/s)). Disky by zvládly víc." — from the brake while copying.
    public static string? BrakeSentence(Brake? brake, IReadOnlyList<ChainElement> chain, IReadOnlyList<string> destNames)
    {
        if (brake is not { } b || b.Element < 0 || b.Element >= chain.Count) return null;
        var e = chain[b.Element];
        var link = e.Link != null ? $" ({e.Link}{(b.LinkFull ? " — linka je plná" : "")})" : "";
        if (b.Element == 0)
            return $"Nejpomalejší článek: čtečka karty{link}. {(destNames.Count == 1 ? "Disk by zvládl víc." : "Disky by zvládly víc.")}";
        var name = b.Element - 1 < destNames.Count ? destNames[b.Element - 1] : e.Role;
        return $"Nejpomalejší článek: disk {name}{link}. Karta by šla číst rychleji.";
    }
}

/// The pill in the top bar and the queue tab's number (UI návrh: „● Běží 1 offload · 1 ve frontě“).
public static class QueueSummary
{
    public static (int Running, int Paused, int Waiting) Count(IEnumerable<(JobPhase Phase, bool Paused)> jobs)
    {
        int running = 0, paused = 0, waiting = 0;
        foreach (var (phase, isPaused) in jobs)
        {
            if (phase == JobPhase.Waiting) waiting++;
            else if (!phase.IsFinished()) { if (isPaused) paused++; else running++; }
        }
        return (running, paused, waiting);
    }

    /// Null when nothing is running, paused or waiting.
    public static string? Pill(int running, int paused, int waiting)
    {
        var parts = new List<string>();
        if (running > 0) parts.Add($"Běží {running} {Offloads(running)}");
        if (paused > 0) parts.Add(running > 0 ? $"{paused} {Texts.Plural(paused, "pozastavený", "pozastavené", "pozastavených")}" : $"Pozastaveno: {paused} {Offloads(paused)}");
        if (waiting > 0) parts.Add($"{waiting} ve frontě");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    static string Offloads(int n) => Texts.Plural(n, "offload", "offloady", "offloadů");

    /// The start button says "Zařadit do fronty" while a card waits for or is in its copy — a new one then waits.
    public static string StartLabel(IEnumerable<JobPhase> phases) =>
        phases.Any(p => p is JobPhase.Waiting or JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying) ? "Zařadit do fronty" : "Spustit offload";
}
