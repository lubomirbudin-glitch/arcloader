namespace Offload.Core;

public sealed partial class OffloadReport
{
    /// Plan 10: this report — written at the end of the copy with verifyPending — once the verification of stage 2 ended,
    /// as a new object (whoever holds this one, the UI, never sees it half changed). `results`: per destination, per file;
    /// `verified`: the verification ran to its end (false = cancelled: unread copies stay "written", as after a cancelled
    /// read-back); `primary`: the destination verified through the proxies (-1: none); `classicWhy`: per destination why it
    /// was read back instead.
    public OffloadReport WithVerification(IReadOnlyList<DestFileResult[]> results, bool verified, int primary, IReadOnlyList<string?> classicWhy,
        IReadOnlyList<string> notes, DateTime finished, TimeSpan verifyPaused)
    {
        var r = FromJson(ToJson());
        r.Clips = Clips;   // the thumbnails and the defect pictures are not in the JSON
        bool IsOk(FileState s) => s == FileState.Verified || (!verified && s == FileState.Written);
        for (int i = 0; i < r.Files.Count; i++)
        {
            var f = r.Files[i];
            f.Destinations = results.Select(d => d[i].State.Wire()).ToList();
            f.Errors = results.Select((d, n) => d[i].Error ?? (d[i].State == FileState.Failed && n < Files[i].Errors.Count ? Files[i].Errors[n] : null)).ToList();
        }
        for (int d = 0; d < r.Destinations.Count; d++)
        {
            var dest = r.Destinations[d];
            dest.FilesOK = results[d].Count(x => IsOk(x.State));
            dest.FilesFailed = results[d].Count(x => !IsOk(x.State));
            dest.VerifyMethod = !verified ? null : d == primary ? ReportDestination.InProxy : ReportDestination.ReadBack;
            dest.VerifyNote = verified && d != primary ? classicWhy[d] : null;
        }
        r.Verified = verified;
        r.VerifyPending = null;
        r.Finished = finished;
        r.VerifyPausedSeconds = verifyPaused > TimeSpan.Zero ? Math.Round(verifyPaused.TotalSeconds, 1) : null;
        r.SafeToFormatSeconds = r.SafeToFormat
            ? Math.Round(Math.Max(0, (finished - r.Started).TotalSeconds - r.PausedSeconds), 1) : null;
        r.VerifyNotes = notes.Count > 0 ? [.. notes] : null;
        return r;
    }
}
