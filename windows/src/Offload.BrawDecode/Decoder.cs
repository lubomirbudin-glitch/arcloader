using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Offload.Core;

namespace Offload.BrawDecode;

/// braw-decode itself: the C++ Mac tool's flow on the Windows COM API. Exit codes as on the Mac:
/// 0 OK (also when the reader closed the pipe), 2 usage, 3 library/codec missing, 4 clip cannot be opened,
/// 5 a frame failed to decode. A closed stdout (ffmpeg has the frames it wanted, e.g. a thumbnail) stops decoding
/// at once and is a success: Offload treats a feeder that ends non-zero on its own as the job's error.
/// Every exit-3/4 line names the library in use (BrawRuntime's errors already do): an old one cannot open clips
/// from newer cameras, and Offload's note at the clip says so.
internal static class Decoder
{
    public const int Ok = 0, Usage = 2, NoLibrary = 3, CannotOpen = 4, FrameFailed = 5;

    /// Internal outcome of Info/Frames: the reader closed stdout. Run turns it into Ok, whatever else happened.
    const int ReaderGone = -1;

    /// The one gamma --gen5 converts (the SDK's string; Offload's LUT rule compares the same one).
    const string FilmGamma = "Blackmagic Design Film";

    /// Frames decoded in parallel by the SDK and then written in order.
    const int Batch = 8;

    /// `libraryDirs` null = BrawLibrary.SearchDirs next to this exe (tests pass their own).
    public static int Run(IReadOnlyList<string> args, Stream stdout, TextWriter stderr, IReadOnlyList<string>? libraryDirs = null)
    {
        var o = Options.Parse(args);
        if (o == null)
        {
            Say(stderr, Options.Usage);
            return Usage;
        }
        var dll = BrawLibrary.Find(libraryDirs ?? BrawLibrary.SearchDirs(AppContext.BaseDirectory.TrimEnd('\\')));
        if (dll == null)
        {
            Say(stderr, "braw-decode: BlackmagicRawAPI.dll not found (install Blackmagic RAW Player, the Blackmagic RAW SDK or DaVinci Resolve: " + Offload.Core.ToolDownloads.BlackmagicRaw + ")");
            return NoLibrary;
        }
        if (BrawRuntime.CreateFactory(dll, out var error) is not BrawFactory factory)
        {
            Say(stderr, $"braw-decode: {error}");
            return NoLibrary;
        }

        var sink = new FrameSink(o.Scale, stderr);
        // Declared before the try: disposed after the finally below has taken the SDK's reference back.
        using var callback = new BrawCallback(sink, line => Say(stderr, line));
        BrawCodec codec = default;
        BrawClip clip = default;
        BrawClipProcessingAttributes gen5 = default;
        try
        {
            int hr = factory.CreateCodec(out codec);
            if (hr != Com.S_OK)
            {
                Say(stderr, $"braw-decode: CreateCodec failed ({Com.Hex(hr)}; library {dll})");
                return NoLibrary;
            }
            // The configuration is read on the first OpenClip, so the pipeline is set before it (CPU like the Mac).
            if (codec.QueryConfiguration(out var config) == Com.S_OK)
            {
                config.SetPipeline(FourCC.PipelineCpu);
                Com.Release(config.Ptr);
            }
            hr = codec.OpenClip(o.Path, out clip);
            if (hr != Com.S_OK)
            {
                Say(stderr, $"braw-decode: cannot open {o.Path} ({Com.Hex(hr)}; library {dll})");
                return CannotOpen;
            }
            hr = codec.SetCallback(callback.Pointer);
            if (hr != Com.S_OK)
            {
                Say(stderr, $"braw-decode: SetCallback failed ({Com.Hex(hr)}; library {dll})");
                return NoLibrary;
            }
            // --gen5 (spec 4.2): after OpenClip, before the first decode job — clone the clip's processing attributes,
            // set colour science Gen 5, read it back; every decode job then gets the clone.
            int? original = null;
            if (o.Gen5)
            {
                gen5 = Gen5Attributes(clip, stderr, out original);
                sink.ClipAttributes = gen5.Ptr;   // 0 = not converted: decoded as recorded
            }
            int code = o.Info ? Info(codec, clip, sink, stdout, stderr, gen5, original) : Frames(o, codec, clip, sink, stdout, stderr);
            if (code == ReaderGone) return Ok;   // like the Mac's _exit(0) on a failed write
            return code != Ok ? code : sink.Errors > 0 ? FrameFailed : Ok;
        }
        finally
        {
            // No job may still be running (and calling back) when the clip, the codec and the callback go away;
            // SetCallback(0) gives the SDK's reference to the callback back before `callback` is disposed.
            if (codec.Ptr != 0) codec.FlushJobs();
            Com.Release(gen5.Ptr);   // the --gen5 clone: after the last job, before the clip
            Com.Release(clip.Ptr);
            if (codec.Ptr != 0)
            {
                codec.SetCallback(0);
                Com.Release(codec.Ptr);
            }
            Com.Release(factory.Ptr);
        }
    }

