using Offload.Core;

namespace Offload.Core.Tests;

/// BlackmagicRawAPI.dll somewhere braw-decode looks (next to the tests, SDK, Player, DaVinci Resolve).
public sealed class BrawLibraryFactAttribute : FactAttribute
{
    public static string? Library => BrawLibrary.Find(AppContext.BaseDirectory.TrimEnd('\\'));

    public BrawLibraryFactAttribute()
    {
        if (Library == null) Skip = "BlackmagicRawAPI.dll not found (Blackmagic RAW SDK / Player / DaVinci Resolve)";
    }
}

/// A real .braw clip: set OFFLOAD_BRAW_SAMPLE to its full path (see windows\README.md, "Vzorek BRAW").
public sealed class BrawSampleFactAttribute : FactAttribute
{
    public static string? Sample =>
        Environment.GetEnvironmentVariable("OFFLOAD_BRAW_SAMPLE") is string p && File.Exists(p) ? p : null;

    public BrawSampleFactAttribute(bool needsFfmpeg = false)
    {
        if (BrawLibraryFactAttribute.Library == null) Skip = "BlackmagicRawAPI.dll not found";
        else if (Sample == null) Skip = "OFFLOAD_BRAW_SAMPLE not set or the file does not exist";
        else if (needsFfmpeg && (TestMedia.Ffmpeg == null || TestMedia.Ffprobe == null)) Skip = "ffmpeg/ffprobe not found";
    }
}
