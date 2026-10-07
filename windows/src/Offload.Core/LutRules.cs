using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Offload.Core;

/// One own LUT rule (spec 4.2, `proxy.lutRules`): "this camera / serial number / reel / folder → this LUT". Exactly one of
/// `lut` (a built-in id or "none") and `lutPath` (an own .cube).
public sealed class LutRule
{
    public const string Camera = "camera", Serial = "serial", Reel = "reel", Folder = "folder";
    public static readonly IReadOnlyList<string> Kinds = [Camera, Serial, Reel, Folder];

    [JsonPropertyName("match")] public string Match { get; set; } = Camera;
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("lut"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Lut { get; set; }
    [JsonPropertyName("lutPath"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LutPath { get; set; }

    /// Keys of a rule a newer version wrote: kept (R24).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// Ruling I3: a rule this version cannot use (a match it does not know, or an item of the wrong JSON type) is kept
    /// exactly as it was read and written back as it was; matching skips it, the editor shows it read-only.
    [JsonIgnore] public JsonElement? Raw { get; set; }
}

/// Reads and writes `lutRules` item by item (M9): one item of the wrong type never fails the whole `proxy` section, and an
/// item this version does not know is kept raw (I3). A `null` item is dropped.
public sealed class LutRuleListConverter : JsonConverter<List<LutRule>>
{
    public override List<LutRule>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("lutRules is not a list");
        var list = new List<LutRule>();
        foreach (var item in JsonElement.ParseValue(ref reader).EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null) continue;
            LutRule? rule = null;
            if (item.ValueKind == JsonValueKind.Object)
            {
                try { rule = item.Deserialize<LutRule>(options); }
                catch (JsonException) { }
            }
            if (rule != null && LutRule.Kinds.Contains(rule.Match)) { list.Add(rule); continue; }
            // Unknown match, wrong types inside, or not an object at all: kept as it was.
            rule ??= new LutRule
            {
                Match = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()! : "?",
            };
            rule.Raw = item.Clone();
            list.Add(rule);
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<LutRule> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var rule in value)
        {
            if (rule.Raw is { } raw) raw.WriteTo(writer);
            else JsonSerializer.Serialize(writer, rule, options);
        }
        writer.WriteEndArray();
    }
}

/// Own LUT rules (spec 4.2, R18): tried top to bottom, only in "Automaticky podle kamery", before the camera is
/// recognised. The first rule that matches decides — also "none".
public static class LutRules
{
    /// A rule this version can use: a known match, a value, exactly one of lut / lutPath.
    public static bool IsValid(LutRule? r) =>
        r != null && r.Raw == null && LutRule.Kinds.Contains(r.Match) && !string.IsNullOrWhiteSpace(r.Value)
        && string.IsNullOrWhiteSpace(r.Lut) != string.IsNullOrWhiteSpace(r.LutPath);

    /// A rule of a newer version (a match this version does not know) or a malformed item: not used, kept (ruling I3).
    public static bool IsFuture(LutRule? r) => r != null && (r.Raw != null || !LutRule.Kinds.Contains(r.Match));

    /// The rules to keep in their order. Spec 4.2: a rule this version knows but that is invalid (no value, both or neither
    /// LUT) is dropped when the settings are read. Ruling I3: a rule it does not know stays untouched, in its place — so
    /// numbering stays the same and a save writes it back unchanged. Values of usable rules are trimmed.
    public static List<LutRule> Valid(IEnumerable<LutRule?>? rules) =>
        (rules ?? []).Where(r => r != null && (IsFuture(r) || IsValid(r))).Select(r => IsFuture(r) ? r! : new LutRule
        {
            Match = r!.Match,
            Value = r.Value.Trim(),
            Lut = string.IsNullOrWhiteSpace(r.Lut) ? null : r.Lut.Trim(),
            LutPath = string.IsNullOrWhiteSpace(r.LutPath) ? null : r.LutPath.Trim(),
            Extra = r.Extra,
        }).ToList();

    /// The first rule that matches the clip, with its number (1 = the top one), or null. `reel`: ReelResolver's answer;
    /// `cardName`: the card folder (the start of the folder pattern). Rules this version cannot use are skipped but count.
    public static (int Number, LutRule Rule)? Match(IReadOnlyList<LutRule> rules, Clip clip, ClipInfo? info, string? reel, string cardName)
    {
        for (int i = 0; i < rules.Count; i++)
            if (IsValid(rules[i]) && Matches(rules[i], clip, info, reel, cardName)) return (i + 1, rules[i]);
        return null;
    }