    /// --gen5: a clone of the clip's processing attributes set to colour science Gen 5 — or default (Ptr 0) when the clip
    /// is Gen 5 already, not in Film gamma, or the SDK refuses (a warning on stderr; then it is decoded as recorded, without a LUT as before).
    /// `original`: the generation the clip was recorded in, when the SDK says. A clone that is not used is released here
    /// (in a finally, whatever happens); the used one by Run's finally.
    internal static BrawClipProcessingAttributes Gen5Attributes(BrawClip clip, TextWriter stderr, out int? original)
    {
        original = null;
        if (clip.CloneClipProcessingAttributes(out var attrs) != Com.S_OK || attrs.Ptr == 0)
        {
            Com.Release(attrs.Ptr);
            Say(stderr, "braw-decode: --gen5: the clip's processing attributes cannot be read — decoding as recorded");
            return default;
        }
        bool use = false;
        try
        {
            use = ToGen5(attrs, stderr, out original);
            return use ? attrs : default;
        }
        finally
        {
            if (!use) Com.Release(attrs.Ptr);
        }
    }

    /// Sets colour science Gen 5 on `attrs` and reads it back (spec 4.2: SetClipAttribute(ColorScienceGen, u16 5)).
    /// Only Blackmagic Design Film is converted (spec 4.2: Film Gen 4 → Gen 5); Video / Extended Video are display-ready
    /// and stay as recorded, silently. False — nothing to use — when the generation is unknown, already 5 or more, the
    /// gamma is not Film, or the SDK does not take 5; an unknown generation or gamma and a refusal are warnings on stderr
    /// (ruling T6-attr: attribute trouble never fails the decoder).
    internal static bool ToGen5(BrawClipProcessingAttributes attrs, TextWriter stderr, out int? original)
    {
        original = attrs.Number(FourCC.ClipAttributeColorScienceGen, out var error);
        if (original is not int gen)
        {
            Say(stderr, $"braw-decode: --gen5: colour science generation unknown{(error != null ? $" ({error})" : "")} — decoding as recorded");
            return false;
        }
        if (gen >= 5) return false;
        var gamma = attrs.Text(FourCC.ClipAttributeGamma, out error);
        if (gamma == null)
        {
            Say(stderr, $"braw-decode: --gen5: gamma unknown{(error != null ? $" ({error})" : "")} — decoding as recorded");
            return false;
        }
        if (!gamma.Equals(FilmGamma, StringComparison.OrdinalIgnoreCase)) return false;
        int hr = attrs.SetNumber(FourCC.ClipAttributeColorScienceGen, 5);
        var now = attrs.Number(FourCC.ClipAttributeColorScienceGen, out _);
        if (hr == Com.S_OK && now == 5) return true;
        Say(stderr, $"braw-decode: --gen5: colour science Gen 5 not accepted ({Com.Hex(hr)}, reads {now?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}) — decoding as Gen {gen}");
        return false;
    }

