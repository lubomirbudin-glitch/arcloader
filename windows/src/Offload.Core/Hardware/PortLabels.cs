using System.Text.RegularExpressions;

namespace Offload.Core.Hardware;

/// The one place that names a USB socket — drive lines, the port list, advice and the copy chain all go through
/// here, so one socket always reads the same. User names (`UsbConnector.Id → název`) replace the default label.
public static partial class PortLabels
{
    /// The socket's name: the user's name, else the default unique label ("port 6", "port 7 › 2").
    public static string Name(UsbConnector c, IReadOnlyDictionary<string, string>? names = null)
    {
        if (names != null)
            foreach (var id in new[] { c.Id }.Concat(c.AltIds))
                if (names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)) return n.Trim();
        return c.Label;
    }

    /// Name plus what the socket can do: "port 6 USB 3", "port 3 jen USB 2.0", "port 4" (unknown).
    public static string WithKind(UsbConnector c, IReadOnlyDictionary<string, string>? names = null) =>
        c.Kind.Length > 0 ? $"{Name(c, names)} {c.Kind}" : Name(c, names);

    public static UsbConnector? Find(IReadOnlyList<UsbConnector>? connectors, string connectorId) =>
        connectors?.FirstOrDefault(c => c.Has(connectorId));

    /// The name of the socket a drive is plugged into; without the topology, "port N" from the link itself.
    public static string Of(UsbLink u, IReadOnlyList<UsbConnector>? connectors, IReadOnlyDictionary<string, string>? names = null) =>
        Find(connectors, u.ConnectorId) is { } c ? Name(c, names) : $"port {u.ConnectorPort}";

    /// One row of the port list: "port 6 USB 3  Elements 25A2 — USB 3 (5 Gb/s)  ← E: (Cíl 1)".
    public static string ListRow(UsbConnector c, IReadOnlyList<RoleLink> drives, IReadOnlyDictionary<string, string>? names = null,
        int labelWidth = 24)
    {
        var device = c.Device is { } dev ? $"{dev.Name} — {UsbSpeeds.Label(dev.Link)}" : "volný";
        var here = drives.Where(d => d.Link.Usb is { } u && c.Has(u.ConnectorId))
            .Select(d =>
            {
                var letter = d.Link.Root.TrimEnd('\\');
                return d.Role == letter ? letter : $"{letter} ({d.Role})";
            }).ToList();
        var tag = here.Count > 0 ? $"  ← {string.Join(", ", here)}" : "";
        return $"{WithKind(c, names).PadRight(labelWidth)} {device}{tag}";
    }

    /// "Intel(R) USB 3.20 eXtensible Host Controller - 1.20 (Microsoft)" → "Intel USB 3.20 xHCI".
    public static string ShortController(string name)
    {
        var s = TrademarkRe().Replace(name, "");
        s = DriverSuffixRe().Replace(s, "");
        s = s.Replace("eXtensible Host Controller", "xHCI", StringComparison.OrdinalIgnoreCase)
             .Replace("Extensible Host Controller", "xHCI", StringComparison.OrdinalIgnoreCase)
             .Replace("Enhanced Host Controller", "EHCI", StringComparison.OrdinalIgnoreCase);
        s = SpacesRe().Replace(s, " ").Trim();
        return s.Length > 0 ? s : name;
    }

    [GeneratedRegex(@"\((R|TM|C)\)|®|™", RegexOptions.IgnoreCase)]
    private static partial Regex TrademarkRe();

    // " - 1.20 (Microsoft)", " - 1.10 (AMD)", trailing "(Microsoft)".
    [GeneratedRegex(@"\s+-\s+[\d.]+(\s*\([^)]*\))?\s*$|\s*\((Microsoft|AMD|Intel)\)\s*$")]
    private static partial Regex DriverSuffixRe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRe();
}
