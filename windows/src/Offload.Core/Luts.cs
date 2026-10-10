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

    /// The user's own file from %APPDATA%\Offload\luts replaced the built-in one (LutCatalog.WithUserOverrides).
    [JsonIgnore] public bool UserFile { get; init; }
}

/// The built-in LUTs shipped next to the app (Offload\luts) or, in a development build, shared\luts in the repo.
public sealed class LutCatalog(string? folder, IReadOnlyList<BuiltinLut> luts)
{
    public string? Folder { get; } = folder;
    public IReadOnlyList<BuiltinLut> Luts { get; } = luts;

    public BuiltinLut? Find(string? id) => id == null ? null : Luts.FirstOrDefault(l => l.Id == id);

    static readonly Lazy<LutCatalog> DefaultCatalog = new(LoadDefault);

    /// The app's and CLI's catalog: the built-in LUTs, each replaced by the user's own file where there is one.
    public static LutCatalog Default => DefaultCatalog.Value;

    /// Builds what Default holds (Default itself is loaded once, on first use).
    public static LutCatalog LoadDefault() => Load(DefaultFolder).WithUserOverrides(UserFolder);

    /// %APPDATA%\Offload\luts (or OFFLOAD_SETTINGS_DIR\luts): `<id>.cube` there replaces the built-in LUT with that id
    /// — like ~/Library/Application Support/Offload/luts on the Mac.
    public static string UserFolder => System.IO.Path.Combine(Desktop.AppData.Folder, "luts");

    public const string UserFileSuffix = " (vlastní soubor)";

    /// The user's own file for a camera log wins over the built-in approximation — e.g. ARRI's official Rec.709 LUT saved
    /// as %APPDATA%\Offload\luts\arri-logc4-awg4.cube (vendor LUTs may not be shipped with the app, but anyone may use
    /// them on their own computer). Only valid 3D .cube files count; other names in the folder are ignored. Never throws.
    public LutCatalog WithUserOverrides(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return this;
        try
        {
            if (!Directory.Exists(folder)) return this;
            return new(Folder, Luts.Select(l =>
            {
                var own = System.IO.Path.Combine(folder, l.Id + ".cube");
                return System.IO.File.Exists(own) && IsValidCube(own) ? l with { Path = own, Name = l.Name + UserFileSuffix, UserFile = true } : l;
            }).ToList());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return this;
        }
    }

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

