using System.Globalization;

namespace Offload.Core.Hardware;

public enum AdviceKind { Warning, Info, Tip }

public sealed record Advice(AdviceKind Kind, string Text);

/// A drive in its role for this offload ("Karta", "Cíl 1" …).
public sealed record RoleLink(string Role, DriveLink Link);

public static class Advisor
{
    public static string PortName(UsbConnector c, IReadOnlyDictionary<string, string>? names) =>
        names != null && names.TryGetValue(c.Id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : c.DefaultLabel;

    public static List<Advice> Advise(IReadOnlyList<RoleLink> drives, IReadOnlyList<UsbConnector> connectors,
        IReadOnlyDictionary<string, double>? measuredMBps = null, IReadOnlyDictionary<string, string>? portNames = null)
    {
        var advice = new List<Advice>();
        string Who(RoleLink r) => $"{r.Role} {r.Link.Product}".Trim();
        var freeFast = connectors.Where(c => c.UserVisible && c.SupportsUsb3 && c.Device == null).Select(c => PortName(c, portNames)).ToList();
        var slowOnly = connectors.Where(c => c.UserVisible && !c.SupportsUsb3).Select(c => PortName(c, portNames)).ToList();

        foreach (var r in drives)
        {
            if (r.Link.Usb is not { } u || u.Capable <= u.Link) continue;
            if (!u.ConnectorSupportsUsb3)
            {
                var where = freeFast.Count > 0 ? $" — přepoj do USB 3 portu: {string.Join(", ", freeFast)}" : " — přepoj do USB 3 portu";
                advice.Add(new Advice(AdviceKind.Warning, $"{Who(r)}: umí {UsbSpeeds.Label(u.Capable)}, ale je v portu jen USB 2.0 (port {u.Port}){where}."));
            }
            else
            {
                advice.Add(new Advice(AdviceKind.Warning, $"{Who(r)}: umí {UsbSpeeds.Label(u.Capable)}, ale linka běží jako {UsbSpeeds.Label(u.Link)} — pravděpodobně kabel jen pro USB 2.0 nebo špatný kontakt; zkus jiný kabel nebo port."));
            }
        }

        foreach (var group in drives.Where(r => r.Link.Usb != null).GroupBy(r => r.Link.Usb!.ControllerName).Where(g => g.Count() >= 2))
            advice.Add(new Advice(AdviceKind.Info, $"{string.Join(", ", group.Select(r => r.Role))} jdou přes jeden řadič ({group.Key}) — dělí se o jeho kapacitu."));

        if (measuredMBps != null && measuredMBps.Count > 0)
        {
            var measured = drives.Where(r => measuredMBps.ContainsKey(r.Role)).ToList();
            if (measured.Count > 0)
            {
                var slowest = measured.MinBy(r => measuredMBps[r.Role])!;
                advice.Add(new Advice(AdviceKind.Info,
                    $"Odhad rychlosti offloadu s tímto zapojením: {Mb(measuredMBps[slowest.Role])} MB/s (brzdí {Who(slowest)})."));
            }
            foreach (var r in measured)
            {
                if (r.Link.Usb is not { } u || u.Link < UsbLinkSpeed.Super) continue;
                double ceiling = UsbSpeeds.PracticalMBps(u.Link);
                if (measuredMBps[r.Role] < ceiling / 2)
                    advice.Add(new Advice(AdviceKind.Info,
                        $"{Who(r)}: linka zvládne ~{Mb(ceiling)} MB/s, disk sám dává {Mb(measuredMBps[r.Role])} MB/s — brzdí disk, ne port."));
            }
        }

        if (freeFast.Count > 0) advice.Add(new Advice(AdviceKind.Tip, $"Volné rychlé porty: {string.Join(", ", freeFast)}."));
        if (slowOnly.Count > 0) advice.Add(new Advice(AdviceKind.Tip, $"Porty jen USB 2.0 — na offload je nepoužívej (hodí se pro klávesnici, myš): {string.Join(", ", slowOnly)}."));
        return advice.OrderBy(a => a.Kind).ToList();
    }

    static string Mb(double v) => v.ToString("0", CultureInfo.InvariantCulture);
}
