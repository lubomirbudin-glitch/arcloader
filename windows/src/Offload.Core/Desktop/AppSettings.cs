using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Offload.Core.Notify;
using Offload.Core.Outputs;

namespace Offload.Core.Desktop;

/// "naming" in settings.json (spec 5.1).
public sealed class NamingSettings
{
    /// Propose the card folder name from the clip names ("A001C003…" → "A001") or the next in line (spec 2.3).
    [JsonPropertyName("suggestFromClips")] public bool SuggestFromClips { get; set; } = true;

    /// Keys of this section a newer version wrote: kept and written back (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// What a project holds (spec 4.1, R17): the top level of settings.json is the profile "Bez projektu", every entry of
/// `projects` is a named profile of the same shape. Tools, port names and the update check stay global (AppSettings).
public class SettingsProfile
{
    [JsonPropertyName("destRoots")] public List<string> DestRoots { get; set; } = [];
    [JsonPropertyName("verify")] public bool Verify { get; set; } = true;
    [JsonPropertyName("analyzeClips")] public bool AnalyzeClips { get; set; } = true;
    [JsonPropertyName("proxy")] public ProxySettings Proxy { get; set; } = new();
    [JsonPropertyName("naming")] public NamingSettings Naming { get; set; } = new();
    /// "xxh64" (default) or "both" = xxh64 + MD5 (spec 3.5, R16). A value a newer version wrote is kept as it is and
    /// read as xxh64 (HashKinds.FromSetting).
    [JsonPropertyName("hash")] public string Hash { get; set; } = "xxh64";
    /// Clip lists and the day report — spec 3.2, 3.3.
    [JsonPropertyName("outputs")] public OutputSettings Outputs { get; set; } = new();
    /// Windows notification and ntfy — spec 4.3.
    [JsonPropertyName("notify")] public NotifySettings Notify { get; set; } = new();

    /// Keys this version does not know (a newer version wrote them): kept as they are and written back, so going back
    /// to an older version never loses anything (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// Sections that did not parse (a value from a newer version): the original JSON, written back by Save unchanged so
    /// the next save never drops them. While one is kept, in-app changes to that section do not persist.
    [JsonIgnore] internal Dictionary<string, JsonElement>? RawSections { get; set; }
}

/// One entry of `projects` (spec 4.1): a named profile.
public sealed class ProjectSettings : SettingsProfile
{
    [JsonPropertyName("name"), JsonPropertyOrder(-1)] public string Name { get; set; } = "";
}

/// The app's settings — the Mac `AppSettings` keys (destRoots, verify, analyzeClips, proxy, tools);
/// `portNames` is Windows only: the user's names for USB sockets (`UsbConnector.Id` → name).
/// The profile keys at the top level are "Bez projektu"; `projects` and `activeProject` hold the named ones (R17).
public sealed class AppSettings : SettingsProfile
{
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

    /// The named profiles (spec 4.1), in the order they were created.
    [JsonPropertyName("projects")] public List<ProjectSettings> Projects { get; set; } = [];
    /// The project in use, or null = "Bez projektu" (the top-level keys).
    [JsonPropertyName("activeProject")] public string? ActiveProject { get; set; }

    /// Entries of `projects` this version cannot use (not an object, or an invalid name — e.g. a longer one a newer version
    /// allows): the original JSON, appended after the readable projects by Save so a downgrade never erases them (M3, R24).
    [JsonIgnore] internal List<JsonElement> UnreadableProjects { get; set; } = [];

    /// The profile the app shows and edits now: the active project, else the top level.
    [JsonIgnore] public SettingsProfile Active => Projects.FirstOrDefault(p => p.Name == ActiveProject) as SettingsProfile ?? this;
}

public static class SettingsStore
{
    public static readonly int[] ProxyHeights = [540, 720, 1080, 2160];

    public static string DefaultPath => Path.Combine(AppData.Folder, "settings.json");

