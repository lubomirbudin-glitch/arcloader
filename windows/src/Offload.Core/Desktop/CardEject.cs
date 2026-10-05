using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Offload.Core.Hardware;

namespace Offload.Core.Desktop;

public sealed record EjectResult(bool Ok, string? Error);

/// Safely removes a card. The volume is locked (fails while another program has a file open on it) and
/// dismounted first. A card in a reader (removable media) is then ejected as media — the reader stays connected
/// for the next card. A USB disk is removed as a device (`CM_Request_Device_EjectW`, like "Bezpečně odebrat").
public static unsafe partial class CardEject
{
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3;
    const uint FsctlLockVolume = 0x00090018, FsctlDismountVolume = 0x00090020;
    const uint IoctlStorageMediaRemoval = 0x002D4804, IoctlStorageEjectMedia = 0x002D4808;
    const int VetoOutstandingOpen = 6;

    internal const string BusyText = "na kartě má otevřený soubor jiný program — zavřete ho a zkuste to znovu";

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint sa, uint disposition, uint flags, nint template);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Request_Device_EjectW")]
    private static partial uint CM_Request_Device_Eject(uint devInst, out int vetoType, char* vetoName, int nameLength, uint flags);

    public static bool CanEject(string? source) => Problem(source) == null;

    /// Why this source cannot be ejected, or null.
    internal static string? Problem(string? source)
    {
        if (source == null) return "neplatná cesta";
        string full;
        try
        {
            full = Path.GetFullPath(source.Length == 2 && source[1] == ':' ? source + "\\" : source);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "neplatná cesta";
        }
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return "síťová složka se nevysouvá";
        if (!string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "zdroj je složka, ne celá karta";
        if (string.Equals(root, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) return "systémový disk nelze vysunout";
        return null;
    }

    /// Removable media is always ejectable; a fixed disk only when it is USB / SD / MMC.
    internal static string? BusProblem(DriveType type, StorageBus bus) =>
        type == DriveType.Removable || bus is StorageBus.Usb or StorageBus.Sd or StorageBus.Mmc ? null : "interní disk nelze vysunout";

    internal static string VetoText(int veto) => veto switch
    {
        VetoOutstandingOpen => BusyText,
        0 => "Windows zařízení nevysunul",
        _ => $"Windows vysunutí odmítl (důvod {veto})",
    };

    /// Never throws; the error text completes "Kartu se nepodařilo vysunout: …".
    public static EjectResult Eject(string source)
    {
        try
        {
            if (Problem(source) is string problem) return new(false, problem);
            var root = Path.GetPathRoot(Path.GetFullPath(source.Length == 2 ? source + "\\" : source))!;
            var type = new DriveInfo(root).DriveType;
            if (type == DriveType.NoRootDirectory) return new(false, "karta není připojená");
            bool media = type == DriveType.Removable;
            uint? disk = null;
            if (!media)
            {
                // Decide before anything is locked or dismounted: a fixed disk is only ejected when it is USB / SD / MMC
                // and its device node is known, so a dismounted volume is never left behind by a failed eject.
                if (BusProblem(DriveType.Fixed, DriveLinks.Resolve(root).Bus) is string busProblem) return new(false, busProblem);
                disk = DriveLinks.DiskNode(root);
                if (disk == null) return new(false, "Windows nenašel zařízení disku — vysuňte ho ikonou Bezpečně odebrat hardware");
            }
            using (var h = CreateFileW(@"\\.\" + root.TrimEnd('\\'), GenericRead | GenericWrite, ShareReadWrite, 0, OpenExisting, 0, 0))
            {
                if (h.IsInvalid) return new(false, "karta není připojená");
                if (!Lock(h)) return new(false, BusyText);
                if (!UsbNative.DeviceIoControl(h, FsctlDismountVolume, null, 0, null, 0, out _, 0))
                    return new(false, "svazek se nepodařilo odpojit — zavřete programy, které kartu používají");
                byte allow = 0;
                UsbNative.DeviceIoControl(h, IoctlStorageMediaRemoval, &allow, 1, null, 0, out _, 0);
                if (media)
                {
                    // Success is the dismount above: a reader that cannot eject the media (IOCTL unsupported) still has the
                    // volume dismounted, so the card in its slot is safe to pull out.
                    UsbNative.DeviceIoControl(h, IoctlStorageEjectMedia, null, 0, null, 0, out _, 0);
                    return new(true, null);
                }
            }
            int veto = 0;
            char* vetoName = stackalloc char[260];
            uint[] targets = DevNodes.Parent(disk!.Value) is uint parent ? [parent, disk.Value] : [disk.Value];
            foreach (var node in targets)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    vetoName[0] = (char)0;
                    if (CM_Request_Device_Eject(node, out veto, vetoName, 260, 0) == 0 && veto == 0) return new(true, null);
                    Thread.Sleep(300);
                }
            }
            var name = new string(vetoName);
            var reason = VetoText(veto) + (name.Length > 0 ? $" ({name})" : "");
            return new(false, reason + " — svazek je odpojený, ale zařízení zůstalo připojené; vysuňte ho ikonou Bezpečně odebrat hardware");
        }
        catch (Exception e)
        {
            return new(false, e.Message);
        }
    }

    static bool Lock(SafeFileHandle h)
    {
        for (int i = 0; i < 10; i++)
        {
            if (UsbNative.DeviceIoControl(h, FsctlLockVolume, null, 0, null, 0, out _, 0)) return true;
            Thread.Sleep(200);
        }
        return false;
    }
}
