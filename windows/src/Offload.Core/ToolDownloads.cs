namespace Offload.Core;

/// Where a missing tool can be downloaded: the makers' own pages (Offload never ships the RAW decoders — their licences
/// do not allow it). Shown in Settings → Dekodéry RAW and in the note of a clip that got no proxy because of it.
public static class ToolDownloads
{
    /// Windows builds of ffmpeg + ffprobe (the "release full" build Offload's zip bundles; linked from ffmpeg.org).
    public const string Ffmpeg = "https://www.gyan.dev/ffmpeg/builds/";
    /// Blackmagic RAW Player and SDK (BlackmagicRawAPI.dll); DaVinci Resolve brings it too.
    public const string BlackmagicRaw = "https://www.blackmagicdesign.com/products/blackmagicraw";
    /// REDCINE-X PRO for Windows, which installs REDline.
    public const string Redline = "https://www.reddigitalcinema.com/download/redcine-x-pro-win";
    /// ARRI Reference Tool (ART), which includes art-cmd.
    public const string ArtCmd = "https://www.arri.com/en/learn/camera-systems/tools/arri-reference-tool";

    /// " — ke stažení: <url>" for the end of a Czech note.
    public static string Hint(string url) => $" — ke stažení: {url}";
}
