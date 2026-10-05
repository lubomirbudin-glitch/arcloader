using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class HwTestTests
{
    [Fact]
    public void Roles_CardsThenDestinations_OtherDrivesNotIncluded()
    {
        List<(string Role, string Root)> expected1 = [("Karta", @"E:\"), ("Cíl 1", @"D:\a"), ("Cíl 2", @"F:\")];
        List<(string Role, string Root)> expected2 = [("Karta 1", @"E:\"), ("Karta 2", @"G:\")];
        Assert.Equal(expected1, HwTest.Roles([@"E:\"], [@"D:\a", @"F:\"]));
        Assert.Equal(expected2, HwTest.Roles([@"E:\", @"G:\"], []));
    }

    [Fact]
    public void QuickTest_NoMeasurement_NothingWritten()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        var dest = dir.Sub("dest");
        var r = HwTest.Run([card], [dest], measure: false);
        Assert.Equal(2, r.Drives.Count);
        Assert.Empty(r.Measurements);
        Assert.Empty(Directory.GetFiles(dest));
    }

    [Fact]
    public void Measure_ReadsCardWritesDestinations_AndCleansUp()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(card, "clip.bin"), 4 << 20, 1);
        var dest = dir.Sub("dest");
        var r = HwTest.Run([card], [dest], measure: true, bytesPerDevice: 8 << 20);
        var m = r.Measurements.ToDictionary(x => x.Role);
        Assert.True(m["Karta"].ReadMBps > 0);
        Assert.Null(m["Karta"].WriteMBps);
        Assert.True(m["Cíl 1"].WriteMBps > 0);
        Assert.Empty(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
        Assert.Contains(r.Advice, a => a.Text.StartsWith("Odhad rychlosti offloadu"));
    }

    [Fact]
    public void SaveLoad_RoundTrip()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "hwtest.json");
        var r = HwTest.Run([dir.Sub("card")], [dir.Sub("dest")], measure: false);
        HwTest.Save(r, path);
        var back = HwTest.Load(path);
        Assert.NotNull(back);
        Assert.Equal(r.Drives.Count, back!.Drives.Count);
        Assert.Equal(r.Connectors.Count, back.Connectors.Count);
        Assert.Null(HwTest.Load(Path.Combine(dir.Path, "missing.json")));
    }

    [Fact]
    public void SaveLoad_RoundTrip_NestedHubsMeasurementsAndAdvice()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "sub", "hwtest.json");
        var inner = new UsbHub("H2", "Hub", [new UsbPort(1, true, true, false, 0, null)]);
        var dev = new UsbDevice("D1", 0x1234, 0x5678, "Disk", UsbLinkSpeed.Super, UsbLinkSpeed.SuperPlus, inner);
        var conn = new UsbConnector("C1", "H1", "xHCI", 1, 2, true, true, dev);
        var link = new DriveLink(@"E:\", StorageBus.Usb, "SSD", new UsbLink("xHCI", "Hub", "C1", 1, UsbLinkSpeed.Super, UsbLinkSpeed.SuperPlus, true, true));
        var r = new HwTestResult(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), [new RoleLink("Karta", link)], [conn],
            [new DriveMeasurement("Karta", @"E:\", 123.5, null)], [new Advice(AdviceKind.Info, "Ahoj č")]);
        HwTest.Save(r, path);
        var back = HwTest.Load(path)!;
        Assert.Equal(r.Time, back.Time);
        Assert.Equal(link, back.Drives[0].Link with { });
        Assert.Equal(conn.Device!.Hub!.Ports[0], back.Connectors[0].Device!.Hub!.Ports[0]);
        Assert.True(back.Connectors[0].SupportsUsb3);
        Assert.Equal(123.5, back.Measurements[0].ReadMBps);
        Assert.Equal("Ahoj č", back.Advice[0].Text);
    }
}
