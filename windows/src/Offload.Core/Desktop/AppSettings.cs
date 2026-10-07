using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core.Desktop;

/// "naming" in settings.json (spec 5.1).
public sealed class NamingSettings
{
    /// Propose the card folder name from the clip names ("A001C003…" → "A001") or the next in line (spec 2.3).
    [JsonPropertyName("suggestFromClips")] public bool SuggestFromClips { get; set; } = true;

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// The app's settings — the Mac `AppSettings` keys (destRoots, verify, analyzeClips, proxy, tools);
/// `portNames` is Windows only: the user's names for USB sockets (`UsbConnector.Id` → name).
public sealed class AppSettings
{
    [JsonPropertyName("destRoots")] public List<string> DestRoots { get; set; } = [];
    [JsonPropertyName("verify")] public bool Verify { get; set; } = true;
    [JsonPropertyName("analyzeClips")] public bool AnalyzeClips { get; set; } = true;
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("tools")] public ExternalTools Tools { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
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

    /// Top-level keys this version does not know (a newer version wrote them): kept as they are and written back,
    /// so going back to an older version never loses projects or rules (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// "proxy" / "tools" sections that did not parse (a value from a newer version): the original JSON, written back by
    /// Save unchanged so the next save never drops them. While one is kept, in-app changes to that section do not persist.
    [JsonIgnore] internal Dictionary<string, JsonElement>? RawSections { get; set; }
}

public static class SettingsStore
{
    public static readonly int[] ProxyHeights = [540, 720, 1080, 2160];

    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The top-level keys Load reads into properties; every other key goes to AppSettings.Extra.
    static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "destRoots", "verify", "analyzeClips", "proxy", "tools", "naming", "portNames" };

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
            void Kept<T>(string key, Func<T, T> set, Func<T> fresh) where T : class
            {
                if (!root.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
                try { set(e.Deserialize<T>(Json) ?? fresh()); }
                catch (Exception)
                {
                    // Not understood by this version: keep the raw section and write it back as it was.
                    (s.RawSections ??= [])[key] = e.Clone();
                    System.Diagnostics.Trace.WriteLine($"settings: section \"{key}\" kept as-is (could not be read)");
                }
            }
            Kept("proxy", (ProxySettings v) => s.Proxy = v, () => new ProxySettings());
            Kept("tools", (ExternalTools v) => s.Tools = v, () => new ExternalTools());
            Kept("naming", (NamingSettings v) => s.Naming = v, () => new NamingSettings());
            Section("portNames", e =>
            {
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in e.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) names[p.Name] = p.Value.GetString()!;
                s.PortNames = names;
            });
            foreach (var (key, value) in root)
                if (!KnownKeys.Contains(key)) (s.Extra ??= [])[key] = value.Clone();   // the document is disposed below
            return Normalize(s);
        }
        catch (Exception)
        {
            // Unreadable, not JSON, or anything unexpected: defaults.
            return new AppSettings();
        }
    }

    internal static AppSettings Normalize(AppSettings s) => Normalize(s, LutCatalog.Default);

    internal static AppSettings Normalize(AppSettings s, LutCatalog catalog)
    {
        s.DestRoots = (s.DestRoots ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        s.Proxy ??= new ProxySettings();
        s.Tools ??= new ExternalTools();
        s.Naming ??= new NamingSettings();
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
        p.LutMode ??= p.LutPath != null ? LutMode.Custom : LutMode.Auto;
        if (catalog.Luts.Count > 0)   // an empty catalog (luts folder missing) must not erase saved choices
        {
            if (catalog.Find(p.LutBuiltin) == null) p.LutBuiltin = null;
            if (catalog.Find(p.LutFallback) == null) p.LutFallback = null;
            if (p.LutMode == LutMode.Builtin && p.LutBuiltin == null) p.LutMode = LutMode.Auto;
        }
        p.ExtraFolder = string.IsNullOrWhiteSpace(p.ExtraFolder) ? null : p.ExtraFolder.Trim();
        return s;
    }

    /// Written to a temporary file next to it, then moved over — a crash never leaves a half-written file.
    public static void Save(AppSettings settings, string path)
    {
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Json);
        if (settings.RawSections is { Count: > 0 } raw && System.Text.Json.Nodes.JsonNode.Parse(bytes) is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var (key, value) in raw) obj[key] = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText());
            bytes = JsonSerializer.SerializeToUtf8Bytes(obj, Json);
        }
        NativeIO.WriteFileDurably(tmp, bytes);
        NativeIO.Move(tmp, path, replace: true);
    }

    /// A deep copy, so a queued job keeps the settings it was started with.
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
}

/// Checks before a start that the proxy/thumbnail settings can work.
public static class ProxyPreflight
{
    /// The custom .cube that must exist before a start (only in custom mode — built-in LUTs ship with the app).
    public static string? LutInUse(ProxySettings p, bool analyzeClips) =>
        (p.Enabled || analyzeClips) && LutResolver.EffectiveMode(p) == LutMode.Custom ? p.LutPath : null;

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