    static bool Matches(LutRule r, Clip clip, ClipInfo? info, string? reel, string cardName)
    {
        var value = r.Value.Trim();
        return r.Match switch
        {
            LutRule.Camera => info?.Camera is string camera && camera.Contains(value, StringComparison.OrdinalIgnoreCase),
            LutRule.Serial => info?.CameraSerial?.Trim() is { Length: > 0 } serial && string.Equals(serial, value, StringComparison.OrdinalIgnoreCase),
            LutRule.Reel => reel?.Trim() is { Length: > 0 } rl && rl.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            LutRule.Folder => FolderMatches(value, FolderOf(clip, cardName)),
            _ => false,
        };
    }

    /// M10: a pattern that runs into the regex timeout counts as no match — never an exception out of the resolver.
    static bool FolderMatches(string pattern, string folder)
    {
        try { return Glob(pattern).IsMatch(folder); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// `<card>/<the clip's folder>` with "/" ("A001/CLIP"); a clip at the card's top is just "A001".
    public static string FolderOf(Clip clip, string cardName)
    {
        var path = clip.Path.Replace('\\', '/');
        int slash = path.LastIndexOf('/');
        return slash > 0 ? $"{cardName}/{path[..slash]}" : cardName;
    }

    /// `*` = any characters (also "/"), `?` = one character, regardless of case; the whole text must match.
    static Regex Glob(string pattern) =>
        new("^" + Regex.Escape(pattern.Replace('\\', '/')).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// The first rule the editor cannot save, in Czech ("Pravidlo 2: vyplňte hodnotu."), or null. A rule of a newer
    /// version (read-only in the editor, ruling I3) is not looked at, but it keeps its number.
    public static string? Problem(IReadOnlyList<LutRule> rules)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (IsFuture(r)) continue;
            if (string.IsNullOrWhiteSpace(r.Value)) return $"Pravidlo {i + 1}: vyplňte hodnotu.";
            if (string.IsNullOrWhiteSpace(r.Lut) == string.IsNullOrWhiteSpace(r.LutPath)) return $"Pravidlo {i + 1}: vyberte LUT.";
        }
        return null;
    }

    /// CLI `--lut-rule <match>=<value>:<lut>` (spec 4.2): `lut` is a built-in id, "none" or a path to a .cube (anything
    /// with ".cube" or a path separator — returned as written). The value ends at the first ":" after "=", so it cannot
    /// contain one.
    public static (LutRule? Rule, string? Error) ParseOption(string spec, LutCatalog cat)
    {
        int eq = spec.IndexOf('=');
        int colon = eq < 0 ? -1 : spec.IndexOf(':', eq + 1);
        if (eq <= 0 || colon < 0) return (null, Msg.BadLutRule(spec));
        var match = spec[..eq].Trim().ToLowerInvariant();
        var value = spec[(eq + 1)..colon].Trim();
        var lut = spec[(colon + 1)..].Trim();
        if (!LutRule.Kinds.Contains(match) || value.Length == 0 || lut.Length == 0) return (null, Msg.BadLutRule(spec));
        if (lut.Equals("none", StringComparison.OrdinalIgnoreCase)) return (new LutRule { Match = match, Value = value, Lut = "none" }, null);
        if (lut.EndsWith(".cube", StringComparison.OrdinalIgnoreCase) || lut.Contains('\\') || lut.Contains('/'))
            return (new LutRule { Match = match, Value = value, LutPath = lut }, null);
        return cat.Find(lut) != null
            ? (new LutRule { Match = match, Value = value, Lut = lut }, null)
            : (null, Msg.UnknownLut(lut, string.Join(", ", cat.Luts.Select(l => l.Id))));
    }

    /// How the report gives the reason: "pravidlo 2: kamera obsahuje „FX6“".
    public static string Describe(int number, LutRule r) => $"pravidlo {number}: " + r.Match switch
    {
        LutRule.Camera => $"kamera obsahuje „{r.Value.Trim()}“",
        LutRule.Serial => $"sériové číslo kamery „{r.Value.Trim()}“",
        LutRule.Reel => $"cívka začíná „{r.Value.Trim()}“",
        _ => $"složka odpovídá „{r.Value.Trim()}“",
    };

    /// The LUT of a matching rule: an own .cube (source "rule"), a built-in one (source "rule"), or none — "none" keeps
    /// the source "none" so a clip without a LUT counts as one everywhere (day report, clip lists).
    public static LutUse Use(int number, LutRule r, LutCatalog cat)
    {
        var reason = Describe(number, r);
        if (r.LutPath is string path) return new LutUse("rule", null, Path.GetFileName(path), reason) { File = path };
        if (string.Equals(r.Lut, "none", StringComparison.OrdinalIgnoreCase)) return LutUse.None(reason);
        return cat.Find(r.Lut) is { } lut
            ? new LutUse("rule", lut.Id, lut.Name, reason) { File = lut.Path }
            : LutUse.None($"{reason} — vestavěný LUT nenalezen");
    }
}
