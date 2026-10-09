using System.CommandLine;
using Offload.Core;
using Offload.Core.Desktop;
using Offload.Core.Outputs;

namespace Offload.Cli;

/// The options of stage 2 (clip analysis, proxies, LUT) — the same for `copy` and `append`.
sealed class StageTwoOptions
{
    readonly Option<bool> noAnalyze = new("--no-analyze") { Description = "Skip clip analysis (metadata + thumbnails in the report)." };
    readonly Option<bool> proxy = new("--proxy") { Description = "Render proxies after the copy is verified." };
    readonly Option<string?> lut = new("--lut") { Description = "LUT for proxies and thumbnails: auto (by camera, default), none, a built-in id (offload luts prints them) or a .cube path." };
    readonly Option<string?> lutFallback = new("--lut-fallback") { Description = "Built-in LUT id (or none) for clips whose camera is not recognised with --lut auto." };
    readonly Option<string[]> lutRule = new("--lut-rule") { Description = "Own LUT rule before the camera detection (with --lut auto), repeat in priority order: <camera|serial|reel|folder>=<value>:<lut id|none|path.cube>, e.g. camera=FX6:sony-slog3-sgamut3cine.", DefaultValueFactory = _ => [] };
    readonly Option<int> proxyHeight = new("--proxy-height") { Description = "Proxy height in pixels.", DefaultValueFactory = _ => 1080 };
    readonly Option<double?> frameAspect = new("--frame-aspect") { Description = "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.", CustomParser = a => CliParse.Parse(a) };
    readonly Option<double> frameScale = new("--frame-scale") { Description = "Frame lines size in % of the image (90 = 10 % overscan).", DefaultValueFactory = _ => 100, CustomParser = a => CliParse.Parse(a) ?? 100 };
    readonly Option<string> proxyCodec = new("--proxy-codec") { Description = "Proxy codec: h264, hevc, proresProxy, proresLT, prores422, prores422HQ.", DefaultValueFactory = _ => "h264" };
    readonly Option<int> proxyBitrate = new("--proxy-bitrate") { Description = "Proxy bitrate (Mb/s) for H.264/HEVC.", DefaultValueFactory = _ => 10 };
    readonly Option<bool> proxyAlways = new("--proxy-always") { Description = "Render a proxy also when the original is lighter (H.264/HEVC no bigger than the proxy and at most its bitrate; by default such a clip gets no H.264/HEVC proxy — a ProRes proxy is always rendered; only when no LUT and no frame-lines crop is used)." };
    readonly Option<bool> noBurnIn = new("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
    readonly Option<string?> proxyFolder = new("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };
    readonly Option<bool> noClipList = new("--no-clip-list") { Description = "No clip list CSV (_REPORT\\<card>_clips.csv)." };
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };
    readonly Option<bool> hwDecode = new("--hw-decode") { Description = "Decode H.264/HEVC sources of proxies on the GPU (experimental; by default they decode in software — hardware decoding gave green frames on the Mac). Encoding uses the GPU either way." };
    readonly Option<bool> brawGen5 = new("--braw-gen5") { Description = "Decode Blackmagic RAW Film Gen 4 as Gen 5 (the Gen 5 LUT then fits). Not yet verified with a Gen 4 sample." };
    readonly Option<bool> noDayReport = new("--no-day-report") { Description = "Do not make the day report (<date>\\_REPORT\\<date>_day_report.html) again after the copy." };
    readonly Option<bool> classicVerify = new("--classic-verify") { Description = "Read every destination back right after the copy (as before 0.6.0) instead of verifying the primary one while the proxies are made." };
    readonly Option<int?> primary = new("--primary") { Description = "Destination (1, 2 …, in the order of --dest) the proxies read and verify; default: the fastest one measured during the copy." };
    readonly Option<bool> noLocalReport = new("--no-local-report") { Description = "Do not copy the card's report (_REPORT, ascmhl) to Documents\\Offload reporty\\<date>\\<card name>\\ on this computer." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, lutRule, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, proxyAlways, hwDecode, noBurnIn, proxyFolder, noClipList, ale, noDayReport, brawGen5, classicVerify, primary, noLocalReport })
            cmd.Options.Add(o);
    }

    /// Sets AnalyzeClips, Outputs and Proxy; throws ArgumentException with a Czech message on a bad value. `project`
    /// (--project): its values are the defaults and only the options written on the command line replace them (spec 4.1).
    public void Apply(ParseResult r, JobConfig config, SettingsProfile? project = null)
    {
        bool Given(Option o) => CliProject.Given(r, project, o);
        var p = project != null ? SettingsStore.Clone(project.Proxy) : new ProxySettings();
        if (Given(proxyCodec))
        {
            if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
                throw new ArgumentException(Msg.UnknownProxyCodec(r.GetValue(proxyCodec)));
            p.Codec = codec;
        }
        if (Given(lut) || Given(lutFallback))
        {
            var (lutMode, lutBuiltin, lutRaw, lutFb, lutErr) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default);
            if (lutErr != null) throw new ArgumentException(lutErr);
            if (Given(lut))
            {
                p.LutMode = lutMode;
                p.LutBuiltin = lutBuiltin;
                p.LutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
                if (p.LutPath != null && !File.Exists(p.LutPath)) throw new ArgumentException(Msg.LutNotFound(p.LutPath));
            }
            if (Given(lutFallback)) p.LutFallback = lutFb;
        }
        if (Given(lutRule))
        {
            var rules = new List<LutRule>();
            foreach (var spec in r.GetValue(lutRule)!)
            {
                var (rule, error) = LutRules.ParseOption(spec, LutCatalog.Default);
                if (rule == null) throw new ArgumentException(error);
                if (rule.LutPath != null)
                {
                    rule.LutPath = CliPath.Full(rule.LutPath);
                    if (!File.Exists(rule.LutPath)) throw new ArgumentException(Msg.RuleLutNotFound(rules.Count + 1, Path.GetFileName(rule.LutPath)));
                }
                rules.Add(rule);
            }
            // Rules work in Auto mode only (R18): written ones with another LUT would be silently ignored.
            if (rules.Count > 0 && LutResolver.EffectiveMode(p) != LutMode.Auto) throw new ArgumentException(Msg.LutRuleNeedsAuto);
            p.LutRules = rules;   // written rules replace the project's (their order is the priority)
        }
        if (Given(proxy)) p.Enabled = r.GetValue(proxy);
        if (Given(proxyHeight)) p.Height = r.GetValue(proxyHeight);
        if (Given(proxyBitrate)) p.BitrateMbps = r.GetValue(proxyBitrate);
        if (Given(frameAspect)) p.FrameAspect = r.GetValue(frameAspect);
        if (Given(frameScale)) p.FrameScale = r.GetValue(frameScale) / 100;
        if (Given(proxyAlways)) p.SkipLighterSources = !r.GetValue(proxyAlways);
        if (Given(noBurnIn)) p.BurnFilename = p.BurnTimecode = !r.GetValue(noBurnIn);
        if (Given(brawGen5)) p.BrawGen4ToGen5 = r.GetValue(brawGen5);
        if (Given(hwDecode)) p.HardwareDecode = r.GetValue(hwDecode);
        if (Given(proxyFolder)) p.ExtraFolder = r.GetValue(proxyFolder) is string pf ? CliPath.Full(pf) : null;
        config.Proxy = p;

        var outputs = project != null ? SettingsStore.Clone(project.Outputs) : new OutputSettings();
        if (Given(noClipList)) outputs.ClipListCsv = !r.GetValue(noClipList);
        if (Given(ale)) outputs.ClipListAle = r.GetValue(ale);
        if (Given(noDayReport)) outputs.DayReport = !r.GetValue(noDayReport);
        if (Given(noLocalReport)) outputs.LocalReport = !r.GetValue(noLocalReport);
        config.Outputs = outputs;
        // Task 2026-10-09: the report on this computer too (the project's folder, else Documents\\Offload reporty).
        config.LocalReports = LocalReports.RootFor(outputs);
        config.AnalyzeClips = Given(noAnalyze) ? !r.GetValue(noAnalyze) : project!.AnalyzeClips;
        // Plan 10 (R11): verify-in-proxy unless --classic-verify (or the project switched it off); --primary picks the disk.
        config.VerifyInProxy = Given(classicVerify) ? !r.GetValue(classicVerify) : project!.VerifyInProxy;
        if (r.GetValue(primary) is int n)
        {
            if (n < 1) throw new ArgumentException(Msg.PrimaryNotPositive(n));
            config.PrimaryDestination = n - 1;
            if (config.DestRoots.Count > 0) CheckPrimary(config, config.DestRoots.Count);   // a copy knows its destinations here
        }
        else config.PrimaryDestination = project != null ? JobConfigs.Primary(project) : null;

        if (r.GetValue(proxyFolder) != null && !p.Enabled)
            Console.Error.WriteLine(Msg.ProxyFolderNoEffect);
        if (ProxyPreflight.ExtraFolderInUse(p) is string folder && !Directory.Exists(folder))
            throw new ArgumentException(Msg.ProxyFolderNotFound(folder));
        // A project's own LUT (or, from task 5, a rule's) that is gone stops the job before it starts, as in the app.
        if (ProxyPreflight.Blocking(p, config.AnalyzeClips) is string missing) throw new ArgumentException(missing);
    }

    /// --primary N above the number of destinations is refused (it used to fall back to the fastest disk silently).
    /// An append calls it again once the earlier backup's folders are known.
    public static void CheckPrimary(JobConfig config, int destinations)
    {
        if (config.PrimaryDestination is int i && i >= destinations) throw new ArgumentException(Msg.PrimaryTooHigh(i + 1, destinations));
    }
}
