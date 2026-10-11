using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.History;
using Offload.Core.Outputs;

namespace Offload.Cli;

/// `arcloader summary-report <card folders | date folders | history ids…> [-o <folder>]` (task 2026-10-09): one summary
/// report (HTML + PDF) of the backups, saved into `<ArcLoader reports>\summary\` and, with -o, into that folder too (never
/// over a file). Reads the backups only. Exit 0; 1 when nothing could be made or the copy into -o failed.
static class SummaryReportCommand
{
    public static Command Create()
    {
        var backups = new Argument<string[]>("backups")
        {
            Description = "Card folders (on a destination, or their copy in Documents\\ArcLoader reports), date folders (every card in them), or history ids.",
            Arity = ArgumentArity.OneOrMore,
        };
        var output = new Option<string?>("--output", "-o") { Description = "Also save the summary into this folder (a suffix _2, _3 … instead of overwriting)." };
        var cmd = new Command("summary-report", "One summary report (HTML + PDF) from several backups: shooting days, clips, files, size, duration, first and last slate, cameras, verification, where each card is backed up. Always saved into <ArcLoader reports>\\summary.");
        cmd.Arguments.Add(backups);
        cmd.Options.Add(output);
        cmd.SetAction(r =>
        {
            try
            {
                var settings = SettingsStore.Load(CliProject.SettingsPath());
                var args = r.GetValue(backups) ?? [];
                // History only when something is not a folder: an id (reading it never writes it).
                IReadOnlyList<HistoryEntry> history = args.All(Directory.Exists) ? [] : HistoryStore.Default(m => Console.Error.WriteLine(m)).Load();
                var inputs = SummaryReport.Inputs(args, history);
                var model = SummaryReport.Load(inputs, SummaryReport.LocalRoots(settings));
                var extra = r.GetValue(output) is string o ? CliPath.Full(o) : null;
                var saved = SummaryReport.Save(model, SummaryReport.LocalRoot(settings.Outputs), extra, m => Console.Error.WriteLine(m));
                Console.WriteLine(Msg.SummaryWritten(saved.Html));
                if (saved.Pdf != null) Console.WriteLine(Msg.SummaryWritten(saved.Pdf));
                if (saved.ExtraHtml != null) Console.WriteLine(Msg.SummaryExtraWritten(saved.ExtraHtml));
                if (saved.ExtraError == null) return 0;
                Console.Error.WriteLine(Msg.CliError + Msg.SummaryExtraFailed(saved.ExtraError));
                return 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
        });
        return cmd;
    }
}
