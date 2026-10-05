using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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
