namespace Offload.Core.Desktop;

/// Offload.exe arguments: `--no-hw-prompt` skips the start-up USB question (automation); every argument that is
/// an existing folder becomes a ticked manual source (drag a folder onto Offload.exe).
public sealed record StartupOptions(bool NoHwPrompt, IReadOnlyList<string> Sources)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        bool noPrompt = false;
        var sources = new List<string>();
        foreach (var a in args)
        {
            if (a.Equals("--no-hw-prompt", StringComparison.OrdinalIgnoreCase)) noPrompt = true;
            else if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                try
                {
                    var full = Path.GetFullPath(a.Length == 2 && a[1] == ':' ? a + "\\" : a);
                    if (Directory.Exists(full)) sources.Add(full);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Not a path.
                }
            }
        }
        return new StartupOptions(noPrompt, sources);
    }
}
