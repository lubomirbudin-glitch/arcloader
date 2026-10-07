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
        };
    }

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
        };
    }
}
