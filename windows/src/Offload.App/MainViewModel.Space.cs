using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Space;

namespace Offload.App;

/// Spec 2.5 in the window: red "not enough space" blocks the start, orange "margin under 5%" only warns. Memory only —
/// sizes from the card inspections, volumes and free space from the destination probes; evaluated 3x/s. The probes
/// themselves run off the UI thread: at Start and when a copy finishes (ruling I4), so the free space is never older
/// than the bytes the running jobs have already written.
public sealed partial class MainViewModel
{
    string? spaceProblem, spaceWarning;
    // The proxy folder's volume and free space, read off the UI thread with its existence check.
    SpaceTarget? folderTarget;
    long? folderFree;
    // What the running jobs had written when the destinations were last probed (ruling I4).
    readonly FreeSpaceBaseline<JobItemViewModel> spaceBaseline = new();

    public string? SpaceProblem => spaceProblem;
    public string? SpaceWarning => spaceWarning;
    /// "Checking cards…" while Start waits for the inspections or the free space; null otherwise.
    public string? StartStatus => startStatus;

    void SetStartStatus(string? text)
    {
        if (startStatus == text) return;
        startStatus = text;
        Raise(nameof(StartStatus));
    }

    List<SpaceTarget> SpaceTargets() => Destinations.Select(d => d.SpaceTarget).OfType<SpaceTarget>().ToList();

    /// The used space of a scanned card volume: the upper estimate while its inspection has not finished.
    long? UsedOnVolume(string path) =>
        volumes.FirstOrDefault(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase)) is { } v ? v.Total - v.Free : null;

    /// What starting `path` now would need on each destination volume (also handed to its job card).
    List<VolumeDemand> CardSpace(string path, IReadOnlyList<SpaceTarget> targets) =>
        AppendChosen(path) && Insight(path)?.History is { } known
            ? AppendSpace(known, targets)
            : SpaceCheck.ForCard(Insight(path), UsedOnVolume(path), Settings.Proxy, resolvedTools, targets, folderTarget);

    /// An append renders proxies as the project of its earlier backup says (ruling I2). The proxy folder's free space is
    /// known only for the folder shown now: another project's folder is not counted.
    List<VolumeDemand> AppendSpace(HistoryMatch known, IReadOnlyList<SpaceTarget> targets)
    {
        var proxy = AppendRules.ProfileFor(AllSettings, known.Entry.Project).Profile.Proxy;
        var folder = folderTarget != null && string.Equals(ProxyPreflight.ExtraFolderInUse(proxy), folderTarget.Root, StringComparison.OrdinalIgnoreCase)
            ? folderTarget : null;
        return SpaceCheck.ForAppend(known, proxy, resolvedTools, targets, folder);
    }

    /// Reads the free space of every destination and of the proxy folder again, off the UI thread, at most `bound`;
    /// when every one answered, the written bytes of the running jobs from before the reads become the baseline their
    /// remaining need is counted against. A disk that does not answer keeps its last figure and the old baseline.
    async Task ProbeFreeSpaceAsync(TimeSpan bound)
    {
        if (closing) return;
        var snapshot = Jobs.OfType<JobItemViewModel>().ToDictionary(j => j, j => j.WrittenNow());   // before the reads: never too high
        var probes = Destinations.ToList().Select(d => d.ProbeFreshAsync()).ToList();
        if (ProxyPreflight.ExtraFolderInUse(Settings.Proxy) is string folder)
            probes.Add(Task.Run(() => ProbeFolder(folder)).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) ApplyFolderProbe(folder, t.Result);
                else AppLog.Write($"proxy folder free space: {t.Exception?.GetBaseException().Message}");
            }, TaskScheduler.FromCurrentSynchronizationContext()));
        try
        {
            if (await StartWait.AllWithinAsync(probes, bound, lifetime.Token)) spaceBaseline.Commit(snapshot);
            else AppLog.Write($"free space: not every destination answered in {bound.TotalSeconds:0} s");
        }
        catch (OperationCanceledException)
        {
            return;   // the window is closing
        }
        RefreshSpace();
    }

    // The health check re-reads the free space while a space problem shows (ruling S3): one read at a time.
    readonly FreeSpaceReprobe spaceReprobe = new();

    void ReprobeStaleSpaceProblem()
    {
        if (!spaceReprobe.TryBegin(SpaceProblem != null)) return;
        ProbeFreeSpaceAsync(TimeSpan.FromSeconds(30)).ContinueWith(_ => spaceReprobe.End(), TaskScheduler.Default);
    }

    /// Recomputes both texts; raises them (and CanStart) only when one changed.
    void RefreshSpace()
    {
        var targets = SpaceTargets();
        var selectedDemands = SelectedSources().SelectMany(p => CardSpace(p, targets)).ToList();
        var demands = selectedDemands.ToList();
        foreach (var j in Jobs.OfType<JobItemViewModel>()) demands.AddRange(j.SpaceDemands(spaceBaseline.WrittenAtProbe(j)));
        var free = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Destinations)
            if (d.SpaceTarget is { } t && d.FreeBytes is long f) free.TryAdd(t.Volume, f);
        if (folderTarget is { } ft && folderFree is long ff) free.TryAdd(ft.Volume, ff);
        var problems = SpacePlanner.Check(demands, free);
        // Every destination tile: its bar (what the ticked cards need there) and the check's sentence about it.
        foreach (var d in Destinations)
        {
            if (d.SpaceTarget is not { } t) { d.SetSpace(0, null, null); continue; }
            var severity = DestinationTile.Severity(problems, t.Label);
            var note = problems.Where(p => p.Label.Split(", ").Contains(t.Label, StringComparer.OrdinalIgnoreCase)).OrderByDescending(p => p.Severity).FirstOrDefault()?.Text;
            d.SetSpace(DestinationTile.Need(selectedDemands, t.Volume), severity, note);
        }
        var problem = problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Blocking)?.Text;
        var warning = problem == null ? problems.FirstOrDefault(p => p.Severity == SpaceSeverity.Warning)?.Text : null;
        if (problem == spaceProblem && warning == spaceWarning) return;
        spaceProblem = problem;
        spaceWarning = warning;
        RaiseMany(nameof(SpaceProblem), nameof(SpaceWarning), nameof(CanStart));
    }
}
