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
    public const string OtherCardText = "karta v mechanice není ta, která se kopírovala";
    public const string InUseText = "z karty ještě kopíruje jiná úloha — vysuňte ji, až její kopie doběhne";

    /// Why "Vysunout kartu" must not eject, or null. `copied` = the card recorded when the copy started,
    /// `current` = the card in the drive now (both `CardIdentity`), `cardInUse` = `CardInUse` for the other jobs.
    public static string? EjectProblem(string? copied, string? current, bool cardInUse)
    {
        if (current == null) return "karta není připojená";
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

    /// Measuring writes 1 GB to the destinations — not while a copy writes or verifies them.
    public static bool MeasureBlocked(IEnumerable<JobPhase> phases) =>
        phases.Any(p => p is JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying);

    public static string ReportPath(string cardPath, string cardName) =>
        Path.Combine(cardPath, Layout.ReportDirName, $"{cardName}_report.html");

    /// The HTML report from the first destination that has it (a destination may be disconnected), or null.
    /// Touches the destinations — never call it on the UI thread.
    public static string? FirstReport(IEnumerable<string> cardPaths, string cardName) =>
        cardPaths.Select(c => ReportPath(c, cardName)).FirstOrDefault(File.Exists);

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
