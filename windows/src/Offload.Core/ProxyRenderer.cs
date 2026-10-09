using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Offload.Core.VerifyInProxy;

namespace Offload.Core;

internal sealed record FramePosition(double Seconds, long Frame, bool FromEnd);

/// Probes clips, makes the report thumbnails (first, middle, last frame) and renders proxies.
/// Works in its own temp folder; ffmpeg runs there so filter graphs only use relative names.
public sealed class ProxyRenderer : IDisposable
{
    static readonly string[] FontCandidates = [@"C:\Windows\Fonts\consola.ttf", @"C:\Windows\Fonts\cour.ttf"];

    readonly Dictionary<ClipFormat, IRawBackend> backends = RawBackends.All.ToDictionary(b => b.Format);

    /// Tests only: other RAW backends (e.g. BRAW with a fake library search or a short --info timeout); they
    /// replace the default one of the same format — also the --gen5 BRAW backend the constructor makes for
    /// proxy.brawGen4ToGen5 (init runs after the constructor; M11 c): a test that injects BRAW passes its own `gen5`.
    internal IEnumerable<IRawBackend> Backends
    {
        get => backends.Values;
        init
        {
            backends = new Dictionary<ClipFormat, IRawBackend>(backends);
            foreach (var b in value) backends[b.Format] = b;
        }
    }
    readonly string workDir;
    /// The renderer's temp folder (intermediates, LUT copies) — for tests.
    internal string WorkDir => workDir;
    readonly bool hasFont;
    /// The LUT files already copied into the work folder (by their name there), and the ones that could not be.
    readonly HashSet<string> copiedLuts = new(StringComparer.Ordinal);
    readonly HashSet<string> failedLuts = new(StringComparer.Ordinal);

    public ProxySettings Settings { get; }
    public ExternalTools Tools { get; }

    /// A proxy render with no new frame (and no data piped into ffmpeg) for this long is stuck (ffmpeg 8.1 with
    /// -hwaccel auto on FFV1 hung forever on VK_ERROR_DEVICE_LOST): ffmpeg is killed and, when it decoded in hardware,
    /// the clip is retried once in software. Until the first sign of progress it gets 3× as long (input probing,
    /// a RAW decoder starting up, the encoder's lookahead).
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    /// Tests shorten it.
    internal TimeSpan StallTimeout { get; init; } = DefaultStallTimeout;

    /// How long a killed ffmpeg is waited for: a process stuck in a GPU driver may never end — the job must go on.
    internal static readonly TimeSpan KillWait = TimeSpan.FromSeconds(10);

    /// Where retries and unkillable processes are reported (the job's log).
    internal Action<string>? Log { get; init; }

    string? encoderName;

    /// Tests only: H.264/HEVC encoder to use instead of the probed one (OffloadOptions.ProxyEncoder).
    internal string? ForcedEncoder { get; init; }

    /// Paths up to this length go to ffmpeg (and a RAW decoder) as they are; longer ones never do (MAX_PATH, 260).
    const int ShortPath = 240;

    /// Where the render writes before its final name: `.<name>.part` beside the proxy while that path is short; for a deep
    /// mirrored folder `.<8 hex>.mov.part` at the top of the `_PROXY` above it (same volume, so the move is a rename);
    /// when even that is too long, the renderer's work folder (then copied onto the destination).
    internal static string PartFor(string output, string workDir)
    {
        var dir = Path.GetDirectoryName(output)!;
        var beside = Path.Combine(dir, "." + Path.GetFileName(output) + ".part");
        if (beside.Length <= ShortPath) return beside;
        var temp = "." + Guid.NewGuid().ToString("N")[..8] + ".mov.part";
        for (var d = dir; d != null; d = Path.GetDirectoryName(d))
        {
            if (!string.Equals(Path.GetFileName(d), Layout.ProxyDirName, StringComparison.OrdinalIgnoreCase)) continue;
            var top = Path.Combine(d, temp);
            if (top.Length <= ShortPath) return top;
            break;
        }
        return Path.Combine(workDir, temp[1..]);
    }

    /// The video encoder proxies are rendered with (probed once, on first use); null without ffmpeg.
    public string? EncoderName => Tools.Ffmpeg is string ffmpeg
        ? encoderName ??= (ForcedEncoder is string forced && Settings.Codec is ProxyCodec.H264 or ProxyCodec.Hevc ? forced : Encoders.Pick(ffmpeg, Settings.Codec))
        : null;

