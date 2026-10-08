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

    /// "Kartu lze vysunout" is a Windows notification only (spec 4.3), not sent to the phone.
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
                ? Attention(card, $"{note} {FirstLine(status.Warning ?? "Záloha není celá ověřená.")}", $"{note} Vyžaduje pozornost — podrobnosti v aplikaci.")
                : new JobOutcome(OutcomeKind.Done, $"Offload: {card} hotovo", note, note == Msg.NothingNew ? note : "Hotovo.");
        }
        if (report == null)
            return Error(card, status.Error ?? "Kopie se nepodařila.", "Chyba před kopírováním — karta NENÍ zálohovaná.");
        if (!report.Success)
        {
            var failed = $"{report.FailedFiles.Count} {Texts.Plural(report.FailedFiles.Count, "soubor selhal", "soubory selhaly", "souborů selhalo")} — karta NENÍ zálohovaná.";
            return Error(card, failed, failed);
        }
        if (!report.Verified) return Attention(card, "Kopie je hotová, ale NEOVĚŘENÁ.", "Kopie je hotová, ale NEOVĚŘENÁ.");
        if (status.ProxiesFailed > 0)
        {
            var proxies = $"Kopie je ověřená, ale {status.ProxiesFailed} {Texts.Plural(status.ProxiesFailed, "proxy selhala", "proxy selhaly", "proxy selhalo")}.";
            return Attention(card, proxies, proxies);
        }
        if (status.Phase == JobPhase.Failed)
            return Attention(card, "Kopie je ověřená, ale zpracování po kopii selhalo.", "Kopie je ověřená, ale zpracování po kopii selhalo.");
        if (status.Warning != null)
            return Attention(card, $"Kopie je ověřená, ale: {FirstLine(status.Warning)}", "Kopie je ověřená, ale vyžaduje pozornost — podrobnosti v aplikaci.");
        // R5, ruling I2 (plan 10): "hotovo" only when every copy is verified (the whole backup for an append or a repair).
        if (!status.SafeToFormat)
            return Attention(card, "Kopie je hotová, ale kartu zatím neformátujte — není celá ověřená.", "Kopie je hotová, ale není celá ověřená.");
        int disks = report.Destinations.Count;
        var done = $"Ověřeno na {disks} {Texts.Plural(disks, "disku", "discích", "discích")}." + (status.ProxiesDone > 0 ? $" Proxy: {status.ProxiesDone} hotovo." : "");
        return new JobOutcome(OutcomeKind.Done, $"Offload: {card} hotovo", done, done);
    }

    /// At the end of stage 1 when everything is copied (R5 "Lze vyjmout") and stage 2 still runs (Windows only); else
    /// null. With verify-in-proxy (plan 10) the copies are verified only with the proxies: formatting waits.
    public static JobOutcome? CopyDone(string card, JobStatus status, bool proxies) =>
        status.SafeToEject && !status.Phase.IsFinished()
            ? new JobOutcome(OutcomeKind.Ejectable, $"Offload: {card} — kartu lze vysunout",
                status.SafeToFormat ? (proxies ? "Proxy se ještě renderují." : "Klipy se ještě zpracovávají.")
                    : "Ověřuje se s proxy — formátovat až po dokončení.", "")
            : null;

    /// Ruling I2 (plan 10): the success chime (and nothing less) — every copy verified everywhere (SafeToFormat) and
    /// nothing to look at: a warning, also a note of the verification (e.g. read through the Windows cache, ruling C2),
    /// is an orange line and "vyžaduje pozornost", as Final says it — never the success sound.
    public static bool Chime(JobStatus status) =>
        status.SafeToFormat && status.Warning == null && status.Phase is not (JobPhase.Cancelled or JobPhase.Failed);

    static JobOutcome Attention(string card, string message, string phone) =>
        new(OutcomeKind.Attention, $"Offload: {card} — vyžaduje pozornost", message, phone);

    static JobOutcome Error(string card, string message, string phone) =>
        new(OutcomeKind.Error, $"Offload: {card} — CHYBA", message, phone);

    static string FirstLine(string text) => text.Split('\n')[0].Trim();
}
