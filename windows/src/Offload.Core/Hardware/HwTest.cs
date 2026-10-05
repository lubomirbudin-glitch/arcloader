using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Offload.Core.Desktop;

namespace Offload.Core.Hardware;

public sealed record DriveMeasurement(string Role, string Root, double? ReadMBps, double? WriteMBps);

public sealed record HwTestResult(DateTime Time, IReadOnlyList<RoleLink> Drives, IReadOnlyList<UsbConnector> Connectors,
    IReadOnlyList<DriveMeasurement> Measurements, IReadOnlyList<Advice> Advice);

/// The start-up hardware check: topology and links in a moment; with `measure`, a short read of each card and
/// write + read of each destination (a temporary file, deleted). Nothing is ever written to a card.
public static class HwTest
{
    public static string DefaultPath => Path.Combine(AppData.Folder, "hwtest.json");

    internal static List<(string Role, string Root)> Roles(IReadOnlyList<string> cards, IReadOnlyList<string> destinations,
        bool rolesByDrive = false)
    {
        var roles = cards.Select((c, i) => (cards.Count == 1 ? "Karta" : $"Karta {i + 1}", c)).ToList();
        roles.AddRange(destinations.Select((d, i) => (rolesByDrive ? d.TrimEnd('\\') : $"Cíl {i + 1}", d)));
        return roles;
    }

    // MB/s, or null when the timing is unusable (zero/negative/NaN) so no Infinity/NaN reaches the result.
    static double? MBps(Bench.Line line) =>
        line.Seconds > 0 && double.IsFinite(line.Seconds) ? line.Bytes / line.Seconds / 1e6 : null;

    /// Whether two paths are on the same volume (unknown → false).
    public static bool SameVolume(string a, string b)
    {
        try
        {
            return NativeIO.VolumeId(a) is string va && NativeIO.VolumeId(b) is string vb && string.Equals(va, vb, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// cancel: stops the measurement at once — inside the device being measured too (its temporary file is
    /// deleted, it counts as not measured) — and the rest are skipped. A destination on a card's volume is never
    /// written to (no measurement, a warning).
    public static HwTestResult Run(IReadOnlyList<string> cards, IReadOnlyList<string> destinations, bool measure,
        long bytesPerDevice = 1_000_000_000, IReadOnlyDictionary<string, string>? portNames = null,
        bool rolesByDrive = false, CancellationToken cancel = default)
    {
        var roles = Roles(cards, destinations, rolesByDrive);
        var drives = roles.Select(r => new RoleLink(r.Role, DriveLinks.Resolve(r.Root))).ToList();
        var connectors = UsbScanner.Scan().Connectors;
        var cardRoles = roles.Where(r => IsCard(r.Role)).ToList();
        var onCard = new Dictionary<string, string>();   // destination role → card role on the same volume
        foreach (var (role, root) in roles.Where(r => !IsCard(r.Role)))
            if (cardRoles.FirstOrDefault(c => SameVolume(c.Root, root)) is { Role: not null } card)
                onCard[role] = card.Role;

        var measurements = new List<DriveMeasurement>();
        if (measure)
        {
            foreach (var (role, root) in roles)
            {
                if (cancel.IsCancellationRequested) break;
                try
                {
                    if (IsCard(role))
                    {
                        var line = Bench.Read(root, bytesPerDevice, [16 << 20], cancel)[0];
                        measurements.Add(new DriveMeasurement(role, root, MBps(line), null));
                    }
                    else if (onCard.ContainsKey(role))
                    {
                        measurements.Add(new DriveMeasurement(role, root, null, null));   // never write to a card
                    }
                    else
                    {
                        var lines = Bench.Write(root, bytesPerDevice, 16 << 20, cancel);
                        measurements.Add(new DriveMeasurement(role, root, MBps(lines[1]), MBps(lines[0])));
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    break;   // interrupted: not measured, the rest skipped
                }
                catch (Exception)
                {
                    measurements.Add(new DriveMeasurement(role, root, null, null));
                }
            }
        }
        return new HwTestResult(DateTime.UtcNow, drives, connectors, measurements, Advise(drives, connectors, measurements, portNames));
    }

    /// The advice for a test result: a destination on a card's volume, then the Advisor's advice with the
    /// measured pace (cards read, destinations written). Run uses it; the app calls it again after a port is renamed.
    public static List<Advice> Advise(IReadOnlyList<RoleLink> drives, IReadOnlyList<UsbConnector> connectors,
        IReadOnlyList<DriveMeasurement> measurements, IReadOnlyDictionary<string, string>? portNames)
    {
        var cards = drives.Where(d => IsCard(d.Role)).ToList();
        var onCard = drives.Where(d => !IsCard(d.Role))
            .Select(d => (d.Role, Card: cards.FirstOrDefault(c => SameVolume(c.Link.Root, d.Link.Root))?.Role))
            .Where(x => x.Card != null);
        var pace = measurements
            .Select(m => (m.Role, Value: IsCard(m.Role) ? m.ReadMBps : m.WriteMBps))
            .Where(x => x.Value is > 0)
            .ToDictionary(x => x.Role, x => x.Value!.Value);
        return onCard
            .Select(x => new Advice(AdviceKind.Warning,
                $"{x.Role} je na stejném svazku jako {x.Card} — na kartu se nezapisuje, zápis se neměří; zvol jiný cílový disk."))
            .Concat(Advisor.Advise(drives, connectors, pace.Count > 0 ? pace : null, portNames))
            .OrderBy(a => a.Kind).ToList();
    }

    static bool IsCard(string role) => role.StartsWith("Karta", StringComparison.Ordinal);

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Save(HwTestResult result, string path)
    {
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        NativeIO.WriteFileDurably(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, Json)));
    }

    public static HwTestResult? Load(string path)
    {
        try
        {
            var r = File.Exists(path) ? JsonSerializer.Deserialize<HwTestResult>(File.ReadAllText(path), Json) : null;
            // A partial or foreign file: treat as no test rather than hand out null lists.
            return r is { Drives: not null, Connectors: not null, Measurements: not null, Advice: not null } ? r : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
