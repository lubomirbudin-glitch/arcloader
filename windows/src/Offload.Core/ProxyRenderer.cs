using System.Globalization;
using System.Text;

namespace Offload.Core;

internal sealed record FramePosition(double Seconds, long Frame, bool FromEnd);

/// Probes clips, makes the report thumbnails (first, middle, last frame) and renders proxies.
/// Works in its own temp folder; ffmpeg runs there so filter graphs only use relative names.
public sealed class ProxyRenderer : IDisposable
{
    static readonly string[] FontCandidates = [@"C:\Windows\Fonts\consola.ttf", @"C:\Windows\Fonts\cour.ttf"];

    readonly Dictionary<ClipFormat, IRawBackend> backends = RawBackends.All.ToDictionary(b => b.Format);
    readonly string workDir;
    readonly bool hasLut;
    readonly bool hasFont;

    public ProxySettings Settings { get; }
    public ExternalTools Tools { get; }

    public ProxyRenderer(ProxySettings settings, ExternalTools tools)
    {
        Settings = settings;
        Tools = tools;
        workDir = Path.Combine(Path.GetTempPath(), "offload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        if (!string.IsNullOrEmpty(settings.LutPath) && File.Exists(settings.LutPath))
        {
            File.Copy(settings.LutPath, Path.Combine(workDir, FilterGraph.LutFile));
            hasLut = true;
        }
        if (FontCandidates.FirstOrDefault(File.Exists) is string font)
        {
            File.Copy(font, Path.Combine(workDir, FilterGraph.FontFile));
            hasFont = true;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(workDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// null when the clip can be rendered, otherwise the reason it cannot.
    public string? UnsupportedReason(Clip clip)
    {
        if (Tools.Ffmpeg == null) return "ffmpeg not found";
        return clip.Format switch
        {
            ClipFormat.Video => null,
            ClipFormat.Unknown => "unknown format",
            _ => backends.TryGetValue(clip.Format, out var b)
                ? (b.IsAvailable(Tools) ? null : b.MissingHint)
                : $"{clip.Format.Label()}: no decoder available",
        };
    }

    public ClipInfo Probe(Clip clip, string cardPath)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
        {
            var ffprobe = Tools.Ffprobe ?? throw new ToolError("ffprobe", -1, "ffprobe not found");
            return FFProbe.Probe(ffprobe, path);
        }
        if (!backends.TryGetValue(clip.Format, out var b) || !b.IsAvailable(Tools))
            throw new ToolError(clip.Format.Label(), -1, UnsupportedReason(clip) ?? "");
        return b.Probe(path, Tools, workDir);
    }

    /// MXF is a container for many codecs; ARRIRAW/ARRICORE inside it only the ARRI tool can read.
    public (Clip Clip, ClipInfo Info)? ReclassifyUndecodableMxf(Clip clip, string cardPath)
    {
        if (clip.Format != ClipFormat.Video || !clip.Path.EndsWith(".mxf", StringComparison.OrdinalIgnoreCase)) return null;
        var arri = backends[ClipFormat.Arriraw];
        if (!arri.IsAvailable(Tools)) return null;
        try
        {
            var info = arri.Probe(Layout.Join(cardPath, clip.Path), Tools, workDir);
            if (info.Width == null) return null;
            return (new Clip { Name = clip.Name, Path = clip.Path, Format = ClipFormat.Arriraw, Segments = clip.Segments, Bytes = clip.Bytes, IsCameraProxy = clip.IsCameraProxy }, info);
        }
        catch (Exception)
        {
            // Best effort: any failure (tool error, malformed metadata JSON, ...) just means "not ARRI".
            return null;
        }
    }

    internal static IReadOnlyList<FramePosition> FramePositions(ClipInfo info)
    {
        var first = new FramePosition(0, 0, false);
        if (info.Duration is not double duration || duration <= 0 || Timecode.Fps(info.FrameRate) is not double fps) return [first];
        long frames = info.Frames ?? (long)Math.Round(duration * fps);
        if (frames <= 1) return [first];
        long middle = frames / 2, last = frames - 1;
        return [first, new FramePosition(middle / fps, middle, false), new FramePosition(last / fps, last, true)];
    }

    internal static string Label(ClipInfo info, FramePosition p) =>
        Timecode.IsValid(info.Timecode) && Timecode.Fps(info.FrameRate) is double fps
            ? Timecode.Add(info.Timecode!, p.Frame, fps)
            : $"#{p.Frame}";

    FrameSource SourceFor(Clip clip, string cardPath, ClipInfo info, double? singleFrameAt, bool forRender)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
            return new FrameSource { InputArgs = forRender ? ["-hwaccel", "auto", "-i", path] : ["-i", path] };
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, "no decoder");
        return b.Source(path, info, Settings, Tools, workDir, singleFrameAt);
    }

    /// First, middle and last frame as small JPEGs with the LUT applied, so log footage is readable.
    public List<Thumbnail> Thumbnails(Clip clip, string cardPath, ClipInfo info, bool applyLut = true)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        var vf = FilterGraph.Thumbnail(hasLut && applyLut);
        var result = new List<Thumbnail>();
        foreach (var pos in FramePositions(info))
        {
            var src = SourceFor(clip, cardPath, info, pos.Seconds, forRender: false);
            var output = Path.Combine(workDir, $"thumb-{Guid.NewGuid():N}.jpg");
            try
            {
                bool video = clip.Format == ClipFormat.Video;
                var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
                if (src.FeederExe == null) args.Add("-nostdin");
                if (video) args.AddRange(pos.FromEnd ? ["-sseof", "-1"] : ["-ss", pos.Seconds.ToString("0.000", CultureInfo.InvariantCulture)]);
                args.AddRange(src.InputArgs);
                // From the end: decode the last second and keep overwriting the image — the final write is the last frame.
                args.AddRange(video && pos.FromEnd ? ["-vf", vf, "-update", "1"] : ["-frames:v", "1", "-vf", vf]);
                args.AddRange(["-q:v", "5", output]);
                RunFfmpeg(ffmpeg, args, src, null, null, () => false);
                result.Add(new Thumbnail(File.ReadAllBytes(output), Label(info, pos)));
            }
            finally
            {
                src.RemoveIntermediates();
                NativeIO.Delete(output);
            }
        }
        return result;
    }

