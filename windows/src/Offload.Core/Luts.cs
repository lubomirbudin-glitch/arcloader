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
