using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class AdvisorTests
{
    const string Hc = "Intel(R) USB 3.20 eXtensible Host Controller";

    static RoleLink Usb(string role, string product, UsbLinkSpeed link, UsbLinkSpeed cap, bool connectorUsb3, int port, string controller = Hc) =>
        new(role, new DriveLink($"{role}:\\", StorageBus.Usb, product, new UsbLink(controller, "Root", $"ROOT#{port}", port, link, cap, connectorUsb3, false)));

    static readonly UsbConnector[] Ports =
    [
        new("ROOT#9", "ROOT", Hc, 9, 0, false, true, null),
        new("ROOT#5", "ROOT", Hc, 5, 20, false, true, null),
        new("ROOT#11", "ROOT", Hc, 11, 26, true, true, null),
        new("ROOT#1", "ROOT", Hc, 1, 17, false, true, new UsbDevice("x", 1, 1, "Keyboard", UsbLinkSpeed.Full, UsbLinkSpeed.Full, null)),
        new("ROOT#14", "ROOT", Hc, 14, 0, false, false, null),
    ];

    [Fact]
    public void Advice_CapableDeviceOnUsb2OnlyPort()
    {
        var a = Advisor.Advise([Usb("Cíl 1", "WD Elements", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.Contains("Cíl 1 WD Elements", w.Text);
        Assert.Contains("umí USB 3 (5 Gb/s)", w.Text);
        Assert.Contains("jen USB 2.0", w.Text);
        Assert.Contains("port 5 USB 3", w.Text);
        Assert.Contains("port 11 USB-C", w.Text);
    }

    // Review focus 2
    [Fact]
    public void Advice_CapableDeviceSlowLinkOnUsb3Connector()
    {
        var a = Advisor.Advise([Usb("Karta", "SanDisk reader", UsbLinkSpeed.High, UsbLinkSpeed.Super, true, 5)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.Contains("kabel", w.Text);
        Assert.DoesNotContain("jen USB 2.0", w.Text);
    }

    // Review focus 5
    [Fact]
    public void Advice_SharedController()
    {
        var a = Advisor.Advise(
        [
            Usb("Karta", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Cíl 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
        ], Ports);
        Assert.Contains(a, x => x.Kind == AdviceKind.Info && x.Text.Contains("Karta, Cíl 1") && x.Text.Contains("jeden řadič"));
    }

    [Fact]
    public void Advice_PortListsAndNames()
    {
        var names = new Dictionary<string, string> { ["ROOT#5"] = "přední levý" };
        var a = Advisor.Advise([], Ports, portNames: names);
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.StartsWith("Volné rychlé porty: ") && x.Text.Contains("přední levý") && x.Text.Contains("port 11 USB-C"));
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.Contains("port 9 jen USB 2.0") && !x.Text.Contains("port 14"));
    }

    [Fact]
    public void Advice_MeasuredEstimateAndSlowDisk()
    {
        var a = Advisor.Advise(
        [
            Usb("Karta", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Cíl 1", "WD Elements", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
            new RoleLink("Cíl 2", new DriveLink("D:\\", StorageBus.Sata, "ST1000LM035", null)),
        ], Ports, new Dictionary<string, double> { ["Karta"] = 300, ["Cíl 1"] = 26, ["Cíl 2"] = 98 });
        Assert.Contains(a, x => x.Kind == AdviceKind.Info && x.Text == "Odhad rychlosti offloadu s tímto zapojením: 26 MB/s (brzdí Cíl 1 WD Elements).");
        Assert.Contains(a, x => x.Text.Contains("Cíl 1 WD Elements") && x.Text.Contains("brzdí disk, ne port"));
    }

    [Fact]
    public void Advice_OrderWarningsFirst()
    {
        var a = Advisor.Advise([Usb("Cíl 1", "WD", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports);
        Assert.Equal(AdviceKind.Warning, a[0].Kind);
        Assert.Equal(a.OrderBy(x => x.Kind).Select(x => x.Kind), a.Select(x => x.Kind));
    }
}
