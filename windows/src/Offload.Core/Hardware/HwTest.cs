using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.Hardware;

public sealed record DriveMeasurement(string Role, string Root, double? ReadMBps, double? WriteMBps);

public sealed record HwTestResult(DateTime Time, IReadOnlyList<RoleLink> Drives, IReadOnlyList<UsbConnector> Connectors,
    IReadOnlyList<DriveMeasurement> Measurements, IReadOnlyList<Advice> Advice);

/// The start-up hardware check: topology and links in a moment; with `measure`, a short read of each card and
/// write + read of each destination (a temporary file, deleted). Nothing is ever written to a card.
public static class HwTest
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Offload", "hwtest.json");

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

    public static HwTestResult Run(IReadOnlyList<string> cards, IReadOnlyList<string> destinations, bool measure,
        long bytesPerDevice = 1_000_000_000, IReadOnlyDictionary<string, string>? portNames = null,
        bool rolesByDrive = false)
    {
        var roles = Roles(cards, destinations, rolesByDrive);
        var drives = roles.Select(r => new RoleLink(r.Role, DriveLinks.Resolve(r.Root))).ToList();
        var connectors = UsbScanner.Scan().Connectors;
        var measurements = new List<DriveMeasurement>();
        if (measure)
        {
            foreach (var (role, root) in roles)
            {
                bool isCard = role.StartsWith("Karta", StringComparison.Ordinal);
                try
                {
                    if (isCard)
                    {
                        var line = Bench.Read(root, bytesPerDevice, [16 << 20])[0];
                        measurements.Add(new DriveMeasurement(role, root, MBps(line), null));
                    }
                    else
                    {
                        var lines = Bench.Write(root, bytesPerDevice, 16 << 20);
                        measurements.Add(new DriveMeasurement(role, root, MBps(lines[1]), MBps(lines[0])));
                    }
                }
                catch (Exception)
                {
                    measurements.Add(new DriveMeasurement(role, root, null, null));
                }
            }
        }
        // The offload pace: cards are read, destinations written.
        var pace = measurements
            .Select(m => (m.Role, Value: m.Role.StartsWith("Karta", StringComparison.Ordinal) ? m.ReadMBps : m.WriteMBps))
            .Where(x => x.Value is > 0)
            .ToDictionary(x => x.Role, x => x.Value!.Value);
        var advice = Advisor.Advise(drives, connectors, pace.Count > 0 ? pace : null, portNames);
        return new HwTestResult(DateTime.UtcNow, drives, connectors, measurements, advice);
    }

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
            return File.Exists(path) ? JsonSerializer.Deserialize<HwTestResult>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
