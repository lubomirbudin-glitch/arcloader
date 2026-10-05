using System.Windows;
using Offload.Core.Desktop;

namespace Offload.App;

/// Called from OnSourceInitialized (before the window is shown): a window taller or wider than the work area
/// (a 1366×768 laptop leaves ~728 px) is shrunk to it, never below its minimum — if even that does not fit, it
/// starts maximized — and centred on the screen or its owner, inside the work area. The work area is the one of
/// the right monitor: the owner's for the Hardware window, the one under the mouse for the main window.
static class WindowSizing
{
    public static void FitToWorkArea(Window w)
    {
        try
        {
            var area = NativeMethods.WorkAreaFor(w) ?? SystemParameters.WorkArea;
            double? cx = null, cy = null;
            if (w.WindowStartupLocation == WindowStartupLocation.CenterOwner && w.Owner is { WindowState: WindowState.Normal } o)
            {
                cx = o.Left + o.ActualWidth / 2;
                cy = o.Top + o.ActualHeight / 2;
            }
            var p = WindowFit.Fit(area.Left, area.Top, area.Width, area.Height, w.Width, w.Height, w.MinWidth, w.MinHeight, cx, cy);
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            if (p.Maximize)
            {
                // Placed on the target monitor first: a window maximizes on the monitor it is on.
                w.Left = p.Left;
                w.Top = p.Top;
                w.WindowState = WindowState.Maximized;
                return;
            }
            w.Width = p.Width;
            w.Height = p.Height;
            w.Left = p.Left;
            w.Top = p.Top;
        }
        catch (Exception e)
        {
            AppLog.Write($"window size: {e.Message}");   // never stop a window from opening over its size
        }
    }
}
