namespace Offload.Core.Hardware;

public sealed record UsbDevice(string InstanceId, ushort Vid, ushort Pid, string Name, UsbLinkSpeed Link, UsbLinkSpeed Capable, UsbHub? Hub);

/// SupportsUsb3: null when the hub could not say (V2 query unsupported: third-party xHCI drivers, VMs).
public sealed record UsbPort(int Number, bool? SupportsUsb3, bool UserVisible, bool TypeC, int CompanionPort, UsbDevice? Device);

public sealed record UsbHub(string InstanceId, string Name, IReadOnlyList<UsbPort> Ports);

public sealed record UsbController(string InstanceId, string Name, IReadOnlyList<UsbHub> RootHubs);

/// One physical socket: an xHCI root hub lists it twice — as a USB 2 port and as its USB 3 twin ("companion");
/// an external USB 3 hub is two hub devices (USB 3 half + USB 2 half) with the same port numbers.
/// Label is the socket's unique default name ("port 6", "port 7 › 2", "Intel USB 3.20 xHCI · port 3");
/// SupportsUsb3 null = unknown (no speed claim).
public sealed record UsbConnector(string Id, string HubInstanceId, string ControllerId, string ControllerName,
    int Number, int Usb2Port, int Usb3Port, bool? SupportsUsb3, bool TypeC, bool UserVisible, UsbDevice? Device, string Label)
{
    /// Other ids under which a drive may report this socket (the port on the other half of a USB 3 hub).
    public IReadOnlyList<string> AltIds { get; init; } = [];

    public bool Has(string connectorId) => Id == connectorId || AltIds.Contains(connectorId);

    /// "USB 3", "USB-C", "jen USB 2.0", "" (unknown) + " (interní)".
    public string Kind
    {
        get
        {
            var speed = SupportsUsb3 switch
            {
                true => TypeC ? "USB-C" : "USB 3",
                false => TypeC ? "USB-C jen USB 2.0" : "jen USB 2.0",
                null => TypeC ? "USB-C" : "",
            };
            var kind = UserVisible ? speed : (speed + " (interní)").Trim();
            return kind;
        }
    }

    public string DefaultLabel => Kind.Length > 0 ? $"{Label} {Kind}" : Label;
}

public sealed record UsbTopology(IReadOnlyList<UsbController> Controllers)
{
    public IReadOnlyList<UsbConnector> Connectors
    {
        get
        {
            var shortNames = Controllers.Select(c => PortLabels.ShortController(c.Name)).ToList();
            return Controllers.SelectMany((c, i) =>
            {
                // Only with more than one controller does a port need the controller in its name; same-named
                // controllers (several AMD xHCI on one board) get a number.
                var prefix = Controllers.Count <= 1 ? ""
                    : shortNames.Count(n => n == shortNames[i]) > 1
                        ? $"{shortNames[i]} ({shortNames.Take(i + 1).Count(n => n == shortNames[i])}) · "
                        : $"{shortNames[i]} · ";
                return c.RootHubs.SelectMany(h => Hardware.Connectors.Build(h, c.InstanceId, c.Name, prefix));
            }).ToList();
        }
    }
}

public static class Connectors
{
    sealed record Slot(UsbPort? Usb2, string? Usb2Hub, UsbPort? Usb3, string? Usb3Hub, UsbPort? Unknown, string? UnknownHub)
    {
        public IEnumerable<UsbPort> Ports => new[] { Usb2, Usb3, Unknown }.OfType<UsbPort>();
    }

    /// The sockets of a root hub (pairs its USB 2 / USB 3 companions) and of every hub below it.
    public static List<UsbConnector> Build(UsbHub hub, string controllerId, string controllerName, string prefix = "") =>
        Build(hub, null, controllerId, controllerName, prefix + "port ");

    // hub: one hub, or (when twin is set) the USB 2 half of an external USB 3 hub whose USB 3 half is `twin`.
    static List<UsbConnector> Build(UsbHub hub, UsbHub? twin, string controllerId, string controllerName, string labelStart)
    {
        var result = new List<UsbConnector>();
        foreach (var slot in twin == null ? PairWithinHub(hub) : PairAcrossHalves(hub, twin))
        {
            var number = slot.Usb2?.Number ?? slot.Usb3?.Number ?? slot.Unknown!.Number;
            var mainHub = slot.Usb2Hub ?? slot.Usb3Hub ?? slot.UnknownHub!;
            var ids = new[] { (slot.Usb2Hub, slot.Usb2), (slot.Usb3Hub, slot.Usb3), (slot.UnknownHub, slot.Unknown) }
                .Where(x => x.Item2 != null).Select(x => $"{x.Item1}#{x.Item2!.Number}").Distinct().ToList();
            var id = $"{mainHub}#{number}";
            bool? usb3 = slot.Usb3 != null ? true : slot.Unknown != null ? slot.Unknown.SupportsUsb3 : false;
            var usb3Device = slot.Usb3?.Device;
            var usb2Device = slot.Usb2?.Device ?? slot.Unknown?.Device;
            var label = labelStart + number;
            result.Add(new UsbConnector(id, mainHub, controllerId, controllerName, number, slot.Usb2?.Number ?? 0, slot.Usb3?.Number ?? 0,
                usb3, slot.Ports.Any(p => p.TypeC), slot.Ports.Any(p => p.UserVisible), usb3Device ?? usb2Device, label)
            {
                AltIds = ids.Where(x => x != id).ToList(),
            });
            // A USB 3 hub shows up on both halves of the socket: walk both, so USB 2 devices behind it don't vanish.
            var childStart = label + " › ";
            if (usb2Device?.Hub is UsbHub h2 && usb3Device?.Hub is UsbHub h3)
                result.AddRange(Build(h2, h3, controllerId, controllerName, childStart));
            else if ((usb3Device?.Hub ?? usb2Device?.Hub) is UsbHub child)
                result.AddRange(Build(child, null, controllerId, controllerName, childStart));
        }
        return result;
    }

