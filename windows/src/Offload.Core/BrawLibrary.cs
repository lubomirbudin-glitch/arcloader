namespace Offload.Core;

/// Where the Blackmagic RAW library (BlackmagicRawAPI.dll) can be. Offload never ships it: it comes with the
/// Blackmagic RAW SDK, the free Blackmagic RAW Player or DaVinci Resolve. Shared by braw-decode (which loads it)
/// and BrawBackend (which only checks it exists, so a missing library reads as "unsupported", not as a failure).
public static class BrawLibrary
{
    public const string DllName = "BlackmagicRawAPI.dll";

    /// Searched in order: next to braw-decode.exe, the SDK, the Player, DaVinci Resolve.
    public static IReadOnlyList<string> SearchDirs(string decoderDir)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return
        [
            decoderDir,
            Path.Combine(pf, "Blackmagic Design", "Blackmagic RAW", "Blackmagic RAW SDK", "Win", "Libraries"),
            Path.Combine(pf, "Blackmagic Design", "Blackmagic RAW", "Blackmagic RAW Player"),
            Path.Combine(pf, "Blackmagic Design", "DaVinci Resolve"),
        ];
    }

    public static string? Find(IEnumerable<string> dirs) => ExternalTools.Find(DllName, dirs, []);

    public static string? Find(string decoderDir) => Find(SearchDirs(decoderDir));
}
