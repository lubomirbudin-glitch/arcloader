using System.Runtime.InteropServices;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public unsafe class UsbNativeTests
{
    [Fact]
    public void StructLayouts_MatchUsbioctlH()
    {
        Assert.Equal(20, sizeof(DevPropKey));
        Assert.Equal(18, sizeof(UsbDeviceDescriptor));
        Assert.Equal(23, (int)Marshal.OffsetOf<UsbNodeConnectionInformationEx>(nameof(UsbNodeConnectionInformationEx.Speed)));
        Assert.Equal(31, (int)Marshal.OffsetOf<UsbNodeConnectionInformationEx>(nameof(UsbNodeConnectionInformationEx.ConnectionStatus)));
        Assert.Equal(16, sizeof(UsbNodeConnectionInformationExV2));
        Assert.Equal(16 + 512, sizeof(UsbPortConnectorProperties));
        Assert.Equal(77, sizeof(UsbHubInformationEx));
        Assert.Equal(12, sizeof(StoragePropertyQuery));
        Assert.Equal(12, sizeof(StorageDeviceNumber));
    }

    [Fact]
    public void IoctlCodes() 
    {
        Assert.Equal(0x220448u, UsbNative.IoctlConnectionInfoEx);
        Assert.Equal(0x22045Cu, UsbNative.IoctlConnectionInfoExV2);
        Assert.Equal(0x220458u, UsbNative.IoctlPortConnectorProperties);
        Assert.Equal(0x220454u, UsbNative.IoctlHubInformationEx);
        Assert.Equal(0x220420u, UsbNative.IoctlDriverKeyName);
    }

    [Fact]
    public void EveryPcHasADisk_WhoseParentChainReachesTheRoot()
    {
        var disks = DevNodes.Interfaces(UsbNative.DiskInterface, null);
        Assert.NotEmpty(disks);
        var id = DevNodes.InterfaceStr(disks[0], DevKeys.InstanceId);
        Assert.False(string.IsNullOrEmpty(id));
        var dn = DevNodes.Locate(id!);
        Assert.NotNull(dn);
        Assert.False(string.IsNullOrEmpty(DevNodes.Name(dn!.Value)));
        string last = "";
        for (uint? cur = dn; cur is uint c; cur = DevNodes.Parent(c)) last = DevNodes.Id(c);
        Assert.StartsWith("HTREE", last, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingNodeAndInterface_ReturnNothing()
    {
        Assert.Null(DevNodes.Locate(@"USB\VID_FFFF&PID_FFFF\nope"));
        Assert.Empty(DevNodes.Interfaces(Guid.NewGuid(), null));
        Assert.Null(UsbHubIo.Open(@"\\?\usb#nope#{f18a0e88-c30c-11d0-8815-00a0c906bed8}"));
    }

    [UsbFact]
    public void RootHub_HasPorts_AndConnectedPortsReportVendor()
    {
        var hc = DevNodes.Interfaces(UsbNative.HostControllerInterface, null)[0];
        var hcDn = DevNodes.Locate(DevNodes.InterfaceStr(hc, DevKeys.InstanceId)!)!.Value;
        var rootHub = DevNodes.Children(hcDn).First(c => DevNodes.Interfaces(UsbNative.HubInterface, DevNodes.Id(c)).Count > 0);
        using var h = UsbHubIo.Open(DevNodes.Interfaces(UsbNative.HubInterface, DevNodes.Id(rootHub))[0]);
        Assert.NotNull(h);
        var ports = UsbHubIo.HubPortCount(h!);
        Assert.True(ports > 0);
        var raws = Enumerable.Range(1, ports!.Value).Select(p => UsbHubIo.QueryPort(h!, p)).ToList();
        Assert.All(raws.Where(r => r.Status == 1), r => Assert.NotEqual(0, r.Vid));
        Assert.All(raws, r => Assert.NotNull(r.Protocols));
    }
}
