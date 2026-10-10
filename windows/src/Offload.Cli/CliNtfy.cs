using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Cli;

/// `--ntfy <url>` (spec 4.3): the job's end goes to an ntfy topic ("https://ntfy.sh/my-secret-topic" = server + topic).
/// Without it a --project with ntfy on sends there. The CLI shows no Windows notifications.
static class CliNtfy
{
    public static Option<string?> Create() =>
        new("--ntfy") { Description = "Send the result to a phone through ntfy: the server and topic as one URL, e.g. https://ntfy.sh/my-secret-topic (only the card name and the result are sent)." };

    /// The job's notify settings: --ntfy wins, else the project's, else none. ArgumentException for a bad URL.
    public static NotifySettings Settings(ParseResult r, Option<string?> ntfy, SettingsProfile? project)
    {
        if (r.GetValue(ntfy) is string url)
        {
            var (server, topic) = NtfyClient.ParseUrl(url) ?? throw new ArgumentException(Msg.BadNtfyUrl(url));
            return new NotifySettings { Windows = false, Ntfy = new NtfySettings { Enabled = true, Server = server, Topic = topic } };
        }
        var s = project != null ? SettingsStore.Clone(project.Notify) : new NotifySettings { Ntfy = new NtfySettings { Enabled = false } };
        s.Windows = false;
        return s;
    }

    /// An append's notify settings (final fix 1): an explicit --ntfy wins; else those of the EARLIER backup's project
    /// (AppendRules.ProfileFor: none or gone = "No project"), never another project's topic given by --project.
    /// `note` is the line to print when the earlier project's settings are used.
    public static NotifySettings ForAppend(ParseResult r, Option<string?> ntfy, AppSettings all, string? earlierProject, out string? note)
    {
        note = null;
        if (r.GetValue(ntfy) != null) return Settings(r, ntfy, null);
        var (profile, gone) = AppendRules.ProfileFor(all, earlierProject);
        if (earlierProject != null) note = gone ? AppendRules.GoneNote(earlierProject) : Msg.CliAppendNotify(earlierProject);
        return Settings(r, ntfy, profile);
    }
}
