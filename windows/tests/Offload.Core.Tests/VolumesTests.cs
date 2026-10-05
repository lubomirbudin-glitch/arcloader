using Offload.Core.Desktop;
using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class VolumesTests
{
    static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void LooksLikeCard_MarkerFolder_AnyCase()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "dcim"));
        Assert.True(Volumes.LooksLikeCameraCard(dir.Path));
    }

    [Fact]
    public void LooksLikeCard_MediaAtRootOrOneLevelDown()
    {
        using var a = new TempDir();
        Touch(Path.Combine(a.Path, "A001.MOV"));
        Assert.True(Volumes.LooksLikeCameraCard(a.Path));

        using var b = new TempDir();
        Touch(Path.Combine(b.Path, "A001", "A001C001.braw"));
        Assert.True(Volumes.LooksLikeCameraCard(b.Path));

        using var red = new TempDir();
        Directory.CreateDirectory(Path.Combine(red.Path, "A001_C001.RDM"));
        Assert.True(Volumes.LooksLikeCameraCard(red.Path));
    }

    [Fact]
    public void LooksLikeCard_No()
    {
        using var deep = new TempDir();
        Touch(Path.Combine(deep.Path, "a", "b", "c.mxf"));
        Touch(Path.Combine(deep.Path, "notes.txt"));
        Assert.False(Volumes.LooksLikeCameraCard(deep.Path));
        Assert.False(Volumes.LooksLikeCameraCard(Path.Combine(deep.Path, "missing")));
    }

    [Theory]
    [InlineData(DriveType.Removable, StorageBus.Unknown, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Usb, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Sd, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Mmc, false, true)]
    [InlineData(DriveType.Fixed, StorageBus.Sata, false, false)]
    [InlineData(DriveType.Fixed, StorageBus.Nvme, false, false)]
    [InlineData(DriveType.Removable, StorageBus.Unknown, true, false)]
    [InlineData(DriveType.Network, StorageBus.Network, false, false)]
    [InlineData(DriveType.CDRom, StorageBus.Unknown, false, false)]
    public void IsCandidate(DriveType type, StorageBus bus, bool isSystem, bool expected) =>
        Assert.Equal(expected, Volumes.IsCandidate(type, bus, isSystem));

    static VolumeItem V(string path, bool card = true, bool dest = false) => new(path, path, 100, 50, "exFAT", card && !dest, dest);

    // Review focus 3
    [Fact]
    public void SelectionAfterRefresh_NewCardTicked_OldChoiceKept_GoneAndDestinationsDropped()
    {
        var previous = new[] { @"F:\", @"G:\", @"H:\" };
        var found = new List<VolumeItem> { V(@"F:\"), V(@"G:\"), V(@"I:\"), V(@"J:\", card: false), V(@"K:\", dest: true) };
        var selected = new[] { @"F:\", @"H:\", @"K:\", @"D:\shoot" };
        var next = Volumes.SelectionAfterRefresh(previous, found, selected, [@"D:\shoot"]);
        Assert.Contains(@"F:\", next);           // still there, still ticked
        Assert.DoesNotContain(@"G:\", next);     // the user unticked it earlier — stays unticked
        Assert.DoesNotContain(@"H:\", next);     // pulled out
        Assert.Contains(@"I:\", next);           // new card → ticked
        Assert.DoesNotContain(@"J:\", next);     // new, but not a card
        Assert.DoesNotContain(@"K:\", next);     // holds a destination
        Assert.Contains(@"D:\shoot", next);      // manual folder
    }

    [Fact]
    public void Scan_NeverOffersTheSystemDrive_AndDoesNotThrow()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory)!;
        var items = Volumes.Scan([]);
        Assert.DoesNotContain(items, v => string.Equals(v.Path, system, StringComparison.OrdinalIgnoreCase));
        Assert.All(items, v => Assert.False(string.IsNullOrEmpty(v.Name)));
    }

    [UsbDriveFact]
    public void Scan_OffersUsbDisk_AndMarksItWhenItHoldsADestination()
    {
        var root = UsbDriveFactAttribute.FirstUsbDrive!;
        Assert.Contains(Volumes.Scan([]), v => v.Path == root && !v.IsDestination);
        var asDest = Volumes.Scan([Path.Combine(root, "Backup")]).Single(v => v.Path == root);
        Assert.True(asDest.IsDestination);
        Assert.False(asDest.LooksLikeCard);
    }

    [Fact]
    public void StartupOptions_Parse()
    {
        using var dir = new TempDir();
        var o = StartupOptions.Parse(["--no-hw-prompt", dir.Path, Path.Combine(dir.Path, "missing"), "--unknown"]);
        Assert.True(o.NoHwPrompt);
        Assert.Equal([Path.GetFullPath(dir.Path)], o.Sources);
        Assert.False(StartupOptions.Parse([]).NoHwPrompt);
    }
}
