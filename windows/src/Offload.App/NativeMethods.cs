using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Offload.Core.Desktop;

namespace Offload.App;

static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    const uint FlashAll = 3, FlashUntilForeground = 12;

    [StructLayout(LayoutKind.Sequential)]
    struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public uint Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    const uint MonitorDefaultToNearest = 2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point32 point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(Point32 point, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    /// The work area (WPF units) of the monitor `window` opens on: its owner's monitor, otherwise the one under the
    /// mouse (where the user just started the app). Null when Windows does not say — the caller falls back to the
    /// primary monitor.
    public static Rect? WorkAreaFor(Window window)
    {
        try
        {
            nint monitor = 0;
            if (window.Owner is { } owner && new WindowInteropHelper(owner).Handle is var ownerHandle && ownerHandle != 0)
                monitor = MonitorFromWindow(ownerHandle, MonitorDefaultToNearest);
            else if (GetCursorPos(out var cursor))
                monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
            if (monitor == 0) return null;
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return null;
            var toDip = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var (left, top, width, height) = WindowFit.FromDevice(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, toDip.M11, toDip.M22);
            return new Rect(left, top, width, height);
        }
        catch (Exception e)
        {
            AppLog.Write($"monitor: {e.Message}");
            return null;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);

    /// Flashes the taskbar button until the user switches to the window (no-op while it is in front).
    public static void Flash(Window window)
    {
        try
        {
            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Window = new WindowInteropHelper(window).Handle,
                Flags = FlashAll | FlashUntilForeground,
            };
            if (info.Window != 0) FlashWindowEx(ref info);
        }
        catch (Exception e)
        {
            AppLog.Write($"flash: {e.Message}");
        }
    }
}