    static int Info(BrawCodec codec, BrawClip clip, FrameSink sink, Stream stdout, TextWriter stderr, BrawClipProcessingAttributes gen5, int? original)
    {
        ulong frames = clip.FrameCount;
        var slots = new[] { new FrameSlot() };
        // Decode frame 0 at the requested scale: the processed size is the truth, not width / scale.
        if (frames > 0)
        {
            sink.Begin(slots, 0);
            Submit(clip, 0, sink);
            codec.FlushJobs();
        }
        (string? gamma, string? gamut, int? gen) = (null, null, null);
        if (gen5.Ptr != 0)
        {
            // Converted: the effective gamma / gamut / generation after the change (spec 4.2), plus the recorded one.
            (gamma, gamut, gen) = Colour(gen5, stderr);
        }
        else if (clip.CloneClipProcessingAttributes(out var attrs) == Com.S_OK && attrs.Ptr != 0)
        {
            try { (gamma, gamut, gen) = Colour(attrs, stderr); }
            finally { Com.Release(attrs.Ptr); }
        }
        var s = slots[0];
        var facts = new ClipFacts(
            s.Ok ? s.Width : clip.Width, s.Ok ? s.Height : clip.Height, clip.Width, clip.Height, clip.FrameRate, frames,
            clip.TimecodeForFrame(0) ?? "", clip.CameraType ?? "", gamma, gamut, gen, gen5.Ptr != 0 ? original : null, sink.SensorRate);
        var bytes = Encoding.UTF8.GetBytes(InfoJson.Format(facts));
        return Write(stdout, bytes, bytes.Length) ? Ok : Closed(stderr);
    }

    /// Gamma, gamut and colour science generation. Ruling T6-attr: an attribute that cannot be read is a warning on
    /// stderr and its key is left out of --info (the LUT rule falls back); it never fails --info.
    internal static (string? Gamma, string? Gamut, int? Gen) Colour(BrawClipProcessingAttributes attrs, TextWriter stderr)
    {
        string? Warn(string name, string? error)
        {
            if (error != null) Say(stderr, $"braw-decode: {name}: {error}");
            return null;
        }
        var gamma = attrs.Text(FourCC.ClipAttributeGamma, out var error) ?? Warn("gamma", error);
        var gamut = attrs.Text(FourCC.ClipAttributeGamut, out error) ?? Warn("gamut", error);
        var gen = attrs.Number(FourCC.ClipAttributeColorScienceGen, out error);
        if (gen == null) Warn("colorScienceGen", error);
        return (gamma, gamut, gen);
    }

    static int Frames(Options o, BrawCodec codec, BrawClip clip, FrameSink sink, Stream stdout, TextWriter stderr)
    {
        var (first, end) = o.Range(clip.FrameCount);
        var slots = Enumerable.Range(0, Batch).Select(_ => new FrameSlot()).ToArray();
        for (ulong b = first; b < end; b += Batch)
        {
            int n = (int)Math.Min((ulong)Batch, end - b);
            sink.Begin(slots, b);
            for (int i = 0; i < n; i++) Submit(clip, b + (ulong)i, sink);
            codec.FlushJobs();
            for (int i = 0; i < n; i++)
            {
                if (!slots[i].Ok)
                {
                    Say(stderr, $"braw-decode: frame {b + (ulong)i} failed");
                    return FrameFailed;
                }
                if (!Write(stdout, slots[i].Data, slots[i].Length)) return Closed(stderr);
            }
        }
        return Ok;
    }

    static void Submit(BrawClip clip, ulong index, FrameSink sink)
    {
        if (clip.CreateJobReadFrame(index, out var job) != Com.S_OK || job.Ptr == 0)
        {
            sink.Fail($"cannot read frame {index}");
            return;
        }
        job.SetUserData((nint)index);
        if (job.Submit() != Com.S_OK)
        {
            Com.Release(job.Ptr);
            sink.Fail($"cannot submit frame {index}");
        }
    }

    static int Closed(TextWriter stderr)
    {
        Say(stderr, "braw-decode: stdout closed by the reader, stopping");
        return ReaderGone;
    }

