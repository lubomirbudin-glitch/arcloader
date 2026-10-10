using System.Globalization;
using System.Text.Json.Serialization;

namespace Offload.Core.Hardware;

public enum AdviceKind { Warning, Info, Tip }

public sealed record Advice(AdviceKind Kind, string Text);

/// A drive in its role for this offload ("Card", "Destination 1" …; before 0.9.0 "Karta", "Cíl 1"). `Card`: whether it is
/// a card — set explicitly by HwTest (saved in hwtest.json as "card"); null (an older file, a caller that does not say)
/// falls back to the role's text, which also knows the old Czech "Karta".
public sealed record RoleLink(string Role, DriveLink Link,
    [property: JsonPropertyName("card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Card = null)
{
    [JsonIgnore]
    public bool IsCard => Card ?? IsCardRole(Role);

    public const string CardRole = "Card";

    public static string DestinationRole(int number) => $"Destination {number}";

    /// Only for a role without the explicit flag: "Card", "Card 2" — and the Czech "Karta" of hwtest.json files written
    /// before 0.9.0.
    public static bool IsCardRole(string role) =>
        role.StartsWith(CardRole, StringComparison.Ordinal) || role.StartsWith("Karta", StringComparison.Ordinal);
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
        // Bare names: the sentence already says "USB 2.0 only". Ports of unknown kind are in neither list.
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
                false => $"{Who(r)}: capable of {UsbSpeeds.Label(u.Capable)}, but plugged into a USB 2.0-only port ({port}) — move it to a USB 3 port{where}.",
                true => $"{Who(r)}: capable of {UsbSpeeds.Label(u.Capable)}, but the link runs as {UsbSpeeds.Label(u.Link)} — probably a USB 2.0-only cable or a bad contact; try another cable or port.",
                null => $"{Who(r)}: capable of {UsbSpeeds.Label(u.Capable)}, but the link runs as {UsbSpeeds.Label(u.Link)} ({port}) — try another cable or a USB 3 port{where}.",
            };
            advice.Add(new Advice(AdviceKind.Warning, text));
        }

        // Shared controller: grouped by the controller's identity, not its name (one board can have several "AMD USB 3.10 …").
        var usbDrives = drives.Where(r => r.Link.Usb != null).ToList();
        foreach (var group in usbDrives.GroupBy(r => r.Link.Usb!.ControllerId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2))
        {
            var name = group.First().Link.Usb!.ControllerName;
            advice.Add(new Advice(AdviceKind.Info, $"{string.Join(", ", group.Select(r => r.Role))} go through one controller ({name}) — they share its bandwidth."));
            // Reader and disks on one controller while another controller has a free fast socket: split them.
            if (group.Any(r => r.IsCard) && group.Any(r => !r.IsCard))
            {
                var other = freeFastPorts.Where(c => !string.Equals(c.ControllerId, group.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(c => PortLabels.WithKind(c, portNames)).ToList();
                if (other.Count > 0)
                    advice.Add(new Advice(AdviceKind.Tip,
                        $"Put the reader and the destination disks on different controllers — free fast ports on another controller: {string.Join(", ", other)}."));
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
                advice.Add(new Advice(AdviceKind.Info, $"{who} go through one hub ({hub.Name}) — they share its link."));
        }

        if (measuredMBps != null && measuredMBps.Count > 0)
        {
            var measured = drives.Where(r => measuredMBps.ContainsKey(r.Role)).ToList();
            if (measured.Count > 0)
            {
                var slowest = measured.MinBy(r => measuredMBps[r.Role])!;
                advice.Add(new Advice(AdviceKind.Info,
                    $"Estimated offload speed with this setup: {Mb(measuredMBps[slowest.Role])} MB/s (bottleneck: {Who(slowest)})."));
            }
            foreach (var r in measured)
            {
                if (r.Link.Usb is not { } u || u.Link < UsbLinkSpeed.Super) continue;
                double ceiling = UsbSpeeds.PracticalMBps(u.Link);
                if (measuredMBps[r.Role] < ceiling / 2)
                    advice.Add(new Advice(AdviceKind.Info,
                        $"{Who(r)}: the link can do ~{Mb(ceiling)} MB/s, the disk itself gives {Mb(measuredMBps[r.Role])} MB/s — the bottleneck is the disk, not the port."));
            }
        }

        if (freeFast.Count > 0) advice.Add(new Advice(AdviceKind.Tip, $"Free fast ports: {string.Join(", ", freeFast)}."));
        if (slowOnly.Count > 0) advice.Add(new Advice(AdviceKind.Tip, $"USB 2.0-only ports — do not use them for offloads (fine for a keyboard or a mouse): {string.Join(", ", slowOnly)}."));
        return advice.OrderBy(a => a.Kind).ToList();
    }

    static string Mb(double v) => v.ToString("0", CultureInfo.InvariantCulture);
}
