using System.CommandLine;
using Offload.Core;
using Offload.Core.Outputs;

namespace Offload.Cli;

/// `offload day-report <day folder>` (spec 3.2): makes the day report again and prints where it is. Exit 0 / 1.
static class DayReportCommand
{
    public static Command Create()
    {
        var folder = new Argument<string>("folder") { Description = "Date folder on a destination (<root>\\<date>), or a card folder in it." };
        var noClipList = new Option<bool>("--no-clip-list") { Description = "No day clip list CSV (_REPORT\\<date>_clips.csv)." };
        var ale = new Option<bool>("--ale") { Description = "Also write the day clip list for Avid (_REPORT\\<date>_clips.ale)." };
        var cmd = new Command("day-report", "Write the day report (HTML, PDF, day clip lists) into <date>\\_REPORT.");
        cmd.Arguments.Add(folder);
        cmd.Options.Add(noClipList);
        cmd.Options.Add(ale);
        cmd.SetAction(r =>
        {
            try
            {
                var day = DayReportService.DayFolderOf(CliPath.Full(r.GetValue(folder)!));
                var outputs = new OutputSettings { ClipListCsv = !r.GetValue(noClipList), ClipListAle = r.GetValue(ale) };
                var html = DayReportService.RegenerateDay(day, outputs, m => Console.Error.WriteLine(m));
                Console.WriteLine(Msg.DayReportWritten(html));
                return 0;
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
