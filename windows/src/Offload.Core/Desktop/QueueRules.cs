namespace Offload.Core.Desktop;

/// Which card is in a drive: the volume (GUID path) plus the serial number its file system got when it was
/// formatted. The volume GUID alone is not enough — a card reader keeps the same volume for every card put into
/// its slot; two separately formatted cards have different serials.
public static class CardIdentity
{
    /// Null when there is no readable volume at `path`. Touches the drive — never call it on the UI thread.
    public static string? Of(string path)
    {
        try
        {
            if (NativeIO.VolumeSerial(path) is not uint serial) return null;
            return NativeIO.VolumeId(path) is string volume ? $"{volume}|{serial:X8}" : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// Decisions of the queue panel that need no window (final review of plan 4).
public static class QueueRules
{
    public const string OtherCardText = "the card in the drive is not the one that was copied";
    public const string InUseText = "another job is still copying from the card — eject it when that copy finishes";
    public const string MeasuringText = "A speed measurement is running — wait or stop it";

    /// Why Start is disabled for a reason the setup itself does not show, or null. A speed measurement writes 1 GB
    /// to the destinations and reads the cards: no offload may start under it (the measurement in turn waits for copies).
    public static string? StartProblem(bool measuring) => measuring ? MeasuringText : null;

    /// Why "Vysunout kartu" must not eject, or null. `copied` = the card recorded when the copy started,
    /// `current` = the card in the drive now (both `CardIdentity`), `cardInUse` = `CardInUse` for the other jobs.
    public static string? EjectProblem(string? copied, string? current, bool cardInUse)
    {
        if (current == null) return "the card is not connected";
        if (copied == null || !string.Equals(copied, current, StringComparison.OrdinalIgnoreCase)) return OtherCardText;
        return cardInUse ? InUseText : null;
    }

    /// Another job still needs the card on the drive of `source`: it is queued or in stage 1 (copy, flush,
    /// verification, report). Stage 2 (clips, proxies) reads only the destinations, so it does not hold the card —
    /// the same rule that lets a job's own card be ejected while its proxies render.
    public static bool CardInUse(string source, IEnumerable<(string Source, JobPhase Phase)> others) =>
        others.Any(o => (o.Phase is JobPhase.Waiting or JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting)
                        && SameDrive(o.Source, source));

    /// The scan shows the card that was copied in its drive (not gone, not another card in the same slot).
    public static bool CardInDrive(string source, string? copied, IEnumerable<VolumeItem> scanned) =>
        copied != null && scanned.Any(v => SameDrive(v.Path, source) && string.Equals(v.Identity, copied, StringComparison.OrdinalIgnoreCase));

    /// A waiting job stands behind a paused one (Tasks 1–3 review, minor 3): the copy queue runs one stage 1 at a time,
    /// so a job paused in stage 1 holds every waiting card; one paused in stage 2 holds none of them.
    public static bool BlockedByPaused(JobPhase phase, IEnumerable<(JobPhase Phase, bool Paused)> others) =>
        phase == JobPhase.Waiting && others.Any(o => o.Paused && o.Phase is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting);

    /// Measuring writes 1 GB to the destinations — not while a copy writes or verifies them.
    public static bool MeasureBlocked(IEnumerable<JobPhase> phases) =>
        phases.Any(p => p is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying);

    /// A job card still needs the app (closing waits for it): not finished, an eject in flight, or — ruling I2 — its
    /// day report is still being made on the post thread until the queue reports the job finished.
    /// "Clear finished" therefore removes a card only when it is finished and not still needed (fix round 1, item 3).
    public static bool StillNeeded(JobPhase phase, bool ejecting, bool dayReportPending, bool leftQueue) =>
        !phase.IsFinished() || ejecting || (dayReportPending && !leftQueue);

    /// A job that ended in stage 1 (done without stage 2, cancelled, failed) has only its day report left, which may queue
    /// behind other cards' stage 2: the attention flash and sound come when the copy ends, not after that report.
    public static bool AlertAtCopyEnd(JobPhase phase) => phase.IsFinished();

    /// Spec 4.3: the end-of-job notification goes out when the copy ends if the job ended there (it must not wait for the
    /// day report), and once more only if that did not happen: at JobFinished.
    public static bool NotifyAtCopyEnd(JobPhase phase, bool notified) => !notified && phase.IsFinished();
    public static bool NotifyAtJobEnd(bool notified) => !notified;

    /// The HTML report of generation `generation` (an append has its own, spec 2.2).
    public static string ReportPath(string cardPath, string cardName, int generation = 1) =>
        Path.Combine(cardPath, Layout.ReportDirName, OffloadReport.HtmlName(cardName, generation));

    /// The HTML report from the first destination that has it (a destination may be disconnected), or null.
    /// Touches the destinations — never call it on the UI thread.
    public static string? FirstReport(IEnumerable<string> cardPaths, string cardName, int generation = 1) =>
        cardPaths.Select(c => ReportPath(c, cardName, generation)).FirstOrDefault(File.Exists);

    static bool SameDrive(string a, string b) =>
        Root(a) is string ra && string.Equals(ra, Root(b), StringComparison.OrdinalIgnoreCase);

    static string? Root(string path)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path.Length == 2 && path[1] == ':' ? path + "\\" : path))?.TrimEnd('\\');
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
