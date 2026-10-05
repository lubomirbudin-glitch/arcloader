using System.Globalization;
using System.Text.Json.Serialization;

namespace Offload.Core.Hardware;

public enum AdviceKind { Warning, Info, Tip }

public sealed record Advice(AdviceKind Kind, string Text);

/// A drive in its role for this offload ("Karta", "Cíl 1" …).
public sealed record RoleLink(string Role, DriveLink Link)
{
    [JsonIgnore]
    public bool IsCard => Role.StartsWith("Karta", StringComparison.Ordinal);
}

public static class Advisor
{
    public static List<Advice> Advise(IReadOnlyList<RoleLink> drives, IReadOnlyList<UsbConnector> connectors,
        IReadOnlyDictionary<string, double>? measuredMBps = null, IReadOnlyDictionary<string, string>? portNames = null)
    {
        var advice = new List<Advice>();
        string Who(RoleLink r) => $"{r.Role} {r.Link.Product}".Trim();
        var freeFastPorts = connectors.Where(c => c.UserVisible && c.SupportsUsb3 == true && c.Device == null).ToList();
        var freeFast = freeFastPorts.Select(c => PortLabels.WithKind(c, portNames)).ToList();
        // Bare names: the sentence already says "jen USB 2.0". Ports of unknown kind are in neither list.
        var slowOnly = connectors.Where(c => c.UserVisible && c.SupportsUsb3 == false).Select(c => PortLabels.Name(c, portNames)).ToList();

        foreach (var r in drives)
        {
            if (r.Link.Usb is not { } u || u.Capable <= u.Link) continue;
            // Never send the drive to the socket it is already in.
            var elsewhere = freeFastPorts.Where(c => !c.Has(u.ConnectorId)).Select(c => PortLabels.WithKind(c, portNames)).ToList();
            var where = elsewhere.Count > 0 ? $": {string.Join(", ", elsewhere)}" : "";
            var port = PortLabels.Of(u, connectors, portNames);
            var text = u.ConnectorUsb3 switch
            {
                false => $"{Who(r)}: umí {UsbSpeeds.Label(u.Capable)}, ale je v portu jen USB 2.0 ({port}) — přepoj do USB 3 portu{where}.",
                true => $"{Who(r)}: umí {UsbSpeeds.Label(u.Capable)}, ale linka běží jako {UsbSpeeds.Label(u.Link)} — pravděpodobně kabel jen pro USB 2.0 nebo špatný kontakt; zkus jiný kabel nebo port.",
                null => $"{Who(r)}: umí {UsbSpeeds.Label(u.Capable)}, ale linka běží jako {UsbSpeeds.Label(u.Link)} ({port}) — zkus jiný kabel nebo USB 3 port{where}.",
            };
            advice.Add(new Advice(AdviceKind.Warning, text));
        }

        // Shared controller: grouped by the controller's identity, not its name (one board can have several "AMD USB 3.10 …").
        var usbDrives = drives.Where(r => r.Link.Usb != null).ToList();
        foreach (var group in usbDrives.GroupBy(r => r.Link.Usb!.ControllerId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2))
        {
            var name = group.First().Link.Usb!.ControllerName;
            advice.Add(new Advice(AdviceKind.Info, $"{string.Join(", ", group.Select(r => r.Role))} jdou přes jeden řadič ({name}) — dělí se o jeho kapacitu."));
            // Reader and disks on one controller while another controller has a free fast socket: split them.
            if (group.Any(r => r.IsCard) && group.Any(r => !r.IsCard))
            {
                var other = freeFastPorts.Where(c => !string.Equals(c.ControllerId, group.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(c => PortLabels.WithKind(c, portNames)).ToList();
                if (other.Count > 0)
                    advice.Add(new Advice(AdviceKind.Tip,
                        $"Čtečku a cílové disky rozděl na různé řadiče — volné rychlé porty na jiném řadiči: {string.Join(", ", other)}."));
            }
        }

        // Shared external hub / dock: the roles share its one uplink. Nearest hub first, each set of roles once.
        var reported = new HashSet<string>();
        var byHub = usbDrives
            .SelectMany(r => r.Link.Usb!.Hubs.Select((h, depth) => (Hub: h, Depth: depth, Role: r)))
            .GroupBy(x => x.Hub.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Hub: g.First().Hub, Depth: g.Min(x => x.Depth), Roles: g.Select(x => x.Role.Role).Distinct().ToList()))
            .Where(x => x.Roles.Count >= 2)
            .OrderBy(x => x.Depth);
        foreach (var (hub, _, roles) in byHub)
        {
            var who = string.Join(", ", roles);
            if (reported.Add(who))
                advice.Add(new Advice(AdviceKind.Info, $"{who} jdou přes jeden hub ({hub.Name}) — dělí se o jeho linku."));
        }

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
