namespace Offload.Core.Hardware;

/// One link of the chain. Link: the USB speed label ("USB 3 (5 Gb/s)") or null for internal disks.
public sealed record ChainElement(string Role, string Product, string Connection, string? Link, double? LinkMBps, double MBps,
    string? ControllerId, string? ControllerName)
{
    public double? Utilization => LinkMBps is double l && l > 0 ? MBps / l : null;
}

/// The chain card → reader/link → controller → disks with live throughput, and what is holding it back.
/// In the CLI only the brake shows (status line); the full per-element view is for the GUI.
public static class ChainView
{
    public static List<ChainElement> Build(IReadOnlyList<RoleLink> links, IReadOnlyList<double> mbps,
        IReadOnlyList<UsbConnector>? connectors = null, IReadOnlyDictionary<string, string>? portNames = null) =>
        links.Select((r, i) =>
        {
            var u = r.Link.Usb;
            return new ChainElement(r.Role, r.Link.Product, r.Link.Connection(connectors, portNames), u != null ? UsbSpeeds.Label(u.Link) : null,
                LinkCeiling(r), i < mbps.Count ? mbps[i] : 0, u?.ControllerId, u?.ControllerName);
        }).ToList();

    /// Realistic MB/s ceiling of the element's USB link; null for internal disks (no "% linky").
    public static double? LinkCeiling(RoleLink r) => r.Link.Usb is { } u && UsbSpeeds.PracticalMBps(u.Link) > 0 ? UsbSpeeds.PracticalMBps(u.Link) : null;

}
