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
            Say(stderr, "braw-decode: BlackmagicRawAPI.dll not found (install Blackmagic RAW Player, the Blackmagic RAW SDK or DaVinci Resolve)");
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
            int code = o.Info ? Info(codec, clip, sink, stdout, stderr) : Frames(o, codec, clip, sink, stdout, stderr);
            if (code == ReaderGone) return Ok;   // like the Mac's _exit(0) on a failed write
            return code != Ok ? code : sink.Errors > 0 ? FrameFailed : Ok;
        }
        finally
        {
            // No job may still be running (and calling back) when the clip, the codec and the callback go away;
            // SetCallback(0) gives the SDK's reference to the callback back before `callback` is disposed.
            if (codec.Ptr != 0) codec.FlushJobs();
            Com.Release(clip.Ptr);
            if (codec.Ptr != 0)
            {
                codec.SetCallback(0);
                Com.Release(codec.Ptr);
            }
            Com.Release(factory.Ptr);
        }
    }

    static int Info(BrawCodec codec, BrawClip clip, FrameSink sink, Stream stdout, TextWriter stderr)
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
        if (clip.CloneClipProcessingAttributes(out var attrs) == Com.S_OK && attrs.Ptr != 0)
        {
            try { (gamma, gamut, gen) = Colour(attrs, stderr); }
            finally { Com.Release(attrs.Ptr); }
        }
        var s = slots[0];
        var facts = new ClipFacts(
            s.Ok ? s.Width : clip.Width, s.Ok ? s.Height : clip.Height, clip.Width, clip.Height, clip.FrameRate, frames,
            clip.TimecodeForFrame(0) ?? "", clip.CameraType ?? "", gamma, gamut, gen);
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

    public int Errors => Volatile.Read(ref errors);

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
            if (result == Com.S_OK) result = frame.SetResourceFormat(FourCC.ResourceFormatRgbU16);
            if (result == Com.S_OK) result = frame.SetResolutionScale(scale);
            if (result == Com.S_OK) result = frame.CreateJobDecodeAndProcessFrame(out decode);
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
