using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Offload.Core.Notify;

namespace Offload.App;

/// Windows notifications without registration (spec 4.3, R19): an icon in the notification area through
/// Shell_NotifyIconW, added at the first notification and removed when the app closes; each notification is a balloon
/// (NIF_INFO), which Windows 10/11 show as a modern notification and keep in the notification centre. A click on the
/// icon or the notification brings the main window back. Messages arrive at the main window's HwndSource (as in
/// VolumeWatcher). Tested by hand (manual test list) — nothing here touches a disk or the network.
sealed unsafe partial class TrayNotifier : IDisposable
{
    const int CallbackMessage = 0x8000 + 42;   // WM_APP + 42
    const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10;
    const uint NiifInfo = 1, NiifWarning = 2, NiifError = 3, NiifLargeIcon = 0x20;
    const uint NotifyIconVersion4 = 4;
    const int NinSelect = 0x400, NinKeySelect = 0x401, NinBalloonUserClick = 0x405, WmLButtonUp = 0x202;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconEx(string file, int index, nint* large, nint* small, uint count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial nint LoadIcon(nint instance, nint name);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string name);

    readonly Window window;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    HwndSource? source;
    nint icon;
    nint hwnd;   // cached: RemoveIcon may run on a crashing thread
    /// The notifier of the running app (the crash handler removes its icon).
    public static TrayNotifier? Current { get; private set; }
    Task<nint>? iconLoad;
    bool added;

    /// The icon or a notification was clicked: show the main window.
    public event Action? Activated;

    public TrayNotifier(Window window) => this.window = window;

    /// After the window has its handle (OnSourceInitialized).
    public void Attach()
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
        hwnd = source?.Handle ?? 0;
        Current = this;
        // Ruling M2: ExtractIconEx reads the exe from disk — never on the UI thread, never at the first notification.
        if (Environment.ProcessPath is string exe)
            iconLoad = Task.Run(() =>
            {
                try
                {
                    nint small = 0;
                    return ExtractIconEx(exe, 0, null, &small, 1) > 0 ? small : 0;
                }
                catch (Exception) { return (nint)0; }
            });
    }

    /// The main window is in front: no notification then (the sound and the taskbar flash are enough).
    public bool WindowInFront => window.IsActive && window.WindowState != WindowState.Minimized;

    public void Show(JobOutcome outcome)
    {
        try
        {
            if (!added && !Add()) return;
            var data = Data(NifInfo);
            Copy(outcome.Title, data.InfoTitle, 64);
            Copy(outcome.Message, data.Info, 256);
            data.InfoFlags = NiifLargeIcon | outcome.Kind switch
            {
                OutcomeKind.Error => NiifError,
                OutcomeKind.Attention => NiifWarning,
                _ => NiifInfo,
            };
            if (!ShellNotifyIcon(NimModify, ref data))
            {
                // Ruling M2: after an Explorer restart the icon is gone (an elevated process never gets TaskbarCreated):
                // add it again once and retry.
                added = false;
                if (!Add() || !ShellNotifyIcon(NimModify, ref data)) AppLog.Write("tray: the notification was not shown");
            }
        }
        catch (Exception e)
        {
            AppLog.Write($"tray: {e.Message}");
        }
    }

    NotifyIconData Data(uint flags) => new()
    {
        Size = (uint)sizeof(NotifyIconData),
        Window = hwnd,
        Id = 1,
        Flags = flags,
    };

    bool Add()
    {
        if (source == null) return false;
        // The extracted icon when it is ready, else the system's default one (never wait for the disk here).
        if (icon == 0 && iconLoad is { IsCompletedSuccessfully: true }) icon = iconLoad.Result;
        var shown = icon != 0 ? icon : LoadIcon(0, 32512);   // IDI_APPLICATION
        var data = Data(NifMessage | NifIcon | NifTip);
        data.CallbackMessage = CallbackMessage;
        data.Icon = shown;
        Copy("Offload", data.Tip, 128);
        // NIM_ADD fails when the icon is already there (e.g. a stale one): NIM_MODIFY then counts as added.
        added = ShellNotifyIcon(NimAdd, ref data) || ShellNotifyIcon(NimModify, ref data);
        if (!added)
        {
            AppLog.Write("tray: the icon could not be added");
            return false;
        }
        data.TimeoutOrVersion = NotifyIconVersion4;
        ShellNotifyIcon(NimSetVersion, ref data);
        return true;
    }

    static void Copy(string text, char* to, int capacity)
    {
        var s = text.Length >= capacity ? text[..(capacity - 2)] + "…" : text;
        for (int i = 0; i < s.Length; i++) to[i] = s[i];
        to[s.Length] = '\0';
    }

    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: the event is the low word of lParam.
            int e = (int)(lParam & 0xFFFF);
            if (e is NinSelect or NinKeySelect or NinBalloonUserClick or WmLButtonUp) Activated?.Invoke();
            handled = true;
        }
        else if (taskbarCreated != 0 && msg == (int)taskbarCreated)
        {
            // Explorer restarted: the icon is gone — put it back right away (if it was there), else the next notification adds it.
            if (added)
            {
                added = false;
                Add();
            }
        }
        return 0;
    }

    /// Always attempts NIM_DELETE (also after a crash, from the unhandled-exception handler): it is harmless when no icon is there.
    public void RemoveIcon()
    {
        try
        {
            var data = Data(0);
            ShellNotifyIcon(NimDelete, ref data);
            added = false;
        }
        catch (Exception) { /* closing */ }
    }

    public void Dispose()
    {
        try
        {
            RemoveIcon();
            if (icon == 0 && iconLoad is { IsCompletedSuccessfully: true }) icon = iconLoad.Result;
            if (icon != 0) DestroyIcon(icon);
            icon = 0;
            source?.RemoveHook(Hook);
        }
        catch (Exception e)
        {
            AppLog.Write($"tray: {e.Message}");
        }
    }
}
