using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Cli;

static class CopyCommand
{
    public static Command Create()
    {
        var source = new Argument<string>("source") { Description = "Card drive or source folder." };
        var dest = new Option<string[]>("--dest", "-d") { Description = "Destination root (repeat for each disk).", Required = true };
        var name = new Option<string?>("--name") { Description = "Card folder name (default: volume label)." };
        var date = new Option<string?>("--date") { Description = "Date folder (default: today, YYYY-MM-DD)." };
        var chunkMb = new Option<int>("--chunk-mb") { Description = "Block size in MB.", DefaultValueFactory = _ => 16 };
        var buffers = new Option<int>("--buffers") { Description = "Blocks in flight.", DefaultValueFactory = _ => 16 };
        var noVerify = new Option<bool>("--no-verify") { Description = "Skip read-back verification (not recommended)." };
        var allowSame = new Option<bool>("--allow-same-device") { Description = "Allow two destinations on the same volume." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Only scan and check, do not copy." };
        var noAnalyze = new Option<bool>("--no-analyze") { Description = "Skip clip analysis (metadata + thumbnails in the report)." };
        var proxy = new Option<bool>("--proxy") { Description = "Render proxies after the copy is verified." };
        var lut = new Option<string?>("--lut") { Description = "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload luts prints them) or a .cube path." };
        var lutFallback = new Option<string?>("--lut-fallback") { Description = "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto." };
        var proxyHeight = new Option<int>("--proxy-height") { Description = "Proxy height in pixels.", DefaultValueFactory = _ => 1080 };
        var frameAspect = new Option<double?>("--frame-aspect") { Description = "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.", CustomParser = a => CliParse.Parse(a) };
        var frameScale = new Option<double>("--frame-scale") { Description = "Frame lines size in % of the image (90 = 10 % overscan).", DefaultValueFactory = _ => 100, CustomParser = a => CliParse.Parse(a) ?? 100 };
        var proxyCodec = new Option<string>("--proxy-codec") { Description = "Proxy codec: h264, hevc, proresProxy, proresLT, prores422, prores422HQ.", DefaultValueFactory = _ => "h264" };
        var proxyBitrate = new Option<int>("--proxy-bitrate") { Description = "Proxy bitrate (Mb/s) for H.264/HEVC.", DefaultValueFactory = _ => 10 };
        var noBurnIn = new Option<bool>("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
        var proxyFolder = new Option<string?>("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };

        var cmd = new Command("copy", "Copy a card to <dest>/<date>/<card name>/ on every destination and verify.");
        cmd.Arguments.Add(source);
        foreach (var o in new Option[] { dest, name, date, chunkMb, buffers, noVerify, allowSame, dryRun, noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder }) cmd.Options.Add(o);
        cmd.SetAction(r =>
        {
            JobConfig config;
            try
            {
                config = new JobConfig(CliPath.Full(r.GetValue(source)!), r.GetValue(dest)!.Select(CliPath.Full).ToList())
                {
                    CardName = r.GetValue(name),
                    Date = r.GetValue(date),
                    ChunkMB = r.GetValue(chunkMb),
                    Buffers = r.GetValue(buffers),
                    Verify = !r.GetValue(noVerify),
                    AllowSameDevice = r.GetValue(allowSame),
                };
                if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
                    throw new ArgumentException($"Unknown proxy codec {r.GetValue(proxyCodec)}");
                var (lutMode, lutBuiltin, lutRaw, lutFb, lutErr) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default);
                if (lutErr != null) throw new ArgumentException(lutErr);
                var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
                if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException($"LUT not found: {lutPath}");
                config.AnalyzeClips = !r.GetValue(noAnalyze);
                config.Proxy = new ProxySettings
                {
                    Enabled = r.GetValue(proxy),
                    LutMode = lutMode,
                    LutBuiltin = lutBuiltin,
                    LutPath = lutPath,
                    LutFallback = lutFb,
                    Height = r.GetValue(proxyHeight),
                    Codec = codec,
                    BitrateMbps = r.GetValue(proxyBitrate),
                    FrameAspect = r.GetValue(frameAspect),
                    FrameScale = r.GetValue(frameScale) / 100,
                    BurnFilename = !r.GetValue(noBurnIn),
                    BurnTimecode = !r.GetValue(noBurnIn),
                    ExtraFolder = r.GetValue(proxyFolder) is string pf ? CliPath.Full(pf) : null,
                };
                if (config.Proxy.ExtraFolder != null && !config.Proxy.Enabled)
                    Console.Error.WriteLine("Note: --proxy-folder has no effect without --proxy.");
                if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string folder && !Directory.Exists(folder))
                    throw new ArgumentException($"Proxy folder not found: {folder}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
            // Only when proxies or clip analysis (thumbnails) are on — otherwise no LUT is used at all.
            if (LutOption.PlanLine(config.Proxy, config.AnalyzeClips) is string lutLine) Console.WriteLine(lutLine);
            return Run(config, r.GetValue(dryRun));
        });
        return cmd;
    }

    static int Run(JobConfig config, bool dryRun)
    {
        var job = new OffloadJob(config);
        OffloadPlan plan;
        try
        {
            plan = job.Prepare();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            return 1;
        }
        Console.WriteLine($"Card {plan.CardName}: {plan.Scan.Files.Count} files, {Format.Bytes(plan.Scan.TotalBytes)}");
        foreach (var p in plan.CardPaths) Console.WriteLine($"  → {p}");
        if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string extra)
            Console.WriteLine($"  proxy also → {Layout.CardPath(extra, plan.Date, plan.CardName)}");
        if (config.AnalyzeClips || config.Proxy.Enabled)
            Console.WriteLine($"  ffmpeg: {config.Tools.Resolved().Ffmpeg ?? "NOT FOUND"}");
        // Connection chain: informational only, must never fail a copy.
        List<RoleLink>? chainLinks = null;
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
        if (dryRun) return 0;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("\nCancelling — finishing current block, partial files will be removed…");
            job.Cancel();
        };

        OffloadReport? report;
        using (new ProgressDisplay(job, chainLinks)) report = job.Run(message => Console.Error.WriteLine(message));

        var status = job.Status();
        if (report != null)
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
            Console.WriteLine($"Report: {Path.Combine(plan.CardPaths[0], Layout.ReportDirName, plan.CardName + "_report.html")}");
        }
        if (status.Error != null) Console.WriteLine($"ERROR: {status.Error}");
        return status.Phase == JobPhase.Done && report?.Success == true ? 0 : 1;
    }
}
