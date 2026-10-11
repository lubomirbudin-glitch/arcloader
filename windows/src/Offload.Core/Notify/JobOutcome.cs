using Offload.Core.Desktop;

namespace Offload.Core.Notify;

/// How a job ended, for a notification (spec 4.3).
public enum OutcomeKind { Done, Attention, Error, Ejectable }

/// A notification: `Title` / `Message` for Windows (may name a disk or carry the error text), `PhoneMessage` for ntfy —
/// only the card name, the result and counts: never a path, a file name or the PC's name (R20, spec 5.6).
public sealed record JobOutcome(OutcomeKind Kind, string Title, string Message, string PhoneMessage)
{
    /// ntfy priority: done 3, attention 4, error 5.
    public int Priority => Kind switch { OutcomeKind.Error => 5, OutcomeKind.Attention => 4, _ => 3 };

    /// ntfy tags (emoji): white_check_mark / warning / rotating_light.
    public IReadOnlyList<string> Tags => Kind switch
    {
        OutcomeKind.Error => ["rotating_light"],
        OutcomeKind.Attention => ["warning"],
        _ => ["white_check_mark"],
    };

    /// "Safe to eject" is a Windows notification only (spec 4.3), not sent to the phone.
    public bool ForPhone => Kind != OutcomeKind.Ejectable;
}

/// The table of spec 4.3 as pure functions over the job's status and report.
public static class JobOutcomes
{
    /// The notification at the end of a job; null when it was cancelled (no notification).
    public static JobOutcome? Final(string card, JobStatus status, OffloadReport? report)
    {
        if (status.Phase == JobPhase.Cancelled) return null;
        // An append with nothing new ends Done without a report (its note says so).
        if (report == null && status.Phase == JobPhase.Done)
        {
            var note = status.Note ?? Msg.NothingNew;
            return status.Warning != null || !status.SafeToFormat
                ? Attention(card, $"{note} {FirstLine(status.Warning ?? "The backup is not fully verified.")}", $"{note} Needs attention — details in the app.")
                : new JobOutcome(OutcomeKind.Done, $"{Product.Name}: {card} done", note, note == Msg.NothingNew ? note : "Done.");
        }
        if (report == null)
            return Error(card, status.Error ?? "The copy failed.", "Error before copying — the card is NOT backed up.");
        if (!report.Success)
        {
            var failed = $"{Texts.N(report.FailedFiles.Count, "file")} failed — the card is NOT backed up.";
            return Error(card, failed, failed);
        }
        if (!report.Verified) return Attention(card, "The copy is done but NOT VERIFIED.", "The copy is done but NOT VERIFIED.");
        // Final review, item 5: "verified" only when the card may be formatted (every copy verified and the results in
        // every folder) — else the copy is only "done".
        var copy = status.SafeToFormat ? "The copy is verified" : "The copy is done";
        if (status.ProxiesFailed > 0)
        {
            var proxies = $"{copy}, but {Texts.N(status.ProxiesFailed, "proxy", "proxies")} failed.";
            return Attention(card, proxies, proxies);
        }
        if (status.Phase == JobPhase.Failed)
            return Attention(card, $"{copy}, but the processing after the copy failed.", $"{copy}, but the processing after the copy failed.");
        if (status.Warning != null)
            return Attention(card, $"{copy}, but: {FirstLine(status.Warning)}", $"{copy}, but needs attention — details in the app.");
        // R5, ruling I2 (plan 10): "done" only when every copy is verified (the whole backup for an append or a repair).
        if (!status.SafeToFormat)
            return Attention(card, "The copy is done, but do not format the card yet — it is not fully verified.", "The copy is done but not fully verified.");
        int disks = report.Destinations.Count;
        var done = $"Verified on {Texts.N(disks, "disk")} — the card is safe to format." + (status.ProxiesDone > 0 ? $" Proxies: {status.ProxiesDone} done." : "");
        return new JobOutcome(OutcomeKind.Done, $"{Product.Name}: {card} done", done, done);
    }

    /// At the end of stage 1 when everything is copied (R5 "Safe to eject") and stage 2 still runs (Windows only); else
    /// null. With verify-in-proxy (plan 10) the copies are verified only with the proxies: formatting waits.
    public static JobOutcome? CopyDone(string card, JobStatus status, bool proxies) =>
        status.SafeToEject && !status.Phase.IsFinished()
            ? new JobOutcome(OutcomeKind.Ejectable, $"{Product.Name}: {card} — safe to eject",
                status.SafeToFormat ? (proxies ? "Proxies are still rendering." : "Clips are still being processed.")
                    : "Verifying with the proxies — format only after it finishes.", "")
            : null;

    /// Ruling I2 (plan 10): the success chime (and nothing less) — every copy verified everywhere (SafeToFormat) and
    /// nothing to look at: a warning, also a note of the verification (e.g. read through the Windows cache, ruling C2),
    /// is an orange line and "needs attention", as Final says it — never the success sound.
    public static bool Chime(JobStatus status) =>
        status.SafeToFormat && status.Warning == null && status.Phase is not (JobPhase.Cancelled or JobPhase.Failed);

    static JobOutcome Attention(string card, string message, string phone) =>
        new(OutcomeKind.Attention, $"{Product.Name}: {card} — needs attention", message, phone);

    static JobOutcome Error(string card, string message, string phone) =>
        new(OutcomeKind.Error, $"{Product.Name}: {card} — ERROR", message, phone);

    static string FirstLine(string text) => text.Split('\n')[0].Trim();
}