    public void Render(Clip clip, string cardPath, ClipInfo info, string output, Action<double> progress, Func<bool> isCancelled)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        var src = SourceFor(clip, cardPath, info, null, forRender: true);
        string? label = null;
        try
        {
            int outHeight = Math.Min(Settings.Height, info.Height ?? Settings.Height) & ~1;
            if (Settings.BurnFilename && hasFont)
            {
                label = $"label-{Guid.NewGuid().ToString("N")[..8]}.txt";
                File.WriteAllText(Path.Combine(workDir, label), clip.Name, new UTF8Encoding(false));
            }
            var vf = FilterGraph.Proxy(Settings, outHeight, src.AlreadyCropped, hasLut, hasFont, label, info.Timecode, info.FrameRate);

            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-nostats", "-progress", "pipe:1" };
            if (src.FeederExe == null) args.Add("-nostdin");
            args.AddRange(src.InputArgs);
            args.AddRange(["-map", "0:v:0"]);
            if (Settings.Audio && info.AudioChannels > 0 && src.AudioFromInput)
            {
                args.AddRange(["-map", "0:a:0?"]);
                args.AddRange(Encoders.AudioArgs(Settings.Codec));
            }
            args.AddRange(["-vf", vf]);
            args.AddRange(Encoders.VideoArgs(Encoders.Pick(ffmpeg, Settings.Codec), Settings.Codec, Settings.BitrateMbps));
            if (Timecode.IsValid(info.Timecode)) args.AddRange(["-timecode", info.Timecode!]);

            var dir = Path.GetDirectoryName(output)!;
            NativeIO.Mkdirs(dir);
            var part = Path.Combine(dir, "." + Path.GetFileName(output) + ".part");
            args.AddRange(["-f", "mov", part]);
            try
            {
                RunFfmpeg(ffmpeg, args, src, info.Duration, progress, isCancelled);
                NativeIO.Move(part, output, replace: true);
            }
            catch
            {
                NativeIO.Delete(part);
                throw;
            }
        }
        finally
        {
            src.RemoveIntermediates();
            if (label != null) NativeIO.Delete(Path.Combine(workDir, label));
        }
    }

    void RunFfmpeg(string ffmpeg, List<string> args, FrameSource src, double? duration, Action<double>? progress, Func<bool> isCancelled)
    {
        using var ff = new ToolProcess(ffmpeg, args, workDir, pipeStdin: src.FeederExe != null);
        using var feeder = src.FeederExe != null ? new ToolProcess(src.FeederExe, src.FeederArgs, workDir, pipeStdin: false) : null;
        Thread? pump = null;
        feeder?.Start(null);
        ff.Start(line =>
        {
            if (progress == null || duration is not double d || d <= 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
            if (double.TryParse(line["out_time_us=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var us))
                progress(Math.Min(1, us / 1_000_000 / d));
        });
        if (feeder != null)
        {
            pump = new Thread(() =>
            {
                try { feeder.Stdout.CopyTo(ff.Stdin, 4 << 20); }
                catch (IOException) { /* ffmpeg closed its input (done or cancelled) */ }
                finally { try { ff.Stdin.Close(); } catch (IOException) { } }
            }) { IsBackground = true };
            pump.Start();
        }
        while (!ff.WaitForExit(200))
        {
            if (!isCancelled()) continue;
            ff.Kill();
            feeder?.Kill();
            ff.WaitForExit();
            throw new ToolError("ffmpeg", -1, "cancelled");
        }
        ff.WaitForExit();
        if (feeder != null)
        {
            if (!feeder.WaitForExit(2000)) feeder.Kill();
            pump!.Join(2000);
        }
        if (ff.ExitCode != 0) throw new ToolError("ffmpeg", ff.ExitCode, ff.StderrTail);
        if (feeder != null && feeder.WaitForExit(0) && feeder.ExitCode is int code && code != 0 && code != -1)
            throw new ToolError(feeder.Name, code, feeder.StderrTail);
    }
}
