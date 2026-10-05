namespace Offload.Core.Hardware;

public sealed record UsbDevice(string InstanceId, ushort Vid, ushort Pid, string Name, UsbLinkSpeed Link, UsbLinkSpeed Capable, UsbHub? Hub);

public sealed record UsbPort(int Number, bool SupportsUsb3, bool UserVisible, bool TypeC, int CompanionPort, UsbDevice? Device);

public sealed record UsbHub(string InstanceId, string Name, IReadOnlyList<UsbPort> Ports);

public sealed record UsbController(string InstanceId, string Name, IReadOnlyList<UsbHub> RootHubs);

/// One physical socket: an xHCI root hub lists it twice — as a USB 2 port and as its USB 3 twin ("companion").
public sealed record UsbConnector(string Id, string HubInstanceId, string ControllerName, int Usb2Port, int Usb3Port,
    bool TypeC, bool UserVisible, UsbDevice? Device)
{
    public bool SupportsUsb3 => Usb3Port > 0;

    public string DefaultLabel =>
        $"port {(Usb2Port > 0 ? Usb2Port : Usb3Port)} {(TypeC ? "USB-C" : SupportsUsb3 ? "USB 3" : "jen USB 2.0")}{(UserVisible ? "" : " (interní)")}";
}

public sealed record UsbTopology(IReadOnlyList<UsbController> Controllers)
{
    public IReadOnlyList<UsbConnector> Connectors =>
        Controllers.SelectMany(c => c.RootHubs.SelectMany(h => Hardware.Connectors.Build(h, c.Name))).ToList();
}

public static class Connectors
{
    public static List<UsbConnector> Build(UsbHub hub, string controllerName)
    {
        var result = new List<UsbConnector>();
        var done = new HashSet<int>();
        foreach (var port in hub.Ports.OrderBy(p => p.SupportsUsb3).ThenBy(p => p.Number))
        {
            if (!done.Add(port.Number)) continue;
            var twin = port.CompanionPort > 0 ? hub.Ports.FirstOrDefault(p => p.Number == port.CompanionPort && p.SupportsUsb3 != port.SupportsUsb3) : null;
            if (twin != null) done.Add(twin.Number);
            var usb2 = port.SupportsUsb3 ? twin : port;
            var usb3 = port.SupportsUsb3 ? port : twin;
            var device = usb3?.Device ?? usb2?.Device;
            int idPort = usb2?.Number ?? usb3!.Number;
            result.Add(new UsbConnector($"{hub.InstanceId}#{idPort}", hub.InstanceId, controllerName, usb2?.Number ?? 0, usb3?.Number ?? 0,
                port.TypeC || (twin?.TypeC ?? false), port.UserVisible || (twin?.UserVisible ?? false), device));
            if (device?.Hub is UsbHub child) result.AddRange(Build(child, controllerName));
        }
        return result;
    }
}

public static class UsbScanner
{
    /// Every USB host controller with its hubs and ports. Never throws; what cannot be read is left out.
    public static UsbTopology Scan()
    {
        var controllers = new List<UsbController>();
        try
        {
            foreach (var iface in DevNodes.Interfaces(UsbNative.HostControllerInterface, null))
            {
                if (DevNodes.InterfaceStr(iface, DevKeys.InstanceId) is not string id || DevNodes.Locate(id) is not uint hc) continue;
                var hubs = DevNodes.Children(hc).Where(DevNodes.IsHub).Select(dn => ReadHub(dn, 0)).OfType<UsbHub>().ToList();
                controllers.Add(new UsbController(id, DevNodes.Name(hc), hubs));
            }
        }
        catch (Exception)
        {
            // Diagnostics are best effort: a device unplugged mid-scan must never take the app down.
        }
        return new UsbTopology(controllers);
    }

    static UsbHub? ReadHub(uint hubNode, int depth)
    {
        if (depth > 6) return null;
        var id = DevNodes.Id(hubNode);
        var paths = DevNodes.Interfaces(UsbNative.HubInterface, id);
        if (paths.Count == 0) return null;
        using var h = UsbHubIo.Open(paths[0]);
        if (h == null || UsbHubIo.HubPortCount(h) is not int count) return new UsbHub(id, DevNodes.Name(hubNode), []);
        var children = DevNodes.Children(hubNode).ToList();
        var ports = new List<UsbPort>();
        for (int n = 1; n <= count; n++)
        {
            var raw = UsbHubIo.QueryPort(h, n);
            UsbDevice? device = null;
            if (raw.Status == 1)
            {
                var link = UsbSpeeds.Link(raw.Speed, raw.V2Flags);
                var cap = UsbSpeeds.Capability(raw.V2Flags, raw.BcdUsb, link);
                uint? child = raw.DriverKey == null ? null
                    : children.Cast<uint?>().FirstOrDefault(c => string.Equals(DevNodes.Str(c!.Value, DevKeys.Driver), raw.DriverKey, StringComparison.OrdinalIgnoreCase));
                if (child is uint c)
                    device = new UsbDevice(DevNodes.Id(c), raw.Vid, raw.Pid, DevNodes.BusName(c), link, cap, raw.IsHub ? ReadHub(c, depth + 1) : null);
                else
                    device = new UsbDevice("", raw.Vid, raw.Pid, $"VID_{raw.Vid:X4}&PID_{raw.Pid:X4}", link, cap, null);
            }
            uint props = raw.Properties ?? 0;
            ports.Add(new UsbPort(n, ((raw.Protocols ?? 0) & 0b100) != 0, (props & 1) != 0, (props & 8) != 0, raw.CompanionPort, device));
        }
        return new UsbHub(id, DevNodes.Name(hubNode), ports);
    }
}