    /// false when the reader went away (broken pipe: ffmpeg exited). Needs a stdout that reports it — see BinaryStdout.
    internal static bool Write(Stream stdout, byte[] data, int length)
    {
        try
        {
            stdout.Write(data, 0, length);
            stdout.Flush();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// One line on stderr; SDK worker threads report in parallel, so every line goes through this lock.
    internal static void Say(TextWriter stderr, string line)
    {
        lock (stderr) stderr.WriteLine(line);
    }
}

/// Binary stdout as a FileStream over the raw handle (ruling M1). Not Console.OpenStandardOutput(): on Windows its
/// stream treats a broken pipe as success, so after ffmpeg exits the decoder would go on decoding for nobody.
/// Not Console.Out either (text, code page).
internal static partial class BinaryStdout
{
    const int StdOutputHandle = -11;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int which);

    /// This process's stdout; Stream.Null when it has none (started without one).
    public static Stream Open()
    {
        nint handle = GetStdHandle(StdOutputHandle);
        return handle == 0 || handle == -1 ? Stream.Null : Open(handle);
    }

    /// Unbuffered (every Write is one WriteFile) and the handle stays open: it is the process's, not ours.
    public static Stream Open(nint handle) => new FileStream(new SafeFileHandle(handle, ownsHandle: false), FileAccess.Write, bufferSize: 0);
}

/// Receives the SDK callbacks: read → set format/scale → decode+process job → copy into the frame's slot.
internal sealed unsafe class FrameSink(Scale scale, TextWriter stderr) : IBrawCallbackSink
{
    volatile FrameSlot[] slots = [];
    ulong first;
    int errors;

    /// --gen5: the processing attributes every decode job uses (0 = the clip's own). Set before the first job.
    public nint ClipAttributes { get; set; }

    public int Errors => Volatile.Read(ref errors);

    /// The rate frame 0 was captured at (task 2026-10-10): IBlackmagicRawFrame::GetSensorRate when frame 0 is read
    /// (--info reads it); null before that and when the SDK says 0 (unknown).
    public float? SensorRate => sensorRate > 0 ? sensorRate : null;
    volatile float sensorRate;

    /// Called between batches only (no jobs in flight). Clears every slot's Ok (the Mac's `s.ok = false`): a slot
    /// the SDK does not fill in this batch must not pass as decoded with the previous batch's frame.
    public void Begin(FrameSlot[] batch, ulong firstFrame)
    {
        foreach (var s in batch) s.Ok = false;
        first = firstFrame;
        slots = batch;   // volatile write after `first`: a callback that sees the batch sees its first frame
    }

    public void Fail(string message)
    {
        Interlocked.Increment(ref errors);
        Decoder.Say(stderr, $"braw-decode: {message}");
    }

    public void ReadComplete(nint readJob, int result, nint framePtr)
    {
        var job = new BrawJob(readJob);
        BrawJob decode = default;
        try
        {
            nint user = job.GetUserData();
            var frame = new BrawFrame(framePtr);
            if (result == Com.S_OK && user == 0 && framePtr != 0) sensorRate = frame.SensorRate;
            if (result == Com.S_OK) result = frame.SetResourceFormat(FourCC.ResourceFormatRgbU16);
            if (result == Com.S_OK) result = frame.SetResolutionScale(scale);
            if (result == Com.S_OK) result = frame.CreateJobDecodeAndProcessFrame(ClipAttributes, out decode);
            if (result == Com.S_OK)
            {
                decode.SetUserData(user);
                result = decode.Submit();
            }
            if (result != Com.S_OK)
            {
                Com.Release(decode.Ptr);
                Fail($"read/decode setup failed ({Com.Hex(result)})");
            }
        }
        finally
        {
            Com.Release(readJob);
        }
    }

    public void ProcessComplete(nint processJob, int result, nint imagePtr)
    {
        try
        {
            ulong index = (ulong)new BrawJob(processJob).GetUserData();
            var batch = slots;
            ulong firstIndex = first;
            if (result != Com.S_OK || index < firstIndex || index - firstIndex >= (ulong)batch.Length)
            {
                Fail($"process failed ({Com.Hex(result)})");
                return;
            }
            var img = new BrawProcessedImage(imagePtr);
            uint width = img.Width, height = img.Height;
            if (!batch[index - firstIndex].Fill(width, height, img.Resource, img.SizeBytes))
                Fail($"frame {index}: processed image smaller than {width}x{height} RGB48");
        }
        finally
        {
            Com.Release(processJob);
        }
    }
}
