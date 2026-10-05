using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Offload.Core;

/// One built-in LUT from shared/luts/luts.json.
public sealed record BuiltinLut(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("maker")] string Maker,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("curve")] string Curve,
    [property: JsonPropertyName("gamut")] string Gamut)
{
    [JsonIgnore] public string Path { get; init; } = "";
}

/// The built-in LUTs shipped next to the app (Offload\luts) or, in a development build, shared\luts in the repo.
public sealed class LutCatalog(string? folder, IReadOnlyList<BuiltinLut> luts)
{
    public string? Folder { get; } = folder;
    public IReadOnlyList<BuiltinLut> Luts { get; } = luts;

    public BuiltinLut? Find(string? id) => id == null ? null : Luts.FirstOrDefault(l => l.Id == id);

    static readonly Lazy<LutCatalog> DefaultCatalog = new(() => Load(DefaultFolder));
    public static LutCatalog Default => DefaultCatalog.Value;

    public static string? DefaultFolder
    {
        get
        {
            var exeDir = AppContext.BaseDirectory.TrimEnd('\\');
            foreach (var candidate in new[] { System.IO.Path.Combine(exeDir, "luts"), System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exeDir) ?? exeDir, "luts") })
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "luts.json"))) return candidate;
            if (!ExternalTools.IsDevBuild(AppContext.BaseDirectory)) return null;
            var d = exeDir;
            for (int i = 0; i < 8 && System.IO.Path.GetDirectoryName(d) is string parent; i++)
            {
                d = parent;
                var repo = System.IO.Path.Combine(d, "shared", "luts");
                if (System.IO.File.Exists(System.IO.Path.Combine(repo, "luts.json"))) return repo;
            }
            return null;
        }
    }

    /// Never throws: a missing or broken folder gives an empty catalog; entries whose file is missing are left out.
    public static LutCatalog Load(string? folder)
    {
        try
        {
            if (folder == null) return new(null, []);
            var json = System.IO.Path.Combine(folder, "luts.json");
            if (!System.IO.File.Exists(json)) return new(null, []);
            var list = JsonSerializer.Deserialize<List<BuiltinLut>>(System.IO.File.ReadAllText(json)) ?? [];
            var ok = list.Where(l => !string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.File))
                .Select(l => l with { Path = System.IO.Path.Combine(folder, l.File) })
                .Where(l => System.IO.File.Exists(l.Path)).ToList();
            return new(ok.Count > 0 ? folder : null, ok);
        }
        catch (Exception)
        {
            return new(null, []);
        }
    }

    public static bool IsValidCube(string path)
    {
        try
        {
            int size = 0, rows = 0;
            foreach (var raw in System.IO.File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("LUT_3D_SIZE", StringComparison.Ordinal))
                    size = int.Parse(line["LUT_3D_SIZE".Length..].Trim(), CultureInfo.InvariantCulture);
                else if (line.Length > 0 && (char.IsDigit(line[0]) || line[0] is '-' or '.')) rows++;
            }
            return size >= 2 && rows == size * size * size;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<LutMode>))]
public enum LutMode
{
    [JsonStringEnumMemberName("auto")] Auto,
    [JsonStringEnumMemberName("builtin")] Builtin,
    [JsonStringEnumMemberName("custom")] Custom,
    [JsonStringEnumMemberName("none")] None,
}

/// The LUT a clip got, and why — goes into the report (key "lut"). File is the .cube to apply (never serialised).
public sealed record LutUse(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("reason")] string Reason)
{
    [JsonIgnore] public string? File { get; init; }
    [JsonIgnore] public bool IsBuiltin => Id != null;

    public static LutUse None(string reason) => new("none", null, "bez LUT", reason);
}

