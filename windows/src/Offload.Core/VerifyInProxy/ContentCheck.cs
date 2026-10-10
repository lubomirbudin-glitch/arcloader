using System.Globalization;
using System.Text.Json.Serialization;

namespace Offload.Core.VerifyInProxy;

/// Spec 7 (content_ok / content_warn), report key `clips[].content`: what the decoder said while it made the proxy. A
/// warning is about the file itself (the copy matches the card bit for bit): it never fails the copy and never blocks
/// "Safe to format", but formatting needs an explicit confirmation (spec 7). Absent for a clip without a proxy.
public sealed class ContentResult
{
    public const string Ok = "ok";
    public const string Warn = "warn";

    /// "ok" or "warn".
    [JsonPropertyName("state")] public string State { get; init; } = Ok;
    /// "decode" (the video decoder reported damaged data), "audio" (an audio decoder did — spec 7: the proxy's sound is
    /// decoded too), "timestamps" (missing or repeated time stamps), "frames" (more frames than the file says), "end"
    /// (fewer: the clip ends early). Null when ok.
    [JsonPropertyName("kind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; init; }
    /// The frame near the first defect (approximate: ffmpeg's progress is reported every half second).
    [JsonPropertyName("frame"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Frame { get; init; }
    /// That frame as the clip's timecode ("01:00:02:13"), or "#<frame>" without one.
    [JsonPropertyName("timecode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Timecode { get; init; }
    /// The decoder's first line about it (English, as ffmpeg wrote it), or the frame counts.
    [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; init; }
    [JsonPropertyName("framesDecoded"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? FramesDecoded { get; init; }
    [JsonPropertyName("framesExpected"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? FramesExpected { get; init; }
    /// The picture at that place (spec 9 "náhled místa vady"), embedded in the HTML report only.
    [JsonIgnore] public byte[]? Jpeg { get; set; }

    [JsonIgnore] public bool IsWarn => State == Warn;

    /// "corrupted data", "corrupted audio", "bad timestamps", "extra frames", "clip ends early".
    public static string KindText(string? kind) => kind switch
    {
        "decode" => "corrupted data",
        "audio" => "corrupted audio",
        "timestamps" => "bad timestamps",
        "frames" => "extra frames",
        "end" => "clip ends early",
        _ => "vada obsahu",
    };
}

/// Watches one ffmpeg proxy run (R9: never -xerror, it would stop the proxy): the stderr lines at -loglevel warning and
/// the `frame=` counter of -progress. Lines that are about damaged data or time stamps are a warning; any other line
/// (encoder notes, deprecated pixel formats …) is not. Thread-safe: stdout and stderr are read on two threads. Only
/// stdout's progress feeds the stall watchdog — a decoder that keeps complaining without a new frame is still stuck.
public sealed class ContentWatch
{
    /// ffmpeg 8.1's own words, measured on a clip with garbage in its mdat and on a truncated faststart MP4 (plan 10).
    static readonly string[] DecodeWords =
    [
        "corrupt decoded frame", "decoding error", "error while decoding", "concealing", "invalid nal", "corrupt input packet",
        "packet corrupt", "invalid data found", "missing picture", "error splitting", "partial file", "decode_slice",
        "error submitting packet to decoder",
    ];

    /// What an audio decoder says about a damaged packet besides the words above (measured on AAC packets filled with
    /// garbage, ffmpeg 8.1; PCM and MP3 from their sources). Only for a line of an audio decoder: the proxy's own AAC
    /// encoder writes "[aac @ …]" lines too ("Too many bits … clamping"), which are not about the file.
    static readonly string[] AudioWords =
    [
        "exceeds limit", "is not allocated", "is not allowed", "reserved bit set", "input buffer exhausted", "invalid pcm packet",
        "header missing", "does not match the sample rate", "overread", "crc mismatch", "invalid frame", "error decoding",
    ];

    /// The decoders whose lines are about sound ("[aac @ …]", "[pcm_s24le @ …]"); "[aist#0:1/…]" is ffmpeg's tag of an
    /// audio input stream.
    static readonly string[] AudioDecoders =
    [
        "aac", "pcm_", "mp3", "mp2", "ac3", "eac3", "opus", "flac", "alac", "dca", "truehd", "vorbis", "adpcm_", "dolby_e", "wmav", "amr",
    ];

    static readonly string[] TimestampWords = ["non-monotonic", "non monotonically", "invalid timestamp", "timestamps are unset", "invalid dts"];

    readonly Lock gate = new();
    long frame;
    string? kind, message;
    long? at;

    /// A line of -progress (stdout).
    public void Progress(string line)
    {
        if (!line.StartsWith("frame=", StringComparison.Ordinal)
            || !long.TryParse(line["frame=".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return;
        lock (gate) frame = Math.Max(frame, n);
    }

    /// A line of stderr.
    public void Stderr(string line)
    {
        var lower = line.ToLowerInvariant();
        bool audio = IsAudio(lower);
        string? k = DecodeWords.Any(lower.Contains) ? (audio ? "audio" : "decode")
            : audio && AudioWords.Any(lower.Contains) ? "audio"
            : TimestampWords.Any(lower.Contains) ? "timestamps" : null;
        if (k == null) return;
        lock (gate)
        {
            if (kind != null) return;   // the first defect is the one reported
            kind = k;
            message = line.Trim();
            at = frame;
        }
    }

    static bool IsAudio(string lower)
    {
        if (lower.Contains("[aist#", StringComparison.Ordinal)) return true;
        if (!lower.StartsWith('[') || lower.IndexOf(" @", StringComparison.Ordinal) is not (> 1 and var end)) return false;
        var name = lower[1..end];
        return AudioDecoders.Any(a => name.StartsWith(a, StringComparison.Ordinal));
    }

    /// A retry of the clip (in software after the hardware decoder failed): what the first attempt saw is forgotten.
    public void Reset()
    {
        lock (gate)
        {
            frame = 0;
            kind = message = null;
            at = null;
        }
    }

    /// The verdict once ffmpeg ended: the first defect line; else the frame count against the file's own (nb_frames —
    /// a clip whose container does not say it is not counted, a guess from the duration would warn about good files).
    public ContentResult Verdict(ClipInfo info)
    {
        lock (gate)
        {
            long? expected = info.Frames is int n && n > 0 ? n : null;
            string? k = kind, msg = message;
            long? where = at;
            if (k == null && expected is long e && frame < e - 1)
                (k, msg, where) = ("end", $"decoded {frame} of {e} frames", frame);
            else if (k == null && expected is long e2 && frame > e2 + 1)
                (k, msg, where) = ("frames", $"decoded {frame} of {e2} frames", e2);
            if (k == null) return new ContentResult { FramesDecoded = frame, FramesExpected = expected };
            return new ContentResult
            {
                State = ContentResult.Warn, Kind = k, Message = msg, Frame = where, Timecode = TimecodeAt(info, where ?? 0),
                FramesDecoded = frame, FramesExpected = expected,
            };
        }
    }

    static string TimecodeAt(ClipInfo info, long frame) =>
        Timecode.IsValid(info.Timecode) && Timecode.Fps(info.FrameRate) is double fps ? Timecode.Add(info.Timecode!, frame, fps) : $"#{frame}";
}
