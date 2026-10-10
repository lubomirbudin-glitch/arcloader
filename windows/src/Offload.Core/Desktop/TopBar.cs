using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

/// The dot of the Hardware button (UI návrh, horní lišta): green = the last USB check found nothing to fix, yellow = it
/// has advice (a warning); none before the first check.
public enum HardwareDot { None, Ok, Warning }

/// The top bar (UI návrh): the project switcher in the place of the design's preset switcher, the Hardware dot.
public static class TopBar
{
    public static HardwareDot Dot(HwTestResult? last) =>
        last == null ? HardwareDot.None : last.Advice.Any(a => a.Kind == AdviceKind.Warning) ? HardwareDot.Warning : HardwareDot.Ok;

    /// What the switcher shows after "Projekt": the active project, or "No project".
    public static string ProjectLabel(string? activeProject) => activeProject ?? Projects.NoProject;
}
