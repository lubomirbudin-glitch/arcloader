using System.Diagnostics;

namespace Offload.App;

/// Opening files and folders in Windows. Returns an error text, or null.
static class Shell
{
    public static string? Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception e)
        {
            return $"Cannot open {path}: {e.Message}";
        }
    }

    /// Explorer on a folder (never a ShellExecute of a path that could be a program).
    public static string? OpenFolder(string folder)
    {
        try
        {
            // /select only selects, it never executes (a trailing backslash would NOT stop Explorer from running a file)
            Process.Start("explorer.exe", $"/select,\"{folder}\"").Dispose();
            return null;
        }
        catch (Exception e)
        {
            return $"Cannot open File Explorer: {e.Message}";
        }
    }

    /// A web address in the default browser — https only (the caller checked).
    public static string? OpenWeb(string url) => Open(url);

    public static string? Reveal(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"").Dispose();
            return null;
        }
        catch (Exception e)
        {
            return $"Cannot open File Explorer: {e.Message}";
        }
    }
}
