using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class DriveLinksTests
{
    [Theory]
    [InlineData(7, StorageBus.Usb)]
    [InlineData(11, StorageBus.Sata)]
    [InlineData(17, StorageBus.Nvme)]
    [InlineData(12, StorageBus.Sd)]
    [InlineData(14, StorageBus.Virtual)]
    [InlineData(0, StorageBus.Unknown)]
    [InlineData(99, StorageBus.Other)]
    public void BusFrom(int raw, StorageBus bus) => Assert.Equal(bus, DriveLinks.BusFrom(raw));

    [Fact]
    public void SystemDrive_IsInternalWithAProductName()
    {
        var link = DriveLinks.Resolve(Path.GetPathRoot(Environment.SystemDirectory)!);
        Assert.NotEqual(StorageBus.Unknown, link.Bus);
        Assert.False(string.IsNullOrWhiteSpace(link.Product));
        Assert.Contains(link.Product, link.Describe());
    }

    // Review focus 3
    [Fact]
    public void Resolve_UncAndMissingPath_DoNotThrow()
    {
        var unc = DriveLinks.Resolve(@"\\server\share\x");
        Assert.Equal(StorageBus.Network, unc.Bus);
        Assert.Equal(@"\\server\share", unc.Product);
        var missing = DriveLinks.Resolve(@"Q:\nope");
        Assert.Null(missing.Usb);
    }

    // Final review 6: a mapped drive is a network disk named by its share; Czech bus names; no " — " without a product.
    [Fact]
    public void Network_AndBusLabels()
    {
        Assert.Equal(@"\\nas\media — síťový disk", DriveLinks.Network(@"Z:\", @"\\nas\media").Describe());
        Assert.Equal("Z: — síťový disk", DriveLinks.Network(@"Z:\", null).Describe());
        Assert.Equal("neznámé připojení", new DriveLink(@"Q:\", StorageBus.Unknown, "", null).Describe());
        Assert.Equal("SD čtečka", new DriveLink(@"G:\", StorageBus.Sd, "", null).Describe());
        Assert.Equal("Disk — virtuální disk", new DriveLink(@"V:\", StorageBus.Virtual, "Disk", null).Describe());
        Assert.Equal("USB (port nezjištěn)", StorageBuses.Label(StorageBus.Usb));
        // No raw English enum names ("Usb", "Sd", "Virtual", "Unknown" …) reach the user.
        foreach (var bus in new[] { StorageBus.Usb, StorageBus.Sd, StorageBus.Mmc, StorageBus.Virtual, StorageBus.Network, StorageBus.Unknown, StorageBus.Other })
            Assert.NotEqual(bus.ToString(), StorageBuses.Label(bus), StringComparer.OrdinalIgnoreCase);
    }

    // Final review 1 + 5 + 8: which socket a hub port belongs to and whether it can do USB 3.
    [Fact]
    public void ConnectorOf_RootTwinsExternalHalvesAndUnknown()
    {
        var root = new Dictionary<int, RawPort>
        {
            [6] = UsbTopologyTests.Raw(6, 0b011, 19),
            [19] = UsbTopologyTests.Raw(19, 0b111, 6),
            [3] = UsbTopologyTests.Raw(3, 0b011, 0),
        };
        RawPort? Q(int n) => root.GetValueOrDefault(n);
        Assert.Equal((6, true), DriveLinks.ConnectorOf(root[19], UsbLinkSpeed.Super, Q));   // USB 3 twin → socket 6
        Assert.Equal((6, true), DriveLinks.ConnectorOf(root[6], UsbLinkSpeed.High, Q));     // USB 2 half of a USB 3 socket
        Assert.Equal((3, (bool?)false), DriveLinks.ConnectorOf(root[3], UsbLinkSpeed.High, Q));
        // External USB 3 hub: the companion is on the other hub half (same number, or a port of the same kind here).
        Assert.Equal((2, true), DriveLinks.ConnectorOf(UsbTopologyTests.Raw(2, 0b011, 2), UsbLinkSpeed.High, _ => null));
        Assert.Equal((2, true), DriveLinks.ConnectorOf(UsbTopologyTests.Raw(2, 0b011, 4), UsbLinkSpeed.High, _ => UsbTopologyTests.Raw(4, 0b011, 0)));
        // V2 unsupported: unknown, unless the link already runs at SuperSpeed.
        Assert.Equal((5, (bool?)null), DriveLinks.ConnectorOf(UsbTopologyTests.Raw(5, null, 0), UsbLinkSpeed.High, _ => null));
        Assert.Equal((5, true), DriveLinks.ConnectorOf(UsbTopologyTests.Raw(5, null, 0), UsbLinkSpeed.Super, _ => null));
    }

    // Final review 1: the drive line and the port list name one device's socket the same way.
    [Fact]
    public void DriveLineAndPortList_ShareTheLabel()
    {
        var hub = new UsbHub("ROOT", "Root",
        [
            new UsbPort(6, false, true, false, 19, null),
            new UsbPort(19, true, true, false, 6, new UsbDevice("USB\\WD", 1, 2, "Elements 25A2", UsbLinkSpeed.Super, UsbLinkSpeed.Super, null)),
        ]);
        var connectors = Connectors.Build(hub, "PCI\\HC", "Intel xHCI");
        var link = new DriveLink(@"E:\", StorageBus.Usb, "WD Elements 25A2",
            new UsbLink("PCI\\HC", "Intel xHCI", "ROOT#19", 6, 19, UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, true));
        var drives = new[] { new RoleLink("E:", link) };
        var row = PortLabels.ListRow(connectors.Single(), drives);
        Assert.Equal("WD Elements 25A2 — USB 3 (5 Gb/s), port 6", link.Describe(connectors));
        Assert.StartsWith("port 6 USB 3", row);
        Assert.EndsWith("← E:", row);
        Assert.Equal("port 6", PortLabels.Of(link.Usb!, connectors));
        // A user's name replaces the label everywhere, whichever half the drive reports.
        var names = new Dictionary<string, string> { ["ROOT#6"] = "přední levý" };
        Assert.Equal("WD Elements 25A2 — USB 3 (5 Gb/s), přední levý", link.Describe(connectors, names));
        Assert.StartsWith("přední levý USB 3", PortLabels.ListRow(connectors.Single(), [new RoleLink("Cíl 1", link)], names));
        Assert.EndsWith("← E: (Cíl 1)", PortLabels.ListRow(connectors.Single(), [new RoleLink("Cíl 1", link)], names));
        // Without the topology: the socket number the link carries, not the raw USB 3 port.
        Assert.Equal("WD Elements 25A2 — USB 3 (5 Gb/s), port 6", link.Describe());
    }

    [UsbDriveFact]
    public void UsbDrive_HasPortControllerAndLink()
    {
        var link = DriveLinks.Resolve(UsbDriveFactAttribute.FirstUsbDrive!);
        Assert.Equal(StorageBus.Usb, link.Bus);
        Assert.NotNull(link.Usb);
        Assert.True(link.Usb!.Port > 0);
        Assert.True(link.Usb.ConnectorPort > 0);
        Assert.False(string.IsNullOrEmpty(link.Usb.ControllerName));
        Assert.False(string.IsNullOrEmpty(link.Usb.ControllerId));
        Assert.NotEqual(UsbLinkSpeed.Unknown, link.Usb.Link);
        Assert.True(link.Usb.Capable >= link.Usb.Link);
        var connectors = UsbScanner.Scan().Connectors;
        var socket = Assert.Single(connectors, c => c.Has(link.Usb.ConnectorId));
        Assert.Equal(socket.ControllerId, link.Usb.ControllerId, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(PortLabels.Name(socket), link.Describe(connectors));
    }
}
