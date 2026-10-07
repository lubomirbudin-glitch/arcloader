using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Notify;

namespace Offload.Cli;

/// `--ntfy <url>` (spec 4.3): the job's end goes to an ntfy topic ("https://ntfy.sh/muj-tajny-kanal" = server + topic).
/// Without it a --project with ntfy on sends there. The CLI shows no Windows notifications.
static class CliNtfy
{
    public static Option<string?> Create() =>
        new("--ntfy") { Description = "Send the result to a phone through ntfy: the server and topic as one URL, e.g. https://ntfy.sh/my-secret-topic (only the card name and the result are sent)." };

    /// The job's notify settings: --ntfy wins, else the project's, else none. ArgumentException (Czech) for a bad URL.
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
}