/// Sony XAVC sidecar "<clip>M01.XML" (NonRealTimeMeta): the gamma and primaries the camera recorded in.
public static class SonyXml
{
    public static (string? Gamma, string? Primaries)? Read(string clipFullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(clipFullPath);
            if (dir == null) return null;
            var xml = Path.Combine(dir, Path.GetFileNameWithoutExtension(clipFullPath) + "M01.XML");
            if (!System.IO.File.Exists(xml)) return null;
            var doc = System.Xml.Linq.XDocument.Load(xml);
            string? Item(string name) => doc.Descendants()
                .FirstOrDefault(e => (string?)e.Attribute("name") == name)?.Attribute("value")?.Value;
            return (Item("CaptureGammaEquation"), Item("CaptureColorPrimaries"));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// Which LUT a clip gets (spec: luts design §3). Pure rules over metadata; never throws.
public static class LutResolver
{
    public static LutMode EffectiveMode(ProxySettings s) => s.LutMode ?? (!string.IsNullOrEmpty(s.LutPath) ? LutMode.Custom : LutMode.Auto);

    public static LutUse Resolve(Clip clip, ClipInfo? info, string clipFullPath, ProxySettings s, LutCatalog cat)
    {
        switch (EffectiveMode(s))
        {
            case LutMode.None:
                return LutUse.None("vypnuto v nastavení");
            case LutMode.Custom:
                return !string.IsNullOrEmpty(s.LutPath) && s.LutPath is string path
                    ? new LutUse("custom", null, System.IO.Path.GetFileName(path), "vlastní LUT") { File = path }
                    : LutUse.None("vlastní LUT není vybraný");
            case LutMode.Builtin:
                return Builtin(cat, s.LutBuiltin, "builtin", "vybraný v nastavení") ?? LutUse.None("vestavěný LUT nenalezen");
        }
        if (cat.Luts.Count == 0) return LutUse.None("vestavěné LUTy chybí");
        var (id, reason) = Detect(clip, info, clipFullPath);
        if (id == "") return LutUse.None(reason);                     // recognised, but not log footage
        if (id != null) return Builtin(cat, id, "auto", reason) ?? LutUse.None("vestavěný LUT nenalezen");
        return Builtin(cat, s.LutFallback, "fallback", "kamera nerozpoznána — záložní LUT") ?? LutUse.None("kamera nerozpoznána");
    }

    static LutUse? Builtin(LutCatalog cat, string? id, string source, string reason) =>
        cat.Find(id) is { } lut ? new LutUse(source, lut.Id, lut.Name, reason) { File = lut.Path } : null;

    /// (id, reason): id null = not recognised, "" = recognised as non-log (no LUT).
    internal static (string? Id, string Reason) Detect(Clip clip, ClipInfo? info, string clipFullPath)
    {
        switch (clip.Format)
        {
            case ClipFormat.Braw: return ("bmd-film-gen5", "Blackmagic RAW (gama Film Gen 5)");
            case ClipFormat.R3d: return ("red-log3g10-rwg", "RED R3D (IPP2 Log3G10)");
            case ClipFormat.Arriraw:
                return ArriBackend.ColorSpace(null, info ?? new ClipInfo()).Contains("LogC4")
                    ? ("arri-logc4-awg4", "ARRIRAW (LogC4)") : ("arri-logc3-awg3", "ARRIRAW (LogC3)");
        }
        if (SonyXml.Read(clipFullPath) is var (gamma, primaries))
        {
            var g = (gamma ?? "").ToLowerInvariant();
            var p = (primaries ?? "").ToLowerInvariant();
            if (g.StartsWith("s-log3", StringComparison.Ordinal))
            {
                bool cine = p.Contains("cine") || (p.Length == 0 && g.EndsWith("cine", StringComparison.Ordinal));
                return (cine ? "sony-slog3-sgamut3cine" : "sony-slog3-sgamut3", $"Sony XML: {gamma} / {primaries}".TrimEnd(' ', '/'));
            }
            if (g.Length > 0) return ("", $"Sony XML: {gamma} (není log)");
        }
        var camera = (info?.Camera ?? "").ToUpperInvariant();
        if (camera.Contains("ALEXA") || camera.Contains("AMIRA"))
            return ArriBackend.IsAlexa35(camera) ? ("arri-logc4-awg4", "ARRI kamera (předpoklad LogC4)") : ("arri-logc3-awg3", "ARRI kamera (předpoklad LogC3)");
        return (null, "kamera nerozpoznána");
    }
}
