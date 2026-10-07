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
    readonly Option<int> proxyHeight = new("--proxy-height") { Description = "Proxy height in pixels.", DefaultValueFactory = _ => 1080 };
    readonly Option<double?> frameAspect = new("--frame-aspect") { Description = "Crop proxies to the frame lines: aspect ratio, e.g. 1.66.", CustomParser = a => CliParse.Parse(a) };
    readonly Option<double> frameScale = new("--frame-scale") { Description = "Frame lines size in % of the image (90 = 10 % overscan).", DefaultValueFactory = _ => 100, CustomParser = a => CliParse.Parse(a) ?? 100 };
    readonly Option<string> proxyCodec = new("--proxy-codec") { Description = "Proxy codec: h264, hevc, proresProxy, proresLT, prores422, prores422HQ.", DefaultValueFactory = _ => "h264" };
    readonly Option<int> proxyBitrate = new("--proxy-bitrate") { Description = "Proxy bitrate (Mb/s) for H.264/HEVC.", DefaultValueFactory = _ => 10 };
    readonly Option<bool> noBurnIn = new("--no-burn-in") { Description = "No burn-in of clip name and timecode." };
    readonly Option<string?> proxyFolder = new("--proxy-folder") { Description = "Also copy every proxy to <folder>\\<date>\\<card name>\\ (with --proxy; never overwrites)." };
    readonly Option<bool> noClipList = new("--no-clip-list") { Description = "No clip list CSV (_REPORT\\<card>_clips.csv)." };
    readonly Option<bool> ale = new("--ale") { Description = "Also write the clip list for Avid (_REPORT\\<card>_clips.ale)." };
    readonly Option<bool> noDayReport = new("--no-day-report") { Description = "Do not make the day report (<date>\\_REPORT\\<date>_day_report.html) again after the copy." };

    public void AddTo(Command cmd)
    {
        foreach (var o in new Option[] { noAnalyze, proxy, lut, lutFallback, proxyHeight, frameAspect, frameScale, proxyCodec, proxyBitrate, noBurnIn, proxyFolder, noClipList, ale, noDayReport })
            cmd.Options.Add(o);
    }

    /// Sets AnalyzeClips and Proxy; throws ArgumentException with a Czech message on a bad value.
    public void Apply(ParseResult r, JobConfig config)
    {
        if (!ProxyCodecExtensions.TryParseWire(r.GetValue(proxyCodec)!, out var codec))
            throw new ArgumentException(Msg.UnknownProxyCodec(r.GetValue(proxyCodec)));
        var (lutMode, lutBuiltin, lutRaw, lutFb, lutErr) = LutOption.Parse(r.GetValue(lut), r.GetValue(lutFallback), LutCatalog.Default);
        if (lutErr != null) throw new ArgumentException(lutErr);
        var lutPath = lutRaw != null ? CliPath.Full(lutRaw) : null;
        if (lutPath != null && !File.Exists(lutPath)) throw new ArgumentException(Msg.LutNotFound(lutPath));
        config.AnalyzeClips = !r.GetValue(noAnalyze);
        config.Outputs = new OutputSettings { ClipListCsv = !r.GetValue(noClipList), ClipListAle = r.GetValue(ale), DayReport = !r.GetValue(noDayReport) };
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
            Console.Error.WriteLine(Msg.ProxyFolderNoEffect);
        if (ProxyPreflight.ExtraFolderInUse(config.Proxy) is string folder && !Directory.Exists(folder))
            throw new ArgumentException(Msg.ProxyFolderNotFound(folder));
    }
}
