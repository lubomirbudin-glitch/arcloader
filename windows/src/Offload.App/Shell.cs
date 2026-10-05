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
