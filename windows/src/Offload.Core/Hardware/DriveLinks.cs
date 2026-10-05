using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Offload.Core.Hardware;

public enum StorageBus { Unknown, Usb, Sata, Nvme, Sd, Mmc, Scsi, Sas, Raid, Virtual, Network, Other }

/// A non-root hub between a device and its controller (a dock, an external hub, a monitor's hub).
public sealed record UsbHubRef(string InstanceId, string Name);

/// Where a USB drive sits. ConnectorId matches `UsbConnector.Id` or one of its `AltIds`; ConnectorPort is the
/// socket's number for the fallback label; Port is the raw hub port (the USB 3 twin, e.g. 19, for socket 6).
/// ConnectorUsb3: null = the hub could not say.
public sealed record UsbLink(string ControllerId, string ControllerName, string ConnectorId, int ConnectorPort, int Port,
    UsbLinkSpeed Link, UsbLinkSpeed Capable, bool? ConnectorUsb3, bool Uasp)
{
    /// Non-root hubs from the device up to the controller, nearest first; empty = plugged straight into the PC.
    public IReadOnlyList<UsbHubRef> Hubs { get; init; } = [];
}

public static class StorageBuses
{
    /// Czech name of a connection without a USB link.
    public static string Label(StorageBus bus) => bus switch
    {
        StorageBus.Nvme => "NVMe",
        StorageBus.Sata => "SATA",
        StorageBus.Sd or StorageBus.Mmc => "SD čtečka",
        StorageBus.Virtual => "virtuální disk",
        StorageBus.Network => "síťový disk",
        StorageBus.Usb => "USB (port nezjištěn)",
        StorageBus.Scsi => "SCSI",
        StorageBus.Sas => "SAS",
        StorageBus.Raid => "RAID",
        StorageBus.Other => "jiné připojení",
        _ => "neznámé připojení",
    };
}

public sealed record DriveLink(string Root, StorageBus Bus, string Product, UsbLink? Usb)
{
    /// "USB 3 (5 Gb/s), port 6" or "SATA" — the socket named through `PortLabels`, like the port list.
    public string Connection(IReadOnlyList<UsbConnector>? connectors = null, IReadOnlyDictionary<string, string>? names = null) =>
        Usb is { } u ? $"{UsbSpeeds.Label(u.Link)}, {PortLabels.Of(u, connectors, names)}" : StorageBuses.Label(Bus);

    /// "WD Elements 25A2 — USB 3 (5 Gb/s), port 6"; without a product just the connection.
    public string Describe(IReadOnlyList<UsbConnector>? connectors = null, IReadOnlyDictionary<string, string>? names = null) =>
        Product.Length > 0 ? $"{Product} — {Connection(connectors, names)}" : Connection(connectors, names);
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
                return Network(root, null);
            // A mapped network drive (Z:) has no storage descriptor: name it by its share, not "unknown".
            if (UsbNative.GetDriveType(root) == UsbNative.DriveRemote) return Network(root, MappedShare(root));
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

    /// A network location: the share (UNC) as the product, else the drive letter.
    internal static DriveLink Network(string root, string? unc) =>
        new(root, StorageBus.Network, string.IsNullOrWhiteSpace(unc) ? root.TrimEnd('\\') : unc.TrimEnd('\\'), null);

    static string? MappedShare(string root)
    {
        if (root.Length < 2 || root[1] != ':') return null;
        int len = 1024;
        char* buf = stackalloc char[len];
        return UsbNative.WNetGetConnection(root[..2], buf, ref len) == 0 ? new string(buf) : null;
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

        // Up to the controller; every hub on the way that is not the root hub is a dock / external hub.
        string controller = "?", controllerId = "";
        var hubs = new List<UsbHubRef>();
        for (uint? cur = hubNode; cur is uint c; cur = DevNodes.Parent(c))
        {
            if (DevNodes.IsHostController(c)) { controller = DevNodes.Name(c); controllerId = DevNodes.Id(c); break; }
            if (DevNodes.IsHub(c) && DevNodes.Parent(c) is uint p && !DevNodes.IsHostController(p))
                hubs.Add(new UsbHubRef(DevNodes.Id(c), DevNodes.BusName(c)));
        }
        var hubId = DevNodes.Id(hubNode);
        var paths = DevNodes.Interfaces(UsbNative.HubInterface, hubId);
        if (paths.Count == 0 || port <= 0) return null;
        using var hubHandle = UsbHubIo.Open(paths[0]);
        if (hubHandle == null) return null;
        var raw = UsbHubIo.QueryPort(hubHandle, port);
        var link = UsbSpeeds.Link(raw.Speed, raw.V2Flags);
        var (connectorPort, connectorUsb3) = ConnectorOf(raw, link, n => UsbHubIo.QueryPort(hubHandle, n));
        bool uasp = string.Equals(DevNodes.Str(dev, DevKeys.Service), "UASPStor", StringComparison.OrdinalIgnoreCase);
        // The raw hub port is one of the socket's ids (`UsbConnector.Id` or `AltIds`), whichever half it is on.
        return new UsbLink(controllerId, controller, $"{hubId}#{port}", connectorPort, port, link,
            UsbSpeeds.Capability(raw.V2Flags, raw.BcdUsb, link), connectorUsb3, uasp) { Hubs = hubs };
    }

    /// The socket a hub port belongs to: its number (the USB 2 twin's on a root hub) and whether it can do USB 3
    /// (null = unknown). A companion of the same kind, or the port itself, means the twin is on the other half of
    /// an external USB 3 hub — the socket is USB 3 and keeps its number.
    internal static (int Port, bool? Usb3) ConnectorOf(RawPort port, UsbLinkSpeed link, Func<int, RawPort?> query)
    {
        int n = port.Number, c = port.CompanionPort;
        bool? here = UsbSpeeds.PortIsUsb3(port.Protocols, link);
        if (c <= 0) return (n, here);
        if (c == n) return (n, true);
        var twin = query(c);
        bool? there = twin == null ? null : UsbSpeeds.PortIsUsb3(twin.Protocols, null);
        if (here is bool h && there is bool t) return h == t ? (n, true) : (h ? c : n, true);
        if (twin != null && twin.CompanionPort == n) return (here == true ? c : there == true ? n : Math.Min(n, c), true);
        return (n, here == true ? true : null);
    }
}
