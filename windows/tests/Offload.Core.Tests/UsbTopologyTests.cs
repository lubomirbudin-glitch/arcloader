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

    [Fact]
    public void Connectors_PairUsb2AndUsb3Ports()
    {
        var c = Connectors.Build(Root(onPort17: Dev("disk", UsbLinkSpeed.Super, UsbLinkSpeed.Super)), "Intel xHCI");
        Assert.Equal(3, c.Count);
        var a = c.Single(x => x.Usb2Port == 1);
        Assert.Equal(17, a.Usb3Port);
        Assert.True(a.SupportsUsb3);
        Assert.Equal("disk", a.Device!.Name);
        Assert.Equal("ROOT#1", a.Id);
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
        var c = Connectors.Build(Root(onPort1: Dev("disk", UsbLinkSpeed.High, UsbLinkSpeed.Super)), "x");
        var a = c.Single(x => x.Usb2Port == 1);
        Assert.True(a.SupportsUsb3);
        Assert.Equal(UsbLinkSpeed.High, a.Device!.Link);
    }

    // Review focus 4
    [Fact]
    public void Connectors_TolerateMissingCompanionAndDevice()
    {
        var hub = new UsbHub("H", "hub", [new UsbPort(1, false, true, false, 99, null), new UsbPort(20, true, false, false, 0, null)]);
        var c = Connectors.Build(hub, "x");
        Assert.Equal(2, c.Count);
        Assert.Equal(0, c.Single(x => x.Usb2Port == 1).Usb3Port);
        Assert.Equal("port 20 USB 3 (interní)", c.Single(x => x.Usb3Port == 20).DefaultLabel);
    }

    [Fact]
    public void Connectors_RecurseIntoExternalHubs()
    {
        var ext = new UsbHub("EXT", "USB2.0 Hub", [new UsbPort(1, false, true, false, 0, Dev("reader", UsbLinkSpeed.High, UsbLinkSpeed.High))]);
        var c = Connectors.Build(Root(onPort3: Dev("hub", UsbLinkSpeed.High, UsbLinkSpeed.High, ext)), "x");
        Assert.Contains(c, x => x.HubInstanceId == "EXT" && x.Device!.Name == "reader");
    }

    [UsbFact]
    public void Scan_FindsControllersPortsAndDevices()
    {
        var t = UsbScanner.Scan();
        Assert.NotEmpty(t.Controllers);
        Assert.All(t.Controllers, c => Assert.False(string.IsNullOrEmpty(c.Name)));
        Assert.NotEmpty(t.Controllers.SelectMany(c => c.RootHubs).SelectMany(h => h.Ports));
        Assert.NotEmpty(t.Connectors);
        var again = UsbScanner.Scan();
        Assert.Equal(t.Connectors.Count, again.Connectors.Count);
    }
}
