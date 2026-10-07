using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;

namespace Offload.Cli;

/// `offload append <source> [--into <card folder>]…` (spec 2.2): only the card's new files into the card folders of
/// the earlier backup, as a new ASC MHL generation. Without --into the folders come from the offload history.
static class AppendCommand
{
    public static Command Create()
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        var into = new Option<string[]>("--into") { Description = "Card folder of the earlier backup (<root>\\<date>\\<card>); repeat for each destination. Default: from the offload history.", DefaultValueFactory = _ => [] };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
        var buffers = new Option<int>("--buffers") { Description = "Blocks in flight.", DefaultValueFactory = _ => 16 };
        var noVerify = new Option<bool>("--no-verify") { Description = "Skip read-back verification (not recommended)." };
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy (the real run plans again under the folder lock, so it may differ)." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (needs --into)." };
        var project = CliProject.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("append", "Copy only the new files of a card into its earlier backup (same card folders, next ASC MHL generation).");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { into, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, project }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
            JobConfig config;
            ProjectSettings? profile;
            try
            {
                // The destinations and checksums come from the earlier backup; the project gives verify and stage 2.
                profile = CliProject.Load(r.GetValue(project));
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), [])
                {
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = CliProject.Given(r, profile, noVerify) ? !r.GetValue(noVerify) : profile!.Verify,
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                    // Ruling M4: the report records the earlier backup's project (the engine reads it from that report);
                    // this is only the fallback — --project gives the settings, not the card's project.
                    Project = profile?.Name,
                };
                stageTwo.Apply(r, config, profile);
                var folders = r.GetValue(into)!.Select(CliPath.Full).ToList();
                if (folders.Count == 0)
                {
                    var known = config.RecordHistory ? CopyCommand.Inspect(config.Source, withHistory: true)?.History : null;
                    if (known == null) throw new ArgumentException(Msg.CardNotInHistory);
                    folders = known.Entry.Destinations.Select(d => d.CardPath).ToList();
                    config.Project = known.Entry.Project;   // the fallback is the history entry's project (M4)
                }
                config.AppendInto = folders;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
            if (profile != null) Console.WriteLine(Msg.CliProject(profile.Name));
            return CopyCommand.RunJob(config, r.GetValue(dryRun));
        });
        return cmd;
    }
}
