using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;
using Offload.Core.History;
using Offload.Core.Notify;

namespace Offload.Cli;

static class CopyCommand
{
    public static Command Create()
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        // Not Required: the parser's "Option '--dest' is required." has no Czech translation — the engine says
        // "Není zadaný žádný cíl." instead.
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", DefaultValueFactory = _ => [] };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label); auto = the reel in the clip names (A001C003… → A001), else the volume label." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
        var buffers = new Option<int>("--buffers") { Description = "Blocks in flight.", DefaultValueFactory = _ => 16 };
        var noVerify = new Option<bool>("--no-verify") { Description = "Skip read-back verification (not recommended)." };
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };
        var noHistory = new Option<bool>("--no-history") { Description = "Do not read or write the offload history (%APPDATA%\\Offload\\history.json)." };
        var hash = new Option<string>("--hash") { Description = "Checksums: xxh64 (default) or both (xxh64 + MD5; MD5 is slower on fast cards).", DefaultValueFactory = _ => "xxh64" };
        var project = CliProject.Create();
        var ntfy = CliNtfy.Create();
        var stageTwo = new StageTwoOptions();

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noHistory, hash, project, ntfy }) cmd.Options.Add(o);
        stageTwo.AddTo(cmd);
        cmd.SetAction(r =>
        {
            JobConfig? config = null;
            CliProfile? picked;
            SettingsProfile? profile;
            try
            {
                // Spec 4.1: the project's values are the defaults, what is written here wins (-d replaces its destinations).
                picked = CliProject.Load(r.GetValue(project));
                profile = picked?.Settings;
                bool Given(Option o) => CliProject.Given(r, profile, o);
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), (Given(dest) ? r.GetValue(dest)! : [.. profile!.DestRoots]).Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name) is string n && !n.Equals("auto", StringComparison.OrdinalIgnoreCase) ? n : null,
                    Date = r.GetValue(date),
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = Given(noVerify) ? !r.GetValue(noVerify) : profile!.Verify,
                    AllowSameDevice = r.GetValue(allowSame),
                    RecordHistory = !r.GetValue(noHistory),
                    Hashes = !Given(hash) ? HashKinds.FromSetting(profile!.Hash)
                        : HashKinds.TryParseWire(r.GetValue(hash), out var kinds) ? kinds : throw new ArgumentException(Msg.UnknownHash(r.GetValue(hash))),
                    Project = picked?.Project,
                    Notify = CliNtfy.Settings(r, ntfy, profile),
                };
                stageTwo.Apply(r, config, profile);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(Msg.CliError + e.Message);
                if (config != null && !r.GetValue(dryRun)) Ntfy(config, null, new JobStatus { Phase = JobPhase.Failed, Error = e.Message }, null);   // card name only
                return 1;
            }
            if (picked != null) Console.WriteLine(Msg.CliProject(picked.Name));
            // A project that proposes names from the clips (naming.suggestFromClips) acts as --name auto unless --name is given.
            bool autoName = r.GetValue(name) is string a ? a.Equals("auto", StringComparison.OrdinalIgnoreCase) : profile?.Naming.SuggestFromClips == true;
            var insight = config.RecordHistory || autoName ? Inspect(config.Source, withHistory: config.RecordHistory) : null;
            if (insight?.History is { } known)
                Console.WriteLine(HistoryText.Line(known, fullPaths: true).Text);   // no question: a script has nobody to ask
            if (autoName)
            {
                // --name auto: the reel from the clip names, otherwise the volume label (as without --name).
                config.CardName = insight?.Name?.Name;
                if (insight?.NameNote is string note) Console.WriteLine(note);
                Console.WriteLine(config.CardName is { } chosen ? Msg.CardNameAuto(chosen) : Msg.CardNameAutoNone);
            }
            return RunJob(config, r.GetValue(dryRun));
        });
        return cmd;
    }

    /// Tests: how the job is made (the engine options of a test differ: card and destinations share one volume).
    internal static Func<JobConfig, OffloadJob> JobFactory { get; set; } = c => new OffloadJob(c);

    /// Plans, prints the plan and runs the job (copy or append). Exit 0 = copied (and verified unless --no-verify) or,
    /// for an append, nothing new; 1 = anything else (a refusal of the engine prints as "Chyba: " + its Czech text).
    ///
    /// An append's printed plan is labelled "plán (může se ještě upřesnit)": the run plans again under the card-folder
    /// lock, so what runs may differ (another append finished, a disk went away). The plan is not repeated right before
    /// the run — the run's own planning is the only one that counts; the report and the status lines tell what happened.
    internal static int RunJob(JobConfig config, bool dryRun)
    {
        // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all.
        if (LutOption.PlanLine(config.Proxy, config.AnalyzeClips) is string lutLine) Console.WriteLine(lutLine);
        var job = JobFactory(config);
        OffloadPlan? plan = null;
        try
        {
            plan = job.Prepare();
        }
        catch (OffloadError e) when (e.Code == PreflightCode.NothingNew)
        {
            if (dryRun)
            {
                Console.WriteLine(e.Message);
                return 0;
            }
            // Not an error — but the run still refreshes history and heals the hash list, so it goes on (and plans again).
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(Msg.CliError + e.Message);
            if (!dryRun) Ntfy(config, null, new JobStatus { Phase = JobPhase.Failed, Error = e.Message }, null);   // a refusal before the copy too
            return 1;
        }
        List<RoleLink>? chainLinks = null;
        if (plan != null)
        {
            Console.WriteLine(PlanLine(plan));
            foreach (var p in plan.CardPaths) Console.WriteLine($"  → {p}");
            if (plan.UnverifiedEarlier > 0) Console.WriteLine("Varování: " + Msg.UnverifiedEarlier(plan.UnverifiedEarlier));
            foreach (var warning in plan.SpaceWarnings) Console.WriteLine("Varování: " + warning);   // R11: never blocks
            if (dryRun)
                foreach (var line in plan.Space) Console.WriteLine(Msg.SpaceLine(line.Label, line.Need, line.Free));
            if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string extra)
                Console.WriteLine($"  proxy also → {Layout.CardPath(extra, plan.Date, plan.CardName)}");
            if (config.AnalyzeClips || config.Proxy.Enabled)
                Console.WriteLine($"  ffmpeg: {config.Tools.Resolved().Ffmpeg ?? "NOT FOUND"}");
            // Connection chain: informational only, must never fail a copy.
            try
            {
                var links = new List<RoleLink> { new("Karta", DriveLinks.Resolve(plan.Source)) };
                links.AddRange(plan.DestRoots.Select((d, i) => new RoleLink($"Cíl {i + 1}", DriveLinks.Resolve(d))));
                var connectors = UsbScanner.Scan().Connectors;
                // Status-line names first ("card", "D1"), so "brzdí: D1" there reads against this list.
                var lines = new List<string> { "  Připojení:" };
                lines.AddRange(links.Select((l, i) => $"    {ProgressDisplay.ShortName(i)} = {l.Role}: {l.Link.Describe(connectors)}"));
                lines.AddRange(Advisor.Advise(links, connectors)
                    .Where(a => a.Kind == AdviceKind.Warning).Select(a => $"  ! {a.Text}"));
                foreach (var l in lines) Console.WriteLine(l);
                chainLinks = links;
            }
            catch (Exception)
            {
                // Hardware info unavailable: just omit it.
            }
        }
        if (dryRun) return 0;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("\n" + Msg.Cancelling);
            job.Cancel();
        };

        OffloadReport? report;
        using (new ProgressDisplay(job, chainLinks)) report = job.Run(message => Console.Error.WriteLine(message));

        var status = job.Status();
        plan = job.Plan ?? plan;
        if (report != null && plan != null)
        {
            Console.WriteLine();
            Console.Write(report.Summary);
            if (report.Clips.Count > 0)
            {
                var done = report.Clips.Count(c => c.ProxyStatus == ProxyStatus.Done);
                Console.WriteLine($"Clips: {report.Clips.Count}" + (config.Proxy.Enabled ? $"  proxies OK {done}, failed {status.ProxiesFailed}" : ""));
                foreach (var c in report.Clips.Where(c => c.ProxyStatus is ProxyStatus.Failed or ProxyStatus.Unsupported))
                    Console.WriteLine($"  {c.Clip.Name}: {c.ProxyStatus.ToString().ToLowerInvariant()} — {c.ProxyNote}");
            }
            Console.WriteLine($"Report: {QueueRules.ReportPath(plan.CardPaths[0], plan.CardName, plan.Generation)}");
        }
        foreach (var day in job.DayReports) Console.WriteLine(Msg.DayReportWritten(day));
        if (status.Note != null) Console.WriteLine(status.Note);
        if (status.Warning != null) Console.WriteLine("Varování: " + status.Warning);
        if (status.Error != null) Console.WriteLine(Msg.CliError + status.Error);
        Ntfy(config, plan, status, report);
        // "Nic nového" ends Done without a report: that is success (exit 0).
        return status.Phase == JobPhase.Done && (report?.Success == true || (report == null && status.Note != null)) ? 0 : 1;
    }

    /// Spec 4.3: --ntfy (or the project's ntfy) once the job has ended — at most 10 s; a failure is only a line on stderr.
    internal static void Ntfy(JobConfig config, OffloadPlan? plan, JobStatus status, OffloadReport? report)
    {
        // Never the source path itself (spec 5.6): the plan's name, the --name, else the name the engine would give.
        string card;
        try { card = plan?.CardName ?? config.CardName ?? Layout.DefaultCardName(config.Source); }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException) { card = ""; }
        if (string.IsNullOrWhiteSpace(card)) card = "karta";
        // The wait itself is bounded (the proxy lookup may ignore the client's token).
        var send = Task.Run(() => NtfyClient.SendOutcomeAsync(config.Notify.Ntfy, JobOutcomes.Final(card, status, report), m => Console.Error.WriteLine(m)));
        if (!send.Wait(NtfyClient.DefaultTimeout + TimeSpan.FromSeconds(1))) Console.Error.WriteLine("ntfy: nečekám déle");
    }

    /// "Card A001: 3 files, 1 GB" for a full copy; an append (Czech, with plural forms): "Doplnění karty A001
    /// (generace 2), plán (může se ještě upřesnit): 2 soubory · 50 KB".
    internal static string PlanLine(OffloadPlan plan) => plan.Mode == PlanMode.Append
        ? $"Doplnění karty {plan.CardName} (generace {plan.Generation}), plán (může se ještě upřesnit): {Texts.Files(plan.Scan.Files.Count, plan.Scan.TotalBytes)}"
        : $"Card {plan.CardName}: {plan.Scan.Files.Count} files, {Format.Bytes(plan.Scan.TotalBytes)}";

    /// The card against history (spec 2.1 CLI) and its clip names (2.3). Never fails the copy: any problem only means
    /// no history line and no suggested name.
    internal static CardInsight? Inspect(string source, bool withHistory)
    {
        try
        {
            var store = withHistory ? HistoryStore.Default(m => Console.Error.WriteLine(m)) : null;
            return CardInspector.Inspect(source, store?.Load(), store, CancellationToken.None);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"history: {e.Message}");
            return null;
        }
    }
}
