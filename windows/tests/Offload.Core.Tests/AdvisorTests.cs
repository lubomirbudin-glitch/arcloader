using Offload.Core.Hardware;

namespace Offload.Core.Tests;

public class AdvisorTests
{
    const string Hc = "Intel(R) USB 3.20 eXtensible Host Controller";
    const string HcId = "PCI\\VEN_8086";

    static RoleLink Usb(string role, string product, UsbLinkSpeed link, UsbLinkSpeed cap, bool? connectorUsb3, int port,
        string controller = Hc, string controllerId = HcId, params UsbHubRef[] hubs) =>
        new(role, new DriveLink($"{role}:\\", StorageBus.Usb, product,
            new UsbLink(controllerId, controller, $"ROOT#{port}", port, port, link, cap, connectorUsb3, false) { Hubs = hubs }));

    static UsbConnector Port(int usb2, int usb3, bool typeC, bool visible, UsbDevice? device = null, string controllerId = HcId,
        string label = "", bool? usb3Known = null) =>
        new($"ROOT#{usb2}", "ROOT", controllerId, Hc, usb2, usb2, usb3, usb3Known ?? usb3 > 0, typeC, visible, device,
            label.Length > 0 ? label : $"port {usb2}");

    static readonly UsbConnector[] Ports =
    [
        Port(9, 0, false, true),
        Port(5, 20, false, true),
        Port(11, 26, true, true),
        Port(1, 17, false, true, new UsbDevice("x", 1, 1, "Keyboard", UsbLinkSpeed.Full, UsbLinkSpeed.Full, null)),
        Port(14, 0, false, false),
    ];

    [Fact]
    public void Advice_CapableDeviceOnUsb2OnlyPort()
    {
        var a = Advisor.Advise([Usb("Cíl 1", "WD Elements", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.Contains("Cíl 1 WD Elements", w.Text);
        Assert.Contains("umí USB 3 (5 Gb/s)", w.Text);
        Assert.Contains("jen USB 2.0 (port 9)", w.Text);
        Assert.Contains("port 5 USB 3", w.Text);
        Assert.Contains("port 11 USB-C", w.Text);
    }

    // Final review 1: the warning names the socket like the port list does (user names included), not the raw port.
    [Fact]
    public void Advice_WarningUsesSocketLabelAndNames()
    {
        var names = new Dictionary<string, string> { ["ROOT#9"] = "zadní dole" };
        var a = Advisor.Advise([Usb("Cíl 1", "WD", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports, portNames: names);
        Assert.Contains("jen USB 2.0 (zadní dole)", Assert.Single(a, x => x.Kind == AdviceKind.Warning).Text);
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

    // Final review 8: unknown socket kind → no "jen USB 2.0" claim.
    [Fact]
    public void Advice_UnknownSocketKind_NoUsb2Claim()
    {
        var a = Advisor.Advise([Usb("Cíl 1", "SSD", UsbLinkSpeed.High, UsbLinkSpeed.Super, null, 5)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.DoesNotContain("jen USB 2.0", w.Text);
        Assert.Contains("USB 3 port", w.Text);
        Assert.DoesNotContain("port 5 USB 3", w.Text);   // never "move to the socket you are in"
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
        Assert.DoesNotContain(a, x => x.Text.Contains("různé řadiče"));   // no other controller here
    }

    // Final review 4: two controllers with the same name are two controllers.
    [Fact]
    public void Advice_SameNamedControllers_NotShared()
    {
        const string Amd = "AMD USB 3.10 eXtensible Host Controller - 1.10 (Microsoft)";
        var a = Advisor.Advise(
        [
            Usb("Karta", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5, Amd, "PCI\\AMD_1"),
            Usb("Cíl 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11, Amd, "PCI\\AMD_2"),
        ], Ports);
        Assert.DoesNotContain(a, x => x.Text.Contains("jeden řadič"));
    }

    // Final review 7: reader and disks on one controller, another controller has a free fast socket → split them.
    [Fact]
    public void Advice_SplitAcrossControllers()
    {
        var ports = Ports.Append(Port(3, 4, false, true, controllerId: "PCI\\ASMEDIA", label: "ASMedia xHCI · port 3")).ToArray();
        var a = Advisor.Advise(
        [
            Usb("Karta", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Cíl 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
        ], ports);
        var tip = Assert.Single(a, x => x.Text.Contains("různé řadiče"));
        Assert.Equal(AdviceKind.Tip, tip.Kind);
        Assert.Contains("ASMedia xHCI · port 3 USB 3", tip.Text);
        Assert.DoesNotContain("port 5", tip.Text);
    }

    // Final review 7: roles behind one dock / hub share its uplink.
    [Fact]
    public void Advice_SharedHub()
    {
        var dock = new UsbHubRef("USB\\DOCK", "USB3.0 Hub");
        var a = Advisor.Advise(
        [
            Usb("Karta", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 1, hubs: [dock]),
            Usb("Cíl 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 2, hubs: [new UsbHubRef("USB\\INNER", "Hub"), dock]),
            Usb("Cíl 2", "HDD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
        ], Ports);
        var info = Assert.Single(a, x => x.Text.Contains("jeden hub"));
        Assert.Equal(AdviceKind.Info, info.Kind);
        Assert.Equal("Karta, Cíl 1 jdou přes jeden hub (USB3.0 Hub) — dělí se o jeho linku.", info.Text);
    }

    [Fact]
    public void Advice_PortListsAndNames()
    {
        var names = new Dictionary<string, string> { ["ROOT#5"] = "přední levý" };
        var a = Advisor.Advise([], Ports, portNames: names);
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.StartsWith("Volné rychlé porty: ") && x.Text.Contains("přední levý") && x.Text.Contains("port 11 USB-C"));
        // Final review 9: the "jen USB 2.0" sentence lists bare names.
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.EndsWith("myš): port 9."));
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
