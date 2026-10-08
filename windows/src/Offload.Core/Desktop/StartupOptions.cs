namespace Offload.Core.Desktop;

/// Offload.exe arguments: `--no-hw-prompt` is still accepted (older scripts) but has no effect — the USB check
/// is on demand only since 2026-10-05 (spec 8a.2), there is no start-up question; every argument that is
/// an existing folder becomes a ticked manual source (drag a folder onto Offload.exe). `--demo <stav>` (pripraveno,
/// probiha, hotovo, nastaveni; overuje — plan 10, both verdicts) fills the window with a state of docs/ui-navrh for the visual check — off by default.
public sealed record StartupOptions(bool NoHwPrompt, IReadOnlyList<string> Sources, DemoState? Demo = null)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        bool noPrompt = false;
        DemoState? demo = null;
        var sources = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Equals("--no-hw-prompt", StringComparison.OrdinalIgnoreCase)) noPrompt = true;
            else if (a.Equals("--demo", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) demo = DemoScenario.Parse(args[++i]);
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
        return new StartupOptions(noPrompt, sources, demo);
    }
}
