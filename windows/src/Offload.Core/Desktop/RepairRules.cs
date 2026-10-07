namespace Offload.Core.Desktop;

/// "Zkopírovat znovu" on a job card (UI návrh 4), without a window.
public static class RepairRules
{
    /// Offered on a job that ended (not cancelled) with files that are not on every destination — while the card it was
    /// copied from is in its drive (a folder is always there) and no repair of the same card folders is queued or running.
    public static bool Offer(JobPhase phase, OffloadReport? report, bool cardPresent, bool repairPending) =>
        phase is JobPhase.Done or JobPhase.Failed && report is { Success: false } r && r.FailedFiles.Count > 0 && cardPresent && !repairPending;

    /// The job card's title: "A001 · oprava".
    public static string Title(string cardName) => $"{cardName} · oprava";

    /// Two jobs work on the same backup: any card folder in common (any case, trailing slash).
    public static bool SameBackup(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Any(x => b.Any(y => string.Equals(x.TrimEnd('\\'), y.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));
}
