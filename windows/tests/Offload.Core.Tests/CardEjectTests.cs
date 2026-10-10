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
        Assert.Equal("the source is a folder, not a whole card", CardEject.Problem(dir.Path));
        Assert.Equal("a network folder cannot be ejected", CardEject.Problem(@"\\server\share"));
        Assert.Equal("the system disk cannot be ejected", CardEject.Problem(Path.GetPathRoot(Environment.SystemDirectory)!));
        Assert.False(CardEject.CanEject(dir.Path));
        var r = CardEject.Eject(dir.Path);
        Assert.False(r.Ok);
        Assert.Equal("the source is a folder, not a whole card", r.Error);
    }

    [Fact]
    public void BareDriveLetter_IsARoot()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = "QRSTUVWXYZ".First(c => !used.Contains(c));
        Assert.Null(CardEject.Problem($"{free}:"));
        var r = CardEject.Eject($@"{free}:\");
        Assert.False(r.Ok);
        Assert.Equal("the card is not connected", r.Error);
    }

    [Fact]
    public void NullSource_IsInvalid_NotAThrow()
    {
        Assert.Equal("invalid path", CardEject.Problem(null));
        Assert.False(CardEject.CanEject(null));
    }

    [Theory]
    [InlineData(DriveType.Removable, StorageBus.Unknown, null)]
    [InlineData(DriveType.Removable, StorageBus.Sata, null)]
    [InlineData(DriveType.Fixed, StorageBus.Usb, null)]
    [InlineData(DriveType.Fixed, StorageBus.Sd, null)]
    [InlineData(DriveType.Fixed, StorageBus.Mmc, null)]
    [InlineData(DriveType.Fixed, StorageBus.Sata, "an internal disk cannot be ejected")]
    [InlineData(DriveType.Fixed, StorageBus.Nvme, "an internal disk cannot be ejected")]
    [InlineData(DriveType.Fixed, StorageBus.Unknown, "an internal disk cannot be ejected")]
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
