using System.CommandLine;
using Offload.Core;
using Offload.Core.Verification;

namespace Offload.Cli;

static class VerifyCommand
{
    public static Command Create()
    {
        var folder = new Argument<string>("folder") { Description = "Card folder (<root>/<date>/<card>) or a whole date folder (every card in it)." };
        var report = new Option<bool>("--report") { Description = "Write the verification report (HTML, PDF, JSON) into each card's _REPORT folder." };
        var cmd = new Command("verify", "Re-verify card folders against their ASC MHL (all generations), or the _REPORT hash list.");
        cmd.Arguments.Add(folder);
        cmd.Options.Add(report);
        cmd.SetAction(r =>
        {
            string full;
            try
            {
                full = CliPath.Full(r.GetValue(folder)!);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                return 1;
            }
            return Run(full, r.GetValue(report));
        });
        return cmd;
    }

    /// Exit 0 when every card verified; 1 when any had a problem (or there was nothing to verify). The problem lines stay
    /// machine-readable (MISMATCH, MISSING, ERROR, EXTRA, FAILED-AT-COPY, CHAIN) for scripts; the summary is Czech.
    static int Run(string folder, bool writeReport)
    {
        var cards = VerifyTargets.Resolve(folder);
        if (cards.Count == 0)
        {
            Console.Error.WriteLine(Msg.CliError + Msg.NoHashRecord(folder));
            return 1;
        }
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCtrlC = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCtrlC;
        try
        {
            bool allOk = true;
            foreach (var card in cards)
            {
                if (cards.Count > 1) Console.WriteLine($"\n{card}");
                allOk &= VerifyOne(card, writeReport, cancel.Token);
                if (cancel.IsCancellationRequested) return 1;
            }
            return allOk ? 0 : 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCtrlC;
        }
    }

    static bool VerifyOne(string card, bool writeReport, CancellationToken ct)
    {
        var last = DateTime.MinValue;
        VerifyResult r;
        try
        {
            r = BackupVerifier.Verify(card, p =>
            {
                if (DateTime.UtcNow - last < TimeSpan.FromMilliseconds(500)) return;
                last = DateTime.UtcNow;
                ConsoleLine.Write($"{p.FileIndex + 1}/{p.FileCount}  {Format.Bytes(p.Bytes)}  {p.CurrentFile}");
            }, ct);
        }
        catch (Exception e)
        {
            ConsoleLine.Clear();
            Console.Error.WriteLine(Msg.CliError + e.Message);
            return false;
        }
        ConsoleLine.Clear();
        foreach (var m in r.Changed) Console.WriteLine($"MISMATCH  {m}");
        foreach (var m in r.Missing) Console.WriteLine($"MISSING   {m}");
        foreach (var m in r.Unreadable) Console.WriteLine($"ERROR     {m.Path}: {m.Error}");
        foreach (var m in r.Extra) Console.WriteLine($"EXTRA     {m}");
        foreach (var m in r.FailedAtCopy) Console.WriteLine($"FAILED-AT-COPY  {m}");
        foreach (var m in r.ChainProblems) Console.WriteLine($"CHAIN     {m}");
        Console.WriteLine(VerifyTexts.CliSummary(r));
        if (writeReport && !r.Cancelled)
        {
            try
            {
                var html = VerifyReport.Write(r, m => Console.Error.WriteLine(m));
                Console.WriteLine($"Report: {html}");
                if (!r.ReportInsideCard) Console.WriteLine(VerifyTexts.ReportOutsideNote(html));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + Msg.ReportNotWritten(e.Message));
            }
        }
        return r.Success;
    }
}
