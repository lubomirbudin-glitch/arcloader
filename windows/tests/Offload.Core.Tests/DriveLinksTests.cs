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
        Assert.Contains(link.Product, link.Description);
    }

    // Review focus 3
    [Fact]
    public void Resolve_UncAndMissingPath_DoNotThrow()
    {
        Assert.Equal(StorageBus.Network, DriveLinks.Resolve(@"\\server\share\x").Bus);
        var missing = DriveLinks.Resolve(@"Q:\nope");
        Assert.Null(missing.Usb);
    }

    [UsbDriveFact]
    public void UsbDrive_HasPortControllerAndLink()
    {
        var link = DriveLinks.Resolve(UsbDriveFactAttribute.FirstUsbDrive!);
        Assert.Equal(StorageBus.Usb, link.Bus);
        Assert.NotNull(link.Usb);
        Assert.True(link.Usb!.Port > 0);
        Assert.False(string.IsNullOrEmpty(link.Usb.ControllerName));
        Assert.NotEqual(UsbLinkSpeed.Unknown, link.Usb.Link);
        Assert.True(link.Usb.Capable >= link.Usb.Link);
        Assert.Contains(UsbScanner.Scan().Connectors, c => c.Id == link.Usb.ConnectorId);
    }
}
