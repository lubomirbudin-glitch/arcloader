using System.Globalization;

namespace Offload.Core.Hardware;

public sealed record ChainElement(string Role, string Product, string Connection, double? LinkMBps, double MBps, string? Controller)
{
    public double? Utilization => LinkMBps is double l && l > 0 ? MBps / l : null;
}

/// The chain card → reader/link → controller → disks with live throughput, and what is holding it back.
public static class ChainView
{
    public static List<ChainElement> Build(IReadOnlyList<RoleLink> links, IReadOnlyList<double> mbps) =>
        links.Select((r, i) =>
        {
            var u = r.Link.Usb;
            var connection = u != null
                ? $"{UsbSpeeds.Label(u.Link)}, port {u.Port}"
                : r.Link.Bus switch { StorageBus.Sata => "SATA", StorageBus.Nvme => "NVMe", StorageBus.Network => "síť", _ => r.Link.Bus.ToString() };
            double? ceiling = u != null ? UsbSpeeds.PracticalMBps(u.Link) : null;
            return new ChainElement(r.Role, r.Link.Product, connection, ceiling, i < mbps.Count ? mbps[i] : 0, u?.ControllerName);
        }).ToList();

    /// The element that sets the pace: the slowest one moving data; "linka je plná" when it runs at > 85 % of its link.
    public static string? Bottleneck(IReadOnlyList<ChainElement> chain)
    {
        var active = chain.Where(e => e.MBps > 0).ToList();
        if (active.Count == 0) return null;
        var slowest = active.MinBy(e => e.MBps)!;
        var who = $"{slowest.Role} {slowest.Product}".Trim();
        return slowest.Utilization > 0.85 ? $"{who} — linka je plná ({slowest.Connection.Split(", port")[0]})" : who;
    }

    public static List<string> Lines(IReadOnlyList<ChainElement> chain)
    {
        var lines = chain.Select(e =>
        {
            var util = e.Utilization is double u ? $"  {(u * 100).ToString("0", CultureInfo.InvariantCulture)} % linky" : "";
            return $"{e.Role} {e.Product} — {e.Connection}  {e.MBps.ToString("0", CultureInfo.InvariantCulture)} MB/s{util}";
        }).ToList();
        foreach (var g in chain.Where(e => e.Controller != null).GroupBy(e => e.Controller!).Where(g => g.Count() >= 2))
            lines.Add($"Řadič {g.Key}: {g.Sum(e => e.MBps).ToString("0", CultureInfo.InvariantCulture)} MB/s");
        if (Bottleneck(chain) is string b) lines.Add($"Úzké hrdlo: {b}");
        return lines;
    }
}
