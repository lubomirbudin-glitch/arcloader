using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Offload.App;

/// Rescans volumes when Windows reports a disk or card arriving or leaving (WM_DEVICECHANGE), debounced 500 ms.
sealed class VolumeWatcher : IDisposable
{
    const int WmDeviceChange = 0x0219;
    const long DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;

    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    HwndSource? source;

    public event Action? Changed;

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
