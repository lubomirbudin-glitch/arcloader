namespace Offload.Core;

/// How frames reach ffmpeg: straight from a file, or piped from a vendor RAW decoder.
internal sealed class FrameSource
{
    public required IReadOnlyList<string> InputArgs { get; init; }
    /// Plan 10 mode A: the tap's activity for the clip ffmpeg reads from it (RangeServer.ActivityOf) — a rise is a sign
    /// of life for the stall watchdog while a slow destination is being read; null otherwise.
    public Func<long>? InputActivity { get; init; }
    /// Decoder whose stdout is piped into ffmpeg's stdin (input "-"), or null.
    public string? FeederExe { get; init; }
    public IReadOnlyList<string> FeederArgs { get; init; } = [];
    /// Turns the feeder's own failure (it ended non-zero while ffmpeg still read) into the clip's note — a
    /// RawDecoderException; null or an unchanged error = the feeder's ToolError as it is.
    public Func<ToolError, Exception>? ExplainFeederError { get; init; }
    public bool AudioFromInput { get; init; } = true;
    /// The decoder already cropped to the frame lines (ARRI does it before debayering).
    public bool AlreadyCropped { get; init; }
    /// Full-range RGB (the BRAW decoder) — built-in LUTs then need FilterGraph.PreMap first.
    public bool FullRange { get; init; }
    /// Intermediate files or folders to delete once ffmpeg is done.
    public IReadOnlyList<string> Cleanup { get; init; } = [];

    public void RemoveIntermediates()
    {
        foreach (var p in Cleanup)
        {
            try
            {
                if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
                else File.Delete(p);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
