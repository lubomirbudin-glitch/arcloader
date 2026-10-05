using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class ChainViewTests
{
    const string Hc = "Intel xHCI";

    static RoleLink Usb(string role, string product, UsbLinkSpeed link) =>
        new(role, new DriveLink("x:\\", StorageBus.Usb, product, new UsbLink(Hc, "Root", "R#1", 5, link, link, true, false)));

    static RoleLink Sata(string role) => new(role, new DriveLink("D:\\", StorageBus.Sata, "ST1000", null));

    [Fact]
    public void Build_UtilizationAndConnection()
    {
        var c = ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.Super), Sata("Cíl 1")], [225, 98]);
        Assert.Equal(0.5, c[0].Utilization!.Value, 2);
        Assert.Equal("USB 3 (5 Gb/s), port 5", c[0].Connection);
        Assert.Null(c[1].Utilization);
        Assert.Equal("SATA", c[1].Connection);
    }

    // Review focus 5
    [Fact]
    public void Bottleneck_SlowestElement_FullLinkSaysSo()
    {
        Assert.Equal("Cíl 1 ST1000", ChainView.Bottleneck(ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.Super), Sata("Cíl 1")], [300, 98])));
        Assert.Equal("Karta reader — linka je plná (USB 2.0 (480 Mb/s))",
            ChainView.Bottleneck(ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.High), Sata("Cíl 1")], [38, 98])));
        Assert.Null(ChainView.Bottleneck(ChainView.Build([Sata("Cíl 1")], [0])));
    }

    [Fact]
    public void Lines_IncludeControllerSum()
    {
        var lines = ChainView.Lines(ChainView.Build([Usb("Karta", "reader", UsbLinkSpeed.Super), Usb("Cíl 1", "SSD", UsbLinkSpeed.Super)], [300, 290]));
        Assert.Contains(lines, l => l.StartsWith("Karta") && l.Contains("300 MB/s") && l.Contains("67 % linky"));
        Assert.Contains(lines, l => l == "Řadič Intel xHCI: 590 MB/s");
        Assert.Contains(lines, l => l.StartsWith("Úzké hrdlo: "));
    }
}
