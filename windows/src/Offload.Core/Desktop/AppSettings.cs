using System.Text.Json;
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
        set
        {
            // Built by hand: keys that differ only in case (a hand-edited file) must not throw — the last one wins.
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, name) in value ?? []) names[id] = name;
            _portNames = names;
        }
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

    /// Missing or corrupt file → defaults; never throws (a settings file must never stop the app from starting).
    /// Each top-level section is read on its own: a section that does not parse (a newer codec name, a wrong type)
    /// falls back to its defaults and the others are kept. A key given twice: the last one wins.
    public static AppSettings Load(string path)
    {
        var s = new AppSettings();
        try
        {
            if (!File.Exists(path)) return s;
            var bytes = File.ReadAllBytes(path);
            // Skip UTF-8 BOM if present (EF BB BF), which Windows PowerShell and old Notepad write
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                bytes = bytes[3..];
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return s;
            var root = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject()) root[p.Name] = p.Value;   // a duplicate key: last wins

            void Section(string key, Action<JsonElement> read)
            {
                if (!root.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
                try
                {
                    read(e);
                }
                catch (Exception)
                {
                    // This section keeps its defaults.
                }
            }

            Section("destRoots", e => s.DestRoots = e.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList());
            Section("verify", e => s.Verify = e.GetBoolean());
            Section("analyzeClips", e => s.AnalyzeClips = e.GetBoolean());
            Section("proxy", e => s.Proxy = e.Deserialize<ProxySettings>(Json) ?? new ProxySettings());
            Section("tools", e => s.Tools = e.Deserialize<ExternalTools>(Json) ?? new ExternalTools());
            Section("portNames", e =>
            {
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in e.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) names[p.Name] = p.Value.GetString()!;
                s.PortNames = names;
            });
            return Normalize(s);
        }
        catch (Exception)
        {
            // Unreadable, not JSON, or anything unexpected: defaults.
            return new AppSettings();
        }
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
        p.ExtraFolder = string.IsNullOrWhiteSpace(p.ExtraFolder) ? null : p.ExtraFolder.Trim();
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
    /// The LUT the next start needs (proxies or thumbnails use it), or null.
    public static string? LutInUse(ProxySettings p, bool analyzeClips) => (p.Enabled || analyzeClips) ? p.LutPath : null;

    /// Why "Spustit offload" must stay disabled, or null. A missing LUT would break thumbnails and proxies.
    /// Touches the disk — the GUI uses the overload with a cached answer instead.
    public static string? Blocking(ProxySettings p, bool analyzeClips) =>
        Blocking(p, analyzeClips, LutInUse(p, analyzeClips) is string lut ? File.Exists(lut) : null);

    /// The same from an answer checked off the UI thread: `lutExists` null = not checked yet (does not block).
    public static string? Blocking(ProxySettings p, bool analyzeClips, bool? lutExists) =>
        LutInUse(p, analyzeClips) is string lut && lutExists == false
            ? $"LUT nenalezen: {Path.GetFileName(lut)} — vyberte jiný nebo ho odeberte."
            : null;

    /// The extra proxy folder the next start copies into (only when proxies are rendered), or null.
    public static string? ExtraFolderInUse(ProxySettings p) => p.Enabled ? p.ExtraFolder : null;

    /// Why the start must wait: the proxy folder is set but missing (a disconnected disk). Touches the disk.
    public static string? ExtraFolderBlocking(ProxySettings p) =>
        ExtraFolderBlocking(p, ExtraFolderInUse(p) is string f ? Directory.Exists(f) : null);

    /// The same from an answer checked off the UI thread: `folderExists` null = not checked yet (does not block).
    public static string? ExtraFolderBlocking(ProxySettings p, bool? folderExists) =>
        ExtraFolderInUse(p) is string f && folderExists == false ? $"Složka pro proxy není dostupná: {f}" : null;

    /// A warning that does not block (copy and verification still run), or null.
    public static string? Warning(ProxySettings p, bool analyzeClips, ExternalTools resolved) =>
        (p.Enabled || analyzeClips) && (resolved.Ffmpeg == null || resolved.Ffprobe == null)
            ? "ffmpeg/ffprobe nenalezen — náhledy a proxy se přeskočí, kopie a ověření proběhnou."
            : null;
}
