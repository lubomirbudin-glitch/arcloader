using System.Text.Json.Serialization;

namespace Offload.Core;

public sealed class ToolError(string tool, int status, string message) : Exception(Describe(tool, status, message))
{
    public string Tool { get; } = tool;
    public int Status { get; } = status;
    public string Detail { get; } = message;

    static string Describe(string tool, int status, string message)
    {
        var last = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3);
        return $"{tool} failed (exit {status}): {string.Join(" | ", last)}";
    }
}

/// External tools the clip/proxy stage can use. Every one is optional: a missing tool only disables
/// the formats that need it, never the offload itself.
public sealed class ExternalTools
{
    [JsonPropertyName("ffmpeg")] public string? Ffmpeg { get; set; }
    [JsonPropertyName("ffprobe")] public string? Ffprobe { get; set; }
    [JsonPropertyName("redline")] public string? Redline { get; set; }
    [JsonPropertyName("artCmd")] public string? ArtCmd { get; set; }
    [JsonPropertyName("brawDecoder")] public string? BrawDecoder { get; set; }
    /// Keys a newer version added inside "tools": kept and written back unchanged (R24).
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    /// Tests only: use the explicit paths and nothing else.
    [JsonIgnore] internal bool NoSearch { get; set; }

    /// Folders searched in order: bin\ next to the app (GUI), the exe's own folder (CLI lives in bin\),
    /// the repo's windows\tools\bin for development builds, %LOCALAPPDATA%\mmMover\bin (then the same folder of
    /// earlier versions, %LOCALAPPDATA%\ArcLoader\bin and %LOCALAPPDATA%\Offload\bin), then PATH.
    public static IReadOnlyList<string> SearchDirs
    {
        get
        {
            var dirs = new List<string>();
            var exeDir = AppContext.BaseDirectory.TrimEnd('\\');
            dirs.Add(Path.Combine(exeDir, "bin"));
            dirs.Add(exeDir);
            if (IsDevBuild(AppContext.BaseDirectory))
            {
                // Only a build running from the repo walks up to windows\tools\bin; an installed one must never pick up C:\tools\bin.
                var d = exeDir;
                for (int i = 0; i < 6 && Path.GetDirectoryName(d) is string parent; i++)
                {
                    d = parent;
                    dirs.Add(Path.Combine(d, "tools", "bin"));
                }
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.Add(Path.Combine(local, Product.Name, "bin"));
            dirs.Add(Path.Combine(local, "ArcLoader", "bin"));   // 0.10.x, before the rename to mmMover: still searched
            dirs.Add(Path.Combine(local, "Offload", "bin"));     // before 0.10.0: still searched
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return dirs;
        }
    }

    /// Running from a project's bin\Debug\ or bin\Release\ output (development), not from an installed copy.
    internal static bool IsDevBuild(string baseDirectory) =>
        baseDirectory.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase)
        || baseDirectory.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);

    static readonly Dictionary<string, string[]> KnownPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REDline.exe"] =
        [
            @"C:\Program Files\REDCINE-X Professional\REDline.exe",
            @"C:\Program Files\REDCINE-X PRO 64-bit\REDline.exe",
        ],
        ["art-cmd.exe"] =
        [
            @"C:\Program Files\ARRI\ARRI Reference Tool\art-cmd.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Name, "arri", "art-cmd.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcLoader", "arri", "art-cmd.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Offload", "arri", "art-cmd.exe"),
        ],
    };

    internal static string? Find(string exeName, IEnumerable<string> dirs, IEnumerable<string> knownPaths)
    {
        foreach (var dir in dirs)
        {
            var p = Path.Combine(dir, exeName);
            if (File.Exists(p)) return p;
        }
        return knownPaths.FirstOrDefault(File.Exists);
    }

    /// Fills every tool that is not set explicitly (or whose explicit path no longer exists).
    public ExternalTools Resolved()
    {
        string? Pick(string? explicitPath, string exeName)
        {
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath)) return explicitPath;
            if (NoSearch) return null;
            return Find(exeName, SearchDirs, KnownPaths.GetValueOrDefault(exeName, []));
        }
        return new ExternalTools
        {
            Ffmpeg = Pick(Ffmpeg, "ffmpeg.exe"),
            Ffprobe = Pick(Ffprobe, "ffprobe.exe"),
            Redline = Pick(Redline, "REDline.exe"),
            ArtCmd = Pick(ArtCmd, "art-cmd.exe"),
            BrawDecoder = Pick(BrawDecoder, "braw-decode.exe"),
            NoSearch = NoSearch,
        };
    }
}
