using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Offload.Core.Desktop;

/// The app's settings — the Mac `AppSettings` keys (destRoots, verify, analyzeClips, proxy, tools);
/// `portNames` is Windows only: the user's names for USB sockets (`UsbConnector.Id` → name).
public sealed class AppSettings
{
    [JsonPropertyName("destRoots")] public List<string> DestRoots { get; set; } = [];
    [JsonPropertyName("verify")] public bool Verify { get; set; } = true;
    [JsonPropertyName("analyzeClips")] public bool AnalyzeClips { get; set; } = true;
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("portNames")]
    public Dictionary<string, string> PortNames
    {
        get => _portNames;
        set => _portNames = value == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    }
    private Dictionary<string, string> _portNames = new(StringComparer.OrdinalIgnoreCase);
}

public static class SettingsStore
{
    public static readonly int[] ProxyHeights = [540, 720, 1080, 2160];

    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// Missing or corrupt file → defaults. Each top-level section is read on its own: a section that does not
    /// parse (a newer codec name, a wrong type) falls back to its defaults and the others are kept.
    public static AppSettings Load(string path)
    {
        var s = new AppSettings();
        JsonObject? root;
        try
        {
            if (!File.Exists(path)) return s;
            var bytes = File.ReadAllBytes(path);
            // Skip UTF-8 BOM if present (EF BB BF), which Windows PowerShell and old Notepad write
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                bytes = bytes[3..];
            root = JsonNode.Parse(bytes) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return s;
        }
        if (root == null) return s;

        T? Get<T>(string key)
        {
            try
            {
                return root[key] is JsonNode n ? n.Deserialize<T>(Json) : default;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or NotSupportedException)
            {
                return default;
            }
        }

        if (Get<List<string>>("destRoots") is { } roots) s.DestRoots = roots;
        if (Get<bool?>("verify") is bool verify) s.Verify = verify;
        if (Get<bool?>("analyzeClips") is bool analyze) s.AnalyzeClips = analyze;
        if (Get<ProxySettings>("proxy") is { } proxy) s.Proxy = proxy;
        if (Get<ExternalTools>("tools") is { } tools) s.Tools = tools;
        if (Get<Dictionary<string, string>>("portNames") is { } names) s.PortNames = names;
        return Normalize(s);
    }

    internal static AppSettings Normalize(AppSettings s)
    {
        s.DestRoots = (s.DestRoots ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        s.Proxy ??= new ProxySettings();
        s.Tools ??= new ExternalTools();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) names[id] = name.Trim();
        s.PortNames = names;
        var p = s.Proxy;
        if (!ProxyHeights.Contains(p.Height)) p.Height = 1080;
        p.BitrateMbps = Math.Clamp(p.BitrateMbps, 2, 50);
        p.FrameScale = double.IsFinite(p.FrameScale) ? Math.Clamp(p.FrameScale, 0.5, 1.0) : 1.0;
        if (p.FrameAspect is double a && (!double.IsFinite(a) || a <= 0)) p.FrameAspect = null;
        if (string.IsNullOrWhiteSpace(p.LutPath)) p.LutPath = null;
        return s;
    }

    /// Written to a temporary file next to it, then moved over — a crash never leaves a half-written file.
    public static void Save(AppSettings settings, string path)
    {
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        NativeIO.WriteFileDurably(tmp, JsonSerializer.SerializeToUtf8Bytes(settings, Json));
        NativeIO.Move(tmp, path, replace: true);
    }

    /// A deep copy, so a queued job keeps the settings it was started with.
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
}

/// Checks before a start that the proxy/thumbnail settings can work.
public static class ProxyPreflight
{
    /// Why "Spustit offload" must stay disabled, or null. A missing LUT would break thumbnails and proxies.
    public static string? Blocking(ProxySettings p, bool analyzeClips) =>
        (p.Enabled || analyzeClips) && p.LutPath is string lut && !File.Exists(lut)
            ? $"LUT nenalezen: {Path.GetFileName(lut)} — vyberte jiný nebo ho odeberte."
            : null;

    /// A warning that does not block (copy and verification still run), or null.
    public static string? Warning(ProxySettings p, bool analyzeClips, ExternalTools resolved) =>
        (p.Enabled || analyzeClips) && (resolved.Ffmpeg == null || resolved.Ffprobe == null)
            ? "ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou."
            : null;
}
