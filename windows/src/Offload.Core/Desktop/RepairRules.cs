namespace Offload.Core.Desktop;

/// "Copy again" on a job card (UI návrh 4), without a window.
public static class RepairRules
{
    /// Offered on a job that ended (not cancelled) with files that are not on every destination — while the card it was
    /// copied from is in its drive (a folder is always there), no repair of the same card folders is queued or running and no
    /// later repair of them has already finished successfully (`repairedLater`).
    public static bool Offer(JobPhase phase, OffloadReport? report, bool cardPresent, bool repairPending, bool repairedLater = false) =>
        phase is JobPhase.Done or JobPhase.Failed && report is { Success: false } r && r.FailedFiles.Count > 0 && cardPresent && !repairPending && !repairedLater;

    /// A repair job (`RepairInto` = the card folders it repairs) of the same backup finished successfully: the failed files
    /// are on every disk now. `laterJobs` are the jobs started after the card in question.
    public static bool RepairedLater(IReadOnlyList<string> cardPaths, IEnumerable<(IReadOnlyList<string>? RepairInto, JobPhase Phase, OffloadReport? Report)> laterJobs) =>
        laterJobs.Any(j => j.RepairInto is { } into && j.Phase == JobPhase.Done && j.Report is { Success: true } && SameBackup(into, cardPaths));

    /// The job card's title: "A001 · oprava".
    public static string Title(string cardName) => $"{cardName} · oprava";

    /// Two jobs work on the same backup: any card folder in common (any case, trailing slash).
    public static bool SameBackup(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Any(x => b.Any(y => string.Equals(x.TrimEnd('\\'), y.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));
}
