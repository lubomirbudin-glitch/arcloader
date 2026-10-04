namespace Offload.Core;

/// How frames reach ffmpeg: straight from a file, or piped from a vendor RAW decoder.
internal sealed class FrameSource
{
    public required IReadOnlyList<string> InputArgs { get; init; }
    /// Decoder whose stdout is piped into ffmpeg's stdin (input "-"), or null.
    public string? FeederExe { get; init; }
    public IReadOnlyList<string> FeederArgs { get; init; } = [];
    public bool AudioFromInput { get; init; } = true;
    /// The decoder already cropped to the frame lines (ARRI does it before debayering).
    public bool AlreadyCropped { get; init; }
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
