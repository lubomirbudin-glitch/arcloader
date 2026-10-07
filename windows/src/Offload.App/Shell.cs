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
            return $"Nejde otevřít {path}: {e.Message}";
        }
    }

    /// Explorer on a folder (never a ShellExecute of a path that could be a program).
    public static string? OpenFolder(string folder)
    {
        try
        {
            // the trailing backslash makes Explorer treat it as a folder, never as a file to run
            Process.Start("explorer.exe", $"\"{folder.TrimEnd('\\')}\\\"").Dispose();
            return null;
        }
        catch (Exception e)
        {
            return $"Nejde otevřít Průzkumník: {e.Message}";
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
            return $"Nejde otevřít Průzkumník: {e.Message}";
        }
    }
}