    /// The camera's serial number from the same sidecar (`<Device … serialNo="…"/>`), or null.
    public static string? Serial(string clipFullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(clipFullPath);
            if (dir == null) return null;
            var xml = Path.Combine(dir, Path.GetFileNameWithoutExtension(clipFullPath) + "M01.XML");
            if (!System.IO.File.Exists(xml)) return null;
            var device = System.Xml.Linq.XDocument.Load(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "Device");
            return device?.Attribute("serialNo")?.Value.Trim() is { Length: > 0 } serial ? serial : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// The rate the camera recorded at (task 2026-10-10), from the same sidecar: `<VideoFrame captureFps="119.88p"
    /// formatFps="23.98p"/>` (S&Q: captureFps is the capture rate, formatFps the file's) → "120000/1001", written like
    /// ClipInfo.FrameRate. "59.94i" counts frames, not fields (→ 29.97). Null without the XML or the attribute.
    public static string? CaptureFrameRate(string clipFullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(clipFullPath);
            if (dir == null) return null;
            var xml = Path.Combine(dir, Path.GetFileNameWithoutExtension(clipFullPath) + "M01.XML");
            if (!System.IO.File.Exists(xml)) return null;
            var frame = System.Xml.Linq.XDocument.Load(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "VideoFrame");
            if (frame?.Attribute("captureFps")?.Value.Trim().ToLowerInvariant() is not { Length: > 0 } text) return null;
            bool interlaced = text.EndsWith('i');
            if (!double.TryParse(text.TrimEnd('p', 'i'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps)
                || fps <= 0)
                return null;
            return RawBackends.Rational(interlaced ? fps / 2 : fps);
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

    /// `cardName`: the card folder - own rules match folders below it and fall back to it as the reel (spec 4.2).
    public static LutUse Resolve(Clip clip, ClipInfo? info, string clipFullPath, ProxySettings s, LutCatalog cat, string cardName = "")
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
        // R18: own rules first, only here in Auto mode.
        if (s.LutRules.Count > 0 && LutRules.Match(s.LutRules, clip, info, info?.Reel ?? ReelResolver.Resolve(clip, info, cardName), cardName) is { } rule)
            return LutRules.Use(rule.Number, rule.Rule, cat);
        if (cat.Luts.Count == 0) return LutUse.None("vestavěné LUTy chybí");
        var (id, reason) = Detect(clip, info, clipFullPath, s.BrawGen4ToGen5);
        if (id == "") return LutUse.None(reason);                     // recognised, but not log footage
        if (id != null) return Builtin(cat, id, "auto", reason) ?? LutUse.None("vestavěný LUT nenalezen");
        return Builtin(cat, s.LutFallback, "fallback", "kamera nerozpoznána — záložní LUT") ?? LutUse.None("kamera nerozpoznána");
    }

    static LutUse? Builtin(LutCatalog cat, string? id, string source, string reason) =>
        cat.Find(id) is { } lut ? new LutUse(source, lut.Id, lut.Name, WithUserFile(reason, lut)) { File = lut.Path } : null;

    /// The report says when the user's own file replaced the built-in LUT, and which one.
    internal static string WithUserFile(string reason, BuiltinLut lut) =>
        lut.UserFile ? $"{reason}; soubor {lut.Path}" : reason;

    /// BRAW decodes in the gamma the clip was shot in: only "Blackmagic Design Film" Gen 5 matches the built-in LUT.
    /// Video / Extended Video / Custom / HDR gammas are display-ready already; an older decoder without the gamma
    /// in --info keeps the old assumption (Film Gen 5).
    /// `gen5Wanted` (proxy.brawGen4ToGen5): a Gen 4 clip that comes back unconverted says so (M11 a).
    static (string? Id, string Reason) Braw(ClipInfo? info, bool gen5Wanted)
    {
        var gamma = info?.Gamma;
        if (string.IsNullOrEmpty(gamma)) return ("bmd-film-gen5", "Blackmagic RAW (gama neuvedena — předpoklad Film Gen 5)");
        var label = info!.ColorScienceGen is int gen ? $"{gamma} Gen {gen}" : gamma;
        if (!gamma.Equals("Blackmagic Design Film", StringComparison.OrdinalIgnoreCase))
            return ("", $"Blackmagic RAW: {label} (není log)");
        // Decoded by braw-decode --gen5 (spec 4.2): recorded in an older generation, now Gen 5 — the Gen 5 LUT fits.
        if (info.ColorScienceGenOriginal is int original && original < 5 && info.ColorScienceGen is int effective && effective >= 5)
            return ("bmd-film-gen5", $"Blackmagic RAW: Film Gen {original} → Gen {effective} (převod SDK)");
        if (info.ColorScienceGen is int g && g < 5)
            return ("", $"Blackmagic RAW: {label} (bez vestavěného LUTu){(gen5Wanted ? " — převod na Gen 5 se nepodařil" : "")}");
        return ("bmd-film-gen5", $"Blackmagic RAW: {label}");
    }

    /// (id, reason): id null = not recognised, "" = recognised as non-log (no LUT).
    internal static (string? Id, string Reason) Detect(Clip clip, ClipInfo? info, string clipFullPath, bool brawGen5 = false)
    {
        switch (clip.Format)
        {
            case ClipFormat.Braw: return Braw(info, brawGen5);
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
            // S-Log2 / S-Log is log too, there is just no built-in LUT for it; the rest (rec709, hlg, s-cinetone) is not log.
            if (g.StartsWith("s-log", StringComparison.Ordinal)) return ("", $"Sony XML: {gamma} (bez vestavěného LUTu)");
            if (g.Length > 0) return ("", $"Sony XML: {gamma} (není log)");
        }
        var camera = (info?.Camera ?? "").ToUpperInvariant();
        if (camera.Contains("ALEXA") || camera.Contains("AMIRA"))
            return ArriBackend.IsAlexa35(camera) ? ("arri-logc4-awg4", "ARRI kamera (předpoklad LogC4)") : ("arri-logc3-awg3", "ARRI kamera (předpoklad LogC3)");
        return (null, "kamera nerozpoznána");
    }
}

/// `--lut` / `--lut-fallback` of the CLI: auto, none, a built-in id, or a path to a .cube (anything with ".cube" or a path separator).
public static class LutOption
{
    public static (LutMode Mode, string? Builtin, string? Path, string? Fallback, string? Error) Parse(string? value, string? fallback, LutCatalog cat)
    {
        string? fb = null;
        if (fallback != null && !fallback.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (cat.Find(fallback) == null) return (LutMode.Auto, null, null, null, Msg.UnknownFallbackLut(fallback, Ids(cat)));
            fb = fallback;
        }
        if (value == null || value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return (LutMode.Auto, null, null, fb, null);
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return (LutMode.None, null, null, fb, null);
        if (value.EndsWith(".cube", StringComparison.OrdinalIgnoreCase) || value.Contains('\\') || value.Contains('/'))
            return (LutMode.Custom, null, value, fb, null);
        return cat.Find(value) != null
            ? (LutMode.Builtin, value, null, fb, null)
            : (LutMode.Auto, null, null, fb, Msg.UnknownLut(value, Ids(cat)));
    }

    static string Ids(LutCatalog cat) => string.Join(", ", cat.Luts.Select(l => l.Id));

    /// The "LUT: ..." line of the CLI plan, or null when neither proxies nor clip analysis (thumbnails) use a LUT.
    public static string? PlanLine(ProxySettings p, bool analyzeClips) => !p.Enabled && !analyzeClips ? null : "LUT: " + p.LutMode switch
    {
        LutMode.None => "none",
        LutMode.Custom => p.LutPath,
        LutMode.Builtin => p.LutBuiltin,
        _ => "auto" + (p.LutFallback != null ? $" (fallback {p.LutFallback})" : "") + (p.LutRules.Count > 0 ? $" + {p.LutRules.Count} own rule(s)" : ""),
    };
}
