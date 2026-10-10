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
        var a = Advisor.Advise([Usb("Destination 1", "WD Elements", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.Contains("Destination 1 WD Elements", w.Text);
        Assert.Contains("capable of USB 3 (5 Gb/s)", w.Text);
        Assert.Contains("USB 2.0-only port (port 9)", w.Text);
        Assert.Contains("port 5 USB 3", w.Text);
        Assert.Contains("port 11 USB-C", w.Text);
    }

    // Final review 1: the warning names the socket like the port list does (user names included), not the raw port.
    [Fact]
    public void Advice_WarningUsesSocketLabelAndNames()
    {
        var names = new Dictionary<string, string> { ["ROOT#9"] = "rear bottom" };
        var a = Advisor.Advise([Usb("Destination 1", "WD", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports, portNames: names);
        Assert.Contains("USB 2.0-only port (rear bottom)", Assert.Single(a, x => x.Kind == AdviceKind.Warning).Text);
    }

    // Review focus 2
    [Fact]
    public void Advice_CapableDeviceSlowLinkOnUsb3Connector()
    {
        var a = Advisor.Advise([Usb("Card", "SanDisk reader", UsbLinkSpeed.High, UsbLinkSpeed.Super, true, 5)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.Contains("cable", w.Text);
        Assert.DoesNotContain("USB 2.0-only port", w.Text);
    }

    // Final review 8: unknown socket kind → no "jen USB 2.0" claim.
    [Fact]
    public void Advice_UnknownSocketKind_NoUsb2Claim()
    {
        var a = Advisor.Advise([Usb("Destination 1", "SSD", UsbLinkSpeed.High, UsbLinkSpeed.Super, null, 5)], Ports);
        var w = Assert.Single(a, x => x.Kind == AdviceKind.Warning);
        Assert.DoesNotContain("USB 2.0-only port", w.Text);
        Assert.Contains("USB 3 port", w.Text);
        Assert.DoesNotContain("port 5 USB 3", w.Text);   // never "move to the socket you are in"
    }

    // Review focus 5
    [Fact]
    public void Advice_SharedController()
    {
        var a = Advisor.Advise(
        [
            Usb("Card", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Destination 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
        ], Ports);
        Assert.Contains(a, x => x.Kind == AdviceKind.Info && x.Text.Contains("Card, Destination 1") && x.Text.Contains("one controller"));
        Assert.DoesNotContain(a, x => x.Text.Contains("different controllers"));   // no other controller here
    }

    // Final review 4: two controllers with the same name are two controllers.
    [Fact]
    public void Advice_SameNamedControllers_NotShared()
    {
        const string Amd = "AMD USB 3.10 eXtensible Host Controller - 1.10 (Microsoft)";
        var a = Advisor.Advise(
        [
            Usb("Card", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5, Amd, "PCI\\AMD_1"),
            Usb("Destination 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11, Amd, "PCI\\AMD_2"),
        ], Ports);
        Assert.DoesNotContain(a, x => x.Text.Contains("one controller"));
    }

    // Final review 7: reader and disks on one controller, another controller has a free fast socket → split them.
    [Fact]
    public void Advice_SplitAcrossControllers()
    {
        var ports = Ports.Append(Port(3, 4, false, true, controllerId: "PCI\\ASMEDIA", label: "ASMedia xHCI · port 3")).ToArray();
        var a = Advisor.Advise(
        [
            Usb("Card", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Destination 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
        ], ports);
        var tip = Assert.Single(a, x => x.Text.Contains("different controllers"));
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
            Usb("Card", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 1, hubs: [dock]),
            Usb("Destination 1", "SSD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 2, hubs: [new UsbHubRef("USB\\INNER", "Hub"), dock]),
            Usb("Destination 2", "HDD", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
        ], Ports);
        var info = Assert.Single(a, x => x.Text.Contains("one hub"));
        Assert.Equal(AdviceKind.Info, info.Kind);
        Assert.Equal("Card, Destination 1 go through one hub (USB3.0 Hub) — they share its link.", info.Text);
    }

    [Fact]
    public void Advice_PortListsAndNames()
    {
        var names = new Dictionary<string, string> { ["ROOT#5"] = "front left" };
        var a = Advisor.Advise([], Ports, portNames: names);
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.StartsWith("Free fast ports: ") && x.Text.Contains("front left") && x.Text.Contains("port 11 USB-C"));
        // Final review 9: the "jen USB 2.0" sentence lists bare names.
        Assert.Contains(a, x => x.Kind == AdviceKind.Tip && x.Text.EndsWith("mouse): port 9."));
    }

    [Fact]
    public void Advice_MeasuredEstimateAndSlowDisk()
    {
        var a = Advisor.Advise(
        [
            Usb("Card", "reader", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 5),
            Usb("Destination 1", "WD Elements", UsbLinkSpeed.Super, UsbLinkSpeed.Super, true, 11),
            new RoleLink("Destination 2", new DriveLink("D:\\", StorageBus.Sata, "ST1000LM035", null)),
        ], Ports, new Dictionary<string, double> { ["Card"] = 300, ["Destination 1"] = 26, ["Destination 2"] = 98 });
        Assert.Contains(a, x => x.Kind == AdviceKind.Info && x.Text == "Estimated offload speed with this setup: 26 MB/s (bottleneck: Destination 1 WD Elements).");
        Assert.Contains(a, x => x.Text.Contains("Destination 1 WD Elements") && x.Text.Contains("the bottleneck is the disk, not the port"));
    }

    [Fact]
    public void Advice_OrderWarningsFirst()
    {
        var a = Advisor.Advise([Usb("Destination 1", "WD", UsbLinkSpeed.High, UsbLinkSpeed.Super, false, 9)], Ports);
        Assert.Equal(AdviceKind.Warning, a[0].Kind);
        Assert.Equal(a.OrderBy(x => x.Kind).Select(x => x.Kind), a.Select(x => x.Kind));
    }
}
