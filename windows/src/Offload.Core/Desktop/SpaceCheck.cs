using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.Core.Desktop;

/// A destination as the space check sees it: its volume (from the probe made off the UI thread) and how it is named.
public sealed record SpaceTarget(string Root, string Volume, string Label);

/// The GUI's space check before a start (spec 2.5), from memory only: card sizes from the inspections, free space and
/// volumes from the destination probes. The engine checks again when each job starts — that is the last word.
public static class SpaceCheck
{
    /// One card about to start: its inspected size, or — not inspected yet — the used space of its volume as an upper
    /// estimate (`usedOnVolume`; null = unknown, then the card adds nothing and never blocks). `folder`: the proxy folder.
    public static List<VolumeDemand> ForCard(CardInsight? insight, long? usedOnVolume, ProxySettings proxy, ExternalTools tools,
        IReadOnlyList<SpaceTarget> targets, SpaceTarget? folder)
    {
        long bytes;
        int files;
        IReadOnlyList<Clip> clips;
        if (insight is { Error: null }) (bytes, files, clips) = (insight.TotalBytes, insight.FileCount, insight.Clips);
        else if (usedOnVolume is long used) (bytes, files, clips) = (used, 0, []);
        else return [];
        return Demands(SpaceEstimator.CardBytes(bytes, files), SpaceEstimator.Extra(clips, proxy, tools), targets, folder);
    }

    /// An append (spec 2.2): only the new files, onto the destinations of the earlier backup that are in `targets`
    /// (others the engine checks when the job starts).
    public static List<VolumeDemand> ForAppend(HistoryMatch match, ProxySettings proxy, ExternalTools tools,
        IReadOnlyList<SpaceTarget> targets, SpaceTarget? folder)
    {
        var mine = match.Entry.Destinations
            .Select(d => targets.FirstOrDefault(t => string.Equals(t.Root.TrimEnd('\\'), d.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            .OfType<SpaceTarget>()
            .ToList();
        var clips = ClipDetector.Detect(match.New);
        return Demands(SpaceEstimator.CardBytes(match.NewBytes, match.New.Count), SpaceEstimator.Extra(clips, proxy, tools), mine, folder);
    }

    static List<VolumeDemand> Demands(long card, SpaceExtra extra, IReadOnlyList<SpaceTarget> targets, SpaceTarget? folder)
    {
        var demands = targets.Select((t, i) => new VolumeDemand(t.Volume, t.Label, extra.ForDestination(i, card))).ToList();
        if (folder != null && extra.ProxyFolder != null && extra.Proxy > 0)
            demands.Add(new VolumeDemand(folder.Volume, folder.Label, new SpaceNeed(0, extra.Proxy, 0)));
        return demands;
    }

    /// What a job in the queue still needs: all of it while waiting, the rest of the card while copying (`written` on
    /// that destination), proxies and report during stage 2, nothing once finished or cancelled.
    public static SpaceNeed Remaining(SpaceNeed need, JobPhase phase, long written) => phase switch
    {
        JobPhase.Waiting => need,
        JobPhase.Copying or JobPhase.Flushing or JobPhase.Verifying or JobPhase.Reporting => need with { Card = Math.Max(0, need.Card - written) },
        JobPhase.Analyzing or JobPhase.Proxies => need with { Card = 0 },
        _ => SpaceNeed.None,
    };
}