    public ProxyRenderer(ProxySettings settings, ExternalTools tools)
    {
        Settings = settings;
        Tools = tools;
        // Spec 4.2: Film Gen 4 BRAW decoded as Gen 5 (braw-decode --gen5) when switched on; its warnings go to the job's
        // log (Log is read when one is written, after init).
        if (settings.BrawGen4ToGen5) backends[ClipFormat.Braw] = new BrawBackend(gen5: true) { Log = m => Log?.Invoke(m) };
        backends[ClipFormat.Arriraw] = new ArriBackend { Log = m => Log?.Invoke(m) };   // a full metadata export is logged
        workDir = Path.Combine(Path.GetTempPath(), "offload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workDir);
        try
        {
            // LUTs are copied lazily, per clip and once each (PrepareLut): built-in ones, the custom one, own rules' ones.
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
        if (Tools.Ffmpeg == null) return Msg.FfmpegNotFound;
        return clip.Format switch
        {
            ClipFormat.Video => null,
            ClipFormat.Unknown => Msg.UnknownFormat,
            _ => backends.TryGetValue(clip.Format, out var b)
                ? (b.IsAvailable(Tools) ? null : b.MissingHint)
                : Msg.NoDecoder(clip.Format.Label()),
        };
    }

    /// How long ffprobe may take on an MXF during the analysis: an ARRIRAW MXF (100+ GB) must never hold it — after the
    /// limit the ARRI tool is asked instead (Analyze). Tests shorten it.
    internal TimeSpan MxfProbeTimeout { get; init; } = TimeSpan.FromSeconds(60);

    static bool IsMxf(Clip clip) => clip.Path.EndsWith(".mxf", StringComparison.OrdinalIgnoreCase);

    /// The clip analysis (stage 2): ffprobe — an MXF with a time limit — and, for an MXF ffmpeg cannot decode, the ARRI
    /// tool, which then reclassifies it as ARRIRAW. Each tool looks at a clip once (ARRI's frame count comes from
    /// ffprobe's answer, not from a second probe). The clip may come back reclassified; a video clip whose info has no
    /// width is one ffmpeg cannot decode.
    public ClipAnalysis Analyze(Clip clip, string cardPath)
    {
        if (clip.Format != ClipFormat.Video || !IsMxf(clip)) return new(clip, Probe(clip, cardPath));
        ClipInfo? probed = null;
        ToolError? failed = null;
        try
        {
            probed = Probe(clip, cardPath, MxfProbeTimeout);
        }
        catch (ToolError e) when (e.Status == -1 && backends[ClipFormat.Arriraw].IsAvailable(Tools))
        {
            failed = e;   // timed out (or could not run): perhaps ARRIRAW — the ARRI tool decides
        }
        if (probed?.Width != null) return new(clip, probed);
        if (ReclassifyUndecodableMxf(clip, cardPath, probed, out var arriError) is { } arri) return new(arri.Clip, arri.Info);
        if (failed != null) throw failed;
        return new(clip, probed!, arriError != null ? UndecodableNote(arriError) : null);
    }

    /// The note of an MXF that neither ffmpeg nor art-cmd could read: art-cmd's own words (its error line, or that it
    /// timed out) — the clip may be ARRIRAW that art-cmd failed on, so never a guess at another codec.
    internal static string UndecodableNote(Exception arriError)
    {
        var why = arriError is ToolError t ? t.Detail : arriError.Message;
        why = string.Join(" | ", why.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (why.Length > 300) why = why[..300] + "…";
        return $"kodek nepodporován ffmpegem a art-cmd (ARRIRAW) ho nepřečetl: {why} — zkopírováno a ověřeno, bez proxy";
    }

    public ClipInfo Probe(Clip clip, string cardPath) => Probe(clip, cardPath, ffprobeTimeout: null);

    ClipInfo Probe(Clip clip, string cardPath, TimeSpan? ffprobeTimeout)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
        {
            var ffprobe = Tools.Ffprobe ?? throw new ToolError("ffprobe", -1, Msg.FfprobeNotFound);
            var info = ffprobeTimeout is TimeSpan limit ? FFProbe.Probe(ffprobe, path, limit) : FFProbe.Probe(ffprobe, path);
            info.CameraSerial ??= SonyXml.Serial(path);   // Sony keeps it in the XML next to the clip (spec 4.2)
            return info;
        }
        if (!backends.TryGetValue(clip.Format, out var b) || !b.IsAvailable(Tools))
            throw new ToolError(clip.Format.Label(), -1, UnsupportedReason(clip) ?? "");
        // An .ari sequence: its frames are the files the scanner found.
        return Logged(() => b is ArriBackend arri ? arri.Probe(path, Tools, workDir, container: null, clip.Segments.Count) : b.Probe(path, Tools, workDir));
    }

    /// A decoder error turned into a note for the clip (RawDecoderException): the decoder's own words — its stderr,
    /// e.g. the library it loaded — go to the job's log, the note to the clip.
    T Logged<T>(Func<T> decode)
    {
        try
        {
            return decode();
        }
        catch (RawDecoderException e)
        {
            Log?.Invoke(e.DecoderError.Message);
            throw;
        }
    }

    /// MXF is a container for many codecs; ARRIRAW/ARRICORE inside it only the ARRI tool can read. `container`: what
    /// ffprobe said about the file (its duration gives the frame count), null when it did not answer.
    public (Clip Clip, ClipInfo Info)? ReclassifyUndecodableMxf(Clip clip, string cardPath, ClipInfo? container = null) =>
        ReclassifyUndecodableMxf(clip, cardPath, container, out _);

    /// `error`: why the ARRI tool could not read the clip (logged too); null when it was not asked or found no picture.
    internal (Clip Clip, ClipInfo Info)? ReclassifyUndecodableMxf(Clip clip, string cardPath, ClipInfo? container, out Exception? error)
    {
        error = null;
        if (clip.Format != ClipFormat.Video || !IsMxf(clip)) return null;
        var arri = backends[ClipFormat.Arriraw];
        if (!arri.IsAvailable(Tools)) return null;
        try
        {
            var path = Layout.Join(cardPath, clip.Path);
            var info = arri is ArriBackend a ? a.Probe(path, Tools, workDir, container) : arri.Probe(path, Tools, workDir);
            if (info.Width == null) return null;
            return (new Clip { Name = clip.Name, Path = clip.Path, Format = ClipFormat.Arriraw, Segments = clip.Segments, Bytes = clip.Bytes, IsCameraProxy = clip.IsCameraProxy }, info);
        }
        catch (Exception e)
        {
            // Not ARRI as far as can be told (a tool error, a time limit, malformed metadata JSON …): the clip is copied and
            // verified without a proxy; why goes to the log and, through Analyze, to the clip's note.
            error = e;
            Log?.Invoke($"ARRI check {clip.Name}: {e.Message}");
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

    /// Hardware decoding (when proxy.hardwareDecode is on) only where it helps and is safe: H.264 and HEVC. Everything
    /// else (FFV1, ProRes, MJPEG, ...) decodes in software: ffmpeg 8.1 with -hwaccel auto hung forever on FFV1 (Vulkan
    /// VK_ERROR_DEVICE_LOST).
    internal static bool UseHardwareDecoding(string? codec) =>
        codec != null && (codec.StartsWith("h264", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("hevc", StringComparison.OrdinalIgnoreCase));

    /// ffmpeg reads the tap (plan 10, mode A) with this input option: a forward jump over more than 64 KB is a new range
    /// request, not a read-through. Without it ffmpeg 8.1 read a moov-at-end MOV and an MXF twice over HTTP (it skipped
    /// the whole mdat by reading it: 200 % of the file); with it 100 % + the few header bytes (plan 10 measurements).
    internal static readonly string[] TapInputArgs = ["-short_seek_size", "65536"];

    FrameSource SourceFor(Clip clip, string cardPath, ClipInfo info, RawLook look, double? singleFrameAt, bool hwaccel, string? inputUrl = null,
        Func<bool>? isCancelled = null, Action<string>? intermediate = null)
    {
        var path = Layout.Join(cardPath, clip.Path);
        if (clip.Format == ClipFormat.Video)
        {
            string[] input = inputUrl != null ? [.. TapInputArgs, "-i", inputUrl] : ["-i", path];
            return new FrameSource
            {
                InputArgs = hwaccel ? ["-hwaccel", "auto", .. input] : input,
                InputActivity = inputUrl != null ? VerifyInProxy.RangeServer.ActivityOf(inputUrl) : null,
            };
        }
        if (!backends.TryGetValue(clip.Format, out var b)) throw new ToolError(clip.Format.Label(), -1, Msg.NoDecoderShort);
        return Logged(() => b is ArriBackend arri && intermediate != null
            ? arri.Source(path, info, Settings, look, Tools, workDir, singleFrameAt, isCancelled, intermediate)
            : b.Source(path, info, Settings, look, Tools, workDir, singleFrameAt, isCancelled));
    }

    /// The .cube for this clip by its name in the work folder, or null for no LUT. Copied on first use, once per file:
    /// a built-in one as `lut-<id>.cube`, an own one (the custom LUT, a rule's) as `lut-custom-<8 hex of SHA-1 of its
    /// path>.cube` — two rules with two .cube files in one job each get their own (spec 4.2). A file that cannot be
    /// read (gone since the check before the start) means no LUT, logged once.
    internal string? PrepareLut(LutUse? lut)
    {
        if (lut?.File is not string file) return null;
        var name = lut.IsBuiltin
            ? $"lut-{lut.Id}.cube"
            : $"lut-custom-{Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant())))[..8]}.cube";
        if (copiedLuts.Contains(name)) return name;
        if (failedLuts.Contains(name)) return null;
        try
        {
            File.Copy(file, Path.Combine(workDir, name), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failedLuts.Add(name);
            Log?.Invoke($"LUT {file}: {e.Message} — no LUT");
            return null;
        }
        copiedLuts.Add(name);
        return name;
    }

    /// Plan 8: a clip whose own LUT (the custom one, or a rule's) cannot be read renders without it — the proxy says so
    /// (null otherwise; built-in LUTs ship with the app).
    public string? LutNote(LutUse? lut) =>
        lut is { IsBuiltin: false, File: string file } && PrepareLut(lut) == null ? Msg.ProxyWithoutLut(Path.GetFileName(file)) : null;

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
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, Msg.FfmpegNotFound);
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
                src = SourceFor(clip, cardPath, info, look, pos.Seconds, hwaccel: false, isCancelled: isCancelled);
                var vf = FilterGraph.Thumbnail(lutFile, NeedsPreMap(lut, src, info));  // the pre-map depends on the source
                bool video = clip.Format == ClipFormat.Video;
                var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
                if (src.FeederExe == null) args.Add("-nostdin");
                if (video) args.AddRange(["-ss", SeekSeconds(info, pos)]);
                args.AddRange(src.InputArgs);
                // Last frame: decode the final second and keep overwriting the image — the last write is the last frame.
                args.AddRange(video && pos.FromEnd ? ["-map", "0:v:0", "-vf", vf, "-update", "1"] : ["-frames:v", "1", "-vf", vf]);
                args.AddRange(["-q:v", "5", output]);
                // A RAW decoder piped in (BRAW) runs under the stall watchdog: data it hands ffmpeg is the progress, and a
                // hung SDK must not hang the job. (A file input prints no progress here, so it is left alone.)
                RunFfmpeg(ffmpeg, args, src, null, null, isCancelled, src.FeederExe != null ? StallTimeout : null);
                if (!File.Exists(output)) throw new ToolError("ffmpeg", 0, "no frame");
                result.Add(new Thumbnail(File.ReadAllBytes(output), Label(info, pos)));
            }
            catch (FfmpegStalledException e) when (src?.FeederExe != null)
            {
                if (isCancelled()) return [];
                // A hung decoder would hang at the next positions too (3 x the grace per clip): stop here.
                lastError = new FfmpegStalledException("dekodér nebo " + e.Message, e);
                Log?.Invoke($"thumbnails {clip.Name}: {lastError.Message}; the remaining frames are skipped");
                break;
            }
            catch (ToolError e) when (src == null && e.Status == -1 && IsBrawDecoder(e.Tool))
            {
                if (isCancelled()) return [];
                // `braw-decode --info` timed out (or could not run at all): it would at the next positions too.
                lastError = e;
                Log?.Invoke($"thumbnails {clip.Name}: {e.Message}; the remaining frames are skipped");
                break;
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

    /// Spec 9 "náhled místa vady": the picture near `frame` of a video clip as a small JPEG (read from its file on
    /// `cardPath`); null when ffmpeg gives none — a damaged place may not decode at all.
    public byte[]? Snapshot(Clip clip, string cardPath, ClipInfo info, long frame)
    {
        if (Tools.Ffmpeg is not string ffmpeg || clip.Format != ClipFormat.Video) return null;
        var output = Path.Combine(workDir, $"defect-{Guid.NewGuid():N}.jpg");
        try
        {
            double seconds = Timecode.Fps(info.FrameRate) is double fps && fps > 0 ? frame / fps : 0;
            var at = Math.Max(0, seconds - 0.5).ToString("0.000000", CultureInfo.InvariantCulture);
            ToolProcess.Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-ss", at, "-i", Layout.Join(cardPath, clip.Path),
                "-frames:v", "1", "-vf", "scale=-2:180", "-q:v", "5", output], workDir, TimeSpan.FromSeconds(60));
            return File.Exists(output) ? File.ReadAllBytes(output) : null;
        }
        catch (Exception e) when (e is ToolError or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            NativeIO.Delete(output);
        }
    }

    /// The tool name of a ToolError raised by braw-decode (its file name without the extension).
    bool IsBrawDecoder(string tool) =>
        Tools.BrawDecoder is string exe && string.Equals(tool, Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase);

    /// Renders the proxy. A render that stalls (no progress for StallTimeout) is killed. With hardware decoding the
    /// clip is then retried once in software — as is one that failed with it; without, the stall fails the proxy.
    /// `inputUrl` (plan 10, mode A): a video clip is read from the tap (RangeServer) instead of from its file. `content`
    /// (spec 7, R9): watches the decoder — ffmpeg then logs at warning level.
    public void Render(Clip clip, string cardPath, ClipInfo info, LutUse? lut, string output, Action<double> progress, Func<bool> isCancelled,
        string? inputUrl = null, ContentWatch? content = null)
    {
        var ffmpeg = Tools.Ffmpeg ?? throw new ToolError("ffmpeg", -1, Msg.FfmpegNotFound);
        var lutFile = PrepareLut(lut);
        // Hardware decoding only when switched on (proxy.hardwareDecode, off by default: green frames on the Mac).
        bool hwaccel = Settings.HardwareDecode && clip.Format == ClipFormat.Video && UseHardwareDecoding(info.Codec);
        Exception first;
        try
        {
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel, inputUrl, content);
            return;
        }
        catch (FfmpegStalledException e) when (!hwaccel && !isCancelled())
        {
            throw new FfmpegStalledException(e.Message + "; proxy nevznikla", e);
        }
        catch (Exception e) when (hwaccel && e is FfmpegStalledException or ToolError && !isCancelled())
        {
            first = e;  // hardware decoding stalled or failed: once more in software
        }
        if (isCancelled()) throw new ToolError("ffmpeg", -1, "cancelled");
        Log?.Invoke($"proxy {clip.Name}: with hardware decoding: {first.Message} — retrying in software");
        content?.Reset();   // what the hardware decoder said is not the file's verdict
        try
        {
            RenderOnce(ffmpeg, clip, cardPath, info, lut, lutFile, output, progress, isCancelled, hwaccel: false, inputUrl, content);
            Log?.Invoke($"proxy {clip.Name}: software decoding succeeded");
        }
        catch (Exception e) when (e is FfmpegStalledException or ToolError && !isCancelled())
        {
            Log?.Invoke($"proxy {clip.Name}: software decoding failed too: {e.Message}");
            throw (first, e) switch
            {
                (FfmpegStalledException, FfmpegStalledException) => new ProxyRetryException(StallNote(StallTimeout, retriedInSoftware: true), e),
                (FfmpegStalledException, _) => new ProxyRetryException($"{first.Message}; bez hardwarového dekódování selhal: {e.Message}", e),
                _ => new ProxyRetryException($"{e.Message}; s hardwarovým dekódováním předtím: {first.Message}", e),
            };
        }
    }

    internal static string StallNote(TimeSpan after, bool retriedInSoftware) =>
        $"ffmpeg se zasekl ({after.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s bez nového snímku)"
        + (retriedInSoftware ? " i bez hardwarového dekódování; proxy nevznikla" : "");

    /// How often the size of the ARRI intermediate is looked at (IntermediateProgress); tests shorten it.
    internal TimeSpan IntermediatePoll { get; init; } = TimeSpan.FromSeconds(1);

    void RenderOnce(string ffmpeg, Clip clip, string cardPath, ClipInfo info, LutUse? lut, string? lutFile, string output,
        Action<double> progress, Func<bool> isCancelled, bool hwaccel, string? inputUrl, ContentWatch? content)
    {
        FrameSource src;
        if (clip.Format == ClipFormat.Arriraw)
        {
            // ARRIRAW: art-cmd writes the whole ProRes LT intermediate first and reports no progress itself — its growing
            // size is 0–80 % of the clip, ffmpeg's own progress the rest (the bar never sits at 0 % for the whole decode).
            using (var estimate = IntermediateProgress.Start(info, Settings, progress, IntermediatePoll))
                src = SourceFor(clip, cardPath, info, LookFor(lutFile != null ? lut : null, info), null, hwaccel, inputUrl, isCancelled, estimate == null ? null : estimate.Watch);
            var whole = progress;
            progress = f => whole(IntermediateProgress.Share + (1 - IntermediateProgress.Share) * f);
        }
        else src = SourceFor(clip, cardPath, info, LookFor(lutFile != null ? lut : null, info), null, hwaccel, inputUrl, isCancelled);
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

            // Warning level with the content check: "corrupt decoded frame" is a warning in ffmpeg 8.1 (plan 10 measurement).
            var args = new List<string> { "-hide_banner", "-loglevel", content != null ? "warning" : "error", "-y", "-nostats", "-progress", "pipe:1" };
            if (src.FeederExe == null) args.Add("-nostdin");
            args.AddRange(src.InputArgs);
            args.AddRange(["-map", "0:v:0"]);
            if (Settings.Audio && info.AudioChannels > 0 && src.AudioFromInput)
            {
                args.AddRange(["-map", "0:a?"]);   // every audio stream (R14)
                args.AddRange(Encoders.AudioArgs(Settings.Codec, info.AudioStreams));
            }
            args.AddRange(["-vf", vf]);
            args.AddRange(Encoders.VideoArgs(EncoderName!, Settings.Codec, Settings.BitrateMbps));
            args.AddRange(Encoders.Rec709Args);
            if (Timecode.IsValid(info.Timecode))
            {
                args.AddRange(["-timecode", info.Timecode!]);
                // The MOV muxer writes it into the timecode track (tmcd) as its source reference: Premiere reads it as
                // Tape Name, Resolve as Reel (ffprobe shows `reel_name` on the data stream). Without TC there is no tmcd.
                if (info.Reel is { Length: > 0 } reel) args.AddRange(["-metadata:s:v:0", $"reel_name={reel}"]);
            }

            NativeIO.Mkdirs(Path.GetDirectoryName(output)!);
            // Short for ffmpeg at any depth of the card's folders (review: MAX_PATH); the final name is never taken from
            // an existing proxy (spec 2.2) — the render fails instead.
            var part = PartFor(output, workDir);
            args.AddRange(["-f", "mov", part]);
            try
            {
                RunFfmpeg(ffmpeg, args, src, info.Duration, progress, isCancelled, StallTimeout, content);
                if (part.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (Path.Exists(output)) throw new IOError("rename", output, 80 /* ERROR_FILE_EXISTS */);
                    ProxyCopy.CopyFile(part, output, replace: false);   // another volume: via `.part` beside it, then renamed
                    NativeIO.Delete(part);
                }
                else NativeIO.Move(part, output, replace: false);
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

    /// `stallAfter` (with -progress pipe:1 in the args): ffmpeg is killed and FfmpegStalledException thrown when it has
    /// reported no new frame or output time and taken no data from the feeder for that long (a stuck ffmpeg keeps
    /// repeating the same progress block); 3× as long before the first sign of progress.
    internal void RunFfmpeg(string ffmpeg, List<string> args, FrameSource src, double? duration, Action<double>? progress, Func<bool> isCancelled,
        TimeSpan? stallAfter = null, ContentWatch? content = null)
    {
        using var ff = new ToolProcess(ffmpeg, args, workDir, pipeStdin: src.FeederExe != null) { StderrLine = content == null ? null : content.Stderr };
        using var feeder = src.FeederExe != null ? new ToolProcess(src.FeederExe, src.FeederArgs, workDir, pipeStdin: false) : null;
        Thread? pump = null;
        Stream? input = null, sink = null;
        bool feederKilled = false;
        // Set by the pump when ffmpeg stopped taking data and the feeder's stdout was closed under it.
        int feederCut = 0;
        var watch = new ProgressWatch();
        long lastAdvance = Environment.TickCount64;
        int advanced = 0;
        void Advance()
        {
            Interlocked.Exchange(ref lastAdvance, Environment.TickCount64);
            Volatile.Write(ref advanced, 1);
        }
        try
        {
            feeder?.Start(null);
            ff.Start(line =>
            {
                content?.Progress(line);
                if (watch.Advanced(line)) Advance();
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
                    try
                    {
                        // Every chunk ffmpeg takes is progress for the stall watchdog (a slow RAW decoder is not stuck).
                        var buffer = new byte[4 << 20];
                        int n;
                        while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            try
                            {
                                to.Write(buffer, 0, n);
                            }
                            catch (Exception)
                            {
                                // ffmpeg went away first (a thumbnail needs one frame, or it failed). Marked before the
                                // feeder's end is closed below, so its exit code is known to be a consequence.
                                Volatile.Write(ref feederCut, 1);
                                break;
                            }
                            Advance();
                        }
                    }
                    catch (Exception) { }
                    finally
                    {
                        try { to.Close(); } catch (Exception) { }
                        // Close the decoder's end too, so a decoder still writing sees the broken pipe and stops by itself
                        // (braw-decode exits 0) instead of blocking on a full pipe until it is killed.
                        try { from.Close(); } catch (Exception) { }
                    }
                }) { IsBackground = true };
                pump.Start();
            }
            long inputSeen = src.InputActivity?.Invoke() ?? 0;
            while (!ff.WaitForExit(200))
            {
                // Plan 10 mode A: the tap still reading the destination for ffmpeg (a slow disk, a piece of a block at a
                // time) is a sign of life; it does not end the 3x grace before the first frame. A hung disk shows none.
                if (src.InputActivity?.Invoke() is long tapActivity && tapActivity != inputSeen)
                {
                    inputSeen = tapActivity;
                    Interlocked.Exchange(ref lastAdvance, Environment.TickCount64);
                }
                bool cancelled = isCancelled();
                bool stalled = !cancelled && stallAfter is TimeSpan limit
                    && Environment.TickCount64 - Interlocked.Read(ref lastAdvance) > limit.TotalMilliseconds * (Volatile.Read(ref advanced) == 1 ? 1 : 3);
                if (!cancelled && !stalled) continue;
                ff.Kill();
                feeder?.Kill();
                feederKilled = true;
                // Bounded: a process stuck in a GPU driver may survive the kill; the job goes on without it.
                if (!ff.WaitForExit((int)KillWait.TotalMilliseconds)) Log?.Invoke($"ffmpeg (pid {ff.Id}) did not end {KillWait.TotalSeconds:0} s after kill");
                pump?.Join(5000);
                if (stalled) throw new FfmpegStalledException(StallNote(stallAfter!.Value, retriedInSoftware: false));
                throw new ToolError("ffmpeg", -1, "cancelled");
            }
            ff.WaitForExit();
            ff.DrainStderr();
            if (feeder != null)
            {
                if (!feeder.WaitForExit(2000)) { feeder.Kill(); feederKilled = true; }
                else feeder.DrainStderr();   // the decoder's last line must be in StderrTail before it becomes the error
                pump!.Join(2000);
            }
            // A feeder that died on its own is the real cause; ffmpeg only saw a truncated stream. One that was killed,
            // or whose stdout the pump closed because ffmpeg had stopped reading, is not: ffmpeg's exit code decides.
            if (feeder != null && !feederKilled && Volatile.Read(ref feederCut) == 0 && feeder.WaitForExit(0) && feeder.ExitCode != 0)
            {
                var error = new ToolError(feeder.Name, feeder.ExitCode, feeder.StderrTail);
                var explained = src.ExplainFeederError?.Invoke(error) ?? error;
                if (explained is RawDecoderException) Log?.Invoke(error.Message);
                throw explained;
            }
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

/// What the clip analysis found: the clip (an MXF may come back as ARRIRAW), its info, and — for an MXF ffmpeg cannot
/// decode whose ARRI check failed — why, as the clip's note (null: the generic "kodek nepodporován" one).
public sealed record ClipAnalysis(Clip Clip, ClipInfo Info, string? UndecodableNote = null)
{
    public void Deconstruct(out Clip clip, out ClipInfo info)
    {
        clip = Clip;
        info = Info;
    }
}

/// Progress while art-cmd writes the ARRIRAW intermediate, which it does not report itself: the size of this clip's
/// intermediate (Watch — never one an earlier clip left in the work folder) against the size it should reach — frames ×
/// output pixels × 0.21 bytes, about ProRes 422 LT (≈ 100 Mb/s at 1080p 29.97) — as 0–80 % of the clip. An estimate, so
/// never more than 95 % of that share.
internal sealed class IntermediateProgress : IDisposable
{
    public const double Share = 0.8;
    const double BytesPerPixel = 0.21;

    readonly double expected;
    readonly Action<double> progress;
    readonly Timer timer;
    readonly Lock gate = new();
    string? file;
    bool stopped;
    double reported;

    IntermediateProgress(double expected, Action<double> progress, TimeSpan interval)
    {
        this.expected = expected;
        this.progress = progress;
        timer = new Timer(_ => Poll(), null, interval, interval);
    }

    /// The intermediate's expected size in bytes, or null when the clip's frames or size are not known (or make no
    /// sense: a width or aspect of 0 would give 0 bytes and a NaN fraction).
    internal static double? ExpectedBytes(ClipInfo info, ProxySettings s)
    {
        long? frames = info.Frames is int known ? known
            : info.Duration is double d && Timecode.Fps(info.FrameRate) is double fps ? (long)Math.Round(d * fps) : null;
        if (frames is not long n || n <= 0 || info.Width is not int w || w <= 0 || info.Height is not int h || h <= 0 || s.Height <= 0) return null;
        double outH = s.Height;
        double aspect = s.FrameAspect ?? w * (info.Squeeze ?? 1) / h;
        double expected = n * outH * (outH * aspect) * BytesPerPixel;
        return double.IsFinite(expected) && expected > 0 ? expected : null;
    }

    /// Null (nothing to report) when the expected size cannot be told.
    public static IntermediateProgress? Start(ClipInfo info, ProxySettings s, Action<double> progress, TimeSpan interval) =>
        ExpectedBytes(info, s) is double expected ? new IntermediateProgress(expected, progress, interval) : null;

    /// The intermediate art-cmd is about to write for this clip; nothing is reported before.
    public void Watch(string intermediate)
    {
        lock (gate) file = intermediate;
    }

    void Poll()
    {
        // Never an exception on the timer's thread: a folder or file going away just means "no new size".
        try
        {
            string? watched;
            lock (gate) watched = file;
            if (watched == null) return;
            var info = new FileInfo(watched);
            long size = info.Exists ? info.Length : 0;
            double fraction = Math.Min(0.95, size / expected) * Share;
            lock (gate)
            {
                if (stopped || fraction <= reported) return;
                reported = fraction;
                progress(fraction);
            }
        }
        catch (Exception)
        {
        }
    }

    /// No report after this returns (a poll in progress finishes first).
    public void Dispose()
    {
        lock (gate) stopped = true;
        timer.Dispose();
    }
}

/// ffmpeg reported no new frame for too long and was killed. The message is the note shown at the clip.
internal sealed class FfmpegStalledException(string message, Exception? inner = null) : Exception(message, inner);

/// The hardware-decoded attempt and the software retry both failed; the message (the note at the clip) names both.
internal sealed class ProxyRetryException(string message, Exception inner) : Exception(message, inner);

/// Watches ffmpeg's -progress output: only a `frame=` or `out_time_us=` value above any seen before (or the final
/// `progress=end`) is progress — a stuck ffmpeg keeps printing the same block every half second, and the opening
/// "frame=0", "out_time_us=N/A" or negative times say nothing has been written yet.
internal sealed class ProgressWatch
{
    long frame, outTime;

    public bool Advanced(string line)
    {
        if (line.StartsWith("frame=", StringComparison.Ordinal)) return Rose(ref frame, line["frame=".Length..]);
        if (line.StartsWith("out_time_us=", StringComparison.Ordinal)) return Rose(ref outTime, line["out_time_us=".Length..]);
        return line == "progress=end";
    }

    static bool Rose(ref long max, string value)
    {
        if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v <= max) return false;
        max = v;
        return true;
    }
}