    /// The keys of a profile (top level and every project) Load reads into properties.
    static readonly string[] ProfileKeys = ["destRoots", "verify", "analyzeClips", "proxy", "naming", "hash", "outputs", "notify"];
    /// The global top-level keys; every other top-level key goes to AppSettings.Extra.
    static readonly string[] GlobalKeys = ["tools", "portNames", "projects", "activeProject"];

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// Missing or corrupt file → defaults; never throws (a settings file must never stop the app from starting).
    /// Each section is read on its own: a section that does not parse (a newer codec name, a wrong type) falls back to
    /// its defaults and the others are kept. A key given twice: the last one wins; so does a project name given twice.
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
            var root = Members(doc.RootElement);
            ReadProfile(root, s, GlobalKeys);
            Kept(root, s, "tools", (ExternalTools v) => s.Tools = v, () => new ExternalTools());
            Section(root, "portNames", e =>
            {
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in e.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) names[p.Name] = p.Value.GetString()!;
                s.PortNames = names;
            });
            ReadProjects(root, s);
            Section(root, "activeProject", e => s.ActiveProject = e.GetString());
            return Normalize(s);
        }
        catch (Exception)
        {
            // Unreadable, not JSON, or anything unexpected: defaults.
            return new AppSettings();
        }
    }

    /// The members of a JSON object; a key given twice: the last one wins. Values are cloned (the document is disposed).
    static Dictionary<string, JsonElement> Members(JsonElement obj)
    {
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject()) members[p.Name] = p.Value.Clone();
        return members;
    }

    /// A plain section: one that does not parse keeps its defaults.
    static void Section(Dictionary<string, JsonElement> obj, string key, Action<JsonElement> read)
    {
        if (!obj.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
        try
        {
            read(e);
        }
        catch (Exception)
        {
            // This section keeps its defaults.
        }
    }

    /// An object section: one this version does not understand is kept raw and written back as it was.
    static void Kept<T>(Dictionary<string, JsonElement> obj, SettingsProfile owner, string key, Action<T> set, Func<T> fresh) where T : class
    {
        if (!obj.TryGetValue(key, out var e) || e.ValueKind == JsonValueKind.Null) return;
        try { set(e.Deserialize<T>(Json) ?? fresh()); }
        catch (Exception)
        {
            (owner.RawSections ??= [])[key] = e;
            System.Diagnostics.Trace.WriteLine($"settings: section \"{key}\" kept as-is (could not be read)");
        }
    }

    /// The profile keys of `obj` into `p`; any key that is neither a profile key nor in `alsoKnown` goes to p.Extra.
    static void ReadProfile(Dictionary<string, JsonElement> obj, SettingsProfile p, IReadOnlyCollection<string> alsoKnown)
    {
        Section(obj, "destRoots", e => p.DestRoots = e.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList());
        Section(obj, "verify", e => p.Verify = e.GetBoolean());
        Section(obj, "analyzeClips", e => p.AnalyzeClips = e.GetBoolean());
        Section(obj, "hash", e => p.Hash = e.GetString() is { Length: > 0 } h ? h : "xxh64");
        Kept(obj, p, "proxy", (ProxySettings v) => p.Proxy = v, () => new ProxySettings());
        Kept(obj, p, "naming", (NamingSettings v) => p.Naming = v, () => new NamingSettings());
        Kept(obj, p, "outputs", (OutputSettings v) => p.Outputs = v, () => new OutputSettings());
        Kept(obj, p, "notify", (NotifySettings v) => p.Notify = v, () => new NotifySettings());
        foreach (var (key, value) in obj)
            if (!ProfileKeys.Contains(key) && !alsoKnown.Contains(key)) (p.Extra ??= [])[key] = value;
    }

    /// `projects` (spec 4.1): an entry that is no object or has no valid name is kept raw (M3: written back after the readable ones); a name given twice
    /// (a hand-edited file) — the last entry wins. A `projects` that is no array is kept raw until a project is saved.
    static void ReadProjects(Dictionary<string, JsonElement> root, AppSettings s)
    {
        if (!root.TryGetValue("projects", out var e) || e.ValueKind == JsonValueKind.Null) return;
        if (e.ValueKind != JsonValueKind.Array)
        {
            (s.RawSections ??= [])["projects"] = e;
            System.Diagnostics.Trace.WriteLine("settings: \"projects\" is not a list — kept as-is");
            return;
        }
        foreach (var item in e.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { s.UnreadableProjects.Add(item.Clone()); continue; }
            var obj = Members(item);
            var name = obj.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            if (Projects.NameProblem(name, []) != null)
            {
                System.Diagnostics.Trace.WriteLine($"settings: project \"{name}\" kept as-is (invalid name)");
                s.UnreadableProjects.Add(item.Clone());
                continue;
            }
            var p = new ProjectSettings { Name = name };
            ReadProfile(obj, p, ["name"]);
            s.Projects.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            s.Projects.Add(p);
        }
    }

    internal static AppSettings Normalize(AppSettings s) => Normalize(s, LutCatalog.Default);

    internal static AppSettings Normalize(AppSettings s, LutCatalog catalog)
    {
        s.Tools ??= new ExternalTools();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in s.PortNames ?? [])
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) names[id] = name.Trim();
        s.PortNames = names;
        s.Projects = (s.Projects ?? []).Where(p => p != null).ToList();
        NormalizeProfile(s, catalog);
        foreach (var p in s.Projects) NormalizeProfile(p, catalog);
        // An active project that is not there (deleted by hand, renamed): "Bez projektu"; otherwise its exact name.
        s.ActiveProject = Projects.Find(s, s.ActiveProject)?.Name;
        return s;
    }

    static void NormalizeProfile(SettingsProfile s, LutCatalog catalog)
    {
        s.DestRoots = (s.DestRoots ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        s.Proxy ??= new ProxySettings();
        s.Naming ??= new NamingSettings();
        s.Outputs ??= new OutputSettings();
        s.Notify ??= new NotifySettings();
        s.Notify.Ntfy ??= new NtfySettings();
        if (string.IsNullOrWhiteSpace(s.Notify.Ntfy.Server)) s.Notify.Ntfy.Server = NtfySettings.DefaultServer;
        s.Notify.Ntfy.Topic = (s.Notify.Ntfy.Topic ?? "").Trim();
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
    }

    /// Written to a temporary file next to it, then moved over — a crash never leaves a half-written file.
    public static void Save(AppSettings settings, string path)
    {
        NativeIO.Mkdirs(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, Json);
        bool raw = settings.RawSections is { Count: > 0 } || settings.UnreadableProjects.Count > 0
            || settings.Projects.Any(p => p.RawSections is { Count: > 0 });
        if (raw && JsonNode.Parse(bytes) is JsonObject obj)
        {
            // A `projects` kept raw (it was no list) is written back only while no project was saved in the app.
            MergeRaw(obj, settings.RawSections, except: settings.Projects.Count > 0 ? "projects" : null);
            if (obj["projects"] is JsonArray list)
                for (int i = 0; i < list.Count && i < settings.Projects.Count; i++)
                    if (list[i] is JsonObject project) MergeRaw(project, settings.Projects[i].RawSections, except: null);
            // Entries of `projects` that could not be read (M3) follow the readable ones.
            if (settings.UnreadableProjects.Count > 0 && obj["projects"] is JsonArray all)
                foreach (var unreadable in settings.UnreadableProjects) all.Add(JsonNode.Parse(unreadable.GetRawText()));
            bytes = JsonSerializer.SerializeToUtf8Bytes(obj, Json);
        }
        NativeIO.WriteFileDurably(tmp, bytes);
        NativeIO.Move(tmp, path, replace: true);
    }

    static void MergeRaw(JsonObject obj, Dictionary<string, JsonElement>? raw, string? except)
    {
        foreach (var (key, value) in raw ?? [])
            if (key != except) obj[key] = JsonNode.Parse(value.GetRawText());
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
