using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class UsbTopologyTests
{
    static UsbDevice Dev(string name, UsbLinkSpeed link, UsbLinkSpeed cap, UsbHub? hub = null) => new($"USB\\{name}", 1, 2, name, link, cap, hub);

    // Root hub like this PC: 2.0 ports 1..3, 3.x ports 17..18 as companions of 1 and 2.
    static UsbHub Root(UsbDevice? onPort1 = null, UsbDevice? onPort17 = null, UsbDevice? onPort3 = null) => new("ROOT", "Root hub",
    [
        new UsbPort(1, false, true, false, 17, onPort1),
        new UsbPort(2, false, true, true, 18, null),
        new UsbPort(3, false, true, false, 0, onPort3),
        new UsbPort(17, true, true, false, 1, onPort17),
        new UsbPort(18, true, true, true, 2, null),
    ]);

    static List<UsbConnector> Build(UsbHub hub) => Connectors.Build(hub, "PCI\\HC", "Intel xHCI");

    [Fact]
    public void Connectors_PairUsb2AndUsb3Ports()
    {
        var c = Build(Root(onPort17: Dev("disk", UsbLinkSpeed.Super, UsbLinkSpeed.Super)));
        Assert.Equal(3, c.Count);
        var a = c.Single(x => x.Usb2Port == 1);
        Assert.Equal(17, a.Usb3Port);
        Assert.True(a.SupportsUsb3);
        Assert.Equal("disk", a.Device!.Name);
        Assert.Equal("ROOT#1", a.Id);
        Assert.True(a.Has("ROOT#17"));
        Assert.Equal("PCI\\HC", a.ControllerId);
        Assert.Equal("port 1", a.Label);
        Assert.Equal("port 1 USB 3", a.DefaultLabel);
        var typeC = c.Single(x => x.Usb2Port == 2);
        Assert.True(typeC.TypeC);
        Assert.Equal("port 2 USB-C", typeC.DefaultLabel);
        var usb2Only = c.Single(x => x.Usb2Port == 3);
        Assert.False(usb2Only.SupportsUsb3);
        Assert.Equal("port 3 jen USB 2.0", usb2Only.DefaultLabel);
    }

    [Fact]
    public void Connectors_DeviceOnUsb2HalfOfUsb3Connector()
    {
        var c = Build(Root(onPort1: Dev("disk", UsbLinkSpeed.High, UsbLinkSpeed.Super)));
        var a = c.Single(x => x.Usb2Port == 1);
        Assert.True(a.SupportsUsb3);
        Assert.Equal(UsbLinkSpeed.High, a.Device!.Link);
    }

    // Review focus 4
    [Fact]
    public void Connectors_TolerateMissingCompanionAndDevice()
    {
        var hub = new UsbHub("H", "hub", [new UsbPort(1, false, true, false, 99, null), new UsbPort(20, true, false, false, 0, null)]);
        var c = Build(hub);
        Assert.Equal(2, c.Count);
        Assert.Equal(0, c.Single(x => x.Usb2Port == 1).Usb3Port);
        Assert.Equal("port 20 USB 3 (interní)", c.Single(x => x.Usb3Port == 20).DefaultLabel);
    }

    [Fact]
    public void Connectors_RecurseIntoExternalHubs()
    {
        var ext = new UsbHub("EXT", "USB2.0 Hub", [new UsbPort(1, false, true, false, 0, Dev("reader", UsbLinkSpeed.High, UsbLinkSpeed.High))]);
        var c = Build(Root(onPort3: Dev("hub", UsbLinkSpeed.High, UsbLinkSpeed.High, ext)));
        Assert.Contains(c, x => x.HubInstanceId == "EXT" && x.Device!.Name == "reader");
    }

    // Final review 1: hub ports are named by their path, never "port 1" again.
    [Fact]
    public void Labels_HubPortsDoNotCollideWithRootPorts()
    {
        var ext = new UsbHub("EXT", "USB2.0 Hub",
        [
            new UsbPort(1, false, true, false, 0, Dev("headset", UsbLinkSpeed.Full, UsbLinkSpeed.Full)),
            new UsbPort(2, false, true, false, 0, null),
            new UsbPort(3, false, true, false, 0, null),
        ]);
        var c = Build(Root(onPort3: Dev("hub", UsbLinkSpeed.High, UsbLinkSpeed.High, ext)));
        Assert.Equal(c.Count, c.Select(x => x.Label).Distinct().Count());
        Assert.Equal("port 3 › 1", c.Single(x => x.Id == "EXT#1").Label);
        Assert.Equal("port 3 › 2 jen USB 2.0", c.Single(x => x.Id == "EXT#2").DefaultLabel);
        Assert.Equal("port 1", c.Single(x => x.Id == "ROOT#1").Label);
    }

    // Final review 1 + 4: the controller goes into the name only with more than one controller; same names get numbers.
    [Fact]
    public void Labels_ControllerPrefixOnlyWithSeveralControllers()
    {
        static UsbHub R(string id) => new(id, "Root", [new UsbPort(1, false, true, false, 0, null)]);
        const string Amd = "AMD USB 3.10 eXtensible Host Controller - 1.10 (Microsoft)";
        var one = new UsbTopology([new UsbController("PCI\\A", Amd, [R("RA")])]);
        Assert.Equal("port 1", one.Connectors.Single().Label);

        var two = new UsbTopology([new UsbController("PCI\\A", Amd, [R("RA")]), new UsbController("PCI\\B", Amd, [R("RB")])]);
        Assert.Equal(["AMD USB 3.10 xHCI (1) · port 1", "AMD USB 3.10 xHCI (2) · port 1"], two.Connectors.Select(c => c.Label));
        Assert.Equal(["PCI\\A", "PCI\\B"], two.Connectors.Select(c => c.ControllerId));

        var mixed = new UsbTopology([new UsbController("PCI\\I", "Intel(R) USB 3.20 eXtensible Host Controller - 1.20 (Microsoft)", [R("RI")]),
            new UsbController("PCI\\A", Amd, [R("RA")])]);
        Assert.Equal("Intel USB 3.20 xHCI · port 1", mixed.Connectors[0].Label);
        Assert.Equal("AMD USB 3.10 xHCI · port 1", mixed.Connectors[1].Label);
    }

    // Final review 8: V2 unsupported → kind unknown, not "jen USB 2.0"; mutual companions still make a USB 3 socket.
    [Fact]
    public void Connectors_UnknownPortKind()
    {
        var hub = new UsbHub("ROOT", "Root",
        [
            new UsbPort(1, null, true, false, 5, null),
            new UsbPort(2, null, true, false, 0, null),
            new UsbPort(5, null, true, false, 1, null),
        ]);
        var c = Build(hub);
        Assert.Equal(2, c.Count);
        var paired = c.Single(x => x.Number == 1);
        Assert.True(paired.SupportsUsb3);
        Assert.Equal(5, paired.Usb3Port);
        var unknown = c.Single(x => x.Number == 2);
        Assert.Null(unknown.SupportsUsb3);
        Assert.Equal("port 2", unknown.DefaultLabel);
        var advice = Advisor.Advise([], c);
        Assert.DoesNotContain(advice, a => a.Text.Contains("port 2"));
    }

    // Final review 5: an external USB 3 hub is two hub devices — the USB 3 half on the socket's USB 3 port and the
    // USB 2 half on its twin. Both are walked and merged by port number; nothing vanishes, nothing looks free.
    [Fact]
    public void Connectors_DualHalfHub_MergedByPortNumber()
    {
        var ss = new UsbHub("SS", "USB3.0 Hub",
        [
            new UsbPort(1, true, true, false, 1, Dev("ssd", UsbLinkSpeed.Super, UsbLinkSpeed.Super)),
            new UsbPort(2, true, true, false, 2, null),
            new UsbPort(3, true, true, false, 3, null),
        ]);
        var hs = new UsbHub("HS", "USB2.0 Hub",
        [
            new UsbPort(1, false, true, false, 1, null),
            new UsbPort(2, false, true, false, 2, Dev("reader", UsbLinkSpeed.High, UsbLinkSpeed.Super)),
            new UsbPort(3, false, true, false, 3, null),
        ]);
        var c = Build(Root(onPort1: Dev("hs-hub", UsbLinkSpeed.High, UsbLinkSpeed.High, hs), onPort17: Dev("ss-hub", UsbLinkSpeed.Super, UsbLinkSpeed.Super, ss)));
        var hubPorts = c.Where(x => x.Label.StartsWith("port 1 › ")).ToList();
        Assert.Equal(3, hubPorts.Count);
        Assert.All(hubPorts, p => Assert.True(p.SupportsUsb3));
        Assert.Equal("ssd", hubPorts.Single(p => p.Number == 1).Device!.Name);
        var reader = hubPorts.Single(p => p.Number == 2);
        Assert.Equal("reader", reader.Device!.Name);
        Assert.True(reader.Has("HS#2") && reader.Has("SS#2"));
        Assert.Null(hubPorts.Single(p => p.Number == 3).Device);
        Assert.Equal("port 1 › 3 USB 3", hubPorts.Single(p => p.Number == 3).DefaultLabel);

        // The reader runs at USB 2.0 behind a USB 3 socket: advice says cable, never "move to <the same port>".
        var link = new DriveLink(@"F:\", StorageBus.Usb, "reader", new UsbLink("PCI\\HC", "Intel xHCI", "HS#2", 2, 2, UsbLinkSpeed.High,
            UsbLinkSpeed.Super, DriveLinks.ConnectorOf(Raw(2, 0b011, 2), UsbLinkSpeed.High, _ => null).Usb3, false));
        var w = Assert.Single(Advisor.Advise([new RoleLink("Karta", link)], c), a => a.Kind == AdviceKind.Warning);
        Assert.Contains("kabel", w.Text);
        Assert.DoesNotContain("port 1 › 2", w.Text);
        Assert.Equal("reader — USB 2.0 (480 Mb/s), port 1 › 2", link.Describe(c));
    }

    internal static RawPort Raw(int n, uint? protocols, int companion) =>
        new(n, 1, 2, false, 1, 2, 0x0300, protocols, null, 1, companion, null);

    [UsbFact]
    public void Scan_FindsControllersPortsAndDevices()
    {
        var t = UsbScanner.Scan();
        Assert.NotEmpty(t.Controllers);
        Assert.All(t.Controllers, c => Assert.False(string.IsNullOrEmpty(c.Name)));
        Assert.NotEmpty(t.Controllers.SelectMany(c => c.RootHubs).SelectMany(h => h.Ports));
        Assert.NotEmpty(t.Connectors);
        Assert.Equal(t.Connectors.Count, t.Connectors.Select(c => c.Label).Distinct().Count());
        var again = UsbScanner.Scan();
        Assert.Equal(t.Connectors.Count, again.Connectors.Count);
    }
}