    // Root hub: USB 2 port n and its USB 3 companion m are one socket. Ports of unknown kind pair only when both
    // name each other as companions (then the socket must be USB 3: only USB 3 sockets have companions).
    static List<Slot> PairWithinHub(UsbHub hub)
    {
        var slots = new List<Slot>();
        var done = new HashSet<int>();
        foreach (var port in hub.Ports.OrderBy(p => p.SupportsUsb3 == true).ThenBy(p => p.Number))
        {
            if (!done.Add(port.Number)) continue;
            var twin = port.CompanionPort > 0 && port.CompanionPort != port.Number
                ? hub.Ports.FirstOrDefault(p => p.Number == port.CompanionPort && !done.Contains(p.Number) && IsTwin(port, p))
                : null;
            if (twin != null) done.Add(twin.Number);
            if (twin == null && port.SupportsUsb3 == null)
            {
                slots.Add(new Slot(null, null, null, null, port, hub.InstanceId));
                continue;
            }
            UsbPort? usb2, usb3;
            if (twin != null) (usb2, usb3) = Order(port, twin);
            else if (port.SupportsUsb3 == true) (usb2, usb3) = (null, port);
            else (usb2, usb3) = (port, null);
            slots.Add(new Slot(usb2, usb2 == null ? null : hub.InstanceId, usb3, usb3 == null ? null : hub.InstanceId, null, null));
        }
        return slots.OrderBy(s => s.Usb2?.Number ?? s.Usb3?.Number ?? s.Unknown!.Number).ToList();
    }

    static bool IsTwin(UsbPort a, UsbPort b) =>
        a.SupportsUsb3 is bool x && b.SupportsUsb3 is bool y ? x != y : b.CompanionPort == a.Number;

    static (UsbPort? Usb2, UsbPort? Usb3) Order(UsbPort a, UsbPort b)
    {
        if (a.SupportsUsb3 == true || b.SupportsUsb3 == false) return (b, a);
        if (b.SupportsUsb3 == true || a.SupportsUsb3 == false) return (a, b);
        return a.Number < b.Number ? (a, b) : (b, a);
    }

    // External USB 3 hub: port n of the USB 2 half and port n of the USB 3 half are the same socket.
    static List<Slot> PairAcrossHalves(UsbHub usb2Half, UsbHub usb3Half) =>
        usb2Half.Ports.Select(p => p.Number).Union(usb3Half.Ports.Select(p => p.Number)).Order()
            .Select(n =>
            {
                var p2 = usb2Half.Ports.FirstOrDefault(p => p.Number == n);
                var p3 = usb3Half.Ports.FirstOrDefault(p => p.Number == n);
                return new Slot(p2, p2 == null ? null : usb2Half.InstanceId, p3, p3 == null ? null : usb3Half.InstanceId, null, null);
            }).ToList();
}

public static class UsbScanner
{
    /// Every USB host controller with its hubs and ports. Never throws; what cannot be read is left out.
    public static UsbTopology Scan()
    {
        var controllers = new List<UsbController>();
        List<string> interfaces;
        try
        {
            interfaces = DevNodes.Interfaces(UsbNative.HostControllerInterface, null);
        }
        catch (Exception)
        {
            return new UsbTopology(controllers);
        }
        foreach (var iface in interfaces)
        {
            try
            {
                if (DevNodes.InterfaceStr(iface, DevKeys.InstanceId) is not string id || DevNodes.Locate(id) is not uint hc) continue;
                var hubs = DevNodes.Children(hc).Where(DevNodes.IsHub).Select(dn => ReadHub(dn, 0)).OfType<UsbHub>().ToList();
                controllers.Add(new UsbController(id, DevNodes.Name(hc), hubs));
            }
            catch (Exception)
            {
                // Diagnostics are best effort: one controller that cannot be read (device unplugged mid-scan, odd
                // driver) must not hide the others or take the app down.
            }
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
            ports.Add(new UsbPort(n, UsbSpeeds.PortIsUsb3(raw.Protocols, device?.Link), (props & 1) != 0, (props & 8) != 0, raw.CompanionPort, device));
        }
        return new UsbHub(id, DevNodes.Name(hubNode), ports);
    }
}
