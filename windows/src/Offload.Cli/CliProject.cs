using System.CommandLine;
using Offload.Core.Desktop;

namespace Offload.Cli;

/// `--project <název>` (spec 4.1): a project saved in the app (settings.json in %APPDATA%\Offload, or
/// OFFLOAD_SETTINGS_DIR) gives the defaults; every option written on the command line wins over it.
static class CliProject
{
    /// Tests: the settings.json the CLI reads (never the real %APPDATA% in a test).
    internal static Func<string> SettingsPath { get; set; } = () => SettingsStore.DefaultPath;

    public static Option<string?> Create() =>
        new("--project") { Description = "Use a project saved in the app (settings.json) for the defaults: destinations, verify, hash, outputs, proxy, notifications. Options given here win." };

    /// The project, or null without --project. "Bez projektu" (any case) is the top-level profile, recorded as no
    /// project. An unknown name: ArgumentException with the Czech message that lists the projects there are.
    public static CliProfile? Load(string? name)
    {
        if (name == null) return null;
        var settings = SettingsStore.Load(SettingsPath());
        if (string.Equals(name.Trim(), Projects.NoProject, StringComparison.OrdinalIgnoreCase)) return new CliProfile(settings, null);
        var project = Projects.Find(settings, name) ?? throw new ArgumentException(Projects.Unknown(name, settings));
        return new CliProfile(project, project.Name);
    }

    /// The option was written on the command line (not just its default). Without a project every option counts as
    /// given, so the CLI behaves exactly as it does without --project.
    public static bool Given(ParseResult r, SettingsProfile? project, Option option) =>
        project == null || r.GetResult(option) is { Implicit: false };
}

/// The profile --project chose: `Project` is what the job records (null for "Bez projektu").
sealed record CliProfile(SettingsProfile Settings, string? Project)
{
    public string Name => Project ?? Projects.NoProject;
}
