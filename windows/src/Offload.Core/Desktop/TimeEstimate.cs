using Offload.Core.Space;

namespace Offload.Core.Desktop;

/// "estimate ~0:08 incl. proxies" (UI návrh, nová věc 10): how long a start takes, from what the jobs of this session really took
/// — the copy and read-back speed of the last job big enough to measure, and the time stage 2 took per second of clip
/// with the same stage-2 settings. Memory only (a new session starts without an estimate). When any part a start needs
/// is not known the estimate is null and the window shows only the size: a guess would be worse than nothing.
public sealed class RateMemory
{
    /// Jobs smaller than this are too short to measure a speed (the start-up cost would dominate).
    public const long MinBytes = 200_000_000;

    double? copy, verify;
    readonly Dictionary<string, double> post = new(StringComparer.Ordinal);

    /// Bytes per second of the copy and of the read-back, null until a job measured them.
    public double? CopyRate => copy;
    public double? VerifyRate => verify;

    /// What stage 2 costs depends on what it does: analysis only, or proxies of one codec, height and bitrate.
    public static string? PostKey(bool analyze, ProxySettings proxy) =>
        proxy.Enabled ? $"proxy|{proxy.Codec.Wire()}|{proxy.Height}|{proxy.BitrateMbps}" : analyze ? "analysis" : null;

    /// A finished job: `stage2` is how long its stage 2 ran without pauses (JobProgress.Stage2Elapsed), null without one;
    /// `endedAs`: how the job ended. Ruling M2: a cancelled job teaches nothing, and a stage 2 that did not end Done (a
    /// proxy run stopped after 2 of 20 clips) would teach a far too low cost. A repair's report covers only some files
    /// and some disks, so it teaches nothing either.
    public void Learn(OffloadReport r, bool analyze, ProxySettings proxy, TimeSpan? stage2, JobPhase endedAs = JobPhase.Done)
    {
        if (endedAs == JobPhase.Cancelled || r.IsRepair) return;
        if (r.TotalBytes >= MinBytes && r.CopySeconds > 0 && r.Success) copy = r.TotalBytes / r.CopySeconds;
        if (r.TotalBytes >= MinBytes && r.Verified && r.VerifySeconds > 0) verify = r.TotalBytes / r.VerifySeconds;
        double clipSeconds = r.Clips.Where(c => !c.Clip.IsCameraProxy).Sum(c => c.Info?.Duration ?? 0);
        if (endedAs == JobPhase.Done && stage2 is TimeSpan t && t > TimeSpan.Zero && clipSeconds > 0 && PostKey(analyze, proxy) is string key)
            post[key] = t.TotalSeconds / clipSeconds;
    }

    /// The seconds of footage on a card before it is read: from the clips' sizes at a careful (low) data rate, as the
    /// space check estimates them (SpaceEstimator.SourceMbps) — the estimate leans long.
    public static double ClipSeconds(IEnumerable<Clip> clips) =>
        clips.Where(c => !c.IsCameraProxy).Sum(c => c.Bytes * 8.0 / (SpaceEstimator.SourceMbps(c) * 1e6));

    /// The whole job before it starts, or null when a part is not known.
    public TimeSpan? Estimate(long bytes, IReadOnlyList<Clip> clips, bool verifyOn, bool analyze, ProxySettings proxy)
    {
        if (copy is not double c) return null;
        double seconds = bytes / c;
        if (verifyOn)
        {
            if (verify is not double v) return null;
            seconds += bytes / v;
        }
        if (PostKey(analyze, proxy) is string key && clips.Count > 0)
        {
            if (!post.TryGetValue(key, out var perSecond)) return null;
            seconds += ClipSeconds(clips) * perSecond;
        }
        return TimeSpan.FromSeconds(seconds);
    }

    /// The rest of a running job while it copies or reads back: the phase's own ETA, then the phases after it. Null when
    /// one of them is not known (the card then says only the phase's ETA).
    public double? Remaining(JobPhase phase, double? phaseEta, long bytes, IReadOnlyList<Clip> clips, bool verifyOn, bool analyze, ProxySettings proxy)
    {
        if (phaseEta is not double eta || phase is not (JobPhase.Copying or JobPhase.Verifying)) return null;
        double rest = eta;
        if (phase == JobPhase.Copying && verifyOn)
        {
            if (verify is not double v) return null;
            rest += bytes / v;
        }
        if (PostKey(analyze, proxy) is string key && clips.Count > 0)
        {
            if (!post.TryGetValue(key, out var perSecond)) return null;
            rest += ClipSeconds(clips) * perSecond;
        }
        return rest;
    }

    /// The right side of the start bar: "estimate ~8:00 incl. proxies", "estimate ~2:10", or null (only the size shows).
    public static string? StartText(TimeSpan? estimate, bool proxies) =>
        estimate is TimeSpan t ? $"estimate ~{JobProgress.EtaText(t.TotalSeconds)}{(proxies ? " incl. proxies" : "")}" : null;
}
