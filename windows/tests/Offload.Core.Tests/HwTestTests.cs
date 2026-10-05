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
    public void Roles_ByDrive_LabelsDestinationsWithDriveLetter()
    {
        List<(string Role, string Root)> expected = [("Karta", @"G:\"), ("E:", @"E:\"), ("D:", @"D:\")];
        Assert.Equal(expected, HwTest.Roles([@"G:\"], [@"E:\", @"D:\"], rolesByDrive: true));
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
    public void Measure_ReadsCard()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(card, "clip.bin"), 4 << 20, 1);
        var r = HwTest.Run([card], [], measure: true, bytesPerDevice: 8 << 20);
        var m = Assert.Single(r.Measurements);
        Assert.True(m.ReadMBps > 0);
        Assert.Null(m.WriteMBps);
        Assert.Single(Directory.GetFiles(card));
        Assert.Contains(r.Advice, a => a.Text.StartsWith("Odhad rychlosti offloadu"));
    }

    [Fact]
    public void Measure_WritesDestination_AndCleansUp()
    {
        using var dir = new TempDir();
        var dest = dir.Sub("dest");
        var r = HwTest.Run([], [dest], measure: true, bytesPerDevice: 8 << 20);
        var m = Assert.Single(r.Measurements);
        Assert.True(m.WriteMBps > 0);
        Assert.True(m.ReadMBps > 0);
        Assert.Empty(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
    }

    // Final review 10: a destination on the card's volume is never written to — no measurement, a warning.
    [Fact]
    public void Measure_DestinationOnCardVolume_Skipped()
    {
        using var dir = new TempDir();
        var card = dir.Sub("card");
        TestFs.WriteRandom(Path.Combine(card, "clip.bin"), 4 << 20, 1);
        var dest = dir.Sub("dest");
        Assert.True(HwTest.SameVolume(card, dest));
        var r = HwTest.Run([card], [dest], measure: true, bytesPerDevice: 8 << 20);
        var m = r.Measurements.ToDictionary(x => x.Role);
        Assert.True(m["Karta"].ReadMBps > 0);
        Assert.Null(m["Cíl 1"].ReadMBps);
        Assert.Null(m["Cíl 1"].WriteMBps);
        Assert.Empty(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
        var w = Assert.Single(r.Advice, a => a.Kind == AdviceKind.Warning && a.Text.Contains("stejném svazku"));
        Assert.StartsWith("Cíl 1 je na stejném svazku jako Karta", w.Text);
        Assert.Equal(AdviceKind.Warning, r.Advice[0].Kind);
    }

    // Final review 11: cancelled → the remaining devices are not measured.
    [Fact]
    public void Measure_Cancelled_StopsBeforeNextDevice()
    {
        using var dir = new TempDir();
        var dest = dir.Sub("dest");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = HwTest.Run([], [dest], measure: true, bytesPerDevice: 8 << 20, cancel: cts.Token);
        Assert.Empty(r.Measurements);
        Assert.Empty(Directory.GetFiles(dest, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Advise_SameAsRun_AndUsesPortNames()
    {
        using var dir = new TempDir();
        var r = HwTest.Run([dir.Sub("card")], [dir.Sub("dest")], measure: false);
        Assert.Equal(r.Advice, HwTest.Advise(r.Drives, r.Connectors, r.Measurements, null));
        if (r.Connectors.FirstOrDefault(c => c.UserVisible && c.SupportsUsb3 == true && c.Device == null) is { } free)
        {
            var named = HwTest.Advise(r.Drives, r.Connectors, r.Measurements, new Dictionary<string, string> { [free.Id] = "přední levý" });
            Assert.Contains(named, a => a.Text.Contains("přední levý"));
        }
    }

    [Fact]
    public void Load_MissingLists_IsNull()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "hwtest.json");
        File.WriteAllText(path, "{}");
        Assert.Null(HwTest.Load(path));
        File.WriteAllText(path, """{"Time":"2026-10-05T10:00:00Z","Drives":[],"Connectors":[],"Measurements":[]}""");
        Assert.Null(HwTest.Load(path));
    }

    // Fix round 1: cancelling stops inside the 1 GB test file, deletes it, and skips the remaining devices.
    [Fact]
    public void Measure_CancelledDuringWrite_StopsAtOnce_NoBenchFileLeft()
    {
        using var dir = new TempDir();
        var dest1 = dir.Sub("dest1");
        var dest2 = dir.Sub("dest2");
        using var cts = BenchTests.CancelWhenBenchFileAppears(dest1);
        var r = HwTest.Run([], [dest1, dest2], measure: true, bytesPerDevice: 512L << 20, cancel: cts.Token);
        Assert.True(cts.IsCancellationRequested);
        Assert.Empty(r.Measurements);   // the interrupted device is "not measured", the next one is skipped
        Assert.Empty(Directory.GetFiles(dest1, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(dest2, "*", SearchOption.AllDirectories));
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
        var conn = new UsbConnector("C1", "H1", @"PCI\HC", "xHCI", 1, 1, 2, true, true, true, dev, "port 1") { AltIds = ["H1#2"] };
        var link = new DriveLink(@"E:\", StorageBus.Usb, "SSD", new UsbLink(@"PCI\HC", "xHCI", "C1", 1, 2, UsbLinkSpeed.Super, UsbLinkSpeed.SuperPlus, null, true)
        {
            Hubs = [new UsbHubRef(@"USB\DOCK", "Dock")],
        });
        var r = new HwTestResult(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), [new RoleLink("Karta", link)], [conn],
            [new DriveMeasurement("Karta", @"E:\", 123.5, null)], [new Advice(AdviceKind.Info, "Ahoj č")]);
        HwTest.Save(r, path);
        var back = HwTest.Load(path)!;
        Assert.Equal(r.Time, back.Time);
        Assert.Equivalent(link, back.Drives[0].Link, strict: true);
        Assert.Equal("port 1", back.Connectors[0].Label);
        Assert.True(back.Connectors[0].Has("H1#2"));
        Assert.Equal(conn.Device!.Hub!.Ports[0], back.Connectors[0].Device!.Hub!.Ports[0]);
        Assert.True(back.Connectors[0].SupportsUsb3);
        Assert.Equal(123.5, back.Measurements[0].ReadMBps);
        Assert.Equal("Ahoj č", back.Advice[0].Text);
    }
}
