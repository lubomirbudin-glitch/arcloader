using System.Globalization;
using System.Runtime.ExceptionServices;
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
    readonly bool hasCustomLut;
    readonly bool hasFont;
    /// Built-in LUT ids already copied into the work folder.
    readonly HashSet<string> builtinLuts = new(StringComparer.Ordinal);

    public ProxySettings Settings { get; }
    public ExternalTools Tools { get; }

    string? encoderName;

    /// The video encoder proxies are rendered with (probed once, on first use); null without ffmpeg.
    public string? EncoderName => Tools.Ffmpeg is string ffmpeg ? encoderName ??= Encoders.Pick(ffmpeg, Settings.Codec) : null;

    public ProxyRenderer(ProxySettings settings, ExternalTools tools)
    {
        Settings = settings;
        Tools = tools;
        workDir = Path.Combine(Path.GetTempPath(), "offload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        try
        {
            // The custom LUT is copied up front as before; built-in ones lazily, per clip (PrepareLut).
            if (LutResolver.EffectiveMode(settings) == LutMode.Custom && !string.IsNullOrEmpty(settings.LutPath) && File.Exists(settings.LutPath))
            {
                File.Copy(settings.LutPath, Path.Combine(workDir, FilterGraph.LutFile));
                hasCustomLut = true;
            }
            if (FontCandidates.FirstOrDefault(File.Exists) is string font)
            {
                File.Copy(font, Path.Combine(workDir, FilterGraph.FontFile));
                hasFont = true;
            }
        }
        catch
        {
            try { Directory.Delete(workDir, recursive: true); } catch (Exception) { }
            throw;
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
        var all = new List<FramePosition> { first };
        if (middle > 0) all.Add(new FramePosition(middle / fps, middle, false));
        if (last > middle) all.Add(new FramePosition(last / fps, last, true));
        return all;
    }

    internal static string Label(ClipInfo info, FramePosition p) =>
        Timecode.IsValid(info.Timecode) && Timecode.Fps(info.FrameRate) is double fps
            ? Timecode.Add(info.Timecode!, p.Frame, fps)
            : $"#{p.Frame}";

    FrameSource SourceFor(Clip clip, string cardPath, ClipInfo info, RawLook look, double? singleFrameAt, bool forRender)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
            return new FrameSource { InputArgs = forRender ? ["-hwaccel", "auto", "-i", path] : ["-i", path] };
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, "no decoder");
        return b.Source(path, info, Settings, look, Tools, workDir, singleFrameAt);
    }

    /// The .cube for this clip by its name in the work folder, or null for no LUT. The custom LUT was copied
    /// in the constructor (null when it was missing); a built-in one is copied on first use, once per id.
    string? PrepareLut(LutUse? lut)
    {
        if (lut?.File is not string file) return null;
        if (!lut.IsBuiltin) return hasCustomLut ? FilterGraph.LutFile : null;
        var name = $"lut-{lut.Id}.cube";
        if (!builtinLuts.Contains(lut.Id!))
        {
            File.Copy(file, Path.Combine(workDir, name), overwrite: true);
            builtinLuts.Add(lut.Id!);
        }
        return name;
    }

    /// RAW decoders hand over log whenever a LUT follows; ARRI's target space must match a built-in ARRI LUT.
    internal static RawLook LookFor(LutUse? lut, ClipInfo info)
    {
        if (lut?.File is not string file) return new RawLook(false, null);
        return new RawLook(true, lut.Id switch
        {
            "arri-logc4-awg4" => "AWG4/D65/LogC4",
            "arri-logc3-awg3" => "AWG3/D65/LogC3",
            null => ArriBackend.ColorSpace(file, info),
            _ => null,
        });
    }

    /// Built-in LUTs expect legal-range input: full-range sources (BRAW, video tagged "pc") are pre-mapped first.
    internal static bool NeedsPreMap(LutUse? lut, FrameSource src, ClipInfo info) =>
        lut?.IsBuiltin == true && (src.FullRange || info.ColorRange == "pc");

    /// Where ffmpeg's input seek (-ss before -i) should land for a thumbnail position. A frame in the middle is
    /// sought half a frame early: its exact time rounded up would skip to the next frame (23.976, 24, 29.97 fps).
    /// The last frame is sought a second before it and decoded to the end of the video stream — never relative
    /// to the container end, which is later when the audio runs longer than the picture.
    internal static string SeekSeconds(ClipInfo info, FramePosition p)
    {
        double at = p.Seconds;
        if (p.FromEnd) at = Math.Max(0, p.Seconds - 1);
        else if (p.Frame > 0 && Timecode.Fps(info.FrameRate) is double fps) at = (p.Frame - 0.5) / fps;
        return at.ToString("0.000000", CultureInfo.InvariantCulture);
    }

    /// First, middle and last frame as small JPEGs with the LUT applied, so log footage is readable.
    /// When `isCancelled` turns true the clip simply gets no thumbnails.
    /// `lut` null = no LUT (thumbnails taken from a finished proxy, which already has it).
    public List<Thumbnail> Thumbnails(Clip clip, string cardPath, ClipInfo info, LutUse? lut, Func<bool>? isCancelled = null)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        isCancelled ??= () => false;
        var lutFile = PrepareLut(lut);
        var look = LookFor(lutFile != null ? lut : null, info);  // log only when a LUT really follows
        var result = new List<Thumbnail>();
        Exception? lastError = null;
        foreach (var pos in FramePositions(info))
        {
            if (isCancelled()) return [];
            FrameSource? src = null;
            var output = Path.Combine(workDir, $"thumb-{Guid.NewGuid():N}.jpg");
            try
            {
                src = SourceFor(clip, cardPath, info, look, pos.Seconds, forRender: false);
                var vf = FilterGraph.Thumbnail(lutFile, NeedsPreMap(lut, src, info));  // the pre-map depends on the source
                bool video = clip.Format == ClipFormat.Video;
                var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
                if (src.FeederExe == null) args.Add("-nostdin");
                if (video) args.AddRange(["-ss", SeekSeconds(info, pos)]);
                args.AddRange(src.InputArgs);
                // Last frame: decode the final second and keep overwriting the image — the last write is the last frame.
                args.AddRange(video && pos.FromEnd ? ["-map", "0:v:0", "-vf", vf, "-update", "1"] : ["-frames:v", "1", "-vf", vf]);
                args.AddRange(["-q:v", "5", output]);
                RunFfmpeg(ffmpeg, args, src, null, null, isCancelled);
                if (!File.Exists(output)) throw new ToolError("ffmpeg", 0, "no frame");
                result.Add(new Thumbnail(File.ReadAllBytes(output), Label(info, pos)));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (isCancelled()) return [];
                lastError = e; // one bad position must not cost the other thumbnails
            }
            finally
            {
                src?.RemoveIntermediates();
                NativeIO.Delete(output);
            }
        }
        if (result.Count == 0 && lastError != null) ExceptionDispatchInfo.Throw(lastError);
        return result;
    }

    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, "ffmpeg not found");
        var lutFile = PrepareLut(lut);
        var src = SourceFor(clip, cardPath, info, LookFor(lutFile != null ? lut : null, info), null, forRender: true);
        string? label = null;
        try
        {
            int outHeight = Math.Min(Settings.Height, info.Height ?? Settings.Height) & ~1;
            if (Settings.BurnFilename && hasFont)
            {
                label = $"label-{Guid.NewGuid().ToString("N")[..8]}.txt";
                File.WriteAllText(Path.Combine(workDir, label), clip.Name, new UTF8Encoding(false));
            }
            var vf = FilterGraph.Proxy(Settings, outHeight, src.AlreadyCropped, lutFile, NeedsPreMap(lut, src, info), hasFont, label, info.Timecode, info.FrameRate);

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
            args.AddRange(Encoders.VideoArgs(EncoderName!, Settings.Codec, Settings.BitrateMbps));
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

    internal void RunFfmpeg(string ffmpeg, List<string> args, FrameSource src, double? duration, Action<double>? progress, Func<bool> isCancelled)
    {
        using var ff = new ToolProcess(ffmpeg, args, workDir, pipeStdin: src.FeederExe != null);
        using var feeder = src.FeederExe != null ? new ToolProcess(src.FeederExe, src.FeederArgs, workDir, pipeStdin: false) : null;
        Thread? pump = null;
        Stream? input = null, sink = null;
        bool feederKilled = false;
        try
        {
            feeder?.Start(null);
            ff.Start(line =>
            {
                if (progress == null || duration is not double d || d <= 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
                if (double.TryParse(line["out_time_us=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var us))
                    progress(Math.Clamp(us / 1_000_000 / d, 0, 1));
            });
            if (feeder != null)
            {
                // Streams are taken here, on the calling thread, so the pump never touches a disposed process.
                input = feeder.Stdout;
                sink = ff.Stdin;
                var from = input;
                var to = sink;
                pump = new Thread(() =>
                {
                    // Any failure here just means the pipe ended (done, cancelled or a process died); the exit codes tell the story.
                    // Never let an exception escape a background thread: it would take the whole process down.
                    try { from.CopyTo(to, 4 << 20); }
                    catch (Exception) { }
                    finally { try { to.Close(); } catch (Exception) { } }
                }) { IsBackground = true };
                pump.Start();
            }
            while (!ff.WaitForExit(200))
            {
                if (!isCancelled()) continue;
                ff.Kill();
                feeder?.Kill();
                feederKilled = true;
                ff.WaitForExit();
                pump?.Join(5000);
                throw new ToolError("ffmpeg", -1, "cancelled");
            }
            ff.WaitForExit();
            if (feeder != null)
            {
                if (!feeder.WaitForExit(2000)) { feeder.Kill(); feederKilled = true; }
                pump!.Join(2000);
            }
            // A feeder that died on its own is the real cause; ffmpeg only saw a truncated stream.
            if (feeder != null && !feederKilled && feeder.WaitForExit(0) && feeder.ExitCode != 0)
                throw new ToolError(feeder.Name, feeder.ExitCode, feeder.StderrTail);
            if (ff.ExitCode != 0) throw new ToolError("ffmpeg", ff.ExitCode, ff.StderrTail);
        }
        finally
        {
            pump?.Join(2000);
            try { input?.Dispose(); } catch (Exception) { }
            try { sink?.Dispose(); } catch (Exception) { }
        }
    }
}
