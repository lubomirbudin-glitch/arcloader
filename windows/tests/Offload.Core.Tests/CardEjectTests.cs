using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

// Never ejects a real device: only inputs that are refused before any device is touched.
public class CardEjectTests
{
    [Fact]
    public void Folder_Network_System_AreRefused()
    {
        using var dir = new TempDir();
        Assert.Equal("zdroj je složka, ne celá karta", CardEject.Problem(dir.Path));
        Assert.Equal("síťová složka se nevysouvá", CardEject.Problem(@"\\server\share"));
        Assert.Equal("systémový disk nelze vysunout", CardEject.Problem(Path.GetPathRoot(Environment.SystemDirectory)!));
        Assert.False(CardEject.CanEject(dir.Path));
        var r = CardEject.Eject(dir.Path);
        Assert.False(r.Ok);
        Assert.Equal("zdroj je složka, ne celá karta", r.Error);
    }

    [Fact]
    public void BareDriveLetter_IsARoot()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = "QRSTUVWXYZ".First(c => !used.Contains(c));
        Assert.Null(CardEject.Problem($"{free}:"));
        var r = CardEject.Eject($@"{free}:\");
        Assert.False(r.Ok);
        Assert.Equal("karta není připojená", r.Error);
    }

    [Fact]
    public void NullSource_IsInvalid_NotAThrow()
    {
        Assert.Equal("neplatná cesta", CardEject.Problem(null));
        Assert.False(CardEject.CanEject(null));
    }

    [Theory]
    [InlineData(DriveType.Removable, StorageBus.Unknown, null)]
    [InlineData(DriveType.Removable, StorageBus.Sata, null)]
    [InlineData(DriveType.Fixed, StorageBus.Usb, null)]
    [InlineData(DriveType.Fixed, StorageBus.Sd, null)]
    [InlineData(DriveType.Fixed, StorageBus.Mmc, null)]
    [InlineData(DriveType.Fixed, StorageBus.Sata, "interní disk nelze vysunout")]
    [InlineData(DriveType.Fixed, StorageBus.Nvme, "interní disk nelze vysunout")]
    [InlineData(DriveType.Fixed, StorageBus.Unknown, "interní disk nelze vysunout")]
    public void BusRule(DriveType type, StorageBus bus, string? expected) =>
        Assert.Equal(expected, CardEject.BusProblem(type, bus));

    [Fact]
    public void VetoTexts()
    {
        Assert.Equal(CardEject.BusyText, CardEject.VetoText(6));
        Assert.Contains("5", CardEject.VetoText(5));
    }

    [UsbDriveFact]
    public void DiskNode_FoundForUsbDrive_WithAParent()
    {
        var node = DriveLinks.DiskNode(UsbDriveFactAttribute.FirstUsbDrive!);
        Assert.NotNull(node);
        Assert.NotNull(DevNodes.Parent(node!.Value));
    }
}
