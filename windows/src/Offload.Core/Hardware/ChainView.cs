using System.Globalization;

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

    /// "Cíl 1 ST1000" or "Karta reader — linka je plná (USB 2.0 (480 Mb/s))".
    public static string Describe(Brake brake, IReadOnlyList<ChainElement> chain)
    {
        if (brake.Element < 0 || brake.Element >= chain.Count) return "";
        var e = chain[brake.Element];
        var who = $"{e.Role} {e.Product}".Trim();
        return brake.LinkFull && e.Link != null ? $"{who} — linka je plná ({e.Link})" : who;
    }

    public static List<string> Lines(IReadOnlyList<ChainElement> chain, Brake? brake)
    {
        var lines = chain.Select(e =>
        {
            var util = e.Utilization is double u ? $"  {(u * 100).ToString("0", CultureInfo.InvariantCulture)} % linky" : "";
            return $"{e.Role} {e.Product} — {e.Connection}  {e.MBps.ToString("0", CultureInfo.InvariantCulture)} MB/s{util}";
        }).ToList();
        // Shared controller: by identity — two controllers may carry the same name.
        foreach (var g in chain.Where(e => e.ControllerId != null).GroupBy(e => e.ControllerId!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2))
            lines.Add($"Řadič {g.First().ControllerName}: {g.Sum(e => e.MBps).ToString("0", CultureInfo.InvariantCulture)} MB/s");
        if (brake is { } b && Describe(b, chain) is { Length: > 0 } text) lines.Add($"Úzké hrdlo: {text}");
        return lines;
    }
}
