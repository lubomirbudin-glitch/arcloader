using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Offload.Core.Desktop;

namespace Offload.App;

/// Rescans volumes when Windows reports a disk or card arriving or leaving (WM_DEVICECHANGE), debounced 500 ms.
sealed class VolumeWatcher : IDisposable
{
    const int WmDeviceChange = 0x0219;
    const long DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;
    const int DbtDevtypVolume = 2;

    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    HwndSource? source;

    public event Action? Changed;
    /// Volumes arrived (drive roots), raised at once — before the debounced Changed.
    public event Action<IReadOnlyList<string>>? Arrived;

    public VolumeWatcher()
    {
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Changed?.Invoke();
        };
    }

    public void Attach(Window window)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && wParam.ToInt64() is DbtDeviceArrival or DbtDeviceRemoveComplete)
        {
            try
            {
                // DEV_BROADCAST_VOLUME: dbcv_devicetype at offset 4, dbcv_unitmask at offset 12.
                if (wParam.ToInt64() == DbtDeviceArrival && lParam != IntPtr.Zero && Marshal.ReadInt32(lParam, 4) == DbtDevtypVolume)
                    Arrived?.Invoke(Volumes.RootsFromUnitMask((uint)Marshal.ReadInt32(lParam, 12)));
            }
            catch (Exception e)
            {
                AppLog.Write($"device arrival: {e.Message}");
            }
            debounce.Stop();
            debounce.Start();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        source?.RemoveHook(Hook);
        debounce.Stop();
    }
}
