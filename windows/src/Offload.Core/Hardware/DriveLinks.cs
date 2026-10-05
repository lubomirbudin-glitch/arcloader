using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core.Hardware;

public enum StorageBus { Unknown, Usb, Sata, Nvme, Sd, Mmc, Scsi, Sas, Raid, Virtual, Network, Other }

public sealed record UsbLink(string ControllerName, string HubName, string ConnectorId, int Port,
    UsbLinkSpeed Link, UsbLinkSpeed Capable, bool ConnectorSupportsUsb3, bool Uasp);

public sealed record DriveLink(string Root, StorageBus Bus, string Product, UsbLink? Usb)
{
    public string Description => Usb is { } u
        ? $"{Product} — {UsbSpeeds.Label(u.Link)}, port {u.Port} ({u.ControllerName})"
        : $"{Product} — {Bus switch { StorageBus.Nvme => "NVMe", StorageBus.Sata => "SATA", StorageBus.Network => "síťový disk", StorageBus.Unknown => "neznámé připojení", _ => Bus.ToString() }}";
}

public static unsafe class DriveLinks
{
    internal static StorageBus BusFrom(int raw) => raw switch
    {
        0 => StorageBus.Unknown,
        1 => StorageBus.Scsi,
        7 => StorageBus.Usb,
        8 => StorageBus.Raid,
        10 => StorageBus.Sas,
        11 => StorageBus.Sata,
        12 => StorageBus.Sd,
        13 => StorageBus.Mmc,
        14 or 15 => StorageBus.Virtual,
        17 => StorageBus.Nvme,
        _ => StorageBus.Other,
    };

    /// Where the volume holding `path` is connected. Never throws: anything unreadable stays Unknown/null.
    public static DriveLink Resolve(string path)
    {
        string root = path;
        try
        {
            root = NativeIO.VolumeRoot(path) ?? Path.GetPathRoot(path) ?? path;
            if (root.StartsWith(@"\\", StringComparison.Ordinal) && !root.StartsWith(@"\\?\", StringComparison.Ordinal))
                return new DriveLink(root, StorageBus.Network, root.TrimEnd('\\'), null);
            var device = root.Length <= 3 ? @"\\.\" + root.TrimEnd('\\') : NativeIO.VolumeId(path)?.TrimEnd('\\');
            if (device == null) return new DriveLink(root, StorageBus.Unknown, "", null);
            using var volume = UsbNative.OpenForQuery(device);
            if (volume == null) return new DriveLink(root, StorageBus.Unknown, "", null);
            var (busRaw, product) = Descriptor(volume);
            var bus = BusFrom(busRaw);
            if (bus != StorageBus.Usb || DiskNumber(volume) is not uint disk) return new DriveLink(root, bus, product, null);
            return new DriveLink(root, bus, product, UsbLinkOf(disk));
        }
        catch (Exception)
        {
            return new DriveLink(root, StorageBus.Unknown, "", null);
        }
    }

    static (int Bus, string Product) Descriptor(SafeFileHandle h)
    {
        var q = new StoragePropertyQuery();  // StorageDeviceProperty, PropertyStandardQuery
        var buf = new byte[1024];
        fixed (byte* p = buf)
            if (!UsbNative.DeviceIoControl(h, UsbNative.IoctlStorageQueryProperty, &q, sizeof(StoragePropertyQuery), p, buf.Length, out _, 0))
                return (0, "");
        string Ascii(int offsetField)
        {
            int o = BitConverter.ToInt32(buf, offsetField);
            if (o <= 0 || o >= buf.Length) return "";
            int e = o;
            while (e < buf.Length && buf[e] != 0) e++;
            return Encoding.ASCII.GetString(buf, o, e - o).Trim();
        }
        var product = string.Join(" ", new[] { Ascii(12), Ascii(16) }.Where(s => s.Length > 0));
        return (BitConverter.ToInt32(buf, 28), product);
    }

    static uint? DiskNumber(SafeFileHandle h)
    {
        StorageDeviceNumber n;
        return UsbNative.DeviceIoControl(h, UsbNative.IoctlStorageGetDeviceNumber, null, 0, &n, sizeof(StorageDeviceNumber), out _, 0)
            ? n.DeviceNumber : null;
    }

    static UsbLink? UsbLinkOf(uint diskNumber)
    {
        string? instance = null;
        foreach (var iface in DevNodes.Interfaces(UsbNative.DiskInterface, null))
        {
            using var h = UsbNative.OpenForQuery(iface);
            if (h != null && DiskNumber(h) == diskNumber) { instance = DevNodes.InterfaceStr(iface, DevKeys.InstanceId); break; }
        }
        if (instance == null || DevNodes.Locate(instance) is not uint diskNode) return null;

        // Walk up to the USB device: the first ancestor whose parent is a hub (BOT: USBSTOR\…, UASP: SCSI\…, composite: &MI_xx).
        uint? usbDevice = null, hub = null;
        for (uint? cur = diskNode; cur is uint c; cur = DevNodes.Parent(c))
        {
            if (DevNodes.Parent(c) is uint parent && DevNodes.IsHub(parent)) { usbDevice = c; hub = parent; break; }
        }
        if (usbDevice is not uint dev || hub is not uint hubNode) return null;
        int port = (int)(DevNodes.UInt(dev, DevKeys.Address) ?? 0);
        string controller = "?";
        for (uint? cur = hubNode; cur is uint c; cur = DevNodes.Parent(c))
            if (DevNodes.IsHostController(c)) { controller = DevNodes.Name(c); break; }
        var hubId = DevNodes.Id(hubNode);
        var paths = DevNodes.Interfaces(UsbNative.HubInterface, hubId);
        if (paths.Count == 0 || port <= 0) return null;
        using var hubHandle = UsbHubIo.Open(paths[0]);
        if (hubHandle == null) return null;
        var raw = UsbHubIo.QueryPort(hubHandle, port);
        var link = UsbSpeeds.Link(raw.Speed, raw.V2Flags);
        bool onUsb3Port = ((raw.Protocols ?? 0) & 0b100) != 0;
        bool connectorUsb3 = onUsb3Port || (raw.CompanionPort > 0 && ((UsbHubIo.QueryPort(hubHandle, raw.CompanionPort).Protocols ?? 0) & 0b100) != 0);
        int connectorPort = onUsb3Port && raw.CompanionPort > 0 ? raw.CompanionPort : port;
        bool uasp = string.Equals(DevNodes.Str(dev, DevKeys.Service), "UASPStor", StringComparison.OrdinalIgnoreCase);
        return new UsbLink(controller, DevNodes.Name(hubNode), $"{hubId}#{connectorPort}", port, link,
            UsbSpeeds.Capability(raw.V2Flags, raw.BcdUsb, link), connectorUsb3, uasp);
    }
}
