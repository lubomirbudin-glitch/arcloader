using Offload.Core.History;

namespace Offload.Core.Desktop;

/// The jobs a Start in the app makes from the settings (spec 4.1, rulings I2 and M15). Every section is the job's own
/// copy: a later edit or a project switch never changes a queued or running job.
public static class JobConfigs
{
    /// A full copy with the profile shown now, recorded under the active project (null = "Bez projektu").
    public static JobConfig Full(AppSettings s, string source, string? cardName, string date)
    {
        var p = s.Active;
        return new JobConfig(source, p.DestRoots.ToList())
        {
            CardName = cardName,
            Date = date,
            Hashes = HashKinds.FromSetting(p.Hash),   // an append (below) keeps the earlier backup's (spec 3.5)
            Verify = p.Verify,
            AnalyzeClips = p.AnalyzeClips,
            Proxy = SettingsStore.Clone(p.Proxy),
            Tools = SettingsStore.Clone(s.Tools),
            Outputs = SettingsStore.Clone(p.Outputs),
            Project = Projects.ActiveName(s),
            Notify = SettingsStore.Clone(p.Notify),
        };
    }

    /// "Zkopírovat znovu" (UI návrh 4) after `finished` ended with failed files: see the overload — with the identity of
    /// the card it copied (ruling I1).
    public static JobConfig Repair(OffloadJob finished) =>
        Repair(finished.Config, finished.Plan ?? throw new InvalidOperationException("The job has no plan: nothing to repair."), finished.SourceIdentity);

    /// "Zkopírovat znovu" (UI návrh 4) after `original` ended with failed files: only those, only where they failed, into
    /// the same card folders — with the job's own copy of the settings (its project, outputs and notifications; a switch
    /// of project since changes nothing). Always verified; no clip analysis or proxies (the clips were analysed with the
    /// backup). `cardIdentity`: the card that was copied (`OffloadJob.SourceIdentity`) — another card is refused.
    public static JobConfig Repair(JobConfig original, OffloadPlan plan, string? cardIdentity) =>
        new(original.Source, plan.DestRoots.ToList())
        {
            AllowSameDevice = true,   // the destinations are fixed by the backup
            RepairInto = plan.CardPaths,
            RepairCardIdentity = cardIdentity,
            CardName = plan.CardName,
            Date = plan.Date,
            Verify = true,
            ChunkMB = original.ChunkMB,
            Buffers = original.Buffers,
            Hashes = original.Hashes,   // the engine uses the backup's (spec 3.5)
            AnalyzeClips = false,
            Proxy = new ProxySettings(),
            Tools = SettingsStore.Clone(original.Tools),
            Outputs = SettingsStore.Clone(original.Outputs),
            Project = plan.Project ?? original.Project,
            Notify = SettingsStore.Clone(original.Notify),
            RecordHistory = original.RecordHistory,
            HistoryFolder = original.HistoryFolder,
        };

    /// Spec 2.2: only the new files, into the card folders of the earlier backup — with the profile of the project that
    /// backup was made in (AppendRules.ProfileFor), recording that project even when it has been deleted since.
    public static JobConfig Append(AppSettings s, string source, HistoryEntry entry)
    {
        var p = AppendRules.ProfileFor(s, entry.Project).Profile;
        return new JobConfig(source, entry.Destinations.Select(d => d.Root).ToList())
        {
            AllowSameDevice = true,   // the destinations are fixed by the earlier backup (it may have been made with --allow-same-device)
            AppendInto = entry.Destinations.Select(d => d.CardPath).ToList(),
            Verify = p.Verify,
            AnalyzeClips = p.AnalyzeClips,
            Proxy = SettingsStore.Clone(p.Proxy),
            Tools = SettingsStore.Clone(s.Tools),
            Outputs = SettingsStore.Clone(p.Outputs),
            Project = entry.Project,   // the engine prefers the earlier report's (M4); this is the fallback
            Notify = SettingsStore.Clone(p.Notify),   // the entry's project's topic, or Bez projektu (I2, P1, P2) — never the active one
        };
    }
}
